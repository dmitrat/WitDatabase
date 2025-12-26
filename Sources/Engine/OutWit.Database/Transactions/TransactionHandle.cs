using OutWit.Database.Interfaces;

namespace OutWit.Database.Transactions;

/// <summary>
/// A disposable handle for transaction management.
/// Auto-rollbacks the transaction if not committed before disposal.
/// </summary>
public sealed class TransactionHandle : IDisposable
{
    #region Fields

    private readonly ITransactionManager m_manager;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new transaction handle.
    /// </summary>
    /// <param name="manager">The transaction manager.</param>
    public TransactionHandle(ITransactionManager manager)
    {
        m_manager = manager;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the handle and auto-rollbacks if transaction is still active.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;

        // Auto-rollback if not committed
        if (m_manager.Transaction != null)
            m_manager.Rollback();
    }

    #endregion
}
