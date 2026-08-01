using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements;

[MemoryPackable]
public partial class WitSqlStatementDropSequence : WitSqlStatement
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitStatementDropSequence(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlStatementDropSequence drop)
            return false;

        return base.Is(drop, tolerance)
               && SequenceName.Is(drop.SequenceName)
               && IfExists.Is(drop.IfExists);
    }

    public override WitSqlStatementDropSequence Clone()
    {
        return new WitSqlStatementDropSequence
        {
            Line = Line,
            Column = Column,
            SequenceName = SequenceName,
            IfExists = IfExists
        };
    }

    #endregion

    #region Proerties

    [ToString]
    public required string SequenceName { get; init; }

    [ToString]
    public bool IfExists { get; init; }

    #endregion
}