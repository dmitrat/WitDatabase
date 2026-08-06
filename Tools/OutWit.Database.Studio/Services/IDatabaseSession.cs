using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// One open database connection, and everything that can be asked of it.
///
/// This is what <c>IDatabaseService</c> used to be, minus the part that made multi-connection
/// impossible: a session is created FOR a connection and lives exactly as long as it, so there is no
/// ConnectAsync here and no CurrentConnection to change underneath a caller. Opening and closing
/// belong to <see cref="IConnectionManager"/>, which owns the collection they would otherwise
/// invalidate.
///
/// <see cref="StatusChanged"/> is the session's own event. The application used to have ONE, raised by
/// the single service and listened to by four ViewModels, so disconnecting anything closed everyone's
/// tabs (WS-13).
/// </summary>
public interface IDatabaseSession
{
    #region Events

    /// <summary>
    /// Raised when THIS connection opens or closes. Never fires for another session.
    /// </summary>
    event EventHandler<bool>? StatusChanged;

    #endregion

    #region Identity

    /// <summary>
    /// Identifies the session for as long as it exists. Tree nodes carry it rather than a reference,
    /// so a node cannot keep a closed session alive.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// What the session was opened with. A copy: the dialog goes on editing its own instance after
    /// the connection is made.
    /// </summary>
    ConnectionInfo Connection { get; }

    /// <summary>
    /// The name shown on the connection's root in the tree and on the tabs that belong to it. Unique
    /// among the open sessions, because two databases in different folders can have the same name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Which of the six connection colours this session was given (WS-3). Repetition is allowed; the
    /// colour is a hint, not an identity.
    /// </summary>
    int ColorIndex { get; }

    /// <summary>
    /// Whether the connection is open.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Whether the connection was opened read-only (WS-10). A property of the connection, so every
    /// tab that belongs to it inherits the answer.
    /// </summary>
    bool IsReadOnly { get; }

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
    /// Executes a statement with values bound to it. The values never pass through the language, so
    /// a quote in one cannot break the statement and a semicolon in one cannot run anything.
    /// </summary>
    Task<QueryResult> ExecuteQueryAsync(SqlStatement statement, CancellationToken ct = default);

    /// <summary>
    /// Executes a non-query SQL statement (INSERT, UPDATE, DELETE).
    /// </summary>
    Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// The same, with values bound.
    /// </summary>
    Task<int> ExecuteNonQueryAsync(SqlStatement statement, CancellationToken ct = default);

    /// <summary>
    /// Executes a scalar query.
    /// </summary>
    Task<object?> ExecuteScalarAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// Runs a set of statements as one transaction: all of them, or none. Used by the table editor,
    /// whose buffer of edits is one decision by the user and has to reach the database as one.
    /// </summary>
    Task<BatchResult> ExecuteBatchAsync(IReadOnlyList<SqlStatement> statements, CancellationToken ct = default);

    #endregion
}
