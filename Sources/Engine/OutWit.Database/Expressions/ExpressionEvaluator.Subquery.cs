using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Query;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// Subquery expression evaluation: scalar subqueries, EXISTS, IN (subquery), ANY/SOME/ALL.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region Scalar Subquery

    /// <summary>
    /// Evaluates a scalar subquery (SELECT that returns a single value).
    /// </summary>
    private WitSqlValue EvaluateSubquery(WitSqlExpressionSubquery subquery, WitSqlRow row)
    {
        // Set outer row for correlated subqueries
        var savedOuterRow = m_context.OuterRow;
        m_context.OuterRow = row;

        try
        {
            var planner = new QueryPlanner(m_context);
            var iterator = planner.Plan(subquery.Query);
            iterator.Open();

            try
            {
                if (!iterator.MoveNext())
                {
                    // Empty result set returns NULL
                    return WitSqlValue.Null;
                }

                var resultRow = iterator.Current;

                // Check for multiple rows (scalar subquery must return exactly one row)
                if (iterator.MoveNext())
                {
                    throw new InvalidOperationException("Scalar subquery returned more than one row");
                }

                // Check for multiple columns (scalar subquery must return exactly one column)
                if (resultRow.ColumnCount != 1)
                {
                    throw new InvalidOperationException($"Scalar subquery returned {resultRow.ColumnCount} columns, expected 1");
                }

                return resultRow[0];
            }
            finally
            {
                iterator.Dispose();
            }
        }
        finally
        {
            m_context.OuterRow = savedOuterRow;
        }
    }

    #endregion

    #region EXISTS

    /// <summary>
    /// Evaluates an EXISTS (or NOT EXISTS) expression.
    /// </summary>
    private WitSqlValue EvaluateExists(WitSqlExpressionExists exists, WitSqlRow row)
    {
        // Set outer row for correlated subqueries
        var savedOuterRow = m_context.OuterRow;
        m_context.OuterRow = row;

        try
        {
            var planner = new QueryPlanner(m_context);
            var iterator = planner.Plan(exists.Query);
            iterator.Open();

            try
            {
                // EXISTS returns true if subquery returns at least one row
                var hasRows = iterator.MoveNext();
                return WitSqlValue.FromBool(exists.IsNot ? !hasRows : hasRows);
            }
            finally
            {
                iterator.Dispose();
            }
        }
        finally
        {
            m_context.OuterRow = savedOuterRow;
        }
    }

    #endregion

    #region IN with Subquery

    /// <summary>
    /// Evaluates IN (subquery) or NOT IN (subquery) expression.
    /// </summary>
    private WitSqlValue EvaluateInSubquery(WitSqlExpressionIn inExpr, WitSqlRow row)
    {
        var value = Evaluate(inExpr.Expression, row);

        if (value.IsNull)
            return WitSqlValue.Null;

        // Set outer row for correlated subqueries
        var savedOuterRow = m_context.OuterRow;
        m_context.OuterRow = row;

        try
        {
            var planner = new QueryPlanner(m_context);
            var iterator = planner.Plan(inExpr.Subquery!);
            iterator.Open();

            bool hasNull = false;

            try
            {
                while (iterator.MoveNext())
                {
                    var subqueryRow = iterator.Current;

                    // Subquery in IN must return exactly one column
                    if (subqueryRow.ColumnCount != 1)
                    {
                        throw new InvalidOperationException(
                            $"Subquery in IN must return exactly one column, got {subqueryRow.ColumnCount}");
                    }

                    var subqueryValue = subqueryRow[0];

                    if (subqueryValue.IsNull)
                    {
                        hasNull = true;
                        continue;
                    }

                    if (value == subqueryValue)
                    {
                        // Found a match
                        return WitSqlValue.FromBool(!inExpr.IsNot);
                    }
                }

                // No match found
                // If we encountered NULLs, result is NULL (per SQL standard)
                // Otherwise, result depends on NOT IN vs IN
                if (hasNull)
                {
                    return WitSqlValue.Null;
                }

                return WitSqlValue.FromBool(inExpr.IsNot);
            }
            finally
            {
                iterator.Dispose();
            }
        }
        finally
        {
            m_context.OuterRow = savedOuterRow;
        }
    }

    #endregion

    #region Quantified (ANY/SOME/ALL)

    /// <summary>
    /// Evaluates a quantified comparison: expression op ANY/SOME/ALL (subquery)
    /// </summary>
    private WitSqlValue EvaluateQuantified(WitSqlExpressionQuantified quantified, WitSqlRow row)
    {
        var leftValue = Evaluate(quantified.Expression, row);

        if (leftValue.IsNull)
            return WitSqlValue.Null;

        // Set outer row for correlated subqueries
        var savedOuterRow = m_context.OuterRow;
        m_context.OuterRow = row;

        try
        {
            var planner = new QueryPlanner(m_context);
            var iterator = planner.Plan(quantified.Subquery);
            iterator.Open();

            try
            {
                return quantified.QuantifierType switch
                {
                    QuantifierType.Any or QuantifierType.Some => EvaluateAny(leftValue, quantified.Operator, iterator),
                    QuantifierType.All => EvaluateAll(leftValue, quantified.Operator, iterator),
                    _ => throw new NotSupportedException($"Quantifier type {quantified.QuantifierType} not supported")
                };
            }
            finally
            {
                iterator.Dispose();
            }
        }
        finally
        {
            m_context.OuterRow = savedOuterRow;
        }
    }

    /// <summary>
    /// Evaluates expression op ANY (subquery).
    /// Returns true if the comparison is true for at least one row.
    /// </summary>
    private WitSqlValue EvaluateAny(WitSqlValue leftValue, BinaryOperatorType op, Interfaces.IResultIterator iterator)
    {
        bool hasNull = false;

        while (iterator.MoveNext())
        {
            var subqueryRow = iterator.Current;

            if (subqueryRow.ColumnCount != 1)
            {
                throw new InvalidOperationException(
                    $"Subquery in ANY/SOME must return exactly one column, got {subqueryRow.ColumnCount}");
            }

            var rightValue = subqueryRow[0];

            if (rightValue.IsNull)
            {
                hasNull = true;
                continue;
            }

            var comparisonResult = CompareValues(leftValue, rightValue, op);

            if (comparisonResult.IsNull)
            {
                hasNull = true;
                continue;
            }

            if (comparisonResult.AsBool())
            {
                // Found one that satisfies - ANY is true
                return WitSqlValue.True;
            }
        }

        // No match found
        // If we encountered NULLs, result is NULL
        // Otherwise, ANY is false
        return hasNull ? WitSqlValue.Null : WitSqlValue.False;
    }

    /// <summary>
    /// Evaluates expression op ALL (subquery).
    /// Returns true if the comparison is true for all rows.
    /// </summary>
    private WitSqlValue EvaluateAll(WitSqlValue leftValue, BinaryOperatorType op, Interfaces.IResultIterator iterator)
    {
        bool hasRows = false;
        bool hasNull = false;

        while (iterator.MoveNext())
        {
            hasRows = true;
            var subqueryRow = iterator.Current;

            if (subqueryRow.ColumnCount != 1)
            {
                throw new InvalidOperationException(
                    $"Subquery in ALL must return exactly one column, got {subqueryRow.ColumnCount}");
            }

            var rightValue = subqueryRow[0];

            if (rightValue.IsNull)
            {
                hasNull = true;
                continue;
            }

            var comparisonResult = CompareValues(leftValue, rightValue, op);

            if (comparisonResult.IsNull)
            {
                hasNull = true;
                continue;
            }

            if (!comparisonResult.AsBool())
            {
                // Found one that doesn't satisfy - ALL is false
                return WitSqlValue.False;
            }
        }

        // If no rows, ALL is vacuously true
        if (!hasRows)
            return WitSqlValue.True;

        // All non-null rows satisfied the condition
        // If we encountered NULLs, result is NULL (can't be certain about those rows)
        // Otherwise, ALL is true
        return hasNull ? WitSqlValue.Null : WitSqlValue.True;
    }

    /// <summary>
    /// Compares two values using the specified operator.
    /// </summary>
    private static WitSqlValue CompareValues(WitSqlValue left, WitSqlValue right, BinaryOperatorType op)
    {
        if (left.IsNull || right.IsNull)
            return WitSqlValue.Null;

        return op switch
        {
            BinaryOperatorType.Equal => WitSqlValue.FromBool(left == right),
            BinaryOperatorType.NotEqual => WitSqlValue.FromBool(left != right),
            BinaryOperatorType.LessThan => WitSqlValue.FromBool(left < right),
            BinaryOperatorType.LessOrEqual => WitSqlValue.FromBool(left <= right),
            BinaryOperatorType.GreaterThan => WitSqlValue.FromBool(left > right),
            BinaryOperatorType.GreaterOrEqual => WitSqlValue.FromBool(left >= right),
            _ => throw new NotSupportedException($"Operator {op} not supported in quantified comparison")
        };
    }

    #endregion
}
