using OutWit.Database.Transactions;

namespace OutWit.Database;

/// <summary>
/// Transaction management operations for WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Transaction Control

    /// <summary>
    /// Begin a new transaction.
    /// </summary>
    /// <returns>A disposable handle that will auto-rollback if not committed.</returns>
    public IDisposable BeginTransaction()
    {
        m_currentTransaction = m_database.BeginTransaction();
        return new TransactionHandle(this);
    }

    /// <summary>
    /// Commit the current transaction.
    /// </summary>
    public void Commit()
    {
        m_currentTransaction?.Commit();
        m_currentTransaction?.Dispose();
        m_currentTransaction = null;
    }

    /// <summary>
    /// Rollback the current transaction.
    /// </summary>
    public void Rollback()
    {
        m_currentTransaction?.Rollback();
        m_currentTransaction?.Dispose();
        m_currentTransaction = null;
    }

    #endregion
}
