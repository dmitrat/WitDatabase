using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementCreateSequence : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementCreateSequence(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementCreateSequence create)
                return false;

            return base.Is(create, tolerance)
                   && SequenceName.Is(create.SequenceName)
                   && IfNotExists.Is(create.IfNotExists)
                   && StartWith.Is(create.StartWith);
        }

        public override WitSqlStatementCreateSequence Clone()
        {
            return new WitSqlStatementCreateSequence
            {
                Line = Line,
                Column = Column,
                SequenceName = SequenceName,
                IfNotExists = IfNotExists,
                StartWith = StartWith
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string SequenceName { get; init; }

        [ToString]
        public bool IfNotExists { get; init; }
        public long StartWith { get; init; } = 1;

        #endregion
    }
}