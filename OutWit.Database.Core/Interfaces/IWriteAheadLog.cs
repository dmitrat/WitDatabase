namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// Interface for Write-Ahead Log implementations.
/// Provides durability for key-value operations by writing to persistent storage
/// before applying changes to the main data structure.
/// </summary>
public interface IWriteAheadLog : IDisposable
{
    /// <summary>
    /// Appends a Put operation to the log.
    /// </summary>
    /// <param name="key">The key to put.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <param name="transactionId">Optional transaction ID (0 = no transaction).</param>
    void AppendPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, long transactionId = 0);
    
    /// <summary>
    /// Appends a Delete operation to the log.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="transactionId">Optional transaction ID (0 = no transaction).</param>
    void AppendDelete(ReadOnlySpan<byte> key, long transactionId = 0);
    
    /// <summary>
    /// Appends a transaction begin marker.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    void AppendBeginTransaction(long transactionId);
    
    /// <summary>
    /// Appends a transaction commit marker.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    void AppendCommitTransaction(long transactionId);
    
    /// <summary>
    /// Appends a transaction rollback marker.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    void AppendRollbackTransaction(long transactionId);
    
    /// <summary>
    /// Ensures all pending writes are flushed to disk.
    /// </summary>
    void Sync();
    
    /// <summary>
    /// Truncates the WAL, removing all entries.
    /// Should be called after successful checkpoint/flush.
    /// </summary>
    void Truncate();
    
    /// <summary>
    /// Replays all entries in the log.
    /// </summary>
    /// <param name="visitor">Visitor to receive replay callbacks.</param>
    /// <returns>Number of entries replayed.</returns>
    int Replay(IWalReplayVisitor visitor);

    /// <summary>
    /// Gets the file path of this WAL.
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// Gets the current size of the WAL file in bytes.
    /// </summary>
    long Size { get; }

    /// <summary>
    /// Gets whether this WAL is encrypted.
    /// </summary>
    bool IsEncrypted { get; }

    /// <summary>
    /// Gets the number of entries written to this WAL.
    /// </summary>
    long EntryCount { get; }
}