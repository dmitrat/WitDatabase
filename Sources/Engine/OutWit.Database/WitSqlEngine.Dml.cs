using OutWit.Database.Definitions;
using OutWit.Database.Schema;
using OutWit.Database.Utils;
using OutWit.Database.Values;

namespace OutWit.Database;

/// <summary>
/// DML (Data Manipulation Language) operations for WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
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

        // If no row ID found, generate one
        if (!hasRowId)
        {
            rowId = m_schema.GetNextRowId(tableName);
        }

        var key = SchemaCatalog.CreateRowKey(tableName, rowId);
        var value = table.SerializeRow(row);

        PutToStore(key, value);
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
        var table = m_schema.GetTable(tableName)
                    ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        var key = SchemaCatalog.CreateRowKey(tableName, rowId);
        var value = table.SerializeRow(newRow);

        PutToStore(key, value);
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
        var key = SchemaCatalog.CreateRowKey(tableName, rowId);
        DeleteFromStore(key);
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

    private void DeleteFromStore(byte[] key)
    {
        if (m_currentTransaction != null)
            m_currentTransaction.Delete(key);
        else
            m_database.Delete(key);
    }

    #endregion
}
