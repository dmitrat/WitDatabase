using OutWit.Database.Constants;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Sql;
using OutWit.Database.Values;
using OutWit.Database.Parser.Analysis;

namespace OutWit.Database.Expressions;

/// <summary>
/// Aggregate expression evaluation: evaluates expressions over groups of rows for HAVING clauses.
/// Handles aggregate functions (COUNT, SUM, AVG, MIN, MAX, GROUP_CONCAT) and mixed expressions
/// containing both aggregate and non-aggregate components.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region Aggregate Expression Evaluation

    /// <summary>
    /// Evaluates an expression that may contain aggregate functions over a group of rows.
    /// Used for HAVING clause evaluation where aggregates need to be computed over the group.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="groupRows">The rows in the current group.</param>
    /// <param name="resultRow">The aggregated result row (for non-aggregate column access).</param>
    /// <returns>The evaluated value.</returns>
    /// <summary>
    /// Evaluates an expression that may contain aggregates, over one group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until 2026-08-01 this was a switch over four expression types whose default handed the whole
    /// expression to the plain row evaluator - which refuses aggregates. So the same aggregate that
    /// works beside a comparison threw inside <c>BETWEEN</c> or <c>IN</c>, and the message a caller
    /// saw was an internal invariant.
    /// </para>
    /// <para>
    /// The structure is gone. Every aggregate call in the expression is computed for this group
    /// first, and the expression is then evaluated by the ordinary evaluator with those results in
    /// hand. That is total by construction: it works for every node type the grammar has and for
    /// every one it gains, because the ordinary evaluator already handles them all.
    /// </para>
    /// </remarks>
    public WitSqlValue EvaluateAggregate(WitSqlExpression expression, IReadOnlyList<WitSqlRow> groupRows, WitSqlRow resultRow)
    {
        var calls = WitSqlNodes.SelfAndDescendants(expression)
            .OfType<WitSqlExpressionFunctionCall>()
            .Where(func => IsAggregateFunction(func) && func.Over == null)
            .ToArray();

        if (calls.Length == 0)
            return Evaluate(expression, resultRow);

        var resolved = new Dictionary<WitSqlExpressionFunctionCall, WitSqlValue>(ReferenceComparer.Instance);

        foreach (var call in calls)
            resolved[call] = EvaluateAggregateFunctionOverGroup(call, groupRows);

        m_aggregates = resolved;

        try
        {
            return Evaluate(expression, resultRow);
        }
        finally
        {
            m_aggregates = null;
        }
    }

    /// <summary>
    /// Aggregate calls are matched by identity, not by value: two occurrences of the same call text
    /// in one expression are separate nodes, and each is computed for its own arguments.
    /// </summary>
    private sealed class ReferenceComparer : IEqualityComparer<WitSqlExpressionFunctionCall>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(WitSqlExpressionFunctionCall? x, WitSqlExpressionFunctionCall? y) =>
            ReferenceEquals(x, y);

        public int GetHashCode(WitSqlExpressionFunctionCall obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    #endregion

    #region Aggregate Functions

    private static bool IsAggregateFunction(WitSqlExpressionFunctionCall func)
    {
        return SqlFunctions.IsAggregate(func.FunctionName);
    }

    private WitSqlValue EvaluateAggregateFunctionOverGroup(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        var funcName = func.FunctionName.ToUpperInvariant();

        return funcName switch
        {
            "COUNT" => EvaluateAggregateCount(func, groupRows),
            "SUM" => EvaluateAggregateSum(func, groupRows),
            "AVG" => EvaluateAggregateAvg(func, groupRows),
            "MIN" => EvaluateAggregateMin(func, groupRows),
            "MAX" => EvaluateAggregateMax(func, groupRows),
            "GROUP_CONCAT" => EvaluateAggregateGroupConcat(func, groupRows),
            _ => WitSqlValue.Null
        };
    }

    private WitSqlValue EvaluateAggregateCount(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.IsStar)
        {
            return WitSqlValue.FromInt(groupRows.Count);
        }

        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.FromInt(groupRows.Count);
        }

        if (func.IsDistinct)
        {
            var distinctValues = new HashSet<WitSqlValue>();
            foreach (var row in groupRows)
            {
                var value = Evaluate(func.Arguments[0], row);
                if (!value.IsNull)
                {
                    distinctValues.Add(value);
                }
            }
            return WitSqlValue.FromInt(distinctValues.Count);
        }

        long count = 0;
        foreach (var row in groupRows)
        {
            var value = Evaluate(func.Arguments[0], row);
            if (!value.IsNull)
            {
                count++;
            }
        }
        return WitSqlValue.FromInt(count);
    }

    private WitSqlValue EvaluateAggregateSum(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        WitSqlValue? sum = null;
        foreach (var row in groupRows)
        {
            var value = Evaluate(func.Arguments[0], row);
            if (!value.IsNull)
            {
                sum = sum == null ? value : sum.Value.Add(value);
            }
        }
        return sum ?? WitSqlValue.Null;
    }

    private WitSqlValue EvaluateAggregateAvg(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        WitSqlValue? sum = null;
        long count = 0;
        foreach (var row in groupRows)
        {
            var value = Evaluate(func.Arguments[0], row);
            if (!value.IsNull)
            {
                sum = sum == null ? value : sum.Value.Add(value);
                count++;
            }
        }

        if (count == 0 || sum == null)
        {
            return WitSqlValue.Null;
        }

        return sum.Value.Divide(WitSqlValue.FromInt(count));
    }

    private WitSqlValue EvaluateAggregateMin(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        WitSqlValue? min = null;
        foreach (var row in groupRows)
        {
            var value = Evaluate(func.Arguments[0], row);
            if (!value.IsNull && (min == null || value < min.Value))
            {
                min = value;
            }
        }
        return min ?? WitSqlValue.Null;
    }

    private WitSqlValue EvaluateAggregateMax(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        WitSqlValue? max = null;
        foreach (var row in groupRows)
        {
            var value = Evaluate(func.Arguments[0], row);
            if (!value.IsNull && (max == null || value > max.Value))
            {
                max = value;
            }
        }
        return max ?? WitSqlValue.Null;
    }

    private WitSqlValue EvaluateAggregateGroupConcat(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        var values = new List<string>();
        foreach (var row in groupRows)
        {
            var value = Evaluate(func.Arguments[0], row);
            if (!value.IsNull)
            {
                values.Add(value.AsString());
            }
        }

        if (values.Count == 0)
        {
            return WitSqlValue.Null;
        }

        return WitSqlValue.FromText(string.Join(",", values));
    }

    #endregion

    #region Aggregate Binary/Unary Operations

    private WitSqlValue EvaluateAggregateBinary(WitSqlExpressionBinary bin, IReadOnlyList<WitSqlRow> groupRows, WitSqlRow resultRow)
    {
        var left = EvaluateAggregate(bin.Left, groupRows, resultRow);

        // Short-circuit evaluation for AND/OR
        if (bin.Operator == BinaryOperatorType.And)
        {
            if (left.IsNull || !left.AsBool())
                return left.IsNull ? WitSqlValue.Null : WitSqlValue.False;
            return EvaluateAggregate(bin.Right, groupRows, resultRow);
        }

        if (bin.Operator == BinaryOperatorType.Or)
        {
            if (!left.IsNull && left.AsBool())
                return WitSqlValue.True;
            return EvaluateAggregate(bin.Right, groupRows, resultRow);
        }

        var right = EvaluateAggregate(bin.Right, groupRows, resultRow);

        return bin.Operator switch
        {
            BinaryOperatorType.Add => left.Add(right),
            BinaryOperatorType.Subtract => left.Subtract(right),
            BinaryOperatorType.Multiply => left.Multiply(right),
            BinaryOperatorType.Divide => left.Divide(right),
            BinaryOperatorType.Modulo => left.Modulo(right),
            BinaryOperatorType.Equal => WitSqlValue.FromBool(left == right),
            BinaryOperatorType.NotEqual => WitSqlValue.FromBool(left != right),
            BinaryOperatorType.LessThan => WitSqlValue.FromBool(left < right),
            BinaryOperatorType.LessOrEqual => WitSqlValue.FromBool(left <= right),
            BinaryOperatorType.GreaterThan => WitSqlValue.FromBool(left > right),
            BinaryOperatorType.GreaterOrEqual => WitSqlValue.FromBool(left >= right),
            BinaryOperatorType.And => WitSqlValue.And(left, right),
            BinaryOperatorType.Or => WitSqlValue.Or(left, right),
            BinaryOperatorType.Concat => left.Concat(right),
            BinaryOperatorType.BitwiseAnd => WitSqlValue.FromInt(left.AsInt64() & right.AsInt64()),
            BinaryOperatorType.BitwiseOr => WitSqlValue.FromInt(left.AsInt64() | right.AsInt64()),
            BinaryOperatorType.LeftShift => WitSqlValue.FromInt(left.AsInt64() << (int)right.AsInt64()),
            BinaryOperatorType.RightShift => WitSqlValue.FromInt(left.AsInt64() >> (int)right.AsInt64()),
            _ => throw new NotSupportedException($"Binary operator {bin.Operator} not supported in HAVING")
        };
    }

    private WitSqlValue EvaluateAggregateUnary(WitSqlExpressionUnary unary, IReadOnlyList<WitSqlRow> groupRows, WitSqlRow resultRow)
    {
        var operand = EvaluateAggregate(unary.Operand, groupRows, resultRow);

        return unary.Operator switch
        {
            UnaryOperatorType.Negate => operand.Negate(),
            UnaryOperatorType.Not => WitSqlValue.Not(operand),
            UnaryOperatorType.BitwiseNot => WitSqlValue.FromInt(~operand.AsInt64()),
            UnaryOperatorType.Plus => operand,
            _ => throw new NotSupportedException($"Unary operator {unary.Operator} not supported in HAVING")
        };
    }

    private static WitSqlValue EvaluateAggregateColumnRef(WitSqlExpressionColumnRef col, WitSqlRow resultRow)
    {
        // Try to get from result row (which has aliased columns)
        return resultRow[col.ColumnName];
    }

    #endregion
}
