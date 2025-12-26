using OutWit.Database.Context;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser;
using OutWit.Database.Schema;
using OutWit.Database.Statements;
using OutWit.Database.Values;

namespace OutWit.Database;

/// <summary>
/// The main SQL execution engine for WitDatabase.
/// Provides query execution, DDL/DML operations, and transaction management.
/// </summary>
public sealed partial class WitSqlEngine : IDatabase, IDisposable, ITransactionManager
{
    #region Fields

    private readonly WitDatabase m_database;
    private readonly SchemaCatalog m_schema;
    private readonly bool m_ownsStore;
    private ITransaction? m_currentTransaction;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new WitSqlEngine instance.
    /// </summary>
    /// <param name="database">The underlying WitDatabase instance.</param>
    /// <param name="ownsStore">If true, the engine will dispose the database when disposed.</param>
    public WitSqlEngine(WitDatabase database, bool ownsStore = false)
    {
        m_database = database;
        m_schema = new SchemaCatalog(database.Store);
        m_ownsStore = ownsStore;
    }

    #endregion

    #region Execute

    /// <summary>
    /// Execute a SQL query and return the result.
    /// </summary>
    /// <param name="sql">SQL query text.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>The query result.</returns>
    public WitSqlResult Execute(string sql,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var statements = WitSql.Parse(sql);
        if (statements.Count == 0)
            throw new InvalidOperationException("No SQL statement found");

        var context = new ContextExecution
        {
            Database = this,
            CancellationToken = cancellationToken,
            LastInsertRowId = LastInsertRowId,
            LastChangesCount = LastChangesCount
        };

        // Add parameters
        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                var paramName = key.StartsWith("@") ? key : $"@{key}";
                context.Parameters[paramName] = WitSqlValue.FromObject(value);
            }
        }

        var executor = new StatementExecutor(context);

        // Execute all statements, return result of last one
        WitSqlResult? result = null;
        foreach (var statement in statements)
        {
            result?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            result = executor.Execute(statement);
        }

        // Persist state for next call
        LastInsertRowId = context.LastInsertRowId;
        LastChangesCount = context.LastChangesCount;

        return result!;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the engine and optionally the underlying database.
    /// </summary>
    public void Dispose()
    {
        m_currentTransaction?.Dispose();

        if (m_ownsStore)
            m_database.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Row ID of last inserted row (for LAST_INSERT_ROWID function).
    /// </summary>
    public long LastInsertRowId { get; private set; }

    /// <summary>
    /// Number of rows affected by last INSERT/UPDATE/DELETE (for CHANGES function).
    /// </summary>
    public long LastChangesCount { get; private set; }

    /// <summary>
    /// Gets the current active transaction, if any.
    /// </summary>
    ITransaction? ITransactionManager.Transaction => m_currentTransaction;

    #endregion
}
