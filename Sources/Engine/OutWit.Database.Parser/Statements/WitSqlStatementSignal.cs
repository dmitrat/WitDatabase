using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// SIGNAL SQLSTATE statement for raising errors from triggers.
    /// </summary>
    /// <remarks>
    /// Syntax: SIGNAL SQLSTATE 'state_code' [SET MESSAGE_TEXT = expression]
    /// Example: SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Balance cannot be negative'
    /// </remarks>
    [MemoryPackable]
    public partial class WitSqlStatementSignal : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementSignal(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementSignal signal)
                return false;

            return base.Is(signal, tolerance)
                   && SqlState.Is(signal.SqlState)
                   && MessageText.Check(signal.MessageText);
        }

        public override WitSqlStatementSignal Clone()
        {
            return new WitSqlStatementSignal
            {
                Line = Line,
                Column = Column,
                SqlState = SqlState,
                MessageText = (WitSqlExpression?)MessageText?.Clone()
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The SQLSTATE code (e.g., '45000' for user-defined exception).
        /// </summary>
        [ToString]
        public required string SqlState { get; init; }

        /// <summary>
        /// Optional message text expression.
        /// </summary>
        public WitSqlExpression? MessageText { get; init; }

        #endregion
    }
}
