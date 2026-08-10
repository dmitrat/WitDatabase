using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;

namespace OutWit.Database.Statements;

/// <summary>
/// Transaction control statement execution.
/// </summary>
public sealed partial class StatementExecutor
{
    #region BEGIN TRANSACTION

    /// <summary>
    /// Executes BEGIN TRANSACTION statement.
    /// Starts a new transaction with the database.
    /// Note: WitSqlStatementBeginTransaction does not support isolation level specification.
    /// Use SET TRANSACTION ISOLATION LEVEL before BEGIN TRANSACTION if needed.
    /// </summary>
    private WitSqlResult ExecuteBeginTransaction(WitSqlStatementBeginTransaction statement)
    {
        // Use pending isolation level from SET TRANSACTION, or default to ReadCommitted.
        //
        // Read from the DATABASE and not from the execution context: a context is built per Execute
        // call, so while this lived there the level survived only when both statements arrived in
        // one batch. A driver that sends them separately - which is what the ADO layer does - always
        // opened at the default, and Serializable, RepeatableRead and Snapshot all behaved as
        // ReadCommitted. The transaction and the store were never at fault.
        Core.Interfaces.WitIsolationLevel isolationLevel;

        if (m_context.Database.PendingIsolationLevel.HasValue)
        {
            isolationLevel = m_context.Database.PendingIsolationLevel.Value;
            m_context.Database.PendingIsolationLevel = null; // Consume the pending level
        }
        else
        {
            isolationLevel = Core.Interfaces.WitIsolationLevel.ReadCommitted;
        }

        m_context.Database.BeginTransaction(isolationLevel);
        return new WitSqlResult();
    }

    #endregion

    #region COMMIT

    /// <summary>
    /// Executes COMMIT statement.
    /// Commits the current transaction, making all changes permanent.
    /// </summary>
    private WitSqlResult ExecuteCommit(WitSqlStatementCommit statement)
    {
        m_context.Database.Commit();
        return new WitSqlResult();
    }

    #endregion

    #region ROLLBACK

    /// <summary>
    /// Executes ROLLBACK statement.
    /// Rolls back the current transaction or to a named savepoint.
    /// </summary>
    private WitSqlResult ExecuteRollback(WitSqlStatementRollback statement)
    {
        if (!string.IsNullOrEmpty(statement.SavepointName))
        {
            // ROLLBACK TO SAVEPOINT
            m_context.Database.RollbackToSavepoint(statement.SavepointName);
        }
        else
        {
            // ROLLBACK (entire transaction)
            m_context.Database.Rollback();
        }
        return new WitSqlResult();
    }

    #endregion

    #region SAVEPOINT

    /// <summary>
    /// Executes SAVEPOINT statement.
    /// Creates a savepoint within the current transaction.
    /// </summary>
    private WitSqlResult ExecuteSavepoint(WitSqlStatementSavepoint statement)
    {
        if (string.IsNullOrEmpty(statement.Name))
        {
            throw new InvalidOperationException("Savepoint name cannot be null or empty.");
        }
        
        m_context.Database.CreateSavepoint(statement.Name);
        return new WitSqlResult();
    }

    #endregion

    #region RELEASE SAVEPOINT

    /// <summary>
    /// Executes RELEASE SAVEPOINT statement.
    /// Releases (destroys) a named savepoint.
    /// </summary>
    private WitSqlResult ExecuteReleaseSavepoint(WitSqlStatementReleaseSavepoint statement)
    {
        if (string.IsNullOrEmpty(statement.Name))
        {
            throw new InvalidOperationException("Savepoint name cannot be null or empty.");
        }
        
        m_context.Database.ReleaseSavepoint(statement.Name);
        return new WitSqlResult();
    }

    #endregion

    #region SET TRANSACTION

    /// <summary>
    /// Executes SET TRANSACTION statement.
    /// Sets transaction properties (currently only isolation level).
    /// Note: This must be executed before BEGIN TRANSACTION to take effect.
    /// </summary>
    private WitSqlResult ExecuteSetTransaction(WitSqlStatementSetTransaction statement)
    {
        // SET TRANSACTION ISOLATION LEVEL - stores the preference for the next transaction.
        // Most databases require this before BEGIN TRANSACTION.
        //
        // Stored on the DATABASE, which is per connection and lives across Execute calls - see
        // ExecuteBeginTransaction for what storing it on the per-call context cost.
        m_context.Database.PendingIsolationLevel = MapIsolationLevel(statement.IsolationLevel);
        
        return new WitSqlResult();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Maps Parser IsolationLevelType to Core IsolationLevel enum.
    /// </summary>
    private static Core.Interfaces.WitIsolationLevel MapIsolationLevel(IsolationLevelType isolationLevel)
    {
        return isolationLevel switch
        {
            IsolationLevelType.ReadUncommitted => Core.Interfaces.WitIsolationLevel.ReadUncommitted,
            IsolationLevelType.ReadCommitted => Core.Interfaces.WitIsolationLevel.ReadCommitted,
            IsolationLevelType.RepeatableRead => Core.Interfaces.WitIsolationLevel.RepeatableRead,
            IsolationLevelType.Serializable => Core.Interfaces.WitIsolationLevel.Serializable,
            IsolationLevelType.Snapshot => Core.Interfaces.WitIsolationLevel.Snapshot,
            _ => throw new ArgumentOutOfRangeException(nameof(isolationLevel), isolationLevel, "Unknown isolation level")
        };
    }

    #endregion
}
