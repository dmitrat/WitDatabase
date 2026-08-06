using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for the database explorer tree.
///
/// The tree has one root per open connection (WS-3): there used to be exactly one, because there could
/// be exactly one connection. Every node carries the id of the connection it came from, so an action
/// on a node goes to that connection and not to whichever one happens to be active.
/// </summary>
public class DatabaseExplorerViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public DatabaseExplorerViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefault();
        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Nodes = [];
    }

    private void InitCommands()
    {
        RefreshCommand = new RelayCommandAsync(RefreshAsync);
        SelectTop100Command = new RelayCommand(SelectTop100);
        SelectTop1000Command = new RelayCommand(SelectTop1000);
        EditDataCommand = new RelayCommandAsync(EditDataAsync);
        ViewStructureCommand = new RelayCommandAsync(ViewStructureAsync);
        ViewDefinitionCommand = new RelayCommandAsync(ViewDefinitionAsync);
        DropObjectCommand = new RelayCommandAsync(DropObjectAsync);
        RenameObjectCommand = new RelayCommandAsync<string>(RenameObjectAsync);
        BeginRenameCommand = new RelayCommand(BeginRename);
        CommitRenameCommand = new RelayCommandAsync(CommitRenameAsync);
        CancelRenameCommand = new RelayCommand(CancelRename);
        TruncateTableCommand = new RelayCommandAsync(TruncateTableAsync);
        CreateTableCommand = new RelayCommandAsync(CreateTableAsync);
        CreateViewCommand = new RelayCommandAsync(CreateViewAsync);
        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync);
        ExpandNodeCommand = new RelayCommandAsync<DatabaseNode>(ExpandNodeAsync);
        ClearFilterCommand = new RelayCommand(() => Filter = string.Empty);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChangedInternal;

        // Only the closing half. A connection's branch is built by whoever opened it, which is a path
        // that can be awaited; an 'async void' event handler here would refresh the tree twice and
        // would have nowhere to put a failure.
        Connections.SessionClosed += OnSessionClosed;
    }

    #endregion

    #region Sessions

    /// <summary>
    /// The connection a node came from, or null if that connection has since been closed. Nodes hold
    /// an id rather than a reference so that a stale node cannot keep a closed database open.
    /// </summary>
    public IDatabaseSession? SessionFor(DatabaseNode? node)
    {
        return node == null ? null : Connections.Find(node.ConnectionId);
    }

    /// <summary>
    /// The connection of the selected node. This is what the object commands act on - and what the
    /// selection makes active - but NOT what an already open tab runs in (WS-3).
    /// </summary>
    public IDatabaseSession? SelectedSession => SessionFor(SelectedNode);

    private void OnSessionClosed(object? sender, SessionEventArgs e)
    {
        var root = Nodes.FirstOrDefault(node => node.ConnectionId == e.Session.Id);

        if (root == null)
            return;

        if (SelectedNode != null && SelectedNode.ConnectionId == e.Session.Id)
            SelectedNode = null;

        Nodes.Remove(root);

        Logger.LogInformation("Explorer dropped the branch of {Name}; {Count} left",
            e.Session.DisplayName, Nodes.Count);
    }

    #endregion

    #region Functions

    private void SelectTop100()
    {
        SelectTopRows(100);
    }

    private void SelectTop1000()
    {
        SelectTopRows(1000);
    }

    private void SelectTopRows(int limit)
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanBrowseData)
            return;

        var tableName = SelectedNode.Name;
        var sql = $"SELECT * FROM [{tableName}] LIMIT {limit}";

        // The tab is opened in the connection the node came from and stays there, so executing it
        // cannot land in another database however the selection moves afterwards.
        var tab = ApplicationVm.WorkspaceTabsVm.OpenQueryTab(sql, $"{tableName} - Top {limit}", session);

        ApplicationVm.WorkspaceTabsVm.ExecuteQueryCommand.Execute(null);

        Logger.LogInformation("Select top {Limit} from {ObjectName} in {Connection}",
            limit, tab.Title, session.DisplayName);
    }

    private async Task EditDataAsync()
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanEditData)
            return;

        var tableName = SelectedNode.Name;

        await ApplicationVm.WorkspaceTabsVm.OpenTableEditTabAsync(session, tableName);

        ApplicationVm.MainWindowVm.StatusText = $"Editing table: {tableName}";
        Logger.LogInformation("Edit data for table {TableName} in {Connection}", tableName, session.DisplayName);
    }

    private async Task ViewStructureAsync()
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanViewStructure)
            return;

        await ApplicationVm.WorkspaceTabsVm.OpenStructureTabAsync(session, SelectedNode.Name, SelectedNode.NodeType);

        Logger.LogInformation("View structure for {ObjectType} {ObjectName}", SelectedNode.NodeType, SelectedNode.Name);
    }

    private async Task ViewDefinitionAsync()
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanViewDefinition)
            return;

        string? definition;
        var objectType = SelectedNode.NodeType;

        try
        {
            definition = objectType switch
            {
                DatabaseNodeType.Table => await session.GetTableDefinitionAsync(SelectedNode.Name),
                DatabaseNodeType.View => await session.GetViewDefinitionAsync(SelectedNode.Name),
                DatabaseNodeType.Trigger => await session.GetTriggerDefinitionAsync(SelectedNode.Name),
                DatabaseNodeType.Index => await session.GetIndexDefinitionAsync(SelectedNode.Name),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get definition for {ObjectName}", SelectedNode.Name);
            ApplicationVm.MainWindowVm.StatusText = $"Failed to get definition: {ex.Message}";
            return;
        }

        if (string.IsNullOrEmpty(definition))
        {
            ApplicationVm.MainWindowVm.StatusText = $"No definition found for {SelectedNode.Name}";
            return;
        }

        var sql = $"-- Definition for {objectType}: {SelectedNode.Name}\n\n{definition}";
        ApplicationVm.WorkspaceTabsVm.OpenQueryTab(sql, $"{SelectedNode.Name} - Definition", session);

        Logger.LogInformation("Viewed definition for {ObjectName}", SelectedNode.Name);
    }

    private async Task DropObjectAsync()
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanDropObject)
            return;

        var objectType = SelectedNode.NodeType switch
        {
            DatabaseNodeType.Table => "TABLE",
            DatabaseNodeType.View => "VIEW",
            DatabaseNodeType.Index => "INDEX",
            DatabaseNodeType.Trigger => "TRIGGER",
            DatabaseNodeType.Sequence => "SEQUENCE",
            _ => null
        };

        if (objectType == null)
            return;

        var objectName = SelectedNode.Name;
        var sql = $"DROP {objectType} IF EXISTS [{objectName}]";

        try
        {
            await session.ExecuteNonQueryAsync(sql);

            // Clear selection before refresh to avoid stale reference
            SelectedNode = null;

            await RefreshAsync(session);

            ApplicationVm.MainWindowVm.StatusText = $"Dropped {objectType.ToLower()}: {objectName}";
            Logger.LogInformation("Dropped {ObjectType}: {ObjectName} in {Connection}",
                objectType, objectName, session.DisplayName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to drop {objectType.ToLower()}: {ex.Message}";
            Logger.LogError(ex, "Failed to drop {ObjectType}: {ObjectName}", objectType, objectName);
        }
    }

    /// <summary>
    /// F2, deferred here from stage 5.
    ///
    /// It is offered for a TABLE and for nothing else, because that is all the language has: measured
    /// 2026-08-06, <c>ALTER TABLE t RENAME TO</c> works, and ALTER VIEW, ALTER INDEX and ALTER TRIGGER
    /// do not exist at all - there is no way to rename a view, an index or a trigger on this engine
    /// short of dropping it and creating it again.
    ///
    /// The name arrives as the command's parameter: the tree edits in place, and a rename with no new
    /// name is a no-op rather than an error.
    /// </summary>
    private async Task RenameObjectAsync(string? newName)
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanRename || string.IsNullOrWhiteSpace(newName))
            return;

        var oldName = SelectedNode.Name;

        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        try
        {
            await session.ExecuteNonQueryAsync(DdlWriter.RenameTable(oldName, newName));

            SelectedNode = null;

            await session.Catalog.RefreshAsync();
            await RefreshAsync(session);

            ApplicationVm.MainWindowVm.StatusText = $"Renamed {oldName} to {newName}";
            Logger.LogInformation("Renamed table {Old} to {New}", oldName, newName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to rename {oldName}: {ex.Message.Split('\n')[0]}";
            Logger.LogError(ex, "Failed to rename {Old}", oldName);
        }
    }

    /// <summary>
    /// Opens the rename box on the selected row. F2 and the context menu both come here.
    /// </summary>
    private void BeginRename()
    {
        if (SelectedNode == null || !CanRename)
            return;

        SelectedNode.RenameText = SelectedNode.Name;
        SelectedNode.IsRenaming = true;
    }

    private async Task CommitRenameAsync()
    {
        var node = SelectedNode;

        if (node is not { IsRenaming: true })
            return;

        var newName = node.RenameText;
        node.IsRenaming = false;

        await RenameObjectAsync(newName);
    }

    private void CancelRename()
    {
        if (SelectedNode == null)
            return;

        SelectedNode.IsRenaming = false;
        SelectedNode.RenameText = SelectedNode.Name;
    }

    /// <summary>
    /// TRUNCATE, also deferred here from stage 5. It empties the table and cannot be undone - and on
    /// this engine DDL is not rolled back, so there is no transaction to hide behind.
    /// </summary>
    private async Task TruncateTableAsync()
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanTruncate)
            return;

        var table = SelectedNode.Name;

        try
        {
            await session.ExecuteNonQueryAsync(DdlWriter.Truncate(table));

            await RefreshAsync(session);

            ApplicationVm.MainWindowVm.StatusText = $"Emptied {table}";
            ApplicationVm.Notifications.Warning($"{table} was emptied", "TRUNCATE cannot be undone.");

            Logger.LogInformation("Truncated {Table}", table);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to empty {table}: {ex.Message.Split('\n')[0]}";
            Logger.LogError(ex, "Failed to truncate {Table}", table);
        }
    }

    private async Task CreateTableAsync()
    {
        if (ApplicationVm.ActiveSession?.IsConnected != true)
            return;

        var createTableVm = new CreateTableViewModel(ApplicationVm);

        var result = await ApplicationVm.Dialogs.ShowCreateTableAsync(createTableVm);

        if (result)
            Logger.LogInformation("Table created successfully");
    }

    private async Task CreateViewAsync()
    {
        if (ApplicationVm.ActiveSession?.IsConnected != true)
            return;

        var createViewVm = new CreateViewViewModel(ApplicationVm);

        var result = await ApplicationVm.Dialogs.ShowCreateViewAsync(createViewVm);

        if (result)
            Logger.LogInformation("View created successfully");
    }

    private async Task CreateIndexAsync()
    {
        if (ApplicationVm.ActiveSession?.IsConnected != true)
            return;

        var createIndexVm = new CreateIndexViewModel(ApplicationVm);

        // Load tables on dialog open
        createIndexVm.LoadTablesCommand.Execute(null);

        var result = await ApplicationVm.Dialogs.ShowCreateIndexAsync(createIndexVm);

        if (result)
            Logger.LogInformation("Index created successfully");
    }

    /// <summary>
    /// Rebuilds every connection's branch, and removes the branches of connections that are gone.
    /// </summary>
    public async Task RefreshAsync()
    {
        foreach (var root in Nodes.ToList())
        {
            if (Connections.Find(root.ConnectionId) == null)
                Nodes.Remove(root);
        }

        foreach (var session in Connections.Sessions.ToList())
            await RefreshAsync(session);
    }

    /// <summary>
    /// Rebuilds one connection's branch, leaving every other branch - and its expanded state - alone.
    /// </summary>
    public async Task RefreshAsync(IDatabaseSession session)
    {
        if (!session.IsConnected)
        {
            Logger.LogWarning("Not connected to {Name}, dropping its branch", session.DisplayName);

            var stale = Nodes.FirstOrDefault(node => node.ConnectionId == session.Id);

            if (stale != null)
                Nodes.Remove(stale);

            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        // Save expanded state before refresh
        var expandedNodes = SaveExpandedState();
        var firstLoad = Nodes.All(node => node.ConnectionId != session.Id);

        try
        {
            var rootNode = new DatabaseNode
            {
                Name = session.DisplayName,
                NodeType = DatabaseNodeType.Database,
                ConnectionId = session.Id,
                IsExpanded = firstLoad || IsExpanded(expandedNodes, session, DatabaseNodeType.Database, session.DisplayName)
            };

            // The completion catalogue is refreshed here and nowhere else (WS-24): this is the moment
            // the application has already decided the schema may have changed, and a cache with its
            // own opinion about that would be a second answer to a question already answered.
            await session.Catalog.RefreshAsync();

            var tables = await session.GetTablesAsync();
            var views = await session.GetViewsAsync();
            var indexes = await session.GetIndexesAsync();
            var triggers = await session.GetTriggersAsync();
            var sequences = await session.GetSequencesAsync();

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Tables",
                DatabaseNodeType.TablesFolder, DatabaseNodeType.Table,
                tables.Select(table => table.Name), expandedByDefault: firstLoad));

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Views",
                DatabaseNodeType.ViewsFolder, DatabaseNodeType.View, views));

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Indexes",
                DatabaseNodeType.IndexesFolder, DatabaseNodeType.Index, indexes));

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Triggers",
                DatabaseNodeType.TriggersFolder, DatabaseNodeType.Trigger, triggers));

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Sequences",
                DatabaseNodeType.SequencesFolder, DatabaseNodeType.Sequence, sequences));

            // The sixth folder (WS-21). The engine has had functions and procedures since phase 9d
            // and the tree has never shown them - which reads, to a user, as the database not having
            // any.
            var routines = await session.GetRoutinesAsync();

            var routinesFolder = BuildFolder(session, expandedNodes, "Routines",
                DatabaseNodeType.RoutinesFolder, DatabaseNodeType.Routine,
                routines.Select(routine => routine.Name));

            foreach (var node in routinesFolder.Children)
            {
                var routine = routines.First(candidate => candidate.Name == node.Name);

                node.Detail = routine.IsFunction
                    ? $"function -> {routine.DataType}"
                    : "procedure";
                node.ChildrenLoaded = true;
            }

            rootNode.Children.Add(routinesFolder);

            WatchForExpansion(rootNode);

            ReplaceRoot(session, rootNode);

            // The counts are NOT awaited (2.2): the tree is usable the moment it is drawn, and each
            // number appears when its query comes back - or does not, if it takes too long.
            _ = CountRowsAsync(session).ContinueWith(
                task => Logger.LogError(task.Exception, "Counting the tables of {Connection} failed",
                    session.DisplayName),
                TaskContinuationOptions.OnlyOnFaulted);

            ApplicationVm.MainWindowVm.StatusText =
                $"{session.DisplayName}: {tables.Count} tables, {views.Count} views, {indexes.Count} indexes, "
                + $"{triggers.Count} triggers, {sequences.Count} sequences";

            Logger.LogInformation(
                "Explorer refreshed {Connection}: {Tables} tables, {Views} views, {Indexes} indexes, {Triggers} triggers, {Sequences} sequences",
                session.DisplayName, tables.Count, views.Count, indexes.Count, triggers.Count, sequences.Count);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load schema: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error loading schema";
            Logger.LogError(ex, "Failed to refresh the branch of {Connection}", session.DisplayName);

            // Reloading the tree is something Studio does on its own, after a DDL statement or a
            // drop. A failure in it has no dialog to belong to, and the status bar is overwritten by
            // whatever happens next (WS-7).
            ApplicationVm.Notifications.Error("The schema could not be reloaded", ex.Message,
                session.DisplayName);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads a table's columns the moment it is first opened in the tree (WS-15).
    ///
    /// Watched on the NODE rather than handled in the view: the node already tells the tree whether
    /// it is expanded, and a second path through the code-behind would be a second thing to keep in
    /// step with it.
    /// </summary>
    private void WatchForExpansion(DatabaseNode root)
    {
        foreach (var folder in root.Children)
        {
            foreach (var node in folder.Children)
            {
                if (node.ChildrenLoaded)
                    continue;

                var target = node;

                target.PropertyChanged += async (_, e) =>
                {
                    if (e.PropertyName != nameof(DatabaseNode.IsExpanded) || !target.IsExpanded)
                        return;

                    await ExpandNodeAsync(target);
                };
            }
        }
    }

    /// <summary>
    /// Asks for the row count of every table of a connection, and stops asking for any that takes
    /// too long (WS-16).
    ///
    /// Deliberately NOT awaited by the refresh: the names are usable the moment the tree is drawn,
    /// and the numbers arrive as they come. A count is a query, and a query on a table nobody has
    /// opened is not worth a frozen window (2.2).
    /// </summary>
    public async Task CountRowsAsync(IDatabaseSession session, CancellationToken ct = default)
    {
        var root = Nodes.FirstOrDefault(node => node.ConnectionId == session.Id);

        var folder = root?.Children.FirstOrDefault(child => child.NodeType == DatabaseNodeType.TablesFolder);

        if (folder == null)
            return;

        foreach (var table in folder.Children.ToList())
        {
            if (ct.IsCancellationRequested || !session.IsConnected)
                return;

            table.CountState = RowCountState.Counting;

            var count = await session.TryCountRowsAsync(table.Name, CountTimeout, ct);

            table.RowCount = count;
            table.CountState = count == null ? RowCountState.TimedOut : RowCountState.Counted;
            table.Detail = count?.ToString("N0");
        }
    }

    /// <summary>
    /// Loads what a node contains the first time it is opened. For a table or a view that is its
    /// columns (WS-15) - the most frequent question anyone asks of a schema, and no reason to open a
    /// tab for it.
    /// </summary>
    public async Task ExpandNodeAsync(DatabaseNode? node)
    {
        if (node == null || node.ChildrenLoaded)
            return;

        var session = SessionFor(node);

        if (session?.IsConnected != true)
            return;

        node.ChildrenLoaded = true;

        try
        {
            var columns = await session.GetColumnsAsync(node.Name);
            var keys = await session.GetForeignKeysAsync(node.Name);
            var foreign = keys.Select(key => key.FromColumn).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                node.Children.Add(new DatabaseNode
                {
                    Name = column.Name,
                    NodeType = DatabaseNodeType.Column,
                    ConnectionId = node.ConnectionId,
                    ParentName = node.Name,
                    Detail = column.DataType,
                    IsPrimaryKey = column.IsPrimaryKey,
                    IsForeignKey = foreign.Contains(column.Name),
                    IsRequired = !column.IsNullable,
                    ChildrenLoaded = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to read the columns of {Node}", node.Name);
        }
    }

    /// <summary>
    /// Everything that matches the filter, across every open connection, with the path that leads to
    /// it. A filter is not the palette (WS-17): it narrows the tree and stays until it is cleared,
    /// while the palette is one jump and closes.
    /// </summary>
    private void ApplyFilter()
    {
        FilterMatches.Clear();

        var text = (Filter ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            FilterSummary = null;
            return;
        }

        var connections = 0;

        foreach (var root in Nodes)
        {
            var before = FilterMatches.Count;

            foreach (var folder in root.Children)
            {
                foreach (var node in folder.Children)
                {
                    if (node.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                    {
                        FilterMatches.Add(new FilterMatch(node, $"{root.Name} / {folder.Name}"));
                        continue;
                    }

                    // Columns count too, and they are what the filter is most often used for: the
                    // name of a column is often all anyone remembers of a schema.
                    foreach (var child in node.Children)
                    {
                        if (child.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                            FilterMatches.Add(new FilterMatch(child, $"{root.Name} / {folder.Name} / {node.Name}"));
                    }
                }
            }

            if (FilterMatches.Count > before)
                connections++;
        }

        FilterSummary = FilterMatches.Count == 0
            ? "No matches"
            : $"{FilterMatches.Count} matches in {connections} connection{(connections == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Puts the rebuilt branch where the old one was, so that refreshing the second of three
    /// connections does not move it to the end of the tree.
    /// </summary>
    private void ReplaceRoot(IDatabaseSession session, DatabaseNode rootNode)
    {
        var existing = Nodes.FirstOrDefault(node => node.ConnectionId == session.Id);

        if (existing != null)
        {
            Nodes[Nodes.IndexOf(existing)] = rootNode;
            return;
        }

        // Kept in the order the connections were opened, which is the order the manager holds them in.
        var position = Connections.Sessions.IndexOf(session);
        Nodes.Insert(position < 0 || position > Nodes.Count ? Nodes.Count : position, rootNode);
    }

    private static DatabaseNode BuildFolder(IDatabaseSession session, HashSet<string> expanded, string name,
        DatabaseNodeType folderType, DatabaseNodeType childType, IEnumerable<string> children,
        bool expandedByDefault = false)
    {
        var folder = new DatabaseNode
        {
            Name = name,
            NodeType = folderType,
            ConnectionId = session.Id,
            ChildrenLoaded = true,
            IsExpanded = expandedByDefault || IsExpanded(expanded, session, folderType, name)
        };

        foreach (var child in children)
        {
            folder.Children.Add(new DatabaseNode
            {
                Name = child,
                NodeType = childType,
                ConnectionId = session.Id,

                // A table's columns are read when it is first expanded; everything else has none.
                ChildrenLoaded = childType != DatabaseNodeType.Table && childType != DatabaseNodeType.View
            });
        }

        // The number of objects, which INFORMATION_SCHEMA answers for nothing. An empty folder keeps
        // its place with a zero rather than disappearing - a node that vanishes breaks the muscle
        // memory of everyone who knew where it was (2.1).
        folder.Detail = folder.Children.Count.ToString();

        return folder;
    }

    /// <summary>
    /// Saves the expanded state of all nodes before refresh. Keyed by connection as well as by node:
    /// two connections to the same database would otherwise share one answer.
    /// </summary>
    private HashSet<string> SaveExpandedState()
    {
        var expanded = new HashSet<string>();
        SaveExpandedStateRecursive(Nodes, expanded);
        return expanded;
    }

    private static void SaveExpandedStateRecursive(IEnumerable<DatabaseNode> nodes, HashSet<string> expanded)
    {
        foreach (var node in nodes)
        {
            if (node.IsExpanded)
                expanded.Add(ExpandedKey(node.ConnectionId, node.NodeType, node.Name));

            SaveExpandedStateRecursive(node.Children, expanded);
        }
    }

    private static bool IsExpanded(HashSet<string> expanded, IDatabaseSession session,
        DatabaseNodeType nodeType, string name)
    {
        return expanded.Contains(ExpandedKey(session.Id, nodeType, name));
    }

    private static string ExpandedKey(Guid connectionId, DatabaseNodeType nodeType, string name)
    {
        return $"{connectionId}:{nodeType}:{name}";
    }

    #endregion

    #region Tools

    private void UpdateCommandStates()
    {
        var nodeType = SelectedNode?.NodeType;
        var connected = SelectedSession?.IsConnected == true;

        CanBrowseData = connected && nodeType is DatabaseNodeType.Table or DatabaseNodeType.View;
        CanEditData = connected && nodeType == DatabaseNodeType.Table;
        CanViewStructure = connected && nodeType is DatabaseNodeType.Table
                                              or DatabaseNodeType.View
                                              or DatabaseNodeType.Index;
        CanViewDefinition = connected && nodeType is DatabaseNodeType.Table
                                               or DatabaseNodeType.View
                                               or DatabaseNodeType.Trigger
                                               or DatabaseNodeType.Index;
        CanDropObject = connected && nodeType is DatabaseNodeType.Table
                                           or DatabaseNodeType.View
                                           or DatabaseNodeType.Index
                                           or DatabaseNodeType.Trigger
                                           or DatabaseNodeType.Sequence;

        // Only a table. ALTER VIEW, ALTER INDEX and ALTER TRIGGER do not exist in this language, so
        // F2 on any of those would be a button that cannot work.
        CanRename = connected && nodeType == DatabaseNodeType.Table;
        CanTruncate = connected && nodeType == DatabaseNodeType.Table;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Filter))
        {
            ApplyFilter();
            return;
        }

        if (e.PropertyName != nameof(SelectedNode))
            return;

        // Selecting in the tree moves the focus - the connection new tabs and object dialogs belong
        // to. It does NOT move the target of a tab that is already open (WS-3).
        var session = SelectedSession;

        if (session != null)
            Connections.Active = session;

        UpdateCommandStates();
    }

    #endregion

    #region Properties

    /// <summary>
    /// The roots of the tree: one per open connection.
    /// </summary>
    public ObservableCollection<DatabaseNode> Nodes { get; private set; } = null!;

    [Notify]
    public DatabaseNode? SelectedNode { get; set; }

    /// <summary>
    /// Narrows the tree to what matches, across every open connection, and stays until it is cleared
    /// (WS-17).
    /// </summary>
    [Notify]
    public string Filter { get; set; } = string.Empty;

    /// <summary>
    /// What the filter found, with the path to each match.
    /// </summary>
    public ObservableCollection<FilterMatch> FilterMatches { get; } = [];

    /// <summary>
    /// "5 matches in 2 connections", or nothing when the filter is empty.
    /// </summary>
    [Notify]
    public string? FilterSummary { get; private set; }

    public bool IsFiltering => !string.IsNullOrWhiteSpace(Filter);

    /// <summary>
    /// How long a row count is allowed to take before it is given up on (WS-16). Two seconds is the
    /// design's number and it is a property so that a test does not have to wait for it.
    /// </summary>
    public TimeSpan CountTimeout { get; set; } = TimeSpan.FromSeconds(2);

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool CanBrowseData { get; private set; }

    [Notify]
    public bool CanEditData { get; private set; }

    [Notify]
    public bool CanViewStructure { get; private set; }

    [Notify]
    public bool CanViewDefinition { get; private set; }

    [Notify]
    public bool CanDropObject { get; private set; }

    /// <summary>
    /// F2. True for a table only - nothing else in this language can be renamed.
    /// </summary>
    [Notify]
    public bool CanRename { get; private set; }

    [Notify]
    public bool CanTruncate { get; private set; }

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand SelectTop100Command { get; private set; } = null!;

    public ICommand SelectTop1000Command { get; private set; } = null!;

    public ICommand EditDataCommand { get; private set; } = null!;

    public ICommand ViewStructureCommand { get; private set; } = null!;

    public ICommand ViewDefinitionCommand { get; private set; } = null!;

    public ICommand DropObjectCommand { get; private set; } = null!;

    /// <summary>
    /// Renames straight away, taking the new name as its parameter.
    /// </summary>
    public ICommand RenameObjectCommand { get; private set; } = null!;

    /// <summary>
    /// F2: opens the box on the selected row.
    /// </summary>
    public ICommand BeginRenameCommand { get; private set; } = null!;

    public ICommand CommitRenameCommand { get; private set; } = null!;

    public ICommand CancelRenameCommand { get; private set; } = null!;

    public ICommand TruncateTableCommand { get; private set; } = null!;

    public ICommand CreateTableCommand { get; private set; } = null!;

    public ICommand CreateViewCommand { get; private set; } = null!;

    public ICommand CreateIndexCommand { get; private set; } = null!;

    /// <summary>
    /// Loads a node's children the first time it is opened.
    /// </summary>
    public ICommand ExpandNodeCommand { get; private set; } = null!;

    public ICommand ClearFilterCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IConnectionManager Connections => ApplicationVm.Connections;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
