using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Text;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using OutWit.Common.Aspects;
using OutWit.Common.Locker;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// Represents a query editor tab with its content and state.
/// </summary>
public class QueryTabViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    private const int DEFAULT_PAGE_SIZE = 100;
    private const string NULL_DISPLAY = "NULL";

    #endregion

    #region Constructors

    public QueryTabViewModel(ApplicationViewModel applicationViewModel)
        : base(applicationViewModel)
    {
        InitDefaults();
        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefaults()
    {
        PageSize = DEFAULT_PAGE_SIZE;
    }

    private void InitCommands()
    {
        FirstPageCommand = new RelayCommand(GoToFirstPage);
        PreviousPageCommand = new RelayCommand(GoToPreviousPage);
        NextPageCommand = new RelayCommand(GoToNextPage);
        LastPageCommand = new RelayCommand(GoToLastPage);
        
        CopyRowsCommand = new RelayCommandAsync(CopyRowsAsync);
        CopyRowsAsInsertCommand = new RelayCommandAsync(CopyRowsAsInsertAsync);
        CopyRowsAsCsvCommand = new RelayCommandAsync(CopyRowsAsCsvAsync);
        CopyAllRowsCommand = new RelayCommandAsync(CopyAllRowsAsync);
        CopyAllRowsAsInsertCommand = new RelayCommandAsync(CopyAllRowsAsInsertAsync);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Pagination Handlers

    private void GoToFirstPage()
    {
        if (!CanGoToPreviousPage) 
            return;

        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        CurrentPage = 1;
        ApplyPagination();
        UpdateStatus();
    }

    private void GoToPreviousPage()
    {
        if (!CanGoToPreviousPage) 
            return;

        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        CurrentPage--;
        ApplyPagination();
        UpdateStatus();
    }

    private void GoToNextPage()
    {
        if (!CanGoToNextPage)
            return;

        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        CurrentPage++;
        ApplyPagination();
        UpdateStatus();
    }

    private void GoToLastPage()
    {
        if (!CanGoToNextPage) 
            return;

        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        CurrentPage = TotalPages;
        ApplyPagination();
        UpdateStatus();
    }

    #endregion

    #region Copy Handlers

    private async Task CopyRowsAsync()
    {
        if (!CanCopyRows)
            return;

        var rows = GetSelectedOrVisibleRows();
        var csv = RowsToCsv(rows, includeHeaders: false);
        await SetClipboardTextAsync(csv);
    }

    private async Task CopyRowsAsCsvAsync()
    {
        if (!CanCopyRows)
            return;

        var rows = GetSelectedOrVisibleRows();
        var csv = RowsToCsv(rows, includeHeaders: true);
        await SetClipboardTextAsync(csv);
    }

    private async Task CopyRowsAsInsertAsync()
    {
        if (!CanCopyRows)
            return;

        var rows = GetSelectedOrVisibleRows();
        var sql = RowsToInsertStatements(rows);
        await SetClipboardTextAsync(sql);
    }

    private async Task CopyAllRowsAsync()
    {
        if (!HasResults || ResultData == null)
            return;

        var csv = RowsToCsv(ResultData.Rows.Cast<DataRow>().ToList(), includeHeaders: true);
        await SetClipboardTextAsync(csv);
    }

    private async Task CopyAllRowsAsInsertAsync()
    {
        if (!HasResults || ResultData == null)
            return;

        var sql = RowsToInsertStatements(ResultData.Rows.Cast<DataRow>().ToList());
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

    #endregion

    #region Functions

    /// <summary>
    /// Sets the full result data and applies pagination.
    /// </summary>
    public void SetResultData(DataTable? data)
    {
        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        ResultData = data;

        if (data == null || data.Rows.Count == 0)
        {
            TotalRowCount = 0;
            CurrentPage = 1;
            CurrentView = null;
            return;
        }

        TotalRowCount = data.Rows.Count;

        ResetPagination();
        UpdateStatus();
    }

    /// <summary>
    /// Applies pagination to show the current page of results.
    /// </summary>
    private void ApplyPagination()
    {
        if (ResultData == null || ResultData.Rows.Count == 0)
        {
            CurrentView = null;
            return;
        }

        // Create a DataView for the current page
        var view = new DataView(ResultData);
        
        // We'll handle pagination in the grid by showing all and letting user scroll
        // For very large datasets, we could implement virtual scrolling
        CurrentView = view;
    }

    /// <summary>
    /// Clears all results and resets pagination.
    /// </summary>
    public void ClearResults()
    {
        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        ResultData?.Dispose();
        ResultData = null;
        CurrentView = null;
        TotalRowCount = 0;
        CurrentPage = 1;
        RowsAffected = 0;
        ExecutionTimeMs = 0;
        ErrorMessage = null;
        SelectedRows = null;

        UpdateStatus();
    }

    private IReadOnlyList<DataRow> GetSelectedOrVisibleRows()
    {
        if (SelectedRows != null && SelectedRows.Count > 0)
            return SelectedRows.Cast<DataRowView>().Select(rv => rv.Row).ToList();

        if (CurrentView != null)
            return CurrentView.Cast<DataRowView>().Select(rv => rv.Row).ToList();

        return [];
    }

    private void ResetPagination()
    {
        using var locker = GlobalLocker.Lock(nameof(QueryTabViewModel));

        CurrentPage = 1;
        ApplyPagination();
    }

    #endregion

    #region Export Functions

    private string RowsToCsv(IReadOnlyList<DataRow> rows, bool includeHeaders)
    {
        if (rows.Count == 0 || ResultData == null)
            return string.Empty;

        var sb = new StringBuilder();

        // Headers
        if (includeHeaders)
        {
            var headers = ResultData.Columns.Cast<DataColumn>().Select(c => EscapeCsvField(c.ColumnName));
            sb.AppendLine(string.Join(",", headers));
        }

        // Rows
        foreach (var row in rows)
        {
            var values = row.ItemArray.Select(v => EscapeCsvField(FormatValue(v)));
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }

    private string RowsToInsertStatements(IReadOnlyList<DataRow> rows)
    {
        if (rows.Count == 0 || ResultData == null)
            return string.Empty;

        var sb = new StringBuilder();
        var tableName = "TableName";
        var columns = string.Join(", ", ResultData.Columns.Cast<DataColumn>().Select(c => c.ColumnName));

        foreach (var row in rows)
        {
            var values = new List<string>();
            for (var i = 0; i < ResultData.Columns.Count; i++)
            {
                values.Add(FormatSqlValue(row[i], ResultData.Columns[i].DataType));
            }
            sb.AppendLine($"INSERT INTO {tableName} ({columns}) VALUES ({string.Join(", ", values)});");
        }

        return sb.ToString();
    }

    private static string FormatValue(object? value)
    {
        if (value == null || value == DBNull.Value)
            return string.Empty;

        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            TimeOnly t => t.ToString("HH:mm:ss"),
            byte[] bytes => bytes.Length <= 32
                ? $"0x{BitConverter.ToString(bytes).Replace("-", "")}"
                : $"0x{BitConverter.ToString(bytes, 0, 32).Replace("-", "")}...",
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    private static string FormatSqlValue(object? value, Type dataType)
    {
        if (value == null || value == DBNull.Value)
            return NULL_DISPLAY;

        // Numbers don't need quotes
        if (IsNumericType(dataType))
            return value.ToString() ?? NULL_DISPLAY;

        // Boolean
        if (dataType == typeof(bool))
            return ((bool)value) ? "TRUE" : "FALSE";

        // Escape single quotes and wrap in quotes
        var str = FormatValue(value);
        return $"'{str.Replace("'", "''")}'";
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        TotalPages = TotalRowCount > 0 
            ? (int)Math.Ceiling((double)TotalRowCount / PageSize) : 1;

        CanGoToPreviousPage = CurrentPage > 1;
        CanGoToNextPage = CurrentPage < TotalPages;

        DisplayedRowStart = TotalRowCount > 0 
            ? (CurrentPage - 1) * PageSize + 1
            : 0;

        DisplayedRowEnd = TotalRowCount > 0 
            ? Math.Min(CurrentPage * PageSize, TotalRowCount) 
            : 0;

        HasResults = TotalRowCount > 0;
        IsSuccess = string.IsNullOrEmpty(ErrorMessage);
        HasMessages = !string.IsNullOrEmpty(ErrorMessage) || RowsAffected > 0;

        DisplayTitle = IsModified ? $"{Title} *" : Title;
        
        var selectedCount = SelectedRows?.Count ?? 0;
        CanCopyRows = HasResults && (selectedCount > 0 || CurrentView?.Count > 0);
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(GlobalLocker.IsLocked(nameof(QueryTabViewModel)))
            return;

        if (e.IsProperty((QueryTabViewModel vm)=>vm.CurrentPage))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.TotalRowCount))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.ErrorMessage))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.RowsAffected))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.IsModified))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.Title))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.CurrentView))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.SelectedRows))
            UpdateStatus();

        if (e.IsProperty((QueryTabViewModel vm) => vm.PageSize))
        {
            ResetPagination();
            UpdateStatus();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Display title of the tab.
    /// </summary>
    [Notify]
    public string Title { get; set; } = "New Query";

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
    /// Indicates if the tab has unsaved changes.
    /// </summary>
    [Notify]
    public bool IsModified { get; set; }

    /// <summary>
    /// Gets the display title with modification indicator.
    /// </summary>
    [Notify]
    public string DisplayTitle { get; private set; } = "";

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
    /// Currently selected rows in the DataGrid.
    /// </summary>
    [Notify]
    public IList? SelectedRows { get; set; }

    #endregion

    #region Pagination Properties

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    [Notify]
    public int CurrentPage { get; set; }

    /// <summary>
    /// Number of rows per page.
    /// </summary>
    [Notify]
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of rows in the result set.
    /// </summary>
    [Notify]
    public int TotalRowCount { get; set; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    [Notify]
    public int TotalPages { get; private set; }

    /// <summary>
    /// First row number displayed (1-based).
    /// </summary>
    [Notify]
    public int DisplayedRowStart { get; private set; }

    /// <summary>
    /// Last row number displayed (1-based).
    /// </summary>
    [Notify]
    public int DisplayedRowEnd { get; private set; }

    /// <summary>
    /// Whether navigation to previous page is available.
    /// </summary>
    [Notify]
    public bool CanGoToPreviousPage { get; private set; }

    /// <summary>
    /// Whether navigation to next page is available.
    /// </summary>
    [Notify]
    public bool CanGoToNextPage { get; private set; }

    #endregion

    #region Commands

    public ICommand FirstPageCommand { get; private set; } = null!;

    public ICommand PreviousPageCommand { get; private set; } = null!;

    public ICommand NextPageCommand { get; private set; } = null!;

    public ICommand LastPageCommand { get; private set; } = null!;
    
    public ICommand CopyRowsCommand { get; private set; } = null!;
    
    public ICommand CopyRowsAsCsvCommand { get; private set; } = null!;
    
    public ICommand CopyRowsAsInsertCommand { get; private set; } = null!;
    
    public ICommand CopyAllRowsCommand { get; private set; } = null!;
    
    public ICommand CopyAllRowsAsInsertCommand { get; private set; } = null!;

    #endregion
}
