using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Transactions
{
    /// <summary>
    /// MVCC transaction implementation with snapshot isolation.
    /// Provides consistent reads from a point-in-time snapshot and 
    /// detects write-write conflicts at commit time.
    /// </summary>
    public sealed class MvccTransaction : IMvccTransaction
    {
        #region Constants

        private static readonly TimeSpan ASYNC_DISPOSE_TIMEOUT = TimeSpan.FromSeconds(5);

        #endregion

        #region Fields

        private readonly MvccTransactionalStore m_store;
        private readonly ITransactionTimestampManager m_timestampManager;
        private readonly IDisposable? m_syncLockHandle;
        private readonly IAsyncDisposable? m_asyncLockHandle;
        private readonly Dictionary<byte[], (byte[]? NewValue, byte[]? OldValue)> m_changes;
        private readonly HashSet<byte[]> m_deletedKeys;
        private readonly HashSet<byte[]> m_readSet;
        private readonly HashSet<byte[]> m_writeSet;
        private readonly HashSet<byte[]> m_lockedKeys;
        private readonly ByteArrayComparer m_comparer;
        private readonly List<Savepoint> m_savepoints;
        private bool m_isReadOnly;

        #endregion

        #region Constructors

        internal MvccTransaction(
            MvccTransactionalStore store,
            long transactionId,
            long snapshotTimestamp,
            ITransactionTimestampManager timestampManager,
            IDisposable? lockHandle = null,
            IsolationLevel isolationLevel = IsolationLevel.Snapshot)
        {
            m_store = store;
            m_timestampManager = timestampManager;
            m_syncLockHandle = lockHandle;
            m_asyncLockHandle = null;
            
            TransactionId = transactionId;
            SnapshotTimestamp = snapshotTimestamp;
            StartTimestamp = snapshotTimestamp;
            IsolationLevel = isolationLevel;
            State = TransactionState.Active;

            m_comparer = ByteArrayComparer.Default;
            m_changes = new Dictionary<byte[], (byte[]?, byte[]?)>(m_comparer);
            m_deletedKeys = new HashSet<byte[]>(m_comparer);
            m_readSet = new HashSet<byte[]>(m_comparer);
            m_writeSet = new HashSet<byte[]>(m_comparer);
            m_lockedKeys = new HashSet<byte[]>(m_comparer);
            m_savepoints = new List<Savepoint>();
            m_isReadOnly = false;

            m_timestampManager.RegisterTransaction(transactionId, snapshotTimestamp);
        }

        internal MvccTransaction(
            MvccTransactionalStore store,
            long transactionId,
            long snapshotTimestamp,
            ITransactionTimestampManager timestampManager,
            IAsyncDisposable? asyncLockHandle,
            IsolationLevel isolationLevel = IsolationLevel.Snapshot)
        {
            m_store = store;
            m_timestampManager = timestampManager;
            m_syncLockHandle = null;
            m_asyncLockHandle = asyncLockHandle;
            
            TransactionId = transactionId;
            SnapshotTimestamp = snapshotTimestamp;
            StartTimestamp = snapshotTimestamp;
            IsolationLevel = isolationLevel;
            State = TransactionState.Active;

            m_comparer = ByteArrayComparer.Default;
            m_changes = new Dictionary<byte[], (byte[]?, byte[]?)>(m_comparer);
            m_deletedKeys = new HashSet<byte[]>(m_comparer);
            m_readSet = new HashSet<byte[]>(m_comparer);
            m_writeSet = new HashSet<byte[]>(m_comparer);
            m_lockedKeys = new HashSet<byte[]>(m_comparer);
            m_savepoints = new List<Savepoint>();
            m_isReadOnly = false;

            m_timestampManager.RegisterTransaction(transactionId, snapshotTimestamp);
        }

        #endregion

        #region Get

        /// <inheritdoc/>
        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            ThrowIfNotActive();

            var keyArray = key.ToArray();

            // Track read for conflict detection (RepeatableRead, Serializable)
            if (IsolationLevel == IsolationLevel.RepeatableRead || 
                IsolationLevel == IsolationLevel.Serializable)
            {
                m_readSet.Add(keyArray);
            }

            // Check local changes first (always see our own writes)
            if (m_changes.TryGetValue(keyArray, out var value))
            {
                return value.NewValue;
            }

            if (m_deletedKeys.Contains(keyArray))
            {
                return null;
            }

            // Read behavior depends on isolation level
            return IsolationLevel switch
            {
                IsolationLevel.ReadUncommitted => GetReadUncommitted(key),
                IsolationLevel.ReadCommitted => GetReadCommitted(key),
                IsolationLevel.RepeatableRead => GetSnapshot(key),
                IsolationLevel.Serializable => GetSnapshot(key),
                IsolationLevel.Snapshot => GetSnapshot(key),
                _ => GetSnapshot(key)
            };
        }

        /// <summary>
        /// Read Uncommitted: Can see uncommitted changes from other transactions.
        /// </summary>
        private byte[]? GetReadUncommitted(ReadOnlySpan<byte> key)
        {
            // Get the latest version regardless of commit status
            var currentTimestamp = m_timestampManager.CurrentTimestamp;
            return m_store.GetAsOf(key, currentTimestamp, TransactionId);
        }

        /// <summary>
        /// Read Committed: Only sees committed data, but no snapshot (reads current committed state).
        /// Each read sees the latest committed data at the time of the read.
        /// </summary>
        private byte[]? GetReadCommitted(ReadOnlySpan<byte> key)
        {
            // Get the latest committed version at current time
            var currentTimestamp = m_timestampManager.CurrentTimestamp;
            return m_store.GetAsOf(key, currentTimestamp, transactionId: 0);
        }

        /// <summary>
        /// Snapshot/RepeatableRead/Serializable: Reads from a consistent snapshot.
        /// </summary>
        private byte[]? GetSnapshot(ReadOnlySpan<byte> key)
        {
            return m_store.GetAsOf(key, SnapshotTimestamp, TransactionId);
        }

        /// <inheritdoc/>
        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Get(key));
        }

        #endregion

        #region Row-Level Locking (FOR UPDATE / FOR SHARE)

        /// <inheritdoc/>
        public byte[]? GetForUpdate(ReadOnlySpan<byte> key, RowLockWaitMode waitMode = RowLockWaitMode.Wait, TimeSpan? timeout = null)
        {
            return GetWithLock(key, RowLockMode.Exclusive, waitMode, timeout);
        }

        /// <inheritdoc/>
        public byte[]? GetForShare(ReadOnlySpan<byte> key, RowLockWaitMode waitMode = RowLockWaitMode.Wait, TimeSpan? timeout = null)
        {
            return GetWithLock(key, RowLockMode.Shared, waitMode, timeout);
        }

        /// <inheritdoc/>
        public async ValueTask<byte[]?> GetForUpdateAsync(
            byte[] key, 
            RowLockWaitMode waitMode = RowLockWaitMode.Wait, 
            TimeSpan? timeout = null, 
            CancellationToken cancellationToken = default)
        {
            return await GetWithLockAsync(key, RowLockMode.Exclusive, waitMode, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<byte[]?> GetForShareAsync(
            byte[] key, 
            RowLockWaitMode waitMode = RowLockWaitMode.Wait, 
            TimeSpan? timeout = null, 
            CancellationToken cancellationToken = default)
        {
            return await GetWithLockAsync(key, RowLockMode.Shared, waitMode, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        private byte[]? GetWithLock(ReadOnlySpan<byte> key, RowLockMode lockMode, RowLockWaitMode waitMode, TimeSpan? timeout)
        {
            ThrowIfNotActive();

            var keyArray = key.ToArray();

            // Check if we already have a lock on this key
            if (!m_lockedKeys.Contains(keyArray))
            {
                var lockManager = m_store.RowLockManager;
                var deadlockDetector = m_store.DeadlockDetector;

                // Check which transactions currently hold locks on this key
                if (waitMode == RowLockWaitMode.Wait && lockManager.IsLocked(key))
                {
                    // Find holders and check for potential deadlock
                    // This is a simplified check - full implementation would track all holders
                }

                var request = new RowLockRequest(keyArray, TransactionId, lockMode, waitMode, timeout);

                try
                {
                    var handle = lockManager.AcquireLock(request);
                    if (handle == null)
                    {
                        // SKIP LOCKED - return null to indicate row was skipped
                        return null;
                    }

                    m_lockedKeys.Add(keyArray);
                }
                catch (RowLockException)
                {
                    // NOWAIT - lock could not be acquired
                    throw;
                }
            }

            // Now read the value
            return Get(key);
        }

        private async ValueTask<byte[]?> GetWithLockAsync(
            byte[] key, 
            RowLockMode lockMode, 
            RowLockWaitMode waitMode, 
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            ThrowIfNotActive();
            cancellationToken.ThrowIfCancellationRequested();

            // Check if we already have a lock on this key
            if (!m_lockedKeys.Contains(key))
            {
                var lockManager = m_store.RowLockManager;

                var request = new RowLockRequest(key, TransactionId, lockMode, waitMode, timeout);

                try
                {
                    var handle = await lockManager.AcquireLockAsync(request, cancellationToken)
                        .ConfigureAwait(false);

                    if (handle == null)
                    {
                        // SKIP LOCKED - return null to indicate row was skipped
                        return null;
                    }

                    m_lockedKeys.Add(key);
                }
                catch (RowLockException)
                {
                    // NOWAIT - lock could not be acquired
                    throw;
                }
            }

            // Now read the value
            return Get(key);
        }

        #endregion

        #region Put

        /// <inheritdoc/>
        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            ThrowIfNotActive();
            ThrowIfReadOnly();

            var keyArray = key.ToArray();
            var valueArray = value.ToArray();

            byte[]? oldValue = null;
            if (!m_changes.TryGetValue(keyArray, out var existing))
            {
                oldValue = m_store.GetAsOf(key, SnapshotTimestamp, TransactionId);
            }
            else
            {
                oldValue = existing.OldValue;
            }

            m_deletedKeys.Remove(keyArray);
            m_changes[keyArray] = (valueArray, oldValue);
            m_writeSet.Add(keyArray);
        }

        /// <inheritdoc/>
        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Put(key, value);
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Delete

        /// <inheritdoc/>
        public bool Delete(ReadOnlySpan<byte> key)
        {
            ThrowIfNotActive();
            ThrowIfReadOnly();

            var keyArray = key.ToArray();

            byte[]? oldValue = null;
            if (!m_changes.TryGetValue(keyArray, out var existing))
            {
                oldValue = m_store.GetAsOf(key, SnapshotTimestamp, TransactionId);
            }
            else
            {
                oldValue = existing.OldValue;
            }

            bool exists = oldValue != null || m_changes.ContainsKey(keyArray);

            m_changes[keyArray] = (null, oldValue);
            m_deletedKeys.Add(keyArray);
            m_writeSet.Add(keyArray);

            return exists;
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Delete(key));
        }

        #endregion

        #region Savepoints

        /// <inheritdoc/>
        public void CreateSavepoint(string name)
        {
            ThrowIfNotActive();
            ValidateSavepointName(name);

            if (m_savepoints.Any(sp => sp.Name == name))
                throw new ArgumentException($"Savepoint '{name}' already exists.", nameof(name));

            var savepoint = new Savepoint(name, m_changes, m_deletedKeys);
            m_savepoints.Add(savepoint);
        }

        /// <inheritdoc/>
        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateSavepoint(name);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void RollbackToSavepoint(string name)
        {
            ThrowIfNotActive();
            ValidateSavepointName(name);

            var index = m_savepoints.FindIndex(sp => sp.Name == name);
            if (index < 0)
                throw new ArgumentException($"Savepoint '{name}' does not exist.", nameof(name));

            var savepoint = m_savepoints[index];
            savepoint.Restore(m_changes, m_deletedKeys);

            // Remove savepoints created after this one
            m_savepoints.RemoveRange(index + 1, m_savepoints.Count - index - 1);

            // Rebuild write set from remaining changes
            m_writeSet.Clear();
            foreach (var key in m_changes.Keys)
            {
                m_writeSet.Add(key);
            }
        }

        /// <inheritdoc/>
        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RollbackToSavepoint(name);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void ReleaseSavepoint(string name)
        {
            ThrowIfNotActive();
            ValidateSavepointName(name);

            var index = m_savepoints.FindIndex(sp => sp.Name == name);
            if (index < 0)
                throw new ArgumentException($"Savepoint '{name}' does not exist.", nameof(name));

            m_savepoints.RemoveRange(index, m_savepoints.Count - index);
        }

        /// <inheritdoc/>
        public ValueTask ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseSavepoint(name);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public bool HasSavepoint(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return m_savepoints.Any(sp => sp.Name == name);
        }

        private static void ValidateSavepointName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name), "Savepoint name cannot be null or empty.");
        }

        #endregion

        #region Conflict Detection

        /// <inheritdoc/>
        public bool HasWriteConflict()
        {
            if (m_writeSet.Count == 0 && 
                (IsolationLevel != IsolationLevel.RepeatableRead && IsolationLevel != IsolationLevel.Serializable))
            {
                return false;
            }

            // Check if any of our written keys have been modified since our snapshot
            foreach (var key in m_writeSet)
            {
                if (m_store.HasConflict(key, SnapshotTimestamp, TransactionId))
                {
                    return true;
                }
            }

            // For RepeatableRead and Serializable: also check read set
            // If any key we read has been modified since our snapshot, that's a conflict
            if (IsolationLevel == IsolationLevel.RepeatableRead || 
                IsolationLevel == IsolationLevel.Serializable)
            {
                foreach (var key in m_readSet)
                {
                    // Skip keys we've written (already checked)
                    if (m_writeSet.Contains(key))
                        continue;

                    if (m_store.HasConflict(key, SnapshotTimestamp, TransactionId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if any key in the read set has been modified by another transaction.
        /// Used for RepeatableRead and Serializable isolation levels.
        /// </summary>
        public bool HasReadConflict()
        {
            if (m_readSet.Count == 0)
                return false;

            foreach (var key in m_readSet)
            {
                if (m_store.HasConflict(key, SnapshotTimestamp, TransactionId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public void SetReadOnly()
        {
            ThrowIfNotActive();

            if (m_writeSet.Count > 0)
            {
                throw new InvalidOperationException(
                    "Cannot set transaction to read-only after writes have been performed.");
            }

            m_isReadOnly = true;
        }

        #endregion

        #region Commit

        /// <inheritdoc/>
        public void Commit()
        {
            ThrowIfNotActive();

            try
            {
                // Check for conflicts based on isolation level
                ValidateNoConflicts();

                // Get commit timestamp
                var commitTimestamp = m_timestampManager.GetNextTimestamp();

                // Apply changes to MVCC store
                foreach (var (key, (newValue, _)) in m_changes)
                {
                    if (newValue == null)
                    {
                        m_store.DeleteVersion(key, commitTimestamp, TransactionId);
                    }
                    else
                    {
                        m_store.PutVersion(key, newValue, commitTimestamp, TransactionId);
                    }
                }

                // Mark transaction as committed in MVCC store
                m_store.CommitTransaction(TransactionId, commitTimestamp);
                m_timestampManager.MarkCommitted(TransactionId, commitTimestamp);

                State = TransactionState.Committed;
            }
            finally
            {
                m_savepoints.Clear();
                ReleaseLocks();
                m_store.NotifyTransactionComplete(this);
            }
        }

        /// <inheritdoc/>
        public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfNotActive();

            try
            {
                ValidateNoConflicts();

                var commitTimestamp = m_timestampManager.GetNextTimestamp();

                foreach (var (key, (newValue, _)) in m_changes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (newValue == null)
                    {
                        m_store.DeleteVersion(key, commitTimestamp, TransactionId);
                    }
                    else
                    {
                        m_store.PutVersion(key, newValue, commitTimestamp, TransactionId);
                    }
                }

                m_store.CommitTransaction(TransactionId, commitTimestamp);
                m_timestampManager.MarkCommitted(TransactionId, commitTimestamp);

                State = TransactionState.Committed;
            }
            finally
            {
                m_savepoints.Clear();
                await ReleaseLocksAsync().ConfigureAwait(false);
                m_store.NotifyTransactionComplete(this);
            }
        }

        private void ValidateNoConflicts()
        {
            // ReadUncommitted and ReadCommitted don't detect conflicts on read set
            if (IsolationLevel == IsolationLevel.ReadUncommitted || 
                IsolationLevel == IsolationLevel.ReadCommitted)
            {
                // Only check write-write conflicts
                if (m_writeSet.Count > 0)
                {
                    foreach (var key in m_writeSet)
                    {
                        if (m_store.HasConflict(key, SnapshotTimestamp, TransactionId))
                        {
                            throw new InvalidOperationException(
                                "Transaction cannot commit due to write-write conflict. " +
                                "Another transaction has modified data that this transaction also modified.");
                        }
                    }
                }
                return;
            }

            // Snapshot, RepeatableRead, Serializable - check write conflicts
            if (HasWriteConflict())
            {
                if (IsolationLevel == IsolationLevel.RepeatableRead || 
                    IsolationLevel == IsolationLevel.Serializable)
                {
                    throw new InvalidOperationException(
                        $"Transaction cannot commit due to serialization failure ({IsolationLevel}). " +
                        "Data read or written by this transaction has been modified by another transaction.");
                }
                
                throw new InvalidOperationException(
                    "Transaction cannot commit due to write-write conflict. " +
                    "Another transaction has modified data that this transaction also modified.");
            }
        }

        #endregion

        #region Rollback

        /// <inheritdoc/>
        public void Rollback()
        {
            if (State != TransactionState.Active)
                return; // Already completed, nothing to do

            RollbackInternal();
        }

        /// <inheritdoc/>
        public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (State != TransactionState.Active)
                return; // Already completed, nothing to do

            await RollbackInternalAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Internal rollback implementation.
        /// </summary>
        private void RollbackInternal()
        {
            // Set state first so even if cleanup fails, transaction is marked as rolled back
            State = TransactionState.RolledBack;
            
            try
            {
                m_changes.Clear();
                m_deletedKeys.Clear();
                m_savepoints.Clear();
                m_readSet.Clear();
                m_writeSet.Clear();

                m_store.RollbackTransaction(TransactionId);
                m_timestampManager.UnregisterTransaction(TransactionId);
            }
            finally
            {
                ReleaseLocks();
                m_store.NotifyTransactionComplete(this);
            }
        }

        /// <summary>
        /// Internal async rollback implementation.
        /// </summary>
        private async ValueTask RollbackInternalAsync()
        {
            // Set state first so even if cleanup fails, transaction is marked as rolled back
            State = TransactionState.RolledBack;
            
            try
            {
                m_changes.Clear();
                m_deletedKeys.Clear();
                m_savepoints.Clear();
                m_readSet.Clear();
                m_writeSet.Clear();

                m_store.RollbackTransaction(TransactionId);
                m_timestampManager.UnregisterTransaction(TransactionId);
            }
            finally
            {
                await ReleaseLocksAsync().ConfigureAwait(false);
                m_store.NotifyTransactionComplete(this);
            }
        }

        #endregion

        #region Tools

        private void ThrowIfNotActive()
        {
            if (State != TransactionState.Active)
                throw new InvalidOperationException($"Transaction is {State}, not Active");
        }

        private void ThrowIfReadOnly()
        {
            if (m_isReadOnly)
                throw new InvalidOperationException("Transaction is read-only and cannot perform writes.");
        }

        private void ReleaseLocks()
        {
            // Release row-level locks
            if (m_lockedKeys.Count > 0)
            {
                m_store.RowLockManager.ReleaseAllLocks(TransactionId);
                m_store.DeadlockDetector.TransactionCompleted(TransactionId);
                m_lockedKeys.Clear();
            }

            // Release database-level locks
            m_syncLockHandle?.Dispose();

            if (m_asyncLockHandle != null)
            {
                try
                {
                    var disposeTask = Task.Run(async () =>
                        await m_asyncLockHandle.DisposeAsync().ConfigureAwait(false));

                    disposeTask.Wait(ASYNC_DISPOSE_TIMEOUT);
                }
                catch (AggregateException)
                {
                    // Ignore dispose exceptions
                }
            }
        }

        private async ValueTask ReleaseLocksAsync()
        {
            // Release row-level locks
            if (m_lockedKeys.Count > 0)
            {
                m_store.RowLockManager.ReleaseAllLocks(TransactionId);
                m_store.DeadlockDetector.TransactionCompleted(TransactionId);
                m_lockedKeys.Clear();
            }

            // Release database-level locks
            m_syncLockHandle?.Dispose();

            if (m_asyncLockHandle != null)
            {
                await m_asyncLockHandle.DisposeAsync().ConfigureAwait(false);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (State == TransactionState.Active)
            {
                Rollback();
            }
        }

        #endregion

        #region IAsyncDisposable

        public async ValueTask DisposeAsync()
        {
            if (State == TransactionState.Active)
            {
                await RollbackAsync().ConfigureAwait(false);
            }
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public TransactionState State { get; private set; }

        /// <inheritdoc/>
        public long TransactionId { get; }

        /// <inheritdoc/>
        public IsolationLevel IsolationLevel { get; }

        /// <inheritdoc/>
        public long SnapshotTimestamp { get; }

        /// <inheritdoc/>
        public long StartTimestamp { get; }

        /// <inheritdoc/>
        public bool IsReadOnly => m_isReadOnly;

        /// <inheritdoc/>
        public IReadOnlySet<byte[]> ReadSet => m_readSet;

        /// <inheritdoc/>
        public IReadOnlySet<byte[]> WriteSet => m_writeSet;

        /// <inheritdoc/>
        public IReadOnlyList<string> Savepoints => m_savepoints.Select(sp => sp.Name).ToList().AsReadOnly();

        #endregion
    }
}
