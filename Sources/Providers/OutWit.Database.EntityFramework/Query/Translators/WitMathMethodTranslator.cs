using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Translates Math method calls to WitSQL functions.
/// </summary>
public sealed class WitMathMethodTranslator : IMethodCallTranslator
{
    #region Constants

    private static readonly MethodInfo MATH_ABS_DECIMAL = typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(decimal)])!;
    private static readonly MethodInfo MATH_ABS_DOUBLE = typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(double)])!;
    private static readonly MethodInfo MATH_ABS_FLOAT = typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(float)])!;
    private static readonly MethodInfo MATH_ABS_INT = typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(int)])!;
    private static readonly MethodInfo MATH_ABS_LONG = typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(long)])!;
    private static readonly MethodInfo MATH_ABS_SHORT = typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(short)])!;
    private static readonly MethodInfo MATH_ABS_SBYTE = typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(sbyte)])!;

    private static readonly MethodInfo MATH_CEILING_DECIMAL = typeof(Math).GetRuntimeMethod(nameof(Math.Ceiling), [typeof(decimal)])!;
    private static readonly MethodInfo MATH_CEILING_DOUBLE = typeof(Math).GetRuntimeMethod(nameof(Math.Ceiling), [typeof(double)])!;

    private static readonly MethodInfo MATH_FLOOR_DECIMAL = typeof(Math).GetRuntimeMethod(nameof(Math.Floor), [typeof(decimal)])!;
    private static readonly MethodInfo MATH_FLOOR_DOUBLE = typeof(Math).GetRuntimeMethod(nameof(Math.Floor), [typeof(double)])!;

    private static readonly MethodInfo MATH_ROUND_DECIMAL = typeof(Math).GetRuntimeMethod(nameof(Math.Round), [typeof(decimal)])!;
    private static readonly MethodInfo MATH_ROUND_DOUBLE = typeof(Math).GetRuntimeMethod(nameof(Math.Round), [typeof(double)])!;
    private static readonly MethodInfo MATH_ROUND_DECIMAL_DIGITS = typeof(Math).GetRuntimeMethod(nameof(Math.Round), [typeof(decimal), typeof(int)])!;
    private static readonly MethodInfo MATH_ROUND_DOUBLE_DIGITS = typeof(Math).GetRuntimeMethod(nameof(Math.Round), [typeof(double), typeof(int)])!;

    private static readonly MethodInfo MATH_TRUNCATE_DECIMAL = typeof(Math).GetRuntimeMethod(nameof(Math.Truncate), [typeof(decimal)])!;
    private static readonly MethodInfo MATH_TRUNCATE_DOUBLE = typeof(Math).GetRuntimeMethod(nameof(Math.Truncate), [typeof(double)])!;

    private static readonly MethodInfo MATH_POW = typeof(Math).GetRuntimeMethod(nameof(Math.Pow), [typeof(double), typeof(double)])!;
    private static readonly MethodInfo MATH_SQRT = typeof(Math).GetRuntimeMethod(nameof(Math.Sqrt), [typeof(double)])!;
    private static readonly MethodInfo MATH_LOG = typeof(Math).GetRuntimeMethod(nameof(Math.Log), [typeof(double)])!;
    private static readonly MethodInfo MATH_LOG_BASE = typeof(Math).GetRuntimeMethod(nameof(Math.Log), [typeof(double), typeof(double)])!;
    private static readonly MethodInfo MATH_LOG10 = typeof(Math).GetRuntimeMethod(nameof(Math.Log10), [typeof(double)])!;
    private static readonly MethodInfo MATH_EXP = typeof(Math).GetRuntimeMethod(nameof(Math.Exp), [typeof(double)])!;

    private static readonly MethodInfo MATH_SIN = typeof(Math).GetRuntimeMethod(nameof(Math.Sin), [typeof(double)])!;
    private static readonly MethodInfo MATH_COS = typeof(Math).GetRuntimeMethod(nameof(Math.Cos), [typeof(double)])!;
    private static readonly MethodInfo MATH_TAN = typeof(Math).GetRuntimeMethod(nameof(Math.Tan), [typeof(double)])!;
    private static readonly MethodInfo MATH_ASIN = typeof(Math).GetRuntimeMethod(nameof(Math.Asin), [typeof(double)])!;
    private static readonly MethodInfo MATH_ACOS = typeof(Math).GetRuntimeMethod(nameof(Math.Acos), [typeof(double)])!;
    private static readonly MethodInfo MATH_ATAN = typeof(Math).GetRuntimeMethod(nameof(Math.Atan), [typeof(double)])!;
    private static readonly MethodInfo MATH_ATAN2 = typeof(Math).GetRuntimeMethod(nameof(Math.Atan2), [typeof(double), typeof(double)])!;

    private static readonly MethodInfo MATH_MAX_INT = typeof(Math).GetRuntimeMethod(nameof(Math.Max), [typeof(int), typeof(int)])!;
    private static readonly MethodInfo MATH_MAX_LONG = typeof(Math).GetRuntimeMethod(nameof(Math.Max), [typeof(long), typeof(long)])!;
    private static readonly MethodInfo MATH_MAX_DOUBLE = typeof(Math).GetRuntimeMethod(nameof(Math.Max), [typeof(double), typeof(double)])!;
    private static readonly MethodInfo MATH_MAX_DECIMAL = typeof(Math).GetRuntimeMethod(nameof(Math.Max), [typeof(decimal), typeof(decimal)])!;

    private static readonly MethodInfo MATH_MIN_INT = typeof(Math).GetRuntimeMethod(nameof(Math.Min), [typeof(int), typeof(int)])!;
    private static readonly MethodInfo MATH_MIN_LONG = typeof(Math).GetRuntimeMethod(nameof(Math.Min), [typeof(long), typeof(long)])!;
    private static readonly MethodInfo MATH_MIN_DOUBLE = typeof(Math).GetRuntimeMethod(nameof(Math.Min), [typeof(double), typeof(double)])!;
    private static readonly MethodInfo MATH_MIN_DECIMAL = typeof(Math).GetRuntimeMethod(nameof(Math.Min), [typeof(decimal), typeof(decimal)])!;

    private static readonly MethodInfo MATH_SIGN_INT = typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(int)])!;
    private static readonly MethodInfo MATH_SIGN_LONG = typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(long)])!;
    private static readonly MethodInfo MATH_SIGN_DOUBLE = typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(double)])!;
    private static readonly MethodInfo MATH_SIGN_DECIMAL = typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(decimal)])!;

    private static readonly HashSet<MethodInfo> ABS_METHODS =
    [
        MATH_ABS_DECIMAL,
        MATH_ABS_DOUBLE,
        MATH_ABS_FLOAT,
        MATH_ABS_INT,
        MATH_ABS_LONG,
        MATH_ABS_SHORT,
        MATH_ABS_SBYTE
    ];

    private static readonly HashSet<MethodInfo> CEILING_METHODS = [MATH_CEILING_DECIMAL, MATH_CEILING_DOUBLE];
    private static readonly HashSet<MethodInfo> FLOOR_METHODS = [MATH_FLOOR_DECIMAL, MATH_FLOOR_DOUBLE];
    private static readonly HashSet<MethodInfo> ROUND_METHODS = [MATH_ROUND_DECIMAL, MATH_ROUND_DOUBLE];
    private static readonly HashSet<MethodInfo> ROUND_DIGITS_METHODS = [MATH_ROUND_DECIMAL_DIGITS, MATH_ROUND_DOUBLE_DIGITS];
    private static readonly HashSet<MethodInfo> TRUNCATE_METHODS = [MATH_TRUNCATE_DECIMAL, MATH_TRUNCATE_DOUBLE];
    private static readonly HashSet<MethodInfo> MAX_METHODS = [MATH_MAX_INT, MATH_MAX_LONG, MATH_MAX_DOUBLE, MATH_MAX_DECIMAL];
    private static readonly HashSet<MethodInfo> MIN_METHODS = [MATH_MIN_INT, MATH_MIN_LONG, MATH_MIN_DOUBLE, MATH_MIN_DECIMAL];
    private static readonly HashSet<MethodInfo> SIGN_METHODS = [MATH_SIGN_INT, MATH_SIGN_LONG, MATH_SIGN_DOUBLE, MATH_SIGN_DECIMAL];

    #endregion

    #region Fields

    private readonly ISqlExpressionFactory m_sqlExpressionFactory;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitMathMethodTranslator"/> class.
    /// </summary>
    /// <param name="sqlExpressionFactory">The SQL expression factory.</param>
    public WitMathMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
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
        if (ABS_METHODS.Contains(method))
        {
            return CreateFunction("ABS", arguments, method.ReturnType);
        }

        if (CEILING_METHODS.Contains(method))
        {
            return CreateFunction("CEIL", arguments, method.ReturnType);
        }

        if (FLOOR_METHODS.Contains(method))
        {
            return CreateFunction("FLOOR", arguments, method.ReturnType);
        }

        if (ROUND_METHODS.Contains(method))
        {
            return CreateFunction("ROUND", arguments, method.ReturnType);
        }

        if (ROUND_DIGITS_METHODS.Contains(method))
        {
            return CreateFunction("ROUND", arguments, method.ReturnType);
        }

        if (TRUNCATE_METHODS.Contains(method))
        {
            return CreateFunction("TRUNC", arguments, method.ReturnType);
        }

        if (method == MATH_POW)
        {
            return CreateFunction("POWER", arguments, typeof(double));
        }

        if (method == MATH_SQRT)
        {
            return CreateFunction("SQRT", arguments, typeof(double));
        }

        if (method == MATH_LOG)
        {
            return CreateFunction("LN", arguments, typeof(double));
        }

        if (method == MATH_LOG_BASE)
        {
            // LOG(base, value) in some SQL dialects, or LOG(value) / LOG(base)
            return CreateFunction("LOG", arguments, typeof(double));
        }

        if (method == MATH_LOG10)
        {
            return CreateFunction("LOG10", arguments, typeof(double));
        }

        if (method == MATH_EXP)
        {
            return CreateFunction("EXP", arguments, typeof(double));
        }

        if (method == MATH_SIN)
        {
            return CreateFunction("SIN", arguments, typeof(double));
        }

        if (method == MATH_COS)
        {
            return CreateFunction("COS", arguments, typeof(double));
        }

        if (method == MATH_TAN)
        {
            return CreateFunction("TAN", arguments, typeof(double));
        }

        if (method == MATH_ASIN)
        {
            return CreateFunction("ASIN", arguments, typeof(double));
        }

        if (method == MATH_ACOS)
        {
            return CreateFunction("ACOS", arguments, typeof(double));
        }

        if (method == MATH_ATAN)
        {
            return CreateFunction("ATAN", arguments, typeof(double));
        }

        if (method == MATH_ATAN2)
        {
            return CreateFunction("ATAN2", arguments, typeof(double));
        }

        if (MAX_METHODS.Contains(method))
        {
            return CreateFunction("MAX", arguments, method.ReturnType);
        }

        if (MIN_METHODS.Contains(method))
        {
            return CreateFunction("MIN", arguments, method.ReturnType);
        }

        if (SIGN_METHODS.Contains(method))
        {
            return CreateFunction("SIGN", arguments, typeof(int));
        }

        return null;
    }

    #endregion

    #region Helpers

    private SqlExpression CreateFunction(string name, IReadOnlyList<SqlExpression> arguments, Type returnType)
    {
        var nullability = arguments.Select(_ => true).ToArray();
        
        return m_sqlExpressionFactory.Function(
            name,
            arguments,
            nullable: true,
            argumentsPropagateNullability: nullability,
            returnType);
    }

    #endregion
}
