using System.ComponentModel;
using System.Data;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.Locker;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Ui.Icons;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// ViewModel for a table data editor tab.
/// Allows browsing, editing, adding, and deleting table data.
/// </summary>
public class TableEditTabViewModel : WorkspaceTabViewModel
{
    #region Constants

    private const int DEFAULT_PAGE_SIZE = 1000;

    #endregion

    #region Fields

    private DataTable? m_originalData;
    private readonly HashSet<DataRow> m_deletedRows = [];
    private readonly HashSet<DataRow> m_modifiedRows = [];
    private readonly List<DataRow> m_newRows = [];

    #endregion

    #region Constructors

    public TableEditTabViewModel(ApplicationViewModel applicationVm, string tableName)
        : base(applicationVm)
    {
        TableName = tableName;
        Title = $"{tableName} - Edit";

        InitDefault();
        InitEvents();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Columns = [];
        PageSize = DEFAULT_PAGE_SIZE;
        
        // Initialize column settings for the edit grid
        EditColumnSettings = new GridColumnSettings();
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        Database.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    private void InitCommands()
    {
        LoadDataCommand = new RelayCommandAsync(LoadDataAsync);
        RefreshCommand = new RelayCommandAsync(RefreshDataAsync);
        AddRowCommand = new RelayCommand(AddRow);
        DeleteRowCommand = new RelayCommand(DeleteSelectedRow);
        CommitCommand = new RelayCommandAsync(CommitChangesAsync);
        RollbackCommand = new RelayCommand(RollbackChanges);
        CellEditedCommand = new RelayCommand<DataRowView>(OnCellEdited);
    }

    #endregion

    #region WorkspaceTabViewModel

    public override WorkspaceTabType TabType => WorkspaceTabType.TableEdit;

    public override string IconPath => StudioIcons.PATH_DB_TABLE;

    public override string? UniqueId => $"edit:{TableName}";

    /// <summary>
    /// The number of edits waiting in the buffer: rows deleted, rows changed, rows added. Named in the
    /// question, because "there are unsaved changes" is a sentence people click through.
    /// </summary>
    public int ChangeCount => m_deletedRows.Count + m_modifiedRows.Count + m_newRows.Count;

    public override bool CanClose() => !HasChanges;

    /// <summary>
    /// Closing a dirty editor used to discard the buffer without a word - the tab went away and the
    /// DataTable was disposed by OnClosed a line later, so there was nothing left to recover from.
    /// </summary>
    public override async Task<bool> ConfirmCloseAsync()
    {
        if (!HasChanges)
            return true;

        var decision = await ApplicationVm.Confirmations.AskAboutUnsavedChangesAsync(Title, ChangeCount);

        switch (decision)
        {
            case UnsavedChangesDecision.Apply:
                await CommitChangesAsync();

                // The commit is a transaction that can be refused - by a constraint, by a conflict, by
                // a lost connection. If it was, the tab stays open with its buffer: closing here would
                // lose exactly what the user asked to keep.
                if (HasChanges || HasError)
                {
                    Logger.LogWarning("Tab {Title} stays open: applying its changes failed", Title);
                    return false;
                }

                return true;

            case UnsavedChangesDecision.Discard:
                Logger.LogInformation("Discarding {Count} unapplied changes in {Title}", ChangeCount, Title);
                return true;

            default:
                return false;
        }
    }

    public override void OnClosed()
    {
        EditableData?.Dispose();
        EditableData = null;
        m_originalData?.Dispose();
        m_originalData = null;
        CurrentView = null;
        ClearChangeTracking();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Loads data for the table.
    /// </summary>
    public async Task LoadDataAsync()
    {
        using var locker = GlobalLocker.Lock(nameof(TableEditTabViewModel));

        await LoadColumnsAsync();
        await LoadTableDataAsync();
    }

    private async Task LoadColumnsAsync()
    {
        if (string.IsNullOrWhiteSpace(TableName) || !Database.IsConnected)
            return;

        try
        {
            var columns = await Database.GetColumnsAsync(TableName);
            Columns = columns.ToList();
            PrimaryKeyColumns = columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

            // WS-35. Without a primary key there is no condition that names one row: the old fallback
            // built a WHERE over every column, which two identical rows both satisfy - so editing one
            // of them changed both, and the affected-row count nobody read was the only sign.
            IsReadOnly = PrimaryKeyColumns.Count == 0;
            ReadOnlyReason = IsReadOnly
                ? $"\"{TableName}\" has no primary key, so a row cannot be identified. "
                  + "The data is shown for viewing; edit it with a query that names the rows you mean."
                : null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load columns: {ex.Message}";
            Logger.LogError(ex, "Failed to load columns for table {TableName}", TableName);
        }
    }

    private async Task LoadTableDataAsync()
    {
        if (string.IsNullOrWhiteSpace(TableName) || !Database.IsConnected)
            return;

        IsLoading = true;
        ClearStatus();
        ClearChangeTracking();

        try
        {
            // Build SQL with ORDER BY if primary keys exist
            var sql = BuildSelectStatement();
            var result = await Database.ExecuteQueryAsync(sql);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                SetErrorStatus(result.ErrorMessage);
                return;
            }

            m_originalData = result.Data?.Copy();
            EditableData = result.Data;

            if (EditableData != null)
            {
                CurrentView = new DataView(EditableData);
                TotalRowCount = EditableData.Rows.Count;
            }

            SetSuccessStatus($"Loaded {TotalRowCount} rows");
            ApplicationVm.MainWindowVm.StatusText = $"Loaded {TotalRowCount} rows from table \"{TableName}\"";
            Logger.LogInformation("Loaded {Count} rows from table {TableName}", TotalRowCount, TableName);
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Failed to load data: {ex.Message}");
            Logger.LogError(ex, "Failed to load data from table {TableName}", TableName);
        }
        finally
        {
            IsLoading = false;
            UpdateStatus();
        }
    }

    private async Task RefreshDataAsync()
    {
        if (HasChanges)
        {
            // TODO: Show confirmation dialog
        }

        await LoadTableDataAsync();
    }

    private void AddRow()
    {
        if (EditableData == null || Columns.Count == 0)
            return;

        var newRow = EditableData.NewRow();

        foreach (var column in Columns)
        {
            if (column.IsAutoIncrement)
            {
                newRow[column.Name] = DBNull.Value;
                continue;
            }

            if (!string.IsNullOrEmpty(column.DefaultValue))
            {
                try
                {
                    var defaultValue = ParseDefaultValue(column.DefaultValue, EditableData.Columns[column.Name].DataType);
                    newRow[column.Name] = defaultValue ?? DBNull.Value;
                }
                catch
                {
                    newRow[column.Name] = DBNull.Value;
                }
            }
            else if (column.IsNullable)
            {
                newRow[column.Name] = DBNull.Value;
            }
        }

        EditableData.Rows.Add(newRow);
        m_newRows.Add(newRow);
        TotalRowCount = EditableData.Rows.Count;

        CurrentView = new DataView(EditableData);

        UpdateStatus();

        Logger.LogDebug("Added new row. Total rows: {Count}, New rows: {NewCount}", TotalRowCount, m_newRows.Count);
    }

    private void DeleteSelectedRow()
    {
        if (SelectedRowView == null || EditableData == null)
            return;

        var row = SelectedRowView.Row;

        if (m_newRows.Contains(row))
        {
            m_newRows.Remove(row);
            EditableData.Rows.Remove(row);
        }
        else
        {
            m_deletedRows.Add(row);
            row.Delete();
        }

        TotalRowCount = EditableData.Rows.Count - m_deletedRows.Count;
        SelectedRowView = null;

        UpdateStatus();
    }

    /// <summary>
    /// Applies the whole edit buffer as ONE transaction (WS-36, B2).
    ///
    /// It used to send the statements one at a time, each in its own try/catch, collecting failures
    /// into a list and showing the first three. A buffer that failed halfway left the rows it had
    /// already written and told the user "Update failed: ..." - who then had no way of knowing what
    /// was in the database. Now nothing is applied unless all of it is, and a refused set keeps its
    /// buffer so that nothing has to be retyped.
    /// </summary>
    private async Task CommitChangesAsync()
    {
        if (EditableData == null || string.IsNullOrWhiteSpace(TableName) || m_originalData == null)
            return;

        if (IsReadOnly)
        {
            SetErrorStatus(ReadOnlyReason ?? "This table cannot be edited.");
            return;
        }

        IsLoading = true;
        ClearStatus();

        try
        {
            var statements = BuildChangeScript(out var buildError);

            if (statements == null)
            {
                SetErrorStatus(buildError!);
                Logger.LogWarning("Nothing applied to {TableName}: {Error}", TableName, buildError);
                return;
            }

            if (statements.Count == 0)
            {
                SetSuccessStatus("Nothing to apply");
                return;
            }

            var result = await Database.ExecuteBatchAsync(statements);

            if (!result.Committed)
            {
                // The buffer is deliberately left alone, and the table is NOT reloaded: a reload would
                // throw away exactly the work the user was told had not been saved.
                SetErrorStatus(
                    $"Nothing was applied. Statement {result.FailedIndex + 1} of {statements.Count} "
                    + $"failed: {result.ErrorMessage}");

                Logger.LogWarning("Commit to {TableName} rolled back at statement {Index}",
                    TableName, result.FailedIndex + 1);
                return;
            }

            SetSuccessStatus($"Applied {statements.Count} changes");
            ApplicationVm.MainWindowVm.StatusText =
                $"Applied {statements.Count} changes to \"{TableName}\"";
            IsModified = false;

            await LoadTableDataAsync();
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Commit failed: {ex.Message}");
            Logger.LogError(ex, "Failed to commit changes to table {TableName}", TableName);
        }
        finally
        {
            IsLoading = false;
            UpdateStatus();
        }
    }

    /// <summary>
    /// Turns the edit buffer into the statements that will be sent, in the order they must run:
    /// deletes, then inserts, then updates. Returns null - with a reason - when a row cannot be
    /// addressed, because a set that cannot be expressed must not be half-expressed.
    /// </summary>
    private List<string>? BuildChangeScript(out string? error)
    {
        error = null;

        var statements = new List<string>();
        var table = SqlValueFormatter.EscapeIdentifier(TableName);

        foreach (var row in m_deletedRows)
        {
            var originalRowIndex = FindOriginalRowIndex(row);

            if (originalRowIndex < 0)
            {
                error = "Cannot delete a row: the row it was loaded from could not be found. Refresh and try again.";
                return null;
            }

            var whereClause = BuildWhereClause(m_originalData!.Rows[originalRowIndex]);

            if (string.IsNullOrEmpty(whereClause))
            {
                error = "Cannot delete a row: the table has no primary key.";
                return null;
            }

            statements.Add($"DELETE FROM [{table}] WHERE {whereClause}");
        }

        foreach (var newRow in m_newRows)
            statements.Add(BuildInsertStatement(newRow));

        foreach (var modifiedRow in m_modifiedRows)
        {
            if (modifiedRow.RowState is DataRowState.Deleted or DataRowState.Detached)
                continue;

            var originalRowIndex = FindOriginalRowIndex(modifiedRow);

            if (originalRowIndex < 0)
            {
                error = "Cannot update a row: the row it was loaded from could not be found. Refresh and try again.";
                return null;
            }

            var whereClause = BuildWhereClause(m_originalData!.Rows[originalRowIndex]);

            if (string.IsNullOrEmpty(whereClause))
            {
                error = "Cannot update a row: the table has no primary key.";
                return null;
            }

            statements.Add(BuildUpdateStatement(modifiedRow, whereClause));
        }

        return statements;
    }

    private int FindOriginalRowIndex(DataRow row)
    {
        if (m_originalData == null)
            return -1;

        if (PrimaryKeyColumns.Count > 0)
        {
            for (int i = 0; i < m_originalData.Rows.Count; i++)
            {
                var originalRow = m_originalData.Rows[i];
                bool match = true;

                foreach (var pkCol in PrimaryKeyColumns)
                {
                    var currentValue = row.RowState == DataRowState.Deleted
                        ? row[pkCol, DataRowVersion.Original]
                        : row[pkCol];
                    var originalValue = originalRow[pkCol];

                    if (!Equals(currentValue, originalValue))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return i;
            }
        }

        if (EditableData != null)
        {
            var currentIndex = EditableData.Rows.IndexOf(row);
            if (currentIndex >= 0 && currentIndex < m_originalData.Rows.Count)
                return currentIndex;
        }

        return -1;
    }

    private void RollbackChanges()
    {
        if (m_originalData == null || EditableData == null)
            return;

        ClearChangeTracking();

        EditableData.Clear();
        foreach (DataRow row in m_originalData.Rows)
        {
            EditableData.ImportRow(row);
        }

        CurrentView = new DataView(EditableData);
        TotalRowCount = EditableData.Rows.Count;

        SetSuccessStatus("Changes discarded");
        ApplicationVm.MainWindowVm.StatusText = "Changes discarded";
        IsModified = false;
        UpdateStatus();
    }

    private void ClearChangeTracking()
    {
        m_deletedRows.Clear();
        m_modifiedRows.Clear();
        m_newRows.Clear();
    }

    private void OnCellEdited(DataRowView? rowView)
    {
        if (EditableData == null || rowView == null)
            return;

        var row = rowView.Row;

        if (!m_newRows.Contains(row))
        {
            m_modifiedRows.Add(row);
            Logger.LogDebug("Row marked as modified. Total modified: {Count}", m_modifiedRows.Count);
        }

        IsModified = true;
        UpdateStatus();
    }

    #endregion

    #region SQL Building

    private string BuildSelectStatement()
    {
        var sql = $"SELECT * FROM [{SqlValueFormatter.EscapeIdentifier(TableName)}]";

        // Add ORDER BY if primary keys exist for consistent ordering
        if (PrimaryKeyColumns.Count > 0)
        {
            var orderByColumns = string.Join(", ", PrimaryKeyColumns.Select(c => $"[{SqlValueFormatter.EscapeIdentifier(c)}]"));
            sql += $" ORDER BY {orderByColumns}";
        }

        sql += $" LIMIT {PageSize}";

        return sql;
    }

    /// <summary>
    /// The condition that names exactly one row: its primary key, or nothing.
    ///
    /// There used to be a fallback that built the condition from EVERY column of the row. That is not
    /// a unique condition - two identical rows both match it, so an UPDATE meant for one changed both,
    /// and a DELETE meant for one removed both. It also compared BLOB columns in a WHERE clause. The
    /// fallback is gone, and a table without a key is not editable at all (WS-35).
    /// </summary>
    private string BuildWhereClause(DataRow row)
    {
        if (PrimaryKeyColumns.Count == 0)
            return string.Empty;

        var conditions = PrimaryKeyColumns
            .Select(pkColumn =>
                $"[{SqlValueFormatter.EscapeIdentifier(pkColumn)}] = {SqlValueFormatter.FormatForSql(row[pkColumn])}");

        return string.Join(" AND ", conditions);
    }

    private string BuildInsertStatement(DataRow row)
    {
        var columns = new List<string>();
        var values = new List<string>();

        foreach (DataColumn col in row.Table.Columns)
        {
            var value = row[col];

            var columnInfo = Columns.FirstOrDefault(c => c.Name == col.ColumnName);
            if (columnInfo?.IsAutoIncrement == true && (value == DBNull.Value || value == null))
                continue;

            columns.Add($"[{SqlValueFormatter.EscapeIdentifier(col.ColumnName)}]");
            values.Add(SqlValueFormatter.FormatForSql(value));
        }

        return $"INSERT INTO [{SqlValueFormatter.EscapeIdentifier(TableName)}] ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
    }

    private string BuildUpdateStatement(DataRow row, string whereClause)
    {
        var setClauses = new List<string>();

        foreach (DataColumn col in row.Table.Columns)
        {
            if (PrimaryKeyColumns.Contains(col.ColumnName))
                continue;

            var value = row[col];
            setClauses.Add($"[{SqlValueFormatter.EscapeIdentifier(col.ColumnName)}] = {SqlValueFormatter.FormatForSql(value)}");
        }

        return $"UPDATE [{SqlValueFormatter.EscapeIdentifier(TableName)}] SET {string.Join(", ", setClauses)} WHERE {whereClause}";
    }

    private static object? ParseDefaultValue(string defaultValue, Type targetType)
    {
        var upper = defaultValue.Trim().ToUpperInvariant();

        if (upper is "NULL")
            return null;

        if (upper is "NOW()" or "CURRENT_TIMESTAMP" or "CURRENT_DATE" or "CURRENT_TIME")
            return DateTime.UtcNow;

        if (upper is "NEWGUID()" or "NEWUUID()")
            return Guid.NewGuid();

        if (upper is "TRUE")
            return true;

        if (upper is "FALSE")
            return false;

        try
        {
            return Convert.ChangeType(defaultValue.Trim('\'', '"'), targetType);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        HasChanges = m_deletedRows.Count > 0 || m_modifiedRows.Count > 0 || m_newRows.Count > 0;
        IsModified = HasChanges;
        CanCommit = HasChanges && !IsLoading && !IsReadOnly;
        CanRollback = HasChanges && !IsLoading;
        CanAddRow = !string.IsNullOrWhiteSpace(TableName) && !IsLoading && Database.IsConnected && !IsReadOnly;
        CanDeleteRow = SelectedRowView != null && !IsLoading && !IsReadOnly;
        CanRefresh = !string.IsNullOrWhiteSpace(TableName) && !IsLoading && Database.IsConnected;
        
        // Status bar states
        HasError = !string.IsNullOrEmpty(ErrorMessage);
        LastOperationSuccess = !HasError && !string.IsNullOrEmpty(StatusMessage);
        IsDefaultState = !HasError && !LastOperationSuccess;
    }

    private void SetSuccessStatus(string message)
    {
        ErrorMessage = null;
        StatusMessage = message;
        UpdateStatus();
    }

    private void SetErrorStatus(string message)
    {
        StatusMessage = null;
        ErrorMessage = message;
        UpdateStatus();
    }

    private void ClearStatus()
    {
        StatusMessage = null;
        ErrorMessage = null;
        UpdateStatus();
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (GlobalLocker.IsLocked(nameof(TableEditTabViewModel)))
            return;

        if (e.IsProperty((TableEditTabViewModel vm) => vm.SelectedRowView))
            UpdateStatus();

        if (e.IsProperty((TableEditTabViewModel vm) => vm.IsLoading))
            UpdateStatus();
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        if (!isConnected)
        {
            // Tab should be closed by WorkspaceTabsViewModel
        }

        UpdateStatus();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Name of the table being edited.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Column definitions for the table.
    /// </summary>
    [Notify]
    public List<ColumnInfo> Columns { get; set; } = null!;

    /// <summary>
    /// Names of primary key columns.
    /// </summary>
    [Notify]
    public List<string> PrimaryKeyColumns { get; set; } = [];

    /// <summary>
    /// The editable data table.
    /// </summary>
    [Notify]
    public DataTable? EditableData { get; set; }

    /// <summary>
    /// Current view for display with sorting support.
    /// </summary>
    [Notify]
    public DataView? CurrentView { get; set; }

    /// <summary>
    /// Currently selected row in the grid.
    /// </summary>
    [Notify]
    public DataRowView? SelectedRowView { get; set; }

    /// <summary>
    /// Total number of rows.
    /// </summary>
    [Notify]
    public int TotalRowCount { get; set; }

    /// <summary>
    /// Page size for data loading.
    /// </summary>
    [Notify]
    public int PageSize { get; set; }

    /// <summary>
    /// Indicates if data is being loaded.
    /// </summary>
    [Notify]
    public bool IsLoading { get; set; }

    /// <summary>
    /// Error message if any operation failed.
    /// </summary>
    [Notify]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Indicates if there are uncommitted changes.
    /// </summary>
    [Notify]
    public bool HasChanges { get; private set; }

    /// <summary>
    /// True when the table cannot be edited safely and the tab is a viewer (WS-35). Today that means
    /// one thing: no primary key, so no row can be named.
    /// </summary>
    [Notify]
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Why editing is off, in words, for the banner. Grey buttons with no explanation send people
    /// looking for a setting that does not exist.
    /// </summary>
    [Notify]
    public string? ReadOnlyReason { get; private set; }

    /// <summary>
    /// Indicates if changes can be committed.
    /// </summary>
    [Notify]
    public bool CanCommit { get; private set; }

    /// <summary>
    /// Indicates if changes can be rolled back.
    /// </summary>
    [Notify]
    public bool CanRollback { get; private set; }

    /// <summary>
    /// Indicates if a new row can be added.
    /// </summary>
    [Notify]
    public bool CanAddRow { get; private set; }

    /// <summary>
    /// Indicates if the selected row can be deleted.
    /// </summary>
    [Notify]
    public bool CanDeleteRow { get; private set; }

    /// <summary>
    /// Indicates if data can be refreshed.
    /// </summary>
    [Notify]
    public bool CanRefresh { get; private set; }

    /// <summary>
    /// Settings for edit grid columns (persistence, visibility, order, etc.).
    /// </summary>
    [Notify]
    public GridColumnSettings EditColumnSettings { get; private set; } = null!;

    /// <summary>
    /// Status message for successful operations.
    /// </summary>
    [Notify]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Indicates if there is an error to display.
    /// </summary>
    [Notify]
    public bool HasError { get; private set; }

    /// <summary>
    /// Indicates if the last operation was successful.
    /// </summary>
    [Notify]
    public bool LastOperationSuccess { get; private set; }

    /// <summary>
    /// Indicates if the status bar should show default state.
    /// </summary>
    [Notify]
    public bool IsDefaultState { get; private set; }

    #endregion

    #region Commands

    public ICommand LoadDataCommand { get; private set; } = null!;

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand AddRowCommand { get; private set; } = null!;

    public ICommand DeleteRowCommand { get; private set; } = null!;

    public ICommand CommitCommand { get; private set; } = null!;

    public ICommand RollbackCommand { get; private set; } = null!;

    public ICommand CellEditedCommand { get; private set; } = null!;

    #endregion

    #region Services

    private IDatabaseService Database => ApplicationVm.Database;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
