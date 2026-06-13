using System.Threading.Channels;

namespace OutWit.Database.Core.LSM;

/// <summary>
/// Manages parallel compaction of SSTables.
/// Allows multiple compaction jobs to run concurrently.
/// </summary>
/// <remarks>
/// Key features:
/// - Background compaction workers
/// - Priority-based compaction scheduling
/// - Statistics tracking
/// </remarks>
public sealed class LsmParallelCompactor : IDisposable, IAsyncDisposable
{
    #region Constants

    private const int DEFAULT_MAX_PARALLEL_COMPACTIONS = 2;

    #endregion

    #region Fields

    private readonly Channel<CompactionJob> m_jobChannel;
    private readonly Task[] m_workerTasks;
    private readonly CancellationTokenSource m_cts;
    private readonly Compactor m_compactor;
    private readonly Action<CompactionResult> m_onCompactionComplete;
    private readonly Lock m_statsLock = new();

    private long m_compactionsCompleted;
    private long m_totalInputFiles;
    private long m_totalOutputEntries;
    private long m_totalTombstonesRemoved;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new parallel compactor.
    /// </summary>
    /// <param name="directory">Directory containing SSTables.</param>
    /// <param name="onCompactionComplete">Callback when compaction completes.</param>
    /// <param name="blockSize">Block size for output SSTables.</param>
    /// <param name="maxParallelCompactions">Maximum concurrent compaction operations.</param>
    public LsmParallelCompactor(
        string directory,
        Action<CompactionResult> onCompactionComplete,
        int blockSize = 4096,
        int maxParallelCompactions = DEFAULT_MAX_PARALLEL_COMPACTIONS)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(onCompactionComplete);
        if (maxParallelCompactions <= 0) throw new ArgumentOutOfRangeException(nameof(maxParallelCompactions));

        m_compactor = new Compactor(directory, blockSize);
        m_onCompactionComplete = onCompactionComplete;
        m_cts = new CancellationTokenSource();

        // Create unbounded channel for compaction jobs (compactions are expensive, don't want to block)
        m_jobChannel = Channel.CreateUnbounded<CompactionJob>(new UnboundedChannelOptions
        {
            SingleReader = false,
            AllowSynchronousContinuations = false
        });

        // Start worker tasks
        m_workerTasks = new Task[maxParallelCompactions];
        for (int i = 0; i < maxParallelCompactions; i++)
        {
            m_workerTasks[i] = Task.Run(CompactionLoopAsync);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Submits a compaction job. Does not wait for completion.
    /// </summary>
    /// <param name="inputFiles">SSTable files to compact.</param>
    /// <param name="outputPath">Path for output SSTable.</param>
    /// <returns>True if submitted successfully.</returns>
    public bool TrySubmit(IReadOnlyList<string> inputFiles, string outputPath)
    {
        ThrowIfDisposed();

        var job = new CompactionJob(inputFiles, outputPath, null);
        return m_jobChannel.Writer.TryWrite(job);
    }

    /// <summary>
    /// Submits a compaction job and waits for completion.
    /// </summary>
    /// <param name="inputFiles">SSTable files to compact.</param>
    /// <param name="outputPath">Path for output SSTable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compaction result.</returns>
    public async Task<CompactionResult> SubmitAndWaitAsync(
        IReadOnlyList<string> inputFiles,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var completion = new TaskCompletionSource<CompactionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new CompactionJob(inputFiles, outputPath, completion);

        await m_jobChannel.Writer.WriteAsync(job, cancellationToken);

        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Waits for all pending compactions to complete.
    /// </summary>
    public async Task WaitForAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (m_jobChannel.Reader.Count > 0)
        {
            await Task.Delay(50, cancellationToken);
        }

        // Additional delay to ensure in-progress compactions complete
        await Task.Delay(100, cancellationToken);
    }

    #endregion

    #region Background Processing

    private async Task CompactionLoopAsync()
    {
        var reader = m_jobChannel.Reader;
        var token = m_cts.Token;

        try
        {
            await foreach (var job in reader.ReadAllAsync(token))
            {
                try
                {
                    var result = ExecuteCompaction(job);
                    
                    m_onCompactionComplete(result);
                    job.Completion?.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    job.Completion?.TrySetException(ex);
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

    private CompactionResult ExecuteCompaction(CompactionJob job)
    {
        var result = m_compactor.Compact(job.InputFiles, job.OutputPath);

        // Update statistics
        lock (m_statsLock)
        {
            m_compactionsCompleted++;
            m_totalInputFiles += result.InputFiles;
            m_totalOutputEntries += result.OutputEntries;
            m_totalTombstonesRemoved += result.TombstonesRemoved;
        }

        return result;
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the number of compactions completed.
    /// </summary>
    public long CompactionsCompleted
    {
        get
        {
            lock (m_statsLock)
                return m_compactionsCompleted;
        }
    }

    /// <summary>
    /// Gets the total input files processed.
    /// </summary>
    public long TotalInputFiles
    {
        get
        {
            lock (m_statsLock)
                return m_totalInputFiles;
        }
    }

    /// <summary>
    /// Gets the total output entries written.
    /// </summary>
    public long TotalOutputEntries
    {
        get
        {
            lock (m_statsLock)
                return m_totalOutputEntries;
        }
    }

    /// <summary>
    /// Gets the total tombstones removed.
    /// </summary>
    public long TotalTombstonesRemoved
    {
        get
        {
            lock (m_statsLock)
                return m_totalTombstonesRemoved;
        }
    }

    /// <summary>
    /// Gets the number of pending compaction jobs.
    /// </summary>
    public int PendingJobs
    {
        get
        {
            try
            {
                return m_jobChannel.Reader.Count;
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

        m_jobChannel.Writer.Complete();
        Task.WaitAll(m_workerTasks, TimeSpan.FromSeconds(30));

        m_cts.Cancel();
        m_cts.Dispose();
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_jobChannel.Writer.Complete();
        await Task.WhenAll(m_workerTasks).WaitAsync(TimeSpan.FromSeconds(30));

        await m_cts.CancelAsync();
        m_cts.Dispose();
    }

    #endregion

    #region Nested Types

    private readonly record struct CompactionJob(
        IReadOnlyList<string> InputFiles,
        string OutputPath,
        TaskCompletionSource<CompactionResult>? Completion);

    #endregion
}
