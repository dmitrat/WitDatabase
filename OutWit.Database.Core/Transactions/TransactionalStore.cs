using System.Runtime.CompilerServices;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Transactions;

/// <summary>
/// Wraps an IKeyValueStore with transaction support.
/// Provides ACID guarantees through journaling and coordinated locking.
/// </summary>
public sealed class TransactionalStore : ITransactionalStore, IAsyncDisposable
{
    #region Constants

    /// <summary>
    /// Provider key for transactional store wrapper.
    /// </summary>
    public const string PROVIDER_KEY = "transactional";

    /// <summary>
    /// Default isolation level for transactions.
    /// </summary>
    public const IsolationLevel DEFAULT_ISOLATION_LEVEL = IsolationLevel.ReadCommitted;

    #endregion

    #region Fields

    private readonly IKeyValueStore m_store;
    private readonly ITransactionJournal? m_journal;
    private readonly LockManager? m_lockManager;
    private readonly bool m_ownsStore;
    private readonly object m_txLock = new();
    private readonly HashSet<Transaction> m_activeTransactions = new();
    private long m_nextTransactionId = 1;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a transactional store without durability or locking (for testing).
    /// </summary>
    public TransactionalStore(IKeyValueStore store, bool ownsStore = true)
        : this(store, journal: null, lockManager: null, ownsStore)
    {
    }

    /// <summary>
    /// Creates a transactional store with the specified journal (no locking).
    /// </summary>
    public TransactionalStore(IKeyValueStore store, ITransactionJournal? journal, bool ownsStore = true)
        : this(store, journal, lockManager: null, ownsStore)
    {
    }

    /// <summary>
    /// Creates a transactional store with the specified journal and lock manager.
    /// </summary>
    /// <param name="store">The underlying key-value store.</param>
    /// <param name="journal">The transaction journal for durability (null = no durability).</param>
    /// <param name="lockManager">The lock manager for concurrency control (null = no locking).</param>
    /// <param name="ownsStore">If true, disposes the store when this is disposed.</param>
    public TransactionalStore(IKeyValueStore store, ITransactionJournal? journal, LockManager? lockManager, bool ownsStore = true)
    {
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        m_journal = journal;
        m_lockManager = lockManager;
        m_ownsStore = ownsStore;

        // Recover any uncommitted transactions
        if (m_journal != null)
        {
            Recover();
        }
    }

    #endregion

    #region BeginTransaction

    /// <inheritdoc/>
    public ITransaction BeginTransaction()
    {
        return BeginTransaction(DEFAULT_ISOLATION_LEVEL);
    }

    /// <inheritdoc/>
    public ITransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        ThrowIfDisposed();
        ValidateIsolationLevel(isolationLevel);

        lock (m_txLock)
        {
            // Acquire write lock if lock manager is available
            IDisposable? lockHandle = null;
            if (m_lockManager != null)
            {
                lockHandle = m_lockManager.AcquireWriteLock();
            }

            try
            {
                var tx = new Transaction(this, m_nextTransactionId++, m_journal, lockHandle, isolationLevel);
                m_activeTransactions.Add(tx);
                return tx;
            }
            catch
            {
                lockHandle?.Dispose();
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return BeginTransactionAsync(DEFAULT_ISOLATION_LEVEL, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<ITransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateIsolationLevel(isolationLevel);

        IAsyncDisposable? lockHandle = null;
        if (m_lockManager != null)
        {
            lockHandle = await m_lockManager.AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            lock (m_txLock)
            {
                var tx = new Transaction(this, m_nextTransactionId++, m_journal, lockHandle, isolationLevel);
                m_activeTransactions.Add(tx);
                return tx;
            }
        }
        catch
        {
            if (lockHandle != null)
                await lockHandle.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    #endregion

    #region Get

    /// <inheritdoc/>
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        
        if (m_lockManager != null)
        {
            using var _ = m_lockManager.AcquireReadLock();
            return m_store.Get(key);
        }
        
        return m_store.Get(key);
    }

    /// <inheritdoc/>
    public async ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (m_lockManager != null)
        {
            await using var _ = await m_lockManager.AcquireReadLockAsync(cancellationToken).ConfigureAwait(false);
            return await m_store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return await m_store.GetAsync(key, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Put

    /// <inheritdoc/>
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();

        using var _ = m_lockManager?.AcquireWriteLock();

        // Non-transactional write (auto-commit)
        m_journal?.BeginTransaction(0);
        m_journal?.LogPut(0, key, value, m_store.Get(key) ?? ReadOnlySpan<byte>.Empty);
        m_store.Put(key, value);
        m_journal?.CommitTransaction(0);
    }

    /// <inheritdoc/>
    public async ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await using var _ = m_lockManager != null
            ? await m_lockManager.AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var oldValue = await m_store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        m_journal?.BeginTransaction(0);
        m_journal?.LogPut(0, key, value, oldValue ?? []);
        await m_store.PutAsync(key, value, cancellationToken).ConfigureAwait(false);
        m_journal?.CommitTransaction(0);
    }

    #endregion

    #region Delete

    /// <inheritdoc/>
    public bool Delete(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();

        using var _ = m_lockManager?.AcquireWriteLock();

        // Non-transactional delete (auto-commit)
        var oldValue = m_store.Get(key);
        m_journal?.BeginTransaction(0);
        m_journal?.LogDelete(0, key, oldValue ?? ReadOnlySpan<byte>.Empty);
        var result = m_store.Delete(key);
        m_journal?.CommitTransaction(0);
        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await using var _ = m_lockManager != null
            ? await m_lockManager.AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var oldValue = await m_store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        m_journal?.BeginTransaction(0);
        m_journal?.LogDelete(0, key, oldValue ?? []);
        var result = await m_store.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        m_journal?.CommitTransaction(0);
        return result;
    }

    #endregion

    #region Scan

    /// <inheritdoc/>
    public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
    {
        ThrowIfDisposed();
        
        // Note: Scan holds read lock for the duration if lock manager is available
        // This is intentional to ensure consistent reads during iteration
        using var _ = m_lockManager?.AcquireReadLock();
        
        // Materialize results while holding lock
        return m_store.Scan(startKey, endKey).ToList();
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? startKey,
        byte[]? endKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // For WASM compatibility, use async scan with lock held during iteration
        if (m_lockManager != null)
        {
            await using var _ = await m_lockManager.AcquireReadLockAsync(cancellationToken).ConfigureAwait(false);
            
            await foreach (var item in m_store.ScanAsync(startKey, endKey, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        else
        {
            await foreach (var item in m_store.ScanAsync(startKey, endKey, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }

    #endregion

    #region Flush

    /// <inheritdoc/>
    public void Flush()
    {
        ThrowIfDisposed();
        
        // Note: If called from within a transaction, the transaction already holds the lock
        // If called directly, we need to acquire the lock
        // Since this is typically called from Commit(), we don't acquire lock here
        
        m_store.Flush();
        m_journal?.Sync();
    }

    /// <inheritdoc/>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await m_store.FlushAsync(cancellationToken).ConfigureAwait(false);
        m_journal?.Sync();
    }

    #endregion

    #region Checkpoint

    /// <summary>
    /// Creates a checkpoint: flushes all data and truncates the journal.
    /// </summary>
    public void Checkpoint()
    {
        ThrowIfDisposed();

        using var _ = m_lockManager?.AcquireWriteLock();

        lock (m_txLock)
        {
            if (m_activeTransactions.Count > 0)
                throw new InvalidOperationException("Cannot checkpoint with active transactions");
        }

        m_store.Flush();
        m_journal?.Checkpoint();
    }

    #endregion

    #region Functions

    // Internal methods for Transaction class
    internal byte[]? GetFromStore(ReadOnlySpan<byte> key) => m_store.Get(key);
    internal void PutToStore(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => m_store.Put(key, value);
    internal bool DeleteFromStore(ReadOnlySpan<byte> key) => m_store.Delete(key);

    // Internal async methods for Transaction class (WASM-compatible)
    internal ValueTask<byte[]?> GetFromStoreAsync(byte[] key, CancellationToken cancellationToken = default)
        => m_store.GetAsync(key, cancellationToken);

    internal ValueTask PutToStoreAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        => m_store.PutAsync(key, value, cancellationToken);

    internal ValueTask<bool> DeleteFromStoreAsync(byte[] key, CancellationToken cancellationToken = default)
        => m_store.DeleteAsync(key, cancellationToken);

    internal void NotifyTransactionComplete(Transaction tx)
    {
        lock (m_txLock)
        {
            m_activeTransactions.Remove(tx);
        }
    }

    #endregion

    #region Tools

    private void Recover()
    {
        var recovered = m_journal?.Recover(m_store) ?? 0;
        if (recovered > 0)
        {
            m_store.Flush();
        }
        m_journal?.Checkpoint();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    private static void ValidateIsolationLevel(IsolationLevel isolationLevel)
    {
        // Currently only ReadCommitted is fully supported
        // Other levels are accepted but will behave as ReadCommitted until MVCC is implemented
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

        m_journal?.Dispose();
        m_lockManager?.Dispose();

        if (m_ownsStore)
        {
            m_store.Dispose();
        }
    }

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
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

        m_journal?.Dispose();
        m_lockManager?.Dispose();

        if (m_ownsStore)
        {
            if (m_store is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                m_store.Dispose();
            }
        }
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
    /// Gets the underlying key-value store.
    /// </summary>
    public IKeyValueStore UnderlyingStore => m_store;

    /// <summary>
    /// Gets the provider key of the underlying store.
    /// </summary>
    public string InnerProviderKey => m_store.ProviderKey;

    /// <summary>
    /// Gets the provider key of the journal, if any.
    /// </summary>
    public string? JournalProviderKey => m_journal?.ProviderKey;

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    #endregion
}
