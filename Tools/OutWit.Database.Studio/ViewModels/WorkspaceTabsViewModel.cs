using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for managing unified workspace tabs (Query, Edit, Structure).
/// </summary>
public class WorkspaceTabsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    /// <summary>
    /// How many closed query tabs are remembered for Ctrl+Shift+T.
    /// </summary>
    public const int CLOSED_TAB_CAPACITY = 10;

    #endregion

    #region Fields

    private int m_nextQueryNumber = 1;

    /// <summary>
    /// The query tabs that have been closed, newest first, so that Ctrl+Shift+T can bring one back.
    ///
    /// The text of a query is usually its only copy - nothing on disk, nothing in a history yet - and
    /// closing the wrong tab is one keystroke away from closing the right one.
    /// </summary>
    private readonly List<ClosedTab> m_closedTabs = [];

    /// <summary>
    /// What is kept about a closed tab. Not the tab itself: it has been disposed, and its result set
    /// belongs to a connection that may since have gone.
    /// </summary>
    private sealed record ClosedTab(string Title, string SqlText, string? FilePath, Guid ConnectionId);

    #endregion

    #region Constructors

    public WorkspaceTabsViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefault();
        InitEvents();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Tabs = [];

        // Create initial query tab
        AddNewQueryTab();
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;

        // Per session, never global. The old ConnectionStatusChanged belonged to the application, so
        // disconnecting one database closed the tabs of all of them (WS-13).
        Connections.SessionOpened += OnSessionOpened;
        Connections.SessionClosed += OnSessionClosed;
        Connections.ActiveChanged += OnActiveSessionChanged;
    }

    private void InitCommands()
    {
        NewQueryTabCommand = new RelayCommand(AddNewQueryTab);
        FindCommand = new RelayCommand(() => SelectedQueryTab?.OpenSearch(replace: false),
            () => SelectedQueryTab != null);
        ReopenClosedTabCommand = new RelayCommand(ReopenClosedTab, () => m_closedTabs.Count > 0);
        ExecuteCurrentStatementCommand = new RelayCommandAsync(ExecuteCurrentStatementAsync);
        CloseTabCommand = new RelayCommandAsync<WorkspaceTabViewModel>(CloseTabAsync);
        CloseAllTabsCommand = new RelayCommandAsync(CloseAllTabsAsync);
        CloseOtherTabsCommand = new RelayCommandAsync<WorkspaceTabViewModel>(CloseOtherTabsAsync);
        PinTabCommand = new RelayCommand<WorkspaceTabViewModel>(PinTab);
        UnpinTabCommand = new RelayCommand<WorkspaceTabViewModel>(UnpinTab);
        SaveTabCommand = new RelayCommandAsync(SaveCurrentTabAsync);
        SaveTabAsCommand = new RelayCommandAsync(SaveCurrentTabAsAsync);
        ExecuteQueryCommand = new RelayCommandAsync(ExecuteQueryAsync);
        ExecuteSelectionCommand = new RelayCommandAsync(ExecuteSelectionAsync);
        ClearResultsCommand = new RelayCommand(ClearResults);
    }

    #endregion

    #region Tab Management - Query

    private void AddNewQueryTab()
    {
        // A tab is opened in the connection the user is looking at, and stays in it (WS-3). At startup
        // there is none, and the tab is unbound until the first database is opened.
        var tab = new QueryTabViewModel(ApplicationVm, Connections.Active)
        {
            Title = Localization.Format("Tab.Query", m_nextQueryNumber++),
            SqlText = string.Empty
        };

        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        Logger.LogInformation("Created new query tab: {Title}", tab.Title);
    }

    /// <param name="activate">
    /// Whether the new tab is brought to the front. <b>False is the middle click</b> (section 2.7):
    /// "open the data in a new tab WITHOUT activating it" is what makes the gesture worth having -
    /// a person can fire it at four tables in four seconds and then read them, instead of being
    /// dragged into each one as it opens.
    /// </param>
    public QueryTabViewModel OpenQueryTab(string sql, string? title = null, IDatabaseSession? session = null,
        bool activate = true)
    {
        var tab = new QueryTabViewModel(ApplicationVm, session ?? Connections.Active)
        {
            Title = title ?? Localization.Format("Tab.Query", m_nextQueryNumber++),
            SqlText = sql
        };

        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);

        if (activate)
            SelectedTab = tab;

        return tab;
    }

    #endregion

    #region Tab Management - Table Edit

    /// <summary>
    /// Opens the table for editing IN THE GIVEN CONNECTION. The same table name in two databases is
    /// two tabs, not one - which is why the session is part of the identity here.
    /// </summary>
    public async Task<TableEditTabViewModel> OpenTableEditTabAsync(IDatabaseSession session, string tableName)
    {
        // Check if tab already exists
        var existingTab = Tabs.OfType<TableEditTabViewModel>()
            .FirstOrDefault(t => t.Session == session && t.TableName == tableName);

        if (existingTab != null)
        {
            SelectedTab = existingTab;
            return existingTab;
        }

        var tab = new TableEditTabViewModel(ApplicationVm, session, tableName);
        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        await tab.LoadDataAsync();

        Logger.LogInformation("Opened table edit tab: {TableName}", tableName);

        return tab;
    }

    #endregion

    #region Tab Management - Structure

    public async Task<StructureTabViewModel> OpenStructureTabAsync(IDatabaseSession session,
        string objectName, DatabaseNodeType objectType)
    {
        // Check if tab already exists
        var existingTab = Tabs.OfType<StructureTabViewModel>()
            .FirstOrDefault(t => t.Session == session
                              && t.ObjectName == objectName
                              && t.ObjectType == objectType);

        if (existingTab != null)
        {
            SelectedTab = existingTab;
            return existingTab;
        }

        var tab = new StructureTabViewModel(ApplicationVm, session, objectName, objectType);
        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        await tab.LoadStructureAsync();

        Logger.LogInformation("Opened structure tab: {Type} {Name}", objectType, objectName);

        return tab;
    }

    #endregion

    #region Tab Management - Database

    /// <summary>
    /// Opens the «База» tab of a connection, or brings the one that is already open to the front
    /// (WS-54).
    /// </summary>
    /// <remarks>
    /// <b>One per connection</b>, because the tab is about the connection rather than about an object
    /// in it - and it is pinned, so it keeps the place it was given rather than drifting along the
    /// strip as query tabs are opened and closed.
    /// </remarks>
    public async Task<DatabaseTabViewModel> OpenDatabaseTabAsync(IDatabaseSession session)
    {
        var existingTab = Tabs.OfType<DatabaseTabViewModel>()
            .FirstOrDefault(tab => tab.Session == session);

        if (existingTab != null)
        {
            SelectedTab = existingTab;
            return existingTab;
        }

        var tab = new DatabaseTabViewModel(ApplicationVm, session);
        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        await tab.RefreshAsync();

        Logger.LogInformation("Opened the database tab of {Name}", session.DisplayName);

        return tab;
    }

    #endregion

    #region Tab Management - Common

    private void AddTab(WorkspaceTabViewModel tab)
    {
        // Find position after pinned tabs
        var insertIndex = Tabs.Count(t => t.IsPinned);
        Tabs.Insert(insertIndex, tab);
    }

    /// <summary>
    /// Brings back the most recently closed query tab, in its own connection if that connection is
    /// still open (WS-3: it does not adopt another one).
    /// </summary>
    private void ReopenClosedTab()
    {
        if (m_closedTabs.Count == 0)
            return;

        var closed = m_closedTabs[0];
        m_closedTabs.RemoveAt(0);

        var session = Connections.Find(closed.ConnectionId);

        var tab = new QueryTabViewModel(ApplicationVm, session)
        {
            Title = closed.Title,
            SqlText = closed.SqlText,
            FilePath = closed.FilePath,
            IsModified = false
        };

        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        Logger.LogInformation("Reopened closed tab: {Title}", tab.Title);
    }

    /// <summary>
    /// Remembers a query tab's text before it goes.
    /// </summary>
    private void RememberClosed(WorkspaceTabViewModel tab)
    {
        if (tab is not QueryTabViewModel query || string.IsNullOrWhiteSpace(query.SqlText))
            return;

        m_closedTabs.Insert(0, new ClosedTab(
            query.Title, query.SqlText, query.FilePath, query.Session?.Id ?? Guid.Empty));

        while (m_closedTabs.Count > CLOSED_TAB_CAPACITY)
            m_closedTabs.RemoveAt(m_closedTabs.Count - 1);
    }

    private async Task CloseTabAsync(WorkspaceTabViewModel? tab)
    {
        if (tab == null)
            return;

        // Don't close pinned tabs via close button
        if (tab.IsPinned)
            return;

        // Don't close if it's the last tab
        if (Tabs.Count <= 1)
            return;

        // ConfirmCloseAsync, not CanClose: a tab holding unapplied edits gets to ask before its
        // buffer is thrown away, and OnClosed below disposes the DataTable that holds it.
        if (!await tab.ConfirmCloseAsync())
            return;

        RememberClosed(tab);

        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.OnClosed();

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        // Select another tab
        if (Tabs.Count > 0)
        {
            if (index >= Tabs.Count)
                index = Tabs.Count - 1;

            SelectedTab = Tabs[index];
        }

        // A tab that said something about itself on the status line takes it back with it.
        ApplicationVm.MainWindowVm.ForgetWhatWasSaidBy(tab);

        Logger.LogInformation("Closed tab: {Title}", tab.Title);
    }

    private async Task CloseAllTabsAsync()
    {
        // Keep at least one tab
        var tabsToClose = Tabs.Where(t => !t.IsPinned).ToList();

        // If all tabs would be closed, keep the selected one or the last one
        if (tabsToClose.Count == Tabs.Count)
        {
            var keepTab = SelectedTab ?? Tabs.LastOrDefault();
            if (keepTab != null)
                tabsToClose.Remove(keepTab);
        }

        foreach (var tab in tabsToClose)
        {
            if (!await tab.ConfirmCloseAsync())
                continue;

            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.OnClosed();
            Tabs.Remove(tab);

            ApplicationVm.MainWindowVm.ForgetWhatWasSaidBy(tab);
        }

        SelectedTab ??= Tabs.LastOrDefault();

        Logger.LogInformation("Closed all unpinned tabs (kept at least one)");
    }

    private async Task CloseOtherTabsAsync(WorkspaceTabViewModel? keepTab)
    {
        if (keepTab == null)
            return;

        var tabsToClose = Tabs.Where(t => t != keepTab && !t.IsPinned).ToList();

        foreach (var tab in tabsToClose)
        {
            if (!await tab.ConfirmCloseAsync())
                continue;

            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.OnClosed();
            Tabs.Remove(tab);

            ApplicationVm.MainWindowVm.ForgetWhatWasSaidBy(tab);
        }

        SelectedTab = keepTab;

        Logger.LogInformation("Closed other tabs, kept: {Title}", keepTab.Title);
    }

    /// <summary>
    /// Asks every tab holding unapplied work, for the two things that end all of them at once:
    /// leaving the application, and disconnecting the database. Returns false as soon as one says no,
    /// and asks no further - the answer is already "stay".
    ///
    /// Deliberately asks BEFORE the connection goes away. A tab asked afterwards could only offer to
    /// discard, since there is nothing left to apply the edits to.
    /// </summary>
    public async Task<bool> ConfirmCloseAllAsync()
    {
        return await ConfirmCloseAsync(Tabs.ToList());
    }

    /// <summary>
    /// The same question, asked of one connection's tabs only - disconnecting it ends them and
    /// nothing else (WS-13). Asking the whole workspace would make a user answer for databases that
    /// are not going anywhere.
    /// </summary>
    public async Task<bool> ConfirmCloseSessionAsync(IDatabaseSession session)
    {
        return await ConfirmCloseAsync(Tabs.Where(tab => tab.Session == session).ToList());
    }

    private static async Task<bool> ConfirmCloseAsync(IReadOnlyList<WorkspaceTabViewModel> tabs)
    {
        foreach (var tab in tabs)
        {
            if (tab.CanClose())
                continue;

            if (!await tab.ConfirmCloseAsync())
                return false;
        }

        return true;
    }

    private void PinTab(WorkspaceTabViewModel? tab)
    {
        if (tab == null || tab.IsPinned)
            return;

        tab.IsPinned = true;

        // Move to beginning (after other pinned tabs)
        Tabs.Remove(tab);
        var insertIndex = Tabs.Count(t => t.IsPinned);
        Tabs.Insert(insertIndex, tab);

        Logger.LogInformation("Pinned tab: {Title}", tab.Title);
    }

    private void UnpinTab(WorkspaceTabViewModel? tab)
    {
        if (tab == null || !tab.IsPinned)
            return;

        tab.IsPinned = false;

        // Move after pinned tabs
        Tabs.Remove(tab);
        var insertIndex = Tabs.Count(t => t.IsPinned);
        Tabs.Insert(insertIndex, tab);

        Logger.LogInformation("Unpinned tab: {Title}", tab.Title);
    }

    #endregion

    #region Query Execution

    /// <summary>
    /// F5: the statement under the cursor of the selected tab (WS-25).
    /// </summary>
    private async Task ExecuteCurrentStatementAsync()
    {
        if (SelectedTab is not QueryTabViewModel queryTab)
            return;

        if (string.IsNullOrWhiteSpace(queryTab.SqlText) || !IsRunnable(queryTab))
            return;

        BeginExecuting(queryTab);

        try
        {
            await queryTab.ExecuteCurrentStatementAsync();

            await FollowTheScriptAsync(queryTab);
        }
        finally
        {
            IsExecuting = false;
            CurrentExecutingTab = null;
        }
    }

    /// <summary>
    /// Everything a run has to say before it starts, in one place.
    /// </summary>
    /// <remarks>
    /// <b>The status bar used to keep the LAST query's completion time while the next one ran.</b>
    /// The progress bar said something was happening and the words beside it said «Выполнено за
    /// 9,60 мс» - about a query that had already finished. Two of the three execution paths set
    /// <c>IsExecuting</c> by hand and neither touched the text; they go through here now, so a
    /// fourth path cannot forget it either.
    /// </remarks>
    private void BeginExecuting(QueryTabViewModel tab)
    {
        IsExecuting = true;
        CurrentExecutingTab = tab;

        ApplicationVm.MainWindowVm.StatusText = ApplicationVm.Localization["Query.Running"];
    }

    private async Task ExecuteQueryAsync()
    {
        if (SelectedTab is not QueryTabViewModel queryTab)
            return;

        if (string.IsNullOrWhiteSpace(queryTab.SqlText))
            return;

        if (!IsRunnable(queryTab))
            return;

        await ExecuteSqlAsync(queryTab, queryTab.SqlText);
    }

    private async Task ExecuteSelectionAsync()
    {
        if (SelectedTab is not QueryTabViewModel queryTab)
            return;

        var sqlToExecute = !string.IsNullOrWhiteSpace(queryTab.SelectedText)
            ? queryTab.SelectedText
            : queryTab.SqlText;

        if (string.IsNullOrWhiteSpace(sqlToExecute))
            return;

        if (!IsRunnable(queryTab))
            return;

        await ExecuteSqlAsync(queryTab, sqlToExecute);
    }

    /// <summary>
    /// Whether the tab has a connection to run in - ITS connection. What is selected in the tree has
    /// nothing to do with the answer (WS-3).
    /// </summary>
    private bool IsRunnable(QueryTabViewModel tab)
    {
        if (tab.Session?.IsConnected == true)
            return true;

        ApplicationVm.MainWindowVm.StatusText = tab.ConnectionName == null
            ? Localization["Query.NotConnected"]
            : Localization.Format("Query.ConnectionClosed", tab.ConnectionName);

        return false;
    }

    /// <summary>
    /// The toolbar's Execute. The work belongs to the tab - it owns the results, the messages and the
    /// connection - so this sets the workspace's own state around it and reloads the tree if the
    /// script changed the schema.
    ///
    /// There used to be a second, independent copy of the execution here, with its own error
    /// handling and its own hand-written scan for a leading DDL keyword through comments. Two paths
    /// meant every change to how a query runs had to be made twice, or - what actually happened -
    /// once.
    /// </summary>
    private async Task ExecuteSqlAsync(QueryTabViewModel tab, string sql)
    {
        BeginExecuting(tab);

        try
        {
            await tab.ExecuteSqlAsync(sql);

            await FollowTheScriptAsync(tab);
        }
        finally
        {
            IsExecuting = false;
            CurrentExecutingTab = null;
        }
    }

    /// <summary>
    /// Brings the tree into line with what a run just did: a reload when the schema changed, and a
    /// fresh count of the tables that were WRITTEN TO when it did not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second half is new on 2026-08-09 and it closes phase 10's item 5 - fifty rows went in
    /// through the editor and the node still said 39. It was recorded as probably the deliberate
    /// laziness of <c>WS-16</c>; measured, it was that nothing asked. DDL had reloaded the branch
    /// since stage 6, and a script that writes rows changes no schema, so it went past both.
    /// </para>
    /// <para>
    /// Only the tables named, and only after a statement came back clean - a count is a query. A
    /// reload counts everything anyway, so the two are exclusive rather than cumulative.
    /// </para>
    /// </remarks>
    private async Task FollowTheScriptAsync(QueryTabViewModel tab)
    {
        if (tab.Session == null)
            return;

        try
        {
            if (tab.DdlWasExecuted)
            {
                Logger.LogInformation("The script changed the schema; reloading the tree of {Connection}",
                    tab.Session.DisplayName);

                // The branch of the connection the statement ran in, not the selected one.
                await ApplicationVm.DatabaseExplorerVm.RefreshAsync(tab.Session);
                return;
            }

            if (tab.TablesWritten.Count == 0)
                return;

            Logger.LogInformation("The script wrote to {Count} table(s) of {Connection}; counting those again",
                tab.TablesWritten.Count, tab.Session.DisplayName);

            await ApplicationVm.DatabaseExplorerVm.CountRowsAsync(tab.Session, tab.TablesWritten);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to bring the tree into line with the script that just ran");
        }
    }

    private void ClearResults()
    {
        if (SelectedTab is QueryTabViewModel queryTab)
        {
            queryTab.ClearResults();
            ApplicationVm.MainWindowVm.StatusText = Localization["Status.ResultsCleared"];
        }
    }

    #endregion

    #region File Operations

    private async Task SaveCurrentTabAsync()
    {
        if (SelectedTab is not QueryTabViewModel queryTab)
            return;

        if (!CanSaveTab)
            return;

        if (string.IsNullOrEmpty(queryTab.FilePath))
        {
            await SaveCurrentTabAsAsync();
            return;
        }

        try
        {
            await File.WriteAllTextAsync(queryTab.FilePath, queryTab.SqlText);
            queryTab.IsModified = false;

            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Status.Saved", queryTab.FilePath);
            Logger.LogInformation("Saved query tab: {FilePath}", queryTab.FilePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Status.SaveFileFailed", ex.Message);
            Logger.LogError(ex, "Failed to save query tab: {FilePath}", queryTab.FilePath);
        }
    }

    private async Task SaveCurrentTabAsAsync()
    {
        if (SelectedTab is not QueryTabViewModel queryTab)
            return;

        var filePath = await ApplicationVm.Dialogs.SaveFileAsync(
            Localization["Menu.SaveQueryAs.Title"],
            suggestedFileName: $"{queryTab.Title}.sql",
            defaultExtension: ".sql",
            filters:
            [
                new FileFilter(Localization.Format("Common.Filter.Files", "SQL"), ["*.sql", "*.witsql"]),
                new FileFilter(Localization["Common.Filter.AllFiles"], ["*.*"])
            ]);

        if (string.IsNullOrEmpty(filePath))
            return;

        try
        {
            await File.WriteAllTextAsync(filePath, queryTab.SqlText);

            queryTab.FilePath = filePath;
            queryTab.Title = Path.GetFileNameWithoutExtension(filePath);
            queryTab.IsModified = false;

            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Status.Saved", filePath);
            Logger.LogInformation("Saved query tab as: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Status.SaveFileFailed", ex.Message);
            Logger.LogError(ex, "Failed to save query tab as new file");
        }
    }

    public async Task OpenFileAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            var tab = new QueryTabViewModel(ApplicationVm, Connections.Active)
            {
                Title = fileName,
                SqlText = content,
                FilePath = filePath,
                IsModified = false
            };

            tab.PropertyChanged += OnTabPropertyChanged;

            AddTab(tab);
            SelectedTab = tab;

            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Status.Opened", filePath);
            Logger.LogInformation("Opened query file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Status.OpenFileFailed", ex.Message);
            Logger.LogError(ex, "Failed to open query file: {FilePath}", filePath);
        }
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        var hasSelectedTab = SelectedTab != null;
        var hasMultipleTabs = Tabs.Count > 1;

        // The selected TAB's connection. Studio can have three databases open and still not be able to
        // run this tab's query, because the connection it belongs to was closed.
        var isConnected = SelectedTab?.Session?.IsConnected == true;

        var isQueryTab = SelectedTab is QueryTabViewModel;
        var queryTab = SelectedTab as QueryTabViewModel;
        var hasSqlText = isQueryTab && !string.IsNullOrWhiteSpace(queryTab?.SqlText);

        // Can only close tab if there are multiple tabs and the selected one is not pinned
        CanCloseTab = hasSelectedTab && hasMultipleTabs && !(SelectedTab?.IsPinned ?? false);
        
        // Can close all only if there are unpinned tabs and we would still have at least one left
        var unpinnedCount = Tabs.Count(t => !t.IsPinned);
        CanCloseAllTabs = unpinnedCount > 1 || (unpinnedCount == 1 && Tabs.Any(t => t.IsPinned));
        
        // Can close others if there are other unpinned tabs
        CanCloseOtherTabs = hasSelectedTab && Tabs.Count(t => !t.IsPinned && t != SelectedTab) > 0;
        
        CanSaveTab = isQueryTab && hasSqlText;
        CanExecuteQuery = isQueryTab && hasSqlText && !IsExecuting && isConnected;

        // Current tab type for UI. The contextual toolbar is built from these: it belongs to the
        // active tab and changes with it (WS-8), rather than being one panel of everything.
        CurrentTabType = SelectedTab?.TabType;

        IsQueryTabSelected = SelectedTab is QueryTabViewModel;
        SelectedQueryTab = SelectedTab as QueryTabViewModel;
        IsTableEditTabSelected = SelectedTab is TableEditTabViewModel;
        IsStructureTabSelected = SelectedTab is StructureTabViewModel;

        // The fourth kind of tab, and it had no band at all: the query toolbar hides for it and
        // nothing took its place, so selecting the Database tab left an empty strip across the
        // window. Seen in the running application and carried forward from phase 10.
        IsDatabaseTabSelected = SelectedTab is DatabaseTabViewModel;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((WorkspaceTabsViewModel vm) => vm.SelectedTab))
        {
            // Notify old tab of deactivation
            // Notify new tab of activation
            SelectedTab?.OnActivated();
            UpdateStatus();
        }

        if (e.IsProperty((WorkspaceTabsViewModel vm) => vm.IsExecuting))
            UpdateStatus();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == SelectedTab)
        {
            if (e.PropertyName == nameof(QueryTabViewModel.SqlText))
                UpdateStatus();
        }
    }

    /// <summary>
    /// A tab that has never had a connection adopts the first one opened, so that a query typed before
    /// File &gt; Open still runs. A tab that HAD one and lost it does not - see
    /// <see cref="WorkspaceTabViewModel.CanBind"/>.
    /// </summary>
    private void OnSessionOpened(object? sender, SessionEventArgs e)
    {
        foreach (var tab in Tabs.Where(tab => tab.CanBind).ToList())
            tab.Bind(e.Session);

        UpdateStatus();
    }

    /// <summary>
    /// WS-13: closing a connection closes the tabs that belong to IT. Everyone else's tabs stay, with
    /// their results, their unapplied edits and their text.
    /// </summary>
    private void OnSessionClosed(object? sender, SessionEventArgs e)
    {
        var tabsToClose = Tabs
            .Where(tab => tab.Session == e.Session && tab.TabType != WorkspaceTabType.Query && !tab.IsPinned)
            .ToList();

        foreach (var tab in tabsToClose)
        {
            // The question belongs before the disconnect (CloseDatabaseAsync asks it), because by
            // here there is no connection left to apply anything to. Saying so out loud rather
            // than discarding in silence.
            if (!tab.CanClose())
                Logger.LogWarning("Discarding unapplied changes in {Title}: the connection is gone", tab.Title);

            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.Unbind();
            tab.OnClosed();
            Tabs.Remove(tab);
        }

        // Query tabs are kept: the text in one is usually its only copy, and it is not the closing
        // connection's to throw away. They lose their connection and say so, rather than quietly
        // running somewhere else.
        foreach (var tab in Tabs.Where(tab => tab.Session == e.Session).ToList())
            tab.Unbind();

        if (Tabs.Count == 0)
            AddNewQueryTab();

        if (SelectedTab == null || !Tabs.Contains(SelectedTab))
            SelectedTab = Tabs.LastOrDefault();

        UpdateStatus();
    }

    private void OnActiveSessionChanged(object? sender, SessionEventArgs? e)
    {
        UpdateStatus();
    }

    #endregion

    #region Properties

    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; private set; } = null!;

    [Notify]
    public WorkspaceTabViewModel? SelectedTab { get; set; }

    [Notify]
    public bool IsExecuting { get; set; }

    [Notify]
    public QueryTabViewModel? CurrentExecutingTab { get; set; }

    [Notify]
    public WorkspaceTabType? CurrentTabType { get; private set; }

    /// <summary>
    /// Which toolbar the frame should be showing (WS-8).
    /// </summary>
    [Notify]
    public bool IsQueryTabSelected { get; private set; }

    [Notify]
    public bool IsTableEditTabSelected { get; private set; }

    [Notify]
    public bool IsStructureTabSelected { get; private set; }

    [Notify]
    public bool IsDatabaseTabSelected { get; private set; }

    [Notify]
    public bool CanCloseTab { get; private set; }

    [Notify]
    public bool CanCloseAllTabs { get; private set; }

    [Notify]
    public bool CanCloseOtherTabs { get; private set; }

    [Notify]
    public bool CanSaveTab { get; private set; }

    [Notify]
    public bool CanExecuteQuery { get; private set; }

    /// <summary>
    /// The selected query tab, if the selected one is a query tab.
    ///
    /// A stored property rather than a cast of <see cref="SelectedTab"/>, because the contextual
    /// toolbar binds through it (WS-8) and a computed getter raises no change notification - the
    /// transaction buttons would go on pointing at the tab that was selected when the window opened.
    /// </summary>
    [Notify]
    public QueryTabViewModel? SelectedQueryTab { get; private set; }

    /// <summary>
    /// Gets the currently selected table edit tab, if any.
    /// </summary>
    public TableEditTabViewModel? SelectedTableEditTab => SelectedTab as TableEditTabViewModel;

    /// <summary>
    /// Gets the currently selected structure tab, if any.
    /// </summary>
    public StructureTabViewModel? SelectedStructureTab => SelectedTab as StructureTabViewModel;

    #endregion

    #region Commands

    public ICommand NewQueryTabCommand { get; private set; } = null!;

    /// <summary>
    /// Opens the find band of the query tab in front.
    /// </summary>
    /// <remarks>
    /// <b>Ctrl+F was the only way to search</b>, which made the search the one frame of the
    /// documentation set that could not be taken without a key being pressed by hand -
    /// <c>Edit</c> offered Copy, Paste and Settings, and the palette could not reach it either.
    /// The gesture still belongs to the window, which handles it where it can see which control
    /// has focus; this is the same band, opened from the menu.
    /// </remarks>
    public ICommand FindCommand { get; private set; } = null!;

    /// <summary>
    /// Ctrl+Shift+T. Brings back the last closed query tab with its text.
    /// </summary>
    public ICommand ReopenClosedTabCommand { get; private set; } = null!;

    /// <summary>
    /// F5. The whole script is <see cref="ExecuteQueryCommand"/>, on Ctrl+Shift+F5.
    /// </summary>
    public ICommand ExecuteCurrentStatementCommand { get; private set; } = null!;

    public ICommand CloseTabCommand { get; private set; } = null!;

    public ICommand CloseAllTabsCommand { get; private set; } = null!;

    public ICommand CloseOtherTabsCommand { get; private set; } = null!;

    public ICommand PinTabCommand { get; private set; } = null!;

    public ICommand UnpinTabCommand { get; private set; } = null!;

    public ICommand SaveTabCommand { get; private set; } = null!;

    public ICommand SaveTabAsCommand { get; private set; } = null!;

    public ICommand ExecuteQueryCommand { get; private set; } = null!;

    public ICommand ExecuteSelectionCommand { get; private set; } = null!;

    public ICommand ClearResultsCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IConnectionManager Connections => ApplicationVm.Connections;

    public ISettingsService Settings => ApplicationVm.Settings;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion

    #region Localization

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
