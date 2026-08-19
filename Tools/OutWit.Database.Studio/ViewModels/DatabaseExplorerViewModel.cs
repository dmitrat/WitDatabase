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
        OpenDatabaseTabCommand = new RelayCommandAsync(OpenDatabaseTabAsync);
        EditDataCommand = new RelayCommandAsync(EditDataAsync);
        ViewStructureCommand = new RelayCommandAsync(ViewStructureAsync);
        ViewDefinitionCommand = new RelayCommandAsync(ViewDefinitionAsync);
        DropObjectCommand = new RelayCommandAsync(DropObjectAsync);
        BeginRenameCommand = new RelayCommand(BeginRename);
        CommitRenameCommand = new RelayCommandAsync(CommitRenameAsync);
        CancelRenameCommand = new RelayCommand(CancelRename);
        TruncateTableCommand = new RelayCommandAsync(TruncateTableAsync);
        CreateTableCommand = new RelayCommandAsync(CreateTableAsync);
        CreateViewCommand = new RelayCommandAsync(CreateViewAsync);
        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync);
        CreateTriggerCommand = new RelayCommandAsync(CreateTriggerAsync);
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

    /// <summary>
    /// The middle click (section 2.7): the data of the selected object, in a tab that does NOT come
    /// to the front.
    ///
    /// <para>
    /// It is the same query the menu's first «Данные» item runs. The limit is 100 rather than the
    /// setting, because <c>DefaultRowLimit</c> is still unwired - phase 15 named it as a dead
    /// setting and <c>WS-23</c> wants it on a selector ON THE TAB rather than baked into the menu.
    /// Reading it here would half-build that, which is the shape phase 15 exists to remove.
    /// </para>
    /// </summary>
    public void BrowseDataInBackground()
    {
        SelectTopRows(100, activate: false);
    }

    /// <summary>
    /// Typing letters moves the selection to the first VISIBLE node whose name starts with what has
    /// been typed (section 2.7). Returns whether anything matched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Visible means what is on the screen, not what is in the tree.</b> A jump into a collapsed
    /// branch would select a node nobody can see - the selection would move, the inspector would
    /// change, and the tree would look untouched.
    /// </para>
    /// <para>
    /// <b>Where the search starts is the whole behaviour.</b> A single letter starts AFTER the
    /// selection, so pressing it again walks the objects beginning with that letter instead of sitting
    /// on the first one forever. A longer prefix starts AT the selection, so growing "a" into "ab"
    /// leaves a selection that still matches where it is rather than hunting for another one.
    /// </para>
    /// </remarks>
    public bool JumpTo(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return false;

        var visible = VisibleNodes().ToList();

        if (visible.Count == 0)
            return false;

        var selected = SelectedNode == null ? -1 : visible.IndexOf(SelectedNode);
        var start = selected < 0 ? 0 : selected + (prefix.Length == 1 ? 1 : 0);

        for (var step = 0; step < visible.Count; step++)
        {
            var node = visible[(start + step) % visible.Count];

            if (!node.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                continue;

            SelectedNode = node;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The nodes in the order the tree draws them: a node, then its children if it is open.
    /// </summary>
    public IEnumerable<DatabaseNode> VisibleNodes()
    {
        return Nodes == null ? [] : Walk(Nodes);

        static IEnumerable<DatabaseNode> Walk(IEnumerable<DatabaseNode> nodes)
        {
            foreach (var node in nodes)
            {
                // The stand-in that makes a table draw an expander is not a row anybody arrows
                // onto: it exists for the moment between opening the node and its columns
                // arriving. Keyboard navigation would otherwise stop on an empty line.
                if (node.IsPlaceholder)
                    continue;

                yield return node;

                if (!node.IsExpanded)
                    continue;

                foreach (var child in Walk(node.Children))
                    yield return child;
            }
        }
    }

    private void SelectTopRows(int limit, bool activate = true)
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanBrowseData)
            return;

        var tableName = SelectedNode.Name;
        var sql = $"SELECT * FROM [{tableName}] LIMIT {limit}";

        // The tab is opened in the connection the node came from and stays there, so executing it
        // cannot land in another database however the selection moves afterwards.
        var tab = ApplicationVm.WorkspaceTabsVm.OpenQueryTab(sql, $"{tableName} - Top {limit}", session, activate);

        // A tab that was not brought to the front is not the SELECTED tab, and the shell's execute
        // command runs the selected one - so a background tab has to be told to run itself. Without
        // this the middle click opens a tab holding SQL and no rows, which is not "open the data".
        if (activate)
            ApplicationVm.WorkspaceTabsVm.ExecuteQueryCommand.Execute(null);
        else
            tab.ExecuteQueryCommand.Execute(null);

        Logger.LogInformation("Select top {Limit} from {ObjectName} in {Connection}",
            limit, tab.Title, session.DisplayName);
    }

    private async Task EditDataAsync()
    {
        var session = SelectedSession;

        if (SelectedNode == null || session == null || !CanEditData)
            return;

        var tableName = SelectedNode.Name;

        var tab = await ApplicationVm.WorkspaceTabsVm.OpenTableEditTabAsync(session, tableName);

        // WS-11: the places have to agree. A table with no primary key opens with every editing
        // control disabled - correctly - and the status bar announced "Editing" beside them anyway.
        // The flag is the tab's own IsReadOnly, which is what CanAddRow, CanDeleteRow and CanCommit
        // are computed from; the connection's read-only mode is a DIFFERENT thing that WorkspaceTab
        // holds under the same name.
        ApplicationVm.MainWindowVm.StatusText = Localization.Format(
            tab.IsReadOnly ? "Explorer.Viewing" : "Explorer.Editing", tableName);

        Logger.LogInformation("Edit data for table {TableName} in {Connection}", tableName, session.DisplayName);
    }

    /// <summary>
    /// Opens the storage tab of the selected connection (WS-54).
    /// </summary>
    private async Task OpenDatabaseTabAsync()
    {
        var session = SelectedSession;

        if (session == null || !CanOpenDatabaseTab)
            return;

        await ApplicationVm.WorkspaceTabsVm.OpenDatabaseTabAsync(session);

        Logger.LogInformation("Opened the database tab of {Connection}", session.DisplayName);
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
            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Explorer.DefinitionFailed", ex.Message);
            return;
        }

        if (string.IsNullOrEmpty(definition))
        {
            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Explorer.NoDefinition", SelectedNode.Name);
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

        // Captured BEFORE the selection is cleared below, and it is what names the object in the
        // user's language. Until 2026-08-10 every one of these sentences was built from the SQL
        // keyword lower-cased - so a Russian interface said "Удалить table «Customers»?" and
        // "Удалено (table): Logs". A term the engine owns is not a noun in someone's sentence, and
        // the localisation lint cannot see it because there is no literal: the word arrives at run
        // time. Found by running the application, which is the only place it was ever visible.
        var nodeType = SelectedNode.NodeType;

        // WS-20. Until 2026-08-10 this method went straight to ExecuteNonQueryAsync: one click in the
        // tree and the table was gone, while the settings page showed a ticked "ask before dropping an
        // object". The question is asked through the confirmation service so that the SETTING is
        // consulted in one place rather than here - a caller that decides for itself is a caller that
        // can forget, which is how all four of these questions came to be missing at once.
        var consequences = await DropConsequencesAsync(session, nodeType, objectName);

        var proceed = await ApplicationVm.Confirmations.AskAboutDestructiveActionAsync(
            new DestructiveAction(
                ConfirmationKind.DroppingObject,
                Localization.Format(SentenceKey("Confirm.Drop.Headline", nodeType), objectName),
                sql,
                consequences));

        if (!proceed)
        {
            Logger.LogInformation("Drop of {ObjectType} {ObjectName} was refused by the user",
                objectType, objectName);
            return;
        }

        try
        {
            await session.ExecuteNonQueryAsync(sql);

            // Clear selection before refresh to avoid stale reference
            SelectedNode = null;

            await RefreshAsync(session);

            ApplicationVm.MainWindowVm.StatusText = Localization.Format(SentenceKey("Explorer.Dropped", nodeType), objectName);
            Logger.LogInformation("Dropped {ObjectType}: {ObjectName} in {Connection}",
                objectType, objectName, session.DisplayName);
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format(SentenceKey("Explorer.DropFailed", nodeType), ex.Message);
            Logger.LogError(ex, "Failed to drop {ObjectType}: {ObjectName}", objectType, objectName);
        }
    }

    /// <summary>
    /// The catalogue key naming this kind of object, per sentence family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written as a switch over WHOLE keys rather than as <c>$"{family}.{nodeType}"</c>, for two
    /// reasons that both came from running into them.
    /// </para>
    /// <para>
    /// First, the localisation lint reads an interpolated string assigned to <c>StatusText</c> as a
    /// sentence built in code - which is exactly what it exists to catch, and it cannot tell a
    /// composed KEY from a composed SENTENCE. It flagged the interpolated version and it was right to.
    /// </para>
    /// <para>
    /// Second, a key that exists only at run time is invisible to a grep and to any check that every
    /// key is present in both languages. Spelled out, each one is findable from the catalogue and back.
    /// </para>
    /// <para>
    /// Russian is why these are whole sentences per type rather than a noun slotted into one template:
    /// "удалить ТАБЛИЦУ" and "ТАБЛИЦА удалена" need different cases, and a template with a noun hole
    /// gets one of them wrong.
    /// </para>
    /// </remarks>
    private static string SentenceKey(string family, DatabaseNodeType nodeType) => (family, nodeType) switch
    {
        ("Confirm.Drop.Headline", DatabaseNodeType.Table) => "Confirm.Drop.Headline.Table",
        ("Confirm.Drop.Headline", DatabaseNodeType.View) => "Confirm.Drop.Headline.View",
        ("Confirm.Drop.Headline", DatabaseNodeType.Index) => "Confirm.Drop.Headline.Index",
        ("Confirm.Drop.Headline", DatabaseNodeType.Trigger) => "Confirm.Drop.Headline.Trigger",
        ("Confirm.Drop.Headline", DatabaseNodeType.Sequence) => "Confirm.Drop.Headline.Sequence",

        ("Explorer.Dropped", DatabaseNodeType.Table) => "Explorer.Dropped.Table",
        ("Explorer.Dropped", DatabaseNodeType.View) => "Explorer.Dropped.View",
        ("Explorer.Dropped", DatabaseNodeType.Index) => "Explorer.Dropped.Index",
        ("Explorer.Dropped", DatabaseNodeType.Trigger) => "Explorer.Dropped.Trigger",
        ("Explorer.Dropped", DatabaseNodeType.Sequence) => "Explorer.Dropped.Sequence",

        ("Explorer.DropFailed", DatabaseNodeType.Table) => "Explorer.DropFailed.Table",
        ("Explorer.DropFailed", DatabaseNodeType.View) => "Explorer.DropFailed.View",
        ("Explorer.DropFailed", DatabaseNodeType.Index) => "Explorer.DropFailed.Index",
        ("Explorer.DropFailed", DatabaseNodeType.Trigger) => "Explorer.DropFailed.Trigger",
        ("Explorer.DropFailed", DatabaseNodeType.Sequence) => "Explorer.DropFailed.Sequence",

        _ => family + ".Table"
    };

    /// <summary>
    /// What breaks if this object goes, in the user's language (WS-20).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk is the one the rebuild dialog already uses - referencing foreign keys and views that
    /// mention the table - rather than a second one written for this dialog. Indexes and triggers are
    /// listed too, because they go with the table and a person deciding about a DROP wants to know
    /// they will have to be made again.
    /// </para>
    /// <para>
    /// <b>A failure to work out the consequences is reported, never swallowed.</b> An empty list means
    /// "nothing else refers to this", and the dialog says so out loud; if the walk itself failed, an
    /// empty list would be a lie in the one direction that matters. So the failure becomes an entry.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> DropConsequencesAsync(
        IDatabaseSession session, DatabaseNodeType nodeType, string objectName)
    {
        if (nodeType != DatabaseNodeType.Table && nodeType != DatabaseNodeType.View)
            return [];

        try
        {
            var consequences = new List<string>();

            var referencing = await session.GetReferencingKeysAsync(objectName);
            foreach (var key in referencing)
            {
                consequences.Add(Localization.Format(
                    "Confirm.Drop.ReferencedBy", key.FromTable, key.FromColumn));
            }

            var views = await session.GetViewsMentioningAsync(objectName);
            foreach (var view in views.Where(v => !v.Equals(objectName, StringComparison.OrdinalIgnoreCase)))
                consequences.Add(Localization.Format("Confirm.Drop.ReadByView", view));

            if (nodeType == DatabaseNodeType.Table)
            {
                var indexes = await session.GetTableIndexesAsync(objectName);
                if (indexes.Count > 0)
                    consequences.Add(Localization.Format("Confirm.Drop.Indexes", indexes.Count));

                var triggers = await session.GetTableTriggersAsync(objectName);
                if (triggers.Count > 0)
                    consequences.Add(Localization.Format("Confirm.Drop.Triggers", triggers.Count));

                var rows = await session.TryCountRowsAsync(objectName, TimeSpan.FromSeconds(2));
                if (rows is > 0)
                    consequences.Add(Localization.Format("Confirm.Drop.Rows", rows.Value));
            }

            return consequences;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not work out what depends on {ObjectName}", objectName);

            return [Localization.Format("Confirm.Drop.ConsequencesUnknown", ex.Message)];
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

            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Explorer.Renamed", oldName, newName);
            Logger.LogInformation("Renamed table {Old} to {New}", oldName, newName);
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Explorer.RenameFailed", oldName, ex.Message.Split('\n')[0]);
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

            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Explorer.Emptied", table);
            ApplicationVm.Notifications.Warning(Localization.Format("Explorer.Emptied", table),
                Localization["Explorer.TruncateWarning"]);

            Logger.LogInformation("Truncated {Table}", table);
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Explorer.EmptyFailed", table, ex.Message.Split('\n')[0]);
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

    /// <summary>
    /// Creates a trigger on the selected table, through the dialog the Structure tab already uses.
    /// </summary>
    /// <remarks>
    /// Nothing here is new but the way in. <c>EditTriggerViewModel</c> knows what this language
    /// allows in a body - DML only, WHEN with the brackets the grammar wants, and no
    /// <c>SET NEW.column</c> - and it was reachable from one button in one tab.
    /// </remarks>
    private async Task CreateTriggerAsync()
    {
        var session = SelectedSession;
        var table = SelectedNode?.Name;

        if (session?.IsConnected != true || string.IsNullOrWhiteSpace(table))
            return;

        var vm = new EditTriggerViewModel(ApplicationVm, session, table, null);

        if (!await ApplicationVm.Dialogs.ShowEditTriggerAsync(vm))
            return;

        await session.Catalog.RefreshAsync();
        await RefreshAsync(session);

        Logger.LogInformation("Trigger created on {Table}", table);
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

                // The colour the Open dialog promised. The tabs and the Connections window both
                // carried it while the roots in the tree looked identical to each other.
                ColorIndex = session.ColorIndex,
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

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Explorer.Folder.Tables",
                DatabaseNodeType.TablesFolder, DatabaseNodeType.Table,
                tables.Select(table => table.Name), expandedByDefault: firstLoad));

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Explorer.Folder.Views",
                DatabaseNodeType.ViewsFolder, DatabaseNodeType.View, views));

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Explorer.Folder.Indexes",
                DatabaseNodeType.IndexesFolder, DatabaseNodeType.Index, indexes));

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Explorer.Folder.Triggers",
                DatabaseNodeType.TriggersFolder, DatabaseNodeType.Trigger, triggers));

            rootNode.Children.Add(BuildFolder(session, expandedNodes, "Explorer.Folder.Sequences",
                DatabaseNodeType.SequencesFolder, DatabaseNodeType.Sequence, sequences));

            // The sixth folder (WS-21). The engine has had functions and procedures since phase 9d
            // and the tree has never shown them - which reads, to a user, as the database not having
            // any.
            var routines = await session.GetRoutinesAsync();

            var routinesFolder = BuildFolder(session, expandedNodes, "Explorer.Folder.Routines",
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
            //
            // And they are not taken at all when the user has said not to (WS-16). A count is the one
            // thing the tree does that touches every table, so on a large database it is exactly the
            // work someone would want to switch off - which is why the setting exists and why it did
            // nothing until 2026-08-10.
            if (ApplicationVm.Settings.Current.CountRowsAutomatically)
            {
                _ = CountRowsAsync(session).ContinueWith(
                    task => Logger.LogError(task.Exception, "Counting the tables of {Connection} failed",
                        session.DisplayName),
                    TaskContinuationOptions.OnlyOnFaulted);
            }

            // Through Plural, one family per noun. The nouns used to be INSIDE the format string with
            // raw numbers passed in, which is why this line printed "1 views, 1 triggers" while the
            // Database tab - the same counts, the same second, through the same mechanism - printed
            // "1 table" correctly. English has one plural rule and this string was the one place that
            // did not ask for it.
            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Explorer.Summary",
                session.DisplayName,
                Localization.Plural("Count.Tables", tables.Count),
                Localization.Plural("Count.Views", views.Count),
                Localization.Plural("Count.Indexes", indexes.Count),
                Localization.Plural("Count.Triggers", triggers.Count),
                Localization.Plural("Count.Sequences", sequences.Count));

            Logger.LogInformation(
                "Explorer refreshed {Connection}: {Tables} tables, {Views} views, {Indexes} indexes, {Triggers} triggers, {Sequences} sequences",
                session.DisplayName, tables.Count, views.Count, indexes.Count, triggers.Count, sequences.Count);
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Explorer.SchemaFailed", ex.Message);
            ApplicationVm.MainWindowVm.StatusText = Localization["Explorer.SchemaFailedShort"];
            Logger.LogError(ex, "Failed to refresh the branch of {Connection}", session.DisplayName);

            // Reloading the tree is something Studio does on its own, after a DDL statement or a
            // drop. A failure in it has no dialog to belong to, and the status bar is overwritten by
            // whatever happens next (WS-7).
            ApplicationVm.Notifications.Error(Localization["Explorer.SchemaReloadFailed"], ex.Message,
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
    /// Counts again the tables a script actually wrote to, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree kept the count it had while rows were being inserted through the editor - fifty rows
    /// went in and the node still said 39 until the database was reopened. That was recorded as
    /// probably the deliberate laziness of <c>WS-16</c> and it is not: a script that changes the
    /// SCHEMA already reloads the branch, and one that only writes ROWS asked for nothing at all,
    /// because a count is only ever taken by a full reload.
    /// </para>
    /// <para>
    /// By table rather than by connection, because a count is a query: an INSERT into one table of
    /// forty must not cost forty <c>COUNT(*)</c>s, and the parser says which table each statement
    /// wrote to. A name that is not in the tree - a table created and filled in the same script, where
    /// the reload has already been asked for - is simply not found here and costs nothing.
    /// </para>
    /// </remarks>
    public async Task CountRowsAsync(IDatabaseSession session, IEnumerable<string> tables,
        CancellationToken ct = default)
    {
        var root = Nodes.FirstOrDefault(node => node.ConnectionId == session.Id);

        var folder = root?.Children.FirstOrDefault(child => child.NodeType == DatabaseNodeType.TablesFolder);

        if (folder == null)
            return;

        var wanted = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);

        foreach (var table in folder.Children.Where(child => wanted.Contains(child.Name)).ToList())
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

            // The stand-in goes as the real thing arrives.
            node.Children.Clear();
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

        IsFiltering = text.Length > 0;

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
                    // name of a column is often all anyone remembers of a schema. The stand-in
                    // child that makes the expander appear is not one of them.
                    foreach (var child in node.Children)
                    {
                        if (child.IsPlaceholder)
                            continue;

                        if (child.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                            FilterMatches.Add(new FilterMatch(child, $"{root.Name} / {folder.Name} / {node.Name}"));
                    }
                }
            }

            if (FilterMatches.Count > before)
                connections++;
        }

        FilterSummary = FilterMatches.Count == 0
            ? Localization["Explorer.Filter.NoMatches"]
            // The second half is read as «в {N} подключении», and the plural table held nominative
            // forms only - so a correct count came out in the wrong case: «11 совпадений в 1
            // подключение». A count is not a noun on its own; the phrase it lands in decides the
            // form, and Russian has six of them.
            : Localization.Format("Explorer.Filter.Matches",
                Localization.Plural("Count.Matches", FilterMatches.Count),
                Localization.Plural("Count.ConnectionsIn", connections));
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

    /// <summary>
    /// A folder of the tree.
    ///
    /// <para>
    /// <paramref name="key"/> is a catalogue key rather than the caption, and the folder is remembered
    /// as expanded BY IT: the name on screen changes with the language, and a memory keyed by what is
    /// drawn would forget every open folder the moment somebody switched. Found by switching the
    /// running application to Russian, where the six folders were the only English left in the tree.
    /// </para>
    /// </summary>
    private DatabaseNode BuildFolder(IDatabaseSession session, HashSet<string> expanded, string key,
        DatabaseNodeType folderType, DatabaseNodeType childType, IEnumerable<string> children,
        bool expandedByDefault = false)
    {
        var name = Localization[key];

        var folder = new DatabaseNode
        {
            Name = name,
            NodeType = folderType,
            ConnectionId = session.Id,
            ChildrenLoaded = true,
            IsExpanded = expandedByDefault || IsExpanded(expanded, session, folderType, key)
        };

        foreach (var child in children)
        {
            // A table's columns are read when it is first expanded; everything else has none.
            var opensIntoColumns = childType is DatabaseNodeType.Table or DatabaseNodeType.View;

            var node = new DatabaseNode
            {
                Name = child,
                NodeType = childType,
                ConnectionId = session.Id,
                ChildrenLoaded = !opensIntoColumns
            };

            // The expander is drawn from the children, and the children are read when the
            // expander is used. One stand-in child breaks that circle; it is replaced by the
            // real columns the first time the node is opened.
            if (opensIntoColumns)
            {
                node.Children.Add(new DatabaseNode
                {
                    Name = string.Empty,
                    NodeType = DatabaseNodeType.Column,
                    ConnectionId = session.Id,
                    ParentName = child,
                    ChildrenLoaded = true,
                    IsPlaceholder = true
                });
            }

            folder.Children.Add(node);
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

        // What this KIND of node has, which is not the same question as what can be done to it
        // now. An item that does not apply is absent; an item that applies but has no connection
        // to run through is greyed. The tree used to answer only the second question, so a folder
        // offered «Empty the table…» and «Drop…» greyed - which reads as though a folder could be
        // emptied or dropped.
        ShowsDatabaseActions = nodeType == DatabaseNodeType.Database;

        // A TABLE is here because a trigger is created on one: without it the Create submenu was
        // hidden exactly where the only item it could offer applies. Measured in the running
        // application on 2026-08-19 - the two halves of phase 3 contradicted each other.
        ShowsCreate = nodeType is DatabaseNodeType.Database
                                or DatabaseNodeType.Table
                                or DatabaseNodeType.TablesFolder
                                or DatabaseNodeType.ViewsFolder
                                or DatabaseNodeType.IndexesFolder
                                or DatabaseNodeType.TriggersFolder
                                or DatabaseNodeType.SequencesFolder
                                or DatabaseNodeType.RoutinesFolder;

        ShowsBrowseData = nodeType is DatabaseNodeType.Table or DatabaseNodeType.View;
        ShowsEditData = nodeType == DatabaseNodeType.Table;
        ShowsRename = nodeType == DatabaseNodeType.Table;
        ShowsTruncate = nodeType == DatabaseNodeType.Table;

        ShowsViewStructure = nodeType is DatabaseNodeType.Table
                                       or DatabaseNodeType.View
                                       or DatabaseNodeType.Index;

        ShowsViewDefinition = nodeType is DatabaseNodeType.Table
                                        or DatabaseNodeType.View
                                        or DatabaseNodeType.Trigger
                                        or DatabaseNodeType.Index;

        ShowsDrop = nodeType is DatabaseNodeType.Table
                              or DatabaseNodeType.View
                              or DatabaseNodeType.Index
                              or DatabaseNodeType.Trigger
                              or DatabaseNodeType.Sequence;

        // A separator is a rule BETWEEN two groups, so it is drawn only when both sides of it have
        // something. The items above each learned to hide themselves and the five separators did
        // not, so a connection's menu drew two rules with nothing between them and two more below
        // its last item, and a folder's menu began with one. Reported from a screenshot, 2026-08-19.
        //
        // Refresh applies to every node, so anything ABOVE it always has something below it - which
        // is why the first three rules ask only about what precedes them.
        var objectActions = ShowsBrowseData || ShowsEditData || ShowsViewStructure || ShowsViewDefinition;

        ShowsRuleBeforeCreate = ShowsDatabaseActions;
        ShowsRuleBeforeObjectActions = objectActions && (ShowsDatabaseActions || ShowsCreate);
        ShowsRuleBeforeRefresh = ShowsDatabaseActions || ShowsCreate || objectActions;
        ShowsRuleBeforeRename = ShowsRename || ShowsTruncate;
        ShowsRuleBeforeDrop = ShowsDrop;

        var connected = SelectedSession?.IsConnected == true;

        // The «База» tab belongs to the CONNECTION, so it is offered on the connection's own node and
        // nowhere else (WS-54).
        CanOpenDatabaseTab = connected && nodeType == DatabaseNodeType.Database;

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
        // A trigger names the table it fires on, so it is offered on a TABLE and nowhere else -
        // the Triggers folder is per connection here, not per table, so it has no table to name.
        // The dialog has existed since stage 8 and was reachable from the Structure tab alone,
        // which is why the screenshot pass reported that Studio cannot create a trigger at all.
        CanCreateTrigger = connected && nodeType == DatabaseNodeType.Table;

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

    /// <summary>
    /// Whether the filter is narrowing anything. Three things in the panel are bound to it: the tree
    /// hides, the list of matches appears, and the Esc button that clears it.
    /// </summary>
    /// <remarks>
    /// <b>It used to be a computed property, and WS-17 did not work because of it.</b>
    /// <c>=> !string.IsNullOrWhiteSpace(Filter)</c> is correct every time it is READ and notifies
    /// nobody, so a binding asked once at load time and never again: typing in the box filled
    /// <see cref="FilterMatches"/> with the right answer and left the panel showing the whole tree,
    /// with no Esc button to say anything had happened. Measured in the running application on
    /// 2026-08-11 - «aspnet» typed into the box, fifteen tables still listed.
    /// <para>
    /// Set from <c>ApplyFilter</c> now, which is the one place that knows.
    /// </para>
    /// </remarks>
    [Notify]
    public bool IsFiltering { get; private set; }

    /// <summary>
    /// How long a row count is allowed to take before it is given up on (WS-16). Two seconds is the
    /// design's number and it is a property so that a test does not have to wait for it.
    /// </summary>
    public TimeSpan CountTimeout { get; set; } = TimeSpan.FromSeconds(2);

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// True on a connection's own node, which is the only place the storage tab makes sense.
    /// </summary>
    [Notify]
    public bool CanOpenDatabaseTab { get; private set; }

    [Notify]
    public bool CanBrowseData { get; private set; }

    [Notify]
    public bool CanEditData { get; private set; }

    /// <summary>
    /// Whether the selected node is a CONNECTION - the Database tab and closing it belong here.
    /// </summary>
    [Notify]
    public bool ShowsDatabaseActions { get; private set; }

    /// <summary>Whether objects can be created under the selected node.</summary>
    [Notify]
    public bool ShowsCreate { get; private set; }

    /// <summary>Whether the selected node holds rows to look at.</summary>
    [Notify]
    public bool ShowsBrowseData { get; private set; }

    /// <summary>Whether the selected node holds rows that can be edited.</summary>
    [Notify]
    public bool ShowsEditData { get; private set; }

    /// <summary>Whether the selected node has a structure to open.</summary>
    [Notify]
    public bool ShowsViewStructure { get; private set; }

    /// <summary>Whether the catalogue can render the selected node as SQL.</summary>
    [Notify]
    public bool ShowsViewDefinition { get; private set; }

    /// <summary>Whether the selected node can be renamed at all - only a table can.</summary>
    [Notify]
    public bool ShowsRename { get; private set; }

    /// <summary>Whether the selected node can be emptied at all.</summary>
    [Notify]
    public bool ShowsTruncate { get; private set; }

    /// <summary>Whether the selected node is an object that can be dropped.</summary>
    [Notify]
    public bool ShowsDrop { get; private set; }

    /// <summary>The rule between the connection's own actions and everything below them.</summary>
    [Notify]
    public bool ShowsRuleBeforeCreate { get; private set; }

    /// <summary>The rule between creating something and looking at what is already there.</summary>
    [Notify]
    public bool ShowsRuleBeforeObjectActions { get; private set; }

    /// <summary>The rule above <c>Refresh</c>, which every node has and nothing else may follow.</summary>
    [Notify]
    public bool ShowsRuleBeforeRefresh { get; private set; }

    /// <summary>The rule above renaming and emptying, which belong to a table alone.</summary>
    [Notify]
    public bool ShowsRuleBeforeRename { get; private set; }

    /// <summary>The rule above dropping, which is kept apart from everything that is not destructive.</summary>
    [Notify]
    public bool ShowsRuleBeforeDrop { get; private set; }

    /// <summary>Whether the selected node has a table for a trigger to belong to.</summary>
    [Notify]
    public bool CanCreateTrigger { get; private set; }

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

    public ICommand OpenDatabaseTabCommand { get; private set; } = null!;

    [Notify]
    public ICommand EditDataCommand { get; private set; } = null!;

    public ICommand ViewStructureCommand { get; private set; } = null!;

    public ICommand ViewDefinitionCommand { get; private set; } = null!;

    public ICommand DropObjectCommand { get; private set; } = null!;

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

    public ICommand CreateTriggerCommand { get; private set; } = null!;

    public ICommand ClearFilterCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IConnectionManager Connections => ApplicationVm.Connections;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion

    #region Localization

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
