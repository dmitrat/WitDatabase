using OutWit.Database.Core.Interfaces;
using OutWit.Database.Transactions;

namespace OutWit.Database.Engine;

/// <summary>
/// Transaction management operations for WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Fields for Savepoint Tracking

    /// <summary>
    /// Tracks row count deltas per table since the last savepoint.
    /// Key = table name, Value = delta (positive for inserts, negative for deletes).
    /// </summary>
    private Dictionary<string, long>? m_rowCountDeltaSinceSavepoint;

    #endregion

    #region Transaction Control

    /// <summary>
    /// Begin a new transaction with default isolation level.
    /// </summary>
    /// <returns>A disposable handle that will auto-rollback if not committed.</returns>
    public IDisposable BeginTransaction()
    {
        return BeginTransaction(WitIsolationLevel.ReadCommitted);
    }

    /// <summary>
    /// Begin a new transaction with specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction.</param>
    /// <returns>A disposable handle that will auto-rollback if not committed.</returns>
    public IDisposable BeginTransaction(WitIsolationLevel isolationLevel)
    {
        if (m_currentTransaction != null)
            throw new InvalidOperationException("A transaction is already active. Commit or rollback it first.");

        m_currentTransaction = m_database.BeginTransaction(isolationLevel);
        m_rowCountDeltaSinceSavepoint = null;
        return new TransactionHandle(this);
    }

    /// <summary>
    /// Commit the current transaction.
    /// </summary>
    public void Commit()
    {
        if (m_currentTransaction == null)
            return;

        m_currentTransaction.Commit();
        m_currentTransaction.Dispose();
        m_currentTransaction = null;
        m_rowCountDeltaSinceSavepoint = null;
    }

    /// <summary>
    /// Rollback the current transaction.
    /// </summary>
    public void Rollback()
    {
        if (m_currentTransaction == null)
            return;

        m_currentTransaction.Rollback();
        m_currentTransaction.Dispose();
        m_currentTransaction = null;
        m_rowCountDeltaSinceSavepoint = null;
        
        // Reload cached metadata (row counts, row IDs) from the store
        // to ensure they reflect the actual persisted state after rollback.
        m_schema.ReloadMetadataFromStore();
    }

    #endregion

    #region Savepoints

    /// <summary>
    /// Create a savepoint within the current transaction.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    public void CreateSavepoint(string name)
    {
        if (m_currentTransaction == null)
            throw new InvalidOperationException("No active transaction. Begin a transaction first.");

        if (m_currentTransaction is ITransactionWithSavepoints txWithSavepoints)
        {
            txWithSavepoints.CreateSavepoint(name);
            
            // Start tracking row count deltas from this savepoint
            m_rowCountDeltaSinceSavepoint = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            throw new NotSupportedException("Current transaction does not support savepoints.");
        }
    }

    /// <summary>
    /// Release a savepoint within the current transaction.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    public void ReleaseSavepoint(string name)
    {
        if (m_currentTransaction == null)
            throw new InvalidOperationException("No active transaction.");

        if (m_currentTransaction is ITransactionWithSavepoints txWithSavepoints)
        {
            txWithSavepoints.ReleaseSavepoint(name);
            // Keep tracking - there might be nested savepoints or more operations
        }
        else
        {
            throw new NotSupportedException("Current transaction does not support savepoints.");
        }
    }

    /// <summary>
    /// Rollback to a savepoint within the current transaction.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    public void RollbackToSavepoint(string name)
    {
        if (m_currentTransaction == null)
            throw new InvalidOperationException("No active transaction.");

        if (m_currentTransaction is ITransactionWithSavepoints txWithSavepoints)
        {
            txWithSavepoints.RollbackToSavepoint(name);
            
            // Revert the row count cache using tracked deltas - O(modified tables), not O(rows)
            if (m_rowCountDeltaSinceSavepoint != null)
            {
                foreach (var (tableName, delta) in m_rowCountDeltaSinceSavepoint)
                {
                    // Subtract the delta to revert to savepoint state
                    // If delta was +2 (2 inserts), we subtract 2
                    // If delta was -1 (1 delete), we add 1 back
                    m_schema.AdjustRowCountCache(tableName, -delta);
                }
                
                // Reset tracking for potential future savepoints
                m_rowCountDeltaSinceSavepoint.Clear();
            }
        }
        else
        {
            throw new NotSupportedException("Current transaction does not support savepoints.");
        }
    }

    /// <summary>
    /// Tracks a row count change for savepoint rollback support.
    /// Called internally by DML operations when a savepoint is active.
    /// </summary>
    /// <param name="tableName">The table that was modified.</param>
    /// <param name="delta">The row count change (+1 for insert, -1 for delete).</param>
    internal void TrackRowCountDelta(string tableName, long delta)
    {
        if (m_rowCountDeltaSinceSavepoint == null)
            return;
            
        if (m_rowCountDeltaSinceSavepoint.TryGetValue(tableName, out var current))
        {
            m_rowCountDeltaSinceSavepoint[tableName] = current + delta;
        }
        else
        {
            m_rowCountDeltaSinceSavepoint[tableName] = delta;
        }
    }

    #endregion
}
