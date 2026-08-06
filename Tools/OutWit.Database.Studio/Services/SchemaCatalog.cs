using System.Collections.Concurrent;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// What one connection contains, held so that it can be answered without waiting.
///
/// Completion is typed at, so it has to answer between two keystrokes; every other consumer of the
/// schema in Studio asks the database and awaits it. Those are different requirements, and this is the
/// difference: names are loaded once and kept, columns are loaded per object the first time anything
/// asks and kept afterwards.
///
/// The cache is deliberately not clever about invalidation - <see cref="RefreshAsync"/> is called
/// where the tree is reloaded, which is the same moment and the same trigger (a statement that changes
/// the schema). A cache with its own opinion about when the schema changed would be a second answer to
/// a question the application already answers.
/// </summary>
public interface ISchemaCatalog
{
    IReadOnlyList<string> Tables { get; }

    IReadOnlyList<string> Views { get; }

    IReadOnlyList<RoutineInfo> Routines { get; }

    /// <summary>
    /// The columns of a table or view, or an empty list when they have not been read yet. Never
    /// blocks: a caller that needs them present asks <see cref="LoadColumnsAsync"/> first.
    /// </summary>
    IReadOnlyList<ColumnInfo> Columns(string objectName);

    /// <summary>
    /// Whether the catalogue knows an object by this name, whatever its kind.
    /// </summary>
    bool Knows(string name);

    /// <summary>
    /// Reads the names again. Everything already cached about columns is dropped with them.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Makes sure the columns of these objects are in the cache. Names that are not objects of this
    /// database are ignored rather than refused - the caller is usually a half-typed statement.
    /// </summary>
    Task LoadColumnsAsync(IEnumerable<string> objectNames, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class SchemaCatalog(IDatabaseSession session) : ISchemaCatalog
{
    #region Fields

    private readonly ConcurrentDictionary<string, IReadOnlyList<ColumnInfo>> m_columns =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyList<string> m_tables = [];
    private volatile IReadOnlyList<string> m_views = [];
    private volatile IReadOnlyList<RoutineInfo> m_routines = [];

    #endregion

    #region Properties

    public IReadOnlyList<string> Tables => m_tables;

    public IReadOnlyList<string> Views => m_views;

    public IReadOnlyList<RoutineInfo> Routines => m_routines;

    #endregion

    #region Functions

    public IReadOnlyList<ColumnInfo> Columns(string objectName)
    {
        return m_columns.TryGetValue(objectName, out var columns) ? columns : [];
    }

    public bool Knows(string name)
    {
        return m_tables.Any(table => table.Equals(name, StringComparison.OrdinalIgnoreCase))
            || m_views.Any(view => view.Equals(name, StringComparison.OrdinalIgnoreCase))
            || m_routines.Any(routine => routine.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!session.IsConnected)
            return;

        try
        {
            var tables = await session.GetTablesAsync(ct);
            var views = await session.GetViewsAsync(ct);
            var routines = await session.GetRoutinesAsync(ct);

            m_tables = tables.Select(table => table.Name).ToList();
            m_views = views.ToList();
            m_routines = routines.ToList();
            m_columns.Clear();
        }
        catch (Exception)
        {
            // A catalogue is a convenience over the database, never the reason a query cannot run.
            // What it could not read stays as it was, and completion offers less rather than nothing.
        }
    }

    public async Task LoadColumnsAsync(IEnumerable<string> objectNames, CancellationToken ct = default)
    {
        if (!session.IsConnected)
            return;

        foreach (var name in objectNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (m_columns.ContainsKey(name) || !Knows(name))
                continue;

            try
            {
                m_columns[name] = await session.GetColumnsAsync(name, ct);
            }
            catch (Exception)
            {
                // Same reason. A table whose columns could not be read simply offers none.
            }
        }
    }

    #endregion
}
