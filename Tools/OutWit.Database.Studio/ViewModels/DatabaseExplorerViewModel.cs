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
        CreateTableCommand = new RelayCommandAsync(CreateTableAsync);
        CreateViewCommand = new RelayCommandAsync(CreateViewAsync);
        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync);
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

            ReplaceRoot(session, rootNode);

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
            IsExpanded = expandedByDefault || IsExpanded(expanded, session, folderType, name)
        };

        foreach (var child in children)
        {
            folder.Children.Add(new DatabaseNode
            {
                Name = child,
                NodeType = childType,
                ConnectionId = session.Id
            });
        }

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
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
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

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand SelectTop100Command { get; private set; } = null!;

    public ICommand SelectTop1000Command { get; private set; } = null!;

    public ICommand EditDataCommand { get; private set; } = null!;

    public ICommand ViewStructureCommand { get; private set; } = null!;

    public ICommand ViewDefinitionCommand { get; private set; } = null!;

    public ICommand DropObjectCommand { get; private set; } = null!;

    public ICommand CreateTableCommand { get; private set; } = null!;

    public ICommand CreateViewCommand { get; private set; } = null!;

    public ICommand CreateIndexCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IConnectionManager Connections => ApplicationVm.Connections;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
