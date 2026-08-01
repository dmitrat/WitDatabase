using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementAlterSequence : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementAlterSequence(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementAlterSequence alter)
                return false;

            return base.Is(alter, tolerance)
                   && SequenceName.Is(alter.SequenceName)
                   && RestartWith.Is(alter.RestartWith);
        }

        public override WitSqlStatementAlterSequence Clone()
        {
            return new WitSqlStatementAlterSequence
            {
                Line = Line,
                Column = Column,
                SequenceName = SequenceName,
                RestartWith = RestartWith
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string SequenceName { get; init; }

        [ToString]
        public long? RestartWith { get; init; }

        #endregion
    }
}