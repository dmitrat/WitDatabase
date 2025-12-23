using System.Collections.Concurrent;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Transactions
{
    /// <summary>
    /// Thread-safe implementation of transaction timestamp management for MVCC.
    /// Provides monotonically increasing timestamps and tracks active/committed transactions.
    /// </summary>
    public sealed class TransactionTimestampManager : ITransactionTimestampManager
    {
        #region Fields

        private readonly ConcurrentDictionary<long, TransactionInfo> m_activeTransactions;
        private readonly ConcurrentDictionary<long, long> m_committedTransactions;
        private readonly object m_timestampLock = new();
        private long m_currentTimestamp;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new transaction timestamp manager.
        /// </summary>
        /// <param name="initialTimestamp">The initial timestamp value (default: 0).</param>
        public TransactionTimestampManager(long initialTimestamp = 0)
        {
            m_currentTimestamp = initialTimestamp;
            m_activeTransactions = new ConcurrentDictionary<long, TransactionInfo>();
            m_committedTransactions = new ConcurrentDictionary<long, long>();
        }

        #endregion

        #region Timestamps

        /// <inheritdoc/>
        public long GetNextTimestamp()
        {
            lock (m_timestampLock)
            {
                return ++m_currentTimestamp;
            }
        }

        #endregion

        #region Registration

        /// <inheritdoc/>
        public void RegisterTransaction(long transactionId, long snapshotTimestamp)
        {
            var info = new TransactionInfo(transactionId, snapshotTimestamp);
            
            if (!m_activeTransactions.TryAdd(transactionId, info))
            {
                throw new InvalidOperationException(
                    $"Transaction {transactionId} is already registered.");
            }
        }

        /// <inheritdoc/>
        public void UnregisterTransaction(long transactionId)
        {
            m_activeTransactions.TryRemove(transactionId, out _);
        }

        #endregion

        #region Commit

        /// <inheritdoc/>
        public void MarkCommitted(long transactionId, long commitTimestamp)
        {
            // Add to committed transactions
            m_committedTransactions[transactionId] = commitTimestamp;
            
            // Remove from active transactions
            m_activeTransactions.TryRemove(transactionId, out _);
        }

        /// <inheritdoc/>
        public long? GetCommitTimestamp(long transactionId)
        {
            if (m_committedTransactions.TryGetValue(transactionId, out var timestamp))
            {
                return timestamp;
            }
            return null;
        }

        /// <inheritdoc/>
        public bool IsCommitted(long transactionId)
        {
            return m_committedTransactions.ContainsKey(transactionId);
        }

        #endregion

        #region Garbage Collection Support

        /// <inheritdoc/>
        public long GetMinimumActiveSnapshotTimestamp()
        {
            var minTimestamp = long.MaxValue;
            var hasActiveTransactions = false;

            foreach (var kvp in m_activeTransactions)
            {
                hasActiveTransactions = true;
                if (kvp.Value.SnapshotTimestamp < minTimestamp)
                {
                    minTimestamp = kvp.Value.SnapshotTimestamp;
                }
            }

            // If no active transactions, return current timestamp
            return hasActiveTransactions ? minTimestamp : CurrentTimestamp;
        }

        /// <summary>
        /// Cleans up old committed transaction records that are no longer needed.
        /// Keeps only commits that are newer than the minimum active snapshot.
        /// </summary>
        /// <returns>The number of records cleaned up.</returns>
        public int CleanupCommittedTransactions()
        {
            var minSnapshot = GetMinimumActiveSnapshotTimestamp();
            var toRemove = new List<long>();

            foreach (var kvp in m_committedTransactions)
            {
                // Keep commits that might still be visible to active transactions
                if (kvp.Value < minSnapshot)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            var removed = 0;
            foreach (var id in toRemove)
            {
                if (m_committedTransactions.TryRemove(id, out _))
                {
                    removed++;
                }
            }

            return removed;
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public int ActiveTransactionCount => m_activeTransactions.Count;

        /// <inheritdoc/>
        public long CurrentTimestamp
        {
            get
            {
                lock (m_timestampLock)
                {
                    return m_currentTimestamp;
                }
            }
        }

        /// <summary>
        /// Gets the number of committed transactions being tracked.
        /// </summary>
        public int CommittedTransactionCount => m_committedTransactions.Count;

        /// <summary>
        /// Gets information about all active transactions.
        /// </summary>
        public IReadOnlyCollection<TransactionInfo> ActiveTransactions => 
            m_activeTransactions.Values.ToList().AsReadOnly();

        #endregion

        #region Nested Types

        /// <summary>
        /// Information about an active transaction.
        /// </summary>
        public readonly struct TransactionInfo
        {
            /// <summary>
            /// Creates a new transaction info.
            /// </summary>
            public TransactionInfo(long transactionId, long snapshotTimestamp)
            {
                TransactionId = transactionId;
                SnapshotTimestamp = snapshotTimestamp;
                StartTime = DateTime.UtcNow;
            }

            /// <summary>
            /// Gets the transaction ID.
            /// </summary>
            public long TransactionId { get; }

            /// <summary>
            /// Gets the snapshot timestamp for this transaction.
            /// </summary>
            public long SnapshotTimestamp { get; }

            /// <summary>
            /// Gets the wall-clock time when the transaction started.
            /// </summary>
            public DateTime StartTime { get; }
        }

        #endregion
    }
}
