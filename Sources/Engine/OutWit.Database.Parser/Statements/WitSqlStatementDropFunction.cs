using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// <c>DROP FUNCTION [IF EXISTS] name</c>
    /// </summary>
    [MemoryPackable]
    public partial class WitSqlStatementDropFunction : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementDropFunction(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementDropFunction drop)
                return false;

            return base.Is(drop, tolerance)
                   && FunctionName.Is(drop.FunctionName)
                   && IfExists.Is(drop.IfExists);
        }

        public override WitSqlStatementDropFunction Clone()
        {
            return new WitSqlStatementDropFunction
            {
                Line = Line,
                Column = Column,
                FunctionName = FunctionName,
                IfExists = IfExists
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string FunctionName { get; init; }

        [ToString]
        public bool IfExists { get; init; }

        #endregion
    }
}
