using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Translates Guid method calls to WitSQL functions.
/// </summary>
public sealed class WitGuidMethodTranslator : IMethodCallTranslator
{
    #region Constants

    private static readonly MethodInfo GUID_NEWGUID = typeof(Guid).GetRuntimeMethod(nameof(Guid.NewGuid), Type.EmptyTypes)!;

    #endregion

    #region Fields

    private readonly ISqlExpressionFactory m_sqlExpressionFactory;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitGuidMethodTranslator"/> class.
    /// </summary>
    /// <param name="sqlExpressionFactory">The SQL expression factory.</param>
    public WitGuidMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        m_sqlExpressionFactory = sqlExpressionFactory;
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
        if (method == GUID_NEWGUID)
        {
            return m_sqlExpressionFactory.Function(
                "NEWGUID",
                Array.Empty<SqlExpression>(),
                nullable: false,
                argumentsPropagateNullability: Array.Empty<bool>(),
                typeof(Guid));
        }

        return null;
    }

    #endregion
}
