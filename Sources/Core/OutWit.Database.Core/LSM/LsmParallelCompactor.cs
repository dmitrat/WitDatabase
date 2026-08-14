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

    /// <summary>
    /// Jobs submitted and not yet finished - queued AND running.
    /// </summary>
    /// <remarks>
    /// The queue's own depth cannot answer this: a job leaves the queue when a worker PICKS IT UP,
    /// which is the moment the expensive part begins. See <see cref="WaitForAllAsync"/>.
    /// </remarks>
    private int m_inFlight;

    /// <summary>
    /// Completed while nothing is in flight, so a waiter awaits it instead of polling.
    /// </summary>
    /// <remarks>
    /// <c>RunContinuationsAsynchronously</c> deliberately: without it the continuation runs on the
    /// worker thread that finished the last compaction, which is the shape that once cost this
    /// repository a 1,004 ms <c>Cancel()</c>.
    /// </remarks>
    private volatile TaskCompletionSource m_idle = AlreadyIdle();

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

        // Counted BEFORE the write, or a worker can take the job and finish it between the write
        // and the increment - which would leave the counter above zero for ever.
        Enter();

        if (m_jobChannel.Writer.TryWrite(job))
            return true;

        Leave();
        return false;
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

        Enter();

        try
        {
            await m_jobChannel.Writer.WriteAsync(job, cancellationToken);
        }
        catch
        {
            Leave();
            throw;
        }

        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Waits for all pending compactions to complete.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This waited on the QUEUE and then slept, and the two are not the same thing.</b> It polled
    /// <c>Reader.Count</c> - jobs still waiting to be picked up - so it returned as soon as a worker
    /// had DEQUEUED the last job, with the compaction itself still running. Its own comment admitted
    /// as much and added a 100 ms delay "to ensure in-progress compactions complete", which is a
    /// guess about how long a compaction takes.
    /// </para>
    /// <para>
    /// On a loaded CI runner the guess is wrong, and it was: a callback that had not run yet made
    /// <c>TrySubmitCompactsFilesTest</c> read 0 results of 1. Eight local runs of that fixture in
    /// isolation passed, which is what this class of defect looks like from the wrong end. It is
    /// public API, so every consumer got the same sleep-and-hope.
    /// </para>
    /// <para>
    /// It waits on a count of jobs that are queued OR running now, and that count reaches zero only
    /// after the completion callback has been invoked.
    /// </para>
    /// </remarks>
    public async Task WaitForAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Read once. If nothing is outstanding now this is already completed and the call returns;
        // if something is, this is the source that completes when THAT work drains. A job submitted
        // after this line belongs to the next wait, which is what the name promises.
        await m_idle.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                finally
                {
                    // After the callback, not before: "all the work is done" has to include the
                    // caller's own handler, which is the only thing a TrySubmit caller can observe.
                    Leave();
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

    /// <summary>One more job outstanding; the compactor is no longer idle.</summary>
    private void Enter()
    {
        if (Interlocked.Increment(ref m_inFlight) == 1)
            m_idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>One fewer; when the last one goes, waiters are released.</summary>
    private void Leave()
    {
        if (Interlocked.Decrement(ref m_inFlight) == 0)
            m_idle.TrySetResult();
    }

    private static TaskCompletionSource AlreadyIdle()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();

        return source;
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
