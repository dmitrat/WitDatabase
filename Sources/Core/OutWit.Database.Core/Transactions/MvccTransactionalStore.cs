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
    /// - Priority-based transaction wait queue
    /// </summary>
    public sealed class MvccTransactionalStore : ITransactionalStore, IMvccStore, IStoreWrapper, IAsyncDisposable
    {
        #region Constants

        /// <summary>
        /// Provider key for MVCC transactional store.
        /// </summary>
        public const string PROVIDER_KEY = "mvcc-transactional";

        /// <summary>
        /// Default isolation level for MVCC transactions.
        /// </summary>
        public const WitIsolationLevel DEFAULT_ISOLATION_LEVEL = WitIsolationLevel.Snapshot;

        #endregion

        #region Fields

        private readonly MvccKeyValueStore m_mvccStore;
        private readonly TransactionTimestampManager m_timestampManager;
        private readonly LockManager? m_lockManager;
        private readonly RowLockManager m_rowLockManager;
        private readonly DeadlockDetector m_deadlockDetector;
        private readonly TransactionWaitQueue m_waitQueue;
        private readonly bool m_ownsStore;
        private readonly WitIsolationLevel m_defaultIsolationLevel;
        private readonly object m_txLock = new();
        private readonly object m_commitLock = new();
        private readonly HashSet<MvccTransaction> m_activeTransactions = new();
        private long m_nextTransactionId = 1;
        private bool m_disposed;

        #endregion

        #region Durability

        /// <summary>
        /// Whether a successful commit is flushed to the underlying store before it returns.
        /// Defaults to <c>true</c>.
        /// </summary>
        /// <remarks>
        /// MVCC is the default transactional mode behind the ADO.NET and EF Core providers, and its
        /// commit path used to apply the new versions in memory and return without flushing anything.
        /// A process kill after a successful COMMIT therefore lost the transaction - the D in ACID -
        /// and there is no journal on this path to replay it from.
        ///
        /// Turning this off trades that guarantee for throughput. It is a legitimate choice for a
        /// disposable test database or a bulk import that will be re-run on failure, and it is what
        /// the pre-2.0 behaviour was, but it must be opted into rather than inherited by accident.
        /// </remarks>
        public bool SynchronousCommit { get; set; } = true;

        /// <summary>
        /// Serialises the apply-and-publish phase of a commit.
        /// </summary>
        /// <remarks>
        /// Held from conflict validation through to publishing the commit timestamp, so a commit is
        /// all-or-nothing as far as any snapshot is concerned, and so validation cannot pass for two
        /// writers that then both install. Deliberately NOT taken by <c>BeginTransaction</c> or by
        /// readers: they would then block behind a commit, and the
        /// <c>Dispose -> Rollback -> NotifyTransactionComplete</c> path would close a cycle with
        /// <c>m_txLock</c>. Lock order is m_commitLock -> m_txLock -> the timestamp manager's lock.
        /// Never held across <c>Flush</c>, <c>ReleaseLocks</c> or the wait queue.
        /// </remarks>
        internal object CommitLock => m_commitLock;

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
            WitIsolationLevel defaultIsolationLevel,
            bool ownsStore = true)
            : this(innerStore, lockManager, defaultIsolationLevel, null, ownsStore)
        {
        }

        /// <summary>
        /// Creates an MVCC transactional store with full configuration.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <param name="lockManager">Lock manager for write serialization (null = no locking).</param>
        /// <param name="defaultIsolationLevel">Default isolation level for transactions.</param>
        /// <param name="waitQueueOptions">Options for transaction wait queue (null = default options).</param>
        /// <param name="ownsStore">If true, disposes the store when this is disposed.</param>
        public MvccTransactionalStore(
            IKeyValueStore innerStore, 
            LockManager? lockManager, 
            WitIsolationLevel defaultIsolationLevel,
            TransactionWaitQueueOptions? waitQueueOptions,
            bool ownsStore = true)
        {
            if (innerStore == null)
                throw new ArgumentNullException(nameof(innerStore));

            // Recover maximum timestamp from existing data.
            // This uses cached value (O(1)) when available, falling back to full scan (O(n)) only for legacy databases.
            var maxTimestamp = MvccKeyValueStore.RecoverMaxTimestamp(innerStore);
            
            m_timestampManager = new TransactionTimestampManager(maxTimestamp);
            m_mvccStore = new MvccKeyValueStore(innerStore, m_timestampManager, ownsStore);
            m_lockManager = lockManager;
            m_rowLockManager = new RowLockManager();
            m_deadlockDetector = new DeadlockDetector(m_rowLockManager, DeadlockVictimStrategy.Youngest);
            m_waitQueue = new TransactionWaitQueue(waitQueueOptions ?? new TransactionWaitQueueOptions());
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
            WitIsolationLevel defaultIsolationLevel,
            bool ownsStore = true)
            : this(mvccStore, timestampManager, lockManager, defaultIsolationLevel, null, ownsStore)
        {
        }

        /// <summary>
        /// Creates an MVCC transactional store with an existing MVCC store and full configuration.
        /// </summary>
        /// <param name="mvccStore">The MVCC key-value store.</param>
        /// <param name="timestampManager">The timestamp manager.</param>
        /// <param name="lockManager">Lock manager for write serialization (null = no locking).</param>
        /// <param name="defaultIsolationLevel">Default isolation level for transactions.</param>
        /// <param name="waitQueueOptions">Options for transaction wait queue (null = default options).</param>
        /// <param name="ownsStore">If true, disposes the store when this is disposed.</param>
        public MvccTransactionalStore(
            MvccKeyValueStore mvccStore,
            TransactionTimestampManager timestampManager,
            LockManager? lockManager,
            WitIsolationLevel defaultIsolationLevel,
            TransactionWaitQueueOptions? waitQueueOptions,
            bool ownsStore = true)
        {
            m_mvccStore = mvccStore ?? throw new ArgumentNullException(nameof(mvccStore));
            m_timestampManager = timestampManager ?? throw new ArgumentNullException(nameof(timestampManager));
            m_lockManager = lockManager;
            m_rowLockManager = new RowLockManager();
            m_deadlockDetector = new DeadlockDetector(m_rowLockManager, DeadlockVictimStrategy.Youngest);
            m_waitQueue = new TransactionWaitQueue(waitQueueOptions ?? new TransactionWaitQueueOptions());
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
        public ITransaction BeginTransaction(WitIsolationLevel isolationLevel)
        {
            return BeginTransaction(isolationLevel, TransactionPriority.Normal);
        }

        /// <summary>
        /// Begins a new transaction with the specified isolation level and priority.
        /// </summary>
        /// <param name="isolationLevel">The isolation level for the transaction.</param>
        /// <param name="priority">The priority for the transaction in wait queue.</param>
        /// <returns>A new transaction.</returns>
        public ITransaction BeginTransaction(WitIsolationLevel isolationLevel, TransactionPriority priority)
        {
            ThrowIfDisposed();
            ValidateIsolationLevel(isolationLevel);

            lock (m_txLock)
            {
                // For MVCC, read-only transactions don't need write locks
                // Write transactions get locks at commit time (optimistic)
                // For now, we don't acquire locks at begin - only for writes
                
                // A snapshot must come from the published watermark, not the raw counter: commit
                // timestamps share that counter and are allocated before the writes are
                // installed, so a snapshot taken from it can sit above a half-applied commit.
                var snapshotTimestamp = m_timestampManager.StableTimestamp;
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
        public ValueTask<ITransaction> BeginTransactionAsync(WitIsolationLevel isolationLevel, CancellationToken cancellationToken = default)
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
                // A snapshot must come from the published watermark, not the raw counter: commit
                // timestamps share that counter and are allocated before the writes are
                // installed, so a snapshot taken from it can sit above a half-applied commit.
                var snapshotTimestamp = m_timestampManager.StableTimestamp;
                var txId = m_nextTransactionId++;

                var tx = new MvccTransaction(
                    this,
                    txId,
                    snapshotTimestamp,
                    m_timestampManager,
                    lockHandle: null,
                    WitIsolationLevel.Snapshot);

                tx.SetReadOnly();
                m_activeTransactions.Add(tx);
                return tx;
            }
        }

        #endregion

        #region Transaction Wait Queue

        /// <summary>
        /// Waits in the transaction queue until signaled or timeout.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <param name="isWriter">Whether this is a write transaction.</param>
        /// <param name="priority">Priority level.</param>
        /// <param name="timeout">Optional timeout.</param>
        /// <returns>True if signaled, false if timed out.</returns>
        public bool WaitInQueue(
            long transactionId, 
            bool isWriter, 
            TransactionPriority priority = TransactionPriority.Normal,
            TimeSpan? timeout = null)
        {
            ThrowIfDisposed();
            return m_waitQueue.EnqueueAndWait(transactionId, isWriter, priority, timeout);
        }

        /// <summary>
        /// Waits in the transaction queue asynchronously until signaled or timeout.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <param name="isWriter">Whether this is a write transaction.</param>
        /// <param name="priority">Priority level.</param>
        /// <param name="timeout">Optional timeout.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if signaled, false if timed out.</returns>
        public Task<bool> WaitInQueueAsync(
            long transactionId, 
            bool isWriter, 
            TransactionPriority priority = TransactionPriority.Normal,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return m_waitQueue.EnqueueAndWaitAsync(transactionId, isWriter, priority, timeout, cancellationToken);
        }

        /// <summary>
        /// Signals the next waiting transaction to proceed.
        /// </summary>
        /// <returns>The transaction ID that was signaled, or null if queue is empty.</returns>
        public long? SignalNextWaiting()
        {
            ThrowIfDisposed();
            return m_waitQueue.SignalNext();
        }

        /// <summary>
        /// Signals a specific transaction to proceed.
        /// </summary>
        /// <param name="transactionId">The transaction ID to signal.</param>
        /// <returns>True if the transaction was found and signaled.</returns>
        public bool SignalTransaction(long transactionId)
        {
            ThrowIfDisposed();
            return m_waitQueue.Signal(transactionId);
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
        public IKeyValueStore Inner => m_mvccStore;

        /// <inheritdoc/>
        public void Flush()
        {
            ThrowIfDisposed();
            m_mvccStore.Flush();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Forwarded rather than left to the interface default, which would call <see cref="Flush"/>.
        /// This is the default transaction model, so an unforwarded checkpoint is one every ADO.NET
        /// and EF Core consumer gets: measured 2026-08-07, a checkpoint asked of an LSM database left
        /// the memtable exactly where it was.
        /// </remarks>
        public void Checkpoint()
        {
            ThrowIfDisposed();
            m_mvccStore.Checkpoint();
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
            
            // Signal next waiting transaction after commit
            SignalNextWaiting();
        }

        /// <inheritdoc/>
        public void RollbackTransaction(long transactionId)
        {
            ThrowIfDisposed();
            m_mvccStore.RollbackTransaction(transactionId);
            
            // Signal next waiting transaction after rollback
            SignalNextWaiting();
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
            
            // Remove from wait queue if waiting
            m_waitQueue.Dequeue(tx.TransactionId);
        }

        #endregion

        #region Garbage Collection

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

        /// <summary>
        /// Creates a background garbage collector for this store.
        /// The caller is responsible for disposing the returned collector.
        /// </summary>
        /// <param name="options">Configuration options for the garbage collector.</param>
        /// <returns>A new background garbage collector.</returns>
        public MvccGarbageCollector CreateBackgroundGarbageCollector(MvccGarbageCollectorOptions? options = null)
        {
            ThrowIfDisposed();
            return new MvccGarbageCollector(m_mvccStore, m_timestampManager, options);
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        private static void ValidateIsolationLevel(WitIsolationLevel isolationLevel)
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

            // Signal all waiting transactions
            m_waitQueue.SignalAll();

            // Rollback any active transactions
            lock (m_txLock)
            {
                foreach (var tx in m_activeTransactions.ToList())
                {
                    try { tx.Rollback(); } catch { }
                }
            }

            // Flush MVCC store to ensure all data is persisted before disposing
            try
            {
                m_mvccStore.Flush();
            }
            catch
            {
                // Best effort - don't fail dispose on flush errors
            }

            m_waitQueue.Dispose();
            m_deadlockDetector.Dispose();
            m_rowLockManager.Dispose();
            m_mvccStore.Dispose();
            m_lockManager?.Dispose();
        }

        /// <summary>
        /// The same shutdown without a synchronous storage call, so the default transaction model can
        /// close a database on a storage that has none.
        /// </summary>
        /// <remarks>
        /// <see cref="TransactionalStore"/> - the lock-based half - has had an asynchronous close since
        /// it was written; this one had none, so <c>WitDatabase.DisposeAsync</c> fell back to the
        /// synchronous close for every database in the <b>default</b> configuration. The two must not
        /// diverge, which is why the probe asserts both.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed) return;
            m_disposed = true;

            m_waitQueue.SignalAll();

            lock (m_txLock)
            {
                foreach (var tx in m_activeTransactions.ToList())
                {
                    try { tx.Rollback(); } catch { }
                }
            }

            try
            {
                await m_mvccStore.FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best effort - don't fail dispose on flush errors, exactly as the synchronous close.
            }

            m_waitQueue.Dispose();
            m_deadlockDetector.Dispose();
            m_rowLockManager.Dispose();

            if (m_mvccStore is IAsyncDisposable asyncStore)
                await asyncStore.DisposeAsync().ConfigureAwait(false);
            else
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
        /// Gets the number of transactions waiting in the queue.
        /// </summary>
        public int WaitingTransactionCount => m_waitQueue.WaitingCount;

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

        /// <summary>
        /// Gets the transaction wait queue.
        /// </summary>
        public TransactionWaitQueue WaitQueue => m_waitQueue;

        /// <inheritdoc/>
        public string ProviderKey => PROVIDER_KEY;

        #endregion
    }
}
