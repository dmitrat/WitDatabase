using OutWit.Database.Parser.Statements;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

public sealed partial class StatementExecutor
{
    #region SELECT

    private WitSqlResult ExecuteSelect(WitSqlStatementSelect select)
    {
        try
        {
            var iterator = m_planner.Plan(select);
            iterator.Open();

            // Materialize rows to a list before clearing CTE state
            // This is necessary because EnumerateRows uses yield return (lazy evaluation)
            var rows = EnumerateRows(iterator).ToList();
            return new WitSqlResult(rows, iterator.Schema);
        }
        finally
        {
            // Clear CTE definitions and cache after query execution
            // CTEs are scoped to a single statement
            m_context.CteDefinitions.Clear();
            m_context.CteCache.Clear();
            
            // Clean up any CTE-related state
            var keysToRemove = m_context.State.Keys
                .Where(k => k.StartsWith("CTE_"))
                .ToList();
            foreach (var key in keysToRemove)
            {
                m_context.State.Remove(key);
            }
        }
    }

    #endregion
}
