using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Service for database operations using ADO.NET.
/// </summary>
public interface IDatabaseService : IDisposable
{
    #region Events

    /// <summary>
    /// Raised when the connection status changes.
    /// </summary>
    event EventHandler<bool>? ConnectionStatusChanged;

    #endregion

    #region Connection

    /// <summary>
    /// Connects to a database.
    /// </summary>
    Task<bool> ConnectAsync(ConnectionInfo connection, CancellationToken ct = default);

    /// <summary>
    /// Disconnects from the current database.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Gets whether the service is currently connected to a database.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the current connection information.
    /// </summary>
    ConnectionInfo? CurrentConnection { get; }

    #endregion

    #region Schema

    /// <summary>
    /// Gets all tables in the database.
    /// </summary>
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all views in the database.
    /// </summary>
    Task<IReadOnlyList<string>> GetViewsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all indexes in the database.
    /// </summary>
    Task<IReadOnlyList<string>> GetIndexesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all triggers in the database.
    /// </summary>
    Task<IReadOnlyList<string>> GetTriggersAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all sequences in the database.
    /// </summary>
    Task<IReadOnlyList<string>> GetSequencesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets columns for a specific table.
    /// </summary>
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// Get table columns information with extended details.
    /// </summary>
    Task<IReadOnlyList<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// Gets the definition (DDL) for a view.
    /// </summary>
    Task<string?> GetViewDefinitionAsync(string viewName, CancellationToken ct = default);

    /// <summary>
    /// Gets the definition (DDL) for a trigger.
    /// </summary>
    Task<string?> GetTriggerDefinitionAsync(string triggerName, CancellationToken ct = default);

    /// <summary>
    /// Gets the definition (DDL) for an index.
    /// </summary>
    Task<string?> GetIndexDefinitionAsync(string indexName, CancellationToken ct = default);

    /// <summary>
    /// Gets the definition (DDL) for a table (CREATE TABLE statement).
    /// </summary>
    Task<string?> GetTableDefinitionAsync(string tableName, CancellationToken ct = default);

    #endregion

    #region Query

    /// <summary>
    /// Executes a SQL query and returns the result.
    /// </summary>
    Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// Executes a non-query SQL statement (INSERT, UPDATE, DELETE).
    /// </summary>
    Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// Executes a scalar query.
    /// </summary>
    Task<object?> ExecuteScalarAsync(string sql, CancellationToken ct = default);

    #endregion
}
