using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementDropTrigger : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementDropTrigger(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementDropTrigger drop)
                return false;

            return base.Is(drop, tolerance) 
                   && TriggerName.Is(drop.TriggerName)
                   && IfExists.Is(drop.IfExists);
        }

        public override WitSqlStatementDropTrigger Clone()
        {
            return new WitSqlStatementDropTrigger
            {
                Line = Line,
                Column = Column,
                TriggerName = TriggerName,
                IfExists = IfExists
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string TriggerName { get; init; }

        [ToString]
        public bool IfExists { get; init; }

        #endregion
    }
}