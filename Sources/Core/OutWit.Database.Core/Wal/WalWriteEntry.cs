using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Wal;

/// <summary>
/// Represents a pending WAL write entry.
/// Used by <see cref="WalBatchCommitter"/> for group commit operations.
/// </summary>
public sealed class WalWriteEntry
{
    #region Constructors

    /// <summary>
    /// Creates a new WAL write entry.
    /// </summary>
    private WalWriteEntry(WalEntryType type, long transactionId, byte[]? key, byte[]? value)
    {
        Type = type;
        TransactionId = transactionId;
        Key = key;
        Value = value;
        CompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a Put entry.
    /// </summary>
    public static WalWriteEntry CreatePut(long transactionId, byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        return new WalWriteEntry(WalEntryType.Put, transactionId, key, value);
    }

    /// <summary>
    /// Creates a Delete entry.
    /// </summary>
    public static WalWriteEntry CreateDelete(long transactionId, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new WalWriteEntry(WalEntryType.Delete, transactionId, key, null);
    }

    /// <summary>
    /// Creates a BeginTransaction entry.
    /// </summary>
    public static WalWriteEntry CreateBeginTransaction(long transactionId)
    {
        return new WalWriteEntry(WalEntryType.BeginTransaction, transactionId, null, null);
    }

    /// <summary>
    /// Creates a CommitTransaction entry.
    /// </summary>
    public static WalWriteEntry CreateCommitTransaction(long transactionId)
    {
        return new WalWriteEntry(WalEntryType.CommitTransaction, transactionId, null, null);
    }

    /// <summary>
    /// Creates a RollbackTransaction entry.
    /// </summary>
    public static WalWriteEntry CreateRollbackTransaction(long transactionId)
    {
        return new WalWriteEntry(WalEntryType.RollbackTransaction, transactionId, null, null);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the entry type.
    /// </summary>
    public WalEntryType Type { get; }

    /// <summary>
    /// Gets the transaction ID.
    /// </summary>
    public long TransactionId { get; }

    /// <summary>
    /// Gets the key (for Put/Delete entries).
    /// </summary>
    public byte[]? Key { get; }

    /// <summary>
    /// Gets the value (for Put entries).
    /// </summary>
    public byte[]? Value { get; }

    /// <summary>
    /// Gets the completion source for async wait.
    /// </summary>
    public TaskCompletionSource<bool> CompletionSource { get; }

    /// <summary>
    /// Gets whether this entry requires immediate flush (e.g., commits).
    /// </summary>
    public bool RequiresFlush => Type == WalEntryType.CommitTransaction;

    #endregion
}
