using OutWit.Database.Context;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser;
using OutWit.Database.Statements;
using OutWit.Database.Values;

namespace OutWit.Database;

/// <summary>
/// Query-related methods for WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Query

    /// <summary>
    /// Execute a query and return all rows as a list.
    /// </summary>
    /// <param name="sql">SQL query text.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <returns>List of all rows returned by the query.</returns>
    public List<WitSqlRow> Query(string sql, IDictionary<string, object?>? parameters = null)
    {
        using var result = Execute(sql, parameters);
        return result.ReadAll();
    }

    /// <summary>
    /// Execute a query and return the first row, or null if no rows.
    /// </summary>
    /// <param name="sql">SQL query text.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <returns>The first row or null.</returns>
    public WitSqlRow? QueryFirstOrDefault(string sql, IDictionary<string, object?>? parameters = null)
    {
        using var result = Execute(sql, parameters);
        return result.Read() ? result.CurrentRow : null;
    }

    /// <summary>
    /// Execute a query and return a single scalar value.
    /// </summary>
    /// <param name="sql">SQL query text.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <returns>The scalar value from the first column of the first row, or NULL.</returns>
    public WitSqlValue ExecuteScalar(string sql, IDictionary<string, object?>? parameters = null)
    {
        using var result = Execute(sql, parameters);
        if (!result.Read() || result.Columns.Count == 0)
            return WitSqlValue.Null;
        return result.CurrentRow[0];
    }

    /// <summary>
    /// Execute a non-query statement (INSERT, UPDATE, DELETE) and return rows affected.
    /// </summary>
    /// <param name="sql">SQL statement text.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <returns>Number of rows affected.</returns>
    public int ExecuteNonQuery(string sql, IDictionary<string, object?>? parameters = null)
    {
        using var result = Execute(sql, parameters);
        return result.RowsAffected;
    }

    /// <summary>
    /// Create a prepared statement for reuse.
    /// </summary>
    /// <param name="sql">SQL query text to prepare.</param>
    /// <returns>A prepared statement that can be executed multiple times.</returns>
    public WitSqlStatementPrepared Prepare(string sql)
    {
        var statements = WitSql.Parse(sql);
        return new WitSqlStatementPrepared(this, statements);
    }

    #endregion

    #region Table Scan

    /// <summary>
    /// Get table metadata by name.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>Table definition or null if not found.</returns>
    public DefinitionTable? GetTable(string tableName)
    {
        return m_schema.GetTable(tableName);
    }

    /// <summary>
    /// Create a table scan iterator.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>An iterator over all rows in the table.</returns>
    public IResultIterator CreateTableScan(string tableName)
    {
        var table = m_schema.GetTable(tableName)
                    ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        return new IteratorTableScan(GetActiveStore(), table);
    }

    /// <summary>
    /// Create an index seek iterator for equality lookup.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="indexName">The index name.</param>
    /// <param name="keyValues">Key values to seek.</param>
    /// <returns>An iterator for matching rows.</returns>
    /// <remarks>Currently falls back to table scan. Index seek to be implemented.</remarks>
    public IResultIterator CreateIndexSeek(string tableName, string indexName, WitSqlValue[] keyValues)
    {
        // TODO: Implement proper index seek
        return CreateTableScan(tableName);
    }

    /// <summary>
    /// Create an index range scan iterator.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="indexName">The index name.</param>
    /// <param name="startKey">Start key value (or null for unbounded).</param>
    /// <param name="startInclusive">Whether start key is inclusive.</param>
    /// <param name="endKey">End key value (or null for unbounded).</param>
    /// <param name="endInclusive">Whether end key is inclusive.</param>
    /// <returns>An iterator for matching rows.</returns>
    /// <remarks>Currently falls back to table scan. Index range scan to be implemented.</remarks>
    public IResultIterator CreateIndexRangeScan(string tableName, string indexName,
        WitSqlValue? startKey, bool startInclusive, WitSqlValue? endKey, bool endInclusive)
    {
        // TODO: Implement proper index range scan
        return CreateTableScan(tableName);
    }

    #endregion

    #region Helpers

    private Core.Interfaces.IKeyValueStore GetActiveStore()
    {
        // ITransaction doesn't extend IKeyValueStore and doesn't have Scan
        // For now, use the underlying store (TODO: support transaction isolation for queries)
        return m_database.Store;
    }

    #endregion
}
