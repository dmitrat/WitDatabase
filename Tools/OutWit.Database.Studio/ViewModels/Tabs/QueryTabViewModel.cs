using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Windows.Input;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.Locker;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Ui.Icons;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// Represents a query editor tab with its content and state.
/// </summary>
public class QueryTabViewModel : WorkspaceTabViewModel
{
    #region Fields

    private CancellationTokenSource? m_executionCts;

    #endregion

    #region Constructors

    public QueryTabViewModel(ApplicationViewModel applicationViewModel)
        : base(applicationViewModel)
    {
        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        ExecuteQueryCommand = new RelayCommandAsync(ExecuteQueryAsync);
        ExecuteSelectionCommand = new RelayCommandAsync(ExecuteSelectionAsync);
        StopQueryCommand = new RelayCommand(StopQuery);
        ClearResultsCommand = new RelayCommand(ClearResults);
        CopyRowsCommand = new RelayCommandAsync(CopyRowsAsync);
        CopyRowsAsInsertCommand = new RelayCommandAsync(CopyRowsAsInsertAsync);
        CopyRowsAsCsvCommand = new RelayCommandAsync(CopyRowsAsCsvAsync);
        CopyAllRowsCommand = new RelayCommandAsync(CopyAllRowsAsync);
        CopyAllRowsAsInsertCommand = new RelayCommandAsync(CopyAllRowsAsInsertAsync);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        Database.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    #endregion

    #region WorkspaceTabViewModel

    public override WorkspaceTabType TabType => WorkspaceTabType.Query;

    public override string IconPath => StudioIcons.PATH_QUERY_EXECUTE;

    public override string? UniqueId => FilePath;

    public override void OnClosed()
    {
        m_executionCts?.Cancel();
        m_executionCts?.Dispose();
        m_executionCts = null;
        
        ResultData?.Dispose();
        ResultData = null;
        CurrentView = null;
    }

    #endregion

    #region Query Execution

    private async Task ExecuteQueryAsync()
    {
        var sql = SqlText?.Trim();
        if (string.IsNullOrEmpty(sql))
            return;

        await ExecuteSqlAsync(sql);
    }

    private async Task ExecuteSelectionAsync()
    {
        var sql = !string.IsNullOrWhiteSpace(SelectedText) ? SelectedText.Trim() : SqlText?.Trim();
        if (string.IsNullOrEmpty(sql))
            return;

        await ExecuteSqlAsync(sql);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        if (!Database.IsConnected)
        {
            ErrorMessage = "Not connected to database";
            return;
        }

        IsExecuting = true;
        ErrorMessage = null;
        m_executionCts?.Dispose();
        m_executionCts = new CancellationTokenSource();

        try
        {
            var result = await Database.ExecuteQueryAsync(sql, m_executionCts.Token);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                ErrorMessage = result.ErrorMessage;
                SetResultData(null);
            }
            else
            {
                SetResultData(result.Data);
                RowsAffected = result.RowsAffected;
            }

            ExecutionTimeMs = result.ExecutionTimeMs;
            
            ApplicationVm.MainWindowVm.StatusText = string.IsNullOrEmpty(ErrorMessage)
                ? $"Query executed successfully in {ExecutionTimeMs:F2}ms"
                : $"Query failed: {ErrorMessage}";
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Query execution cancelled";
            ApplicationVm.MainWindowVm.StatusText = "Query cancelled";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Logger.LogError(ex, "Query execution failed");
            ApplicationVm.MainWindowVm.StatusText = $"Query failed: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
            UpdateStatus();
        }
    }

    private void StopQuery()
    {
        m_executionCts?.Cancel();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Sets the result data for display.
    /// </summary>
    public void SetResultData(DataTable? data)
    {
        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        ResultData = data;

        if (data == null || data.Rows.Count == 0)
        {
            TotalRowCount = 0;
            CurrentView = null;
            return;
        }

        TotalRowCount = data.Rows.Count;
        CurrentView = new DataView(data);

        UpdateStatus();
    }

    /// <summary>
    /// Clears all results.
    /// </summary>
    public void ClearResults()
    {
        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        ResultData?.Dispose();
        ResultData = null;
        CurrentView = null;
        TotalRowCount = 0;
        RowsAffected = 0;
        ExecutionTimeMs = 0;
        ErrorMessage = null;
        SelectedRows = null;

        UpdateStatus();
    }

    private async Task CopyRowsAsync()
    {
        if (!CanCopyRows)
            return;

        var rows = GetSelectedOrVisibleRows();
        var csv = Export.RowsToCsv(rows, ResultData!, includeHeaders: false);
        await SetClipboardTextAsync(csv);
    }

    private async Task CopyRowsAsCsvAsync()
    {
        if (!CanCopyRows)
            return;

        var rows = GetSelectedOrVisibleRows();
        var csv = Export.RowsToCsv(rows, ResultData!, includeHeaders: true);
        await SetClipboardTextAsync(csv);
    }

    private async Task CopyRowsAsInsertAsync()
    {
        if (!CanCopyRows)
            return;

        var rows = GetSelectedOrVisibleRows();
        var sql = Export.RowsToInsertStatements(rows, ResultData!, "TableName");
        await SetClipboardTextAsync(sql);
    }

    private async Task CopyAllRowsAsync()
    {
        if (!HasResults || ResultData == null)
            return;

        var csv = Export.ToCsv(ResultData, includeHeaders: true);
        await SetClipboardTextAsync(csv);
    }

    private async Task CopyAllRowsAsInsertAsync()
    {
        if (!HasResults || ResultData == null)
            return;

        var sql = Export.ToInsertStatements(ResultData, "TableName");
        await SetClipboardTextAsync(sql);
    }

    private async Task SetClipboardTextAsync(string text)
    {
        var mainWindow = ApplicationVm.MainWindow;
        if (mainWindow == null)
            return;

        var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private IEnumerable<DataRowView> GetSelectedOrVisibleRows()
    {
        if (SelectedRows != null && SelectedRows.Count > 0)
            return SelectedRows.Cast<DataRowView>();

        if (CurrentView != null)
            return CurrentView.Cast<DataRowView>();

        return [];
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        HasResults = TotalRowCount > 0;
        IsSuccess = string.IsNullOrEmpty(ErrorMessage);
        HasMessages = !string.IsNullOrEmpty(ErrorMessage) || RowsAffected > 0;
        CanExecuteQuery = Database.IsConnected && !IsExecuting;

        var selectedCount = SelectedRows?.Count ?? 0;
        CanCopyRows = HasResults && (selectedCount > 0 || CurrentView?.Count > 0);
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (GlobalLocker.IsLocked(nameof(QueryTabViewModel)))
            return;

        if (e.IsProperty((QueryTabViewModel vm) => vm.TotalRowCount))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.ErrorMessage))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.RowsAffected))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.CurrentView))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.SelectedRows))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.IsExecuting))
            UpdateStatus();
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        UpdateStatus();
    }

    #endregion

    #region Properties

    /// <summary>
    /// SQL text content of the query.
    /// </summary>
    [Notify]
    public string SqlText { get; set; } = string.Empty;

    /// <summary>
    /// Currently selected text in the SQL editor.
    /// </summary>
    [Notify]
    public string? SelectedText { get; set; }

    /// <summary>
    /// File path if the query is saved to a file.
    /// </summary>
    [Notify]
    public string? FilePath { get; set; }

    /// <summary>
    /// Full result data as DataTable.
    /// </summary>
    [Notify]
    public DataTable? ResultData { get; private set; }

    /// <summary>
    /// Current view for display (supports sorting).
    /// </summary>
    [Notify]
    public DataView? CurrentView { get; set; }

    /// <summary>
    /// Error message from query execution.
    /// </summary>
    [Notify]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of rows affected by the query.
    /// </summary>
    [Notify]
    public int RowsAffected { get; set; }

    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    [Notify]
    public double ExecutionTimeMs { get; set; }

    /// <summary>
    /// Total number of rows in the result set.
    /// </summary>
    [Notify]
    public int TotalRowCount { get; set; }

    /// <summary>
    /// Gets whether the tab has results to display.
    /// </summary>
    [Notify]
    public bool HasResults { get; private set; }

    /// <summary>
    /// Gets whether the query execution was successful.
    /// </summary>
    [Notify]
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Gets whether there are messages to display.
    /// </summary>
    [Notify]
    public bool HasMessages { get; private set; }

    /// <summary>
    /// Gets whether rows can be copied.
    /// </summary>
    [Notify]
    public bool CanCopyRows { get; private set; }

    /// <summary>
    /// Gets whether a query can be executed.
    /// </summary>
    [Notify]
    public bool CanExecuteQuery { get; private set; }

    /// <summary>
    /// Gets whether query is currently executing.
    /// </summary>
    [Notify]
    public bool IsExecuting { get; private set; }

    /// <summary>
    /// Currently selected rows in the DataGrid.
    /// </summary>
    [Notify]
    public IList? SelectedRows { get; set; }

    #endregion

    #region Commands

    public ICommand ExecuteQueryCommand { get; private set; } = null!;

    public ICommand ExecuteSelectionCommand { get; private set; } = null!;

    public ICommand StopQueryCommand { get; private set; } = null!;

    public ICommand ClearResultsCommand { get; private set; } = null!;

    public ICommand CopyRowsCommand { get; private set; } = null!;

    public ICommand CopyRowsAsCsvCommand { get; private set; } = null!;

    public ICommand CopyRowsAsInsertCommand { get; private set; } = null!;

    public ICommand CopyAllRowsCommand { get; private set; } = null!;

    public ICommand CopyAllRowsAsInsertCommand { get; private set; } = null!;

    #endregion

    #region Services

    private IDatabaseService Database => ApplicationVm.Database;

    private IExportService Export => ApplicationVm.Export;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
