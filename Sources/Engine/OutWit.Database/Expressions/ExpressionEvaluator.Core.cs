using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// Core expression evaluation: literals, column references, parameters, binary/unary operators.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region Literal

    private WitSqlValue EvaluateLiteral(WitSqlExpressionLiteral lit)
    {
        return lit.Type switch
        {
            LiteralType.Null => WitSqlValue.Null,
            LiteralType.Integer => WitSqlValue.FromInt((long)lit.Value!),
            LiteralType.Real => WitSqlValue.FromReal((double)lit.Value!),
            LiteralType.String => WitSqlValue.FromText((string)lit.Value!),
            LiteralType.Blob => WitSqlValue.FromBlob((byte[])lit.Value!),
            LiteralType.Boolean => WitSqlValue.FromBool((bool)lit.Value!),
            LiteralType.CurrentTimestamp => WitSqlValue.FromDateTime(DateTime.UtcNow),
            LiteralType.CurrentDate => WitSqlValue.FromDateOnly(DateOnly.FromDateTime(DateTime.UtcNow)),
            LiteralType.CurrentTime => WitSqlValue.FromTimeOnly(TimeOnly.FromDateTime(DateTime.UtcNow)),
            _ => throw new NotSupportedException($"Literal type not supported: {lit.Type}")
        };
    }

    #endregion

    #region Column Reference

    private WitSqlValue EvaluateColumnRef(WitSqlExpressionColumnRef col, WitSqlRow row)
    {
        // Handle EXCLUDED pseudo-table for ON CONFLICT DO UPDATE
        if (col.IsExcluded)
        {
            if (m_context.ExcludedRow == null)
                throw new InvalidOperationException("EXCLUDED pseudo-table not available outside ON CONFLICT DO UPDATE");
            if (m_context.ExcludedRow.Value.TryGetValue(col.ColumnName, out var excludedValue))
                return excludedValue;
            throw new KeyNotFoundException($"Column '{col.ColumnName}' not found in EXCLUDED row");
        }

        // Handle OLD/NEW pseudo-tables for trigger context
        if (col.TableName != null && m_context.TriggerContext != null)
        {
            if (col.TableName.Equals("OLD", StringComparison.OrdinalIgnoreCase))
            {
                if (m_context.TriggerContext.OldRow == null)
                    throw new InvalidOperationException("OLD pseudo-table not available in INSERT triggers");
                if (m_context.TriggerContext.OldRow.Value.TryGetValue(col.ColumnName, out var oldValue))
                    return oldValue;
                throw new KeyNotFoundException($"Column '{col.ColumnName}' not found in OLD row");
            }
            if (col.TableName.Equals("NEW", StringComparison.OrdinalIgnoreCase))
            {
                if (m_context.TriggerContext.NewRow == null)
                    throw new InvalidOperationException("NEW pseudo-table not available in DELETE triggers");
                if (m_context.TriggerContext.NewRow.Value.TryGetValue(col.ColumnName, out var newValue))
                    return newValue;
                throw new KeyNotFoundException($"Column '{col.ColumnName}' not found in NEW row");
            }
        }

        // When table name is specified, try qualified name FIRST
        // This is critical for joins where multiple tables have same column names
        if (col.TableName != null)
        {
            var fullName = $"{col.TableName}.{col.ColumnName}";
            if (row.TryGetValue(fullName, out var value))
                return value;
            
            // For correlated subqueries, also check the outer row with qualified name
            if (m_context.OuterRow != null && m_context.OuterRow.Value.TryGetValue(fullName, out var outerValue))
                return outerValue;
        }

        // Try to get by simple column name from current row
        if (row.TryGetValue(col.ColumnName, out var simpleValue))
            return simpleValue;

        // For correlated subqueries, check the outer row
        if (m_context.OuterRow != null)
        {
            // Try with table alias if specified
            if (col.TableName != null)
            {
                var fullName = $"{col.TableName}.{col.ColumnName}";
                if (m_context.OuterRow.Value.TryGetValue(fullName, out var outerQualified))
                    return outerQualified;
            }
            
            // Try simple column name from outer row
            if (m_context.OuterRow.Value.TryGetValue(col.ColumnName, out var outerSimple))
                return outerSimple;
        }

        // Check parameters
        var paramName = $"@{col.ColumnName}";
        if (m_context.Parameters.TryGetValue(paramName, out var paramValue))
            return paramValue;

        throw new KeyNotFoundException($"Column '{col.ColumnName}' not found");
    }

    #endregion

    #region Parameter

    private WitSqlValue EvaluateParameter(WitSqlExpressionParameter param)
    {
        string key = param.ParameterType switch
        {
            ParameterType.Named => $"@{param.Name}",
            ParameterType.Colon => $":{param.Name}",
            ParameterType.DollarNamed => $"${param.Name}",
            ParameterType.Positional => "?",
            ParameterType.Numbered => $"${param.Position}",
            _ => throw new NotSupportedException($"Parameter type not supported: {param.ParameterType}")
        };

        if (m_context.Parameters.TryGetValue(key, out var value))
            return value;

        // Prefix-agnostic fallback for named placeholders ($name / :name / @name).
        // A caller may register the value under a bare name (which the engine
        // normalizes to "@name") or under a prefix style different from the one used
        // in the SQL. The exact-key match above always wins first, so this fallback
        // only resolves when there is no exact match - it never overrides an explicit
        // binding, so it cannot bind the wrong parameter.
        if (param.Name != null)
        {
            if (m_context.Parameters.TryGetValue(param.Name, out value))
                return value;
            if (m_context.Parameters.TryGetValue($"@{param.Name}", out value))
                return value;
        }

        throw new KeyNotFoundException($"Parameter '{key}' not found");
    }

    #endregion

    #region Binary Operators

    private WitSqlValue EvaluateBinary(WitSqlExpressionBinary bin, WitSqlRow row)
    {
        var left = Evaluate(bin.Left, row);

        // Short-circuit evaluation for AND/OR
        if (bin.Operator == BinaryOperatorType.And)
        {
            if (left.IsNull || !left.AsBool())
                return left.IsNull ? WitSqlValue.Null : WitSqlValue.False;
            return Evaluate(bin.Right, row);
        }

        if (bin.Operator == BinaryOperatorType.Or)
        {
            if (!left.IsNull && left.AsBool())
                return WitSqlValue.True;
            return Evaluate(bin.Right, row);
        }

        var right = Evaluate(bin.Right, row);

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
            BinaryOperatorType.Concat => left.Concat(right),
            BinaryOperatorType.And => WitSqlValue.And(left, right),
            BinaryOperatorType.Or => WitSqlValue.Or(left, right),
            BinaryOperatorType.BitwiseAnd => WitSqlValue.FromInt(left.AsInt64() & right.AsInt64()),
            BinaryOperatorType.BitwiseOr => WitSqlValue.FromInt(left.AsInt64() | right.AsInt64()),
            BinaryOperatorType.LeftShift => WitSqlValue.FromInt(left.AsInt64() << (int)right.AsInt64()),
            BinaryOperatorType.RightShift => WitSqlValue.FromInt(left.AsInt64() >> (int)right.AsInt64()),
            _ => throw new NotSupportedException($"Operator not supported: {bin.Operator}")
        };
    }

    #endregion

    #region Unary Operators

    private WitSqlValue EvaluateUnary(WitSqlExpressionUnary unary, WitSqlRow row)
    {
        var operand = Evaluate(unary.Operand, row);

        return unary.Operator switch
        {
            UnaryOperatorType.Negate => operand.Negate(),
            UnaryOperatorType.Plus => operand,
            UnaryOperatorType.Not => WitSqlValue.Not(operand),
            UnaryOperatorType.BitwiseNot => WitSqlValue.FromInt(~operand.AsInt64()),
            _ => throw new NotSupportedException($"Unary operator not supported: {unary.Operator}")
        };
    }

    #endregion
}
