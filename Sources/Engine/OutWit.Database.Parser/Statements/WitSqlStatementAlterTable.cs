using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.AlterActions;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementAlterTable : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementAlterTable(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementAlterTable alter)
                return false;

            return base.Is(alter, tolerance)
                   && TableName.Is(alter.TableName)
                   && Action.Is(alter.Action, tolerance);
        }

        public override WitSqlStatementAlterTable Clone()
        {
            return new WitSqlStatementAlterTable
            {
                Line = Line,
                Column = Column,
                TableName = TableName,
                Action = (AlterAction)Action.Clone()
            };
        }

        #endregion

        #region Proeprties

        [ToString]
        public required string TableName { get; init; }

        [ToString]
        public required AlterAction Action { get; init; }

        #endregion
    }
}