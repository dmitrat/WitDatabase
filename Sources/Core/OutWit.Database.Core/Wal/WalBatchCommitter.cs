using System.Threading.Channels;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Wal;

/// <summary>
/// Provides batched (group) commit functionality for WAL operations.
/// Multiple writers can submit entries concurrently, and commits are batched together
/// for improved throughput while maintaining ACID guarantees.
/// 
/// Key features:
/// - Lock-free entry submission via Channel
/// - Configurable batch size and timeout
/// - Automatic flush on commit entries
/// - Background processing thread
/// </summary>
/// <remarks>
/// Group commit significantly improves throughput when many transactions commit
/// simultaneously by batching fsync() calls. Instead of one fsync per transaction,
/// multiple commits share a single fsync.
/// </remarks>
public sealed class WalBatchCommitter : IDisposable, IAsyncDisposable
{
    #region Constants

    private const int DEFAULT_MAX_BATCH_SIZE = 100;
    private const int DEFAULT_BATCH_TIMEOUT_MS = 10;

    #endregion

    #region Fields

    private readonly IWriteAheadLog m_wal;
    private readonly Channel<WalWriteEntry> m_channel;
    private readonly Task m_processingTask;
    private readonly CancellationTokenSource m_cts;
    private readonly int m_maxBatchSize;
    private readonly int m_batchTimeoutMs;
    private readonly Lock m_statsLock = new();
    
    private long m_entriesWritten;
    private long m_batchesCommitted;
    private long m_totalLatencyTicks;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new batch committer wrapping the specified WAL.
    /// </summary>
    /// <param name="wal">The underlying WAL implementation.</param>
    /// <param name="maxBatchSize">Maximum entries per batch before forced flush.</param>
    /// <param name="batchTimeoutMs">Maximum wait time in ms before flushing a partial batch.</param>
    public WalBatchCommitter(
        IWriteAheadLog wal, 
        int maxBatchSize = DEFAULT_MAX_BATCH_SIZE,
        int batchTimeoutMs = DEFAULT_BATCH_TIMEOUT_MS)
    {
        ArgumentNullException.ThrowIfNull(wal);
        if (maxBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxBatchSize));
        if (batchTimeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(batchTimeoutMs));

        m_wal = wal;
        m_maxBatchSize = maxBatchSize;
        m_batchTimeoutMs = batchTimeoutMs;
        m_cts = new CancellationTokenSource();

        // Unbounded channel for lock-free submission
        m_channel = Channel.CreateUnbounded<WalWriteEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        // Start background processing
        m_processingTask = Task.Run(ProcessEntriesAsync);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Submits a Put entry to the WAL.
    /// Returns immediately; entry is written asynchronously.
    /// </summary>
    public ValueTask<bool> SubmitPutAsync(long transactionId, byte[] key, byte[] value)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreatePut(transactionId, key, value);
        return SubmitEntryAsync(entry);
    }

    /// <summary>
    /// Submits a Delete entry to the WAL.
    /// </summary>
    public ValueTask<bool> SubmitDeleteAsync(long transactionId, byte[] key)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateDelete(transactionId, key);
        return SubmitEntryAsync(entry);
    }

    /// <summary>
    /// Submits a BeginTransaction entry to the WAL.
    /// </summary>
    public ValueTask<bool> SubmitBeginTransactionAsync(long transactionId)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateBeginTransaction(transactionId);
        return SubmitEntryAsync(entry);
    }

    /// <summary>
    /// Submits a CommitTransaction entry to the WAL.
    /// This triggers a flush to ensure durability.
    /// </summary>
    public ValueTask<bool> SubmitCommitTransactionAsync(long transactionId)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateCommitTransaction(transactionId);
        return SubmitEntryAsync(entry);
    }

    /// <summary>
    /// Submits a RollbackTransaction entry to the WAL.
    /// </summary>
    public ValueTask<bool> SubmitRollbackTransactionAsync(long transactionId)
    {
        ThrowIfDisposed();
        var entry = WalWriteEntry.CreateRollbackTransaction(transactionId);
        return SubmitEntryAsync(entry);
    }

    /// <summary>
    /// Flushes all pending entries and ensures they are persisted.
    /// Blocks until all pending entries are written and synced.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        // Create a marker entry to track when flush is complete
        var flushMarker = WalWriteEntry.CreateCommitTransaction(-1); // Special marker
        
        if (!m_channel.Writer.TryWrite(flushMarker))
        {
            throw new InvalidOperationException("Failed to submit flush marker");
        }

        await flushMarker.CompletionSource.Task.WaitAsync(cancellationToken);
    }

    #endregion

    #region Processing

    private async ValueTask<bool> SubmitEntryAsync(WalWriteEntry entry)
    {
        if (!m_channel.Writer.TryWrite(entry))
        {
            entry.CompletionSource.SetException(
                new InvalidOperationException("Failed to submit entry to WAL"));
            return false;
        }

        return await entry.CompletionSource.Task;
    }

    private async Task ProcessEntriesAsync()
    {
        var batch = new List<WalWriteEntry>(m_maxBatchSize);
        var reader = m_channel.Reader;
        var token = m_cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                batch.Clear();
                var startTicks = Environment.TickCount64;

                // Collect batch with timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(m_batchTimeoutMs);

                try
                {
                    // Wait for at least one entry
                    if (await reader.WaitToReadAsync(timeoutCts.Token))
                    {
                        // Read available entries up to batch size
                        while (batch.Count < m_maxBatchSize && reader.TryRead(out var entry))
                        {
                            batch.Add(entry);
                        }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // Timeout - process what we have
                }

                if (batch.Count == 0) continue;

                // Write all entries in batch
                try
                {
                    foreach (var entry in batch)
                    {
                        WriteEntry(entry);
                    }

                    // Sync to disk
                    m_wal.Sync();

                    // Mark all entries as complete
                    foreach (var entry in batch)
                    {
                        entry.CompletionSource.TrySetResult(true);
                    }

                    // Update statistics
                    lock (m_statsLock)
                    {
                        m_entriesWritten += batch.Count;
                        m_batchesCommitted++;
                        m_totalLatencyTicks += Environment.TickCount64 - startTicks;
                    }
                }
                catch (Exception ex)
                {
                    // Mark all entries as failed
                    foreach (var entry in batch)
                    {
                        entry.CompletionSource.TrySetException(ex);
                    }
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
                if (entry.TransactionId >= 0) // Skip flush marker
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
    /// Gets the total number of entries written.
    /// </summary>
    public long EntriesWritten
    {
        get
        {
            lock (m_statsLock)
                return m_entriesWritten;
        }
    }

    /// <summary>
    /// Gets the total number of batches committed.
    /// </summary>
    public long BatchesCommitted
    {
        get
        {
            lock (m_statsLock)
                return m_batchesCommitted;
        }
    }

    /// <summary>
    /// Gets the average entries per batch.
    /// </summary>
    public double AverageEntriesPerBatch
    {
        get
        {
            lock (m_statsLock)
            {
                return m_batchesCommitted > 0 
                    ? (double)m_entriesWritten / m_batchesCommitted 
                    : 0;
            }
        }
    }

    /// <summary>
    /// Gets the average batch latency in milliseconds.
    /// </summary>
    public double AverageBatchLatencyMs
    {
        get
        {
            lock (m_statsLock)
            {
                return m_batchesCommitted > 0 
                    ? (double)m_totalLatencyTicks / m_batchesCommitted 
                    : 0;
            }
        }
    }

    /// <summary>
    /// Gets the approximate number of pending entries in the queue.
    /// Note: This may not be exact due to concurrent access.
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
                return -1; // Count not supported
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

    /// <summary>
    /// Disposes the batch committer, flushing pending entries.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        // Signal shutdown
        m_channel.Writer.Complete();
        m_cts.Cancel();

        // Wait for processing to complete with timeout
        m_processingTask.Wait(TimeSpan.FromSeconds(5));

        m_cts.Dispose();
    }

    #endregion

    #region IAsyncDisposable

    /// <summary>
    /// Asynchronously disposes the batch committer.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        // Signal shutdown
        m_channel.Writer.Complete();
        await m_cts.CancelAsync();

        // Wait for processing to complete
        await m_processingTask.WaitAsync(TimeSpan.FromSeconds(5));

        m_cts.Dispose();
    }

    #endregion
}
