using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.MergeClauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

/// <summary>
/// MERGE and TRUNCATE statement execution.
/// </summary>
public sealed partial class StatementExecutor
{
    #region TRUNCATE

    private WitSqlResult ExecuteTruncate(WitSqlStatementTruncate truncate)
    {
        // Verify table exists
        var table = m_context.Database.GetTable(truncate.TableName)
            ?? throw new InvalidOperationException($"Table '{truncate.TableName}' not found");

        // TRUNCATE does NOT fire triggers (that's intentional per SQL standard)
        m_context.Database.TruncateTable(truncate.TableName);

        // Update context
        m_context.LastChangesCount = 0;

        return new WitSqlResult(0);
    }

    #endregion

    #region MERGE

    private WitSqlResult ExecuteMerge(WitSqlStatementMerge merge)
    {
        var targetTable = m_context.Database.GetTable(merge.TargetTable)
            ?? throw new InvalidOperationException($"Target table '{merge.TargetTable}' not found");

        // Build source iterator
        IResultIterator sourceIterator;
        if (merge.SourceSelect != null)
        {
            // Source is a subquery
            sourceIterator = m_planner.Plan(merge.SourceSelect);
        }
        else if (merge.SourceTable != null)
        {
            // Source is a table
            sourceIterator = m_context.Database.CreateTableScan(merge.SourceTable);
        }
        else
        {
            throw new InvalidOperationException("MERGE statement must have a source table or subquery");
        }

        sourceIterator.Open();
        var evaluator = new ExpressionEvaluator(m_context);

        // Build alias mapping for column references
        var targetAlias = merge.TargetAlias ?? merge.TargetTable;
        var sourceAlias = merge.SourceAlias ?? merge.SourceTable ?? "_source";

        int rowsAffected = 0;

        try
        {
            // Process each source row
            while (sourceIterator.MoveNext())
            {
                var sourceRow = sourceIterator.Current;

                // Find matching row in target table
                var (matchedRowId, matchedTargetRow) = FindMatchingTargetRow(
                    targetTable, merge.OnCondition, sourceRow, evaluator, targetAlias, sourceAlias);

                // Process WHEN clauses in order - first matching clause wins
                foreach (var whenClause in merge.WhenClauses)
                {
                    bool shouldProcess = whenClause.IsMatched
                        ? matchedRowId != null
                        : matchedRowId == null;

                    if (!shouldProcess)
                        continue;

                    // Check additional condition if present (supports complex expressions)
                    if (whenClause.Condition != null)
                    {
                        var combinedRow = BuildMergeRow(matchedTargetRow, sourceRow, targetAlias, sourceAlias);
                        var conditionResult = evaluator.Evaluate(whenClause.Condition, combinedRow);
                        if (!conditionResult.IsTrue)
                            continue;
                    }

                    // Execute the action
                    var executed = ExecuteMergeAction(
                        merge.TargetTable, targetTable, whenClause,
                        matchedRowId, matchedTargetRow, sourceRow,
                        evaluator, targetAlias, sourceAlias);

                    if (executed)
                        rowsAffected++;

                    // Only execute the first matching WHEN clause
                    break;
                }
            }
        }
        finally
        {
            sourceIterator.Dispose();
        }

        m_context.LastChangesCount = rowsAffected;
        return new WitSqlResult(rowsAffected);
    }

    /// <summary>
    /// Executes a MERGE action (UPDATE, DELETE, or INSERT).
    /// </summary>
    /// <returns>True if an action was executed.</returns>
    private bool ExecuteMergeAction(
        string tableName,
        DefinitionTable targetTable,
        ClauseMergeWhen whenClause,
        long? matchedRowId,
        WitSqlRow? matchedTargetRow,
        WitSqlRow sourceRow,
        ExpressionEvaluator evaluator,
        string targetAlias,
        string sourceAlias)
    {
        switch (whenClause.ActionType)
        {
            case MergeActionType.Update:
                if (matchedRowId != null && whenClause.SetClauses != null)
                {
                    ExecuteMergeUpdate(targetTable, matchedRowId.Value, matchedTargetRow!.Value,
                        sourceRow, whenClause.SetClauses, evaluator, targetAlias, sourceAlias);
                    return true;
                }
                break;

            case MergeActionType.Delete:
                if (matchedRowId != null)
                {
                    m_context.Database.DeleteRow(tableName, matchedRowId.Value);
                    return true;
                }
                break;

            case MergeActionType.Insert:
                if (matchedRowId == null)
                {
                    ExecuteMergeInsert(targetTable, sourceRow, whenClause, evaluator, sourceAlias);
                    return true;
                }
                break;
        }

        return false;
    }

    /// <summary>
    /// Finds a matching row in the target table based on the ON condition.
    /// </summary>
    private (long? RowId, WitSqlRow? Row) FindMatchingTargetRow(
        DefinitionTable targetTable,
        WitSqlExpression onCondition,
        WitSqlRow sourceRow,
        ExpressionEvaluator evaluator,
        string targetAlias,
        string sourceAlias)
    {
        var iterator = m_context.Database.CreateTableScan(targetTable.Name);
        iterator.Open();

        try
        {
            while (iterator.MoveNext())
            {
                var targetRow = iterator.Current;
                var combinedRow = BuildMergeRow(targetRow, sourceRow, targetAlias, sourceAlias);

                var matchResult = evaluator.Evaluate(onCondition, combinedRow);
                if (matchResult.IsTrue)
                {
                    var rowId = targetRow["_rowid"].AsInt64();
                    return (rowId, targetRow);
                }
            }
        }
        finally
        {
            iterator.Dispose();
        }

        return (null, null);
    }

    /// <summary>
    /// Builds a combined row from target and source rows with proper aliasing.
    /// Supports complex expressions by providing both aliased and unaliased column access.
    /// </summary>
    private static WitSqlRow BuildMergeRow(
        WitSqlRow? targetRow,
        WitSqlRow sourceRow,
        string targetAlias,
        string sourceAlias)
    {
        var values = new List<WitSqlValue>();
        var names = new List<string>();

        // Add target columns with alias prefix
        if (targetRow != null)
        {
            for (int i = 0; i < targetRow.Value.ColumnCount; i++)
            {
                var colName = targetRow.Value.ColumnNames[i];
                var value = targetRow.Value[i];

                // Add with alias prefix (t.Column)
                values.Add(value);
                names.Add($"{targetAlias}.{colName}");

                // Also add without prefix for simple references (skip _rowid)
                if (!colName.Equals("_rowid", StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(value);
                    names.Add(colName);
                }
            }
        }

        // Add source columns with alias prefix
        for (int i = 0; i < sourceRow.ColumnCount; i++)
        {
            var colName = sourceRow.ColumnNames[i];
            var value = sourceRow[i];

            // Add with alias prefix (s.Column)
            values.Add(value);
            names.Add($"{sourceAlias}.{colName}");

            // Also add without prefix for unqualified references
            // (only if not conflicting with target columns)
            if (targetRow == null || !targetRow.Value.ColumnNames.Contains(colName, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value);
                names.Add(colName);
            }
        }

        return new WitSqlRow([.. values], [.. names]);
    }

    /// <summary>
    /// Executes the UPDATE action for a MERGE statement.
    /// </summary>
    private void ExecuteMergeUpdate(
        DefinitionTable targetTable,
        long rowId,
        WitSqlRow targetRow,
        WitSqlRow sourceRow,
        IReadOnlyList<ClauseSet> setClauses,
        ExpressionEvaluator evaluator,
        string targetAlias,
        string sourceAlias)
    {
        var combinedRow = BuildMergeRow(targetRow, sourceRow, targetAlias, sourceAlias);
        var newValues = targetRow.Values.ToArray();
        var columnNames = targetRow.ColumnNames.ToArray();

        foreach (var setClause in setClauses)
        {
            for (int i = 0; i < columnNames.Length; i++)
            {
                if (columnNames[i].Equals(setClause.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    newValues[i] = evaluator.Evaluate(setClause.Value, combinedRow);
                    break;
                }
            }
        }

        // Recalculate STORED computed columns
        RecalculateStoredComputedColumns(targetTable, newValues, columnNames, evaluator);

        var updatedRow = new WitSqlRow(newValues, columnNames);

        // Validate constraints
        ValidateNotNullConstraints(targetTable, updatedRow);
        updatedRow = CoerceDeclaredScale(targetTable, updatedRow);
        ValidateConstraints(targetTable, updatedRow, targetTable.Name, rowId);

        // Perform update
        m_context.Database.UpdateRow(targetTable.Name, rowId, updatedRow);
    }

    /// <summary>
    /// Executes the INSERT action for a MERGE statement.
    /// </summary>
    private void ExecuteMergeInsert(
        DefinitionTable targetTable,
        WitSqlRow sourceRow,
        ClauseMergeWhen whenClause,
        ExpressionEvaluator evaluator,
        string sourceAlias)
    {
        var values = new WitSqlValue[targetTable.Columns.Count];
        var names = targetTable.Columns.Select(c => c.Name).ToArray();
        var dummyRow = new WitSqlRow([], []);
        long rowId = 0;

        // Initialize with defaults (skip computed columns for now)
        for (int i = 0; i < targetTable.Columns.Count; i++)
        {
            var col = targetTable.Columns[i];
            if (col.IsComputed)
            {
                values[i] = WitSqlValue.Null;
            }
            else if (col.IsAutoIncrement)
            {
                rowId = m_context.Database.GetNextAutoIncrement(targetTable.Name);
                values[i] = WitSqlValue.FromInt(rowId);
            }
            else if (col.ResolveDefault() is not null)
            {
                var defaultExpr = col.ResolveDefault()!;
                values[i] = evaluator.Evaluate(defaultExpr, dummyRow);
            }
            else
            {
                values[i] = WitSqlValue.Null;
            }
        }

        // Build source row with alias prefix for expression evaluation
        var sourceRowForEval = BuildSourceRowForEval(sourceRow, sourceAlias);

        // Set values from INSERT columns/values
        if (whenClause.InsertColumns != null && whenClause.InsertValues != null)
        {
            for (int i = 0; i < whenClause.InsertColumns.Count && i < whenClause.InsertValues.Count; i++)
            {
                var colIndex = targetTable.GetOrdinal(whenClause.InsertColumns[i]);
                if (colIndex >= 0)
                {
                    var col = targetTable.Columns[colIndex];
                    if (col.IsComputed)
                    {
                        throw new InvalidOperationException(
                            $"Cannot INSERT into computed column '{col.Name}'");
                    }
                    values[colIndex] = evaluator.Evaluate(whenClause.InsertValues[i], sourceRowForEval);
                }
            }
        }
        else if (whenClause.InsertValues != null)
        {
            // Positional: INSERT VALUES (...)
            int valueIndex = 0;
            for (int i = 0; i < targetTable.Columns.Count && valueIndex < whenClause.InsertValues.Count; i++)
            {
                var col = targetTable.Columns[i];
                if (!col.IsComputed)
                {
                    values[i] = evaluator.Evaluate(whenClause.InsertValues[valueIndex], sourceRowForEval);
                    valueIndex++;
                }
            }
        }

        // Calculate STORED computed columns
        RecalculateStoredComputedColumns(targetTable, values, names, evaluator);

        var row = new WitSqlRow(values, names);

        // Validate NOT NULL constraints
        for (int i = 0; i < targetTable.Columns.Count; i++)
        {
            var col = targetTable.Columns[i];
            if (!col.Nullable && values[i].IsNull)
            {
                throw new InvalidOperationException($"NOT NULL constraint failed: {targetTable.Name}.{col.Name}");
            }
        }

        row = CoerceDeclaredScale(targetTable, row);
        ValidateConstraints(targetTable, row, targetTable.Name);
        m_context.Database.InsertRow(targetTable.Name, row);
    }

    /// <summary>
    /// Builds a row from source with alias prefix for expression evaluation.
    /// </summary>
    private static WitSqlRow BuildSourceRowForEval(WitSqlRow sourceRow, string sourceAlias)
    {
        var values = new List<WitSqlValue>();
        var names = new List<string>();

        for (int i = 0; i < sourceRow.ColumnCount; i++)
        {
            var value = sourceRow[i];
            var colName = sourceRow.ColumnNames[i];

            // Add with alias prefix
            values.Add(value);
            names.Add($"{sourceAlias}.{colName}");

            // Also add without prefix
            values.Add(value);
            names.Add(colName);
        }

        return new WitSqlRow([.. values], [.. names]);
    }

    /// <summary>
    /// Recalculates STORED computed columns for a row.
    /// </summary>
    private static void RecalculateStoredComputedColumns(
        DefinitionTable table,
        WitSqlValue[] values,
        string[] columnNames,
        ExpressionEvaluator evaluator)
    {
        var storedComputedColumns = table.Columns.Where(c => c.IsComputed && c.IsStored).ToList();
        if (storedComputedColumns.Count == 0)
            return;

        var intermediateRow = new WitSqlRow(values, columnNames);

        foreach (var computedCol in storedComputedColumns)
        {
            for (int i = 0; i < columnNames.Length; i++)
            {
                if (columnNames[i].Equals(computedCol.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (computedCol.ResolveComputed() is not null)
                    {
                        var expr = computedCol.ResolveComputed()!;
                        values[i] = evaluator.Evaluate(expr, intermediateRow);
                    }
                    break;
                }
            }
        }
    }

    #endregion
}
