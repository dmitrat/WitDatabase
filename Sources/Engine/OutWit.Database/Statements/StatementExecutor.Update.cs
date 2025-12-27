using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

/// <summary>
/// UPDATE statement execution.
/// </summary>
public sealed partial class StatementExecutor
{
    #region UPDATE

    private WitSqlResult ExecuteUpdate(WitSqlStatementUpdate update)
    {
        var table = m_context.Database.GetTable(update.TableName)
            ?? throw new InvalidOperationException($"Table '{update.TableName}' not found");

        // Validate that we're not trying to UPDATE computed columns or ROWVERSION directly
        foreach (var setClause in update.SetClauses)
        {
            var col = table.GetColumn(setClause.ColumnName);
            if (col != null && col.IsComputed)
            {
                throw new InvalidOperationException(
                    $"Cannot UPDATE computed column '{setClause.ColumnName}'");
            }
            if (col != null && col.Type == WitDataType.RowVersion)
            {
                throw new InvalidOperationException(
                    $"Cannot UPDATE ROWVERSION column '{setClause.ColumnName}'");
            }
        }

        // Create iterator - either simple scan or join with FROM clause
        var iterator = CreateUpdateIterator(update);

        iterator.Open();
        var evaluator = new ExpressionEvaluator(m_context);

        // Get computed columns and ROWVERSION columns info
        var storedComputedColumns = table.Columns
            .Where(c => c.IsComputed && c.IsStored)
            .ToList();

        var rowVersionColumns = table.Columns
            .Where(c => c.Type == WitDataType.RowVersion)
            .ToList();

        // Collect rows to update (can't modify while iterating)
        var rowsToUpdate = new List<(long RowId, WitSqlRow OldRow, WitSqlRow NewRow, WitSqlRow JoinedRow)>();

        // Determine the alias/prefix for the target table columns
        var tableAlias = update.TableAlias ?? update.TableName;

        try
        {
            while (iterator.MoveNext())
            {
                var currentRow = iterator.Current;
                
                // Get row ID - try with alias first, then table name, then direct
                var rowId = GetRowIdFromRow(currentRow, tableAlias, update.TableName);
                
                // Build old row from target table columns only
                var oldRow = ExtractTableRow(currentRow, table, tableAlias);
                var newValues = oldRow.Values.ToArray();
                var columnNames = oldRow.ColumnNames.ToArray();

                foreach (var setClause in update.SetClauses)
                {
                    for (int i = 0; i < columnNames.Length; i++)
                    {
                        if (columnNames[i].Equals(setClause.ColumnName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Evaluate expression with joined row context (for FROM clause access)
                            newValues[i] = evaluator.Evaluate(setClause.Value, currentRow);
                            break;
                        }
                    }
                }

                // Auto-increment ROWVERSION columns
                foreach (var rowVersionCol in rowVersionColumns)
                {
                    for (int i = 0; i < columnNames.Length; i++)
                    {
                        if (columnNames[i].Equals(rowVersionCol.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            newValues[i] = WitSqlValue.FromRowVersion(m_context.Database.GetNextRowVersion(table.Name));
                            break;
                        }
                    }
                }

                // Create intermediate row for computing computed columns
                var intermediateRow = new WitSqlRow(newValues, columnNames);

                // Recalculate STORED computed columns
                foreach (var computedCol in storedComputedColumns)
                {
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

                rowsToUpdate.Add((rowId, oldRow, new WitSqlRow(newValues, columnNames), currentRow));
            }
        }
        finally
        {
            iterator.Dispose();
        }

        // Apply updates with trigger invocation
        int rowsAffected = 0;
        List<WitSqlRow>? returningRows = null;

        if (update.ReturningClause != null)
        {
            returningRows = [];
        }

        foreach (var (rowId, oldRow, originalNewRow, joinedRow) in rowsToUpdate)
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

            // Collect RETURNING row
            if (returningRows != null)
            {
                var returningRow = BuildReturningRow(newRow, update.ReturningClause!, table);
                returningRows.Add(returningRow);
            }

            // Fire AFTER UPDATE triggers
            WitSqlRow? afterRow = newRow;
            FireTriggers(update.TableName, TriggerEvent.Update, TriggerTime.After, oldRow, ref afterRow);
        }

        m_context.LastChangesCount = rowsAffected;

        // Return result with RETURNING rows if specified
        if (returningRows != null)
        {
            var schema = BuildReturningSchema(update.ReturningClause!, table);
            return new WitSqlResult(rowsAffected, returningRows, schema);
        }

        return new WitSqlResult(rowsAffected);
    }

    /// <summary>
    /// Creates an iterator for UPDATE statement, handling optional FROM clause.
    /// </summary>
    private Interfaces.IResultIterator CreateUpdateIterator(WitSqlStatementUpdate update)
    {
        var tableAlias = update.TableAlias ?? update.TableName;
        
        // Create base iterator for target table
        Interfaces.IResultIterator iterator = m_context.Database.CreateTableScan(update.TableName);
        iterator = new IteratorAlias(iterator, tableAlias);

        // If FROM clause exists, join with those tables
        if (update.FromClause != null && update.FromClause.Count > 0)
        {
            foreach (var fromSource in update.FromClause)
            {
                var rightIterator = CreateTableSourceIterator(fromSource);
                iterator = new IteratorJoin(iterator, rightIterator, JoinType.Cross, null, m_context);
            }
        }

        // Apply WHERE filter
        if (update.WhereClause != null)
        {
            iterator = new IteratorFilter(iterator, update.WhereClause, m_context);
        }

        return iterator;
    }

    /// <summary>
    /// Creates an iterator for a table source (simple table, join, or subquery).
    /// </summary>
    private Interfaces.IResultIterator CreateTableSourceIterator(TableSource source)
    {
        return source switch
        {
            TableSourceSimple simple => CreateSimpleTableIterator(simple),
            TableSourceJoin join => CreateJoinIterator(join),
            TableSourceSubquery subquery => CreateSubqueryIterator(subquery),
            _ => throw new NotSupportedException($"Table source type not supported: {source.GetType().Name}")
        };
    }

    private Interfaces.IResultIterator CreateSimpleTableIterator(TableSourceSimple simple)
    {
        // Check if it's a view
        var view = m_context.Database.GetView(simple.TableName);
        if (view != null)
        {
            var viewSelect = Parser.WitSql.ParseStatement(view.SelectSql) as WitSqlStatementSelect
                ?? throw new InvalidOperationException($"View '{view.Name}' contains invalid SELECT statement");
            
            var planner = new Query.QueryPlanner(m_context);
            var viewIterator = planner.Plan(viewSelect);
            return new IteratorAlias(viewIterator, simple.Alias ?? simple.TableName);
        }

        var iterator = m_context.Database.CreateTableScan(simple.TableName);
        return new IteratorAlias(iterator, simple.Alias ?? simple.TableName);
    }

    private Interfaces.IResultIterator CreateJoinIterator(TableSourceJoin join)
    {
        var left = CreateTableSourceIterator(join.Left);
        var right = CreateTableSourceIterator(join.Right);
        return new IteratorJoin(left, right, join.JoinType, join.OnCondition, m_context);
    }

    private Interfaces.IResultIterator CreateSubqueryIterator(TableSourceSubquery subquery)
    {
        var planner = new Query.QueryPlanner(m_context);
        var subqueryIterator = planner.Plan(subquery.Subquery);
        var alias = subquery.Alias ?? throw new InvalidOperationException("Subquery must have an alias");
        return new IteratorAlias(subqueryIterator, alias);
    }

    /// <summary>
    /// Gets the _rowid value from a row, trying different column name patterns.
    /// </summary>
    private static long GetRowIdFromRow(WitSqlRow row, string tableAlias, string tableName)
    {
        // Try alias._rowid first
        if (row.TryGetValue($"{tableAlias}._rowid", out var value))
            return value.AsInt64();
        
        // Try tableName._rowid
        if (tableAlias != tableName && row.TryGetValue($"{tableName}._rowid", out value))
            return value.AsInt64();
        
        // Try plain _rowid
        if (row.TryGetValue("_rowid", out value))
            return value.AsInt64();

        throw new InvalidOperationException("Cannot find _rowid column in row");
    }

    /// <summary>
    /// Extracts only the target table's columns from a joined row.
    /// </summary>
    private static WitSqlRow ExtractTableRow(WitSqlRow joinedRow, DefinitionTable table, string tableAlias)
    {
        var columnNames = new List<string>();
        var values = new List<WitSqlValue>();

        // Add _rowid first
        columnNames.Add("_rowid");
        values.Add(WitSqlValue.FromInt(GetRowIdFromRow(joinedRow, tableAlias, table.Name)));

        foreach (var col in table.Columns)
        {
            columnNames.Add(col.Name);
            
            // Try qualified name first
            if (joinedRow.TryGetValue($"{tableAlias}.{col.Name}", out var value))
            {
                values.Add(value);
            }
            else if (joinedRow.TryGetValue(col.Name, out value))
            {
                values.Add(value);
            }
            else
            {
                values.Add(WitSqlValue.Null);
            }
        }

        return new WitSqlRow(values.ToArray(), columnNames.ToArray());
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
}
