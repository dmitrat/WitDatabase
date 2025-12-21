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
        if (context.NOT() != null) return UnaryOperatorType.Not;
        if (context.TILDE() != null) return UnaryOperatorType.BitwiseNot;
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
