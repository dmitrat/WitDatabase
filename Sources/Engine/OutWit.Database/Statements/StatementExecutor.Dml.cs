using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

public sealed partial class StatementExecutor
{
    #region INSERT

    private WitSqlResult ExecuteInsert(WitSqlStatementInsert insert)
    {
        var table = m_context.Database.GetTable(insert.TableName)
            ?? throw new InvalidOperationException($"Table '{insert.TableName}' not found");

        int rowsAffected = 0;
        long lastRowId = 0;

        if (insert.Values != null)
        {
            foreach (var valueRow in insert.Values)
            {
                var (row, rowId) = BuildInsertRow(table, insert.ColumnNames, valueRow);

                // Fire BEFORE INSERT triggers
                WitSqlRow? newRow = row;
                if (!FireTriggers(insert.TableName, TriggerEvent.Insert, TriggerTime.Before, null, ref newRow))
                    continue; // Trigger cancelled

                row = newRow!.Value;

                // Check for INSTEAD OF triggers
                WitSqlRow? insteadOfRow = row;
                if (FireInsteadOfTrigger(insert.TableName, TriggerEvent.Insert, null, ref insteadOfRow))
                {
                    rowsAffected++;
                    continue; // INSTEAD OF executed, skip normal insert
                }

                ValidateConstraints(table, row, insert.TableName);
                m_context.Database.InsertRow(insert.TableName, row);
                lastRowId = rowId;
                rowsAffected++;

                // Fire AFTER INSERT triggers
                WitSqlRow? afterRow = row;
                FireTriggers(insert.TableName, TriggerEvent.Insert, TriggerTime.After, null, ref afterRow);
            }
        }
        else if (insert.SelectSource != null)
        {
            rowsAffected = ExecuteInsertFromSelect(insert, table);
        }

        // Update context for LAST_INSERT_ROWID() and CHANGES()
        m_context.LastInsertRowId = lastRowId;
        m_context.LastChangesCount = rowsAffected;

        return new WitSqlResult(rowsAffected);
    }

    private int ExecuteInsertFromSelect(WitSqlStatementInsert insert, DefinitionTable table)
    {
        var iterator = m_planner.Plan(insert.SelectSource!);
        iterator.Open();

        int rowsAffected = 0;
        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        try
        {
            while (iterator.MoveNext())
            {
                var values = new WitSqlValue[table.Columns.Count];
                var columnNames = table.Columns.Select(c => c.Name).ToArray();
                long rowId = 0;

                // Initialize with defaults and auto-increment
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var col = table.Columns[i];
                    if (col.IsAutoIncrement)
                    {
                        rowId = m_context.Database.GetNextAutoIncrement(table.Name);
                        values[i] = WitSqlValue.FromInt(rowId);
                    }
                    else if (col.DefaultValue != null)
                    {
                        var defaultExpr = Parser.WitSql.ParseExpression(col.DefaultValue);
                        values[i] = evaluator.Evaluate(defaultExpr, dummyRow);
                    }
                    else
                    {
                        values[i] = WitSqlValue.Null;
                    }
                }

                // Map SELECT columns to INSERT columns
                if (insert.ColumnNames != null && insert.ColumnNames.Count > 0)
                {
                    // Named columns: INSERT INTO table (col1, col2) SELECT ...
                    for (int i = 0; i < insert.ColumnNames.Count && i < iterator.Current.ColumnCount; i++)
                    {
                        var colIndex = table.GetOrdinal(insert.ColumnNames[i]);
                        if (colIndex >= 0)
                        {
                            values[colIndex] = iterator.Current[i];
                        }
                    }
                }
                else
                {
                    // Positional: INSERT INTO table SELECT ...
                    for (int i = 0; i < iterator.Current.ColumnCount && i < table.Columns.Count; i++)
                    {
                        values[i] = iterator.Current[i];
                    }
                }

                var row = new WitSqlRow(values, columnNames);

                // Fire BEFORE INSERT triggers
                WitSqlRow? newRow = row;
                if (!FireTriggers(insert.TableName, TriggerEvent.Insert, TriggerTime.Before, null, ref newRow))
                    continue;

                row = newRow!.Value;

                // Check for INSTEAD OF triggers
                WitSqlRow? insteadOfRow = row;
                if (FireInsteadOfTrigger(insert.TableName, TriggerEvent.Insert, null, ref insteadOfRow))
                {
                    rowsAffected++;
                    continue;
                }

                ValidateConstraints(table, row, insert.TableName);
                m_context.Database.InsertRow(insert.TableName, row);
                rowsAffected++;

                // Fire AFTER INSERT triggers
                WitSqlRow? afterRow = row;
                FireTriggers(insert.TableName, TriggerEvent.Insert, TriggerTime.After, null, ref afterRow);
            }
        }
        finally
        {
            iterator.Dispose();
        }

        return rowsAffected;
    }

    private (WitSqlRow Row, long RowId) BuildInsertRow(
        DefinitionTable table,
        IReadOnlyList<string>? columnNames,
        IReadOnlyList<WitSqlExpression> valueExprs)
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        var values = new WitSqlValue[table.Columns.Count];
        var names = table.Columns.Select(c => c.Name).ToArray();
        long rowId = 0;

        // Initialize with defaults (skip computed columns for now)
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (col.IsComputed)
            {
                // Computed columns will be calculated after all regular values are set
                values[i] = WitSqlValue.Null;
            }
            else if (col.IsAutoIncrement)
            {
                rowId = m_context.Database.GetNextAutoIncrement(table.Name);
                values[i] = WitSqlValue.FromInt(rowId);
            }
            else if (col.DefaultValue != null)
            {
                // Parse and evaluate default expression
                var defaultExpr = Parser.WitSql.ParseExpression(col.DefaultValue);
                values[i] = evaluator.Evaluate(defaultExpr, dummyRow);
            }
            else
            {
                values[i] = WitSqlValue.Null;
            }
        }

        // Set specified values
        if (columnNames != null && columnNames.Count > 0)
        {
            // Named columns: INSERT INTO table (col1, col2) VALUES (val1, val2)
            for (int i = 0; i < columnNames.Count && i < valueExprs.Count; i++)
            {
                var colIndex = table.GetOrdinal(columnNames[i]);
                if (colIndex >= 0)
                {
                    var col = table.Columns[colIndex];
                    // Don't allow setting computed columns directly
                    if (col.IsComputed)
                    {
                        throw new InvalidOperationException(
                            $"Cannot INSERT into computed column '{col.Name}'");
                    }
                    values[colIndex] = evaluator.Evaluate(valueExprs[i], dummyRow);
                }
            }
        }
        else
        {
            // Positional: INSERT INTO table VALUES (val1, val2, ...)
            // Count non-computed columns for positional matching
            int valueIndex = 0;
            for (int i = 0; i < table.Columns.Count && valueIndex < valueExprs.Count; i++)
            {
                var col = table.Columns[i];
                if (!col.IsComputed)
                {
                    values[i] = evaluator.Evaluate(valueExprs[valueIndex], dummyRow);
                    valueIndex++;
                }
            }
        }

        // Now calculate STORED computed columns
        var intermediateRow = new WitSqlRow(values, names);
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (col.IsComputed && col.IsStored && !string.IsNullOrEmpty(col.ComputedExpression))
            {
                var expr = Parser.WitSql.ParseExpression(col.ComputedExpression);
                values[i] = evaluator.Evaluate(expr, intermediateRow);
            }
        }

        // Validate NOT NULL constraints
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (!col.Nullable && values[i].IsNull)
            {
                throw new InvalidOperationException($"NOT NULL constraint failed: {table.Name}.{col.Name}");
            }
        }

        return (new WitSqlRow(values, names), rowId);
    }

    #endregion

    #region UPDATE

    private WitSqlResult ExecuteUpdate(WitSqlStatementUpdate update)
    {
        var table = m_context.Database.GetTable(update.TableName)
            ?? throw new InvalidOperationException($"Table '{update.TableName}' not found");

        // Create a full scan and filter
        var iterator = m_context.Database.CreateTableScan(update.TableName);

        if (update.WhereClause != null)
        {
            iterator = new IteratorFilter(iterator, update.WhereClause, m_context);
        }

        iterator.Open();
        var evaluator = new ExpressionEvaluator(m_context);

        // Get computed columns info
        var storedComputedColumns = table.Columns
            .Where(c => c.IsComputed && c.IsStored)
            .ToList();

        // Collect rows to update (can't modify while iterating)
        var rowsToUpdate = new List<(long RowId, WitSqlRow OldRow, WitSqlRow NewRow)>();

        try
        {
            while (iterator.MoveNext())
            {
                var currentRow = iterator.Current;
                var oldRow = currentRow;
                var newValues = currentRow.Values.ToArray();
                var columnNames = currentRow.ColumnNames.ToArray();

                foreach (var setClause in update.SetClauses)
                {
                    for (int i = 0; i < columnNames.Length; i++)
                    {
                        if (columnNames[i].Equals(setClause.ColumnName, StringComparison.OrdinalIgnoreCase))
                        {
                            newValues[i] = evaluator.Evaluate(setClause.Value, currentRow);
                            break;
                        }
                    }
                }

                // Create intermediate row for computing computed columns
                var intermediateRow = new WitSqlRow(newValues, columnNames);

                // Recalculate STORED computed columns
                // Note: currentRow has _rowid at index 0, so table column index needs +1 offset
                foreach (var computedCol in storedComputedColumns)
                {
                    // Find the column in the row (which includes _rowid as first column)
                    for (int i = 0; i < columnNames.Length; i++)
                    {
                        if (columnNames[i].Equals(computedCol.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(computedCol.ComputedExpression))
                            {
                                var expr = Parser.WitSql.ParseExpression(computedCol.ComputedExpression);
                                newValues[i] = evaluator.Evaluate(expr, intermediateRow);
                            }
                            break;
                        }
                    }
                }

                // Get row ID from _rowid column (added by TableScanIterator)
                var rowId = currentRow["_rowid"].AsInt64();
                rowsToUpdate.Add((rowId, oldRow, new WitSqlRow(newValues, columnNames)));
            }
        }
        finally
        {
            iterator.Dispose();
        }

        // Apply updates with trigger invocation
        int rowsAffected = 0;
        foreach (var (rowId, oldRow, originalNewRow) in rowsToUpdate)
        {
            var newRow = originalNewRow;

            // Fire BEFORE UPDATE triggers (can modify NEW row)
            WitSqlRow? newRowRef = newRow;
            if (!FireTriggers(update.TableName, TriggerEvent.Update, TriggerTime.Before, oldRow, ref newRowRef))
                continue; // Trigger cancelled

            newRow = newRowRef!.Value;

            // Check for INSTEAD OF triggers
            WitSqlRow? insteadOfRow = newRow;
            if (FireInsteadOfTrigger(update.TableName, TriggerEvent.Update, oldRow, ref insteadOfRow))
            {
                rowsAffected++;
                continue; // INSTEAD OF executed, skip normal update
            }

            // Validate NOT NULL constraints
            ValidateNotNullConstraints(table, newRow);

            ValidateConstraints(table, newRow, update.TableName, rowId);
            m_context.Database.UpdateRow(update.TableName, rowId, newRow);
            rowsAffected++;

            // Fire AFTER UPDATE triggers
            WitSqlRow? afterRow = newRow;
            FireTriggers(update.TableName, TriggerEvent.Update, TriggerTime.After, oldRow, ref afterRow);
        }

        m_context.LastChangesCount = rowsAffected;
        return new WitSqlResult(rowsAffected);
    }

    /// <summary>
    /// Validates NOT NULL constraints for a row.
    /// </summary>
    private static void ValidateNotNullConstraints(DefinitionTable table, WitSqlRow row)
    {
        foreach (var col in table.Columns)
        {
            if (!col.Nullable && row[col.Name].IsNull)
            {
                throw new InvalidOperationException($"NOT NULL constraint failed: {table.Name}.{col.Name}");
            }
        }
    }

    #endregion

    #region DELETE

    private WitSqlResult ExecuteDelete(WitSqlStatementDelete delete)
    {
        var iterator = m_context.Database.CreateTableScan(delete.TableName);

        if (delete.WhereClause != null)
        {
            iterator = new IteratorFilter(iterator, delete.WhereClause, m_context);
        }

        iterator.Open();
        var rowsToDelete = new List<(long RowId, WitSqlRow OldRow)>();

        try
        {
            while (iterator.MoveNext())
            {
                var rowId = iterator.Current["_rowid"].AsInt64();
                rowsToDelete.Add((rowId, iterator.Current));
            }
        }
        finally
        {
            iterator.Dispose();
        }

        int rowsAffected = 0;
        foreach (var (rowId, oldRow) in rowsToDelete)
        {
            // Fire BEFORE DELETE triggers
            WitSqlRow? newRow = null;
            if (!FireTriggers(delete.TableName, TriggerEvent.Delete, TriggerTime.Before, oldRow, ref newRow))
                continue; // Trigger cancelled

            // Check for INSTEAD OF triggers
            WitSqlRow? insteadOfRow = null;
            if (FireInsteadOfTrigger(delete.TableName, TriggerEvent.Delete, oldRow, ref insteadOfRow))
            {
                rowsAffected++;
                continue; // INSTEAD OF executed, skip normal delete
            }

            m_context.Database.DeleteRow(delete.TableName, rowId);
            rowsAffected++;

            // Fire AFTER DELETE triggers
            WitSqlRow? afterRow = null;
            FireTriggers(delete.TableName, TriggerEvent.Delete, TriggerTime.After, oldRow, ref afterRow);
        }

        m_context.LastChangesCount = rowsAffected;
        return new WitSqlResult(rowsAffected);
    }

    #endregion
}
