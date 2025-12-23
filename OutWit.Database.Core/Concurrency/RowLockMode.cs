namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Defines the lock mode for row-level locks.
    /// </summary>
    public enum RowLockMode
    {
        /// <summary>
        /// Shared lock (FOR SHARE) - allows multiple readers.
        /// Other transactions can read but not write the locked row.
        /// </summary>
        Shared,

        /// <summary>
        /// Exclusive lock (FOR UPDATE) - single writer.
        /// Prevents other transactions from reading or writing the locked row.
        /// </summary>
        Exclusive
    }

    /// <summary>
    /// Defines the wait behavior when a lock cannot be acquired immediately.
    /// </summary>
    public enum RowLockWaitMode
    {
        /// <summary>
        /// Wait until the lock can be acquired (default behavior).
        /// </summary>
        Wait,

        /// <summary>
        /// Fail immediately if the lock cannot be acquired (NOWAIT).
        /// </summary>
        NoWait,

        /// <summary>
        /// Skip rows that are locked by other transactions (SKIP LOCKED).
        /// </summary>
        SkipLocked
    }
}
