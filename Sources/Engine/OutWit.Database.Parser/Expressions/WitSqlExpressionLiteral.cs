using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Expressions;

[MemoryPackable]
public partial class WitSqlExpressionLiteral : WitSqlExpression
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitExpressionLiteral(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlExpressionLiteral literal)
            return false;

        return base.Is(literal, tolerance)
               && Type.Is(literal.Type)
               && ValuesAre(Value, literal.Value);
    }

    /// <summary>
    /// Compares two literal payloads.
    /// </summary>
    /// <remarks>
    /// A <c>BLOB</c> literal carries a <c>byte[]</c>, and until 2026-07-31 this used
    /// <c>Equals</c> - reference equality for an array. So <c>X'DEADBEEF'</c> never compared equal
    /// to an identical literal, <b>including a second parse of its own text</b>.
    /// </remarks>
    private static bool ValuesAre(object? left, object? right) =>
        left is byte[] first && right is byte[] second
            ? first.AsSpan().SequenceEqual(second)
            : Equals(left, right);

    public override WitSqlExpressionLiteral Clone()
    {
        return new WitSqlExpressionLiteral
        {
            Line = Line,
            Column = Column,
            Type = Type,
            Value = Value
        };
    }

    #endregion

    #region Properties

    [ToString]
    public required LiteralType Type { get; init; }

    [Serializers.LiteralValueFormatter.Apply]
    public object? Value { get; init; }

    #endregion
}