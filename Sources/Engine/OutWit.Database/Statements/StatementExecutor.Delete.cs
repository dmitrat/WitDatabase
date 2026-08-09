using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Iterators;
using OutWit.Database.Optimizers;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

/// <summary>
/// DELETE statement execution.
/// </summary>
public sealed partial class StatementExecutor
{
    #region DELETE

    private WitSqlResult ExecuteDelete(WitSqlStatementDelete delete)
    {
        // Get table definition - needed for schema and RETURNING
        var table = m_context.Database.GetTable(delete.TableName)
            ?? throw new InvalidOperationException($"Table '{delete.TableName}' not found");

        // Try fast path for simple PK-based DELETE (no USING clause, no triggers, simple WHERE)
        if (TryExecuteDeleteFastPath(delete, table, out var fastResult))
        {
            return fastResult;
        }

        // Try batch fast path for PK IN (...) pattern
        if (TryExecuteDeleteBatchFastPath(delete, table, out var batchResult))
        {
            return batchResult;
        }

        // Fall back to standard iterator-based execution
        return ExecuteDeleteStandard(delete, table);
    }

    /// <summary>
    /// Attempts to execute DELETE using fast path (direct row access).
    /// Fast path is used when:
    /// - No USING clause (simple table DELETE)
    /// - Simple PK equality WHERE clause (Id = @value)
    /// - No BEFORE/INSTEAD OF triggers that might modify behavior
    /// </summary>
    private bool TryExecuteDeleteFastPath(WitSqlStatementDelete delete, DefinitionTable table, out WitSqlResult result)
    {
        result = default!;

        // Fast path not applicable with USING clause
        if (delete.UsingClause != null && delete.UsingClause.Count > 0)
            return false;

        // Fast path not applicable without WHERE clause (would delete all rows)
        if (delete.WhereClause == null)
            return false;

        // Check for BEFORE or INSTEAD OF triggers (they might modify behavior)
        var beforeTriggers = m_context.Database.GetTriggersForTable(
            delete.TableName, TriggerEvent.Delete, TriggerTime.Before);
        if (beforeTriggers.Any())
            return false;

        var insteadOfTriggers = m_context.Database.GetTriggersForTable(
            delete.TableName, TriggerEvent.Delete, TriggerTime.InsteadOf);
        if (insteadOfTriggers.Any())
            return false;

        // Try to extract simple PK equality condition
        var pkCondition = TryExtractSimplePkCondition(delete.WhereClause, table);
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
        var existingRow = m_context.Database.GetRowById(delete.TableName, rowId);

        if (existingRow == null)
        {
            // Row not found
            result = new WitSqlResult(0);
            return true;
        }

        // Extract row values (without _rowid prefix in names)
        var oldValues = existingRow.Value.Values.ToArray();
        var columnNames = existingRow.Value.ColumnNames.ToArray();

        // Skip _rowid in column names
        var dataStartIndex = columnNames[0] == "_rowid" ? 1 : 0;
        var oldRow = new WitSqlRow(
            oldValues.Skip(dataStartIndex).ToArray(),
            columnNames.Skip(dataStartIndex).ToArray());

        // Handle cascading actions for foreign keys referencing this table
        HandleCascadingActions(delete.TableName, oldRow, isDelete: true);

        // Handle RETURNING clause
        List<WitSqlRow>? returningRows = null;
        if (delete.ReturningClause != null)
        {
            var returningRow = BuildReturningRow(oldRow, delete.ReturningClause, table);
            returningRows = [returningRow];
        }

        // Perform the delete
        m_context.Database.DeleteRow(delete.TableName, rowId);

        // Fire AFTER DELETE triggers
        WitSqlRow? afterRow = null;
        FireTriggers(delete.TableName, TriggerEvent.Delete, TriggerTime.After, oldRow, ref afterRow, null);

        m_context.LastChangesCount = 1;

        // Return result with RETURNING rows if specified
        if (returningRows != null)
        {
            var schema = BuildReturningSchema(delete.ReturningClause!, table);
            result = new WitSqlResult(1, returningRows, schema);
        }
        else
        {
            result = new WitSqlResult(1);
        }

        return true;
    }

    /// <summary>
    /// Attempts to execute DELETE using batch fast path (direct row access for multiple rows).
    /// Batch fast path is used when:
    /// - No USING clause (simple table DELETE)
    /// - Simple PK IN (...) WHERE clause
    /// - No BEFORE/INSTEAD OF triggers
    /// </summary>
    private bool TryExecuteDeleteBatchFastPath(WitSqlStatementDelete delete, DefinitionTable table, out WitSqlResult result)
    {
        result = default!;

        // Batch fast path not applicable with USING clause
        if (delete.UsingClause != null && delete.UsingClause.Count > 0)
            return false;

        // Batch fast path not applicable without WHERE clause
        if (delete.WhereClause == null)
            return false;

        // Check for BEFORE or INSTEAD OF triggers (they might modify behavior)
        var beforeTriggers = m_context.Database.GetTriggersForTable(
            delete.TableName, TriggerEvent.Delete, TriggerTime.Before);
        if (beforeTriggers.Any())
            return false;

        var insteadOfTriggers = m_context.Database.GetTriggersForTable(
            delete.TableName, TriggerEvent.Delete, TriggerTime.InsteadOf);
        if (insteadOfTriggers.Any())
            return false;

        // Try to extract PK IN (...) condition
        var pkInCondition = TryExtractPkInCondition(delete.WhereClause, table);
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

        int rowsAffected = 0;
        List<WitSqlRow>? returningRows = delete.ReturningClause != null ? [] : null;

        foreach (var rowId in pkValues)
        {
            var existingRow = m_context.Database.GetRowById(delete.TableName, rowId);
            if (existingRow == null)
                continue;

            // Extract row values
            var oldValues = existingRow.Value.Values.ToArray();
            var columnNames = existingRow.Value.ColumnNames.ToArray();

            var dataStartIndex = columnNames[0] == "_rowid" ? 1 : 0;
            var oldRow = new WitSqlRow(
                oldValues.Skip(dataStartIndex).ToArray(),
                columnNames.Skip(dataStartIndex).ToArray());

            // Handle cascading actions for foreign keys referencing this table
            HandleCascadingActions(delete.TableName, oldRow, isDelete: true);

            // Handle RETURNING clause
            if (returningRows != null)
            {
                var returningRow = BuildReturningRow(oldRow, delete.ReturningClause!, table);
                returningRows.Add(returningRow);
            }

            // Perform the delete
            m_context.Database.DeleteRow(delete.TableName, rowId);
            rowsAffected++;

            // Fire AFTER DELETE triggers
            WitSqlRow? afterRow = null;
            FireTriggers(delete.TableName, TriggerEvent.Delete, TriggerTime.After, oldRow, ref afterRow, null);
        }

        m_context.LastChangesCount = rowsAffected;

        if (returningRows != null)
        {
            var schema = BuildReturningSchema(delete.ReturningClause!, table);
            result = new WitSqlResult(rowsAffected, returningRows, schema);
        }
        else
        {
            result = new WitSqlResult(rowsAffected);
        }

        return true;
    }

    /// <summary>
    /// Standard iterator-based DELETE execution.
    /// </summary>
    private WitSqlResult ExecuteDeleteStandard(WitSqlStatementDelete delete, DefinitionTable table)
    {
        // Create iterator - either simple scan or join with USING clause
        var iterator = CreateDeleteIterator(delete);

        iterator.Open();
        var rowsToDelete = new List<(long RowId, WitSqlRow OldRow)>();

        // Determine the alias/prefix for the target table columns
        var tableAlias = delete.TableAlias ?? delete.TableName;

        try
        {
            while (iterator.MoveNext())
            {
                var rowId = GetRowIdFromRow(iterator.Current, tableAlias, delete.TableName);
                var oldRow = ExtractTableRow(iterator.Current, table, tableAlias);
                rowsToDelete.Add((rowId, oldRow));
            }
        }
        finally
        {
            iterator.Dispose();
        }

        // Deduplicate rows by rowId (in case USING produces multiple matches for same row)
        var uniqueRowsToDelete = rowsToDelete
            .GroupBy(x => x.RowId)
            .Select(g => g.First())
            .ToList();

        int rowsAffected = 0;
        List<WitSqlRow>? returningRows = null;

        if (delete.ReturningClause != null)
        {
            returningRows = [];
        }

        foreach (var (rowId, oldRow) in uniqueRowsToDelete)
        {
            // Fire BEFORE DELETE triggers
            WitSqlRow? newRow = null;
            if (!FireTriggers(delete.TableName, TriggerEvent.Delete, TriggerTime.Before, oldRow, ref newRow, null))
                continue; // Trigger cancelled

            // Check for INSTEAD OF triggers
            WitSqlRow? insteadOfRow = null;
            if (FireInsteadOfTrigger(delete.TableName, TriggerEvent.Delete, oldRow, ref insteadOfRow, null))
            {
                rowsAffected++;
                continue; // INSTEAD OF executed, skip normal delete
            }

            // Handle cascading actions for foreign keys referencing this table
            HandleCascadingActions(delete.TableName, oldRow, isDelete: true);

            // Collect RETURNING row before deletion
            if (returningRows != null)
            {
                var returningRow = BuildReturningRow(oldRow, delete.ReturningClause!, table);
                returningRows.Add(returningRow);
            }

            m_context.Database.DeleteRow(delete.TableName, rowId);
            rowsAffected++;

            // Fire AFTER DELETE triggers
            WitSqlRow? afterRow = null;
            FireTriggers(delete.TableName, TriggerEvent.Delete, TriggerTime.After, oldRow, ref afterRow, null);
        }

        m_context.LastChangesCount = rowsAffected;

        // Return result with RETURNING rows if specified
        if (returningRows != null)
        {
            var schema = BuildReturningSchema(delete.ReturningClause!, table);
            return new WitSqlResult(rowsAffected, returningRows, schema);
        }

        return new WitSqlResult(rowsAffected);
    }

    /// <summary>
    /// Creates an iterator for DELETE statement, handling optional USING clause.
    /// Uses index optimization when possible for better performance.
    /// </summary>
    private Interfaces.IResultIterator CreateDeleteIterator(WitSqlStatementDelete delete)
    {
        var tableAlias = delete.TableAlias ?? delete.TableName;

        // If USING clause exists, we need to do a join - can't optimize simply
        if (delete.UsingClause != null && delete.UsingClause.Count > 0)
        {
            return CreateDeleteIteratorWithUsing(delete, tableAlias);
        }

        // Use DML optimizer for potential index access
        var optimizer = new DmlOptimizer(m_context);
        return optimizer.CreateOptimizedIterator(delete.TableName, tableAlias, delete.WhereClause);
    }

    /// <summary>
    /// Creates an iterator for DELETE with USING clause (join scenario).
    /// </summary>
    private Interfaces.IResultIterator CreateDeleteIteratorWithUsing(WitSqlStatementDelete delete, string tableAlias)
    {
        // Create base iterator for target table
        Interfaces.IResultIterator iterator = m_context.Database.CreateTableScan(delete.TableName);
        iterator = new IteratorAlias(iterator, tableAlias);

        // Join with USING clause tables
        foreach (var usingSource in delete.UsingClause!)
        {
            var rightIterator = CreateTableSourceIterator(usingSource);
            iterator = new IteratorJoin(iterator, rightIterator, JoinType.Cross, null, m_context);
        }

        // Apply WHERE filter
        if (delete.WhereClause != null)
        {
            iterator = new IteratorFilter(iterator, delete.WhereClause, m_context);
        }

        return iterator;
    }

    #endregion
}
