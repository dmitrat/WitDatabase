using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Translates string method calls to WitSQL functions.
/// </summary>
public sealed class WitStringMethodTranslator : IMethodCallTranslator
{
    #region Constants

    private static readonly MethodInfo STRING_TOUPPER_METHOD = typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Type.EmptyTypes)!;
    private static readonly MethodInfo STRING_TOLOWER_METHOD = typeof(string).GetRuntimeMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo STRING_TRIM_METHOD = typeof(string).GetRuntimeMethod(nameof(string.Trim), Type.EmptyTypes)!;
    private static readonly MethodInfo STRING_TRIMSTART_METHOD = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), Type.EmptyTypes)!;
    private static readonly MethodInfo STRING_TRIMEND_METHOD = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), Type.EmptyTypes)!;
    private static readonly MethodInfo STRING_CONTAINS_METHOD = typeof(string).GetRuntimeMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo STRING_STARTSWITH_METHOD = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo STRING_ENDSWITH_METHOD = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), [typeof(string)])!;
    private static readonly MethodInfo STRING_INDEXOF_METHOD = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(string)])!;
    private static readonly MethodInfo STRING_REPLACE_METHOD = typeof(string).GetRuntimeMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;
    private static readonly MethodInfo STRING_SUBSTRING_METHOD = typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;
    private static readonly MethodInfo STRING_SUBSTRING_START_METHOD = typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int)])!;
    private static readonly MethodInfo STRING_ISNULLOREMPTY_METHOD = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrEmpty), [typeof(string)])!;
    private static readonly MethodInfo STRING_ISNULLORWHITESPACE_METHOD = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!;
    private static readonly MethodInfo STRING_CONCAT_2_METHOD = typeof(string).GetRuntimeMethod(nameof(string.Concat), [typeof(string), typeof(string)])!;
    private static readonly MethodInfo STRING_CONCAT_3_METHOD = typeof(string).GetRuntimeMethod(nameof(string.Concat), [typeof(string), typeof(string), typeof(string)])!;

    #endregion

    #region Fields

    private readonly ISqlExpressionFactory m_sqlExpressionFactory;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitStringMethodTranslator"/> class.
    /// </summary>
    /// <param name="sqlExpressionFactory">The SQL expression factory.</param>
    public WitStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
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
        if (method == STRING_TOUPPER_METHOD && instance != null)
        {
            return m_sqlExpressionFactory.Function(
                "UPPER",
                [instance],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string));
        }

        if (method == STRING_TOLOWER_METHOD && instance != null)
        {
            return m_sqlExpressionFactory.Function(
                "LOWER",
                [instance],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string));
        }

        if (method == STRING_TRIM_METHOD && instance != null)
        {
            return m_sqlExpressionFactory.Function(
                "TRIM",
                [instance],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string));
        }

        if (method == STRING_TRIMSTART_METHOD && instance != null)
        {
            return m_sqlExpressionFactory.Function(
                "LTRIM",
                [instance],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string));
        }

        if (method == STRING_TRIMEND_METHOD && instance != null)
        {
            return m_sqlExpressionFactory.Function(
                "RTRIM",
                [instance],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string));
        }

        if (method == STRING_CONTAINS_METHOD && instance != null)
        {
            // INSTR(instance, argument) > 0
            var instrCall = m_sqlExpressionFactory.Function(
                "INSTR",
                [instance, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(int));

            return m_sqlExpressionFactory.GreaterThan(
                instrCall,
                m_sqlExpressionFactory.Constant(0));
        }

        if (method == STRING_STARTSWITH_METHOD && instance != null)
        {
            // instance LIKE argument || '%'
            var pattern = m_sqlExpressionFactory.Add(
                arguments[0],
                m_sqlExpressionFactory.Constant("%"));

            return m_sqlExpressionFactory.Like(instance, pattern);
        }

        if (method == STRING_ENDSWITH_METHOD && instance != null)
        {
            // instance LIKE '%' || argument
            var pattern = m_sqlExpressionFactory.Add(
                m_sqlExpressionFactory.Constant("%"),
                arguments[0]);

            return m_sqlExpressionFactory.Like(instance, pattern);
        }

        if (method == STRING_INDEXOF_METHOD && instance != null)
        {
            // INSTR(instance, argument) - 1 (to match 0-based .NET indexing)
            var instrCall = m_sqlExpressionFactory.Function(
                "INSTR",
                [instance, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(int));

            return m_sqlExpressionFactory.Subtract(
                instrCall,
                m_sqlExpressionFactory.Constant(1));
        }

        if (method == STRING_REPLACE_METHOD && instance != null)
        {
            return m_sqlExpressionFactory.Function(
                "REPLACE",
                [instance, arguments[0], arguments[1]],
                nullable: true,
                argumentsPropagateNullability: [true, true, true],
                typeof(string));
        }

        if (method == STRING_SUBSTRING_METHOD && instance != null)
        {
            // SUBSTR(instance, start + 1, length) - SQL uses 1-based indexing
            var startPlusOne = m_sqlExpressionFactory.Add(
                arguments[0],
                m_sqlExpressionFactory.Constant(1));

            return m_sqlExpressionFactory.Function(
                "SUBSTR",
                [instance, startPlusOne, arguments[1]],
                nullable: true,
                argumentsPropagateNullability: [true, true, true],
                typeof(string));
        }

        if (method == STRING_SUBSTRING_START_METHOD && instance != null)
        {
            // SUBSTR(instance, start + 1) - SQL uses 1-based indexing
            var startPlusOne = m_sqlExpressionFactory.Add(
                arguments[0],
                m_sqlExpressionFactory.Constant(1));

            return m_sqlExpressionFactory.Function(
                "SUBSTR",
                [instance, startPlusOne],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(string));
        }

        if (method == STRING_ISNULLOREMPTY_METHOD)
        {
            // argument IS NULL OR argument = ''
            var isNull = m_sqlExpressionFactory.IsNull(arguments[0]);
            var isEmpty = m_sqlExpressionFactory.Equal(
                arguments[0],
                m_sqlExpressionFactory.Constant(""));

            return m_sqlExpressionFactory.OrElse(isNull, isEmpty);
        }

        if (method == STRING_ISNULLORWHITESPACE_METHOD)
        {
            // argument IS NULL OR TRIM(argument) = ''
            var isNull = m_sqlExpressionFactory.IsNull(arguments[0]);
            var trimmed = m_sqlExpressionFactory.Function(
                "TRIM",
                [arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string));
            var isEmpty = m_sqlExpressionFactory.Equal(
                trimmed,
                m_sqlExpressionFactory.Constant(""));

            return m_sqlExpressionFactory.OrElse(isNull, isEmpty);
        }

        if (method == STRING_CONCAT_2_METHOD)
        {
            // argument1 || argument2
            return m_sqlExpressionFactory.Add(arguments[0], arguments[1]);
        }

        if (method == STRING_CONCAT_3_METHOD)
        {
            // argument1 || argument2 || argument3
            var first = m_sqlExpressionFactory.Add(arguments[0], arguments[1]);
            return m_sqlExpressionFactory.Add(first, arguments[2]);
        }

        return null;
    }

    #endregion
}
