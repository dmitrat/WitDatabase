namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Manages transaction timestamps for MVCC (Multi-Version Concurrency Control).
    /// Provides monotonically increasing timestamps and tracks active transactions.
    /// </summary>
    public interface ITransactionTimestampManager
    {
        /// <summary>
        /// Gets the next timestamp for a new transaction.
        /// Timestamps are guaranteed to be monotonically increasing.
        /// </summary>
        /// <returns>A unique timestamp for the new transaction.</returns>
        long GetNextTimestamp();

        /// <summary>
        /// Registers a transaction as active with its snapshot timestamp.
        /// </summary>
        /// <param name="transactionId">The unique transaction ID.</param>
        /// <param name="snapshotTimestamp">The snapshot timestamp for the transaction.</param>
        void RegisterTransaction(long transactionId, long snapshotTimestamp);

        /// <summary>
        /// Unregisters a transaction when it completes (commit or rollback).
        /// </summary>
        /// <param name="transactionId">The transaction ID to unregister.</param>
        void UnregisterTransaction(long transactionId);

        /// <summary>
        /// Marks a transaction as committed at the specified timestamp.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <param name="commitTimestamp">The commit timestamp.</param>
        void MarkCommitted(long transactionId, long commitTimestamp);

        /// <summary>
        /// Gets the commit timestamp of a transaction, if committed.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <returns>The commit timestamp, or null if not committed.</returns>
        long? GetCommitTimestamp(long transactionId);

        /// <summary>
        /// Checks if a transaction has been committed.
        /// </summary>
        /// <param name="transactionId">The transaction ID to check.</param>
        /// <returns>True if the transaction is committed, false otherwise.</returns>
        bool IsCommitted(long transactionId);

        /// <summary>
        /// Gets the minimum snapshot timestamp among all active transactions.
        /// Used for garbage collection to determine which versions can be cleaned up.
        /// </summary>
        /// <returns>The minimum active snapshot timestamp, or current timestamp if no active transactions.</returns>
        long GetMinimumActiveSnapshotTimestamp();

        /// <summary>
        /// Gets the number of currently active transactions.
        /// </summary>
        int ActiveTransactionCount { get; }

        /// <summary>
        /// Gets the current timestamp (last assigned timestamp).
        /// </summary>
        long CurrentTimestamp { get; }
    }
}
