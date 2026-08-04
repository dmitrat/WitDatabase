using System.Runtime.CompilerServices;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tree;

/// <summary>
/// Thread-safe BTree store wrapper for concurrent access.
/// Provides safe concurrent access to StoreBTree for multi-threaded scenarios.
/// </summary>
/// <remarks>
/// This store is designed for scenarios where multiple threads need to access the same BTree.
/// It uses a simple but effective ReaderWriterLock strategy:
/// - Multiple concurrent readers allowed
/// - Single writer with exclusive access
///
/// For maximum single-threaded performance, use StoreBTree directly.
/// This wrapper adds ~1-5% overhead for thread safety.
///
/// <b>No method here may hold the lock across an await.</b> <see cref="ReaderWriterLockSlim"/> is
/// thread-affine: it records which THREAD holds the lock, so a continuation that resumes on another
/// one throws <c>SynchronizationLockException: The write lock is being released without being held</c>
/// out of the release - and, far worse, leaves the lock held by a thread that has moved on, so every
/// later reader and writer waits for ever. All four asynchronous entry points did exactly that until
/// 2026-07-30; it went unnoticed while only the main store used this wrapper, and surfaced the moment
/// secondary indexes did, because <c>IndexManager.FlushAsync</c> is a genuinely asynchronous path.
/// The asynchronous methods therefore do their work through the synchronous ones, which is what they
/// effectively did before - the lock was held for the whole await anyway.
///
/// The same reasoning is why <see cref="Scan"/> hands its results out in chunks rather than holding
/// the read lock across the consumer's code.
/// </remarks>
public sealed class BTreeConcurrentStore : IKeyValueStore, IKeyValueStoreStatistics, IProviderMetadataSource, IAsyncDisposable
{
    #region Constants

    /// <summary>
    /// Provider key for concurrent B-Tree store.
    /// </summary>
    public const string PROVIDER_KEY = "btree-concurrent";

    /// <summary>
    /// Entries read per chunk of a scan. Bounds how long a scan holds the read lock, and with it how
    /// long a writer waits behind a reader.
    /// </summary>
    private const int SCAN_CHUNK_ENTRIES = 512;

    /// <summary>
    /// Bytes read per chunk of a scan, so that a store of large values does not turn the entry limit
    /// into a large allocation.
    /// </summary>
    private const long SCAN_CHUNK_BYTES = 1024 * 1024;

    #endregion

    #region Fields

    private readonly StoreBTree m_store;
    private readonly ReaderWriterLockSlim m_lock;
    private readonly BTreeConcurrencyOptions m_options;
    private readonly bool m_ownsStore;

    private long m_readCount;
    private long m_writeCount;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new concurrent BTree store with file storage.
    /// </summary>
    /// <param name="filePath">Path to the database file.</param>
    /// <param name="options">Concurrency options.</param>
    /// <param name="pageSize">Page size in bytes.</param>
    /// <param name="cacheSize">Number of pages to cache.</param>
    public BTreeConcurrentStore(
        string filePath,
        BTreeConcurrencyOptions? options = null,
        int pageSize = 4096,
        int cacheSize = 1000)
    {
        m_options = options ?? BTreeConcurrencyOptions.Default;
        m_store = new StoreBTree(filePath, pageSize, cacheSize);
        m_ownsStore = true;
        m_lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    }

    /// <summary>
    /// Creates a new concurrent BTree store wrapping an existing StoreBTree.
    /// </summary>
    /// <param name="store">The underlying store.</param>
    /// <param name="options">Concurrency options.</param>
    /// <param name="ownsStore">Whether to dispose the store on disposal.</param>
    public BTreeConcurrentStore(
        StoreBTree store,
        BTreeConcurrencyOptions? options = null,
        bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        
        m_store = store;
        m_options = options ?? BTreeConcurrencyOptions.Default;
        m_ownsStore = ownsStore;
        m_lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    }

    #endregion

    #region Get

    /// <inheritdoc/>
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        IncrementReadCount();

        m_lock.EnterReadLock();
        try
        {
            return m_store.Get(key);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Synchronous under the lock, deliberately - see the type's remarks on why no method here may
    /// await inside <see cref="m_lock"/>.
    /// </remarks>
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
        ThrowIfDisposed();
        IncrementWriteCount();

        m_lock.EnterWriteLock();
        try
        {
            m_store.Put(key, value);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Synchronous under the lock, deliberately - see the type's remarks on why no method here may
    /// await inside <see cref="m_lock"/>.
    /// </remarks>
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
        ThrowIfDisposed();
        IncrementWriteCount();

        m_lock.EnterWriteLock();
        try
        {
            return m_store.Delete(key);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Synchronous under the lock, deliberately - see the type's remarks on why no method here may
    /// await inside <see cref="m_lock"/>.
    /// </remarks>
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
        ThrowIfDisposed();
        return ScanInternal(startKey, endKey);
    }

    /// <summary>
    /// The scan itself, as an iterator, taken one chunk at a time.
    /// </summary>
    /// <remarks>
    /// A B+Tree walk cannot be handed out lazily under the read lock the way an append-only store
    /// can. Holding the lock across the consumer's code deadlocks the engine's own DML: deleting
    /// rows through an index range scan writes to this store while the scan is open, and the
    /// read-to-write upgrade on the same thread throws. Materialising the whole range under one lock
    /// is safe but charges the caller for everything it did not ask for - measured on a 200,000-entry
    /// index, an open-ended range whose consumer took five entries cost 108 ms and 25.5 MB where the
    /// unwrapped store cost 3.1 ms and nothing.
    ///
    /// So each chunk is read under the read lock, with no writer anywhere inside the tree; the lock
    /// is released before the chunk reaches the consumer; and the next chunk re-seeks from the key
    /// immediately after the last one returned - a position expressed as a key, which a concurrent
    /// split cannot invalidate the way it invalidates a page-and-slot cursor.
    ///
    /// The semantic this buys, stated because it is real: <b>a scan is not a snapshot</b>. Writes
    /// that land between chunks are visible to the remainder of the scan. That is exactly what the
    /// unwrapped store does, so concurrent mode now behaves like the default mode rather than
    /// differently from it.
    /// </remarks>
    private IEnumerable<(byte[] Key, byte[] Value)> ScanInternal(byte[]? startKey, byte[]? endKey)
    {
        var next = startKey;

        while (true)
        {
            ThrowIfDisposed();

            List<(byte[] Key, byte[] Value)> chunk;
            bool rangeExhausted;

            m_lock.EnterReadLock();
            try
            {
                chunk = ReadChunk(next, endKey, out rangeExhausted);
            }
            finally
            {
                m_lock.ExitReadLock();
            }

            foreach (var entry in chunk)
                yield return entry;

            if (rangeExhausted || chunk.Count == 0)
                yield break;

            next = KeyAfter(chunk[^1].Key);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? startKey,
        byte[]? endKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var next = startKey;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            List<(byte[] Key, byte[] Value)> chunk;
            bool rangeExhausted;

            m_lock.EnterReadLock();
            try
            {
                chunk = ReadChunk(next, endKey, out rangeExhausted);
            }
            finally
            {
                m_lock.ExitReadLock();
            }

            foreach (var entry in chunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return entry;
            }

            if (rangeExhausted || chunk.Count == 0)
                break;

            next = KeyAfter(chunk[^1].Key);
        }

        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// Reads at most one chunk of the range. The caller must hold the read lock.
    /// </summary>
    /// <param name="rangeExhausted">
    /// True when the underlying scan ended on its own, which is the only way to know the range is
    /// finished - a chunk that happens to fill exactly to the limit says nothing.
    /// </param>
    private List<(byte[] Key, byte[] Value)> ReadChunk(byte[]? startKey, byte[]? endKey, out bool rangeExhausted)
    {
        var chunk = new List<(byte[] Key, byte[] Value)>();
        long bytes = 0;

        rangeExhausted = true;

        foreach (var entry in m_store.Scan(startKey, endKey))
        {
            chunk.Add(entry);
            bytes += entry.Key.Length + (entry.Value?.Length ?? 0);

            if (chunk.Count >= SCAN_CHUNK_ENTRIES || bytes >= SCAN_CHUNK_BYTES)
            {
                rangeExhausted = false;
                break;
            }
        }

        return chunk;
    }

    /// <summary>
    /// The smallest key that sorts strictly after <paramref name="key"/>. Keys are compared
    /// lexicographically, so appending a zero byte lands between <paramref name="key"/> and every
    /// key above it, whether or not that key has this one as a prefix.
    /// </summary>
    private static byte[] KeyAfter(byte[] key)
    {
        var next = new byte[key.Length + 1];
        key.CopyTo(next, 0);
        next[key.Length] = 0;
        return next;
    }

    #endregion

    #region Flush

    /// <inheritdoc/>
    public void Flush()
    {
        ThrowIfDisposed();

        m_lock.EnterWriteLock();
        try
        {
            m_store.Flush();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Forwarded rather than left to the interface default, which would call <see cref="Flush"/> -
    /// on a wrapped LSM store that means "make durable" and would quietly not reorganise anything.
    /// </remarks>
    public void Checkpoint()
    {
        ThrowIfDisposed();

        m_lock.EnterWriteLock();
        try
        {
            ((IKeyValueStore)m_store).Checkpoint();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Synchronous under the lock, deliberately - see the type's remarks on why no method here may
    /// await inside <see cref="m_lock"/>. This is the one that was caught: an index flush resumed on
    /// a different thread and threw <c>SynchronizationLockException</c> out of <c>ExitWriteLock</c>.
    /// </remarks>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Flush();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region IKeyValueStoreStatistics

    /// <inheritdoc/>
    public long Count()
    {
        ThrowIfDisposed();

        m_lock.EnterReadLock();
        try
        {
            return m_store.Count();
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public ValueTask<long> CountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Count());
    }

    /// <inheritdoc/>
    public long ApproximateSizeInBytes
    {
        get
        {
            ThrowIfDisposed();
            return m_store.ApproximateSizeInBytes;
        }
    }

    /// <inheritdoc/>
    public long EstimatedKeyCount => Count();

    /// <inheritdoc/>
    public bool AreStatisticsExact => true;

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    /// <inheritdoc/>
    /// <remarks>
    /// Delegated rather than answered here: since 12.0.0 every B+Tree store the builder produces is
    /// wrapped in this one, so a caller that asked the store for its metadata and got nothing would be
    /// asking the wrapper.
    /// </remarks>
    public ProviderMetadata? StoredMetadata => m_store.StoredMetadata;

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the total number of read operations.
    /// </summary>
    public long ReadCount => Volatile.Read(ref m_readCount);

    /// <summary>
    /// Gets the total number of write operations.
    /// </summary>
    public long WriteCount => Volatile.Read(ref m_writeCount);

    /// <summary>
    /// Gets the concurrency options.
    /// </summary>
    public BTreeConcurrencyOptions Options => m_options;

    #endregion

    #region Private Helpers

    private void IncrementReadCount()
    {
        if (m_options.TrackStatistics)
        {
            Interlocked.Increment(ref m_readCount);
        }
    }

    private void IncrementWriteCount()
    {
        if (m_options.TrackStatistics)
        {
            Interlocked.Increment(ref m_writeCount);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_lock.Dispose();

        if (m_ownsStore)
        {
            m_store.Dispose();
        }
    }

    /// <summary>
    /// Closes the wrapped store asynchronously.
    /// </summary>
    /// <remarks>
    /// Since 12.0.0 every B+Tree store the builder produces is wrapped in this one, so a wrapper with
    /// no asynchronous disposal broke the asynchronous close of <b>every</b> database - the layers above
    /// look for <see cref="IAsyncDisposable"/> and fall back to the synchronous close when they do not
    /// find it, and <see cref="StoreBTree"/>'s asynchronous close was then never reached.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_lock.Dispose();

        if (m_ownsStore)
        {
            await m_store.DisposeAsync().ConfigureAwait(false);
        }
    }

    #endregion
}
