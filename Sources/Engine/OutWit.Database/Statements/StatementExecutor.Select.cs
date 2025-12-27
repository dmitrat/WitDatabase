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

            var rows = EnumerateRows(iterator);
            return new WitSqlResult(rows, iterator.Schema);
        }
        finally
        {
            // Clear CTE definitions after query execution
            // CTEs are scoped to a single statement
            m_context.CteDefinitions.Clear();
        }
    }

    #endregion
}
