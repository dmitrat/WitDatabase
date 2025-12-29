using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Translates member access expressions to WitSQL functions.
/// </summary>
public sealed class WitMemberTranslator : IMemberTranslator
{
    #region Fields

    private readonly ISqlExpressionFactory m_sqlExpressionFactory;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitMemberTranslator"/> class.
    /// </summary>
    /// <param name="sqlExpressionFactory">The SQL expression factory.</param>
    public WitMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        m_sqlExpressionFactory = sqlExpressionFactory;
    }

    #endregion

    #region IMemberTranslator

    /// <inheritdoc/>
    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // String.Length
        if (member.DeclaringType == typeof(string) && member.Name == nameof(string.Length))
        {
            return m_sqlExpressionFactory.Function(
                "LENGTH",
                [instance!],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(int));
        }

        // DateTime properties
        if (member.DeclaringType == typeof(DateTime))
        {
            if (instance != null)
            {
                return TranslateDateTimeMember(instance, member.Name);
            }
            
            return TranslateStaticDateTimeMember(member.Name);
        }

        // DateOnly properties
        if (member.DeclaringType == typeof(DateOnly) && instance != null)
        {
            return TranslateDateOnlyMember(instance, member.Name);
        }

        // TimeOnly properties
        if (member.DeclaringType == typeof(TimeOnly) && instance != null)
        {
            return TranslateTimeOnlyMember(instance, member.Name);
        }

        // TimeSpan properties
        if (member.DeclaringType == typeof(TimeSpan) && instance != null)
        {
            return TranslateTimeSpanMember(instance, member.Name);
        }

        return null;
    }

    #endregion

    #region DateTime Translation

    private SqlExpression? TranslateDateTimeMember(SqlExpression instance, string memberName)
    {
        return memberName switch
        {
            nameof(DateTime.Year) => CreateExtractFunction("YEAR", instance, typeof(int)),
            nameof(DateTime.Month) => CreateExtractFunction("MONTH", instance, typeof(int)),
            nameof(DateTime.Day) => CreateExtractFunction("DAY", instance, typeof(int)),
            nameof(DateTime.Hour) => CreateExtractFunction("HOUR", instance, typeof(int)),
            nameof(DateTime.Minute) => CreateExtractFunction("MINUTE", instance, typeof(int)),
            nameof(DateTime.Second) => CreateExtractFunction("SECOND", instance, typeof(int)),
            nameof(DateTime.Millisecond) => CreateExtractFunction("MILLISECOND", instance, typeof(int)),
            nameof(DateTime.DayOfWeek) => CreateExtractFunction("DAYOFWEEK", instance, typeof(int)),
            nameof(DateTime.DayOfYear) => CreateExtractFunction("DAYOFYEAR", instance, typeof(int)),
            nameof(DateTime.Date) => m_sqlExpressionFactory.Function(
                "DATE",
                [instance],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(DateTime)),
            nameof(DateTime.TimeOfDay) => m_sqlExpressionFactory.Function(
                "TIME",
                [instance],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(TimeSpan)),
            _ => null
        };
    }

    private SqlExpression? TranslateStaticDateTimeMember(string memberName)
    {
        return memberName switch
        {
            nameof(DateTime.Now) => m_sqlExpressionFactory.Function(
                "NOW",
                Array.Empty<SqlExpression>(),
                nullable: false,
                argumentsPropagateNullability: Array.Empty<bool>(),
                typeof(DateTime)),
            nameof(DateTime.UtcNow) => m_sqlExpressionFactory.Function(
                "NOW",
                Array.Empty<SqlExpression>(),
                nullable: false,
                argumentsPropagateNullability: Array.Empty<bool>(),
                typeof(DateTime)),
            nameof(DateTime.Today) => m_sqlExpressionFactory.Function(
                "DATE",
                [m_sqlExpressionFactory.Function(
                    "NOW",
                    Array.Empty<SqlExpression>(),
                    nullable: false,
                    argumentsPropagateNullability: Array.Empty<bool>(),
                    typeof(DateTime))],
                nullable: false,
                argumentsPropagateNullability: [false],
                typeof(DateTime)),
            _ => null
        };
    }

    #endregion

    #region DateOnly Translation

    private SqlExpression? TranslateDateOnlyMember(SqlExpression instance, string memberName)
    {
        return memberName switch
        {
            nameof(DateOnly.Year) => CreateExtractFunction("YEAR", instance, typeof(int)),
            nameof(DateOnly.Month) => CreateExtractFunction("MONTH", instance, typeof(int)),
            nameof(DateOnly.Day) => CreateExtractFunction("DAY", instance, typeof(int)),
            nameof(DateOnly.DayOfWeek) => CreateExtractFunction("DAYOFWEEK", instance, typeof(int)),
            nameof(DateOnly.DayOfYear) => CreateExtractFunction("DAYOFYEAR", instance, typeof(int)),
            _ => null
        };
    }

    #endregion

    #region TimeOnly Translation

    private SqlExpression? TranslateTimeOnlyMember(SqlExpression instance, string memberName)
    {
        return memberName switch
        {
            nameof(TimeOnly.Hour) => CreateExtractFunction("HOUR", instance, typeof(int)),
            nameof(TimeOnly.Minute) => CreateExtractFunction("MINUTE", instance, typeof(int)),
            nameof(TimeOnly.Second) => CreateExtractFunction("SECOND", instance, typeof(int)),
            nameof(TimeOnly.Millisecond) => CreateExtractFunction("MILLISECOND", instance, typeof(int)),
            _ => null
        };
    }

    #endregion

    #region TimeSpan Translation

    private SqlExpression? TranslateTimeSpanMember(SqlExpression instance, string memberName)
    {
        return memberName switch
        {
            nameof(TimeSpan.Hours) => CreateExtractFunction("HOUR", instance, typeof(int)),
            nameof(TimeSpan.Minutes) => CreateExtractFunction("MINUTE", instance, typeof(int)),
            nameof(TimeSpan.Seconds) => CreateExtractFunction("SECOND", instance, typeof(int)),
            nameof(TimeSpan.Milliseconds) => CreateExtractFunction("MILLISECOND", instance, typeof(int)),
            nameof(TimeSpan.TotalDays) => m_sqlExpressionFactory.Divide(
                m_sqlExpressionFactory.Function(
                    "TOTAL_SECONDS",
                    [instance],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(double)),
                m_sqlExpressionFactory.Constant(86400.0)),
            nameof(TimeSpan.TotalHours) => m_sqlExpressionFactory.Divide(
                m_sqlExpressionFactory.Function(
                    "TOTAL_SECONDS",
                    [instance],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(double)),
                m_sqlExpressionFactory.Constant(3600.0)),
            nameof(TimeSpan.TotalMinutes) => m_sqlExpressionFactory.Divide(
                m_sqlExpressionFactory.Function(
                    "TOTAL_SECONDS",
                    [instance],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(double)),
                m_sqlExpressionFactory.Constant(60.0)),
            nameof(TimeSpan.TotalSeconds) => m_sqlExpressionFactory.Function(
                "TOTAL_SECONDS",
                [instance],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(double)),
            _ => null
        };
    }

    #endregion

    #region Helpers

    private SqlExpression CreateExtractFunction(string part, SqlExpression instance, Type returnType)
    {
        return m_sqlExpressionFactory.Function(
            part,
            [instance],
            nullable: true,
            argumentsPropagateNullability: [true],
            returnType);
    }

    #endregion
}
