using System.Runtime.CompilerServices;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Builder;

/// <summary>
/// LSM store with parallel write support.
/// Wraps StoreLsm and adds LsmParallelWriter for high-throughput concurrent writes.
/// </summary>
/// <remarks>
/// This store provides:
/// - Thread-local write buffers to reduce contention
/// - Background buffer merging
/// - Statistics tracking for performance analysis
/// </remarks>
public sealed class LsmParallelStore : IKeyValueStore, IKeyValueStoreStatistics, IAsyncDisposable
{
    #region Constants

    /// <summary>
    /// Provider key for parallel LSM store.
    /// </summary>
    public const string PROVIDER_KEY = "lsm-parallel";

    #endregion

    #region Fields

    private readonly StoreLsm m_store;
    private readonly LsmParallelWriter m_writer;
    private readonly LsmParallelStoreOptions m_options;
    private readonly bool m_ownsStore;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new parallel LSM store.
    /// </summary>
    /// <param name="directory">Directory for LSM files.</param>
    /// <param name="options">Parallel store options.</param>
    public LsmParallelStore(string directory, LsmParallelStoreOptions? options = null)
    {
        m_options = options ?? LsmParallelStoreOptions.Default;
        m_store = new StoreLsm(directory);
        m_ownsStore = true;
        m_writer = new LsmParallelWriter(
            m_store,
            bufferSizeThreshold: m_options.BufferSizeThreshold,
            maxPendingBuffers: m_options.MaxWriters * 10,
            flushIntervalMs: m_options.FlushIntervalMs);
    }

    /// <summary>
    /// Creates a new parallel LSM store wrapping an existing StoreLsm.
    /// </summary>
    /// <param name="store">The underlying store.</param>
    /// <param name="options">Parallel store options.</param>
    /// <param name="ownsStore">Whether to dispose the store on disposal.</param>
    public LsmParallelStore(StoreLsm store, LsmParallelStoreOptions? options = null, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);

        m_store = store;
        m_options = options ?? LsmParallelStoreOptions.Default;
        m_ownsStore = ownsStore;
        m_writer = new LsmParallelWriter(
            m_store,
            bufferSizeThreshold: m_options.BufferSizeThreshold,
            maxPendingBuffers: m_options.MaxWriters * 10,
            flushIntervalMs: m_options.FlushIntervalMs);
    }

    #endregion

    #region Get

    /// <inheritdoc/>
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();

        // Read your own writes. This used to go straight to the underlying store, so a key written by
        // this thread and still sitting in its buffer read back as absent - and that is not merely
        // inconvenient, it silently breaks the MVCC commit protocol, which reads the store to find the
        // versions it just installed and mark them committed. See the class remarks.
        m_writer.FlushCurrentBufferAndWait();
        return m_store.Get(key);
    }

    /// <inheritdoc/>
    public async ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await m_writer.FlushCurrentBufferAsync(cancellationToken);
        return await m_store.GetAsync(key, cancellationToken);
    }

    #endregion

    #region Put

    /// <inheritdoc/>
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();
        // Writes go through parallel writer
        m_writer.Put(key, value);
    }

    /// <inheritdoc/>
    public async ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await m_writer.PutAsync(key, value, cancellationToken);
    }

    #endregion

    #region Delete

    /// <inheritdoc/>
    public bool Delete(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        m_writer.Delete(key);
        return true; // Actual deletion happens during merge
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await m_writer.DeleteAsync(key, cancellationToken);
        return true;
    }

    #endregion

    #region Scan

    /// <inheritdoc/>
    public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
    {
        ThrowIfDisposed();

        // Flush pending writes before scan AND WAIT. FlushCurrentBuffer only queues the buffer - its
        // own doc comment says "Does not wait for merge to complete" - so this line used to read a
        // store the merge it had just requested had not reached.
        m_writer.FlushCurrentBufferAndWait();
        return m_store.Scan(startKey, endKey);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? startKey,
        byte[]? endKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // Flush pending writes before scan
        await m_writer.FlushCurrentBufferAsync(cancellationToken);

        await foreach (var item in m_store.ScanAsync(startKey, endKey, cancellationToken))
        {
            yield return item;
        }
    }

    #endregion

    #region Flush

    /// <inheritdoc/>
    public void Flush()
    {
        ThrowIfDisposed();
        // Flush all buffers synchronously
        m_writer.FlushAllAsync().GetAwaiter().GetResult();
        m_store.Flush();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The buffer drain comes first and is not optional. Writes sit in <see cref="m_writer"/> until
    /// it is flushed, so forwarding straight to the wrapped store checkpoints a store that has not
    /// been given the writes yet - three parallel-store tests went red on exactly that, reading 0
    /// rows where they expected 800 and 1,000. It is the same read-your-own-writes hazard that once
    /// made this store lose acknowledged writes.
    /// </remarks>
    public void Checkpoint()
    {
        ThrowIfDisposed();

        m_writer.FlushAllAsync().GetAwaiter().GetResult();
        m_store.Checkpoint();
    }

    /// <inheritdoc/>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await m_writer.FlushAllAsync(cancellationToken);
        await m_store.FlushAsync(cancellationToken);
    }

    #endregion

    #region IKeyValueStoreStatistics

    /// <inheritdoc/>
    public long Count()
    {
        ThrowIfDisposed();
        return m_store.Count();
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
    public bool AreStatisticsExact => m_store.AreStatisticsExact;

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the number of buffers submitted to the parallel writer.
    /// </summary>
    public long BuffersSubmitted => m_writer.BuffersSubmitted;

    /// <summary>
    /// Gets the total number of entries merged.
    /// </summary>
    public long EntriesMerged => m_writer.EntriesMerged;

    /// <summary>
    /// Gets the number of merge operations performed.
    /// </summary>
    public long MergeOperations => m_writer.MergeOperations;

    /// <summary>
    /// Gets the average entries per merge operation.
    /// </summary>
    public double AverageEntriesPerMerge => m_writer.AverageEntriesPerMerge;

    /// <summary>
    /// Gets the number of pending buffers in the queue.
    /// </summary>
    public int PendingBuffers => m_writer.PendingBuffers;

    /// <summary>
    /// Gets the parallel store options.
    /// </summary>
    public LsmParallelStoreOptions Options => m_options;

    #endregion

    #region Tools

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

        m_writer.Dispose();

        if (m_ownsStore)
        {
            m_store.Dispose();
        }
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        await m_writer.DisposeAsync();

        if (m_ownsStore)
        {
            m_store.Dispose();
        }
    }

    #endregion
}
