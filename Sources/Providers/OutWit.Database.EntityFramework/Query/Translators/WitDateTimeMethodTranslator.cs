using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Translates DateTime method calls to WitSQL functions.
/// </summary>
public sealed class WitDateTimeMethodTranslator : IMethodCallTranslator
{
    #region Constants

    private static readonly MethodInfo DATETIME_ADDDAYS = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddDays), [typeof(double)])!;
    private static readonly MethodInfo DATETIME_ADDMONTHS = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMonths), [typeof(int)])!;
    private static readonly MethodInfo DATETIME_ADDYEARS = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddYears), [typeof(int)])!;
    private static readonly MethodInfo DATETIME_ADDHOURS = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddHours), [typeof(double)])!;
    private static readonly MethodInfo DATETIME_ADDMINUTES = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMinutes), [typeof(double)])!;
    private static readonly MethodInfo DATETIME_ADDSECONDS = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddSeconds), [typeof(double)])!;
    private static readonly MethodInfo DATETIME_ADDMILLISECONDS = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMilliseconds), [typeof(double)])!;

    #endregion

    #region Fields

    private readonly ISqlExpressionFactory m_sqlExpressionFactory;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDateTimeMethodTranslator"/> class.
    /// </summary>
    /// <param name="sqlExpressionFactory">The SQL expression factory.</param>
    public WitDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
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
        if (instance == null)
        {
            return null;
        }

        if (method == DATETIME_ADDDAYS)
        {
            return CreateDateAddFunction(instance, arguments[0], "day");
        }

        if (method == DATETIME_ADDMONTHS)
        {
            return CreateDateAddFunction(instance, arguments[0], "month");
        }

        if (method == DATETIME_ADDYEARS)
        {
            return CreateDateAddFunction(instance, arguments[0], "year");
        }

        if (method == DATETIME_ADDHOURS)
        {
            return CreateDateAddFunction(instance, arguments[0], "hour");
        }

        if (method == DATETIME_ADDMINUTES)
        {
            return CreateDateAddFunction(instance, arguments[0], "minute");
        }

        if (method == DATETIME_ADDSECONDS)
        {
            return CreateDateAddFunction(instance, arguments[0], "second");
        }

        if (method == DATETIME_ADDMILLISECONDS)
        {
            return CreateDateAddFunction(instance, arguments[0], "millisecond");
        }

        return null;
    }

    #endregion

    #region Helpers

    private SqlExpression CreateDateAddFunction(SqlExpression dateTime, SqlExpression interval, string part)
    {
        return m_sqlExpressionFactory.Function(
            "DATEADD",
            [m_sqlExpressionFactory.Constant(part), interval, dateTime],
            nullable: true,
            argumentsPropagateNullability: [false, true, true],
            typeof(DateTime));
    }

    #endregion
}
