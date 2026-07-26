using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Visitor;

internal sealed partial class WitSqlVisitor
{
    #region Operator Parsing Helpers

    private static UnaryOperatorType ParseUnaryOperator(WitSqlParser.UnaryExprContext context)
    {
        if (context.MINUS() != null) return UnaryOperatorType.Negate;
        if (context.PLUS() != null) return UnaryOperatorType.Plus;
        if (context.TILDE() != null) return UnaryOperatorType.BitwiseNot;
        // NOT is no longer part of unaryExpr - it has its own low-precedence alternative (notExpr).
        throw new InvalidOperationException("Unknown unary operator");
    }

    private static BinaryOperatorType ParseMulDivOperator(WitSqlParser.MulDivExprContext context)
    {
        if (context.STAR() != null) return BinaryOperatorType.Multiply;
        if (context.SLASH() != null) return BinaryOperatorType.Divide;
        if (context.PERCENT() != null) return BinaryOperatorType.Modulo;
        throw new InvalidOperationException("Unknown mul/div operator");
    }

    private static BinaryOperatorType ParseCompareOperator(WitSqlParser.CompareExprContext context)
    {
        if (context.LT() != null) return BinaryOperatorType.LessThan;
        if (context.LE() != null) return BinaryOperatorType.LessOrEqual;
        if (context.GT() != null) return BinaryOperatorType.GreaterThan;
        if (context.GE() != null) return BinaryOperatorType.GreaterOrEqual;
        throw new InvalidOperationException("Unknown comparison operator");
    }

    #endregion

    #region Identifier Normalization

    /// <summary>
    /// Normalizes an identifier by removing quotes.
    /// Supports: "identifier", [identifier], `identifier`
    /// </summary>
    /// <param name="text">Raw identifier text from parser.</param>
    /// <returns>Unquoted identifier name.</returns>
    internal static string NormalizeIdentifier(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Double-quoted: "identifier" or "escaped""quote"
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length >= 2)
        {
            var inner = text.Substring(1, text.Length - 2);
            return inner.Replace("\"\"", "\"");
        }

        // Square brackets: [identifier]
        if (text.StartsWith('[') && text.EndsWith(']') && text.Length >= 2)
        {
            return text.Substring(1, text.Length - 2);
        }

        // Backticks: `identifier`
        if (text.StartsWith('`') && text.EndsWith('`') && text.Length >= 2)
        {
            return text.Substring(1, text.Length - 2);
        }

        // Unquoted identifier - return as-is
        return text;
    }

    /// <summary>
    /// Gets normalized table name from tableName context.
    /// Handles schema.table format: schema.table -> schema.table (both parts normalized).
    /// </summary>
    internal static string GetTableName(WitSqlParser.TableNameContext context)
    {
        var schemaName = context.schemaName();
        var simpleTableName = context.simpleTableName();

        if (schemaName != null)
        {
            var schema = NormalizeIdentifier(schemaName.GetText());
            var table = NormalizeIdentifier(simpleTableName.GetText());
            return $"{schema}.{table}";
        }

        return NormalizeIdentifier(simpleTableName.GetText());
    }

    /// <summary>
    /// Gets normalized column name from columnName context.
    /// </summary>
    internal static string GetColumnName(WitSqlParser.ColumnNameContext context)
    {
        return NormalizeIdentifier(context.GetText());
    }

    /// <summary>
    /// Gets normalized alias from alias context.
    /// </summary>
    internal static string? GetAlias(WitSqlParser.AliasContext? context)
    {
        return context != null ? NormalizeIdentifier(context.GetText()) : null;
    }

    #endregion

    #region String/Blob Literal Parsing

    private static string ParseStringLiteral(string text)
    {
        // Remove quotes and handle escaped quotes
        var inner = text.Substring(1, text.Length - 2);
        return inner.Replace("''", "'");
    }

    private static byte[] ParseBlobLiteral(string text)
    {
        // Format: X'hexdigits'
        var hex = text.Substring(2, text.Length - 3);
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    #endregion
}
