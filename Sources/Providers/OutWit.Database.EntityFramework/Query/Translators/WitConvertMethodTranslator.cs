using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Translates <c>ToString()</c> and <c>Convert.ToString(...)</c> on a primitive into a SQL
/// <c>CAST</c>, so the conversion happens in the database.
/// </summary>
/// <remarks>
/// <para>
/// <c>Docs/KnownIssues.md</c> 3. <b>The engine was never at fault</b> - <c>CAST(x AS VARCHAR)</c>,
/// <c>CAST(x AS TEXT)</c> and <c>CONVERT(VARCHAR, x)</c> all answer correctly when executed
/// directly. Nothing translated the CLR call, so EF had nothing to emit and refused the whole query:
/// <i>"Translation of method 'int.ToString' failed"</i>. The shape it bites is a stored code shown as
/// a name - <c>GroupBy(x =&gt; x.DeviceType)</c> then <c>group.Key.ToString()</c>.
/// </para>
/// <para>
/// <b>Only where it is a conversion.</b> <c>ToString()</c> on a string is the value itself, and one
/// taking a format or a culture is not a cast at all - a <c>CAST</c> would silently ignore the format
/// and answer with something else, which is worse than refusing to translate. Those are left to EF,
/// which evaluates them on the client in a projection and refuses them in a predicate, and that is
/// the honest outcome.
/// </para>
/// </remarks>
public sealed class WitConvertMethodTranslator : IMethodCallTranslator
{
    #region Constants

    /// <summary>
    /// The CLR types whose <c>ToString()</c> is a plain conversion with no formatting behind it.
    /// </summary>
    /// <remarks>
    /// <c>string</c> is deliberately absent - its <c>ToString()</c> is the value - and so are the
    /// temporal types: <c>DateTime.ToString()</c> renders in the CURRENT CULTURE, so casting it in
    /// the database would answer a different string on a machine with another locale. A query whose
    /// result depends on where it ran is the defect this project keeps finding, not a feature.
    /// </remarks>
    private static readonly HashSet<Type> CONVERTIBLE =
    [
        typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort),
        typeof(int), typeof(uint),
        typeof(long), typeof(ulong),
        typeof(decimal), typeof(double), typeof(float),
        typeof(bool), typeof(char), typeof(Guid)
    ];

    #endregion

    #region Fields

    private readonly ISqlExpressionFactory m_sqlExpressionFactory;

    private readonly RelationalTypeMapping m_stringMapping;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitConvertMethodTranslator"/> class.
    /// </summary>
    /// <param name="sqlExpressionFactory">The SQL expression factory.</param>
    /// <param name="typeMappingSource">Where the store type of a string comes from.</param>
    public WitConvertMethodTranslator(ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource)
    {
        m_sqlExpressionFactory = sqlExpressionFactory;
        m_stringMapping = typeMappingSource.FindMapping(typeof(string))!;
    }

    #endregion

    #region IMethodCallTranslator

    /// <inheritdoc/>
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // x.ToString(), and nothing that takes a format or a culture.
        if (method.Name == nameof(ToString) &&
            method.GetParameters().Length == 0 &&
            instance != null &&
            IsConvertible(instance.Type))
        {
            return Cast(instance);
        }

        // Convert.ToString(x) - a different CLR method reaching the same SQL. Its overloads taking a
        // format provider or a base are not conversions of this kind either.
        if (method.DeclaringType == typeof(Convert) &&
            method.Name == nameof(Convert.ToString) &&
            arguments.Count == 1 &&
            IsConvertible(arguments[0].Type))
        {
            return Cast(arguments[0]);
        }

        return null;
    }

    #endregion

    #region Tools

    private SqlExpression Cast(SqlExpression operand) =>
        m_sqlExpressionFactory.Convert(operand, typeof(string), m_stringMapping);

    private static bool IsConvertible(Type type) =>
        CONVERTIBLE.Contains(Nullable.GetUnderlyingType(type) ?? type);

    #endregion
}
