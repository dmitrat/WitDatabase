using OutWit.Database.Context;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// Evaluates SQL expressions against a row context.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region Fields

    private readonly ContextExecution m_context;

    #endregion

    #region Constructors

    public ExpressionEvaluator(ContextExecution context)
    {
        m_context = context;
    }

    #endregion

    #region Evaluate

    /// <summary>
    /// Evaluate an expression and return the result value.
    /// </summary>
    public WitSqlValue Evaluate(WitSqlExpression expression, WitSqlRow row)
    {
        return expression switch
        {
            WitSqlExpressionLiteral lit => EvaluateLiteral(lit),
            WitSqlExpressionColumnRef col => EvaluateColumnRef(col, row),
            WitSqlExpressionParameter param => EvaluateParameter(param),
            WitSqlExpressionBinary bin => EvaluateBinary(bin, row),
            WitSqlExpressionUnary unary => EvaluateUnary(unary, row),
            WitSqlExpressionFunctionCall func => EvaluateFunction(func, row),
            WitSqlExpressionCase caseExpr => EvaluateCase(caseExpr, row),
            WitSqlExpressionCast cast => EvaluateCast(cast, row),
            WitSqlExpressionBetween between => EvaluateBetween(between, row),
            WitSqlExpressionIn inExpr => EvaluateIn(inExpr, row),
            WitSqlExpressionLike like => EvaluateLike(like, row),
            WitSqlExpressionIsNull isNull => EvaluateIsNull(isNull, row),
            WitSqlExpressionIif iif => EvaluateIif(iif, row),
            WitSqlExpressionGlob glob => EvaluateGlob(glob, row),
            WitSqlExpressionCollate collate => EvaluateCollate(collate, row),
            WitSqlExpressionExists exists => EvaluateExists(exists, row),
            WitSqlExpressionQuantified quantified => EvaluateQuantified(quantified, row),
            WitSqlExpressionSubquery subquery => EvaluateSubquery(subquery, row),
            WitSqlExpressionOrderByColumnIndex colIndex => EvaluateOrderByColumnIndex(colIndex, row),
            _ => throw new NotSupportedException($"Expression type not supported: {expression.GetType().Name}")
        };
    }

    /// <summary>
    /// Evaluates a column index expression for ORDER BY on aggregate results.
    /// </summary>
    private static WitSqlValue EvaluateOrderByColumnIndex(WitSqlExpressionOrderByColumnIndex colIndex, WitSqlRow row)
    {
        if (colIndex.ColumnIndex < 0 || colIndex.ColumnIndex >= row.ColumnCount)
        {
            throw new InvalidOperationException(
                $"Column index {colIndex.ColumnIndex} is out of range. Row has {row.ColumnCount} columns.");
        }

        return row[colIndex.ColumnIndex];
    }

    #endregion
}
