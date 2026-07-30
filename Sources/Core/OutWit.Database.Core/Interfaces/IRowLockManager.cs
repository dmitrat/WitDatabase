namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Interface for row-level lock management.
    /// Provides fine-grained locking at the key level.
    /// </summary>
    public interface IRowLockManager : IDisposable
    {
        /// <summary>
        /// Acquires a lock on the specified key.
        /// </summary>
        /// <param name="request">The lock request.</param>
        /// <returns>A handle to release the lock, or null if SKIP LOCKED and row is locked.</returns>
        /// <exception cref="RowLockException">Thrown if NOWAIT and lock cannot be acquired.</exception>
        /// <exception cref="TimeoutException">Thrown if timeout expires while waiting.</exception>
        RowLockHandle? AcquireLock(RowLockRequest request);

        /// <summary>
        /// Acquires a lock on the specified key asynchronously.
        /// </summary>
        /// <param name="request">The lock request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A handle to release the lock, or null if SKIP LOCKED and row is locked.</returns>
        ValueTask<RowLockHandle?> AcquireLockAsync(RowLockRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases all locks held by a transaction.
        /// Called automatically on commit or rollback.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        void ReleaseAllLocks(long transactionId);

        /// <summary>
        /// Releases one transaction's lock on a single key, granting it to the next waiter if any.
        /// </summary>
        /// <remarks>
        /// This is what <see cref="RowLockHandle.Dispose"/> calls. Note that the engine's own
        /// transactions do <b>not</b> release locks this way: under two-phase locking a row lock is
        /// held to the end of the transaction and released by <see cref="ReleaseAllLocks"/>, which is
        /// why <c>MvccTransaction</c> never disposes the handles it acquires. This member exists for a
        /// caller driving <see cref="IRowLockManager"/> directly.
        /// </remarks>
        /// <param name="key">The locked key.</param>
        /// <param name="transactionId">The transaction whose lock is released.</param>
        void ReleaseLock(ReadOnlySpan<byte> key, long transactionId);

        /// <summary>
        /// Checks if a key is currently locked by any transaction.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>True if locked, false otherwise.</returns>
        bool IsLocked(ReadOnlySpan<byte> key);

        /// <summary>
        /// Checks if a key is locked by a specific transaction.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <param name="transactionId">The transaction ID.</param>
        /// <returns>True if locked by this transaction, false otherwise.</returns>
        bool IsLockedByTransaction(ReadOnlySpan<byte> key, long transactionId);

        /// <summary>
        /// Gets all keys currently locked by a transaction.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <returns>Collection of locked keys.</returns>
        IReadOnlyCollection<byte[]> GetLockedKeys(long transactionId);

        /// <summary>
        /// Gets every transaction currently holding a lock on a key.
        /// </summary>
        /// <remarks>
        /// This is the inverse of <see cref="GetLockedKeys"/>, and it is what deadlock detection needs:
        /// a waiter has to know all of the holders it is waiting behind, not just whether the key is
        /// locked. A shared lock can have several holders, so <see cref="IsLocked"/> is not enough.
        /// </remarks>
        /// <param name="key">The key to inspect.</param>
        /// <returns>The holding transaction IDs, empty if the key is not locked.</returns>
        IReadOnlyCollection<long> GetHoldingTransactions(ReadOnlySpan<byte> key);

        /// <summary>
        /// Gets the total number of active locks.
        /// </summary>
        int LockCount { get; }

        /// <summary>
        /// Gets the number of transactions currently holding locks.
        /// </summary>
        int HoldingTransactionCount { get; }
    }
}
