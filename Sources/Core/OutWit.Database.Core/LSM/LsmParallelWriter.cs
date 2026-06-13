using System.Threading.Channels;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.LSM;

/// <summary>
/// Coordinates parallel writes to LSM-Tree storage.
/// Multiple threads can submit writes concurrently through thread-local buffers,
/// while a background thread merges buffers into the main MemTable.
/// 
/// Key features:
/// - Thread-local write buffers to reduce contention
/// - Background buffer merge thread with batch writes
/// - Configurable buffer size and flush thresholds
/// - Statistics tracking
/// </summary>
/// <remarks>
/// This class enables higher write throughput by:
/// 1. Allowing writers to batch their writes in thread-local buffers
/// 2. Merging buffers asynchronously using batch operations to reduce lock contention
/// 3. Supporting both fire-and-forget and awaitable write modes
/// </remarks>
public sealed class LsmParallelWriter : IDisposable, IAsyncDisposable
{
    #region Constants

    private const int DEFAULT_BUFFER_SIZE_THRESHOLD = 64 * 1024; // 64KB
    private const int DEFAULT_MAX_PENDING_BUFFERS = 100;
    private const int DEFAULT_FLUSH_INTERVAL_MS = 10;

    #endregion

    #region Fields

    private readonly StoreLsm m_store;
    private readonly Channel<(LsmWriteBuffer Buffer, TaskCompletionSource<bool>? Completion)> m_bufferChannel;
    private readonly ThreadLocal<LsmWriteBuffer> m_threadLocalBuffer;
    private readonly Task m_mergeTask;
    private readonly CancellationTokenSource m_cts;
    private readonly int m_bufferSizeThreshold;
    private readonly int m_flushIntervalMs;
    private readonly Lock m_statsLock = new();

    private long m_buffersSubmitted;
    private long m_entriesMerged;
    private long m_mergeOperations;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new parallel writer for the specified LSM store.
    /// </summary>
    /// <param name="store">The LSM store to write to.</param>
    /// <param name="bufferSizeThreshold">Size threshold for auto-flushing thread-local buffers.</param>
    /// <param name="maxPendingBuffers">Maximum pending buffers in the merge queue.</param>
    /// <param name="flushIntervalMs">Interval for periodic buffer flush.</param>
    public LsmParallelWriter(
        StoreLsm store,
        int bufferSizeThreshold = DEFAULT_BUFFER_SIZE_THRESHOLD,
        int maxPendingBuffers = DEFAULT_MAX_PENDING_BUFFERS,
        int flushIntervalMs = DEFAULT_FLUSH_INTERVAL_MS)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (bufferSizeThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSizeThreshold));
        if (maxPendingBuffers <= 0) throw new ArgumentOutOfRangeException(nameof(maxPendingBuffers));
        if (flushIntervalMs < 0) throw new ArgumentOutOfRangeException(nameof(flushIntervalMs));

        m_store = store;
        m_bufferSizeThreshold = bufferSizeThreshold;
        m_flushIntervalMs = flushIntervalMs;
        m_cts = new CancellationTokenSource();

        // Create bounded channel for buffer queue
        m_bufferChannel = Channel.CreateBounded<(LsmWriteBuffer, TaskCompletionSource<bool>?)>(
            new BoundedChannelOptions(maxPendingBuffers)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        // Thread-local buffers
        m_threadLocalBuffer = new ThreadLocal<LsmWriteBuffer>(
            () => new LsmWriteBuffer(sizeThreshold: bufferSizeThreshold),
            trackAllValues: true);

        // Start background merge task
        m_mergeTask = Task.Run(MergeLoopAsync);
    }

    #endregion

    #region Write Operations

    /// <summary>
    /// Buffers a Put operation. May trigger automatic flush.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();

        var buffer = GetOrCreateBuffer();
        buffer.Put(key, value);

        if (buffer.ShouldFlush)
        {
            FlushCurrentBuffer();
        }
    }

    /// <summary>
    /// Buffers a Put operation and waits for it to be merged.
    /// </summary>
    public async Task PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var buffer = GetOrCreateBuffer();
        buffer.Put(key, value);

        if (buffer.ShouldFlush)
        {
            await FlushCurrentBufferAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Buffers a Delete operation. May trigger automatic flush.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    public void Delete(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();

        var buffer = GetOrCreateBuffer();
        buffer.Delete(key);

        if (buffer.ShouldFlush)
        {
            FlushCurrentBuffer();
        }
    }

    /// <summary>
    /// Buffers a Delete operation and waits for it to be merged.
    /// </summary>
    public async Task DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var buffer = GetOrCreateBuffer();
        buffer.Delete(key);

        if (buffer.ShouldFlush)
        {
            await FlushCurrentBufferAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Flushes the current thread's buffer to the merge queue.
    /// Does not wait for merge to complete.
    /// </summary>
    public void FlushCurrentBuffer()
    {
        ThrowIfDisposed();

        var buffer = m_threadLocalBuffer.Value;
        if (buffer == null || buffer.IsEmpty)
            return;

        // Submit buffer and create new one for this thread
        if (m_bufferChannel.Writer.TryWrite((buffer, null)))
        {
            Interlocked.Increment(ref m_buffersSubmitted);
            m_threadLocalBuffer.Value = new LsmWriteBuffer(sizeThreshold: m_bufferSizeThreshold);
        }
    }

    /// <summary>
    /// Flushes the current thread's buffer and waits for merge to complete.
    /// </summary>
    public async Task FlushCurrentBufferAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var buffer = m_threadLocalBuffer.Value;
        if (buffer == null || buffer.IsEmpty)
            return;

        // Create completion source to wait for merge
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Submit buffer
        await m_bufferChannel.Writer.WriteAsync((buffer, completion), cancellationToken);
        Interlocked.Increment(ref m_buffersSubmitted);
        m_threadLocalBuffer.Value = new LsmWriteBuffer(sizeThreshold: m_bufferSizeThreshold);

        // Wait for merge to complete
        await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Flushes all thread-local buffers and waits for all merges to complete.
    /// </summary>
    public async Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var completions = new List<TaskCompletionSource<bool>>();

        // Flush all thread-local buffers with completion tracking
        foreach (var buffer in m_threadLocalBuffer.Values)
        {
            if (buffer != null && !buffer.IsEmpty)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                completions.Add(completion);
                
                await m_bufferChannel.Writer.WriteAsync((buffer, completion), cancellationToken);
                Interlocked.Increment(ref m_buffersSubmitted);
            }
        }

        // Wait for all merges to complete
        if (completions.Count > 0)
        {
            await Task.WhenAll(completions.Select(c => c.Task)).WaitAsync(cancellationToken);
        }

        // Reset thread-local buffers
        m_threadLocalBuffer.Value = new LsmWriteBuffer(sizeThreshold: m_bufferSizeThreshold);
    }

    #endregion

    #region Background Processing

    private LsmWriteBuffer GetOrCreateBuffer()
    {
        var buffer = m_threadLocalBuffer.Value;
        
        // Create new buffer if null or disposed (after FlushAllAsync)
        if (buffer == null || buffer.IsDisposed)
        {
            buffer = new LsmWriteBuffer(sizeThreshold: m_bufferSizeThreshold);
            m_threadLocalBuffer.Value = buffer;
        }
        return buffer;
    }

    private async Task MergeLoopAsync()
    {
        var reader = m_bufferChannel.Reader;
        var token = m_cts.Token;

        try
        {
            while (true)
            {
                // Wait for buffers with a periodic timeout. WaitToReadAsync returns
                // false once the channel is completed AND fully drained, which is the
                // clean-shutdown exit: Dispose calls Writer.Complete() and joins this
                // task BEFORE cancelling the token (mirrors LsmMemTableFlusher /
                // LsmParallelCompactor), so the final merges below run against a live
                // store and every queued/awaited buffer is durably written, never
                // dropped or faulted by a premature cancellation.
                bool hasData;
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeoutCts.CancelAfter(m_flushIntervalMs);

                    try
                    {
                        hasData = await reader.WaitToReadAsync(timeoutCts.Token);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        // Periodic flush tick - merge anything queued, then keep waiting.
                        DrainPendingBuffers(reader);
                        continue;
                    }
                }

                // Channel completed and empty -> clean shutdown.
                if (!hasData)
                    break;

                // Collect a bounded batch and merge.
                var buffersToMerge = new List<(LsmWriteBuffer Buffer, TaskCompletionSource<bool>? Completion)>();
                while (buffersToMerge.Count < 16 && reader.TryRead(out var item))
                    buffersToMerge.Add(item);

                if (buffersToMerge.Count > 0)
                    MergeBuffersBatch(buffersToMerge);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Hard cancel - only happens if the drain join in Dispose timed out.
        }
        catch (ChannelClosedException)
        {
            // Channel completed concurrently - nothing more to do.
        }
        finally
        {
            // Safety net: merge any buffer that slipped in after Complete().
            DrainPendingBuffers(reader);
        }
    }

    private void DrainPendingBuffers(ChannelReader<(LsmWriteBuffer Buffer, TaskCompletionSource<bool>? Completion)> reader)
    {
        var buffersToMerge = new List<(LsmWriteBuffer Buffer, TaskCompletionSource<bool>? Completion)>();
        while (reader.TryRead(out var item))
            buffersToMerge.Add(item);

        if (buffersToMerge.Count > 0)
            MergeBuffersBatch(buffersToMerge);
    }

    /// <summary>
    /// Merges multiple buffers in a single batch for better performance.
    /// </summary>
    private void MergeBuffersBatch(List<(LsmWriteBuffer Buffer, TaskCompletionSource<bool>? Completion)> buffers)
    {
        var allEntries = new List<(byte[] Key, byte[]? Value, bool IsDelete)>();
        var completions = new List<TaskCompletionSource<bool>>();
        
        // Collect all entries from all buffers
        foreach (var (buffer, completion) in buffers)
        {
            try
            {
                var entries = buffer.Drain();
                allEntries.AddRange(entries);
                buffer.Dispose();
                
                if (completion != null)
                {
                    completions.Add(completion);
                }
            }
            catch (Exception ex)
            {
                completion?.TrySetException(ex);
            }
        }
        
        // Single batch write to store
        try
        {
            // Group by operation type for better locality
            var puts = allEntries.Where(e => !e.IsDelete).ToList();
            var deletes = allEntries.Where(e => e.IsDelete).ToList();
            
            // Batch puts
            foreach (var (key, value, _) in puts)
            {
                m_store.Put(key, value!);
            }
            
            // Batch deletes
            foreach (var (key, _, _) in deletes)
            {
                m_store.Delete(key);
            }
            
            // Update stats
            lock (m_statsLock)
            {
                m_entriesMerged += allEntries.Count;
                m_mergeOperations++;
            }
            
            // Signal all completions
            foreach (var completion in completions)
            {
                completion.TrySetResult(true);
            }
        }
        catch (Exception ex)
        {
            foreach (var completion in completions)
            {
                completion.TrySetException(ex);
            }
        }
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the number of buffers submitted for merge.
    /// </summary>
    public long BuffersSubmitted => Volatile.Read(ref m_buffersSubmitted);

    /// <summary>
    /// Gets the total number of entries merged.
    /// </summary>
    public long EntriesMerged
    {
        get
        {
            lock (m_statsLock)
                return m_entriesMerged;
        }
    }

    /// <summary>
    /// Gets the number of merge operations performed.
    /// </summary>
    public long MergeOperations
    {
        get
        {
            lock (m_statsLock)
                return m_mergeOperations;
        }
    }

    /// <summary>
    /// Gets the average entries per merge operation.
    /// </summary>
    public double AverageEntriesPerMerge
    {
        get
        {
            lock (m_statsLock)
            {
                return m_mergeOperations > 0
                    ? (double)m_entriesMerged / m_mergeOperations
                    : 0;
            }
        }
    }

    /// <summary>
    /// Gets the number of pending buffers in the queue.
    /// </summary>
    public int PendingBuffers
    {
        get
        {
            try
            {
                return m_bufferChannel.Reader.Count;
            }
            catch (NotSupportedException)
            {
                return -1;
            }
        }
    }

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

        // Stop accepting new buffers, then let the merge loop drain the queue and
        // write it through to the (still-live) store before we cancel. Cancelling
        // first would tear the merge loop down mid-drain and fault awaited writes.
        m_bufferChannel.Writer.Complete();

        if (!m_mergeTask.Wait(TimeSpan.FromSeconds(5)))
        {
            // Drain is taking too long - force the loop to stop.
            m_cts.Cancel();
            m_mergeTask.Wait(TimeSpan.FromSeconds(1));
        }

        // Idempotent: ensure the token is cancelled before it is disposed.
        m_cts.Cancel();

        // Dispose thread-local buffers
        foreach (var buffer in m_threadLocalBuffer.Values)
        {
            buffer?.Dispose();
        }
        m_threadLocalBuffer.Dispose();

        m_cts.Dispose();
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        // Drain-before-cancel, same ordering as the synchronous Dispose.
        m_bufferChannel.Writer.Complete();

        try
        {
            await m_mergeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Drain is taking too long - force the loop to stop.
            await m_cts.CancelAsync();
            try
            {
                await m_mergeTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
                // Give up waiting; cancellation has been requested.
            }
        }

        // Idempotent: ensure the token is cancelled before it is disposed.
        await m_cts.CancelAsync();

        // Dispose thread-local buffers
        foreach (var buffer in m_threadLocalBuffer.Values)
        {
            buffer?.Dispose();
        }
        m_threadLocalBuffer.Dispose();

        m_cts.Dispose();
    }

    #endregion
}
