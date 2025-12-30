using System.Threading.Channels;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Wal;

/// <summary>
/// Provides parallel write capabilities for WAL operations.
/// Multiple threads can submit writes concurrently through a lock-free queue,
/// while a single background thread serializes writes to the WAL file.
/// 
/// Key features:
/// - Lock-free write submission from multiple threads
/// - Single writer thread ensures serial disk writes
/// - Configurable parallelism options
/// - Supports both sync and async APIs
/// </summary>
/// <remarks>
/// This class enables parallel write throughput by decoupling the caller threads
/// from the actual disk I/O. Writers submit entries to a concurrent queue and
/// can optionally wait for durability confirmation.
/// 
/// For maximum throughput, use <see cref="WalBatchCommitter"/> which additionally
/// batches multiple commits into single fsync calls.
/// </remarks>
public sealed class WalParallelWriter : IDisposable, IAsyncDisposable
{
    #region Constants

    private const int DEFAULT_QUEUE_BOUND = 10_000;
    private const int DEFAULT_WAIT_TIMEOUT_MS = 30_000;

    #endregion

    #region Fields

    private readonly IWriteAheadLog m_wal;
    private readonly Channel<WalWriteEntry> m_channel;
    private readonly Task m_writerTask;
    private readonly CancellationTokenSource m_cts;
    private readonly int m_waitTimeoutMs;

    private long m_entriesSubmitted;
    private long m_entriesWritten;
    private long m_syncCount;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new parallel writer wrapping the specified WAL.
    /// </summary>
    /// <param name="wal">The underlying WAL implementation.</param>
    /// <param name="queueBound">Maximum pending entries (0 = unbounded).</param>
    /// <param name="waitTimeoutMs">Default timeout for sync operations.</param>
    public WalParallelWriter(
        IWriteAheadLog wal,
        int queueBound = DEFAULT_QUEUE_BOUND,
        int waitTimeoutMs = DEFAULT_WAIT_TIMEOUT_MS)
    {
        ArgumentNullException.ThrowIfNull(wal);
        if (queueBound < 0) throw new ArgumentOutOfRangeException(nameof(queueBound));

        m_wal = wal;
        m_waitTimeoutMs = waitTimeoutMs;
        m_cts = new CancellationTokenSource();

        // Create channel - bounded or unbounded
        m_channel = queueBound > 0
            ? Channel.CreateBounded<WalWriteEntry>(new BoundedChannelOptions(queueBound)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            })
            : Channel.CreateUnbounded<WalWriteEntry>(new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = false
            });

        // Start background writer
        m_writerTask = Task.Run(WriteLoopAsync);
    }

    #endregion

    #region Write Operations

    /// <summary>
    /// Submits a Put operation. Does not wait for completion.
    /// </summary>
    /// <returns>True if submitted successfully.</returns>
    public bool TryPut(long transactionId, byte[] key, byte[] value)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreatePut(transactionId, key, value);
        var result = m_channel.Writer.TryWrite(entry);
        if (result) Interlocked.Increment(ref m_entriesSubmitted);
        return result;
    }

    /// <summary>
    /// Submits a Put operation and waits for it to be written.
    /// </summary>
    public async Task PutAsync(long transactionId, byte[] key, byte[] value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreatePut(transactionId, key, value);
        await SubmitAndWaitAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Submits a Delete operation. Does not wait for completion.
    /// </summary>
    public bool TryDelete(long transactionId, byte[] key)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateDelete(transactionId, key);
        var result = m_channel.Writer.TryWrite(entry);
        if (result) Interlocked.Increment(ref m_entriesSubmitted);
        return result;
    }

    /// <summary>
    /// Submits a Delete operation and waits for it to be written.
    /// </summary>
    public async Task DeleteAsync(long transactionId, byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateDelete(transactionId, key);
        await SubmitAndWaitAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Submits a BeginTransaction and waits for confirmation.
    /// </summary>
    public async Task BeginTransactionAsync(long transactionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateBeginTransaction(transactionId);
        await SubmitAndWaitAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Submits a CommitTransaction and waits for durability.
    /// This ensures the commit is synced to disk before returning.
    /// </summary>
    public async Task CommitTransactionAsync(long transactionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateCommitTransaction(transactionId);
        await SubmitAndWaitAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Submits a RollbackTransaction.
    /// </summary>
    public async Task RollbackTransactionAsync(long transactionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateRollbackTransaction(transactionId);
        await SubmitAndWaitAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Ensures all pending entries are written and synced to disk.
    /// </summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        // Submit a commit marker to trigger sync
        var marker = WalWriteEntry.CreateCommitTransaction(-1);
        await SubmitAndWaitAsync(marker, cancellationToken);
    }

    #endregion

    #region Processing

    private async Task SubmitAndWaitAsync(WalWriteEntry entry, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref m_entriesSubmitted);
        
        await m_channel.Writer.WriteAsync(entry, cancellationToken);
        
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(m_waitTimeoutMs);
        
        try
        {
            await entry.CompletionSource.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"WAL write timed out after {m_waitTimeoutMs}ms");
        }
    }

    private async Task WriteLoopAsync()
    {
        var reader = m_channel.Reader;
        var token = m_cts.Token;

        try
        {
            await foreach (var entry in reader.ReadAllAsync(token))
            {
                try
                {
                    WriteEntry(entry);
                    
                    // Sync on commits
                    if (entry.RequiresFlush || entry.TransactionId == -1)
                    {
                        m_wal.Sync();
                        Interlocked.Increment(ref m_syncCount);
                    }

                    entry.CompletionSource.TrySetResult(true);
                    Interlocked.Increment(ref m_entriesWritten);
                }
                catch (Exception ex)
                {
                    entry.CompletionSource.TrySetException(ex);
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

    private void WriteEntry(WalWriteEntry entry)
    {
        switch (entry.Type)
        {
            case WalEntryType.Put:
                m_wal.AppendPut(entry.Key!, entry.Value!, entry.TransactionId);
                break;
            case WalEntryType.Delete:
                m_wal.AppendDelete(entry.Key!, entry.TransactionId);
                break;
            case WalEntryType.BeginTransaction:
                m_wal.AppendBeginTransaction(entry.TransactionId);
                break;
            case WalEntryType.CommitTransaction:
                if (entry.TransactionId >= 0) // Skip sync marker
                {
                    m_wal.AppendCommitTransaction(entry.TransactionId);
                }
                break;
            case WalEntryType.RollbackTransaction:
                m_wal.AppendRollbackTransaction(entry.TransactionId);
                break;
        }
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the number of entries submitted.
    /// </summary>
    public long EntriesSubmitted => Volatile.Read(ref m_entriesSubmitted);

    /// <summary>
    /// Gets the number of entries successfully written.
    /// </summary>
    public long EntriesWritten => Volatile.Read(ref m_entriesWritten);

    /// <summary>
    /// Gets the number of sync operations performed.
    /// </summary>
    public long SyncCount => Volatile.Read(ref m_syncCount);

    /// <summary>
    /// Gets the approximate number of pending entries in the queue.
    /// Returns -1 if count is not supported.
    /// </summary>
    public int PendingEntries
    {
        get
        {
            try
            {
                return m_channel.Reader.Count;
            }
            catch (NotSupportedException)
            {
                return -1;
            }
        }
    }

    /// <summary>
    /// Gets whether the writer is currently busy processing.
    /// </summary>
    public bool IsBusy
    {
        get
        {
            var pending = PendingEntries;
            return (pending > 0 || pending == -1) && !m_writerTask.IsCompleted;
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

    /// <summary>
    /// Disposes the parallel writer.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_channel.Writer.Complete();
        m_cts.Cancel();
        
        m_writerTask.Wait(TimeSpan.FromSeconds(5));
        
        m_cts.Dispose();
    }

    #endregion

    #region IAsyncDisposable

    /// <summary>
    /// Asynchronously disposes the parallel writer.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_channel.Writer.Complete();
        await m_cts.CancelAsync();

        await m_writerTask.WaitAsync(TimeSpan.FromSeconds(5));
        
        m_cts.Dispose();
    }

    #endregion
}
