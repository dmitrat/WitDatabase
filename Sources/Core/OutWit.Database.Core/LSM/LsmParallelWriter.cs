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
    private readonly ThreadLocal<BufferSlot> m_threadLocalSlot;
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

        // Thread-local buffers, each behind a slot the owner and a foreign flush share - see
        // BufferSlot for why the indirection exists.
        m_threadLocalSlot = new ThreadLocal<BufferSlot>(
            () => new BufferSlot(new LsmWriteBuffer(sizeThreshold: bufferSizeThreshold)),
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

        var slot = GetOrCreateSlot();
        bool shouldFlush;

        lock (slot.Gate)
        {
            slot.Buffer.Put(key, value);
            shouldFlush = slot.Buffer.ShouldFlush;
        }

        if (shouldFlush)
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

        var slot = GetOrCreateSlot();
        bool shouldFlush;

        lock (slot.Gate)
        {
            slot.Buffer.Put(key, value);
            shouldFlush = slot.Buffer.ShouldFlush;
        }

        if (shouldFlush)
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

        var slot = GetOrCreateSlot();
        bool shouldFlush;

        lock (slot.Gate)
        {
            slot.Buffer.Delete(key);
            shouldFlush = slot.Buffer.ShouldFlush;
        }

        if (shouldFlush)
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

        var slot = GetOrCreateSlot();
        bool shouldFlush;

        lock (slot.Gate)
        {
            slot.Buffer.Delete(key);
            shouldFlush = slot.Buffer.ShouldFlush;
        }

        if (shouldFlush)
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

        var slot = GetOrCreateSlot();

        // Taken under the slot's gate, so the buffer that goes to the merge loop is one nobody can
        // still be writing into - including this thread, which now has a fresh one.
        var buffer = TakeBuffer(slot);
        if (buffer == null)
            return;

        if (m_bufferChannel.Writer.TryWrite((buffer, null)))
        {
            Interlocked.Increment(ref m_buffersSubmitted);
            return;
        }

        // The queue is full and this overload does not wait. Put the entries back rather than drop
        // them: they are already out of the slot, so nothing else will ever flush them.
        ReturnBuffer(slot, buffer);
    }

    /// <summary>
    /// Flushes the current thread's buffer and blocks until the merge has been applied to the store.
    /// </summary>
    /// <remarks>
    /// This is what a synchronous read has to call before it queries the store. The fire-and-forget
    /// <see cref="FlushCurrentBuffer"/> is not enough: it queues the buffer and returns, so a read that
    /// follows it still sees a store the merge has not reached. Cheap when there is nothing pending -
    /// an empty buffer returns without touching the channel.
    /// </remarks>
    public void FlushCurrentBufferAndWait()
    {
        ThrowIfDisposed();

        var slot = GetOrCreateSlot();

        lock (slot.Gate)
        {
            if (slot.Buffer.IsEmpty)
                return;
        }

        FlushCurrentBufferAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Flushes the current thread's buffer and waits for merge to complete.
    /// </summary>
    public async Task FlushCurrentBufferAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var slot = GetOrCreateSlot();

        var buffer = TakeBuffer(slot);
        if (buffer == null)
            return;

        // Create completion source to wait for merge
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Submit buffer
        await m_bufferChannel.Writer.WriteAsync((buffer, completion), cancellationToken);
        Interlocked.Increment(ref m_buffersSubmitted);

        // Wait for merge to complete
        await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Flushes every thread's buffer and waits for all merges to complete.
    /// </summary>
    /// <remarks>
    /// This reaches into buffers belonging to threads that are still running, and it has to: the
    /// commit path calls it to make everything durable, so leaving another thread's entries behind
    /// would lose acknowledged writes. What it must not do is take a buffer out from under the thread
    /// that is writing into it.
    ///
    /// It used to do exactly that. The buffer went to the merge loop while its owner kept appending
    /// to the same <c>List</c>, and the merge drained and disposed it: measured, a producer writing
    /// 2000 entries alongside a flushing thread lost runs of eight and nine consecutive entries, and
    /// the third round died inside <c>Drain</c> with "Destination array was not long enough" - a list
    /// copied while it was being added to. It also reset only the CALLING thread's slot, so every
    /// other owner was left holding a buffer that had already been merged and disposed.
    ///
    /// Each buffer is now taken under its slot's gate, which is the same gate the owner appends
    /// under, and replaced with a fresh one in the same breath. The owner never touches a buffer that
    /// has been handed away, and nothing is left unflushed.
    /// </remarks>
    public async Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var completions = new List<TaskCompletionSource<bool>>();

        foreach (var slot in m_threadLocalSlot.Values)
        {
            if (slot == null)
                continue;

            var buffer = TakeBuffer(slot);
            if (buffer == null)
                continue;

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Add(completion);

            await m_bufferChannel.Writer.WriteAsync((buffer, completion), cancellationToken);
            Interlocked.Increment(ref m_buffersSubmitted);
        }

        // Everything ALREADY queued has to be merged too, not only what this call handed over.
        // Buffers reach the queue with no completion attached - Put auto-flushes at the size
        // threshold and FlushCurrentBuffer is fire-and-forget - so waiting for this call's own
        // completions returned while another thread's entries were still in flight, and this is the
        // method the commit path uses to make writes durable. Found by CI, which lost the tail of a
        // batch that a second thread had queued moments earlier.
        //
        // The channel is FIFO with a single reader, so an empty buffer queued last completes only
        // after every buffer ahead of it has been applied to the store. It is not counted as a
        // submitted buffer, because it carries no writes.
        var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        completions.Add(barrier);

        await m_bufferChannel.Writer.WriteAsync(
            (new LsmWriteBuffer(sizeThreshold: m_bufferSizeThreshold), barrier), cancellationToken);

        await Task.WhenAll(completions.Select(c => c.Task)).WaitAsync(cancellationToken);
    }

    #endregion

    #region Background Processing

    private BufferSlot GetOrCreateSlot()
    {
        var slot = m_threadLocalSlot.Value;

        if (slot == null)
        {
            slot = new BufferSlot(new LsmWriteBuffer(sizeThreshold: m_bufferSizeThreshold));
            m_threadLocalSlot.Value = slot;
        }

        return slot;
    }

    /// <summary>
    /// Takes a slot's buffer for merging and leaves a fresh one in its place, or returns null when
    /// there is nothing to flush. Taken under the slot's gate, so no writer is inside the buffer at
    /// the moment it changes hands.
    /// </summary>
    private LsmWriteBuffer? TakeBuffer(BufferSlot slot)
    {
        lock (slot.Gate)
        {
            var buffer = slot.Buffer;

            if (buffer.IsDisposed)
            {
                slot.Buffer = new LsmWriteBuffer(sizeThreshold: m_bufferSizeThreshold);
                return null;
            }

            if (buffer.IsEmpty)
                return null;

            slot.Buffer = new LsmWriteBuffer(sizeThreshold: m_bufferSizeThreshold);
            return buffer;
        }
    }

    /// <summary>
    /// Puts a taken buffer back, for the fire-and-forget flush that could not queue it. Its entries
    /// go in front of anything written since, because that is the order they were written in.
    /// </summary>
    private static void ReturnBuffer(BufferSlot slot, LsmWriteBuffer buffer)
    {
        lock (slot.Gate)
        {
            var written = slot.Buffer;

            // The common case is that nothing was written in between - only the owner thread appends,
            // and it is the thread running this method.
            if (!written.IsDisposed && !written.IsEmpty)
            {
                foreach (var (key, value, isDelete) in written.Drain())
                {
                    if (isDelete)
                        buffer.Delete(key);
                    else
                        buffer.Put(key, value!);
                }
            }

            slot.Buffer = buffer;

            if (!ReferenceEquals(written, buffer))
                written.Dispose();
        }
    }

    /// <summary>
    /// A thread's write buffer, held behind a gate that both its owner and a foreign
    /// <see cref="FlushAllAsync"/> take.
    /// </summary>
    /// <remarks>
    /// The indirection is what makes a foreign flush safe. <see cref="ThreadLocal{T}"/> hands out
    /// another thread's value but gives no way to REPLACE it, so a flush that wanted a thread's
    /// buffer could only take it and leave the owner holding the same object - which the merge loop
    /// then drained and disposed underneath it. With the buffer behind a slot, taking it and leaving
    /// a fresh one is a single operation under one lock, and the owner's next append finds the fresh
    /// buffer rather than a merged one.
    /// </remarks>
    private sealed class BufferSlot
    {
        public BufferSlot(LsmWriteBuffer buffer) => Buffer = buffer;

        /// <summary>Held while appending to, or exchanging, <see cref="Buffer"/>.</summary>
        public Lock Gate { get; } = new();

        public LsmWriteBuffer Buffer { get; set; }
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
            // Apply in the order the caller issued them. Grouping by operation type "for better
            // locality" reordered every Delete after every Put in the batch, so a caller that wrote a
            // key and then deleted a DIFFERENT version of it - which is exactly what MVCC does on
            // commit - had the delete applied to a store state that never existed.
            foreach (var (key, value, isDelete) in allEntries)
            {
                if (isDelete)
                    m_store.Delete(key);
                else
                    m_store.Put(key, value!);
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

        // Hand over the buffers that are still filling BEFORE closing the queue. Completing the channel
        // drains what was already queued and nothing else, so until 11.3.0 the entries below the size
        // threshold - which is to say the tail of every workload - were thrown away by the
        // m_threadLocalSlot disposal below. With MVCC the commit path calls FlushAllAsync and hid it;
        // with Transactions=false nothing does, and `Store=lsm` with a parallel mode LOST THE LAST ROW
        // WRITTEN across a clean close and reopen while the rows before it survived. Measured in
        // CombinationMatrixTests. A store that accepted a write does not get to discard it at close.
        try
        {
            FlushAllAsync().GetAwaiter().GetResult();
        }
        finally
        {
            DisposeCore();
        }
    }

    private void DisposeCore()
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
        foreach (var slot in m_threadLocalSlot.Values)
        {
            slot?.Buffer.Dispose();
        }
        m_threadLocalSlot.Dispose();

        m_cts.Dispose();
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;

        // Same reason as the synchronous Dispose: the buffers still filling are handed over before the
        // queue is closed, or their entries are discarded with the thread-local slots below.
        try
        {
            await FlushAllAsync().ConfigureAwait(false);
        }
        finally
        {
            await DisposeCoreAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeCoreAsync()
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
        foreach (var slot in m_threadLocalSlot.Values)
        {
            slot?.Buffer.Dispose();
        }
        m_threadLocalSlot.Dispose();

        m_cts.Dispose();
    }

    #endregion
}
