using OutWit.Database.Context;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// Evaluates SQL expressions that may contain aggregate functions.
/// Used for HAVING clause evaluation where aggregates need to be computed over a group of rows.
/// </summary>
public sealed class AggregateExpressionEvaluator
{
    #region Constants

    private static readonly HashSet<string> AGGREGATE_FUNCTIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX", "GROUP_CONCAT"
    };

    #endregion

    #region Fields

    private readonly ExpressionEvaluator m_baseEvaluator;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new aggregate expression evaluator.
    /// </summary>
    /// <param name="context">The execution context.</param>
    public AggregateExpressionEvaluator(ContextExecution context)
    {
        m_baseEvaluator = new ExpressionEvaluator(context);
    }

    #endregion

    #region Evaluate

    /// <summary>
    /// Evaluates an expression that may contain aggregate functions over a group of rows.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="groupRows">The rows in the current group.</param>
    /// <param name="resultRow">The aggregated result row (for non-aggregate column access).</param>
    /// <returns>The evaluated value.</returns>
    public WitSqlValue Evaluate(WitSqlExpression expression, IReadOnlyList<WitSqlRow> groupRows, WitSqlRow resultRow)
    {
        return expression switch
        {
            WitSqlExpressionFunctionCall func when IsAggregateFunction(func) 
                => EvaluateAggregateFunction(func, groupRows),
            WitSqlExpressionBinary bin 
                => EvaluateBinary(bin, groupRows, resultRow),
            WitSqlExpressionUnary unary 
                => EvaluateUnary(unary, groupRows, resultRow),
            WitSqlExpressionColumnRef col 
                => EvaluateColumnRef(col, resultRow),
            // For non-aggregate expressions, delegate to base evaluator using result row
            _ => m_baseEvaluator.Evaluate(expression, resultRow)
        };
    }

    #endregion

    #region Aggregate Functions

    private static bool IsAggregateFunction(WitSqlExpressionFunctionCall func)
    {
        return AGGREGATE_FUNCTIONS.Contains(func.FunctionName);
    }

    private WitSqlValue EvaluateAggregateFunction(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        var funcName = func.FunctionName.ToUpperInvariant();

        return funcName switch
        {
            "COUNT" => EvaluateCount(func, groupRows),
            "SUM" => EvaluateSum(func, groupRows),
            "AVG" => EvaluateAvg(func, groupRows),
            "MIN" => EvaluateMin(func, groupRows),
            "MAX" => EvaluateMax(func, groupRows),
            "GROUP_CONCAT" => EvaluateGroupConcat(func, groupRows),
            _ => WitSqlValue.Null
        };
    }

    private WitSqlValue EvaluateCount(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
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
                var value = m_baseEvaluator.Evaluate(func.Arguments[0], row);
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
            var value = m_baseEvaluator.Evaluate(func.Arguments[0], row);
            if (!value.IsNull)
            {
                count++;
            }
        }
        return WitSqlValue.FromInt(count);
    }

    private WitSqlValue EvaluateSum(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        WitSqlValue? sum = null;
        foreach (var row in groupRows)
        {
            var value = m_baseEvaluator.Evaluate(func.Arguments[0], row);
            if (!value.IsNull)
            {
                sum = sum == null ? value : sum.Value.Add(value);
            }
        }
        return sum ?? WitSqlValue.Null;
    }

    private WitSqlValue EvaluateAvg(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        WitSqlValue? sum = null;
        long count = 0;
        foreach (var row in groupRows)
        {
            var value = m_baseEvaluator.Evaluate(func.Arguments[0], row);
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

    private WitSqlValue EvaluateMin(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        WitSqlValue? min = null;
        foreach (var row in groupRows)
        {
            var value = m_baseEvaluator.Evaluate(func.Arguments[0], row);
            if (!value.IsNull && (min == null || value < min.Value))
            {
                min = value;
            }
        }
        return min ?? WitSqlValue.Null;
    }

    private WitSqlValue EvaluateMax(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        WitSqlValue? max = null;
        foreach (var row in groupRows)
        {
            var value = m_baseEvaluator.Evaluate(func.Arguments[0], row);
            if (!value.IsNull && (max == null || value > max.Value))
            {
                max = value;
            }
        }
        return max ?? WitSqlValue.Null;
    }

    private WitSqlValue EvaluateGroupConcat(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlRow> groupRows)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
        {
            return WitSqlValue.Null;
        }

        var values = new List<string>();
        foreach (var row in groupRows)
        {
            var value = m_baseEvaluator.Evaluate(func.Arguments[0], row);
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

    #region Binary/Unary Operations

    private WitSqlValue EvaluateBinary(WitSqlExpressionBinary bin, IReadOnlyList<WitSqlRow> groupRows, WitSqlRow resultRow)
    {
        var left = Evaluate(bin.Left, groupRows, resultRow);

        // Short-circuit evaluation for AND/OR
        if (bin.Operator == BinaryOperatorType.And)
        {
            if (left.IsNull || !left.AsBool())
                return left.IsNull ? WitSqlValue.Null : WitSqlValue.False;
            return Evaluate(bin.Right, groupRows, resultRow);
        }

        if (bin.Operator == BinaryOperatorType.Or)
        {
            if (!left.IsNull && left.AsBool())
                return WitSqlValue.True;
            return Evaluate(bin.Right, groupRows, resultRow);
        }

        var right = Evaluate(bin.Right, groupRows, resultRow);

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

    private WitSqlValue EvaluateUnary(WitSqlExpressionUnary unary, IReadOnlyList<WitSqlRow> groupRows, WitSqlRow resultRow)
    {
        var operand = Evaluate(unary.Operand, groupRows, resultRow);

        return unary.Operator switch
        {
            UnaryOperatorType.Negate => operand.Negate(),
            UnaryOperatorType.Not => WitSqlValue.Not(operand),
            UnaryOperatorType.BitwiseNot => WitSqlValue.FromInt(~operand.AsInt64()),
            UnaryOperatorType.Plus => operand,
            _ => throw new NotSupportedException($"Unary operator {unary.Operator} not supported in HAVING")
        };
    }

    private static WitSqlValue EvaluateColumnRef(WitSqlExpressionColumnRef col, WitSqlRow resultRow)
    {
        // Try to get from result row (which has aliased columns)
        return resultRow[col.ColumnName];
    }

    #endregion
}
