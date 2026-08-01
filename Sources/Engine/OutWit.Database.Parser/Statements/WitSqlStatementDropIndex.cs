using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementDropIndex : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementDropIndex(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementDropIndex drop)
                return false;

            return base.Is(drop, tolerance)
                   && IndexName.Is(drop.IndexName)
                   && IfExists.Is(drop.IfExists);
        }

        public override WitSqlStatementDropIndex Clone()
        {
            return new WitSqlStatementDropIndex
            {
                Line = Line,
                Column = Column,
                IndexName = IndexName,
                IfExists = IfExists
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string IndexName { get; init; }

        [ToString]
        public bool IfExists { get; init; }

        #endregion
    }
}