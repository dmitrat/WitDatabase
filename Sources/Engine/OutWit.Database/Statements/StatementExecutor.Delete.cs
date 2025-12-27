using OutWit.Database.Definitions;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Statements;
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
            if (!FireTriggers(delete.TableName, TriggerEvent.Delete, TriggerTime.Before, oldRow, ref newRow))
                continue; // Trigger cancelled

            // Check for INSTEAD OF triggers
            WitSqlRow? insteadOfRow = null;
            if (FireInsteadOfTrigger(delete.TableName, TriggerEvent.Delete, oldRow, ref insteadOfRow))
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
            FireTriggers(delete.TableName, TriggerEvent.Delete, TriggerTime.After, oldRow, ref afterRow);
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
    /// </summary>
    private Interfaces.IResultIterator CreateDeleteIterator(WitSqlStatementDelete delete)
    {
        var tableAlias = delete.TableAlias ?? delete.TableName;
        
        // Create base iterator for target table
        Interfaces.IResultIterator iterator = m_context.Database.CreateTableScan(delete.TableName);
        iterator = new IteratorAlias(iterator, tableAlias);

        // If USING clause exists, join with those tables
        if (delete.UsingClause != null && delete.UsingClause.Count > 0)
        {
            foreach (var usingSource in delete.UsingClause)
            {
                var rightIterator = CreateTableSourceIterator(usingSource);
                iterator = new IteratorJoin(iterator, rightIterator, Parser.Schema.Types.JoinType.Cross, null, m_context);
            }
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
