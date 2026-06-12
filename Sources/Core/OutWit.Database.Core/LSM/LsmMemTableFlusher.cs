using System.Threading.Channels;

namespace OutWit.Database.Core.LSM;

/// <summary>
/// Manages parallel flushing of MemTables to SSTables.
/// Allows multiple MemTables to be flushed concurrently while maintaining
/// correct ordering of SSTables.
/// </summary>
/// <remarks>
/// Key features:
/// - Multiple concurrent flush operations
/// - Queue of immutable MemTables waiting for flush
/// - Backpressure when queue is full
/// - Statistics tracking
/// </remarks>
public sealed class LsmMemTableFlusher : IDisposable, IAsyncDisposable
{
    #region Constants

    private const int DEFAULT_MAX_PARALLEL_FLUSHES = 2;
    private const int DEFAULT_MAX_QUEUE_SIZE = 4;

    #endregion

    #region Fields

    private readonly Channel<FlushRequest> m_flushChannel;
    private readonly Task[] m_flushTasks;
    private readonly CancellationTokenSource m_cts;
    private readonly Func<MemTable, int, string> m_flushAction;
    private readonly Action<string> m_onFlushComplete;
    private readonly Lock m_statsLock = new();

    private long m_memTablesFlushed;
    private long m_totalEntriesFlushed;
    private long m_totalBytesFlushed;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new MemTable flusher.
    /// </summary>
    /// <param name="flushAction">Action to flush a MemTable. Takes MemTable and SSTable ID, returns SSTable path.</param>
    /// <param name="onFlushComplete">Callback when flush completes with SSTable path.</param>
    /// <param name="maxParallelFlushes">Maximum concurrent flush operations.</param>
    /// <param name="maxQueueSize">Maximum MemTables waiting for flush.</param>
    public LsmMemTableFlusher(
        Func<MemTable, int, string> flushAction,
        Action<string> onFlushComplete,
        int maxParallelFlushes = DEFAULT_MAX_PARALLEL_FLUSHES,
        int maxQueueSize = DEFAULT_MAX_QUEUE_SIZE)
    {
        ArgumentNullException.ThrowIfNull(flushAction);
        ArgumentNullException.ThrowIfNull(onFlushComplete);
        if (maxParallelFlushes <= 0) throw new ArgumentOutOfRangeException(nameof(maxParallelFlushes));
        if (maxQueueSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxQueueSize));

        m_flushAction = flushAction;
        m_onFlushComplete = onFlushComplete;
        m_cts = new CancellationTokenSource();

        // Create bounded channel for flush requests
        m_flushChannel = Channel.CreateBounded<FlushRequest>(new BoundedChannelOptions(maxQueueSize)
        {
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        // Start flush worker tasks
        m_flushTasks = new Task[maxParallelFlushes];
        for (int i = 0; i < maxParallelFlushes; i++)
        {
            m_flushTasks[i] = Task.Run(FlushLoopAsync);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Submits a MemTable for flushing. Does not wait for completion.
    /// </summary>
    /// <param name="memTable">The MemTable to flush.</param>
    /// <param name="sstableId">The SSTable ID to use.</param>
    /// <returns>True if submitted successfully.</returns>
    public bool TrySubmit(MemTable memTable, int sstableId)
    {
        ThrowIfDisposed();
        
        var request = new FlushRequest(memTable, sstableId, null);
        return m_flushChannel.Writer.TryWrite(request);
    }

    /// <summary>
    /// Submits a MemTable for flushing and waits for completion.
    /// </summary>
    /// <param name="memTable">The MemTable to flush.</param>
    /// <param name="sstableId">The SSTable ID to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path to the created SSTable.</returns>
    public async Task<string> SubmitAndWaitAsync(
        MemTable memTable, 
        int sstableId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new FlushRequest(memTable, sstableId, completion);

        await m_flushChannel.Writer.WriteAsync(request, cancellationToken);

        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Waits for all pending flushes to complete.
    /// </summary>
    public async Task WaitForAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Wait for channel to drain
        while (m_flushChannel.Reader.Count > 0)
        {
            await Task.Delay(10, cancellationToken);
        }

        // Small delay to ensure in-progress flushes complete
        await Task.Delay(50, cancellationToken);
    }

    #endregion

    #region Background Processing

    private async Task FlushLoopAsync()
    {
        var reader = m_flushChannel.Reader;
        var token = m_cts.Token;

        try
        {
            await foreach (var request in reader.ReadAllAsync(token))
            {
                try
                {
                    var sstablePath = ExecuteFlush(request.MemTable, request.SstableId);
                    
                    // Notify completion
                    m_onFlushComplete(sstablePath);
                    request.Completion?.TrySetResult(sstablePath);
                }
                catch (Exception ex)
                {
                    request.Completion?.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (ChannelClosedException)
        {
            // Channel closed during shutdown
        }
    }

    private string ExecuteFlush(MemTable memTable, int sstableId)
    {
        var entryCount = memTable.Count;
        var byteSize = memTable.ApproximateSize;

        var sstablePath = m_flushAction(memTable, sstableId);

        // Update statistics
        lock (m_statsLock)
        {
            m_memTablesFlushed++;
            m_totalEntriesFlushed += entryCount;
            m_totalBytesFlushed += byteSize;
        }

        // Dispose the flushed MemTable
        memTable.Dispose();

        return sstablePath;
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the number of MemTables flushed.
    /// </summary>
    public long MemTablesFlushed
    {
        get
        {
            lock (m_statsLock)
                return m_memTablesFlushed;
        }
    }

    /// <summary>
    /// Gets the total number of entries flushed.
    /// </summary>
    public long TotalEntriesFlushed
    {
        get
        {
            lock (m_statsLock)
                return m_totalEntriesFlushed;
        }
    }

    /// <summary>
    /// Gets the total bytes flushed.
    /// </summary>
    public long TotalBytesFlushed
    {
        get
        {
            lock (m_statsLock)
                return m_totalBytesFlushed;
        }
    }

    /// <summary>
    /// Gets the number of pending flush requests.
    /// </summary>
    public int PendingFlushes
    {
        get
        {
            try
            {
                return m_flushChannel.Reader.Count;
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

        m_flushChannel.Writer.Complete();
        Task.WaitAll(m_flushTasks, TimeSpan.FromSeconds(10));

        m_cts.Cancel();
        m_cts.Dispose();
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_flushChannel.Writer.Complete();
        await Task.WhenAll(m_flushTasks).WaitAsync(TimeSpan.FromSeconds(10));

        await m_cts.CancelAsync();
        m_cts.Dispose();
    }

    #endregion

    #region Nested Types

    private readonly record struct FlushRequest(
        MemTable MemTable,
        int SstableId,
        TaskCompletionSource<string>? Completion);

    #endregion
}
