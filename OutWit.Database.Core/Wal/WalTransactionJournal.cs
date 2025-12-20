using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Wal;

/// <summary>
/// Adapter that wraps IWriteAheadLog to implement ITransactionJournal.
/// Provides transaction support for any WAL implementation.
/// </summary>
public sealed class WalTransactionJournal : ITransactionJournal
{
    #region Constants

    /// <summary>
    /// Provider key for WAL-based transaction journal.
    /// </summary>
    public const string PROVIDER_KEY = "wal";

    #endregion

    #region Fields

    private readonly IWriteAheadLog m_wal;
    private readonly bool m_ownsWal;
    private readonly long m_checkpointThreshold;
    private long m_sizeAtLastCheckpoint;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a transaction journal wrapping the specified WAL.
    /// </summary>
    /// <param name="wal">The underlying WAL implementation.</param>
    /// <param name="ownsWal">If true, disposes the WAL when this is disposed.</param>
    /// <param name="checkpointThreshold">Auto-checkpoint size threshold (0 = disabled).</param>
    public WalTransactionJournal(IWriteAheadLog wal, bool ownsWal = true, long checkpointThreshold = 1024 * 1024)
    {
        m_wal = wal ?? throw new ArgumentNullException(nameof(wal));
        m_ownsWal = ownsWal;
        m_checkpointThreshold = checkpointThreshold;
        m_sizeAtLastCheckpoint = wal.Size;
    }

    /// <summary>
    /// Creates a new WAL-based transaction journal at the specified path.
    /// </summary>
    /// <param name="filePath">Path to the WAL file.</param>
    /// <param name="encryptor">Optional encryptor for encryption.</param>
    /// <param name="createNew">If true, creates a new WAL file.</param>
    /// <param name="checkpointThreshold">Auto-checkpoint threshold.</param>
    public WalTransactionJournal(
        string filePath, 
        IBlockEncryptor? encryptor = null, 
        bool createNew = false,
        long checkpointThreshold = 1024 * 1024)
        : this(new WriteAheadLog(filePath, encryptor, createNew), ownsWal: true, checkpointThreshold)
    {
    }

    /// <summary>
    /// Creates a new WAL-based transaction journal at the specified path.
    /// Simplified constructor for factory registration.
    /// </summary>
    /// <param name="walPath">Path to the WAL file.</param>
    /// <param name="pageSize">Page size (not used by WAL, kept for consistency).</param>
    public WalTransactionJournal(string walPath, int pageSize)
        : this(walPath, encryptor: null, createNew: false)
    {
    }

    #endregion

    #region ITransactionJournal Implementation

    /// <inheritdoc/>
    public void BeginTransaction(long transactionId)
    {
        ThrowIfDisposed();
        m_wal.AppendBeginTransaction(transactionId);
    }

    /// <inheritdoc/>
    public void LogPut(long transactionId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ReadOnlySpan<byte> oldValue)
    {
        ThrowIfDisposed();
        // WAL doesn't need oldValue - forward replay only
        m_wal.AppendPut(key, value, transactionId);
    }

    /// <inheritdoc/>
    public void LogDelete(long transactionId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> oldValue)
    {
        ThrowIfDisposed();
        // WAL doesn't need oldValue - forward replay only
        m_wal.AppendDelete(key, transactionId);
    }

    /// <inheritdoc/>
    public void CommitTransaction(long transactionId)
    {
        ThrowIfDisposed();
        m_wal.AppendCommitTransaction(transactionId);
    }

    /// <inheritdoc/>
    public void RollbackTransaction(long transactionId)
    {
        ThrowIfDisposed();
        m_wal.AppendRollbackTransaction(transactionId);
    }

    /// <inheritdoc/>
    public void Sync()
    {
        ThrowIfDisposed();
        m_wal.Sync();
    }

    /// <inheritdoc/>
    public int Recover(IKeyValueStore store)
    {
        ThrowIfDisposed();
        
        var visitor = new WalReplayVisitorTransactional(
            (key, value) => store.Put(key, value),
            key => store.Delete(key));
        
        m_wal.Replay(visitor);
        return visitor.ReplayedCount;
    }

    /// <inheritdoc/>
    public void Checkpoint()
    {
        ThrowIfDisposed();
        m_wal.Truncate();
        m_sizeAtLastCheckpoint = m_wal.Size;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the file path of this journal.
    /// </summary>
    public string FilePath => m_wal.FilePath;

    /// <summary>
    /// Gets the current size of the journal file in bytes.
    /// </summary>
    public long Size => m_wal.Size;

    /// <summary>
    /// Gets whether this journal is encrypted.
    /// </summary>
    public bool IsEncrypted => m_wal.IsEncrypted;

    /// <summary>
    /// Gets the auto-checkpoint threshold in bytes.
    /// </summary>
    public long CheckpointThreshold => m_checkpointThreshold;

    /// <summary>
    /// Gets whether auto-checkpoint is due (size exceeds threshold).
    /// </summary>
    public bool NeedsCheckpoint => m_checkpointThreshold > 0 && Size > m_sizeAtLastCheckpoint + m_checkpointThreshold;

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

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

        if (m_ownsWal)
        {
            m_wal.Dispose();
        }
    }

    #endregion
}
