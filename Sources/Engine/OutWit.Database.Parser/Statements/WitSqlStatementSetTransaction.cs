using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// Represents a SET TRANSACTION ISOLATION LEVEL statement.
    /// </summary>
    [MemoryPackable]
    public partial class WitSqlStatementSetTransaction : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementSetTransaction(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementSetTransaction setTransaction)
                return false;

            return base.Is(other, tolerance)
                   && IsolationLevel.Is(setTransaction.IsolationLevel);
        }

        public override WitSqlStatementSetTransaction Clone()
        {
            return new WitSqlStatementSetTransaction
            {
                Line = Line,
                Column = Column,
                IsolationLevel = IsolationLevel
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The isolation level to set.
        /// </summary>
        [ToString]
        public required IsolationLevelType IsolationLevel { get; init; }

        #endregion
    }
}
