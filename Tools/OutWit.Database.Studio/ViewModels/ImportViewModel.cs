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
/// <summary>A row the import would not take, and why.</summary>
/// <param name="Line">
/// The line number IN THE FILE, one-based - not the data row number. They differ by one whenever
/// there is a header, and this report is meant to be read next to the file in an editor.
/// </param>
/// <param name="Reason">The engine's own message, untranslated (WS-64).</param>
/// <param name="Text">The line itself, so the report can be fixed and fed back in.</param>
public sealed record ImportRejection(int Line, string Reason, string Text);

/// <summary>Which step of the wizard is showing (6.4).</summary>
public enum ImportStep
{
    /// <summary>The file: delimiter, encoding, header row, and a preview of what was read.</summary>
    File = 1,

    /// <summary>Where it goes.</summary>
    Destination = 2,

    /// <summary>How the columns line up, and what to do when a key collides.</summary>
    Columns = 3
}

/// <summary>
/// What happens when an imported row collides with one that is already there (6.4).
///
/// <para>
/// All three are real on this engine, which was measured rather than assumed - see
/// <c>ImportConflictProbeTests</c>. <see cref="Update"/> is a <c>MERGE</c>, and the probe checks both
/// halves of it: the matched row is updated and the unmatched one is inserted. An update path that
/// only updated would silently drop every new row in the file.
/// </para>
/// </summary>
public enum ImportConflict
{
    /// <summary>Leave the row that is there, count the one from the file as skipped.</summary>
    Skip,

    /// <summary>Update it - a MERGE, so a row that is not there is still inserted.</summary>
    Update,

    /// <summary>Stop the import. What has been written stays written, and the report says how much.</summary>
    Abort
}

/// <summary>
/// Which step is showing, and which conflict answer is chosen, for the markup. One converter per
/// value rather than one taking a parameter, for the reason given on <c>SettingsSection</c>.
/// </summary>
public static class ImportSteps
{
    public static readonly Avalonia.Data.Converters.IValueConverter IsFile =
        new Avalonia.Data.Converters.FuncValueConverter<ImportStep, bool>(step => step == ImportStep.File);

    public static readonly Avalonia.Data.Converters.IValueConverter IsDestination =
        new Avalonia.Data.Converters.FuncValueConverter<ImportStep, bool>(step => step == ImportStep.Destination);

    public static readonly Avalonia.Data.Converters.IValueConverter IsColumns =
        new Avalonia.Data.Converters.FuncValueConverter<ImportStep, bool>(step => step == ImportStep.Columns);

    /// <summary>Import is offered on the last step only - the earlier ones have Next.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter IsNotColumns =
        new Avalonia.Data.Converters.FuncValueConverter<ImportStep, bool>(step => step != ImportStep.Columns);

    public static readonly Avalonia.Data.Converters.IValueConverter IsNotFile =
        new Avalonia.Data.Converters.FuncValueConverter<ImportStep, bool>(step => step != ImportStep.File);

    public static readonly Avalonia.Data.Converters.IValueConverter ConflictIsSkip =
        new Avalonia.Data.Converters.FuncValueConverter<ImportConflict, bool>(c => c == ImportConflict.Skip);

    public static readonly Avalonia.Data.Converters.IValueConverter ConflictIsUpdate =
        new Avalonia.Data.Converters.FuncValueConverter<ImportConflict, bool>(c => c == ImportConflict.Update);

    public static readonly Avalonia.Data.Converters.IValueConverter ConflictIsAbort =
        new Avalonia.Data.Converters.FuncValueConverter<ImportConflict, bool>(c => c == ImportConflict.Abort);

    /// <summary>
    /// How strongly each label in the step strip is drawn - the current one full, the others faded.
    ///
    /// <para>
    /// These return a DOUBLE, and that is the whole point. The first version bound the bool converters
    /// above to <c>Opacity</c>, which is a double: the binding failed, fell through to its
    /// FallbackValue of 1, and every step was drawn identically. A strip that does not say which step
    /// you are on is not a step strip, and it looked completely normal in a screenshot. Found by
    /// driving the application.
    /// </para>
    /// </summary>
    public static readonly Avalonia.Data.Converters.IValueConverter OpacityForFile =
        new Avalonia.Data.Converters.FuncValueConverter<ImportStep, double>(step => step == ImportStep.File ? 1.0 : 0.4);

    public static readonly Avalonia.Data.Converters.IValueConverter OpacityForDestination =
        new Avalonia.Data.Converters.FuncValueConverter<ImportStep, double>(step => step == ImportStep.Destination ? 1.0 : 0.4);

    public static readonly Avalonia.Data.Converters.IValueConverter OpacityForColumns =
        new Avalonia.Data.Converters.FuncValueConverter<ImportStep, double>(step => step == ImportStep.Columns ? 1.0 : 0.4);
}

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
        NextCommand = new RelayCommand(GoNext);
        BackCommand = new RelayCommand(GoBack);
        WriteReportCommand = new RelayCommandAsync(WriteReportAsync);
        ChooseConflictCommand = new RelayCommand<string>(answer =>
            OnConflict = Enum.TryParse<ImportConflict>(answer, out var parsed) ? parsed : OnConflict);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;

        // A sentence built here is rendered once and would stay in the language it was built in.
        Localization.LanguageChanged += (_, _) => RefreshLanguage();
    }

    /// <summary>The text on this window that came out of the catalogue rather than the markup.</summary>
    private void RefreshLanguage()
    {
        OnPropertyChanged(nameof(PreviewRowsSummary));
        OnPropertyChanged(nameof(PreviewColumnsSummary));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(FailedText));
    }

    #endregion

    #region Functions

    public async Task InitializeAsync()
    {
        if (Database?.IsConnected != true)
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
            ImportFormat.Csv => new FileFilter(Localization.Format("Common.Filter.Files", "CSV"), ["*.csv"]),
            ImportFormat.Json => new FileFilter(Localization.Format("Common.Filter.Files", "JSON"), ["*.json"]),
            _ => new FileFilter(Localization["Common.Filter.AllFiles"], ["*.*"])
        };

        var filePath = await ApplicationVm.Dialogs.OpenFileAsync(Localization["Dialog.Import.PickFile"],
        [
            filters,
            new FileFilter(Localization["Common.Filter.AllFiles"], ["*.*"])
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
            ErrorMessage = Localization.Format("Dialog.Import.PreviewFailed", ex.Message);
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
        var session = Database;

        if (string.IsNullOrEmpty(SelectedTable) || session?.IsConnected != true)
            return;

        try
        {
            var targetColumns = await session.GetColumnsAsync(SelectedTable);
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

        // Captured once, for the whole import: this is a loop over thousands of statements inside one
        // transaction, and re-reading the active connection each time would let a click in the tree
        // move the target halfway through (WS-3).
        var session = Database;

        if (session?.IsConnected != true)
        {
            ErrorMessage = Localization["Common.NotConnected"];
            return;
        }

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
                ErrorMessage = Localization["Dialog.Import.NoColumns"];
                return;
            }

            var targetColumns = string.Join(", ", includedMappings.Select(m => $"[{m.TargetColumn}]"));
            
            if (SelectedFormat == ImportFormat.Csv)
            {
                await ImportCsvAsync(session, targetColumns, includedMappings, ct);
            }
            else
            {
                await ImportJsonAsync(session, targetColumns, includedMappings, ct);
            }

            if (!ct.IsCancellationRequested)
            {
                var statusMsg = RowsFailed > 0
                    ? Localization.Format("Dialog.Import.DoneWithFailures", RowsImported, SelectedTable, RowsFailed)
                    : Localization.Format("Dialog.Import.Done", RowsImported, SelectedTable);
                
                ApplicationVm.MainWindowVm.StatusText = statusMsg;

                // Also a notification (WS-7): an import can take minutes, and by the time it ends
                // the user is looking at another tab. The status bar keeps only the last thing that
                // happened; this keeps all of them.
                if (RowsFailed > 0)
                {
                    ApplicationVm.Notifications.Warning(statusMsg,
                        Localization.Plural("Count.RowsRefused", RowsFailed), session.DisplayName);
                }
                else
                {
                    ApplicationVm.Notifications.Information(statusMsg, connection: session.DisplayName);
                }
                Logger.LogInformation("Imported {RowCount} rows into {TableName}, {FailedCount} failed", 
                    RowsImported, SelectedTable, RowsFailed);
                
                await ApplicationVm.DatabaseExplorerVm.RefreshAsync();
                
                // Show error summary if there were failures
                if (RowsFailed > 0 && !ContinueOnError)
                {
                    ErrorMessage = Localization.Format("Dialog.Import.WithErrors", RowsImported, RowsFailed);
                }
                else
                {
                    DialogClosed?.Invoke(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = Localization.Format("Dialog.Import.Cancelled", RowsImported);
            Logger.LogInformation("Import cancelled after {RowCount} rows", RowsImported);
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Dialog.Import.Failed", ex.Message);
            Logger.LogError(ex, "Import failed");
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>
    /// <b>Batches, not one transaction (6.4).</b> A million rows in one transaction is a million
    /// versions in MVCC and a journal that grows until it stops; and a cancel would then throw away
    /// work the user watched happen. So the file is written in batches, a cancel stops at a batch
    /// boundary, and the report says honestly how many rows are already in the database.
    ///
    /// <para>
    /// All-or-nothing is still available and is a separate choice - <see cref="AllOrNothing"/> - for
    /// the people who genuinely need it. It is not the default, because the default should not be the
    /// mode that fails on the largest file.
    /// </para>
    /// </summary>
    private async Task ImportCsvAsync(IDatabaseSession session, string targetColumns, List<ColumnMapping> mappings, CancellationToken ct)
    {
        var delimiterChar = string.IsNullOrEmpty(Delimiter) ? ',' : Delimiter[0];
        var lineNumber = 0;
        var dataLineCount = HasHeaders ? TotalRows - 1 : TotalRows;
        var inBatch = 0;

        using var reader = new StreamReader(InputPath!);

        var useTransaction = AllOrNothing;

        if (useTransaction)
        {
            await session.ExecuteNonQueryAsync("BEGIN TRANSACTION", ct);
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

                // The line number IN THE FILE, not the data row number. They differ by one whenever
                // there is a header, and the report exists to be opened next to the file in an editor:
                // "row 412" pointing at line 413 is a small trap that costs someone ten minutes.
                var dataRowNumber = lineNumber + 1;

                try
                {
                    var values = ParseCsvLine(line, delimiterChar);
                    var sqlValues = BuildSqlValues(values, mappings, PreviewData!);

                    await session.ExecuteNonQueryAsync(
                        StatementFor(targetColumns, sqlValues, mappings), ct);

                    RowsImported++;
                    inBatch++;

                    if (!useTransaction && inBatch >= BATCH_SIZE)
                        inBatch = 0;
                }
                catch (OperationCanceledException)
                {
                    throw; // Re-throw cancellation
                }
                catch (Exception ex)
                {
                    RowsFailed++;

                    // Every rejected row is recorded, not only the first ten - the ten are what the
                    // window shows and the rest are what "report to CSV" writes. An import that
                    // reports "16 skipped" and can name three of them is an import nobody can fix.
                    Rejected.Add(new ImportRejection(dataRowNumber, ex.Message, line));

                    if (ImportErrors.Count < MAX_ERRORS_TO_SHOW)
                    {
                        ImportErrors.Add($"Row {dataRowNumber}: {ex.Message}");
                    }

                    // A key collision is the one failure the user chose an answer for; anything else
                    // follows StopOnError.
                    var collision = ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);

                    if ((collision && OnConflict == ImportConflict.Abort) || (!collision && StopOnError))
                    {
                        throw;
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
                await session.ExecuteNonQueryAsync("COMMIT", ct);
            }
        }
        catch
        {
            if (useTransaction)
            {
                await session.ExecuteNonQueryAsync("ROLLBACK");
            }
            throw;
        }
    }

    private async Task ImportJsonAsync(IDatabaseSession session, string targetColumns, List<ColumnMapping> mappings, CancellationToken ct)
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
            await session.ExecuteNonQueryAsync("BEGIN TRANSACTION", ct);
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
                    await session.ExecuteNonQueryAsync(sql, ct);
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
                await session.ExecuteNonQueryAsync("COMMIT", ct);
            }
        }
        catch
        {
            if (useTransaction)
            {
                await session.ExecuteNonQueryAsync("ROLLBACK");
            }
            throw;
        }
    }

    /// <summary>
    /// The statement one row becomes. <c>Update</c> is a <c>MERGE</c>, which this engine performs -
    /// measured in <c>ImportConflictProbeTests</c>, both halves of it: the matched row is updated and
    /// the unmatched one is inserted. Skip and Abort are both a plain INSERT; they differ in what is
    /// done with the refusal, not in what is asked for.
    /// </summary>
    private string StatementFor(string targetColumns, string sqlValues, List<ColumnMapping> mappings)
    {
        if (OnConflict != ImportConflict.Update || string.IsNullOrEmpty(KeyColumn))
            return $"INSERT INTO [{SelectedTable}] ({targetColumns}) VALUES ({sqlValues})";

        var columns = targetColumns.Split(',', StringSplitOptions.TrimEntries);
        var values = SplitTopLevel(sqlValues);

        if (columns.Length != values.Count)
            return $"INSERT INTO [{SelectedTable}] ({targetColumns}) VALUES ({sqlValues})";

        var source = string.Join(", ",
            columns.Select((column, index) => $"{values[index]} AS {column}"));

        var assignments = string.Join(", ",
            columns.Where(column => !column.Contains(KeyColumn, StringComparison.OrdinalIgnoreCase))
                .Select(column => $"{column} = s.{column}"));

        // A table whose only mapped column is the key has nothing to update, and MERGE with an empty
        // SET is not a statement. The row is then either there or not, which is what INSERT answers.
        if (string.IsNullOrEmpty(assignments))
            return $"INSERT INTO [{SelectedTable}] ({targetColumns}) VALUES ({sqlValues})";

        return $"MERGE INTO [{SelectedTable}] AS t USING (SELECT {source}) AS s "
            + $"ON t.[{KeyColumn}] = s.[{KeyColumn}] "
            + $"WHEN MATCHED THEN UPDATE SET {assignments} "
            + $"WHEN NOT MATCHED THEN INSERT ({targetColumns}) VALUES ({string.Join(", ", values)})";
    }

    /// <summary>
    /// Splits a VALUES list on commas that are not inside a quoted string. A plain Split would cut
    /// <c>'Smith, John'</c> in half and produce a statement with one value too many.
    /// </summary>
    private static List<string> SplitTopLevel(string values)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in values)
        {
            if (character == '\'')
                inQuotes = !inQuotes;

            if (character == ',' && !inQuotes)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
            parts.Add(current.ToString().Trim());

        return parts;
    }

    private string BuildSqlValues(string[] csvValues, List<ColumnMapping> mappings, DataTable schema)
    {
        var values = new List<string>();
        
        foreach (var mapping in mappings)
        {
            var colIndex = schema.Columns.IndexOf(mapping.SourceColumn);
            if (colIndex >= 0 && colIndex < csvValues.Length)
            {
                // An empty field is NULL or an empty string, and a CSV cannot tell them apart - so the
                // user does, once, in step 1. NULL is the default because a missing value is what an
                // empty field usually means, and a column that is NOT NULL will say so either way.
                var field = csvValues[colIndex];

                values.Add(!EmptyIsNull && field.Length == 0 ? "''" : FormatSqlValue(field));
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

    /// <summary>
    /// Writes every rejected row to a CSV beside the source, with the line number, the engine's own
    /// message and the line itself - so the file can be repaired and fed back in. The window shows ten;
    /// this is all of them.
    /// </summary>
    private async Task WriteReportAsync()
    {
        if (Rejected.Count == 0)
            return;

        var target = await ApplicationVm.Dialogs.SaveFileAsync(
            Localization["Dialog.Import.RejectedRows"],
            suggestedFileName: Path.GetFileNameWithoutExtension(InputPath) + "-rejected.csv",
            defaultExtension: "csv",
            filters: [new FileFilter(Localization.Format("Common.Filter.Files", "CSV"), ["*.csv"])]);

        if (string.IsNullOrEmpty(target))
            return;

        var text = new StringBuilder();

        text.AppendLine("Line,Reason,Text");

        foreach (var rejection in Rejected)
        {
            text.Append(rejection.Line).Append(',')
                .Append(Csv(rejection.Reason)).Append(',')
                .AppendLine(Csv(rejection.Text));
        }

        await File.WriteAllTextAsync(target, text.ToString());

        ReportPath = target;

        return;

        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>The wizard's steps (6.4). Three, because the decisions depend on each other.</summary>
    private void GoNext()
    {
        if (Step < ImportStep.Columns)
            Step++;
    }

    private void GoBack()
    {
        if (Step > ImportStep.File)
            Step--;
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

        if (e.IsProperty((ImportViewModel vm) => vm.TotalRows) ||
            e.IsProperty((ImportViewModel vm) => vm.RowsImported) ||
            e.IsProperty((ImportViewModel vm) => vm.RowsFailed) ||
            e.IsProperty((ImportViewModel vm) => vm.ColumnMappings))
        {
            RefreshLanguage();
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
    /// What the preview found, one sentence each. They were built out of <c>&lt;Run&gt;</c> fragments in
    /// the markup - "File contains approximately" + a number + "rows" - which fixes English word order
    /// into the window and cannot carry a plural in a language that has three of them.
    /// </summary>
    public string PreviewRowsSummary =>
        Localization.Format("Dialog.Import.Preview.Approx", Localization.Plural("Count.Rows", TotalRows));

    public string PreviewColumnsSummary => Localization.Plural("Count.Columns", ColumnMappings?.Count ?? 0);

    /// <summary>How far the import has got, over the window while it runs.</summary>
    public string ProgressText => Localization.Format("Dialog.Import.Progress", RowsImported, TotalRows);

    /// <summary>How many rows the database refused, under the progress bar.</summary>
    public string FailedText => Localization.Plural("Count.RowsRefused", RowsFailed);

    /// <summary>
    /// If true, continues importing even if some rows fail.
    /// If false (default), stops on first error and rolls back.
    /// </summary>
    /// <summary>
    /// Kept for the call sites that still set it. It used to mean two different things at once -
    /// "keep going past a bad row" AND "do not wrap the file in a transaction" - which is why it is
    /// now three separate choices: <see cref="StopOnError"/>, <see cref="OnConflict"/> and
    /// <see cref="AllOrNothing"/>.
    /// </summary>
    [Notify]
    public bool ContinueOnError { get; set; }

    /// <summary>Which step of the wizard is showing (6.4).</summary>
    [Notify]
    public ImportStep Step { get; set; } = ImportStep.File;

    /// <summary>What to do when an imported row collides with one that is already there.</summary>
    [Notify]
    public ImportConflict OnConflict { get; set; } = ImportConflict.Skip;

    /// <summary>
    /// The column the collision is decided by - the primary key. <c>Update</c> needs it: a MERGE has
    /// to be told what "the same row" means, and the design says so next to the option.
    /// </summary>
    [Notify]
    public string? KeyColumn { get; set; }

    /// <summary>Whether a row that fails for a reason other than a key collision stops the import.</summary>
    [Notify]
    public bool StopOnError { get; set; }

    /// <summary>
    /// Whether the whole file is one transaction. <b>Off by default</b>: a million rows in one
    /// transaction is a million versions in MVCC and a journal that grows until it stops. It is here
    /// for the people who genuinely need all-or-nothing, and it says what it costs.
    /// </summary>
    [Notify]
    public bool AllOrNothing { get; set; }

    /// <summary>
    /// An empty field means NULL rather than an empty string. On by default, because a CSV has no way
    /// to tell them apart and NULL is what a missing value is.
    /// </summary>
    [Notify]
    public bool EmptyIsNull { get; set; } = true;

    /// <summary>
    /// Every rejected row, not only the ten the window shows. This is what "report to CSV" writes, and
    /// an import that reports "16 skipped" and can name three of them is an import nobody can fix.
    /// </summary>
    public List<ImportRejection> Rejected { get; } = [];

    /// <summary>
    /// List of error messages for failed rows (limited to MAX_ERRORS_TO_SHOW).
    /// </summary>
    public ObservableCollection<string> ImportErrors { get; } = new();

    #endregion

    #region Commands

    /// <summary>Where the report of rejected rows was written, once it has been.</summary>
    [Notify]
    public string? ReportPath { get; set; }

    /// <summary>Picks the answer to a key collision from the markup.</summary>
    public ICommand ChooseConflictCommand { get; private set; } = null!;

    public ICommand NextCommand { get; private set; } = null!;

    public ICommand BackCommand { get; private set; } = null!;

    /// <summary>Writes every rejected row to a CSV that can be repaired and fed back in.</summary>
    public ICommand WriteReportCommand { get; private set; } = null!;

    public ICommand BrowseCommand { get; private set; } = null!;

    public ICommand PreviewCommand { get; private set; } = null!;

    public ICommand ImportCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand CancelImportCommand { get; private set; } = null!;

    #endregion

    #region Services

    /// <summary>
    /// The active connection - the one selected in the tree.
    /// </summary>
    private IDatabaseSession? Database => ApplicationVm.ActiveSession;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
