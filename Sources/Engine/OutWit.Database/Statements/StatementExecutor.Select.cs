using OutWit.Database.Parser.Statements;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

public sealed partial class StatementExecutor
{
    #region SELECT

    private WitSqlResult ExecuteSelect(WitSqlStatementSelect select)
    {
        var iterator = m_planner.Plan(select);
        iterator.Open();

        var rows = EnumerateRows(iterator);
        return new WitSqlResult(rows, iterator.Schema);
    }

    #endregion
}
