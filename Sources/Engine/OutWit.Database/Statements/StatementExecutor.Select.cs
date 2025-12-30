using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

public sealed partial class StatementExecutor
{
    #region SELECT

    private WitSqlResult ExecuteSelect(WitSqlStatementSelect select)
    {
        var iterator = m_planner.Plan(select);
        iterator.Open();

        // Create cleanup action for CTE state
        // This will be called when the result is disposed
        var context = m_context;
        Action cleanupAction = () =>
        {
            // Clear CTE definitions and cache after query execution
            // CTEs are scoped to a single statement
            context.CteDefinitions.Clear();
            context.CteCache.Clear();
            
            // Clean up any CTE-related state
            var keysToRemove = context.State.Keys
                .Where(k => k.StartsWith("CTE_"))
                .ToList();
            foreach (var key in keysToRemove)
            {
                context.State.Remove(key);
            }
        };

        // Return streaming result - rows are read on demand, not materialized
        return new WitSqlResult(iterator, cleanupAction);
    }

    #endregion
}
