using OutWit.Database.Definitions;
using OutWit.Database.Schema;
using OutWit.Database.Sql;
using OutWit.Database.Utils;
using OutWit.Database.Values;

namespace OutWit.Database.Engine;

/// <summary>
/// DML (Data Manipulation Language) operations for WitSqlEngine - basic row operations.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Get Row

    /// <summary>
    /// Get a single row by its row ID. This is a direct key-value lookup, much faster than scanning.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="rowId">The row ID to fetch.</param>
    /// <returns>The row if found, null otherwise.</returns>
    public WitSqlRow? GetRowById(string tableName, long rowId)
    {
        var table = m_schema.GetTable(tableName);
        if (table == null)
            return null;

        var key = SchemaCatalog.CreateRowKey(tableName, rowId);
        var value = GetFromStore(key);
        
        if (value == null)
            return null;

        var dataRow = table.DeserializeRow(value);
        
        // Build row with _rowid prepended
        var values = new WitSqlValue[dataRow.ColumnCount + 1];
        var names = new string[dataRow.ColumnCount + 1];
        
        values[0] = WitSqlValue.FromInt(rowId);
        names[0] = "_rowid";
        
        for (int i = 0; i < dataRow.ColumnCount; i++)
        {
            values[i + 1] = dataRow[i];
            names[i + 1] = dataRow.ColumnNames[i];
        }
        
        return new WitSqlRow(values, names);
    }

    #endregion

    #region Insert

    /// <summary>
    /// Insert a row into a table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="row">The row to insert.</param>
    public void InsertRow(string tableName, WitSqlRow row)
    {
        var table = m_schema.GetTable(tableName)
                    ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        // Get row ID from auto-increment primary key column or _rowid
        long rowId = 0;
        bool hasRowId = false;

        // First, try auto-increment primary key column
        var autoIncrementCol = table.Columns.FirstOrDefault(c => c.IsAutoIncrement);
        if (autoIncrementCol != null && row.TryGetValue(autoIncrementCol.Name, out var pkValue) && !pkValue.IsNull)
        {
            rowId = pkValue.AsInt64();
            hasRowId = true;
        }
        // Then try _rowid column
        else if (row.TryGetValue(table.RowIdColumn, out var rowIdValue) && !rowIdValue.IsNull)
        {
            rowId = rowIdValue.AsInt64();
            hasRowId = true;
        }

        // If no row ID found, generate one (use transaction if active)
        if (!hasRowId)
        {
            rowId = m_schema.GetNextRowId(tableName, m_currentTransaction);
        }

        var key = SchemaCatalog.CreateRowKey(tableName, rowId);
        var value = table.SerializeRow(row);

        PutToStore(key, value);

        // Update all secondary indexes for this table
        UpdateIndexesOnInsert(tableName, table, rowId, row);
        
        // Update row count
        m_schema.IncrementRowCount(tableName, 1, m_currentTransaction);
        
        // Track for savepoint rollback
        TrackRowCountDelta(tableName, +1);
    }

    #endregion

    #region Update

    /// <summary>
    /// Update a row in a table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="rowId">The row ID to update.</param>
    /// <param name="newRow">The new row data.</param>
    public void UpdateRow(string tableName, long rowId, WitSqlRow newRow)
    {
        UpdateRow(tableName, rowId, newRow, modifiedColumns: null);
    }

    /// <summary>
    /// Update a row in a table with knowledge of which columns were modified.
    /// This enables optimization to skip index updates when no indexed columns changed.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="rowId">The row ID to update.</param>
    /// <param name="newRow">The new row data.</param>
    /// <param name="modifiedColumns">Set of column names that were modified, or null to update all indexes.</param>
    public void UpdateRow(string tableName, long rowId, WitSqlRow newRow, IReadOnlySet<string>? modifiedColumns)
    {
        var table = m_schema.GetTable(tableName)
                    ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        // Read old row for index update
        var key = SchemaCatalog.CreateRowKey(tableName, rowId);
        var oldValue = GetFromStore(key);
        WitSqlRow? oldRow = null;
        
        if (oldValue != null)
        {
            oldRow = table.DeserializeRow(oldValue);
        }

        // Update the row data
        var newValue = table.SerializeRow(newRow);
        PutToStore(key, newValue);

        // Update indexes (remove old keys, add new keys)
        // Pass modified columns for optimization
        UpdateIndexesOnUpdate(tableName, table, rowId, oldRow, newRow, modifiedColumns);
        
        // Note: UPDATE does not change row count
    }

    #endregion

    #region Delete

    /// <summary>
    /// Delete a row from a table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="rowId">The row ID to delete.</param>
    public void DeleteRow(string tableName, long rowId)
    {
        var table = m_schema.GetTable(tableName);

        // Read the row before deletion for index cleanup
        var key = SchemaCatalog.CreateRowKey(tableName, rowId);
        WitSqlRow? oldRow = null;
        
        if (table != null)
        {
            var oldValue = GetFromStore(key);
            if (oldValue != null)
            {
                oldRow = table.DeserializeRow(oldValue);
            }
        }

        // Delete the row
        DeleteFromStore(key);

        // Remove from all indexes
        if (table != null && oldRow != null)
        {
            UpdateIndexesOnDelete(tableName, table, rowId, oldRow.Value);
        }
        
        // Update row count
        m_schema.DecrementRowCount(tableName, 1, m_currentTransaction);
        
        // Track for savepoint rollback
        TrackRowCountDelta(tableName, -1);
    }

    #endregion

    #region Truncate

    /// <summary>
    /// Truncate all rows from a table.
    /// Faster than DELETE without WHERE - removes all data and resets auto-increment.
    /// Does NOT fire triggers.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    public void TruncateTable(string tableName)
    {
        var table = m_schema.GetTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        // Get current row count before truncate for delta tracking
        var currentCount = m_schema.GetRowCount(tableName);

        // Get all indexes for this table
        var indexes = m_schema.GetTableIndexes(tableName).ToList();

        // Delete all rows from the table
        var prefix = SchemaCatalog.GetTableDataPrefix(tableName);
        var endPrefix = SchemaCatalog.GetTableDataEndPrefix(tableName);

        var keysToDelete = new List<byte[]>();
        foreach (var (key, _) in m_database.Scan(prefix, endPrefix))
        {
            keysToDelete.Add(key);
        }

        // Delete all rows
        foreach (var key in keysToDelete)
        {
            DeleteFromStore(key);
        }

        // Clear all secondary indexes for this table
        foreach (var indexDef in indexes)
        {
            var secondaryIndex = m_database.GetIndex(indexDef.Name);
            secondaryIndex?.Clear();
        }

        // Reset auto-increment counter to 0
        m_schema.ResetRowId(tableName, 0, m_currentTransaction);
        
        // Reset row count to 0
        m_schema.ResetRowCount(tableName, 0, m_currentTransaction);
        
        // Track for savepoint rollback (negative delta = all rows deleted)
        if (currentCount > 0)
        {
            TrackRowCountDelta(tableName, -currentCount);
        }
    }

    #endregion

    #region Store Access

    private void PutToStore(byte[] key, byte[] value)
    {
        if (m_currentTransaction != null)
            m_currentTransaction.Put(key, value);
        else
            m_database.Put(key, value);
    }

    private byte[]? GetFromStore(byte[] key)
    {
        if (m_currentTransaction != null)
            return m_currentTransaction.Get(key);
        else
            return m_database.Get(key);
    }

    private void DeleteFromStore(byte[] key)
    {
        if (m_currentTransaction != null)
            m_currentTransaction.Delete(key);
        else
            m_database.Delete(key);
    }

    #endregion
}
