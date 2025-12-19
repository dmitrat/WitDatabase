using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Transactions
{
    /// <summary>
    /// Transaction implementation with change tracking.
    /// Buffers all changes until Commit() or Rollback() is called.
    /// Holds a write lock for the duration of the transaction if locking is enabled.
    /// </summary>
    public sealed class Transaction : ITransaction
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

        #endregion

        #region Constructors

        internal Transaction(TransactionalStore store, long transactionId, ITransactionJournal? journal, IDisposable? lockHandle = null)
        {
            m_store = store;
            m_journal = journal;
            m_syncLockHandle = lockHandle;
            m_asyncLockHandle = null;
            TransactionId = transactionId;
            State = TransactionState.Active;

            m_comparer = ByteArrayComparer.Default;
            m_changes = new Dictionary<byte[], (byte[]?, byte[]?)>(m_comparer);
            m_deletedKeys = new HashSet<byte[]>(m_comparer);

            m_journal?.BeginTransaction(transactionId);
        }

        internal Transaction(TransactionalStore store, long transactionId, ITransactionJournal? journal, IAsyncDisposable? asyncLockHandle)
        {
            m_store = store;
            m_journal = journal;
            m_syncLockHandle = null;
            m_asyncLockHandle = asyncLockHandle;
            TransactionId = transactionId;
            State = TransactionState.Active;

            m_comparer = ByteArrayComparer.Default;
            m_changes = new Dictionary<byte[], (byte[]?, byte[]?)>(m_comparer);
            m_deletedKeys = new HashSet<byte[]>(m_comparer);

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
        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Get(key));
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
                        m_store.DeleteFromStore(key);
                    }
                    else
                    {
                        m_store.PutToStore(key, newValue);
                    }
                }

                await m_store.FlushAsync(cancellationToken).ConfigureAwait(false);
                m_journal?.CommitTransaction(TransactionId);
                State = TransactionState.Committed;
            }
            finally
            {
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

        #endregion

    }
}
