using OutWit.Database.Definitions;

namespace OutWit.Database;

/// <summary>
/// DDL (Data Definition Language) operations for views in WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Get View

    /// <summary>
    /// Get a view by name.
    /// </summary>
    /// <param name="viewName">The view name.</param>
    /// <returns>The view definition or null if not found.</returns>
    public DefinitionView? GetView(string viewName)
    {
        return m_schema.GetView(viewName);
    }

    #endregion

    #region Create View

    /// <summary>
    /// Create a view.
    /// </summary>
    /// <param name="name">The view name.</param>
    /// <param name="selectSql">The SELECT statement defining the view.</param>
    /// <param name="columnAliases">Optional column aliases for the view.</param>
    public void CreateView(string name, string selectSql, IReadOnlyList<string>? columnAliases)
    {
        m_schema.CreateView(new DefinitionView
        {
            Name = name,
            SelectSql = selectSql,
            ColumnAliases = columnAliases
        });
    }

    #endregion

    #region Drop View

    /// <summary>
    /// Drop a view.
    /// </summary>
    /// <param name="name">The view name to drop.</param>
    public void DropView(string name)
    {
        m_schema.DropView(name);
    }

    #endregion
}
