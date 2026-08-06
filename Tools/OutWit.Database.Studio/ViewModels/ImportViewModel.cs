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
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// Import format options.
/// </summary>
public enum ImportFormat
{
    Csv,
    Json
}

/// <summary>
/// ViewModel for import dialog.
/// </summary>
public class ImportViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    private const int PREVIEW_ROW_LIMIT = 100;
    private const int BATCH_SIZE = 100;
    private const int MAX_ERRORS_TO_SHOW = 10;

    #endregion

    #region Events

    public event Action<bool>? DialogClosed;

    #endregion

    #region Fields

    private CancellationTokenSource? m_importCts;

    #endregion

    #region Constructors

    public ImportViewModel(ApplicationViewModel applicationVm)
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
        ColumnMappings = [];
        SelectedFormat = ImportFormat.Csv;
        HasHeaders = true;
        Delimiter = ",";
        ContinueOnError = false;
    }

    private void InitCommands()
    {
        BrowseCommand = new RelayCommandAsync(BrowseAsync);
        PreviewCommand = new RelayCommandAsync(PreviewAsync);
        ImportCommand = new RelayCommandAsync(ImportAsync);
        CancelCommand = new RelayCommand(Cancel);
        CancelImportCommand = new RelayCommand(CancelImport);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Functions

    public async Task InitializeAsync()
    {
        if (!Database.IsConnected)
            return;

        try
        {
            var tables = await Database.GetTablesAsync();
            AvailableTables = new ObservableCollection<string>(tables.Select(t => t.Name));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load tables for import");
        }
    }

    private async Task BrowseAsync()
    {
        var filters = SelectedFormat switch
        {
            ImportFormat.Csv => new FileFilter("CSV Files", ["*.csv"]),
            ImportFormat.Json => new FileFilter("JSON Files", ["*.json"]),
            _ => new FileFilter("All Files", ["*.*"])
        };

        var filePath = await ApplicationVm.Dialogs.OpenFileAsync("Select file to import",
        [
            filters,
            new FileFilter("All Files", ["*.*"])
        ]);

        if (!string.IsNullOrEmpty(filePath))
        {
            InputPath = filePath;
            await PreviewAsync();
        }
    }

    private async Task PreviewAsync()
    {
        if (string.IsNullOrEmpty(InputPath) || !File.Exists(InputPath))
            return;

        PreviewData = null;
        ColumnMappings.Clear();
        ErrorMessage = null;

        try
        {
            // Count total lines for progress
            TotalRows = await CountLinesAsync(InputPath);
            
            var data = SelectedFormat switch
            {
                ImportFormat.Csv => await ParseCsvPreviewAsync(InputPath),
                ImportFormat.Json => await ParseJsonPreviewAsync(InputPath),
                _ => null
            };

            if (data != null)
            {
                PreviewData = data;

                foreach (DataColumn col in data.Columns)
                {
                    var mapping = new ColumnMapping(ApplicationVm, col.ColumnName);
                    ColumnMappings.Add(mapping);
                }

                await AutoMapColumnsAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Preview failed: {ex.Message}";
            Logger.LogError(ex, "Import preview failed");
        }
    }

    private static async Task<int> CountLinesAsync(string filePath)
    {
        var count = 0;
        using var reader = new StreamReader(filePath);
        while (await reader.ReadLineAsync() != null)
            count++;
        return count;
    }

    private async Task AutoMapColumnsAsync()
    {
        if (string.IsNullOrEmpty(SelectedTable))
            return;

        try
        {
            var targetColumns = await Database.GetColumnsAsync(SelectedTable);
            var targetColumnNames = targetColumns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            AvailableTargetColumns = new ObservableCollection<string>(targetColumns.Select(c => c.Name));

            foreach (var mapping in ColumnMappings)
            {
                if (targetColumnNames.Contains(mapping.SourceColumn))
                {
                    mapping.TargetColumn = targetColumns.First(c => 
                        c.Name.Equals(mapping.SourceColumn, StringComparison.OrdinalIgnoreCase)).Name;
                }
            }
            
            UpdateStatus();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to auto-map columns");
        }
    }

    private async Task<DataTable?> ParseCsvPreviewAsync(string filePath)
    {
        var table = new DataTable();
        var delimiterChar = string.IsNullOrEmpty(Delimiter) ? ',' : Delimiter[0];
        
        using var reader = new StreamReader(filePath);
        var lineNumber = 0;
        
        while (await reader.ReadLineAsync() is { } line && lineNumber <= PREVIEW_ROW_LIMIT)
        {
            var values = ParseCsvLine(line, delimiterChar);
            
            if (lineNumber == 0)
            {
                if (HasHeaders)
                {
                    foreach (var header in values)
                        table.Columns.Add(header);
                    lineNumber++;
                    continue;
                }
                else
                {
                    for (var i = 0; i < values.Length; i++)
                        table.Columns.Add($"Column{i + 1}");
                }
            }

            var row = table.NewRow();
            for (var j = 0; j < Math.Min(values.Length, table.Columns.Count); j++)
            {
                row[j] = string.IsNullOrEmpty(values[j]) ? DBNull.Value : values[j];
            }
            table.Rows.Add(row);
            lineNumber++;
        }

        return table;
    }

    private static string[] ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var inQuotes = false;
        var current = new StringBuilder();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }

    private async Task<DataTable?> ParseJsonPreviewAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var table = new DataTable();
        
        json = json.Trim();
        if (!json.StartsWith('[') || !json.EndsWith(']'))
        {
            throw new FormatException("JSON must be an array of objects");
        }

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var array = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(json, options);
        if (array == null || array.Length == 0)
            return table;

        TotalRows = array.Length;

        var firstObj = array[0];
        foreach (var prop in firstObj.EnumerateObject())
        {
            table.Columns.Add(prop.Name);
        }

        var maxRows = Math.Min(array.Length, PREVIEW_ROW_LIMIT);
        for (var i = 0; i < maxRows; i++)
        {
            var row = table.NewRow();
            foreach (var prop in array[i].EnumerateObject())
            {
                if (table.Columns.Contains(prop.Name))
                {
                    row[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.Null 
                        ? DBNull.Value 
                        : prop.Value.ToString();
                }
            }
            table.Rows.Add(row);
        }

        return table;
    }

    private async Task ImportAsync()
    {
        if (!CanImport)
            return;

        IsImporting = true;
        ErrorMessage = null;
        ImportProgress = 0;
        RowsImported = 0;
        RowsFailed = 0;
        ImportErrors.Clear();
        
        m_importCts?.Dispose();
        m_importCts = new CancellationTokenSource();
        var ct = m_importCts.Token;

        try
        {
            var includedMappings = ColumnMappings
                .Where(m => m.IsIncluded && !string.IsNullOrEmpty(m.TargetColumn))
                .ToList();

            if (includedMappings.Count == 0)
            {
                ErrorMessage = "No columns mapped for import";
                return;
            }

            var targetColumns = string.Join(", ", includedMappings.Select(m => $"[{m.TargetColumn}]"));
            
            if (SelectedFormat == ImportFormat.Csv)
            {
                await ImportCsvAsync(targetColumns, includedMappings, ct);
            }
            else
            {
                await ImportJsonAsync(targetColumns, includedMappings, ct);
            }

            if (!ct.IsCancellationRequested)
            {
                var statusMsg = RowsFailed > 0
                    ? $"Imported {RowsImported} rows into {SelectedTable} ({RowsFailed} failed)"
                    : $"Imported {RowsImported} rows into {SelectedTable}";
                
                ApplicationVm.MainWindowVm.StatusText = statusMsg;
                Logger.LogInformation("Imported {RowCount} rows into {TableName}, {FailedCount} failed", 
                    RowsImported, SelectedTable, RowsFailed);
                
                await ApplicationVm.DatabaseExplorerVm.RefreshAsync();
                
                // Show error summary if there were failures
                if (RowsFailed > 0 && !ContinueOnError)
                {
                    ErrorMessage = $"Import completed with errors. {RowsImported} rows imported, {RowsFailed} failed.";
                }
                else
                {
                    DialogClosed?.Invoke(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = $"Import cancelled. {RowsImported} rows were imported before cancellation.";
            Logger.LogInformation("Import cancelled after {RowCount} rows", RowsImported);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
            Logger.LogError(ex, "Import failed");
        }
        finally
        {
            IsImporting = false;
        }
    }

    private async Task ImportCsvAsync(string targetColumns, List<ColumnMapping> mappings, CancellationToken ct)
    {
        var delimiterChar = string.IsNullOrEmpty(Delimiter) ? ',' : Delimiter[0];
        var lineNumber = 0;
        var dataLineCount = HasHeaders ? TotalRows - 1 : TotalRows;
        
        using var reader = new StreamReader(InputPath!);
        
        // Use transaction only if NOT continuing on error (atomic mode)
        var useTransaction = !ContinueOnError;
        
        if (useTransaction)
        {
            await Database.ExecuteNonQueryAsync("BEGIN TRANSACTION", ct);
        }
        
        try
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                ct.ThrowIfCancellationRequested();
                
                // Skip header
                if (lineNumber == 0 && HasHeaders)
                {
                    lineNumber++;
                    continue;
                }
                
                var dataRowNumber = HasHeaders ? lineNumber : lineNumber + 1;
                
                try
                {
                    var values = ParseCsvLine(line, delimiterChar);
                    var sqlValues = BuildSqlValues(values, mappings, PreviewData!);
                    var sql = $"INSERT INTO [{SelectedTable}] ({targetColumns}) VALUES ({sqlValues})";
                    
                    await Database.ExecuteNonQueryAsync(sql, ct);
                    RowsImported++;
                }
                catch (OperationCanceledException)
                {
                    throw; // Re-throw cancellation
                }
                catch (Exception ex)
                {
                    RowsFailed++;
                    
                    if (ImportErrors.Count < MAX_ERRORS_TO_SHOW)
                    {
                        ImportErrors.Add($"Row {dataRowNumber}: {ex.Message}");
                    }
                    
                    if (!ContinueOnError)
                    {
                        throw; // Stop on first error in atomic mode
                    }
                    
                    Logger.LogWarning(ex, "Failed to import row {RowNumber}", dataRowNumber);
                }
                
                lineNumber++;
                
                // Update progress every 100 rows
                if (lineNumber % BATCH_SIZE == 0)
                {
                    ImportProgress = (double)(RowsImported + RowsFailed) / dataLineCount * 100;
                }
            }
            
            ImportProgress = 100;
            
            if (useTransaction)
            {
                await Database.ExecuteNonQueryAsync("COMMIT", ct);
            }
        }
        catch
        {
            if (useTransaction)
            {
                await Database.ExecuteNonQueryAsync("ROLLBACK");
            }
            throw;
        }
    }

    private async Task ImportJsonAsync(string targetColumns, List<ColumnMapping> mappings, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(InputPath!, ct);
        
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var array = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(json, options);
        if (array == null || array.Length == 0)
            return;

        var useTransaction = !ContinueOnError;
        
        if (useTransaction)
        {
            await Database.ExecuteNonQueryAsync("BEGIN TRANSACTION", ct);
        }
        
        try
        {
            for (var i = 0; i < array.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var rowNumber = i + 1;
                
                try
                {
                    var values = new List<string>();
                    foreach (var mapping in mappings)
                    {
                        if (array[i].TryGetProperty(mapping.SourceColumn, out var prop))
                        {
                            values.Add(FormatJsonValue(prop));
                        }
                        else
                        {
                            values.Add("NULL");
                        }
                    }
                    
                    var sql = $"INSERT INTO [{SelectedTable}] ({targetColumns}) VALUES ({string.Join(", ", values)})";
                    await Database.ExecuteNonQueryAsync(sql, ct);
                    RowsImported++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RowsFailed++;
                    
                    if (ImportErrors.Count < MAX_ERRORS_TO_SHOW)
                    {
                        ImportErrors.Add($"Row {rowNumber}: {ex.Message}");
                    }
                    
                    if (!ContinueOnError)
                    {
                        throw;
                    }
                    
                    Logger.LogWarning(ex, "Failed to import row {RowNumber}", rowNumber);
                }
                
                if (i % BATCH_SIZE == 0)
                {
                    ImportProgress = (double)(RowsImported + RowsFailed) / array.Length * 100;
                }
            }
            
            ImportProgress = 100;
            
            if (useTransaction)
            {
                await Database.ExecuteNonQueryAsync("COMMIT", ct);
            }
        }
        catch
        {
            if (useTransaction)
            {
                await Database.ExecuteNonQueryAsync("ROLLBACK");
            }
            throw;
        }
    }

    private string BuildSqlValues(string[] csvValues, List<ColumnMapping> mappings, DataTable schema)
    {
        var values = new List<string>();
        
        foreach (var mapping in mappings)
        {
            var colIndex = schema.Columns.IndexOf(mapping.SourceColumn);
            if (colIndex >= 0 && colIndex < csvValues.Length)
            {
                values.Add(FormatSqlValue(csvValues[colIndex]));
            }
            else
            {
                values.Add("NULL");
            }
        }
        
        return string.Join(", ", values);
    }

    private static string FormatSqlValue(object? value)
    {
        if (value == null || value == DBNull.Value || (value is string s && string.IsNullOrEmpty(s)))
            return "NULL";

        return value switch
        {
            string str => $"'{str.Replace("'", "''")}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateOnly d => $"'{d:yyyy-MM-dd}'",
            TimeOnly t => $"'{t:HH:mm:ss}'",
            bool b => b ? "TRUE" : "FALSE",
            byte[] bytes => $"X'{BitConverter.ToString(bytes).Replace("-", "")}'",
            _ => $"'{value.ToString()?.Replace("'", "''") ?? ""}'"
        };
    }

    private static string FormatJsonValue(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Null => "NULL",
            System.Text.Json.JsonValueKind.True => "TRUE",
            System.Text.Json.JsonValueKind.False => "FALSE",
            System.Text.Json.JsonValueKind.Number => element.ToString(),
            System.Text.Json.JsonValueKind.String => $"'{element.GetString()?.Replace("'", "''") ?? ""}'",
            _ => $"'{element.ToString().Replace("'", "''")}'"
        };
    }

    private void Cancel()
    {
        DialogClosed?.Invoke(false);
    }

    private void CancelImport()
    {
        m_importCts?.Cancel();
    }

    private void UpdateStatus()
    {
        var hasSource = !string.IsNullOrEmpty(InputPath) && File.Exists(InputPath);
        var hasTarget = !string.IsNullOrEmpty(SelectedTable);
        var hasMappings = ColumnMappings.Any(m => m.IsIncluded && !string.IsNullOrEmpty(m.TargetColumn));
        CanImport = hasSource && hasTarget && hasMappings && !IsImporting;
        CanPreview = hasSource && !IsImporting;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((ImportViewModel vm) => vm.InputPath) ||
            e.IsProperty((ImportViewModel vm) => vm.SelectedTable) ||
            e.IsProperty((ImportViewModel vm) => vm.IsImporting))
        {
            UpdateStatus();
        }

        if (e.IsProperty((ImportViewModel vm) => vm.SelectedTable) && !string.IsNullOrEmpty(SelectedTable))
        {
            AutoMapColumnsAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Logger.LogError(t.Exception, "Auto-map columns failed");
            });
        }
    }

    #endregion

    #region Properties

    [Notify]
    public ObservableCollection<string> AvailableTables { get; private set; } = null!;

    [Notify]
    public ObservableCollection<string> AvailableTargetColumns { get; private set; } = new();

    [Notify]
    public string? SelectedTable { get; set; }

    [Notify]
    public ImportFormat SelectedFormat { get; set; }

    [Notify]
    public string? InputPath { get; set; }

    [Notify]
    public bool HasHeaders { get; set; }

    [Notify]
    public string Delimiter { get; set; } = null!;

    [Notify]
    public ObservableCollection<ColumnMapping> ColumnMappings { get; private set; } = null!;

    [Notify]
    public DataTable? PreviewData { get; private set; }

    [Notify]
    public int TotalRows { get; private set; }

    [Notify]
    public bool IsImporting { get; private set; }

    [Notify]
    public string? ErrorMessage { get; private set; }

    [Notify]
    public bool CanImport { get; private set; }

    [Notify]
    public bool CanPreview { get; private set; }

    [Notify]
    public int RowsImported { get; private set; }

    [Notify]
    public int RowsFailed { get; private set; }

    [Notify]
    public double ImportProgress { get; private set; }

    /// <summary>
    /// If true, continues importing even if some rows fail.
    /// If false (default), stops on first error and rolls back.
    /// </summary>
    [Notify]
    public bool ContinueOnError { get; set; }

    /// <summary>
    /// List of error messages for failed rows (limited to MAX_ERRORS_TO_SHOW).
    /// </summary>
    public ObservableCollection<string> ImportErrors { get; } = new();

    #endregion

    #region Commands

    public ICommand BrowseCommand { get; private set; } = null!;

    public ICommand PreviewCommand { get; private set; } = null!;

    public ICommand ImportCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand CancelImportCommand { get; private set; } = null!;

    #endregion

    #region Services

    private IDatabaseService Database => ApplicationVm.Database;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
