using OutWit.Database.Context;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Utils;
using OutWit.Database.Values;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Engine;

/// <summary>
/// DML (Data Manipulation Language) operations for WitSqlEngine - index update logic.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Index Updates on Insert

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

            // Check partial index WHERE condition
            if (!EvaluatePartialIndexCondition(indexDef, row))
                continue; // Row doesn't match partial index condition

            // Build the index key from the row values (supports expression indexes)
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

    #endregion

    #region Index Updates on Update

    /// <summary>
    /// Updates all secondary indexes after a row update.
    /// </summary>
    private void UpdateIndexesOnUpdate(string tableName, DefinitionTable table, long rowId, WitSqlRow? oldRow, WitSqlRow newRow)
    {
        UpdateIndexesOnUpdate(tableName, table, rowId, oldRow, newRow, modifiedColumns: null);
    }

    /// <summary>
    /// Updates secondary indexes after a row update, with optimization for known modified columns.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="table">The table definition.</param>
    /// <param name="rowId">The row ID being updated.</param>
    /// <param name="oldRow">The old row data (before update).</param>
    /// <param name="newRow">The new row data (after update).</param>
    /// <param name="modifiedColumns">Set of column names that were modified, or null to check all indexes.</param>
    private void UpdateIndexesOnUpdate(
        string tableName, 
        DefinitionTable table, 
        long rowId, 
        WitSqlRow? oldRow, 
        WitSqlRow newRow,
        IReadOnlySet<string>? modifiedColumns)
    {
        var indexes = m_schema.GetTableIndexes(tableName);
        foreach (var indexDef in indexes)
        {
            // Early exit: if we know which columns were modified and this index
            // doesn't use any of them, skip the index entirely
            if (modifiedColumns != null && !IndexUsesAnyColumn(indexDef, modifiedColumns))
                continue;

            var secondaryIndex = m_database.GetIndex(indexDef.Name);
            if (secondaryIndex == null)
                continue;

            var primaryKey = BuildPrimaryKey(rowId);

            // Check if old row was in index (partial index condition)
            bool oldRowInIndex = oldRow != null && EvaluatePartialIndexCondition(indexDef, oldRow.Value);
            bool newRowInIndex = EvaluatePartialIndexCondition(indexDef, newRow);

            // Build old and new index keys
            var oldIndexKey = oldRowInIndex ? BuildIndexKey(table, indexDef, oldRow!.Value) : null;
            var newIndexKey = newRowInIndex ? BuildIndexKey(table, indexDef, newRow) : null;

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
    /// Checks if an index uses any of the specified columns.
    /// </summary>
    /// <param name="indexDef">The index definition.</param>
    /// <param name="columns">Set of column names to check.</param>
    /// <returns>True if the index uses any of the columns, false otherwise.</returns>
    private static bool IndexUsesAnyColumn(DefinitionIndex indexDef, IReadOnlySet<string> columns)
    {
        // Check direct column references
        foreach (var indexColumn in indexDef.Columns)
        {
            if (columns.Contains(indexColumn))
                return true;
        }

        // An expression index, or a filtered one, is affected when the write touches a column the
        // expression actually reads. Until 9.0.0 this asked whether the rendered text of the
        // expression *contained* the column's name, which said yes for a column named Age and a
        // filter mentioning Agent, and would now say no whenever the rendering was absent.
        return indexDef.ReferencedColumns().Overlaps(columns);
    }

    #endregion

    #region Index Updates on Delete

    /// <summary>
    /// Updates all secondary indexes after a row delete.
    /// </summary>
    /// <summary>
    /// Best-effort removal of the index entries a failed insert may have added.
    /// </summary>
    /// <remarks>
    /// Used to compensate a rejected INSERT. Entries that were never added are simply absent, and
    /// the row that caused the conflict carries a different row id, so its entry is not touched. A
    /// failure while compensating must not mask the original error, so it is swallowed - the caller
    /// is already unwinding.
    /// </remarks>
    private void TryRemoveIndexEntries(string tableName, DefinitionTable table, long rowId, WitSqlRow row)
    {
        try
        {
            UpdateIndexesOnDelete(tableName, table, rowId, row);
        }
        catch
        {
            // Swallowed on purpose: the original exception is the one that matters.
        }
    }

    private void UpdateIndexesOnDelete(string tableName, DefinitionTable table, long rowId, WitSqlRow oldRow)
    {
        var indexes = m_schema.GetTableIndexes(tableName);
        foreach (var indexDef in indexes)
        {
            var secondaryIndex = m_database.GetIndex(indexDef.Name);
            if (secondaryIndex == null)
                continue;

            // Check partial index WHERE condition
            if (!EvaluatePartialIndexCondition(indexDef, oldRow))
                continue; // Row wasn't in this partial index

            var indexKey = BuildIndexKey(table, indexDef, oldRow);
            if (indexKey == null)
                continue;

            var primaryKey = BuildPrimaryKey(rowId);
            secondaryIndex.Remove(indexKey, primaryKey);
        }
    }

    #endregion

    #region Index Key Building

    /// <summary>
    /// Evaluates the WHERE condition of a partial/filtered index.
    /// </summary>
    /// <param name="indexDef">The index definition.</param>
    /// <param name="row">The row to evaluate against.</param>
    /// <returns>True if row should be included in index, false otherwise.</returns>
    private bool EvaluatePartialIndexCondition(DefinitionIndex indexDef, WitSqlRow row)
    {
        // If no WHERE clause, all rows are included
        var expression = indexDef.ResolveWhere();

        if (expression is null)
            return true;

        try
        {
            var context = new ContextExecution { Database = this };
            var evaluator = new ExpressionEvaluator(context);
            var result = evaluator.Evaluate(expression, row);
            
            // Return true if expression evaluates to true (non-null, non-false)
            return result.IsTrue;
        }
        catch
        {
            // If evaluation fails, exclude from index for safety
            return false;
        }
    }

    /// <summary>
    /// Builds an index key from row values based on index definition.
    /// Supports expression indexes by evaluating expressions.
    /// </summary>
    /// <returns>The serialized index key, or null if any key column is null.</returns>
    private byte[]? BuildIndexKey(DefinitionTable table, DefinitionIndex indexDef, WitSqlRow row)
    {
        var keyValues = new WitSqlValue[indexDef.Columns.Count];
        var columnTypes = new WitDataType[indexDef.Columns.Count];

        for (int i = 0; i < indexDef.Columns.Count; i++)
        {
            var columnName = indexDef.Columns[i];
            WitSqlValue value;
            WitDataType columnType;
            
            // Check if this is an expression column
            var indexExpression = indexDef.ResolveColumnExpression(i);
            if (indexExpression != null)
            {
                // Evaluate the expression
                value = EvaluateIndexExpression(indexExpression, row);
                // For expression indexes, determine type from the result
                columnType = DetermineTypeFromValue(value);
            }
            else
            {
                // Skip _rowid - it's a system column not in the index
                if (columnName.Equals("_rowid", StringComparison.OrdinalIgnoreCase))
                    return null;
                    
                var column = table.GetColumn(columnName);
                if (column == null)
                    return null;

                columnType = column.Type;

                if (!row.TryGetValue(columnName, out value))
                    value = WitSqlValue.Null;
            }

            // Skip null values in index (standard SQL behavior for most DBs)
            if (value.IsNull)
                return null;

            keyValues[i] = value;
            columnTypes[i] = columnType;
        }

        return WitTypeConverter.SerializeIndexKey(keyValues, columnTypes);
    }

    /// <summary>
    /// Evaluates an expression for an expression index.
    /// </summary>
    private WitSqlValue EvaluateIndexExpression(WitSqlExpression expression, WitSqlRow row)
    {
        try
        {
            var context = new ContextExecution { Database = this };
            var evaluator = new ExpressionEvaluator(context);
            return evaluator.Evaluate(expression, row);
        }
        catch
        {
            // If evaluation fails, return null
            return WitSqlValue.Null;
        }
    }

    /// <summary>
    /// Determines the data type from a WitSqlValue for expression indexes.
    /// </summary>
    private static WitDataType DetermineTypeFromValue(WitSqlValue value)
    {
        return value.Type switch
        {
            WitSqlType.Integer => WitDataType.Int64,
            WitSqlType.Real => WitDataType.Float64,
            WitSqlType.Text => WitDataType.StringVariable,
            WitSqlType.Blob => WitDataType.BinaryVariable,
            WitSqlType.Boolean => WitDataType.Boolean,
            WitSqlType.Null => WitDataType.StringVariable, // Default for nulls
            _ => WitDataType.StringVariable
        };
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
}
