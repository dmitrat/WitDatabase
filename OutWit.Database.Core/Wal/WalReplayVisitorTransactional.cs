using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Wal;

/// <summary>
/// Transactional replay visitor that tracks transaction state.
/// Only applies operations from committed transactions.
/// </summary>
public class WalReplayVisitorTransactional : IWalReplayVisitor
{
    #region Fields

    private readonly Action<byte[], byte[]> m_onPut;
    private readonly Action<byte[]> m_onDelete;
    private readonly Dictionary<long, List<(bool IsPut, byte[] Key, byte[]? Value)>> m_pendingOps = new();
    private readonly HashSet<long> m_committed = new();

    #endregion

    #region Constructors

    public WalReplayVisitorTransactional(Action<byte[], byte[]> onPut, Action<byte[]> onDelete)
    {
        m_onPut = onPut ?? throw new ArgumentNullException(nameof(onPut));
        m_onDelete = onDelete ?? throw new ArgumentNullException(nameof(onDelete));
    }

    #endregion

    #region Functions

    public void OnPut(long transactionId, byte[] key, byte[] value)
    {
        if (transactionId == 0)
        {
            // Non-transactional - apply immediately
            m_onPut(key, value);
            ReplayedCount++;
        }
        else
        {
            // Buffer for transaction
            if (!m_pendingOps.TryGetValue(transactionId, out var ops))
            {
                ops = new List<(bool, byte[], byte[]?)>();
                m_pendingOps[transactionId] = ops;
            }
            ops.Add((true, key, value));
        }
    }

    public void OnDelete(long transactionId, byte[] key)
    {
        if (transactionId == 0)
        {
            m_onDelete(key);
            ReplayedCount++;
        }
        else
        {
            if (!m_pendingOps.TryGetValue(transactionId, out var ops))
            {
                ops = new List<(bool, byte[], byte[]?)>();
                m_pendingOps[transactionId] = ops;
            }
            ops.Add((false, key, null));
        }
    }

    public void OnBeginTransaction(long transactionId)
    {
        m_pendingOps[transactionId] = new List<(bool, byte[], byte[]?)>();
    }

    public void OnCommitTransaction(long transactionId)
    {
        m_committed.Add(transactionId);
        
        // Apply buffered operations
        if (m_pendingOps.TryGetValue(transactionId, out var ops))
        {
            foreach (var (isPut, key, value) in ops)
            {
                if (isPut)
                    m_onPut(key, value!);
                else
                    m_onDelete(key);
                ReplayedCount++;
            }
            m_pendingOps.Remove(transactionId);
        }
    }

    public void OnRollbackTransaction(long transactionId)
    {
        m_pendingOps.Remove(transactionId);
    }

    #endregion

    #region Properties

    public int ReplayedCount { get; private set; }

    #endregion
}