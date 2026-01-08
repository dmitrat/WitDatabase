using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for managing multiple query editor tabs.
/// </summary>
public class QueryTabsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ILogger<QueryTabsViewModel> m_logger;
    private int m_nextQueryNumber = 1;

    #endregion

    #region Constructors

    public QueryTabsViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryTabsViewModel>.Instance;

        InitDefault();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Tabs = new ObservableCollection<QueryTab>();
        
        // Create initial tab
        AddNewTab();
    }

    private void InitCommands()
    {
        NewTabCommand = new RelayCommand(_ => AddNewTab());
        CloseTabCommand = new RelayCommand<QueryTab>(CloseTab, CanCloseTab);
        CloseAllTabsCommand = new RelayCommand<object>(_ => CloseAllTabs(), _ => CanCloseAllTabs());
        CloseOtherTabsCommand = new RelayCommand<QueryTab>(CloseOtherTabs, CanCloseOtherTabs);
        SaveTabCommand = new RelayCommand<QueryTab>(async tab => await SaveTabAsync(tab), CanSaveTab);
        SaveTabAsCommand = new RelayCommand<QueryTab>(async tab => await SaveTabAsAsync(tab), CanSaveTab);
        ExecuteQueryCommand = new RelayCommand<QueryTab>(async tab => await ExecuteQueryAsync(tab), CanExecuteQuery);
        ClearResultsCommand = new RelayCommand<QueryTab>(ClearResults, tab => tab != null);
    }

    #endregion

    #region Commands

    public ICommand NewTabCommand { get; private set; } = null!;
    public ICommand CloseTabCommand { get; private set; } = null!;
    public ICommand CloseAllTabsCommand { get; private set; } = null!;
    public ICommand CloseOtherTabsCommand { get; private set; } = null!;
    public ICommand SaveTabCommand { get; private set; } = null!;
    public ICommand SaveTabAsCommand { get; private set; } = null!;
    public ICommand ExecuteQueryCommand { get; private set; } = null!;
    public ICommand ClearResultsCommand { get; private set; } = null!;

    private void AddNewTab()
    {
        var tab = new QueryTab
        {
            Title = $"Query {m_nextQueryNumber++}",
            SqlText = string.Empty
        };

        Tabs.Add(tab);
        SelectedTab = tab;

        m_logger.LogInformation("Created new query tab: {Title}", tab.Title);
    }

    private void CloseTab(QueryTab? tab)
    {
        if (tab == null)
            return;

        // Check for unsaved changes
        if (tab.IsModified)
        {
            // TODO: Show confirmation dialog
            // For now, just close
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        // Select another tab
        if (Tabs.Count > 0)
        {
            if (index >= Tabs.Count)
                index = Tabs.Count - 1;
            
            SelectedTab = Tabs[index];
        }
        else
        {
            // Always keep at least one tab
            AddNewTab();
        }

        m_logger.LogInformation("Closed query tab: {Title}", tab.Title);
    }

    private bool CanCloseTab(QueryTab? tab)
    {
        return tab != null && Tabs.Count > 1;
    }

    private void CloseAllTabs()
    {
        // Check for unsaved changes
        var modifiedTabs = Tabs.Where(t => t.IsModified).ToList();
        if (modifiedTabs.Count > 0)
        {
            // TODO: Show confirmation dialog
        }

        Tabs.Clear();
        AddNewTab();

        m_logger.LogInformation("Closed all query tabs");
    }

    private bool CanCloseAllTabs()
    {
        return Tabs.Count > 0;
    }

    private void CloseOtherTabs(QueryTab? tab)
    {
        if (tab == null)
            return;

        // Check for unsaved changes in other tabs
        var modifiedTabs = Tabs.Where(t => t != tab && t.IsModified).ToList();
        if (modifiedTabs.Count > 0)
        {
            // TODO: Show confirmation dialog
        }

        var tabsToRemove = Tabs.Where(t => t != tab).ToList();
        foreach (var t in tabsToRemove)
        {
            Tabs.Remove(t);
        }

        SelectedTab = tab;

        m_logger.LogInformation("Closed other query tabs, kept: {Title}", tab.Title);
    }

    private bool CanCloseOtherTabs(QueryTab? tab)
    {
        return tab != null && Tabs.Count > 1;
    }

    private async Task SaveTabAsync(QueryTab? tab)
    {
        if (tab == null)
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
            OnPropertyChanged(nameof(Tabs));

            ApplicationVm.MainWindowVm.StatusText = $"Saved: {tab.FilePath}";
            m_logger.LogInformation("Saved query tab: {FilePath}", tab.FilePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = $"Error saving file: {ex.Message}";
            m_logger.LogError(ex, "Failed to save query tab: {FilePath}", tab.FilePath);
        }
    }

    private async Task SaveTabAsAsync(QueryTab? tab)
    {
        if (tab == null)
            return;

        // TODO: Show save file dialog
        // For now, just log
        m_logger.LogInformation("Save As requested for tab: {Title}", tab.Title);
        
        await Task.CompletedTask;
    }

    private bool CanSaveTab(QueryTab? tab)
    {
        return tab != null && !string.IsNullOrWhiteSpace(tab.SqlText);
    }

    private async Task ExecuteQueryAsync(QueryTab? tab)
    {
        if (tab == null || string.IsNullOrWhiteSpace(tab.SqlText))
            return;

        IsExecuting = true;
        CurrentExecutingTab = tab;
        tab.ErrorMessage = null;
        tab.ResultDataView = null;

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await m_databaseService.ExecuteQueryAsync(tab.SqlText);
            stopwatch.Stop();

            if (result.IsSuccess)
            {
                // Convert DataTable to DataView for binding
                if (result.ResultTable != null)
                {
                    tab.ResultDataView = result.ResultTable.DefaultView;
                }

                tab.RowsAffected = result.RowsAffected;
                tab.ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds;
                
                ApplicationVm.MainWindowVm.StatusText = 
                    $"Query executed in {tab.ExecutionTimeMs:F2}ms. {tab.RowsAffected} rows affected.";

                m_logger.LogInformation("Query executed successfully: {Time}ms, {Rows} rows",
                    tab.ExecutionTimeMs, tab.RowsAffected);
            }
            else
            {
                tab.ErrorMessage = result.ErrorMessage;
                ApplicationVm.MainWindowVm.StatusText = "Query execution failed";
                m_logger.LogWarning("Query execution failed: {Error}", result.ErrorMessage);
            }

            OnPropertyChanged(nameof(Tabs));
        }
        catch (Exception ex)
        {
            tab.ErrorMessage = $"Execution error: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Query execution error";
            m_logger.LogError(ex, "Query execution error");
        }
        finally
        {
            IsExecuting = false;
            CurrentExecutingTab = null;
        }
    }

    private bool CanExecuteQuery(QueryTab? tab)
    {
        return tab != null 
            && !string.IsNullOrWhiteSpace(tab.SqlText) 
            && !IsExecuting 
            && m_databaseService.IsConnected;
    }

    private void ClearResults(QueryTab? tab)
    {
        if (tab == null)
            return;

        tab.ResultDataView = null;
        tab.ErrorMessage = null;
        tab.RowsAffected = 0;
        tab.ExecutionTimeMs = 0;

        OnPropertyChanged(nameof(Tabs));
        ApplicationVm.MainWindowVm.StatusText = "Results cleared";
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Opens a query from a file.
    /// </summary>
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

            Tabs.Add(tab);
            SelectedTab = tab;

            ApplicationVm.MainWindowVm.StatusText = $"Opened: {filePath}";
            m_logger.LogInformation("Opened query file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ApplicationVm.MainWindowVm.StatusText = $"Error opening file: {ex.Message}";
            m_logger.LogError(ex, "Failed to open query file: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Marks the current tab as modified.
    /// </summary>
    public void MarkCurrentTabAsModified()
    {
        if (SelectedTab != null)
        {
            SelectedTab.IsModified = true;
            OnPropertyChanged(nameof(Tabs));
        }
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

    /// <summary>
    /// Gets the SQL text of the currently selected tab.
    /// </summary>
    public string CurrentSqlText
    {
        get => SelectedTab?.SqlText ?? string.Empty;
        set
        {
            if (SelectedTab != null)
            {
                SelectedTab.SqlText = value;
                SelectedTab.IsModified = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Tabs));
            }
        }
    }

    #endregion
}
