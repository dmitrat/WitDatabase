using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// <c>DROP PROCEDURE [IF EXISTS] name</c>
    /// </summary>
    [MemoryPackable]
    public partial class WitSqlStatementDropProcedure : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementDropProcedure(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementDropProcedure drop)
                return false;

            return base.Is(drop, tolerance)
                   && ProcedureName.Is(drop.ProcedureName)
                   && IfExists.Is(drop.IfExists);
        }

        public override WitSqlStatementDropProcedure Clone()
        {
            return new WitSqlStatementDropProcedure
            {
                Line = Line,
                Column = Column,
                ProcedureName = ProcedureName,
                IfExists = IfExists
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string ProcedureName { get; init; }

        [ToString]
        public bool IfExists { get; init; }

        #endregion
    }
}
