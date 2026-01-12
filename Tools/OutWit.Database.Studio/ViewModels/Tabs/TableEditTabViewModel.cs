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

    public override bool CanClose()
    {
        // TODO: Show confirmation dialog if HasChanges
        return true;
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
        ErrorMessage = null;
        ClearChangeTracking();

        try
        {
            // Build SQL with ORDER BY if primary keys exist
            var sql = BuildSelectStatement();
            var result = await Database.ExecuteQueryAsync(sql);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                ErrorMessage = result.ErrorMessage;
                return;
            }

            m_originalData = result.Data?.Copy();
            EditableData = result.Data;

            if (EditableData != null)
            {
                CurrentView = new DataView(EditableData);
                TotalRowCount = EditableData.Rows.Count;
            }

            ApplicationVm.MainWindowVm.StatusText = $"Loaded {TotalRowCount} rows from table \"{TableName}\"";
            Logger.LogInformation("Loaded {Count} rows from table {TableName}", TotalRowCount, TableName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load data: {ex.Message}";
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

    private async Task CommitChangesAsync()
    {
        if (EditableData == null || string.IsNullOrWhiteSpace(TableName) || m_originalData == null)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var errors = new List<string>();

            // Process deletions
            foreach (var row in m_deletedRows)
            {
                var originalRowIndex = FindOriginalRowIndex(row);
                if (originalRowIndex < 0)
                {
                    errors.Add("Cannot delete row: original row not found");
                    continue;
                }

                var originalRow = m_originalData.Rows[originalRowIndex];
                var whereClause = BuildWhereClause(originalRow);

                if (string.IsNullOrEmpty(whereClause))
                {
                    errors.Add("Cannot delete row: no primary key or unique identifier");
                    continue;
                }

                var deleteSql = $"DELETE FROM [{SqlValueFormatter.EscapeIdentifier(TableName)}] WHERE {whereClause}";
                Logger.LogDebug("Executing DELETE: {Sql}", deleteSql);

                try
                {
                    await Database.ExecuteNonQueryAsync(deleteSql);
                }
                catch (Exception ex)
                {
                    errors.Add($"Delete failed: {ex.Message}");
                }
            }

            // Process new rows
            foreach (var newRow in m_newRows)
            {
                var insertSql = BuildInsertStatement(newRow);
                Logger.LogDebug("Executing INSERT: {Sql}", insertSql);

                try
                {
                    await Database.ExecuteNonQueryAsync(insertSql);
                }
                catch (Exception ex)
                {
                    errors.Add($"Insert failed: {ex.Message}");
                }
            }

            // Process modifications
            foreach (var modifiedRow in m_modifiedRows)
            {
                if (modifiedRow.RowState == DataRowState.Deleted || modifiedRow.RowState == DataRowState.Detached)
                    continue;

                var originalRowIndex = FindOriginalRowIndex(modifiedRow);
                if (originalRowIndex < 0)
                {
                    errors.Add("Cannot update row: original row not found");
                    continue;
                }

                var originalRow = m_originalData.Rows[originalRowIndex];
                var whereClause = BuildWhereClause(originalRow);

                if (string.IsNullOrEmpty(whereClause))
                {
                    errors.Add("Cannot update row: no primary key or unique identifier");
                    continue;
                }

                var updateSql = BuildUpdateStatement(modifiedRow, whereClause);
                Logger.LogDebug("Executing UPDATE: {Sql}", updateSql);

                try
                {
                    var rowsAffected = await Database.ExecuteNonQueryAsync(updateSql);
                    Logger.LogDebug("UPDATE affected {Rows} rows", rowsAffected);
                }
                catch (Exception ex)
                {
                    errors.Add($"Update failed: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                ErrorMessage = string.Join("\n", errors);
            }
            else
            {
                ApplicationVm.MainWindowVm.StatusText = "Changes committed successfully";
                IsModified = false;
            }

            await LoadTableDataAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Commit failed: {ex.Message}";
            Logger.LogError(ex, "Failed to commit changes to table {TableName}", TableName);
        }
        finally
        {
            IsLoading = false;
            UpdateStatus();
        }
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

    private string BuildWhereClause(DataRow row)
    {
        var conditions = new List<string>();

        if (PrimaryKeyColumns.Count > 0)
        {
            foreach (var pkColumn in PrimaryKeyColumns)
            {
                var value = row[pkColumn];
                conditions.Add($"[{SqlValueFormatter.EscapeIdentifier(pkColumn)}] = {SqlValueFormatter.FormatForSql(value)}");
            }
        }
        else
        {
            foreach (DataColumn col in row.Table.Columns)
            {
                var value = row[col];
                if (value == DBNull.Value)
                    conditions.Add($"[{SqlValueFormatter.EscapeIdentifier(col.ColumnName)}] IS NULL");
                else
                    conditions.Add($"[{SqlValueFormatter.EscapeIdentifier(col.ColumnName)}] = {SqlValueFormatter.FormatForSql(value)}");
            }
        }

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
        CanCommit = HasChanges && !IsLoading;
        CanRollback = HasChanges && !IsLoading;
        CanAddRow = !string.IsNullOrWhiteSpace(TableName) && !IsLoading && Database.IsConnected;
        CanDeleteRow = SelectedRowView != null && !IsLoading;
        CanRefresh = !string.IsNullOrWhiteSpace(TableName) && !IsLoading && Database.IsConnected;
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
