using System.Runtime.CompilerServices;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Transactions
{
    /// <summary>
    /// Transaction implementation with change tracking and savepoint support.
    /// Buffers all changes until Commit() or Rollback() is called.
    /// Holds a write lock for the duration of the transaction if locking is enabled.
    /// </summary>
    public sealed class Transaction : ITransactionWithSavepoints
    {
        #region Constants

        private static readonly TimeSpan ASYNC_DISPOSE_TIMEOUT = TimeSpan.FromSeconds(5);

        #endregion

        #region Fields

        private readonly TransactionalStore m_store;
        private readonly ITransactionJournal? m_journal;
        private readonly IDisposable? m_syncLockHandle;
        private readonly IAsyncDisposable? m_asyncLockHandle;
        private readonly Dictionary<byte[], (byte[]? NewValue, byte[]? OldValue)> m_changes;
        private readonly HashSet<byte[]> m_deletedKeys;
        private readonly ByteArrayComparer m_comparer;
        private readonly List<Savepoint> m_savepoints;

        #endregion

        #region Constructors

        internal Transaction(
            TransactionalStore store, 
            long transactionId, 
            ITransactionJournal? journal, 
            IDisposable? lockHandle = null,
            WitIsolationLevel isolationLevel = WitIsolationLevel.ReadCommitted)
        {
            m_store = store;
            m_journal = journal;
            m_syncLockHandle = lockHandle;
            m_asyncLockHandle = null;
            TransactionId = transactionId;
            IsolationLevel = isolationLevel;
            State = TransactionState.Active;

            m_comparer = ByteArrayComparer.Default;
            m_changes = new Dictionary<byte[], (byte[]?, byte[]?)>(m_comparer);
            m_deletedKeys = new HashSet<byte[]>(m_comparer);
            m_savepoints = new List<Savepoint>();

            m_journal?.BeginTransaction(transactionId);
        }

        internal Transaction(
            TransactionalStore store, 
            long transactionId, 
            ITransactionJournal? journal, 
            IAsyncDisposable? asyncLockHandle,
            WitIsolationLevel isolationLevel = WitIsolationLevel.ReadCommitted)
        {
            m_store = store;
            m_journal = journal;
            m_syncLockHandle = null;
            m_asyncLockHandle = asyncLockHandle;
            TransactionId = transactionId;
            IsolationLevel = isolationLevel;
            State = TransactionState.Active;

            m_comparer = ByteArrayComparer.Default;
            m_changes = new Dictionary<byte[], (byte[]?, byte[]?)>(m_comparer);
            m_deletedKeys = new HashSet<byte[]>(m_comparer);
            m_savepoints = new List<Savepoint>();

            m_journal?.BeginTransaction(transactionId);
        }

        #endregion
        
        #region Get

        /// <inheritdoc/>
        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            ThrowIfNotActive();

            var keyArray = key.ToArray();

            if (m_changes.TryGetValue(keyArray, out var value))
            {
                return value.NewValue;
            }

            if (m_deletedKeys.Contains(keyArray))
            {
                return null;
            }

            return m_store.GetFromStore(key);
        }

        /// <inheritdoc/>
        public async ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            ThrowIfNotActive();
            cancellationToken.ThrowIfCancellationRequested();

            if (m_changes.TryGetValue(key, out var value))
            {
                return value.NewValue;
            }

            if (m_deletedKeys.Contains(key))
            {
                return null;
            }

            return await m_store.GetFromStoreAsync(key, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Put

        /// <inheritdoc/>
        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            ThrowIfNotActive();

            var keyArray = key.ToArray();
            var valueArray = value.ToArray();

            byte[]? oldValue = null;
            if (!m_changes.TryGetValue(keyArray, out var existing))
            {
                oldValue = m_store.GetFromStore(key);
            }
            else
            {
                oldValue = existing.OldValue;
            }

            m_deletedKeys.Remove(keyArray);
            m_changes[keyArray] = (valueArray, oldValue);
            m_journal?.LogPut(TransactionId, key, value, oldValue ?? ReadOnlySpan<byte>.Empty);
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

            var keyArray = key.ToArray();

            byte[]? oldValue = null;
            if (!m_changes.TryGetValue(keyArray, out var existing))
            {
                oldValue = m_store.GetFromStore(key);
            }
            else
            {
                oldValue = existing.OldValue;
            }

            bool exists = oldValue != null || m_changes.ContainsKey(keyArray);

            m_changes[keyArray] = (null, oldValue);
            m_deletedKeys.Add(keyArray);
            m_journal?.LogDelete(TransactionId, key, oldValue ?? ReadOnlySpan<byte>.Empty);

            return exists;
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Delete(key));
        }

        #endregion

        #region Scan

        /// <inheritdoc/>
        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
        {
            ThrowIfNotActive();

            // Get results from underlying store (no lock needed - we already hold write lock)
            var storeResults = m_store.ScanFromStore(startKey, endKey);

            // Merge store results with local changes
            return MergeScanResults(storeResults, startKey, endKey);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
            byte[]? startKey, 
            byte[]? endKey, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfNotActive();
            cancellationToken.ThrowIfCancellationRequested();

            // Get results from underlying store (no lock needed - we already hold write lock)
            var storeResults = await m_store.ScanFromStoreAsync(startKey, endKey, cancellationToken)
                .ConfigureAwait(false);

            // Merge store results with local changes
            foreach (var item in MergeScanResults(storeResults, startKey, endKey))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }

        private IEnumerable<(byte[] Key, byte[] Value)> MergeScanResults(
            IEnumerable<(byte[] Key, byte[] Value)> storeResults,
            byte[]? startKey,
            byte[]? endKey)
        {
            // Create a sorted dictionary for merging
            var merged = new SortedDictionary<byte[], byte[]>(m_comparer);

            // Add store results, excluding deleted keys
            foreach (var (key, value) in storeResults)
            {
                if (!m_deletedKeys.Contains(key))
                {
                    merged[key] = value;
                }
            }

            // Apply local changes (inserts, updates, and filter by range)
            foreach (var (key, (newValue, _)) in m_changes)
            {
                // Check if key is in range
                if (!IsKeyInRange(key, startKey, endKey))
                    continue;

                if (newValue == null)
                {
                    // Delete
                    merged.Remove(key);
                }
                else
                {
                    // Insert or Update
                    merged[key] = newValue;
                }
            }

            return merged.Select(kvp => (kvp.Key, kvp.Value));
        }

        private bool IsKeyInRange(byte[] key, byte[]? startKey, byte[]? endKey)
        {
            if (startKey != null && m_comparer.Compare(key, startKey) < 0)
                return false;

            if (endKey != null && m_comparer.Compare(key, endKey) >= 0)
                return false;

            return true;
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

            // What the journal has already been told about each key. Put and Delete log eagerly, at
            // the moment they are called, while the store itself is not touched until Commit - so
            // restoring the in-memory change set is not enough on its own. Without the compensation
            // below, recovery replayed writes the transaction had discarded before it committed.
            var logged = new Dictionary<byte[], (byte[]? Value, byte[]? Original)>(m_comparer);
            foreach (var (key, (newValue, oldValue)) in m_changes)
                logged[key] = (newValue, oldValue);

            savepoint.Restore(m_changes, m_deletedKeys);

            if (m_journal != null)
                CompensateJournal(logged);

            // Remove all savepoints created after this one
            m_savepoints.RemoveRange(index + 1, m_savepoints.Count - index - 1);
        }

        /// <summary>
        /// Tells the journal what the rollback put back, for every key whose logged value no longer
        /// matches where the transaction now stands.
        /// </summary>
        /// <remarks>
        /// A compensating record rather than a new entry type on purpose: the replay visitor applies
        /// a transaction's operations in the order they were logged, so a later record simply wins,
        /// and no change to the log format or its version is needed.
        ///
        /// The subtle case is a key the transaction touched after the savepoint but that already
        /// existed in the store beforehand. Restoring drops it from the change set entirely, and the
        /// correct compensation is not a delete - it is a put of the value the store held when the
        /// transaction first touched it, which is exactly what OldValue records.
        /// </remarks>
        private void CompensateJournal(Dictionary<byte[], (byte[]? Value, byte[]? Original)> logged)
        {
            foreach (var (key, (loggedValue, original)) in logged)
            {
                var restored = m_changes.TryGetValue(key, out var entry)
                    ? entry.NewValue
                    : original;

                if (BytesEqual(loggedValue, restored))
                    continue;

                if (restored == null)
                    m_journal!.LogDelete(TransactionId, key, loggedValue ?? ReadOnlySpan<byte>.Empty);
                else
                    m_journal!.LogPut(TransactionId, key, restored, loggedValue ?? ReadOnlySpan<byte>.Empty);
            }
        }

        private static bool BytesEqual(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null)
                return false;

            return left.AsSpan().SequenceEqual(right);
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

            // Remove this savepoint and all savepoints created after it
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

        #region Commit

        /// <inheritdoc/>
        public void Commit()
        {
            ThrowIfNotActive();

            try
            {
                foreach (var (key, (newValue, _)) in m_changes)
                {
                    if (newValue == null)
                    {
                        m_store.DeleteFromStore(key);
                    }
                    else
                    {
                        m_store.PutToStore(key, newValue);
                    }
                }

                m_store.Flush();
                m_journal?.CommitTransaction(TransactionId);
                State = TransactionState.Committed;
            }
            finally
            {
                m_savepoints.Clear();
                ReleaseLocks();
            }
        }

        /// <inheritdoc/>
        public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfNotActive();

            try
            {
                foreach (var (key, (newValue, _)) in m_changes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (newValue == null)
                    {
                        await m_store.DeleteFromStoreAsync(key, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await m_store.PutToStoreAsync(key, newValue, cancellationToken).ConfigureAwait(false);
                    }
                }

                await m_store.FlushAsync(cancellationToken).ConfigureAwait(false);
                m_journal?.CommitTransaction(TransactionId);
                State = TransactionState.Committed;
            }
            finally
            {
                m_savepoints.Clear();
                await ReleaseLocksAsync().ConfigureAwait(false);
            }
        }

        #endregion

        #region Rollback

        /// <inheritdoc/>
        public void Rollback()
        {
            ThrowIfNotActive();

            try
            {
                m_changes.Clear();
                m_deletedKeys.Clear();
                m_savepoints.Clear();
                m_journal?.RollbackTransaction(TransactionId);
                State = TransactionState.RolledBack;
            }
            finally
            {
                ReleaseLocks();
            }
        }

        /// <inheritdoc/>
        public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfNotActive();

            try
            {
                m_changes.Clear();
                m_deletedKeys.Clear();
                m_savepoints.Clear();
                m_journal?.RollbackTransaction(TransactionId);
                State = TransactionState.RolledBack;
            }
            finally
            {
                await ReleaseLocksAsync().ConfigureAwait(false);
            }
        }

        #endregion

        #region Tools

        private void ThrowIfNotActive()
        {
            if (State != TransactionState.Active)
                throw new InvalidOperationException($"Transaction is {State}, not Active");
        }

        /// <summary>
        /// Releases locks synchronously. Safe to call from sync context.
        /// Uses Task.Run with timeout to avoid deadlocks when disposing async handles.
        /// </summary>
        private void ReleaseLocks()
        {
            m_store.NotifyTransactionComplete(this);
            m_syncLockHandle?.Dispose();
            
            // Safely dispose async handle from sync context
            if (m_asyncLockHandle != null)
            {
                try
                {
                    // Use Task.Run to avoid capturing sync context and potential deadlock
                    var disposeTask = Task.Run(async () => 
                        await m_asyncLockHandle.DisposeAsync().ConfigureAwait(false));
                    
                    // Wait with timeout to prevent hanging
                    if (!disposeTask.Wait(ASYNC_DISPOSE_TIMEOUT))
                    {
                        // Log warning but don't throw - lock will be released eventually
                        // or process will terminate
                    }
                }
                catch (AggregateException)
                {
                    // Ignore dispose exceptions - best effort cleanup
                }
            }
        }

        /// <summary>
        /// Releases locks asynchronously.
        /// </summary>
        private async ValueTask ReleaseLocksAsync()
        {
            m_store.NotifyTransactionComplete(this);
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
        public WitIsolationLevel IsolationLevel { get; }

        /// <inheritdoc/>
        public IReadOnlyList<string> Savepoints => m_savepoints.Select(sp => sp.Name).ToList().AsReadOnly();

        #endregion

    }
}
