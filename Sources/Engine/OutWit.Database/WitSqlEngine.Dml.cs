using OutWit.Database.Definitions;
using OutWit.Database.Schema;
using OutWit.Database.Types;
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

        // Update all secondary indexes for this table
        UpdateIndexesOnInsert(tableName, table, rowId, row);
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
        UpdateIndexesOnUpdate(tableName, table, rowId, oldRow, newRow);
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
    }

    #endregion

    #region Index Updates

    /// <summary>
    /// Updates all secondary indexes after a row insert.
    /// </summary>
    private void UpdateIndexesOnInsert(string tableName, DefinitionTable table, long rowId, WitSqlRow row)
    {
        var indexes = m_schema.GetTableIndexes(tableName);
        foreach (var indexDef in indexes)
        {
            var secondaryIndex = m_database.GetIndex(indexDef.Name);
            if (secondaryIndex == null)
                continue;

            // Build the index key from the row values
            var indexKey = BuildIndexKey(table, indexDef, row);
            if (indexKey == null)
                continue; // Skip if any key column is null and index doesn't support nulls

            // Build primary key (row ID in BigEndian format)
            var primaryKey = BuildPrimaryKey(rowId);

            // Add to index
            try
            {
                secondaryIndex.Add(indexKey, primaryKey);
            }
            catch (InvalidOperationException)
            {
                // Unique index violation - should have been caught by constraint validation
                // Re-throw with more context
                throw new InvalidOperationException(
                    $"UNIQUE constraint failed: Index '{indexDef.Name}' on table '{tableName}'");
            }
        }
    }

    /// <summary>
    /// Updates all secondary indexes after a row update.
    /// </summary>
    private void UpdateIndexesOnUpdate(string tableName, DefinitionTable table, long rowId, WitSqlRow? oldRow, WitSqlRow newRow)
    {
        var indexes = m_schema.GetTableIndexes(tableName);
        foreach (var indexDef in indexes)
        {
            var secondaryIndex = m_database.GetIndex(indexDef.Name);
            if (secondaryIndex == null)
                continue;

            var primaryKey = BuildPrimaryKey(rowId);

            // Build old and new index keys
            var oldIndexKey = oldRow != null ? BuildIndexKey(table, indexDef, oldRow.Value) : null;
            var newIndexKey = BuildIndexKey(table, indexDef, newRow);

            // Check if the indexed columns actually changed
            bool keysEqual = oldIndexKey != null && newIndexKey != null && 
                             oldIndexKey.AsSpan().SequenceEqual(newIndexKey.AsSpan());

            if (keysEqual)
                continue; // No change to indexed columns

            // Remove old key if it existed
            if (oldIndexKey != null)
            {
                secondaryIndex.Remove(oldIndexKey, primaryKey);
            }

            // Add new key if not null
            if (newIndexKey != null)
            {
                try
                {
                    secondaryIndex.Add(newIndexKey, primaryKey);
                }
                catch (InvalidOperationException)
                {
                    // Unique index violation - rollback by re-adding old key
                    if (oldIndexKey != null)
                    {
                        secondaryIndex.Add(oldIndexKey, primaryKey);
                    }
                    throw new InvalidOperationException(
                        $"UNIQUE constraint failed: Index '{indexDef.Name}' on table '{tableName}'");
                }
            }
        }
    }

    /// <summary>
    /// Updates all secondary indexes after a row delete.
    /// </summary>
    private void UpdateIndexesOnDelete(string tableName, DefinitionTable table, long rowId, WitSqlRow oldRow)
    {
        var indexes = m_schema.GetTableIndexes(tableName);
        foreach (var indexDef in indexes)
        {
            var secondaryIndex = m_database.GetIndex(indexDef.Name);
            if (secondaryIndex == null)
                continue;

            var indexKey = BuildIndexKey(table, indexDef, oldRow);
            if (indexKey == null)
                continue;

            var primaryKey = BuildPrimaryKey(rowId);
            secondaryIndex.Remove(indexKey, primaryKey);
        }
    }

    /// <summary>
    /// Builds an index key from row values based on index definition.
    /// </summary>
    /// <returns>The serialized index key, or null if any key column is null.</returns>
    private byte[]? BuildIndexKey(DefinitionTable table, DefinitionIndex indexDef, WitSqlRow row)
    {
        var keyValues = new WitSqlValue[indexDef.Columns.Count];
        var columnTypes = new WitDataType[indexDef.Columns.Count];

        for (int i = 0; i < indexDef.Columns.Count; i++)
        {
            var columnName = indexDef.Columns[i];
            
            // Skip _rowid - it's a system column not in the index
            if (columnName.Equals("_rowid", StringComparison.OrdinalIgnoreCase))
                return null;
                
            var column = table.GetColumn(columnName);
            if (column == null)
                return null;

            columnTypes[i] = column.Type;

            if (!row.TryGetValue(columnName, out var value))
                value = WitSqlValue.Null;

            // Skip null values in index (standard SQL behavior for most DBs)
            if (value.IsNull)
                return null;

            keyValues[i] = value;
        }

        return WitTypeConverter.SerializeIndexKey(keyValues, columnTypes);
    }

    /// <summary>
    /// Builds a primary key (row ID) in the format used by secondary indexes.
    /// </summary>
    private static byte[] BuildPrimaryKey(long rowId)
    {
        var key = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(key, rowId);
        return key;
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
