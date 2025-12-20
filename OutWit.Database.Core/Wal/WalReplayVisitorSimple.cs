using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Wal;

/// <summary>
/// Simple replay visitor that only handles Put and Delete operations.
/// Useful for non-transactional WAL replay (e.g., LSM MemTable recovery).
/// </summary>
public class WalReplayVisitorSimple : IWalReplayVisitor
{
    #region Fields

    private readonly Action<byte[], byte[]> m_onPut;

    private readonly Action<byte[]> m_onDelete;

    #endregion

    #region Constructors

    public WalReplayVisitorSimple(Action<byte[], byte[]> onPut, Action<byte[]> onDelete)
    {
        m_onPut = onPut ?? throw new ArgumentNullException(nameof(onPut));
        m_onDelete = onDelete ?? throw new ArgumentNullException(nameof(onDelete));
    }

    #endregion

    #region Functions

    public void OnPut(long transactionId, byte[] key, byte[] value) => m_onPut(key, value);
    public void OnDelete(long transactionId, byte[] key) => m_onDelete(key);
    public void OnBeginTransaction(long transactionId) { }
    public void OnCommitTransaction(long transactionId) { }
    public void OnRollbackTransaction(long transactionId) { }

    #endregion
}