using System.Text;
using OutWit.Common.MemoryPack;
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
        m_lock.EnterReadLock();
        try
        {
            return m_views.GetValueOrDefault(name);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets all views.
    /// </summary>
    public IEnumerable<DefinitionView> GetViews()
    {
        m_lock.EnterReadLock();
        try
        {
            return m_views.Values.ToList();
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Creates a new view.
    /// </summary>
    public void CreateView(DefinitionView view)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (m_views.ContainsKey(view.Name))
                throw new InvalidOperationException($"View '{view.Name}' already exists");

            // Check for name conflicts with tables
            if (m_tables.ContainsKey(view.Name))
                throw new InvalidOperationException($"A table with name '{view.Name}' already exists");

            m_views[view.Name] = view;
            SaveViews();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Drops a view.
    /// </summary>
    public bool DropView(string name)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_views.Remove(name))
                return false;

            SaveViews();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    private void SaveViews()
    {
        List<DefinitionView> views = m_views.Values.ToList();
        PutSchemaRecord(VIEWS_KEY_BYTES.AsSpan(), views.ToMemoryPackBytes());
    }

    private void LoadViews()
    {
        var viewsData = GetSchemaRecord(VIEWS_KEY_BYTES.AsSpan());
        if (viewsData == null || viewsData.Length == 0)
            return;

        var views = ReadSchemaRecord<List<DefinitionView>>(viewsData, "views");

        foreach (var view in views)
            m_views[view.Name] = view;
    }

    #endregion
}
