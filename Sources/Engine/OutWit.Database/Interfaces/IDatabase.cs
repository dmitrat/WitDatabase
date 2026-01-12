using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Interfaces;

/// <summary>
/// Interface for database access from execution engine.
/// </summary>
public interface IDatabase
{
    /// <summary>
    /// Get table metadata.
    /// </summary>
    DefinitionTable? GetTable(string tableName);

    /// <summary>
    /// Gets the row count for a table using cached metadata.
    /// This is an O(1) operation.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>The row count, or -1 if the table doesn't exist or count is unknown.</returns>
    long GetTableRowCount(string tableName);

    /// <summary>
    /// Get iterator for full table scan.
    /// </summary>
    IResultIterator CreateTableScan(string tableName);

    /// <summary>
    /// Get iterator for index seek (equality lookup).
    /// </summary>
    IResultIterator CreateIndexSeek(string tableName, string indexName, WitSqlValue[] keyValues);

    /// <summary>
    /// Get iterator for index range scan.
    /// </summary>
    IResultIterator CreateIndexRangeScan(string tableName, string indexName, 
        WitSqlValue? startKey, bool startInclusive, WitSqlValue? endKey, bool endInclusive);

    /// <summary>
    /// Get a single row by its row ID. This is a direct key-value lookup, much faster than scanning.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="rowId">The row ID to fetch.</param>
    /// <returns>The row if found, null otherwise.</returns>
    WitSqlRow? GetRowById(string tableName, long rowId);

    /// <summary>
    /// Insert a row into a table.
    /// </summary>
    void InsertRow(string tableName, WitSqlRow row);

    /// <summary>
    /// Update a row in a table.
    /// </summary>
    void UpdateRow(string tableName, long rowId, WitSqlRow newRow);

    /// <summary>
    /// Update a row in a table with knowledge of which columns were modified.
    /// This enables optimization to skip index updates when no indexed columns changed.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="rowId">The row ID to update.</param>
    /// <param name="newRow">The new row data.</param>
    /// <param name="modifiedColumns">Set of column names that were modified, or null to update all indexes.</param>
    void UpdateRow(string tableName, long rowId, WitSqlRow newRow, IReadOnlySet<string>? modifiedColumns);

    /// <summary>
    /// Delete a row from a table.
    /// </summary>
    void DeleteRow(string tableName, long rowId);

    /// <summary>
    /// Truncate all rows from a table.
    /// Faster than DELETE without WHERE - removes all data and resets auto-increment.
    /// </summary>
    void TruncateTable(string tableName);

    /// <summary>
    /// Create a new table.
    /// </summary>
    void CreateTable(DefinitionTable metadata);

    /// <summary>
    /// Drop a table.
    /// </summary>
    void DropTable(string tableName);

    /// <summary>
    /// Add a column to an existing table.
    /// </summary>
    void AddColumn(string tableName, DefinitionColumn metadataColumn);

    /// <summary>
    /// Add a computed column to an existing table.
    /// For STORED computed columns, evaluates expression for all existing rows.
    /// For VIRTUAL computed columns, just updates metadata (evaluated on query).
    /// </summary>
    void AddComputedColumn(string tableName, DefinitionColumn computedColumn);

    /// <summary>
    /// Drop a column from an existing table.
    /// </summary>
    void DropColumn(string tableName, string columnName);

    /// <summary>
    /// Rename a table.
    /// </summary>
    void RenameTable(string oldName, string newName);

    /// <summary>
    /// Rename a column in a table.
    /// </summary>
    void RenameColumn(string tableName, string oldColumnName, string newColumnName);

    /// <summary>
    /// Change a column's data type.
    /// </summary>
    void AlterColumnType(string tableName, string columnName, WitDataType newType);

    /// <summary>
    /// Set a column's default value.
    /// </summary>
    void SetColumnDefault(string tableName, string columnName, WitSqlValue? defaultValue);

    /// <summary>
    /// Drop a column's default value.
    /// </summary>
    void DropColumnDefault(string tableName, string columnName);

    /// <summary>
    /// Set or drop NOT NULL constraint on a column.
    /// </summary>
    void SetColumnNotNull(string tableName, string columnName, bool notNull);

    /// <summary>
    /// Add a named constraint to an existing table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="constraint">The constraint definition.</param>
    void AddConstraint(string tableName, DefinitionNamedConstraint constraint);

    /// <summary>
    /// Drop a named constraint from an existing table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="constraintName">The constraint name.</param>
    void DropConstraint(string tableName, string constraintName);

    /// <summary>
    /// Get an index by name.
    /// </summary>
    DefinitionIndex? GetIndex(string indexName);

    /// <summary>
    /// Get all indexes for a table.
    /// </summary>
    IEnumerable<DefinitionIndex> GetTableIndexes(string tableName);

    /// <summary>
    /// Create an index.
    /// </summary>
    void CreateIndex(DefinitionIndex metadata);

    /// <summary>
    /// Drop an index.
    /// </summary>
    void DropIndex(string indexName);

    /// <summary>
    /// Get next value for an auto-increment column.
    /// </summary>
    long GetNextAutoIncrement(string tableName);

    /// <summary>
    /// Ensures the auto-increment counter is at least the specified value.
    /// This is called when a row is inserted with an explicit ID value
    /// to prevent future auto-increment values from colliding.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="minValue">The minimum value the counter should be at.</param>
    void EnsureAutoIncrementAtLeast(string tableName, long minValue);

    /// <summary>
    /// Get next value for a ROWVERSION column.
    /// ROWVERSION is a database-wide counter, not per-table.
    /// </summary>
    ulong GetNextRowVersion(string tableName);

    /// <summary>
    /// Begin a transaction.
    /// </summary>
    IDisposable BeginTransaction();

    /// <summary>
    /// Begin a transaction with specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level.</param>
    /// <returns>A disposable transaction handle.</returns>
    IDisposable BeginTransaction(WitIsolationLevel isolationLevel);

    /// <summary>
    /// Commit current transaction.
    /// </summary>
    void Commit();

    /// <summary>
    /// Rollback current transaction.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Gets the currently active transaction, or null if none.
    /// </summary>
    ITransaction? CurrentTransaction { get; }

    /// <summary>
    /// Create a savepoint within the current transaction.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    void CreateSavepoint(string name);

    /// <summary>
    /// Release a savepoint within the current transaction.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    void ReleaseSavepoint(string name);

    /// <summary>
    /// Rollback to a savepoint within the current transaction.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    void RollbackToSavepoint(string name);

    /// <summary>
    /// Get a view by name.
    /// </summary>
    DefinitionView? GetView(string viewName);

    /// <summary>
    /// Create a view.
    /// </summary>
    void CreateView(string name, string selectSql, IReadOnlyList<string>? columnAliases);

    /// <summary>
    /// Drop a view.
    /// </summary>
    void DropView(string name);

    /// <summary>
    /// Get a trigger by name.
    /// </summary>
    DefinitionTrigger? GetTrigger(string triggerName);

    /// <summary>
    /// Get all triggers for a table and event.
    /// </summary>
    IEnumerable<DefinitionTrigger> GetTriggersForTable(string tableName, TriggerEvent? evt = null, TriggerTime? time = null);

    /// <summary>
    /// Create a trigger.
    /// </summary>
    void CreateTrigger(DefinitionTrigger trigger);

    /// <summary>
    /// Drop a trigger.
    /// </summary>
    void DropTrigger(string name);

    /// <summary>
    /// Get a sequence by name.
    /// </summary>
    DefinitionSequence? GetSequence(string sequenceName);

    /// <summary>
    /// Create a sequence.
    /// </summary>
    void CreateSequence(string name, long startWith);

    /// <summary>
    /// Drop a sequence.
    /// </summary>
    void DropSequence(string name);

    /// <summary>
    /// Get next value from sequence.
    /// </summary>
    long NextVal(string sequenceName);

    /// <summary>
    /// Get current value of sequence.
    /// </summary>
    long CurrVal(string sequenceName);

    /// <summary>
    /// Restart sequence.
    /// </summary>
    void RestartSequence(string name, long? restartWith);
}