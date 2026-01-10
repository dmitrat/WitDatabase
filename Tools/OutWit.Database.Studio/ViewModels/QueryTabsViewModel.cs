using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for managing multiple query editor tabs.
/// </summary>
public class QueryTabsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    /// <summary>
    /// DDL keywords that should trigger a schema refresh after execution.
    /// </summary>
    private static readonly string[] DDL_KEYWORDS = 
    [
        "CREATE", "DROP", "ALTER", "TRUNCATE", "RENAME"
    ];

    #endregion

    #region Fields

    private int m_nextQueryNumber = 1;

    #endregion

    #region Constructors

    public QueryTabsViewModel(ApplicationViewModel applicationVm)
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
        
        // Create initial tab
        AddNewTab();
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        Database.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    private void InitCommands()
    {
        NewTabCommand = new RelayCommand(_ => AddNewTab());
        CloseTabCommand = new RelayCommand<QueryTab>(CloseTab);
        CloseAllTabsCommand = new RelayCommand(_ => CloseAllTabs());
        CloseOtherTabsCommand = new RelayCommand<QueryTab>(CloseOtherTabs);
        SaveTabCommand = new RelayCommand<QueryTab>(async tab => await SaveTabAsync(tab));
        SaveTabAsCommand = new RelayCommand<QueryTab>(async tab => await SaveTabAsAsync(tab));
        ExecuteQueryCommand = new RelayCommand<QueryTab>(async tab => await ExecuteQueryAsync(tab));
        ExecuteSelectionCommand = new RelayCommand<string>(async sql => await ExecuteSelectionAsync(sql));
        ClearResultsCommand = new RelayCommand<QueryTab>(ClearResults);
    }

    #endregion

    #region Functions

    private void AddNewTab()
    {
        var tab = new QueryTab
        {
            Title = $"Query {m_nextQueryNumber++}",
            SqlText = string.Empty
        };

        tab.PropertyChanged += OnTabPropertyChanged;

        Tabs.Add(tab);
        SelectedTab = tab;

        Logger.LogInformation("Created new query tab: {Title}", tab.Title);
    }

    private void CloseTab(QueryTab? tab)
    {
        if (tab == null || !CanCloseTab)
            return;

        tab.PropertyChanged -= OnTabPropertyChanged;

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count > 0)
        {
            if (index >= Tabs.Count)
                index = Tabs.Count - 1;
            
            SelectedTab = Tabs[index];
        }
        else
        {
            AddNewTab();
        }

        Logger.LogInformation("Closed query tab: {Title}", tab.Title);
    }

    private void CloseAllTabs()
    {
        foreach (var tab in Tabs)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
        }

        Tabs.Clear();
        AddNewTab();
        Logger.LogInformation("Closed all query tabs");
    }

    private void CloseOtherTabs(QueryTab? tab)
    {
        if (tab == null || !CanCloseOtherTabs)
            return;

        var tabsToRemove = Tabs.Where(t => t != tab).ToList();
        foreach (var t in tabsToRemove)
        {
            t.PropertyChanged -= OnTabPropertyChanged;
            Tabs.Remove(t);
        }

        SelectedTab = tab;
        Logger.LogInformation("Closed other query tabs, kept: {Title}", tab.Title);
    }

    private async Task SaveTabAsync(QueryTab? tab)
    {
        if (tab == null || !CanSaveTab)
            return;

        if (string.IsNullOrEmpty(tab.FilePath))
        {
            await SaveTabAsAsync(tab);
            return;
        }

        try
        {
            await File.WriteAllTextAsync(tab.FilePath, tab.SqlText);
            tab.IsModified = false;

            ApplicationVm.MainWindowVm.StatusText = $"Saved: {tab.FilePath}";
            Logger.LogInformation("Saved query tab: {FilePath}", tab.FilePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = $"Error saving file: {ex.Message}";
            Logger.LogError(ex, "Failed to save query tab: {FilePath}", tab.FilePath);
        }
    }

    private async Task SaveTabAsAsync(QueryTab? tab)
    {
        if (tab == null)
            return;

        // TODO: Show save file dialog
        Logger.LogInformation("Save As requested for tab: {Title}", tab.Title);
        await Task.CompletedTask;
    }

    private async Task ExecuteQueryAsync(QueryTab? tab)
    {
        if (tab == null)
            tab = SelectedTab;

        if (tab == null || string.IsNullOrWhiteSpace(tab.SqlText))
            return;

        if (!Database.IsConnected)
        {
            ApplicationVm.MainWindowVm.StatusText = "Not connected to database";
            return;
        }

        await ExecuteSqlAsync(tab, tab.SqlText);
    }

    private async Task ExecuteSelectionAsync(string? selectedText)
    {
        if (SelectedTab == null)
            return;

        var sqlToExecute = string.IsNullOrWhiteSpace(selectedText) 
            ? SelectedTab.SqlText 
            : selectedText;

        if (string.IsNullOrWhiteSpace(sqlToExecute))
            return;

        if (!Database.IsConnected)
        {
            ApplicationVm.MainWindowVm.StatusText = "Not connected to database";
            return;
        }

        await ExecuteSqlAsync(SelectedTab, sqlToExecute);
    }

    private async Task ExecuteSqlAsync(QueryTab tab, string sql)
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

                // Refresh schema tree after DDL operations
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

    private void ClearResults(QueryTab? tab)
    {
        if (tab == null)
            return;

        tab.ClearResults();
        ApplicationVm.MainWindowVm.StatusText = "Results cleared";
    }

    /// <summary>
    /// Checks if the SQL statement is a DDL statement that modifies schema.
    /// </summary>
    private static bool IsDdlStatement(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        // Get first non-whitespace word
        var trimmed = sql.TrimStart();
        var firstWord = trimmed.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                               .FirstOrDefault()?
                               .ToUpperInvariant();

        return firstWord != null && DDL_KEYWORDS.Contains(firstWord);
    }

    #endregion

    #region Public Methods

    public async Task OpenFileAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            var tab = new QueryTab
            {
                Title = fileName,
                SqlText = content,
                FilePath = filePath,
                IsModified = false
            };

            tab.PropertyChanged += OnTabPropertyChanged;

            Tabs.Add(tab);
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

    public void MarkCurrentTabAsModified()
    {
        if (SelectedTab != null)
        {
            SelectedTab.IsModified = true;
        }
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        var hasSelectedTab = SelectedTab != null;
        var hasMultipleTabs = Tabs.Count > 1;
        var hasSqlText = hasSelectedTab && !string.IsNullOrWhiteSpace(SelectedTab!.SqlText);
        var isConnected = Database.IsConnected;
        
        CanCloseTab = hasSelectedTab && hasMultipleTabs;
        CanCloseAllTabs = Tabs.Count > 0;
        CanCloseOtherTabs = hasSelectedTab && hasMultipleTabs;
        CanSaveTab = hasSqlText;
        CanExecuteQuery = hasSqlText && !IsExecuting && isConnected;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((QueryTabsViewModel vm) => vm.SelectedTab))
            UpdateStatus();

        if (e.IsProperty((QueryTabsViewModel vm) => vm.IsExecuting))
            UpdateStatus();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == SelectedTab && e.PropertyName == nameof(QueryTab.SqlText))
        {
            UpdateStatus();
        }
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        UpdateStatus();
    }

    #endregion

    #region Properties

    public ObservableCollection<QueryTab> Tabs { get; private set; } = null!;

    [Notify]
    public QueryTab? SelectedTab { get; set; }

    [Notify]
    public bool IsExecuting { get; set; }

    [Notify]
    public QueryTab? CurrentExecutingTab { get; set; }

    public string CurrentSqlText
    {
        get => SelectedTab?.SqlText ?? string.Empty;
        set
        {
            if (SelectedTab != null)
            {
                SelectedTab.SqlText = value;
                SelectedTab.IsModified = true;
            }
        }
    }

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

    #endregion

    #region Commands

    public ICommand NewTabCommand { get; private set; } = null!;

    public ICommand CloseTabCommand { get; private set; } = null!;

    public ICommand CloseAllTabsCommand { get; private set; } = null!;

    public ICommand CloseOtherTabsCommand { get; private set; } = null!;

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
