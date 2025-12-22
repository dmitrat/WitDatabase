using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.Clauses
{
    /// <summary>
    /// Represents a FOR UPDATE/SHARE clause in a SELECT statement.
    /// </summary>
    public class ClauseFor : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ClauseFor clause)
                return false;

            return LockingType.Is(clause.LockingType)
                   && IsNoWait.Is(clause.IsNoWait)
                   && IsSkipLocked.Is(clause.IsSkipLocked);
        }

        public override ClauseFor Clone()
        {
            return new ClauseFor
            {
                LockingType = LockingType,
                IsNoWait = IsNoWait,
                IsSkipLocked = IsSkipLocked
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The type of lock (FOR UPDATE or FOR SHARE).
        /// </summary>
        public required LockingType LockingType { get; init; }

        /// <summary>
        /// Whether NOWAIT is specified - fail immediately if lock cannot be acquired.
        /// </summary>
        public bool IsNoWait { get; init; }

        /// <summary>
        /// Whether SKIP LOCKED is specified - skip rows that are already locked.
        /// </summary>
        public bool IsSkipLocked { get; init; }

        #endregion
    }
}
