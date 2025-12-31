using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Expressions;

/// <summary>
/// Expression that references a result column by its 0-based index.
/// Used internally by QueryPlanner to resolve ORDER BY aggregate expressions
/// to their corresponding SELECT list positions.
/// </summary>
/// <remarks>
/// This expression is not parsed from SQL - it is created during query planning
/// when ORDER BY contains aggregate expressions like SUM(Amount) that need to
/// reference the computed column from the SELECT list.
/// </remarks>
public sealed class WitSqlExpressionOrderByColumnIndex : WitSqlExpression
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        // This expression type is handled specially in ExpressionEvaluator
        // and should not be visited through the standard visitor pattern.
        throw new NotSupportedException(
            "WitSqlExpressionOrderByColumnIndex should be evaluated directly, not visited.");
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlExpressionOrderByColumnIndex indexExpr)
            return false;

        return base.Is(indexExpr, tolerance)
               && ColumnIndex.Is(indexExpr.ColumnIndex);
    }

    public override WitSqlExpressionOrderByColumnIndex Clone()
    {
        return new WitSqlExpressionOrderByColumnIndex
        {
            Line = Line,
            Column = Column,
            ColumnIndex = ColumnIndex
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The 0-based index of the column in the result set.
    /// </summary>
    [ToString]
    public required int ColumnIndex { get; init; }

    #endregion
}
