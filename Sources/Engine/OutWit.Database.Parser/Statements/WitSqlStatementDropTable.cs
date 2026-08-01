using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementDropTable : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementDropTable(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementDropTable drop)
                return false;

            return base.Is(drop, tolerance)
                   && TableName.Is(drop.TableName)
                   && IfExists.Is(drop.IfExists);
        }

        public override WitSqlStatementDropTable Clone()
        {
            return new WitSqlStatementDropTable
            {
                Line = Line,
                Column = Column,
                TableName = TableName,
                IfExists = IfExists
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string TableName { get; init; }

        [ToString]
        public bool IfExists { get; init; }

        #endregion
    }
}