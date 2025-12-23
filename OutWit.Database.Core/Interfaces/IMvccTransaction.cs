using OutWit.Database.Core.Concurrency;

namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Represents an MVCC transaction with snapshot isolation support.
    /// Extends the standard transaction with MVCC-specific functionality.
    /// </summary>
    public interface IMvccTransaction : ITransactionWithSavepoints
    {
        /// <summary>
        /// Gets the snapshot timestamp for this transaction.
        /// All reads see a consistent view of data as of this timestamp.
        /// </summary>
        long SnapshotTimestamp { get; }

        /// <summary>
        /// Gets the start timestamp of the transaction.
        /// Used for tracking when the transaction began.
        /// </summary>
        long StartTimestamp { get; }

        /// <summary>
        /// Gets whether this transaction is read-only.
        /// Read-only transactions don't require write locks and can run concurrently.
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// Gets the set of keys that have been read by this transaction.
        /// Used for conflict detection in RepeatableRead and Serializable isolation.
        /// </summary>
        IReadOnlySet<byte[]> ReadSet { get; }

        /// <summary>
        /// Gets the set of keys that have been written by this transaction.
        /// Used for conflict detection at commit time.
        /// </summary>
        IReadOnlySet<byte[]> WriteSet { get; }

        /// <summary>
        /// Checks if committing this transaction would cause a write-write conflict.
        /// Called before commit to detect conflicts with concurrent transactions.
        /// </summary>
        /// <returns>True if there is a conflict, false otherwise.</returns>
        bool HasWriteConflict();

        /// <summary>
        /// Marks this transaction as a read-only transaction.
        /// After calling this, the transaction cannot perform any writes.
        /// Read-only transactions can run without write locks.
        /// </summary>
        void SetReadOnly();

        #region Row-Level Locking (FOR UPDATE / FOR SHARE)

        /// <summary>
        /// Gets a value with an exclusive lock (FOR UPDATE).
        /// The lock is held until the transaction commits or rolls back.
        /// </summary>
        /// <param name="key">The key to read and lock.</param>
        /// <param name="waitMode">Lock wait behavior (Wait, NoWait, SkipLocked).</param>
        /// <param name="timeout">Optional timeout for waiting.</param>
        /// <returns>The value, or null if not found or skipped.</returns>
        byte[]? GetForUpdate(ReadOnlySpan<byte> key, RowLockWaitMode waitMode = RowLockWaitMode.Wait, TimeSpan? timeout = null);

        /// <summary>
        /// Gets a value with a shared lock (FOR SHARE).
        /// The lock is held until the transaction commits or rolls back.
        /// </summary>
        /// <param name="key">The key to read and lock.</param>
        /// <param name="waitMode">Lock wait behavior (Wait, NoWait, SkipLocked).</param>
        /// <param name="timeout">Optional timeout for waiting.</param>
        /// <returns>The value, or null if not found or skipped.</returns>
        byte[]? GetForShare(ReadOnlySpan<byte> key, RowLockWaitMode waitMode = RowLockWaitMode.Wait, TimeSpan? timeout = null);

        /// <summary>
        /// Gets a value with an exclusive lock asynchronously (FOR UPDATE).
        /// </summary>
        ValueTask<byte[]?> GetForUpdateAsync(byte[] key, RowLockWaitMode waitMode = RowLockWaitMode.Wait, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a value with a shared lock asynchronously (FOR SHARE).
        /// </summary>
        ValueTask<byte[]?> GetForShareAsync(byte[] key, RowLockWaitMode waitMode = RowLockWaitMode.Wait, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

        #endregion
    }
}
