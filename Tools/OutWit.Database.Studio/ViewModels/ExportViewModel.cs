using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Text;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// Export format options.
/// </summary>
public enum ExportFormat
{
    Csv,
    Json,
    Sql,

    /// <summary>
    /// A Markdown table. Added in stage 9 because it is what the design offers and because it is what
    /// people actually paste into an issue - the one format here that is read rather than loaded.
    /// </summary>
    Markdown
}

/// <summary>
/// What is exported (WS-51). <b>The scope is chosen first</b>, because it is the only thing that
/// differs between exporting twelve selected rows and exporting a whole table, and asking for it last
/// is what makes someone export the wrong thing twice.
/// </summary>
public enum ExportScope
{
    /// <summary>The rows selected in the grid.</summary>
    Selection,

    /// <summary>The page on screen - which is what the grid actually holds.</summary>
    Page,

    /// <summary>
    /// Every row. For a table that is a new query; for a query result it is the page, because a result
    /// set has no "rest" to fetch - the statement would have to be run again.
    /// </summary>
    Everything
}

/// <summary>
/// Which scope is chosen, for the markup. One converter per value rather than one taking a parameter,
/// for the reason given on <c>SettingsSection</c>: with compiled bindings a ConverterParameter is an
/// untyped string nothing checks, and a renamed value would leave a radio button that never lights up
/// and no build error anywhere.
/// </summary>
public static class ExportScopes
{
    public static readonly Avalonia.Data.Converters.IValueConverter IsSelection =
        new Avalonia.Data.Converters.FuncValueConverter<ExportScope, bool>(scope => scope == ExportScope.Selection);

    public static readonly Avalonia.Data.Converters.IValueConverter IsPage =
        new Avalonia.Data.Converters.FuncValueConverter<ExportScope, bool>(scope => scope == ExportScope.Page);

    public static readonly Avalonia.Data.Converters.IValueConverter IsEverything =
        new Avalonia.Data.Converters.FuncValueConverter<ExportScope, bool>(scope => scope == ExportScope.Everything);
}

/// <summary>
/// ViewModel for export dialog.
/// </summary>
public class ExportViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    private const int PROGRESS_UPDATE_INTERVAL = 100;

    #endregion

    #region Events

    public event Action<bool>? DialogClosed;

    #endregion

    #region Fields

    private CancellationTokenSource? m_exportCts;

    #endregion

    #region Constructors

    public ExportViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefaults();
        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefaults()
    {
        AvailableTables = [];
        SelectedFormat = ExportFormat.Csv;
        IncludeHeaders = true;
        FormatDatesAsIso = true;
        OutputPath = string.Empty;
    }

    private void InitCommands()
    {
        BrowseCommand = new RelayCommandAsync(BrowseAsync);
        ExportCommand = new RelayCommandAsync(ExportAsync);
        CancelCommand = new RelayCommand(Cancel);
        CancelExportCommand = new RelayCommand(CancelExport);
        ChooseScopeCommand = new RelayCommand<string>(scope =>
            SelectedScope = Enum.TryParse<ExportScope>(scope, out var parsed) ? parsed : SelectedScope);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Initializes the dialog with available tables.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (Database?.IsConnected != true)
            return;

        try
        {
            var tables = await Database.GetTablesAsync();
            AvailableTables = new ObservableCollection<string>(tables.Select(t => t.Name));

            if (AvailableTables.Count > 0)
                SelectedTable = AvailableTables[0];
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load tables for export");
        }
    }

    /// <summary>
    /// Sets the data to export directly (for exporting query results).
    /// </summary>
    public void SetDataSource(DataTable data, string sourceName)
    {
        SetDataSource(data, sourceName, selection: null, rowsInSource: data.Rows.Count);
    }

    /// <summary>
    /// The grid's three answers at once (WS-51): what is selected, what is on the page, and how many
    /// rows there are altogether.
    ///
    /// <para>
    /// <paramref name="rowsInSource"/> is what the table HAS, which is not what the page holds - the
    /// grid pages server-side since stage 7. Passing the page count for it would make "All" a lie in
    /// the one place a person checks before pressing Export.
    /// </para>
    /// </summary>
    public void SetDataSource(DataTable data, string sourceName, IReadOnlyList<DataRowView>? selection,
        int rowsInSource)
    {
        DataToExport = data;
        SourceName = sourceName;
        IsQueryResult = true;
        Selection = selection ?? [];
        RowsInSource = rowsInSource < 0 ? data.Rows.Count : rowsInSource;
        TotalRows = RowsInSource;

        // The scope starts on whatever the user has actually got: a selection if there is one, the
        // page otherwise. Starting on "everything" is how an export of one row becomes an export of
        // four million.
        SelectedScope = Selection.Count > 0 ? ExportScope.Selection : ExportScope.Page;

        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(EverythingCount));
        OnPropertyChanged(nameof(CanExportSelection));
    }

    /// <summary>
    /// The rows the chosen scope covers, taken from what the grid handed over. <c>Everything</c> for a
    /// TABLE is answered by a new query in <c>ExportAsync</c>; here it is the page, which is all a
    /// query result has.
    /// </summary>
    private DataTable RowsForScope(DataTable page)
    {
        if (SelectedScope != ExportScope.Selection || Selection.Count == 0)
            return page;

        var selected = page.Clone();

        foreach (var row in Selection)
            selected.ImportRow(row.Row);

        return selected;
    }

    private async Task BrowseAsync()
    {
        var extension = SelectedFormat switch
        {
            ExportFormat.Csv => "csv",
            ExportFormat.Json => "json",
            ExportFormat.Sql => "sql",
            _ => "txt"
        };

        var defaultName = !string.IsNullOrEmpty(SelectedTable) 
            ? $"{SelectedTable}.{extension}" 
            : $"export.{extension}";

        var filePath = await ApplicationVm.Dialogs.SaveFileAsync(
            "Export to file",
            suggestedFileName: defaultName,
            defaultExtension: extension,
            filters:
            [
                new FileFilter($"{extension.ToUpper()} Files", [$"*.{extension}"]),
                new FileFilter("All Files", ["*.*"])
            ]);

        if (!string.IsNullOrEmpty(filePath))
            OutputPath = filePath;
    }

    private async Task ExportAsync()
    {
        if (!CanExport)
            return;

        IsExporting = true;
        ErrorMessage = null;
        ExportProgress = 0;
        RowsExported = 0;

        m_exportCts?.Dispose();
        m_exportCts = new CancellationTokenSource();
        var ct = m_exportCts.Token;

        try
        {
            DataTable dataToExport;

            if (IsQueryResult && DataToExport != null)
            {
                dataToExport = RowsForScope(DataToExport);
            }
            else if (!string.IsNullOrEmpty(SelectedTable))
            {
                var session = Database;

                if (session?.IsConnected != true)
                {
                    ErrorMessage = "Not connected to a database";
                    return;
                }

                var result = await session.ExecuteQueryAsync($"SELECT * FROM [{SelectedTable}]", ct);
                if (result.Data == null)
                {
                    ErrorMessage = "Failed to load table data";
                    return;
                }
                dataToExport = result.Data;
            }
            else
            {
                ErrorMessage = "No data source selected";
                return;
            }

            TotalRows = dataToExport.Rows.Count;

            var tableName = IsQueryResult ? SourceName ?? "QueryResult" : SelectedTable ?? "Table";

            switch (SelectedFormat)
            {
                case ExportFormat.Csv:
                    await ExportToCsvWithProgressAsync(dataToExport, OutputPath, ct);
                    break;
                case ExportFormat.Json:
                    await ExportToJsonWithProgressAsync(dataToExport, OutputPath, ct);
                    break;
                case ExportFormat.Sql:
                    await ExportToSqlWithProgressAsync(dataToExport, tableName, OutputPath, ct);
                    break;
                case ExportFormat.Markdown:
                    await ExportToMarkdownAsync(dataToExport, OutputPath, ct);
                    break;
            }

            if (!ct.IsCancellationRequested)
            {
                ApplicationVm.MainWindowVm.StatusText = $"Exported {RowsExported} rows to {Path.GetFileName(OutputPath)}";

                ApplicationVm.Notifications.Information(
                    $"Exported {RowsExported} rows",
                    OutputPath,
                    ApplicationVm.ActiveSession?.DisplayName);
                Logger.LogInformation("Exported {RowCount} rows to {FilePath}", RowsExported, OutputPath);
                DialogClosed?.Invoke(true);
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = $"Export cancelled after {RowsExported} rows.";
            Logger.LogInformation("Export cancelled after {RowCount} rows", RowsExported);
            
            // Delete partial file
            if (File.Exists(OutputPath))
            {
                try { File.Delete(OutputPath); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
            Logger.LogError(ex, "Export failed");
        }
        finally
        {
            IsExporting = false;
        }
    }

    private async Task ExportToCsvWithProgressAsync(DataTable data, string filePath, CancellationToken ct)
    {
        await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        if (IncludeHeaders)
        {
            var headers = data.Columns.Cast<DataColumn>()
                .Select(c => EscapeCsvField(c.ColumnName));
            await writer.WriteLineAsync(string.Join(",", headers));
        }

        var rowIndex = 0;
        foreach (DataRow row in data.Rows)
        {
            ct.ThrowIfCancellationRequested();
            
            var values = row.ItemArray.Select(v => EscapeCsvField(FormatValue(v)));
            await writer.WriteLineAsync(string.Join(",", values));
            
            rowIndex++;
            RowsExported = rowIndex;
            
            if (rowIndex % PROGRESS_UPDATE_INTERVAL == 0)
            {
                ExportProgress = (double)rowIndex / data.Rows.Count * 100;
            }
        }
        
        ExportProgress = 100;
    }

    private async Task ExportToJsonWithProgressAsync(DataTable data, string filePath, CancellationToken ct)
    {
        await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        await writer.WriteLineAsync("[");

        var rowIndex = 0;
        foreach (DataRow row in data.Rows)
        {
            ct.ThrowIfCancellationRequested();
            
            var sb = new StringBuilder("  {");
            var columnIndex = 0;
            
            foreach (DataColumn column in data.Columns)
            {
                var value = row[column];
                var jsonValue = FormatJsonValue(value);
                sb.Append($"\"{column.ColumnName}\": {jsonValue}");
                
                if (columnIndex < data.Columns.Count - 1)
                    sb.Append(", ");
                columnIndex++;
            }
            
            sb.Append('}');
            if (rowIndex < data.Rows.Count - 1)
                sb.Append(',');
            
            await writer.WriteLineAsync(sb.ToString());
            
            rowIndex++;
            RowsExported = rowIndex;
            
            if (rowIndex % PROGRESS_UPDATE_INTERVAL == 0)
            {
                ExportProgress = (double)rowIndex / data.Rows.Count * 100;
            }
        }

        await writer.WriteLineAsync("]");
        ExportProgress = 100;
    }

    private async Task ExportToSqlWithProgressAsync(DataTable data, string tableName, string filePath, CancellationToken ct)
    {
        await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        var columns = string.Join(", ", data.Columns.Cast<DataColumn>()
            .Select(c => $"[{c.ColumnName.Replace("]", "]]")}]"));

        var rowIndex = 0;
        foreach (DataRow row in data.Rows)
        {
            ct.ThrowIfCancellationRequested();
            
            var values = new List<string>();
            for (var i = 0; i < data.Columns.Count; i++)
            {
                values.Add(FormatSqlValue(row[i], data.Columns[i].DataType));
            }
            
            await writer.WriteLineAsync($"INSERT INTO [{tableName}] ({columns}) VALUES ({string.Join(", ", values)});");
            
            rowIndex++;
            RowsExported = rowIndex;
            
            if (rowIndex % PROGRESS_UPDATE_INTERVAL == 0)
            {
                ExportProgress = (double)rowIndex / data.Rows.Count * 100;
            }
        }
        
        ExportProgress = 100;
    }

    private static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";

        return field;
    }

    /// <summary>
    /// A Markdown table (WS-51). The one format here meant to be READ rather than loaded, so a pipe
    /// inside a value is escaped and a newline becomes a space - a broken table is worse than a value
    /// that lost its line break, and anyone who needs the value exactly has three other formats.
    /// </summary>
    private async Task ExportToMarkdownAsync(DataTable data, string path, CancellationToken ct)
    {
        var text = new StringBuilder();

        var headers = data.Columns.Cast<DataColumn>().Select(column => Cell(column.ColumnName)).ToList();

        text.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
        text.Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).AppendLine(" |");

        foreach (DataRow row in data.Rows)
        {
            ct.ThrowIfCancellationRequested();

            text.Append("| ")
                .Append(string.Join(" | ", row.ItemArray.Select(value => Cell(FormatValue(value)))))
                .AppendLine(" |");

            RowsExported++;
        }

        await File.WriteAllTextAsync(path, text.ToString(), ct);

        return;

        static string Cell(string? value)
        {
            return (value ?? string.Empty)
                .Replace("|", "\\|", StringComparison.Ordinal)
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
        }
    }

    private string FormatValue(object? value)
    {
        if (value == null || value == DBNull.Value)
            return string.Empty;

        return value switch
        {
            DateTime dt => FormatDatesAsIso ? dt.ToString("yyyy-MM-ddTHH:mm:ss") : dt.ToString("yyyy-MM-dd HH:mm:ss"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            TimeOnly t => t.ToString("HH:mm:ss"),
            DateTimeOffset dto => FormatDatesAsIso ? dto.ToString("yyyy-MM-ddTHH:mm:sszzz") : dto.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            bool b => b ? "true" : "false",
            byte[] bytes => $"0x{BitConverter.ToString(bytes).Replace("-", "")}",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string FormatSqlValue(object? value, Type dataType)
    {
        if (value == null || value == DBNull.Value)
            return "NULL";

        if (IsNumericType(dataType))
            return value.ToString() ?? "NULL";

        if (dataType == typeof(bool))
            return (bool)value ? "TRUE" : "FALSE";

        if (value is byte[] bytes)
            return $"X'{BitConverter.ToString(bytes).Replace("-", "")}'";

        if (value is Guid guid)
            return $"'{guid}'";

        var str = value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            TimeOnly t => t.ToString("HH:mm:ss"),
            _ => value.ToString() ?? string.Empty
        };

        return $"'{str.Replace("'", "''")}'";
    }

    private static string FormatJsonValue(object? value)
    {
        if (value == null || value == DBNull.Value)
            return "null";

        return value switch
        {
            string str => $"\"{EscapeJsonString(str)}\"",
            bool b => b ? "true" : "false",
            DateTime dt => $"\"{dt:yyyy-MM-ddTHH:mm:ss}\"",
            DateOnly d => $"\"{d:yyyy-MM-dd}\"",
            TimeOnly t => $"\"{t:HH:mm:ss}\"",
            byte[] bytes => $"\"{Convert.ToBase64String(bytes)}\"",
            Guid guid => $"\"{guid}\"",
            int or long or short or byte or sbyte or uint or ulong or ushort => value.ToString() ?? "null",
            float or double or decimal => value.ToString() ?? "null",
            _ => $"\"{EscapeJsonString(value.ToString() ?? string.Empty)}\""
        };
    }

    private static string EscapeJsonString(string str)
    {
        return str
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
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

    private void Cancel()
    {
        DialogClosed?.Invoke(false);
    }

    private void CancelExport()
    {
        m_exportCts?.Cancel();
    }

    private void UpdateStatus()
    {
        var hasSource = IsQueryResult ? DataToExport != null : !string.IsNullOrEmpty(SelectedTable);
        var hasPath = !string.IsNullOrEmpty(OutputPath);
        CanExport = hasSource && hasPath && !IsExporting;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((ExportViewModel vm) => vm.SelectedTable) ||
            e.IsProperty((ExportViewModel vm) => vm.OutputPath) ||
            e.IsProperty((ExportViewModel vm) => vm.IsExporting) ||
            e.IsProperty((ExportViewModel vm) => vm.DataToExport))
        {
            UpdateStatus();
        }

        // Update suggested file extension when format changes
        if (e.IsProperty((ExportViewModel vm) => vm.SelectedFormat) && !string.IsNullOrEmpty(OutputPath))
        {
            var extension = SelectedFormat switch
            {
                ExportFormat.Csv => ".csv",
                ExportFormat.Json => ".json",
                ExportFormat.Sql => ".sql",
                _ => ".txt"
            };
            OutputPath = Path.ChangeExtension(OutputPath, extension);
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Available tables for export.
    /// </summary>
    [Notify]
    public ObservableCollection<string> AvailableTables { get; private set; } = null!;

    /// <summary>
    /// Selected table to export.
    /// </summary>
    [Notify]
    public string? SelectedTable { get; set; }

    /// <summary>
    /// Selected export format.
    /// </summary>
    [Notify]
    public ExportFormat SelectedFormat { get; set; }

    /// <summary>What is exported (WS-51). Chosen first, and started on what the user has.</summary>
    [Notify]
    public ExportScope SelectedScope { get; set; } = ExportScope.Page;

    /// <summary>Picks the scope from the markup.</summary>
    public ICommand ChooseScopeCommand { get; private set; } = null!;

    /// <summary>The rows the grid handed over as selected.</summary>
    public IReadOnlyList<DataRowView> Selection { get; private set; } = [];

    /// <summary>How many rows the SOURCE has - not how many the page holds.</summary>
    public int RowsInSource { get; private set; }

    public int SelectionCount => Selection.Count;

    public int PageCount => DataToExport?.Rows.Count ?? 0;

    public int EverythingCount => RowsInSource;

    /// <summary>
    /// Whether "Selection" can be chosen at all. An empty selection offered as a scope is a button
    /// that writes an empty file.
    /// </summary>
    public bool CanExportSelection => Selection.Count > 0;

    /// <summary>
    /// Whether to include column headers (CSV only).
    /// </summary>
    [Notify]
    public bool IncludeHeaders { get; set; }

    /// <summary>
    /// Whether to format dates as ISO 8601.
    /// </summary>
    [Notify]
    public bool FormatDatesAsIso { get; set; }

    /// <summary>
    /// Output file path.
    /// </summary>
    [Notify]
    public string OutputPath { get; set; } = null!;

    /// <summary>
    /// Whether export is in progress.
    /// </summary>
    [Notify]
    public bool IsExporting { get; private set; }

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    [Notify]
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Whether export can be performed.
    /// </summary>
    [Notify]
    public bool CanExport { get; private set; }

    /// <summary>
    /// Number of rows exported.
    /// </summary>
    [Notify]
    public int RowsExported { get; private set; }

    /// <summary>
    /// Total number of rows to export (for progress tracking).
    /// </summary>
    [Notify]
    public int TotalRows { get; private set; }

    /// <summary>
    /// Export progress percentage (0 to 100).
    /// </summary>
    [Notify]
    public double ExportProgress { get; private set; }

    /// <summary>
    /// Data to export (for query results).
    /// </summary>
    [Notify]
    public DataTable? DataToExport { get; private set; }

    /// <summary>
    /// Source name (table name or "Query Result").
    /// </summary>
    [Notify]
    public string? SourceName { get; private set; }

    /// <summary>
    /// Whether exporting query results (vs table).
    /// </summary>
    [Notify]
    public bool IsQueryResult { get; private set; }

    #endregion

    #region Commands

    public ICommand BrowseCommand { get; private set; } = null!;

    public ICommand ExportCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand CancelExportCommand { get; private set; } = null!;

    #endregion

    #region Services

    /// <summary>
    /// The active connection - the one selected in the tree.
    /// </summary>
    private IDatabaseSession? Database => ApplicationVm.ActiveSession;

    private IExportService Export => ApplicationVm.Export;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
