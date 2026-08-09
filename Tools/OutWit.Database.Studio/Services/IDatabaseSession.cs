using System.Data;
using OutWit.Database.AdoNet.Maintenance;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
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

    /// <summary>
    /// Raised when a manual transaction is opened, committed or rolled back on this connection
    /// (WS-26). On the session rather than on the tab, because a transaction belongs to a CONNECTION:
    /// two query tabs of the same database share one, and the second must not be told otherwise.
    /// </summary>
    event EventHandler? TransactionChanged;

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
    /// What the database recorded when it was created - store, page size, format version, providers
    /// and feature flags - read just BEFORE this session opened it (WS-54).
    /// </summary>
    /// <remarks>
    /// Captured at open because an open database holds an exclusive file lock and the header cannot be
    /// read behind it. Nothing in it changes while the database is open, so a value read a moment
    /// earlier is the current one. Null when there was nothing to read.
    /// </remarks>
    StoredConfiguration? StoredConfiguration { get; }

    /// <summary>
    /// How full the page cache is at the moment of the call: which cache, pages held, and how many of
    /// those are dirty. Null when nothing is open, and for a store with no page cache (LSM).
    /// </summary>
    /// <remarks>
    /// A READING rather than a fact, which is what makes it a property and not a captured value the
    /// way <see cref="StoredConfiguration"/> is: the next statement changes it, so it is asked again
    /// every time the Database tab is refreshed.
    /// </remarks>
    PageCacheOccupancy? CacheOccupancy { get; }

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
    /// The view's query alone, for the editor that rewrites it. Null when the catalogue cannot render
    /// the body, which is what stops the designer replacing a view with half of one.
    /// </summary>
    Task<string?> GetViewBodyAsync(string viewName, CancellationToken ct = default);

    /// <summary>
    /// Gets the definition (DDL) for a view - the <c>CREATE VIEW</c>, which is what a dump has to
    /// carry. <see cref="GetViewBodyAsync"/> is the query inside it.
    /// </summary>
    Task<string?> GetViewDefinitionAsync(string viewName, CancellationToken ct = default);

    /// <summary>
    /// Gets the definition (DDL) for a trigger - the whole <c>CREATE TRIGGER</c>, assembled from the
    /// catalogue's parts. The catalogue's own <c>ACTION_STATEMENT</c> is the body alone, and a dump
    /// carrying that cannot be run back into a database.
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

    /// <summary>
    /// Functions and procedures (WS-21).
    /// </summary>
    Task<IReadOnlyList<RoutineInfo>> GetRoutinesAsync(CancellationToken ct = default);

    /// <summary>
    /// The foreign keys that leave this table.
    /// </summary>
    Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// The foreign keys that point at this table.
    /// </summary>
    Task<IReadOnlyList<ForeignKeyInfo>> GetReferencingKeysAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// The indexes of one table, with the columns each covers.
    /// </summary>
    Task<IReadOnlyList<IndexInfo>> GetTableIndexesAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// How many rows the table has, or null if the count did not finish within the timeout (WS-16).
    /// </summary>
    Task<long?> TryCountRowsAsync(string tableName, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// The triggers on one table, complete enough to be written out again - which a rebuild has to do,
    /// because a trigger is dropped with its table.
    /// </summary>
    Task<IReadOnlyList<TriggerInfo>> GetTableTriggersAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// Whether the table holds any rows. Asked by scanning for one, never with COUNT(*).
    /// </summary>
    Task<bool> HasAnyRowsAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// How many values in a column would not survive a conversion to another type, or null when the
    /// engine will not answer for that pair of types (WS-41).
    /// </summary>
    Task<int?> CountValuesThatWillNotConvertAsync(string tableName, string columnName, string fromType,
        string toType, CancellationToken ct = default);

    /// <summary>
    /// The views whose stored definition names a table.
    /// </summary>
    Task<IReadOnlyList<string>> GetViewsMentioningAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// The same schema, cached, for the one consumer that cannot await it: completion answers between
    /// two keystrokes (WS-24). One per connection, living exactly as long as it.
    /// </summary>
    ISchemaCatalog Catalog { get; }

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

    #region Transaction

    /// <summary>
    /// Whether a manual transaction is open on this connection (WS-26). Autocommit - every statement
    /// on its own - is what a session does until someone opens one.
    /// </summary>
    bool HasOpenTransaction { get; }

    /// <summary>
    /// The isolation the open transaction was begun at, or the one the next will be begun at. Five
    /// levels, defaulting to ReadCommitted, which is the engine's own default.
    /// </summary>
    IsolationLevel Isolation { get; set; }

    /// <summary>
    /// How many statements have been executed inside the open transaction. Shown next to the
    /// transaction indicator: "open" alone does not tell anyone what is at stake in it.
    /// </summary>
    int TransactionStatementCount { get; }

    /// <summary>
    /// Opens a transaction at <see cref="Isolation"/>. Throws if one is already open - one connection
    /// can hold one, and pretending otherwise is how two owners end up sharing a commit.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Commits the open transaction. Does nothing when none is open, so that a toolbar that has just
    /// been clicked twice does not throw at a person.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Rolls the open transaction back.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken ct = default);

    #endregion

    #region Storage

    /// <summary>
    /// What this connection's storage can say about itself (WS-56).
    /// </summary>
    /// <remarks>
    /// Through the session rather than around it, for the same reason every other engine call is: a
    /// ViewModel holding a <c>WitDbConnection</c> would be holding a connection it did not open and
    /// cannot see closed.
    /// </remarks>
    Task<WitDbStorageSnapshot> GetStorageSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// Forces the store's in-memory state out into its on-disk structure.
    /// </summary>
    /// <remarks>
    /// The result says what it DID - <c>Completed</c>, <c>NothingToDo</c> or <c>NotSupported</c> -
    /// with the SSTable counts on either side, judged by what changed on disk. The panel writes the
    /// sentence for it; a void here could not say "there was nothing to do", which is exactly how the
    /// silent <c>Compact()</c> hid.
    /// </remarks>
    Task<WitDbMaintenanceResult> CheckpointAsync(CancellationToken ct = default);

    /// <summary>
    /// Merges the store's files. Only an LSM store has files to merge; a paged one answers
    /// <see cref="WitDbMaintenanceOutcome.NotSupported"/>, which is what keeps the button off the
    /// screen rather than greyed out (WS-55).
    /// </summary>
    Task<WitDbMaintenanceResult> CompactAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads every row and every value a query returns, without materialising any of it, and answers
    /// how many rows there were (WS-61).
    /// </summary>
    /// <remarks>
    /// The count comes from the rows themselves rather than from <c>COUNT(*)</c>, which on this engine
    /// is a counter kept beside the data - the two can disagree, and telling them apart is one of the
    /// few things a read check can actually find.
    /// </remarks>
    Task<long> ScanAsync(string sql, CancellationToken ct = default);

    /// <inheritdoc cref="ScanAsync(string, CancellationToken)"/>
    Task<long> ScanAsync(SqlStatement statement, CancellationToken ct = default);

    #endregion
}
