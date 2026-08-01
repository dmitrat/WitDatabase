using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementDropView : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementDropView(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementDropView drop)
                return false;

            return base.Is(drop, tolerance)
                   && ViewName.Is(drop.ViewName)
                   && IfExists.Is(drop.IfExists);
        }

        public override WitSqlStatementDropView Clone()
        {
            return new WitSqlStatementDropView
            {
                Line = Line,
                Column = Column,
                ViewName = ViewName,
                IfExists = IfExists
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string ViewName { get; init; }

        [ToString]
        public bool IfExists { get; init; }

        #endregion
    }
}