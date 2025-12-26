using OutWit.Database.Context;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

public sealed partial class StatementExecutor
{
    #region Trigger Invocation

    /// <summary>
    /// Fires triggers for a table operation.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="evt">The trigger event (Insert/Update/Delete).</param>
    /// <param name="time">When to fire (Before/After).</param>
    /// <param name="oldRow">The old row (for UPDATE/DELETE).</param>
    /// <param name="newRow">The new row (for INSERT/UPDATE). Modified by BEFORE triggers.</param>
    /// <returns>True if operation should continue, false if cancelled by trigger.</returns>
    private bool FireTriggers(
        string tableName,
        TriggerEvent evt,
        TriggerTime time,
        WitSqlRow? oldRow,
        ref WitSqlRow? newRow)
    {
        var triggers = m_context.Database.GetTriggersForTable(tableName, evt, time);

        foreach (var trigger in triggers)
        {
            if (!ExecuteTrigger(trigger, oldRow, ref newRow))
                return false; // Operation cancelled
        }

        return true;
    }

    /// <summary>
    /// Fires INSTEAD OF triggers for a table operation.
    /// </summary>
    /// <returns>True if INSTEAD OF trigger was executed (skip normal operation).</returns>
    private bool FireInsteadOfTrigger(
        string tableName,
        TriggerEvent evt,
        WitSqlRow? oldRow,
        ref WitSqlRow? newRow)
    {
        var triggers = m_context.Database.GetTriggersForTable(tableName, evt, TriggerTime.InsteadOf);

        foreach (var trigger in triggers)
        {
            if (ExecuteTrigger(trigger, oldRow, ref newRow))
                return true; // INSTEAD OF executed
        }

        return false;
    }

    private bool ExecuteTrigger(DefinitionTrigger trigger, WitSqlRow? oldRow, ref WitSqlRow? newRow)
    {
        // Set up trigger context
        var savedContext = m_context.TriggerContext;
        m_context.TriggerContext = new ContextTrigger
        {
            OldRow = oldRow,
            NewRow = newRow,
            Cancel = false,
            TriggerName = trigger.Name
        };

        try
        {
            // Evaluate WHEN condition if present
            if (trigger.WhenCondition != null)
            {
                var whenExpr = WitSql.ParseExpression(trigger.WhenCondition);
                var evaluator = new ExpressionEvaluator(m_context);
                var contextRow = newRow ?? oldRow ?? new WitSqlRow([], []);
                var result = evaluator.Evaluate(whenExpr, contextRow);

                if (result.IsNull || !result.AsBool())
                    return true; // Condition not met, continue operation
            }

            // Execute trigger body statements
            ExecuteTriggerBody(trigger.Body);

            // Check if operation was cancelled (BEFORE triggers only)
            if (trigger.Time == TriggerTime.Before && m_context.TriggerContext.Cancel)
            {
                return false; // Operation cancelled
            }

            // Update NEW row with any modifications from BEFORE trigger
            if (trigger.Time == TriggerTime.Before && m_context.TriggerContext.NewRow.HasValue)
            {
                newRow = m_context.TriggerContext.NewRow;
            }

            // INSTEAD OF trigger was executed
            return trigger.Time == TriggerTime.InsteadOf;
        }
        finally
        {
            m_context.TriggerContext = savedContext;
        }
    }

    private void ExecuteTriggerBody(string bodySql)
    {
        // Parse and execute each statement in the trigger body
        try
        {
            var statements = bodySql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var stmtSql in statements)
            {
                if (string.IsNullOrWhiteSpace(stmtSql) || stmtSql.StartsWith("/*"))
                    continue;

                var stmt = WitSql.ParseStatement(stmtSql);
                Execute(stmt);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error executing trigger body: {ex.Message}", ex);
        }
    }

    #endregion
}
