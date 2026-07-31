using System.Buffers;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Iterators;
using OutWit.Database.Optimizers;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
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

        // Try fast path for simple PK-based UPDATE (no FROM clause, no triggers, simple WHERE)
        if (TryExecuteUpdateFastPath(update, table, out var fastResult))
        {
            return fastResult;
        }

        // Try batch fast path for PK IN (...) pattern
        if (TryExecuteUpdateBatchFastPath(update, table, out var batchResult))
        {
            return batchResult;
        }

        // Try streaming path for bulk updates without BEFORE/INSTEAD OF triggers
        if (TryExecuteUpdateStreaming(update, table, out var streamingResult))
        {
            return streamingResult;
        }

        // Fall back to standard iterator-based execution
        return ExecuteUpdateStandard(update, table);
    }

    /// <summary>
    /// Attempts to execute UPDATE using fast path (direct row access).
    /// Fast path is used when:
    /// - No FROM clause (simple table UPDATE)
    /// - Simple PK equality WHERE clause (Id = @value)
    /// - No BEFORE/INSTEAD OF triggers that might modify behavior
    /// </summary>
    private bool TryExecuteUpdateFastPath(WitSqlStatementUpdate update, DefinitionTable table, out WitSqlResult result)
    {
        result = default!;
        
        // Fast path not applicable with FROM clause
        if (update.FromClause != null && update.FromClause.Count > 0)
            return false;
        
        // Fast path not applicable without WHERE clause
        if (update.WhereClause == null)
            return false;

        // Check for BEFORE or INSTEAD OF triggers (they might modify behavior)
        var beforeTriggers = m_context.Database.GetTriggersForTable(
            update.TableName, TriggerEvent.Update, TriggerTime.Before);
        if (beforeTriggers.Any())
            return false;

        var insteadOfTriggers = m_context.Database.GetTriggersForTable(
            update.TableName, TriggerEvent.Update, TriggerTime.InsteadOf);
        if (insteadOfTriggers.Any())
            return false;

        // Try to extract simple PK equality condition
        var pkCondition = TryExtractSimplePkCondition(update.WhereClause, table);
        if (pkCondition == null)
            return false;

        // Evaluate PK value
        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);
        
        WitSqlValue pkValue;
        try
        {
            pkValue = evaluator.Evaluate(pkCondition.Value.ValueExpression, dummyRow);
        }
        catch
        {
            return false; // Expression evaluation failed
        }

        if (pkValue.IsNull)
        {
            // NULL PK matches nothing
            result = new WitSqlResult(0);
            return true;
        }

        // Get row directly by ID
        long rowId = pkValue.AsInt64();
        var existingRow = m_context.Database.GetRowById(update.TableName, rowId);
        
        if (existingRow == null)
        {
            // Row not found
            result = new WitSqlResult(0);
            return true;
        }

        // Extract row values (without _rowid prefix in names)
        var oldValues = existingRow.Value.Values.ToArray();
        var columnNames = existingRow.Value.ColumnNames.ToArray();

        // Skip _rowid in column names for update
        var dataStartIndex = columnNames[0] == "_rowid" ? 1 : 0;
        var dataColumnCount = columnNames.Length - dataStartIndex;
        
        var newValues = new WitSqlValue[dataColumnCount];
        var dataColumnNames = new string[dataColumnCount];
        
        for (int i = 0; i < dataColumnCount; i++)
        {
            newValues[i] = oldValues[i + dataStartIndex];
            dataColumnNames[i] = columnNames[i + dataStartIndex];
        }

        // Track which columns are being modified
        var modifiedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Apply SET clauses
        foreach (var setClause in update.SetClauses)
        {
            for (int i = 0; i < dataColumnNames.Length; i++)
            {
                if (dataColumnNames[i].Equals(setClause.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    var newValue = evaluator.Evaluate(setClause.Value, existingRow.Value);
                    // Only mark as modified if value actually changed
                    if (!newValues[i].Equals(newValue))
                    {
                        newValues[i] = newValue;
                        modifiedColumns.Add(setClause.ColumnName);
                    }
                    break;
                }
            }
        }

        // Handle ROWVERSION columns
        var rowVersionColumns = table.Columns.Where(c => c.Type == WitDataType.RowVersion).ToList();
        foreach (var rowVersionCol in rowVersionColumns)
        {
            for (int i = 0; i < dataColumnNames.Length; i++)
            {
                if (dataColumnNames[i].Equals(rowVersionCol.Name, StringComparison.OrdinalIgnoreCase))
                {
                    newValues[i] = WitSqlValue.FromRowVersion(m_context.Database.GetNextRowVersion(table.Name));
                    break;
                }
            }
        }

        // Handle STORED computed columns
        var storedComputedColumns = table.Columns.Where(c => c.IsComputed && c.IsStored).ToList();
        if (storedComputedColumns.Count > 0)
        {
            var intermediateRow = new WitSqlRow(newValues, dataColumnNames);
            foreach (var computedCol in storedComputedColumns)
            {
                for (int i = 0; i < dataColumnNames.Length; i++)
                {
                    if (dataColumnNames[i].Equals(computedCol.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(computedCol.ComputedExpression))
                        {
                            // Use cached expression parsing for better performance
                            var expr = GetOrParseExpression(computedCol.ComputedExpression);
                            newValues[i] = evaluator.Evaluate(expr, intermediateRow);
                        }
                        break;
                    }
                }
            }
        }

        var newRow = new WitSqlRow(newValues, dataColumnNames);

        // Validate NOT NULL constraints
        ValidateNotNullConstraints(table, newRow);

        // Optimized constraint validation for fast path:
        // - Skip UNIQUE check on PK (it's not being modified and we're updating the same row)
        // - Only validate constraints on modified columns
        newRow = CoerceDeclaredScale(table, newRow);
        ValidateConstraintsFastPath(table, newRow, update.TableName, rowId, modifiedColumns);

        // Perform the update
        HandleReferentialUpdate(update.TableName, existingRow.Value, newRow);
            m_context.Database.UpdateRow(update.TableName, rowId, newRow);

        // Handle RETURNING clause
        if (update.ReturningClause != null)
        {
            var returningRow = BuildReturningRow(newRow, update.ReturningClause, table);
            var schema = BuildReturningSchema(update.ReturningClause, table);
            result = new WitSqlResult(1, [returningRow], schema);
        }
        else
        {
            result = new WitSqlResult(1);
        }

        // Fire AFTER UPDATE triggers
        var oldRow = new WitSqlRow(
            oldValues.Skip(dataStartIndex).ToArray(), 
            columnNames.Skip(dataStartIndex).ToArray());
        WitSqlRow? afterRow = newRow;
        FireTriggers(update.TableName, TriggerEvent.Update, TriggerTime.After, oldRow, ref afterRow);

        m_context.LastChangesCount = 1;
        return true;
    }

    /// <summary>
    /// Attempts to execute UPDATE using batch fast path (direct row access for multiple rows).
    /// Batch fast path is used when:
    /// - No FROM clause (simple table UPDATE)
    /// - Simple PK IN (...) WHERE clause
    /// - No BEFORE/INSTEAD OF triggers
    /// </summary>
    private bool TryExecuteUpdateBatchFastPath(WitSqlStatementUpdate update, DefinitionTable table, out WitSqlResult result)
    {
        result = default!;

        // Batch fast path not applicable with FROM clause
        if (update.FromClause != null && update.FromClause.Count > 0)
            return false;

        // Batch fast path not applicable without WHERE clause
        if (update.WhereClause == null)
            return false;

        // Check for BEFORE or INSTEAD OF triggers (they might modify behavior)
        var beforeTriggers = m_context.Database.GetTriggersForTable(
            update.TableName, TriggerEvent.Update, TriggerTime.Before);
        if (beforeTriggers.Any())
            return false;

        var insteadOfTriggers = m_context.Database.GetTriggersForTable(
            update.TableName, TriggerEvent.Update, TriggerTime.InsteadOf);
        if (insteadOfTriggers.Any())
            return false;

        // Try to extract PK IN (...) condition
        var pkInCondition = TryExtractPkInCondition(update.WhereClause, table);
        if (pkInCondition == null)
            return false;

        // Evaluate all PK values
        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        var pkValues = new List<long>();
        foreach (var valueExpr in pkInCondition.Value.ValueExpressions)
        {
            try
            {
                var pkValue = evaluator.Evaluate(valueExpr, dummyRow);
                if (!pkValue.IsNull)
                {
                    pkValues.Add(pkValue.AsInt64());
                }
            }
            catch
            {
                return false; // Expression evaluation failed
            }
        }

        if (pkValues.Count == 0)
        {
            result = new WitSqlResult(0);
            return true;
        }

        // Get computed and ROWVERSION columns info
        var storedComputedColumns = table.Columns.Where(c => c.IsComputed && c.IsStored).ToList();
        var rowVersionColumns = table.Columns.Where(c => c.Type == WitDataType.RowVersion).ToList();

        // Determine which columns will be modified by SET clauses
        var setColumnNames = update.SetClauses.Select(s => s.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int rowsAffected = 0;
        List<WitSqlRow>? returningRows = update.ReturningClause != null ? [] : null;

        foreach (var rowId in pkValues)
        {
            var existingRow = m_context.Database.GetRowById(update.TableName, rowId);
            if (existingRow == null)
                continue;

            // Extract row values
            var oldValues = existingRow.Value.Values.ToArray();
            var columnNames = existingRow.Value.ColumnNames.ToArray();

            var dataStartIndex = columnNames[0] == "_rowid" ? 1 : 0;
            var dataColumnCount = columnNames.Length - dataStartIndex;

            var newValues = new WitSqlValue[dataColumnCount];
            var dataColumnNames = new string[dataColumnCount];

            for (int i = 0; i < dataColumnCount; i++)
            {
                newValues[i] = oldValues[i + dataStartIndex];
                dataColumnNames[i] = columnNames[i + dataStartIndex];
            }

            // Track actually modified columns for this row
            var modifiedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Apply SET clauses
            foreach (var setClause in update.SetClauses)
            {
                for (int i = 0; i < dataColumnNames.Length; i++)
                {
                    if (dataColumnNames[i].Equals(setClause.ColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        var newValue = evaluator.Evaluate(setClause.Value, existingRow.Value);
                        if (!newValues[i].Equals(newValue))
                        {
                            newValues[i] = newValue;
                            modifiedColumns.Add(setClause.ColumnName);
                        }
                        break;
                    }
                }
            }

            // Handle ROWVERSION columns
            foreach (var rowVersionCol in rowVersionColumns)
            {
                for (int i = 0; i < dataColumnNames.Length; i++)
                {
                    if (dataColumnNames[i].Equals(rowVersionCol.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        newValues[i] = WitSqlValue.FromRowVersion(m_context.Database.GetNextRowVersion(table.Name));
                        break;
                    }
                }
            }

            // Handle STORED computed columns
            if (storedComputedColumns.Count > 0)
            {
                var intermediateRow = new WitSqlRow(newValues, dataColumnNames);
                foreach (var computedCol in storedComputedColumns)
                {
                    for (int i = 0; i < dataColumnNames.Length; i++)
                    {
                        if (dataColumnNames[i].Equals(computedCol.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(computedCol.ComputedExpression))
                            {
                                // Use cached expression parsing for better performance
                                var expr = GetOrParseExpression(computedCol.ComputedExpression);
                                newValues[i] = evaluator.Evaluate(expr, intermediateRow);
                            }
                            break;
                        }
                    }
                }
            }

            var newRow = new WitSqlRow(newValues, dataColumnNames);

            // Validate NOT NULL constraints
            ValidateNotNullConstraints(table, newRow);

            // Optimized constraint validation - only for modified columns
            newRow = CoerceDeclaredScale(table, newRow);
            ValidateConstraintsFastPath(table, newRow, update.TableName, rowId, modifiedColumns);

            // Perform the update
            HandleReferentialUpdate(update.TableName, existingRow.Value, newRow);
            m_context.Database.UpdateRow(update.TableName, rowId, newRow);
            rowsAffected++;

            // Handle RETURNING clause
            if (returningRows != null)
            {
                var returningRow = BuildReturningRow(newRow, update.ReturningClause!, table);
                returningRows.Add(returningRow);
            }

            // Fire AFTER UPDATE triggers
            var oldRow = new WitSqlRow(
                oldValues.Skip(dataStartIndex).ToArray(),
                columnNames.Skip(dataStartIndex).ToArray());
            WitSqlRow? afterRow = newRow;
            FireTriggers(update.TableName, TriggerEvent.Update, TriggerTime.After, oldRow, ref afterRow);
        }

        m_context.LastChangesCount = rowsAffected;

        if (returningRows != null)
        {
            var schema = BuildReturningSchema(update.ReturningClause!, table);
            result = new WitSqlResult(rowsAffected, returningRows, schema);
        }
        else
        {
            result = new WitSqlResult(rowsAffected);
        }

        return true;
    }

    /// <summary>
    /// Attempts to execute UPDATE using streaming (update rows during iteration).
    /// This is used when:
    /// - No FROM clause (simple table UPDATE)
    /// - No BEFORE/INSTEAD OF triggers
    /// - No RETURNING clause (would need to accumulate results)
    /// - No UNIQUE constraints on SET columns (avoids conflicts during streaming)
    /// </summary>
    private bool TryExecuteUpdateStreaming(WitSqlStatementUpdate update, DefinitionTable table, out WitSqlResult result)
    {
        result = default!;

        // Streaming not applicable with FROM clause (need full join context)
        if (update.FromClause != null && update.FromClause.Count > 0)
            return false;

        // Streaming not applicable with RETURNING clause (need all rows)
        if (update.ReturningClause != null)
            return false;

        // Check for BEFORE or INSTEAD OF triggers
        var beforeTriggers = m_context.Database.GetTriggersForTable(
            update.TableName, TriggerEvent.Update, TriggerTime.Before);
        if (beforeTriggers.Any())
            return false;

        var insteadOfTriggers = m_context.Database.GetTriggersForTable(
            update.TableName, TriggerEvent.Update, TriggerTime.InsteadOf);
        if (insteadOfTriggers.Any())
            return false;

        // Get columns being modified
        var setColumnNames = update.SetClauses
            .Select(s => s.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Check if any UNIQUE constraint involves SET columns
        // (streaming could cause temporary duplicates which would fail validation)
        bool hasUniqueOnSetColumns = false;
        foreach (var col in table.Columns)
        {
            if (col.IsUnique && setColumnNames.Contains(col.Name))
            {
                hasUniqueOnSetColumns = true;
                break;
            }
        }
        if (table.UniqueConstraints != null)
        {
            foreach (var uniqueColumns in table.UniqueConstraints)
            {
                if (uniqueColumns.Any(c => setColumnNames.Contains(c)))
                {
                    hasUniqueOnSetColumns = true;
                    break;
                }
            }
        }

        // If UNIQUE constraints on SET columns, fall back to standard path
        if (hasUniqueOnSetColumns)
            return false;

        // Execute streaming update
        result = ExecuteUpdateStreamingCore(update, table, setColumnNames);
        return true;
    }

    /// <summary>
    /// Core streaming UPDATE implementation.
    /// Updates rows immediately during iteration without accumulating.
    /// </summary>
    private WitSqlResult ExecuteUpdateStreamingCore(
        WitSqlStatementUpdate update,
        DefinitionTable table,
        HashSet<string> setColumnNames)
    {
        // First pass: collect row IDs to update (minimal memory)
        var rowIdsToUpdate = new List<long>();
        
        using (var iterator = CreateUpdateIterator(update))
        {
            iterator.Open();
            var tableAlias = update.TableAlias ?? update.TableName;
            
            while (iterator.MoveNext())
            {
                var rowId = GetRowIdFromRow(iterator.Current, tableAlias, update.TableName);
                rowIdsToUpdate.Add(rowId);
            }
        }

        if (rowIdsToUpdate.Count == 0)
        {
            m_context.LastChangesCount = 0;
            return new WitSqlResult(0);
        }

        // Get computed and ROWVERSION columns info
        var storedComputedColumns = table.Columns.Where(c => c.IsComputed && c.IsStored).ToList();
        var rowVersionColumns = table.Columns.Where(c => c.Type == WitDataType.RowVersion).ToList();

        // Check if AFTER triggers exist
        var afterTriggers = m_context.Database.GetTriggersForTable(
            update.TableName, TriggerEvent.Update, TriggerTime.After);
        bool hasAfterTriggers = afterTriggers.Any();

        var evaluator = new ExpressionEvaluator(m_context);
        int rowsAffected = 0;

        // Second pass: update rows by ID
        foreach (var rowId in rowIdsToUpdate)
        {
            var existingRow = m_context.Database.GetRowById(update.TableName, rowId);
            if (existingRow == null)
                continue; // Row was deleted between passes

            // Extract row values
            var oldValues = existingRow.Value.Values;
            var columnNames = existingRow.Value.ColumnNames;

            var dataStartIndex = columnNames[0] == "_rowid" ? 1 : 0;
            var dataColumnCount = columnNames.Count - dataStartIndex;

            var newValues = new WitSqlValue[dataColumnCount];
            var dataColumnNames = new string[dataColumnCount];

            for (int i = 0; i < dataColumnCount; i++)
            {
                newValues[i] = oldValues[i + dataStartIndex];
                dataColumnNames[i] = columnNames[i + dataStartIndex];
            }

            // Track modified columns for this row
            var modifiedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Apply SET clauses
            foreach (var setClause in update.SetClauses)
            {
                for (int i = 0; i < dataColumnNames.Length; i++)
                {
                    if (dataColumnNames[i].Equals(setClause.ColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        var newValue = evaluator.Evaluate(setClause.Value, existingRow.Value);
                        if (!newValues[i].Equals(newValue))
                        {
                            newValues[i] = newValue;
                            modifiedColumns.Add(setClause.ColumnName);
                        }
                        break;
                    }
                }
            }

            // Handle ROWVERSION columns
            foreach (var rowVersionCol in rowVersionColumns)
            {
                for (int i = 0; i < dataColumnNames.Length; i++)
                {
                    if (dataColumnNames[i].Equals(rowVersionCol.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        newValues[i] = WitSqlValue.FromRowVersion(m_context.Database.GetNextRowVersion(table.Name));
                        break;
                    }
                }
            }

            // Handle STORED computed columns
            if (storedComputedColumns.Count > 0)
            {
                var intermediateRow = new WitSqlRow(newValues, dataColumnNames);
                foreach (var computedCol in storedComputedColumns)
                {
                    for (int i = 0; i < dataColumnNames.Length; i++)
                    {
                        if (dataColumnNames[i].Equals(computedCol.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(computedCol.ComputedExpression))
                            {
                                var expr = GetOrParseExpression(computedCol.ComputedExpression);
                                newValues[i] = evaluator.Evaluate(expr, intermediateRow);
                            }
                            break;
                        }
                    }
                }
            }

            var newRow = new WitSqlRow(newValues, dataColumnNames);

            newRow = CoerceDeclaredScale(table, newRow);

            // Validate NOT NULL constraints
            ValidateNotNullConstraints(table, newRow);

            ValidateDeclaredSizes(table, newRow, update.TableName);

            // Validate CHECK and FK constraints only (no UNIQUE since we checked earlier)
            ValidateCheckConstraintsFastPath(table, newRow, update.TableName, modifiedColumns);
            ValidateForeignKeyConstraintsFastPath(table, newRow, update.TableName, modifiedColumns);

            // Perform the update
            HandleReferentialUpdate(update.TableName, existingRow.Value, newRow);
            m_context.Database.UpdateRow(update.TableName, rowId, newRow);
            rowsAffected++;

            // Fire AFTER UPDATE triggers if any
            if (hasAfterTriggers)
            {
                var oldRow = new WitSqlRow(
                    oldValues.Skip(dataStartIndex).ToArray(),
                    columnNames.Skip(dataStartIndex).ToArray());
                WitSqlRow? afterRow = newRow;
                FireTriggers(update.TableName, TriggerEvent.Update, TriggerTime.After, oldRow, ref afterRow);
            }
        }

        m_context.LastChangesCount = rowsAffected;
        return new WitSqlResult(rowsAffected);
    }

    /// <summary>
    /// Optimized constraint validation for UPDATE fast path.
    /// Only validates constraints that are affected by modified columns.
    /// </summary>
    private void ValidateConstraintsFastPath(
        DefinitionTable table, 
        WitSqlRow row, 
        string tableName, 
        long excludeRowId,
        HashSet<string> modifiedColumns)
    {
        // If no columns modified, no validation needed
        if (modifiedColumns.Count == 0)
            return;

        // The declared sizes, which this path did not check at all. It is a THIRD validation entry point
        // beside ValidateConstraints and ValidateConstraintsWithAutoGen, so the enforcement added for
        // INSERT reached UPDATE nowhere - a hundred characters went into a VARCHAR(5) without complaint.
        // Found by a test written for the scale, not for this.
        ValidateDeclaredSizes(table, row, tableName);

        // Validate CHECK constraints only for modified columns
        ValidateCheckConstraintsFastPath(table, row, tableName, modifiedColumns);

        // Validate UNIQUE constraints only if modified columns are part of a UNIQUE constraint
        ValidateUniqueConstraintsFastPath(table, row, tableName, excludeRowId, modifiedColumns);

        // Validate FK constraints only if modified columns are FK columns
        ValidateForeignKeyConstraintsFastPath(table, row, tableName, modifiedColumns);
    }

    /// <summary>
    /// Validate CHECK constraints only for modified columns.
    /// </summary>
    private void ValidateCheckConstraintsFastPath(
        DefinitionTable table, 
        WitSqlRow row, 
        string tableName,
        HashSet<string> modifiedColumns)
    {
        var evaluator = new ExpressionEvaluator(m_context);

        // Validate column-level CHECK constraints only for modified columns
        foreach (var col in table.Columns)
        {
            if (col.CheckExpression == null)
                continue;

            // Only check if this column was modified
            if (!modifiedColumns.Contains(col.Name))
                continue;

            var columnValue = row[col.Name];
            if (columnValue.IsNull)
                continue;

            // Use cached expression parsing
            var checkExpr = GetOrParseExpression(col.CheckExpression);
            var result = evaluator.Evaluate(checkExpr, row);

            if (!result.IsNull && !result.AsBool())
            {
                throw new InvalidOperationException($"CHECK constraint failed for column {tableName}.{col.Name}");
            }
        }

        // Table-level CHECK constraints might depend on any column, so check all
        if (table.CheckExpressions != null)
        {
            foreach (var checkSql in table.CheckExpressions)
            {
                // Use cached expression parsing
                var checkExpr = GetOrParseExpression(checkSql);
                var result = evaluator.Evaluate(checkExpr, row);

                if (!result.IsNull && !result.AsBool())
                {
                    throw new InvalidOperationException($"CHECK constraint failed for table {tableName}");
                }
            }
        }
    }

    /// <summary>
    /// Validate UNIQUE constraints only for modified columns.
    /// </summary>
    private void ValidateUniqueConstraintsFastPath(
        DefinitionTable table, 
        WitSqlRow row, 
        string tableName,
        long excludeRowId,
        HashSet<string> modifiedColumns)
    {
        // Collect unique constraints that involve modified columns
        var constraintsToCheck = new List<IReadOnlyList<string>>();

        // Column-level UNIQUE
        foreach (var col in table.Columns)
        {
            if (col.IsUnique && modifiedColumns.Contains(col.Name))
            {
                constraintsToCheck.Add([col.Name]);
            }
        }

        // Check if any PK column is being modified - if so, we need to validate PK uniqueness
        var pkColumns = table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
        if (pkColumns.Count > 0 && pkColumns.Any(pk => modifiedColumns.Contains(pk)))
        {
            // PK is being modified, need to check PK uniqueness
            constraintsToCheck.Add(pkColumns);
        }

        // Table-level UNIQUE constraints
        if (table.UniqueConstraints != null)
        {
            foreach (var uniqueColumns in table.UniqueConstraints)
            {
                if (uniqueColumns.Any(c => modifiedColumns.Contains(c)))
                {
                    constraintsToCheck.Add(uniqueColumns);
                }
            }
        }

        if (constraintsToCheck.Count == 0)
            return;

        // Try to use indexes for validation
        var indexes = m_context.Database.GetTableIndexes(tableName).ToList();
        var remainingConstraints = new List<IReadOnlyList<string>>();

        foreach (var columns in constraintsToCheck)
        {
            var keyValues = columns.Select(c => row[c]).ToArray();
            
            // Skip if any value is NULL
            if (keyValues.Any(v => v.IsNull))
                continue;

            bool foundViaIndex = false;

            // Try to find matching unique index
            foreach (var index in indexes)
            {
                if (index.IsUnique && 
                    index.Columns.Count == columns.Count &&
                    index.Columns.All(ic => columns.Any(c => c.Equals(ic, StringComparison.OrdinalIgnoreCase))))
                {
                    try
                    {
                        using var seekIterator = m_context.Database.CreateIndexSeek(tableName, index.Name, keyValues);
                        seekIterator.Open();
                        
                        while (seekIterator.MoveNext())
                        {
                            var existingRowId = seekIterator.Current["_rowid"].AsInt64();
                            if (existingRowId != excludeRowId)
                            {
                                throw new InvalidOperationException(
                                    $"UNIQUE constraint failed: {tableName}.{columns[0]}");
                            }
                        }
                        foundViaIndex = true;
                    }
                    catch (InvalidOperationException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Index seek failed, will fall back to scan
                    }
                    break;
                }
            }

            if (!foundViaIndex)
            {
                remainingConstraints.Add(columns);
            }
        }

        // Fall back to scan for remaining constraints
        if (remainingConstraints.Count > 0)
        {
            using var iterator = m_context.Database.CreateTableScan(tableName);
            iterator.Open();

            while (iterator.MoveNext())
            {
                var existingRow = iterator.Current;
                var existingRowId = existingRow["_rowid"].AsInt64();
                
                if (existingRowId == excludeRowId)
                    continue;

                foreach (var columns in remainingConstraints)
                {
                    if (IsUniqueViolation(row, existingRow, columns))
                    {
                        throw new InvalidOperationException(
                            $"UNIQUE constraint failed: {tableName}.{columns[0]}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Validate FK constraints only for modified FK columns.
    /// </summary>
    private void ValidateForeignKeyConstraintsFastPath(
        DefinitionTable table, 
        WitSqlRow row, 
        string tableName,
        HashSet<string> modifiedColumns)
    {
        // Column-level FK
        foreach (var col in table.Columns)
        {
            if (col.ForeignKey != null && modifiedColumns.Contains(col.Name))
            {
                ValidateForeignKeyReference(col.ForeignKey, row, tableName);
            }
        }

        // Table-level FK
        if (table.ForeignKeys != null)
        {
            foreach (var fk in table.ForeignKeys)
            {
                if (fk.Columns.Any(c => modifiedColumns.Contains(c)))
                {
                    ValidateForeignKeyReference(fk, row, tableName);
                }
            }
        }
    }
 
    /// <summary>
    /// Tries to extract a simple PK equality condition from WHERE clause.
    /// Only returns a condition if the PK is AUTOINCREMENT (meaning PK value equals rowId).
    /// </summary>
    private PkCondition? TryExtractSimplePkCondition(WitSqlExpression whereClause, DefinitionTable table)
    {
        // Find single-column PK that is AUTOINCREMENT
        var pkColumns = table.Columns.Where(c => c.IsPrimaryKey).ToList();
        if (pkColumns.Count != 1)
            return null;

        var pkColumn = pkColumns[0];
        
        // Fast path only works when PK value equals _rowid (which is only true for AUTOINCREMENT)
        // For non-AUTOINCREMENT PKs, the PK value is user-specified and differs from internal rowId
        if (!pkColumn.IsAutoIncrement)
            return null;

        // Check for simple equality: Id = @value
        if (whereClause is WitSqlExpressionBinary binary && 
            binary.Operator == BinaryOperatorType.Equal)
        {
            // Check column = value pattern
            if (binary.Left is WitSqlExpressionColumnRef leftCol &&
                leftCol.ColumnName.Equals(pkColumn.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new PkCondition { ValueExpression = binary.Right };
            }
            
            // Check value = column pattern
            if (binary.Right is WitSqlExpressionColumnRef rightCol &&
                rightCol.ColumnName.Equals(pkColumn.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new PkCondition { ValueExpression = binary.Left };
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to extract a PK IN (...) condition from WHERE clause.
    /// Only returns a condition if the PK is AUTOINCREMENT (meaning PK value equals rowId).
    /// </summary>
    private PkInCondition? TryExtractPkInCondition(WitSqlExpression whereClause, DefinitionTable table)
    {
        // Find single-column PK that is AUTOINCREMENT
        var pkColumns = table.Columns.Where(c => c.IsPrimaryKey).ToList();
        if (pkColumns.Count != 1)
            return null;

        var pkColumn = pkColumns[0];
        
        // Fast path only works when PK value equals _rowid (which is only true for AUTOINCREMENT)
        // For non-AUTOINCREMENT PKs, the PK value is user-specified and differs from internal rowId
        if (!pkColumn.IsAutoIncrement)
            return null;

        // Check for IN expression: Id IN (1, 2, 3)
        if (whereClause is WitSqlExpressionIn inExpr && !inExpr.IsNot)
        {
            // Only support value lists, not subqueries
            if (inExpr.Values == null || inExpr.Subquery != null)
                return null;

            // Check if expression is PK column
            if (inExpr.Expression is WitSqlExpressionColumnRef colRef &&
                colRef.ColumnName.Equals(pkColumn.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new PkInCondition { ValueExpressions = inExpr.Values.ToList() };
            }
        }

        return null;
    }

    private readonly struct PkCondition
    {
        public required WitSqlExpression ValueExpression { get; init; }
    }

    private readonly struct PkInCondition
    {
        public required List<WitSqlExpression> ValueExpressions { get; init; }
    }

    /// <summary>
    /// Standard iterator-based UPDATE execution.
    /// </summary>
    private WitSqlResult ExecuteUpdateStandard(WitSqlStatementUpdate update, DefinitionTable table)
    {
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

            newRow = CoerceDeclaredScale(table, newRow);
            ValidateConstraints(table, newRow, update.TableName, rowId);
            HandleReferentialUpdate(update.TableName, oldRow, newRow);
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
    /// Uses index optimization when possible for better performance.
    /// </summary>
    private Interfaces.IResultIterator CreateUpdateIterator(WitSqlStatementUpdate update)
    {
        var tableAlias = update.TableAlias ?? update.TableName;
        
        // If FROM clause exists, we need to do a join - can't optimize simply
        if (update.FromClause != null && update.FromClause.Count > 0)
        {
            return CreateUpdateIteratorWithFrom(update, tableAlias);
        }

        // Use DML optimizer for potential index access
        var optimizer = new DmlOptimizer(m_context);
        return optimizer.CreateOptimizedIterator(update.TableName, tableAlias, update.WhereClause);
    }

    /// <summary>
    /// Creates an iterator for UPDATE with FROM clause (join scenario).
    /// </summary>
    private Interfaces.IResultIterator CreateUpdateIteratorWithFrom(WitSqlStatementUpdate update, string tableAlias)
    {
        // Create base iterator for target table
        Interfaces.IResultIterator iterator = m_context.Database.CreateTableScan(update.TableName);
        iterator = new IteratorAlias(iterator, tableAlias);

        // Join with FROM clause tables
        foreach (var fromSource in update.FromClause!)
        {
            var rightIterator = CreateTableSourceIterator(fromSource);
            iterator = new IteratorJoin(iterator, rightIterator, JoinType.Cross, null, m_context);
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
