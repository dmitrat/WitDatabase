using OutWit.Database.Core.Mvcc;

namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Key-value store with Multi-Version Concurrency Control (MVCC) support.
    /// Stores multiple versions of each key for snapshot isolation.
    /// </summary>
    public interface IMvccStore : IKeyValueStore
    {
        /// <summary>
        /// Gets the value for a key as of a specific timestamp.
        /// Returns the latest version that is visible at the given timestamp.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="snapshotTimestamp">The snapshot timestamp.</param>
        /// <param name="transactionId">The reading transaction ID (0 for non-transactional).</param>
        /// <returns>The value if visible, null otherwise.</returns>
        byte[]? GetAsOf(ReadOnlySpan<byte> key, long snapshotTimestamp, long transactionId = 0);

        /// <summary>
        /// Gets the value and MVCC record for a key as of a specific timestamp.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="snapshotTimestamp">The snapshot timestamp.</param>
        /// <param name="transactionId">The reading transaction ID (0 for non-transactional).</param>
        /// <returns>The MVCC record if visible, null otherwise.</returns>
        MvccRecord? GetRecordAsOf(ReadOnlySpan<byte> key, long snapshotTimestamp, long transactionId = 0);

        /// <summary>
        /// Inserts or updates a key-value pair with MVCC versioning.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="timestamp">The creation timestamp for this version.</param>
        /// <param name="transactionId">The creating transaction ID (0 for immediate commit).</param>
        void PutVersion(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, long timestamp, long transactionId = 0);

        /// <summary>
        /// Marks a key as deleted with MVCC versioning.
        /// Does not physically remove the key; creates a tombstone version.
        /// </summary>
        /// <param name="key">The key to delete.</param>
        /// <param name="timestamp">The deletion timestamp.</param>
        /// <param name="transactionId">The deleting transaction ID (0 for immediate commit).</param>
        /// <returns>True if the key existed and was marked deleted.</returns>
        bool DeleteVersion(ReadOnlySpan<byte> key, long timestamp, long transactionId = 0);

        /// <summary>
        /// Commits all changes made by a transaction.
        /// Marks all records created by the transaction as committed.
        /// </summary>
        /// <param name="transactionId">The transaction ID to commit.</param>
        /// <param name="commitTimestamp">The commit timestamp.</param>
        void CommitTransaction(long transactionId, long commitTimestamp);

        /// <summary>
        /// Rolls back all changes made by a transaction.
        /// Removes all uncommitted versions created by the transaction.
        /// </summary>
        /// <param name="transactionId">The transaction ID to rollback.</param>
        void RollbackTransaction(long transactionId);

        /// <summary>
        /// Scans key-value pairs as of a specific timestamp.
        /// </summary>
        /// <param name="startKey">Start of key range (inclusive), null for beginning.</param>
        /// <param name="endKey">End of key range (exclusive), null for end.</param>
        /// <param name="snapshotTimestamp">The snapshot timestamp.</param>
        /// <param name="transactionId">The reading transaction ID (0 for non-transactional).</param>
        /// <returns>Enumerable of visible key-value pairs.</returns>
        IEnumerable<(byte[] Key, byte[] Value)> ScanAsOf(
            byte[]? startKey, 
            byte[]? endKey, 
            long snapshotTimestamp, 
            long transactionId = 0);

        /// <summary>
        /// Removes old versions that are no longer visible to any active transaction.
        /// </summary>
        /// <param name="minActiveSnapshotTimestamp">
        /// The minimum snapshot timestamp among all active transactions.
        /// Versions older than this that have been superseded can be removed.
        /// </param>
        /// <returns>The number of versions removed.</returns>
        int GarbageCollect(long minActiveSnapshotTimestamp);

        /// <summary>
        /// Gets the number of versions stored for a key.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>The number of versions, or 0 if key doesn't exist.</returns>
        int GetVersionCount(ReadOnlySpan<byte> key);

        /// <summary>
        /// Gets all versions of a key (for debugging/diagnostics).
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <returns>All MVCC records for the key, newest first.</returns>
        IReadOnlyList<MvccRecord> GetAllVersions(ReadOnlySpan<byte> key);
    }
}
