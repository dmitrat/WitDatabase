using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;

namespace OutWit.Database.Statements;

/// <summary>
/// DDL execution for VIEW operations (CREATE, DROP).
/// </summary>
public sealed partial class StatementExecutor
{
    #region CREATE VIEW

    private WitSqlResult ExecuteCreateView(WitSqlStatementCreateView createView)
    {
        // Check if view already exists
        if (m_context.Database.GetView(createView.ViewName) != null)
        {
            if (createView.IfNotExists)
                return new WitSqlResult();
            throw new InvalidOperationException($"View '{createView.ViewName}' already exists");
        }

        // Only the tree is stored. INFORMATION_SCHEMA renders its own text from it when asked.
        m_context.Database.CreateView(createView.ViewName, createView.Query, createView.ColumnNames);
        return new WitSqlResult();
    }

    #endregion

    #region DROP VIEW

    private WitSqlResult ExecuteDropView(WitSqlStatementDropView dropView)
    {
        var view = m_context.Database.GetView(dropView.ViewName);
        if (view == null)
        {
            if (dropView.IfExists)
                return new WitSqlResult();
            throw new InvalidOperationException($"View '{dropView.ViewName}' does not exist");
        }

        m_context.Database.DropView(dropView.ViewName);
        return new WitSqlResult();
    }

    #endregion
}
