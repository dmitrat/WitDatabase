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

    private static readonly string[] DDL_KEYWORDS =
    [
        "CREATE", "DROP", "ALTER", "TRUNCATE", "RENAME"
    ];

    #endregion

    #region Fields

    private int m_nextQueryNumber = 1;

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
        Database.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    private void InitCommands()
    {
        NewQueryTabCommand = new RelayCommand(AddNewQueryTab);
        CloseTabCommand = new RelayCommand<WorkspaceTabViewModel>(CloseTab);
        CloseAllTabsCommand = new RelayCommand(CloseAllTabs);
        CloseOtherTabsCommand = new RelayCommand<WorkspaceTabViewModel>(CloseOtherTabs);
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
        var tab = new QueryTabViewModel(ApplicationVm)
        {
            Title = $"Query {m_nextQueryNumber++}",
            SqlText = string.Empty
        };

        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        Logger.LogInformation("Created new query tab: {Title}", tab.Title);
    }

    public QueryTabViewModel OpenQueryTab(string sql, string? title = null)
    {
        var tab = new QueryTabViewModel(ApplicationVm)
        {
            Title = title ?? $"Query {m_nextQueryNumber++}",
            SqlText = sql
        };

        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        return tab;
    }

    #endregion

    #region Tab Management - Table Edit

    public async Task<TableEditTabViewModel> OpenTableEditTabAsync(string tableName)
    {
        // Check if tab already exists
        var existingTab = Tabs.OfType<TableEditTabViewModel>()
            .FirstOrDefault(t => t.UniqueId == $"edit:{tableName}");

        if (existingTab != null)
        {
            SelectedTab = existingTab;
            return existingTab;
        }

        var tab = new TableEditTabViewModel(ApplicationVm, tableName);
        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        await tab.LoadDataAsync();

        Logger.LogInformation("Opened table edit tab: {TableName}", tableName);

        return tab;
    }

    #endregion

    #region Tab Management - Structure

    public async Task<StructureTabViewModel> OpenStructureTabAsync(string objectName, DatabaseNodeType objectType)
    {
        // Check if tab already exists
        var uniqueId = $"structure:{objectType}:{objectName}";
        var existingTab = Tabs.OfType<StructureTabViewModel>()
            .FirstOrDefault(t => t.UniqueId == uniqueId);

        if (existingTab != null)
        {
            SelectedTab = existingTab;
            return existingTab;
        }

        var tab = new StructureTabViewModel(ApplicationVm, objectName, objectType);
        tab.PropertyChanged += OnTabPropertyChanged;

        AddTab(tab);
        SelectedTab = tab;

        await tab.LoadStructureAsync();

        Logger.LogInformation("Opened structure tab: {Type} {Name}", objectType, objectName);

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

    private void CloseTab(WorkspaceTabViewModel? tab)
    {
        if (tab == null)
            return;

        // Don't close pinned tabs via close button
        if (tab.IsPinned)
            return;

        // Don't close if it's the last tab
        if (Tabs.Count <= 1)
            return;

        if (!tab.CanClose())
            return;

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

        Logger.LogInformation("Closed tab: {Title}", tab.Title);
    }

    private void CloseAllTabs()
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
            if (!tab.CanClose())
                continue;

            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.OnClosed();
            Tabs.Remove(tab);
        }

        SelectedTab ??= Tabs.LastOrDefault();

        Logger.LogInformation("Closed all unpinned tabs (kept at least one)");
    }

    private void CloseOtherTabs(WorkspaceTabViewModel? keepTab)
    {
        if (keepTab == null)
            return;

        var tabsToClose = Tabs.Where(t => t != keepTab && !t.IsPinned).ToList();

        foreach (var tab in tabsToClose)
        {
            if (!tab.CanClose())
                continue;

            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.OnClosed();
            Tabs.Remove(tab);
        }

        SelectedTab = keepTab;

        Logger.LogInformation("Closed other tabs, kept: {Title}", keepTab.Title);
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

    private async Task ExecuteQueryAsync()
    {
        if (SelectedTab is not QueryTabViewModel queryTab)
            return;

        if (string.IsNullOrWhiteSpace(queryTab.SqlText))
            return;

        if (!Database.IsConnected)
        {
            ApplicationVm.MainWindowVm.StatusText = "Not connected to database";
            return;
        }

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

        if (!Database.IsConnected)
        {
            ApplicationVm.MainWindowVm.StatusText = "Not connected to database";
            return;
        }

        await ExecuteSqlAsync(queryTab, sqlToExecute);
    }

    private async Task ExecuteSqlAsync(QueryTabViewModel tab, string sql)
    {
        IsExecuting = true;
        CurrentExecutingTab = tab;
        tab.ErrorMessage = null;
        tab.SetResultData(null);

        var isDdlStatement = IsDdlStatement(sql);

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await Database.ExecuteQueryAsync(sql);
            stopwatch.Stop();

            if (result.IsSuccess)
            {
                tab.SetResultData(result.Data);
                tab.RowsAffected = result.RowsAffected;
                tab.ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds;

                ApplicationVm.MainWindowVm.StatusText =
                    $"Query executed in {tab.ExecutionTimeMs:F2}ms. {tab.RowsAffected} rows affected.";

                Logger.LogInformation("Query executed successfully: {Time}ms, {Rows} rows",
                    tab.ExecutionTimeMs, tab.RowsAffected);

                if (isDdlStatement)
                {
                    Logger.LogInformation("DDL statement detected, refreshing schema tree");
                    await ApplicationVm.DatabaseExplorerVm.RefreshAsync();
                }
            }
            else
            {
                tab.ErrorMessage = result.ErrorMessage;
                ApplicationVm.MainWindowVm.StatusText = "Query execution failed";
                Logger.LogWarning("Query execution failed: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            tab.ErrorMessage = $"Execution error: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Query execution error";
            Logger.LogError(ex, "Query execution error");
        }
        finally
        {
            IsExecuting = false;
            CurrentExecutingTab = null;
        }
    }

    private void ClearResults()
    {
        if (SelectedTab is QueryTabViewModel queryTab)
        {
            queryTab.ClearResults();
            ApplicationVm.MainWindowVm.StatusText = "Results cleared";
        }
    }

    private static bool IsDdlStatement(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        var trimmed = sql.TrimStart();
        var firstWord = trimmed.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                               .FirstOrDefault()?
                               .ToUpperInvariant();

        return firstWord != null && DDL_KEYWORDS.Contains(firstWord);
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

            ApplicationVm.MainWindowVm.StatusText = $"Saved: {queryTab.FilePath}";
            Logger.LogInformation("Saved query tab: {FilePath}", queryTab.FilePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = $"Error saving file: {ex.Message}";
            Logger.LogError(ex, "Failed to save query tab: {FilePath}", queryTab.FilePath);
        }
    }

    private async Task SaveCurrentTabAsAsync()
    {
        if (SelectedTab is not QueryTabViewModel queryTab)
            return;

        if (ApplicationVm.MainWindow == null)
            return;

        var storageProvider = ApplicationVm.MainWindow.StorageProvider;

        var saveOptions = new FilePickerSaveOptions
        {
            Title = "Save Query As",
            DefaultExtension = ".sql",
            SuggestedFileName = $"{queryTab.Title}.sql",
            FileTypeChoices =
            [
                new FilePickerFileType("SQL Files") { Patterns = ["*.sql", "*.witsql"] },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        };

        var file = await storageProvider.SaveFilePickerAsync(saveOptions);

        if (file == null)
            return;

        try
        {
            var filePath = file.Path.LocalPath;
            await File.WriteAllTextAsync(filePath, queryTab.SqlText);

            queryTab.FilePath = filePath;
            queryTab.Title = Path.GetFileNameWithoutExtension(filePath);
            queryTab.IsModified = false;

            ApplicationVm.MainWindowVm.StatusText = $"Saved: {filePath}";
            Logger.LogInformation("Saved query tab as: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = $"Error saving file: {ex.Message}";
            Logger.LogError(ex, "Failed to save query tab as new file");
        }
    }

    public async Task OpenFileAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            var tab = new QueryTabViewModel(ApplicationVm)
            {
                Title = fileName,
                SqlText = content,
                FilePath = filePath,
                IsModified = false
            };

            tab.PropertyChanged += OnTabPropertyChanged;

            AddTab(tab);
            SelectedTab = tab;

            ApplicationVm.MainWindowVm.StatusText = $"Opened: {filePath}";
            Logger.LogInformation("Opened query file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = $"Error opening file: {ex.Message}";
            Logger.LogError(ex, "Failed to open query file: {FilePath}", filePath);
        }
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        var hasSelectedTab = SelectedTab != null;
        var hasMultipleTabs = Tabs.Count > 1;
        var isConnected = Database.IsConnected;

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

        // Current tab type for UI
        CurrentTabType = SelectedTab?.TabType;
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

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        if (!isConnected)
        {
            // Close all edit and structure tabs when disconnected
            var tabsToClose = Tabs.Where(t => t.TabType != WorkspaceTabType.Query && !t.IsPinned).ToList();

            foreach (var tab in tabsToClose)
            {
                tab.PropertyChanged -= OnTabPropertyChanged;
                tab.OnClosed();
                Tabs.Remove(tab);
            }

            if (Tabs.Count == 0)
            {
                AddNewQueryTab();
            }
        }

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
    /// Gets the currently selected query tab, if any.
    /// </summary>
    public QueryTabViewModel? SelectedQueryTab => SelectedTab as QueryTabViewModel;

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

    public IDatabaseService Database => ApplicationVm.Database;

    public ISettingsService Settings => ApplicationVm.Settings;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
