using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.ViewModels.Tabs;

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
        NewTabCommand = new RelayCommand(AddNewTab);
        CloseTabCommand = new RelayCommand<QueryTabViewModel>(CloseTab);
        CloseAllTabsCommand = new RelayCommand(CloseAllTabs);
        CloseOtherTabsCommand = new RelayCommand<QueryTabViewModel>(CloseOtherTabs);
        SaveTabCommand = new RelayCommandAsync(SaveTabAsync);
        SaveTabAsCommand = new RelayCommandAsync(SaveTabAsAsync);
        ExecuteQueryCommand = new RelayCommandAsync(ExecuteQueryAsync);
        ExecuteSelectionCommand = new RelayCommandAsync(ExecuteSelectionAsync);
        ClearResultsCommand = new RelayCommand(ClearResults);
    }

    #endregion

    #region Functions

    private void AddNewTab()
    {
        var tab = new QueryTabViewModel(ApplicationVm)
        {
            Title = $"Query {m_nextQueryNumber++}",
            SqlText = string.Empty
        };

        tab.PropertyChanged += OnTabPropertyChanged;

        Tabs.Add(tab);
        SelectedTab = tab;

        Logger.LogInformation("Created new query tab: {Title}", tab.Title);
    }

    private void CloseTab(QueryTabViewModel? tab)
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

    private void CloseOtherTabs(QueryTabViewModel? tab)
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

    private async Task SaveTabAsync()
    {
        if (SelectedTab == null || !CanSaveTab)
            return;

        if (string.IsNullOrEmpty(SelectedTab.FilePath))
        {
            await SaveTabAsAsync();
            return;
        }

        try
        {
            await File.WriteAllTextAsync(SelectedTab.FilePath, SelectedTab.SqlText);
            SelectedTab.IsModified = false;

            ApplicationVm.MainWindowVm.StatusText = $"Saved: {SelectedTab.FilePath}";
            Logger.LogInformation("Saved query tab: {FilePath}", SelectedTab.FilePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = $"Error saving file: {ex.Message}";
            Logger.LogError(ex, "Failed to save query tab: {FilePath}", SelectedTab.FilePath);
        }
    }

    private async Task SaveTabAsAsync()
    {
        if (SelectedTab == null)
            return;

        if (ApplicationVm.MainWindow == null)
            return;

        var storageProvider = ApplicationVm.MainWindow.StorageProvider;

        var saveOptions = new FilePickerSaveOptions
        {
            Title = "Save Query As",
            DefaultExtension = ".sql",
            SuggestedFileName = $"{SelectedTab.Title}.sql",
            FileTypeChoices =
            [
                new FilePickerFileType("SQL Files")
                {
                    Patterns = ["*.sql", "*.witsql"]
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"]
                }
            ]
        };

        var file = await storageProvider.SaveFilePickerAsync(saveOptions);

        if (file == null)
            return;

        try
        {
            var filePath = file.Path.LocalPath;
            await File.WriteAllTextAsync(filePath, SelectedTab.SqlText);
            
            SelectedTab.FilePath = filePath;
            SelectedTab.Title = Path.GetFileNameWithoutExtension(filePath);
            SelectedTab.IsModified = false;

            ApplicationVm.MainWindowVm.StatusText = $"Saved: {filePath}";
            Logger.LogInformation("Saved query tab as: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = $"Error saving file: {ex.Message}";
            Logger.LogError(ex, "Failed to save query tab as new file");
        }
    }

    private async Task ExecuteQueryAsync()
    {
        if (SelectedTab == null || string.IsNullOrWhiteSpace(SelectedTab.SqlText))
            return;

        if (!Database.IsConnected)
        {
            ApplicationVm.MainWindowVm.StatusText = "Not connected to database";
            return;
        }

        await ExecuteSqlAsync(SelectedTab, SelectedTab.SqlText);
    }

    private async Task ExecuteSelectionAsync()
    {
        if (SelectedTab == null)
            return;

        // Use selected text if available, otherwise use full SQL text
        var sqlToExecute = !string.IsNullOrWhiteSpace(SelectedTab.SelectedText) 
            ? SelectedTab.SelectedText 
            : SelectedTab.SqlText;

        if (string.IsNullOrWhiteSpace(sqlToExecute))
            return;

        if (!Database.IsConnected)
        {
            ApplicationVm.MainWindowVm.StatusText = "Not connected to database";
            return;
        }

        await ExecuteSqlAsync(SelectedTab, sqlToExecute);
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

    private void ClearResults()
    {
        if (SelectedTab == null)
            return;

        SelectedTab.ClearResults();
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

            var tab = new QueryTabViewModel(ApplicationVm)
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
        if (sender == SelectedTab && e.PropertyName == nameof(QueryTabViewModel.SqlText))
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

    public ObservableCollection<QueryTabViewModel> Tabs { get; private set; } = null!;

    [Notify]
    public QueryTabViewModel? SelectedTab { get; set; }

    [Notify]
    public bool IsExecuting { get; set; }

    [Notify]
    public QueryTabViewModel? CurrentExecutingTab { get; set; }

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
