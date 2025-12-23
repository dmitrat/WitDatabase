using System.Runtime.CompilerServices;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Mvcc;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Transactions
{
    /// <summary>
    /// Transactional store with MVCC (Multi-Version Concurrency Control) support.
    /// Provides snapshot isolation for concurrent transactions.
    /// 
    /// Key features:
    /// - Multiple concurrent read transactions
    /// - Read transactions don't block writes
    /// - Write transactions detect conflicts at commit time
    /// - Snapshot isolation by default
    /// </summary>
    public sealed class MvccTransactionalStore : ITransactionalStore, IMvccStore
    {
        #region Constants

        /// <summary>
        /// Provider key for MVCC transactional store.
        /// </summary>
        public const string PROVIDER_KEY = "mvcc-transactional";

        /// <summary>
        /// Default isolation level for MVCC transactions.
        /// </summary>
        public const IsolationLevel DEFAULT_ISOLATION_LEVEL = IsolationLevel.Snapshot;

        #endregion

        #region Fields

        private readonly MvccKeyValueStore m_mvccStore;
        private readonly TransactionTimestampManager m_timestampManager;
        private readonly LockManager? m_lockManager;
        private readonly RowLockManager m_rowLockManager;
        private readonly DeadlockDetector m_deadlockDetector;
        private readonly bool m_ownsStore;
        private readonly IsolationLevel m_defaultIsolationLevel;
        private readonly object m_txLock = new();
        private readonly HashSet<MvccTransaction> m_activeTransactions = new();
        private long m_nextTransactionId = 1;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an MVCC transactional store wrapping the specified key-value store.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <param name="ownsStore">If true, disposes the store when this is disposed.</param>
        public MvccTransactionalStore(IKeyValueStore innerStore, bool ownsStore = true)
            : this(innerStore, lockManager: null, DEFAULT_ISOLATION_LEVEL, ownsStore)
        {
        }

        /// <summary>
        /// Creates an MVCC transactional store with optional lock manager.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <param name="lockManager">Lock manager for write serialization (null = no locking).</param>
        /// <param name="ownsStore">If true, disposes the store when this is disposed.</param>
        public MvccTransactionalStore(IKeyValueStore innerStore, LockManager? lockManager, bool ownsStore = true)
            : this(innerStore, lockManager, DEFAULT_ISOLATION_LEVEL, ownsStore)
        {
        }

        /// <summary>
        /// Creates an MVCC transactional store with optional lock manager and custom default isolation level.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <param name="lockManager">Lock manager for write serialization (null = no locking).</param>
        /// <param name="defaultIsolationLevel">Default isolation level for transactions.</param>
        /// <param name="ownsStore">If true, disposes the store when this is disposed.</param>
        public MvccTransactionalStore(
            IKeyValueStore innerStore, 
            LockManager? lockManager, 
            IsolationLevel defaultIsolationLevel,
            bool ownsStore = true)
        {
            if (innerStore == null)
                throw new ArgumentNullException(nameof(innerStore));

            m_timestampManager = new TransactionTimestampManager();
            m_mvccStore = new MvccKeyValueStore(innerStore, m_timestampManager, ownsStore);
            m_lockManager = lockManager;
            m_rowLockManager = new RowLockManager();
            m_deadlockDetector = new DeadlockDetector(m_rowLockManager, DeadlockVictimStrategy.Youngest);
            m_ownsStore = ownsStore;
            m_defaultIsolationLevel = defaultIsolationLevel;
        }

        /// <summary>
        /// Creates an MVCC transactional store with an existing MVCC store.
        /// </summary>
        /// <param name="mvccStore">The MVCC key-value store.</param>
        /// <param name="timestampManager">The timestamp manager.</param>
        /// <param name="lockManager">Lock manager for write serialization (null = no locking).</param>
        /// <param name="ownsStore">If true, disposes the store when this is disposed.</param>
        public MvccTransactionalStore(
            MvccKeyValueStore mvccStore,
            TransactionTimestampManager timestampManager,
            LockManager? lockManager = null,
            bool ownsStore = true)
            : this(mvccStore, timestampManager, lockManager, DEFAULT_ISOLATION_LEVEL, ownsStore)
        {
        }

        /// <summary>
        /// Creates an MVCC transactional store with an existing MVCC store and custom default isolation level.
        /// </summary>
        /// <param name="mvccStore">The MVCC key-value store.</param>
        /// <param name="timestampManager">The timestamp manager.</param>
        /// <param name="lockManager">Lock manager for write serialization (null = no locking).</param>
        /// <param name="defaultIsolationLevel">Default isolation level for transactions.</param>
        /// <param name="ownsStore">If true, disposes the store when this is disposed.</param>
        public MvccTransactionalStore(
            MvccKeyValueStore mvccStore,
            TransactionTimestampManager timestampManager,
            LockManager? lockManager,
            IsolationLevel defaultIsolationLevel,
            bool ownsStore = true)
        {
            m_mvccStore = mvccStore ?? throw new ArgumentNullException(nameof(mvccStore));
            m_timestampManager = timestampManager ?? throw new ArgumentNullException(nameof(timestampManager));
            m_lockManager = lockManager;
            m_rowLockManager = new RowLockManager();
            m_deadlockDetector = new DeadlockDetector(m_rowLockManager, DeadlockVictimStrategy.Youngest);
            m_ownsStore = ownsStore;
            m_defaultIsolationLevel = defaultIsolationLevel;
        }

        #endregion

        #region BeginTransaction

        /// <inheritdoc/>
        public ITransaction BeginTransaction()
        {
            return BeginTransaction(m_defaultIsolationLevel);
        }

        /// <inheritdoc/>
        public ITransaction BeginTransaction(IsolationLevel isolationLevel)
        {
            ThrowIfDisposed();
            ValidateIsolationLevel(isolationLevel);

            lock (m_txLock)
            {
                // For MVCC, read-only transactions don't need write locks
                // Write transactions get locks at commit time (optimistic)
                // For now, we don't acquire locks at begin - only for writes
                
                var snapshotTimestamp = m_timestampManager.GetNextTimestamp();
                var txId = m_nextTransactionId++;

                var tx = new MvccTransaction(
                    this, 
                    txId, 
                    snapshotTimestamp, 
                    m_timestampManager,
                    lockHandle: null,
                    isolationLevel);

                m_activeTransactions.Add(tx);
                return tx;
            }
        }

        /// <inheritdoc/>
        public ValueTask<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return BeginTransactionAsync(m_defaultIsolationLevel, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ITransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(BeginTransaction(isolationLevel));
        }

        /// <summary>
        /// Begins a read-only transaction that doesn't require any locks.
        /// </summary>
        public IMvccTransaction BeginReadOnlyTransaction()
        {
            ThrowIfDisposed();

            lock (m_txLock)
            {
                var snapshotTimestamp = m_timestampManager.GetNextTimestamp();
                var txId = m_nextTransactionId++;

                var tx = new MvccTransaction(
                    this,
                    txId,
                    snapshotTimestamp,
                    m_timestampManager,
                    lockHandle: null,
                    IsolationLevel.Snapshot);

                tx.SetReadOnly();
                m_activeTransactions.Add(tx);
                return tx;
            }
        }

        #endregion

        #region IKeyValueStore Implementation

        /// <inheritdoc/>
        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            return m_mvccStore.Get(key);
        }

        /// <inheritdoc/>
        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return m_mvccStore.GetAsync(key, cancellationToken);
        }

        /// <inheritdoc/>
        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            ThrowIfDisposed();

            using var _ = m_lockManager?.AcquireWriteLock();
            m_mvccStore.Put(key, value);
        }

        /// <inheritdoc/>
        public async ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await using var _ = m_lockManager != null
                ? await m_lockManager.AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false)
                : null;

            await m_mvccStore.PutAsync(key, value, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public bool Delete(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();

            using var _ = m_lockManager?.AcquireWriteLock();
            return m_mvccStore.Delete(key);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await using var _ = m_lockManager != null
                ? await m_lockManager.AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false)
                : null;

            return await m_mvccStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
        {
            ThrowIfDisposed();
            return m_mvccStore.Scan(startKey, endKey);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
            byte[]? startKey,
            byte[]? endKey,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return m_mvccStore.ScanAsync(startKey, endKey, cancellationToken);
        }

        /// <inheritdoc/>
        public void Flush()
        {
            ThrowIfDisposed();
            m_mvccStore.Flush();
        }

        /// <inheritdoc/>
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return m_mvccStore.FlushAsync(cancellationToken);
        }

        #endregion

        #region IMvccStore Implementation

        /// <inheritdoc/>
        public byte[]? GetAsOf(ReadOnlySpan<byte> key, long snapshotTimestamp, long transactionId = 0)
        {
            ThrowIfDisposed();
            return m_mvccStore.GetAsOf(key, snapshotTimestamp, transactionId);
        }

        /// <inheritdoc/>
        public MvccRecord? GetRecordAsOf(ReadOnlySpan<byte> key, long snapshotTimestamp, long transactionId = 0)
        {
            ThrowIfDisposed();
            return m_mvccStore.GetRecordAsOf(key, snapshotTimestamp, transactionId);
        }

        /// <inheritdoc/>
        public void PutVersion(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, long timestamp, long transactionId = 0)
        {
            ThrowIfDisposed();
            m_mvccStore.PutVersion(key, value, timestamp, transactionId);
        }

        /// <inheritdoc/>
        public bool DeleteVersion(ReadOnlySpan<byte> key, long timestamp, long transactionId = 0)
        {
            ThrowIfDisposed();
            return m_mvccStore.DeleteVersion(key, timestamp, transactionId);
        }

        /// <inheritdoc/>
        public void CommitTransaction(long transactionId, long commitTimestamp)
        {
            ThrowIfDisposed();
            m_mvccStore.CommitTransaction(transactionId, commitTimestamp);
        }

        /// <inheritdoc/>
        public void RollbackTransaction(long transactionId)
        {
            ThrowIfDisposed();
            m_mvccStore.RollbackTransaction(transactionId);
        }

        /// <inheritdoc/>
        public IEnumerable<(byte[] Key, byte[] Value)> ScanAsOf(
            byte[]? startKey,
            byte[]? endKey,
            long snapshotTimestamp,
            long transactionId = 0)
        {
            ThrowIfDisposed();
            return m_mvccStore.ScanAsOf(startKey, endKey, snapshotTimestamp, transactionId);
        }

        /// <inheritdoc/>
        public int GarbageCollect(long minActiveSnapshotTimestamp)
        {
            ThrowIfDisposed();
            return m_mvccStore.GarbageCollect(minActiveSnapshotTimestamp);
        }

        /// <inheritdoc/>
        public int GetVersionCount(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            return m_mvccStore.GetVersionCount(key);
        }

        /// <inheritdoc/>
        public IReadOnlyList<MvccRecord> GetAllVersions(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            return m_mvccStore.GetAllVersions(key);
        }

        #endregion

        #region Conflict Detection

        /// <summary>
        /// Checks if there's a write conflict for the given key.
        /// A conflict exists if another transaction has modified the key since the given snapshot.
        /// </summary>
        internal bool HasConflict(byte[] key, long snapshotTimestamp, long transactionId)
        {
            var versions = m_mvccStore.GetAllVersions(key);
            
            foreach (var version in versions)
            {
                // Skip our own writes
                if (version.TransactionId == transactionId)
                    continue;

                // Check for committed writes after our snapshot
                if (version.IsCommitted && version.CreateTimestamp > snapshotTimestamp)
                    return true;

                // Check for uncommitted writes from other transactions
                if (!version.IsCommitted && version.TransactionId != transactionId)
                {
                    // Another active transaction has written to this key
                    // This is a potential conflict - depends on who commits first
                    // For first-committer-wins, we detect this at commit time
                    if (m_timestampManager.IsCommitted(version.TransactionId))
                    {
                        var commitTs = m_timestampManager.GetCommitTimestamp(version.TransactionId);
                        if (commitTs > snapshotTimestamp)
                            return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region Transaction Lifecycle

        internal void NotifyTransactionComplete(MvccTransaction tx)
        {
            lock (m_txLock)
            {
                m_activeTransactions.Remove(tx);
            }
        }

        /// <summary>
        /// Runs garbage collection to clean up old versions.
        /// </summary>
        /// <returns>The number of old versions removed.</returns>
        public int RunGarbageCollection()
        {
            ThrowIfDisposed();

            var minSnapshot = m_timestampManager.GetMinimumActiveSnapshotTimestamp();
            return m_mvccStore.GarbageCollect(minSnapshot);
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        private static void ValidateIsolationLevel(IsolationLevel isolationLevel)
        {
            if (!Enum.IsDefined(isolationLevel))
            {
                throw new ArgumentOutOfRangeException(nameof(isolationLevel), isolationLevel,
                    "Invalid isolation level.");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;

            // Rollback any active transactions
            lock (m_txLock)
            {
                foreach (var tx in m_activeTransactions.ToList())
                {
                    try { tx.Rollback(); } catch { }
                }
            }

            m_deadlockDetector.Dispose();
            m_rowLockManager.Dispose();
            m_mvccStore.Dispose();
            m_lockManager?.Dispose();
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public int ActiveTransactionCount
        {
            get
            {
                lock (m_txLock)
                {
                    return m_activeTransactions.Count;
                }
            }
        }

        /// <summary>
        /// Gets the underlying MVCC key-value store.
        /// </summary>
        public MvccKeyValueStore MvccStore => m_mvccStore;

        /// <summary>
        /// Gets the transaction timestamp manager.
        /// </summary>
        public TransactionTimestampManager TimestampManager => m_timestampManager;

        /// <summary>
        /// Gets the row-level lock manager.
        /// </summary>
        public IRowLockManager RowLockManager => m_rowLockManager;

        /// <summary>
        /// Gets the deadlock detector.
        /// </summary>
        public DeadlockDetector DeadlockDetector => m_deadlockDetector;

        /// <inheritdoc/>
        public string ProviderKey => PROVIDER_KEY;

        #endregion
    }
}
