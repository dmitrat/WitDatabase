using System.Text;
using OutWit.Common.Json;
using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Views management part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Views

    /// <summary>
    /// Gets a view by name.
    /// </summary>
    public DefinitionView? GetView(string name)
    {
        return m_views.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets all views.
    /// </summary>
    public IEnumerable<DefinitionView> GetViews() => m_views.Values;

    /// <summary>
    /// Creates a new view.
    /// </summary>
    public void CreateView(DefinitionView view)
    {
        if (m_views.ContainsKey(view.Name))
            throw new InvalidOperationException($"View '{view.Name}' already exists");

        // Check for name conflicts with tables
        if (m_tables.ContainsKey(view.Name))
            throw new InvalidOperationException($"A table with name '{view.Name}' already exists");

        m_views[view.Name] = view;
        SaveViews();
    }

    /// <summary>
    /// Drops a view.
    /// </summary>
    public bool DropView(string name)
    {
        if (!m_views.Remove(name))
            return false;

        SaveViews();
        return true;
    }

    private void SaveViews()
    {
        List<DefinitionView> views = m_views.Values.ToList();
        m_store.Put(Encoding.UTF8.GetBytes(VIEWS_KEY).AsSpan(), views.ToJsonBytes());
    }

    private void LoadViews()
    {
        var viewsData = m_store.Get(Encoding.UTF8.GetBytes(VIEWS_KEY).AsSpan());
        if (viewsData == null || viewsData.Length == 0)
            return;

        var views = viewsData.FromJsonBytes<List<DefinitionView>>();
        if (views == null)
            return;

        foreach (var view in views)
            m_views[view.Name] = view;
    }

    #endregion
}
