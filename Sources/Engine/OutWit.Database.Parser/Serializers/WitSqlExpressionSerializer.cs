using System.Text;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Serializers;

/// <summary>
/// Serializes SqlExpression AST back to SQL text for storage.
/// Uses visitor pattern for proper handling of all expression types.
/// </summary>
public sealed class WitSqlExpressionSerializer : IWitSqlVisitor<string>
{
    #region Fields

    private static readonly WitSqlExpressionSerializer INSTANCE = new();

    #endregion

    #region Constructors

    private WitSqlExpressionSerializer()
    {
    }

    #endregion

    #region Serialize

    /// <summary>
    /// Serializes an expression to SQL text.
    /// </summary>
    public static string Serialize(WitSqlExpression expression)
    {
        return expression.Accept(INSTANCE);
    }

    #endregion

    #region IWitSqlVisitor - Statements (Not Supported)

    public string VisitStatementSelect(WitSqlStatementSelect node) =>
        throw new NotSupportedException("SELECT statement serialization not supported");

    public string VisitStatementInsert(WitSqlStatementInsert node) =>
        throw new NotSupportedException("INSERT statement serialization not supported");

    public string VisitStatementUpdate(WitSqlStatementUpdate node) =>
        throw new NotSupportedException("UPDATE statement serialization not supported");

    public string VisitStatementDelete(WitSqlStatementDelete node) =>
        throw new NotSupportedException("DELETE statement serialization not supported");

    public string VisitStatementCreateTable(WitSqlStatementCreateTable node) =>
        throw new NotSupportedException("CREATE TABLE statement serialization not supported");

    public string VisitStatementDropTable(WitSqlStatementDropTable node) =>
        throw new NotSupportedException("DROP TABLE statement serialization not supported");

    public string VisitStatementAlterTable(WitSqlStatementAlterTable node) =>
        throw new NotSupportedException("ALTER TABLE statement serialization not supported");

    public string VisitStatementCreateIndex(WitSqlStatementCreateIndex node) =>
        throw new NotSupportedException("CREATE INDEX statement serialization not supported");

    public string VisitStatementDropIndex(WitSqlStatementDropIndex node) =>
        throw new NotSupportedException("DROP INDEX statement serialization not supported");

    public string VisitStatementCreateView(WitSqlStatementCreateView node) =>
        throw new NotSupportedException("CREATE VIEW statement serialization not supported");

    public string VisitStatementDropView(WitSqlStatementDropView node) =>
        throw new NotSupportedException("DROP VIEW statement serialization not supported");

    public string VisitStatementCreateTrigger(WitSqlStatementCreateTrigger node) =>
        throw new NotSupportedException("CREATE TRIGGER statement serialization not supported");

    public string VisitStatementDropTrigger(WitSqlStatementDropTrigger node) =>
        throw new NotSupportedException("DROP TRIGGER statement serialization not supported");

    public string VisitStatementCreateSequence(WitSqlStatementCreateSequence node) =>
        throw new NotSupportedException("CREATE SEQUENCE statement serialization not supported");

    public string VisitStatementDropSequence(WitSqlStatementDropSequence node) =>
        throw new NotSupportedException("DROP SEQUENCE statement serialization not supported");

    public string VisitStatementAlterSequence(WitSqlStatementAlterSequence node) =>
        throw new NotSupportedException("ALTER SEQUENCE statement serialization not supported");

    public string VisitStatementTruncate(WitSqlStatementTruncate node) =>
        throw new NotSupportedException("TRUNCATE statement serialization not supported");

    public string VisitStatementSignal(WitSqlStatementSignal node) =>
        throw new NotSupportedException("SIGNAL statement serialization not supported");

    public string VisitStatementBeginTransaction(WitSqlStatementBeginTransaction node) =>
        throw new NotSupportedException("BEGIN TRANSACTION statement serialization not supported");

    public string VisitStatementCommit(WitSqlStatementCommit node) =>
        throw new NotSupportedException("COMMIT statement serialization not supported");

    public string VisitStatementRollback(WitSqlStatementRollback node) =>
        throw new NotSupportedException("ROLLBACK statement serialization not supported");

    public string VisitStatementSavepoint(WitSqlStatementSavepoint node) =>
        throw new NotSupportedException("SAVEPOINT statement serialization not supported");

    public string VisitStatementReleaseSavepoint(WitSqlStatementReleaseSavepoint node) =>
        throw new NotSupportedException("RELEASE SAVEPOINT statement serialization not supported");

    public string VisitStatementSetTransaction(WitSqlStatementSetTransaction node) =>
        throw new NotSupportedException("SET TRANSACTION statement serialization not supported");

    public string VisitStatementMerge(WitSqlStatementMerge node) =>
        throw new NotSupportedException("MERGE statement serialization not supported");

    public string VisitStatementExplain(WitSqlStatementExplain node) =>
        throw new NotSupportedException("EXPLAIN statement serialization not supported");

    #endregion

    #region IWitSqlVisitor - Expressions

    public string VisitExpressionLiteral(WitSqlExpressionLiteral node)
    {
        return node.Type switch
        {
            LiteralType.Null => "NULL",
            LiteralType.Integer => node.Value?.ToString() ?? "0",
            LiteralType.Real => ((double)(node.Value ?? 0.0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            LiteralType.String => $"'{EscapeString(node.Value?.ToString() ?? "")}'",
            LiteralType.Blob => $"X'{Convert.ToHexString((byte[])(node.Value ?? Array.Empty<byte>()))}'",
            LiteralType.Boolean => (bool)(node.Value ?? false) ? "TRUE" : "FALSE",
            LiteralType.CurrentTimestamp => "CURRENT_TIMESTAMP",
            LiteralType.CurrentDate => "CURRENT_DATE",
            LiteralType.CurrentTime => "CURRENT_TIME",
            _ => throw new NotSupportedException($"Unsupported literal type: {node.Type}")
        };
    }

    public string VisitExpressionColumnRef(WitSqlExpressionColumnRef node)
    {
        // Handle EXCLUDED pseudo-table for ON CONFLICT DO UPDATE
        if (node.IsExcluded)
            return $"EXCLUDED.{QuoteIdentifier(node.ColumnName)}";

        if (node.TableName != null)
            return $"{QuoteIdentifier(node.TableName)}.{QuoteIdentifier(node.ColumnName)}";
        return QuoteIdentifier(node.ColumnName);
    }

    public string VisitExpressionBinary(WitSqlExpressionBinary node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        var op = GetBinaryOperator(node.Operator);

        // Add parentheses for proper precedence
        return $"({left} {op} {right})";
    }

    public string VisitExpressionUnary(WitSqlExpressionUnary node)
    {
        var operand = node.Operand.Accept(this);
        var op = node.Operator switch
        {
            UnaryOperatorType.Negate => "-",
            UnaryOperatorType.Plus => "+",
            UnaryOperatorType.Not => "NOT ",
            UnaryOperatorType.BitwiseNot => "~",
            _ => throw new NotSupportedException($"Unsupported unary operator: {node.Operator}")
        };
        return $"({op}{operand})";
    }

    public string VisitExpressionFunctionCall(WitSqlExpressionFunctionCall node)
    {
        var sb = new StringBuilder();
        sb.Append(node.FunctionName);
        sb.Append('(');

        if (node.IsStar)
        {
            sb.Append('*');
        }
        else if (node.Arguments != null && node.Arguments.Count > 0)
        {
            if (node.IsDistinct)
                sb.Append("DISTINCT ");
            sb.Append(string.Join(", ", node.Arguments.Select(a => a.Accept(this))));
        }

        sb.Append(')');

        if (node.Over != null)
        {
            sb.Append(" OVER (");
            if (node.Over.PartitionBy != null && node.Over.PartitionBy.Count > 0)
            {
                sb.Append("PARTITION BY ");
                sb.Append(string.Join(", ", node.Over.PartitionBy.Select(p => p.Accept(this))));
            }
            if (node.Over.OrderBy != null && node.Over.OrderBy.Count > 0)
            {
                if (node.Over.PartitionBy != null && node.Over.PartitionBy.Count > 0)
                    sb.Append(' ');
                sb.Append("ORDER BY ");
                sb.Append(string.Join(", ", node.Over.OrderBy.Select(o =>
                    o.Expression.Accept(this) + (o.Descending ? " DESC" : ""))));
            }
            sb.Append(')');
        }

        return sb.ToString();
    }

    public string VisitExpressionCase(WitSqlExpressionCase node)
    {
        var sb = new StringBuilder("CASE");

        if (node.Operand != null)
        {
            sb.Append(' ');
            sb.Append(node.Operand.Accept(this));
        }

        foreach (var when in node.WhenClauses)
        {
            sb.Append(" WHEN ");
            sb.Append(when.When.Accept(this));
            sb.Append(" THEN ");
            sb.Append(when.Then.Accept(this));
        }

        if (node.ElseResult != null)
        {
            sb.Append(" ELSE ");
            sb.Append(node.ElseResult.Accept(this));
        }

        sb.Append(" END");
        return sb.ToString();
    }

    public string VisitExpressionCast(WitSqlExpressionCast node)
    {
        var expr = node.Expression.Accept(this);
        var typeName = node.TargetType.TypeName;

        if (node.TargetType.Length.HasValue)
        {
            if (node.TargetType.Scale.HasValue)
                typeName += $"({node.TargetType.Length}, {node.TargetType.Scale})";
            else
                typeName += $"({node.TargetType.Length})";
        }

        return $"CAST({expr} AS {typeName})";
    }

    public string VisitExpressionBetween(WitSqlExpressionBetween node)
    {
        var expr = node.Expression.Accept(this);
        var low = node.Low.Accept(this);
        var high = node.High.Accept(this);
        var notStr = node.IsNot ? "NOT " : "";
        return $"({expr} {notStr}BETWEEN {low} AND {high})";
    }

    public string VisitExpressionIn(WitSqlExpressionIn node)
    {
        var expr = node.Expression.Accept(this);
        var notStr = node.IsNot ? "NOT " : "";

        if (node.Values != null)
        {
            var values = string.Join(", ", node.Values.Select(v => v.Accept(this)));
            return $"({expr} {notStr}IN ({values}))";
        }
        else if (node.Subquery != null)
        {
            // Subquery serialization not fully supported
            return $"({expr} {notStr}IN (SELECT ...))";
        }

        return $"({expr} {notStr}IN ())";
    }

    public string VisitExpressionLike(WitSqlExpressionLike node)
    {
        var expr = node.Expression.Accept(this);
        var pattern = node.Pattern.Accept(this);
        var notStr = node.IsNot ? "NOT " : "";

        if (node.Escape != null)
        {
            var escape = node.Escape.Accept(this);
            return $"({expr} {notStr}LIKE {pattern} ESCAPE {escape})";
        }

        return $"({expr} {notStr}LIKE {pattern})";
    }

    public string VisitExpressionIsNull(WitSqlExpressionIsNull node)
    {
        var expr = node.Expression.Accept(this);
        var notStr = node.IsNot ? " NOT" : "";
        return $"({expr} IS{notStr} NULL)";
    }

    public string VisitExpressionSubquery(WitSqlExpressionSubquery node)
    {
        // Full subquery serialization is complex - defer for now
        return "(SELECT ...)";
    }

    public string VisitExpressionGlob(WitSqlExpressionGlob node)
    {
        var expr = node.Expression.Accept(this);
        var pattern = node.Pattern.Accept(this);
        var notStr = node.IsNot ? "NOT " : "";
        return $"({expr} {notStr}GLOB {pattern})";
    }

    public string VisitExpressionIif(WitSqlExpressionIif node)
    {
        var condition = node.Condition.Accept(this);
        var trueVal = node.TrueValue.Accept(this);
        var falseVal = node.FalseValue.Accept(this);
        return $"IIF({condition}, {trueVal}, {falseVal})";
    }

    public string VisitExpressionExists(WitSqlExpressionExists node)
    {
        var notStr = node.IsNot ? "NOT " : "";
        // Full subquery serialization is complex - defer for now
        return $"({notStr}EXISTS (SELECT ...))";
    }

    public string VisitExpressionQuantified(WitSqlExpressionQuantified node)
    {
        var expr = node.Expression.Accept(this);
        var op = GetBinaryOperator(node.Operator);
        var quantifier = node.QuantifierType switch
        {
            QuantifierType.Any => "ANY",
            QuantifierType.Some => "SOME",
            QuantifierType.All => "ALL",
            _ => throw new NotSupportedException($"Unsupported quantifier type: {node.QuantifierType}")
        };
        // Full subquery serialization is complex - defer for now
        return $"({expr} {op} {quantifier} (SELECT ...))";
    }

    public string VisitExpressionParameter(WitSqlExpressionParameter node)
    {
        return node.ParameterType switch
        {
            ParameterType.Named => $"@{node.Name}",
            ParameterType.Colon => $":{node.Name}",
            ParameterType.DollarNamed => $"${node.Name}",
            ParameterType.Positional => "?",
            ParameterType.Numbered => $"${node.Position}",
            _ => throw new NotSupportedException($"Unsupported parameter type: {node.ParameterType}")
        };
    }

    public string VisitExpressionCollate(WitSqlExpressionCollate node)
    {
        var operand = node.Operand?.Accept(this) ?? "";
        return $"({operand} COLLATE {node.CollationName})";
    }

    #endregion

    #region Helpers

    private static string GetBinaryOperator(BinaryOperatorType op)
    {
        return op switch
        {
            BinaryOperatorType.Add => "+",
            BinaryOperatorType.Subtract => "-",
            BinaryOperatorType.Multiply => "*",
            BinaryOperatorType.Divide => "/",
            BinaryOperatorType.Modulo => "%",
            BinaryOperatorType.Equal => "=",
            BinaryOperatorType.NotEqual => "<>",
            BinaryOperatorType.LessThan => "<",
            BinaryOperatorType.LessOrEqual => "<=",
            BinaryOperatorType.GreaterThan => ">",
            BinaryOperatorType.GreaterOrEqual => ">=",
            BinaryOperatorType.And => "AND",
            BinaryOperatorType.Or => "OR",
            BinaryOperatorType.Concat => "||",
            BinaryOperatorType.BitwiseAnd => "&",
            BinaryOperatorType.BitwiseOr => "|",
            BinaryOperatorType.LeftShift => "<<",
            BinaryOperatorType.RightShift => ">>",
            _ => throw new NotSupportedException($"Unsupported binary operator: {op}")
        };
    }

    private static string EscapeString(string value)
    {
        return value.Replace("'", "''");
    }

    private static string QuoteIdentifier(string identifier)
    {
        // Only quote if necessary (contains special chars or is a reserved word)
        if (NeedsQuoting(identifier))
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        return identifier;
    }

    private static bool NeedsQuoting(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return true;

        // Check for special characters
        foreach (var c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return true;
        }

        // Check if starts with digit
        if (char.IsDigit(identifier[0]))
            return true;

        // Check for reserved words (case-insensitive)
        if (IsReservedWord(identifier))
            return true;

        return false;
    }

    private static readonly HashSet<string> RESERVED_WORDS = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER",
        "TABLE", "INDEX", "VIEW", "TRIGGER", "SEQUENCE", "INTO", "VALUES", "SET", "AND", "OR",
        "NOT", "NULL", "IS", "IN", "LIKE", "BETWEEN", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER",
        "ON", "AS", "ORDER", "BY", "GROUP", "HAVING", "LIMIT", "OFFSET", "UNION", "ALL", "DISTINCT",
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT", "CHECK", "UNIQUE", "DEFAULT",
        "ASC", "DESC", "TRUE", "FALSE", "CASE", "WHEN", "THEN", "ELSE", "END", "CAST", "EXISTS",
        "ANY", "SOME", "IF", "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "RETURNING"
    };

    private static bool IsReservedWord(string identifier) => RESERVED_WORDS.Contains(identifier);

    #endregion
}
