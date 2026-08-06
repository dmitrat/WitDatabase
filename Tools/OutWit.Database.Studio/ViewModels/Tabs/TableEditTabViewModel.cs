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

    /// <summary>
    /// Where each page starts, by key: the key of the LAST row of the page before it. Index 0 is
    /// null - the first page starts at the beginning.
    ///
    /// Kept because a page is fetched with "WHERE key > anchor" rather than with OFFSET: OFFSET makes
    /// the engine walk everything it skips (measured: 1.4 s against 0.4 s at 400,000 rows), and it
    /// silently repeats or drops rows when the table changes underneath the reader.
    /// </summary>
    private readonly List<object?> m_pageAnchors = [null];

    #endregion

    #region Constructors

    public TableEditTabViewModel(ApplicationViewModel applicationVm, IDatabaseSession session, string tableName)
        : base(applicationVm, session)
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

        Filters = [];
        ConflictColumns = [];
        PageSizes = [200, DEFAULT_PAGE_SIZE, 5000, 0];
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
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
        NextPageCommand = new RelayCommandAsync(NextPageAsync);
        PreviousPageCommand = new RelayCommandAsync(PreviousPageAsync);
        FirstPageCommand = new RelayCommandAsync(FirstPageAsync);

        SortByCommand = new RelayCommandAsync<string>(SortByAsync);
        ApplyFiltersCommand = new RelayCommandAsync(ApplyFiltersAsync);
        ClearFiltersCommand = new RelayCommandAsync(ClearFiltersAsync);
        CountRowsCommand = new RelayCommandAsync(CountRowsAsync);
        ShowViewSqlCommand = new RelayCommand(ShowViewSql);
        ShowChangesSqlCommand = new RelayCommand(ShowChangesSql);
        RereadCommand = new RelayCommandAsync(ResolveByRereadingAsync);
        OverwriteCommand = new RelayCommandAsync(ResolveByOverwritingAsync);
    }

    #endregion

    #region WorkspaceTabViewModel

    public override WorkspaceTabType TabType => WorkspaceTabType.TableEdit;

    public override string IconPath => StudioIcons.PATH_DB_TABLE;

    /// <summary>
    /// The connection is part of the identity: the same table name in two databases is two different
    /// tables, and a tab keyed on the name alone would hand one database's user the other's rows.
    /// </summary>
    public override string? UniqueId => $"edit:{Session?.Id}:{TableName}";

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
        var session = Session;

        if (string.IsNullOrWhiteSpace(TableName) || session?.IsConnected != true)
            return;

        try
        {
            var columns = await session.GetColumnsAsync(TableName);
            Columns = columns.ToList();
            PrimaryKeyColumns = columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

            // A box per column, kept across reloads so that a refresh does not clear what was typed.
            if (Filters.Count == 0)
                Filters = columns
                    .Select(column => new ColumnFilter(column.Name, column.DataType ?? ""))
                    .ToList();

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
        var session = Session;

        if (string.IsNullOrWhiteSpace(TableName) || session?.IsConnected != true)
            return;

        IsLoading = true;
        ClearStatus();
        ClearChangeTracking();

        try
        {
            // One row more than the page is asked for: if it comes back, there is a next page. The
            // alternative is COUNT(*), which on this engine is a separate counter that can disagree
            // with the rows.
            var query = GridQuery.Page(View(PageIndex));

            Paging = query.Paging;
            ViewDescription = query.Description;
            IsDeepPage = query.Paging == GridPaging.Offset && PageIndex > 0;

            var result = await session.ExecuteQueryAsync(query.Statement);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                SetErrorStatus(result.ErrorMessage);
                return;
            }

            var table = result.Data;
            HasNextPage = PageSize > 0 && table != null && table.Rows.Count > PageSize;

            if (HasNextPage && table != null)
                table.Rows.RemoveAt(table.Rows.Count - 1);

            table?.AcceptChanges();

            m_originalData = table?.Copy();
            EditableData = table;

            if (EditableData != null)
            {
                CurrentView = new DataView(EditableData);
                TotalRowCount = EditableData.Rows.Count;
            }

            HasPreviousPage = PageIndex > 0;
            RememberPageEnd();

            SetSuccessStatus(DescribePage());
            ApplicationVm.MainWindowVm.StatusText =
                $"{DescribePage()} of table \"{TableName}\" in {session.DisplayName}";
            Logger.LogInformation("Loaded page {Page} ({Count} rows) of table {TableName}",
                PageIndex + 1, TotalRowCount, TableName);
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

    #endregion

    #region The view: sorting, filters, Show SQL

    /// <summary>
    /// What the grid is showing, as a question the engine can be asked (WS-30, WS-32). Everything -
    /// the page, Show SQL, the count - is built from this one value, so what is displayed and what is
    /// sent cannot drift apart.
    /// </summary>
    private GridView View(int pageIndex)
    {
        var conditions = new List<GridFilterCondition>();
        var index = 0;

        foreach (var filter in Filters)
        {
            var column = Columns.FirstOrDefault(candidate => candidate.Name == filter.Column);

            if (column == null)
                continue;

            var condition = GridFilter.Parse(filter.Text, column, index++);

            if (condition != null)
                conditions.Add(condition);
        }

        var anchor = pageIndex < m_pageAnchors.Count ? m_pageAnchors[pageIndex] : null;

        return new GridView(
            TableName,
            conditions,
            SortColumn,
            SortDescending,
            CanPageByKey ? PrimaryKeyColumns[0] : null,
            pageIndex,
            PageSize,
            anchor);
    }

    /// <summary>
    /// Sorting is a new query (WS-30). Sorting the page already fetched would sort a sample of the
    /// table and present it as the table - which is a lie the user only catches on page two.
    /// </summary>
    private async Task SortByAsync(string? column)
    {
        if (string.IsNullOrEmpty(column) || HasChanges)
            return;

        if (string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
            SortDescending = !SortDescending;
        else
        {
            SortColumn = column;
            SortDescending = false;
        }

        await ResetToFirstPageAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        if (HasChanges)
            return;

        await ResetToFirstPageAsync();
    }

    private async Task ClearFiltersAsync()
    {
        foreach (var filter in Filters)
            filter.Text = null;

        await ResetToFirstPageAsync();
    }

    /// <summary>
    /// Any change to the view starts again from the first page: the anchors belong to the old order
    /// and the old conditions, and reusing them would page through a table that no longer exists.
    /// </summary>
    private async Task ResetToFirstPageAsync()
    {
        m_pageAnchors.Clear();
        m_pageAnchors.Add(null);

        PageIndex = 0;
        TotalRows = null;

        await LoadTableDataAsync();
    }

    /// <summary>
    /// The total, when somebody asks for it (4.2). Never on its own: an unfiltered count on this
    /// engine is a counter kept beside the data, and a filtered one is a scan - neither is something
    /// to pay for on every page just to fill in a label.
    /// </summary>
    private async Task CountRowsAsync()
    {
        var session = Session;

        if (session?.IsConnected != true)
            return;

        var result = await session.ExecuteQueryAsync(GridQuery.Count(View(PageIndex)));

        if (result.Data is { Rows.Count: > 0 })
            TotalRows = Convert.ToInt64(result.Data.Rows[0][0]);

        UpdateStatus();
    }

    /// <summary>
    /// The bridge from clicks to the editor (WS-32): everything done by clicking is a SELECT, and
    /// showing it explains what happened and lets the user go on by hand where the clicks stop.
    /// </summary>
    private void ShowViewSql()
    {
        var session = Session;

        if (session == null)
            return;

        ApplicationVm.WorkspaceTabsVm.OpenQueryTab(GridQuery.Whole(View(PageIndex)).ToDisplaySql(),
            $"SQL of {TableName}", session);
    }

    /// <summary>
    /// The other direction: the edit buffer as the transaction it will become - BEFORE it is applied,
    /// which is the only moment at which it is useful.
    /// </summary>
    private void ShowChangesSql()
    {
        var session = Session;

        if (session == null)
            return;

        var statements = BuildChangeScript(out var error);

        if (statements == null)
        {
            SetErrorStatus(error!);
            return;
        }

        var script = statements.Count == 0
            ? "-- there is nothing in the edit buffer"
            : "BEGIN TRANSACTION;\n"
              + string.Join("\n", statements.Select(statement => statement.ToDisplaySql() + ";"))
              + "\nCOMMIT;";

        ApplicationVm.WorkspaceTabsVm.OpenQueryTab(script, $"Changes to {TableName}", session);
    }

    #endregion

    #region Paging

    /// <summary>
    /// Keyset paging needs exactly one key column: two columns need a row-value comparison, which is
    /// a different question from this one. A composite key pages by OFFSET and says so.
    /// </summary>
    public bool CanPageByKey => PrimaryKeyColumns.Count == 1;

    /// <summary>
    /// Remembers where this page ended, so the next one can start there.
    /// </summary>
    private void RememberPageEnd()
    {
        if (Paging == GridPaging.Offset || !CanPageByKey || EditableData == null || EditableData.Rows.Count == 0)
            return;

        var lastKey = EditableData.Rows[^1][PrimaryKeyColumns[0]];

        while (m_pageAnchors.Count <= PageIndex + 1)
            m_pageAnchors.Add(null);

        m_pageAnchors[PageIndex + 1] = lastKey;
    }

    private async Task NextPageAsync()
    {
        if (!HasNextPage || HasChanges)
            return;

        PageIndex++;
        await LoadTableDataAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (PageIndex == 0 || HasChanges)
            return;

        PageIndex--;
        await LoadTableDataAsync();
    }

    private async Task FirstPageAsync()
    {
        if (PageIndex == 0 || HasChanges)
            return;

        PageIndex = 0;
        await LoadTableDataAsync();
    }

    /// <summary>
    /// What the status line says about the page. Never a total: a total is COUNT(*), and on this
    /// engine that is a counter kept beside the data rather than the data.
    /// </summary>
    private string DescribePage()
    {
        var where = PageIndex == 0 ? "" : $" (page {PageIndex + 1})";
        var more = HasNextPage ? ", more to come" : "";

        return $"Loaded {TotalRowCount} rows{where}{more}";
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

        // The buffer goes to the connection this tab was opened in, and to no other. If that
        // connection has been closed the buffer stays where it is: it is unapplied work, and the
        // honest thing is to say so rather than to apply it somewhere else (WS-3, WS-13).
        var session = Session;

        if (session?.IsConnected != true)
        {
            SetErrorStatus($"Nothing was applied: the connection this tab belongs to "
                + $"({ConnectionName ?? "unknown"}) is closed.");
            return;
        }

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

            var result = await session.ExecuteBatchAsync(statements);

            if (!result.Committed)
            {
                // The buffer is deliberately left alone, and the table is NOT reloaded: a reload would
                // throw away exactly the work the user was told had not been saved.
                SetErrorStatus(
                    $"Nothing was applied. Statement {result.FailedIndex + 1} of {statements.Count} "
                    + $"failed: {result.ErrorMessage}");

                if (result.IsConflict)
                    await DescribeConflictAsync(session, result.FailedIndex, statements.Count);

                Logger.LogWarning("Commit to {TableName} rolled back at statement {Index}",
                    TableName, result.FailedIndex + 1);
                return;
            }

            ClearConflict();

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
    private List<SqlStatement>? BuildChangeScript(out string? error)
    {
        error = null;

        var statements = new List<SqlStatement>();
        var table = SqlValueFormatter.EscapeIdentifier(TableName);

        foreach (var row in m_deletedRows)
        {
            var originalRowIndex = FindOriginalRowIndex(row);

            if (originalRowIndex < 0)
            {
                error = "Cannot delete a row: the row it was loaded from could not be found. Refresh and try again.";
                return null;
            }

            var where = OverwriteConflicts
                ? BuildWhereClause(m_originalData!.Rows[originalRowIndex], "w")
                : BuildVersionedWhereClause(m_originalData!.Rows[originalRowIndex], "w");

            if (where == null)
            {
                error = "Cannot delete a row: the table has no primary key.";
                return null;
            }

            statements.Add(new SqlStatement(
                $"DELETE FROM [{table}] WHERE {where.Value.Clause}", where.Value.Parameters,
                ExpectedRows: OverwriteConflicts ? null : 1));
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

            var where = OverwriteConflicts
                ? BuildWhereClause(m_originalData!.Rows[originalRowIndex], "w")
                : BuildVersionedWhereClause(m_originalData!.Rows[originalRowIndex], "w");

            if (where == null)
            {
                error = "Cannot update a row: the table has no primary key.";
                return null;
            }

            statements.Add(BuildUpdateStatement(modifiedRow, where.Value));
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

    #region Conflict

    /// <summary>
    /// Reads the row as it is in the database now and puts it beside what the user has (WS-37).
    ///
    /// "The transaction was rejected" is what this replaces, and it leaves the user with no way to
    /// decide anything. Both values, side by side, turn it into a choice: re-read, or apply over.
    /// </summary>
    private async Task DescribeConflictAsync(IDatabaseSession session, int failedIndex, int total)
    {
        ConflictColumns.Clear();

        var row = ConflictRow(failedIndex);

        if (row == null)
        {
            HasConflict = true;
            ConflictSummary = "A row was changed by another connection after it was read here.";
            return;
        }

        var where = BuildWhereClause(row, "c");

        if (where == null)
            return;

        var current = await session.ExecuteQueryAsync(new SqlStatement(
            $"SELECT * FROM [{SqlValueFormatter.EscapeIdentifier(TableName)}] WHERE {where.Value.Clause}",
            where.Value.Parameters));

        var key = string.Join(", ",
            PrimaryKeyColumns.Select(column => $"{column} = {SqlValueFormatter.FormatForSql(row[column])}"));

        if (current.Data == null || current.Data.Rows.Count == 0)
        {
            HasConflict = true;
            ConflictSummary = $"The row {key} has been DELETED by another connection.";
            return;
        }

        var live = current.Data.Rows[0];

        // "Mine" is the value in the EDIT BUFFER, not the one the row was read with: the user is
        // deciding between what they typed and what is in the database, and the row they loaded from
        // is of no interest to either side. The original row is only what the WHERE was built from.
        var edited = EditedRowFor(row) ?? row;

        foreach (DataColumn column in row.Table.Columns)
        {
            if (!current.Data.Columns.Contains(column.ColumnName))
                continue;

            var mine = edited.RowState == DataRowState.Deleted ? row[column] : edited[column.ColumnName];
            var theirs = live[column.ColumnName];

            if (Equals(mine, theirs))
                continue;

            ConflictColumns.Add(new ConflictedValue(
                column.ColumnName,
                SqlValueFormatter.FormatForSql(mine),
                SqlValueFormatter.FormatForSql(theirs)));
        }

        HasConflict = true;
        ConflictSummary = $"The row {key} was changed by another connection after it was read here. "
            + $"Statement {failedIndex + 1} of {total} matched nothing, so nothing was applied.";
    }

    /// <summary>
    /// The row the failed statement was built from - deletes come first in the script, then inserts,
    /// then updates, which is the order <see cref="BuildChangeScript"/> writes them in.
    /// </summary>
    private DataRow? ConflictRow(int failedIndex)
    {
        var deletes = m_deletedRows.ToList();

        if (failedIndex < deletes.Count)
            return Original(deletes[failedIndex]);

        var afterInserts = failedIndex - deletes.Count - m_newRows.Count;
        var updates = m_modifiedRows
            .Where(row => row.RowState is not (DataRowState.Deleted or DataRowState.Detached))
            .ToList();

        return afterInserts >= 0 && afterInserts < updates.Count ? Original(updates[afterInserts]) : null;
    }

    private DataRow? Original(DataRow row)
    {
        var index = FindOriginalRowIndex(row);

        return index >= 0 ? m_originalData!.Rows[index] : null;
    }

    /// <summary>
    /// The buffer's row that the given ORIGINAL row belongs to - the other direction of
    /// <see cref="Original"/>, and what carries the values the user typed.
    /// </summary>
    private DataRow? EditedRowFor(DataRow original)
    {
        if (PrimaryKeyColumns.Count == 0 || EditableData == null)
            return null;

        foreach (DataRow row in EditableData.Rows)
        {
            if (row.RowState == DataRowState.Deleted)
                continue;

            if (PrimaryKeyColumns.All(key => Equals(row[key], original[key])))
                return row;
        }

        return null;
    }

    /// <summary>
    /// Throws the buffer away and reads the page again - the "re-read" half of the choice.
    /// </summary>
    private async Task ResolveByRereadingAsync()
    {
        ClearConflict();
        ClearChangeTracking();

        await LoadTableDataAsync();
    }

    /// <summary>
    /// Applies the same edits without the version check - the "apply over" half.
    ///
    /// Deliberately a separate press: it overwrites somebody else's work, which is a decision, and a
    /// decision is not something a client should make on a user's behalf because a retry is easier.
    /// </summary>
    private async Task ResolveByOverwritingAsync()
    {
        ClearConflict();

        OverwriteConflicts = true;

        try
        {
            await CommitChangesAsync();
        }
        finally
        {
            OverwriteConflicts = false;
        }
    }

    private void ClearConflict()
    {
        HasConflict = false;
        ConflictSummary = null;
        ConflictColumns.Clear();
    }

    #endregion

    #region SQL Building

    /// <summary>
    /// The condition that names exactly one row: its primary key, or nothing.
    ///
    /// There used to be a fallback that built the condition from EVERY column of the row. That is not
    /// a unique condition - two identical rows both match it, so an UPDATE meant for one changed both,
    /// and a DELETE meant for one removed both. It also compared BLOB columns in a WHERE clause. The
    /// fallback is gone, and a table without a key is not editable at all (WS-35).
    ///
    /// The key's VALUE is bound, not written into the text: a key can be a string, and a string can
    /// contain a quote.
    /// </summary>
    private (string Clause, List<Models.SqlParameter> Parameters)? BuildWhereClause(DataRow row, string prefix)
    {
        if (PrimaryKeyColumns.Count == 0)
            return null;

        var conditions = new List<string>();
        var parameters = new List<Models.SqlParameter>();

        for (var i = 0; i < PrimaryKeyColumns.Count; i++)
        {
            var name = $"@{prefix}{i}";
            var column = SqlValueFormatter.EscapeIdentifier(PrimaryKeyColumns[i]);

            conditions.Add($"[{column}] = {name}");
            parameters.Add(new Models.SqlParameter(name, row[PrimaryKeyColumns[i]]));
        }

        return (string.Join(" AND ", conditions), parameters);
    }

    /// <summary>
    /// The same condition, plus the values the row was READ with (WS-37).
    ///
    /// The key alone names the row; these name the version of it. If somebody else changed the row
    /// since it was loaded, the statement matches nothing, the count says so and the whole set is
    /// rolled back - which is the only way this engine can be asked the question, since it has no
    /// optimistic concurrency of its own.
    ///
    /// BLOB columns are left out: comparing a megabyte in a WHERE clause to find out whether it moved
    /// costs more than the edit, and the key plus the other columns is already a version.
    /// </summary>
    private (string Clause, List<Models.SqlParameter> Parameters)? BuildVersionedWhereClause(
        DataRow original, string prefix)
    {
        var key = BuildWhereClause(original, prefix);

        if (key == null)
            return null;

        var conditions = new List<string> { key.Value.Clause };
        var parameters = new List<Models.SqlParameter>(key.Value.Parameters);

        foreach (DataColumn column in original.Table.Columns)
        {
            if (PrimaryKeyColumns.Contains(column.ColumnName))
                continue;

            if (column.DataType == typeof(byte[]))
                continue;

            var value = original[column];
            var escaped = SqlValueFormatter.EscapeIdentifier(column.ColumnName);

            if (value == DBNull.Value || value == null)
            {
                conditions.Add($"[{escaped}] IS NULL");
                continue;
            }

            var name = $"@{prefix}v{parameters.Count}";

            conditions.Add($"[{escaped}] = {name}");
            parameters.Add(new Models.SqlParameter(name, value));
        }

        return (string.Join(" AND ", conditions), parameters);
    }

    private SqlStatement BuildInsertStatement(DataRow row)
    {
        var columns = new List<string>();
        var placeholders = new List<string>();
        var parameters = new List<Models.SqlParameter>();

        foreach (DataColumn col in row.Table.Columns)
        {
            var value = row[col];

            var columnInfo = Columns.FirstOrDefault(c => c.Name == col.ColumnName);
            if (columnInfo?.IsAutoIncrement == true && (value == DBNull.Value || value == null))
                continue;

            var name = $"@v{parameters.Count}";

            columns.Add($"[{SqlValueFormatter.EscapeIdentifier(col.ColumnName)}]");
            placeholders.Add(name);
            parameters.Add(new Models.SqlParameter(name, value));
        }

        return new SqlStatement(
            $"INSERT INTO [{SqlValueFormatter.EscapeIdentifier(TableName)}] "
            + $"({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})",
            parameters);
    }

    private SqlStatement BuildUpdateStatement(DataRow row, (string Clause, List<Models.SqlParameter> Parameters) where)
    {
        var setClauses = new List<string>();
        var parameters = new List<Models.SqlParameter>();

        foreach (DataColumn col in row.Table.Columns)
        {
            if (PrimaryKeyColumns.Contains(col.ColumnName))
                continue;

            var name = $"@s{parameters.Count}";

            setClauses.Add($"[{SqlValueFormatter.EscapeIdentifier(col.ColumnName)}] = {name}");
            parameters.Add(new Models.SqlParameter(name, row[col]));
        }

        parameters.AddRange(where.Parameters);

        return new SqlStatement(
            $"UPDATE [{SqlValueFormatter.EscapeIdentifier(TableName)}] "
            + $"SET {string.Join(", ", setClauses)} WHERE {where.Clause}",
            parameters,
            ExpectedRows: OverwriteConflicts ? null : 1);
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
        CanAddRow = !string.IsNullOrWhiteSpace(TableName) && !IsLoading && Session?.IsConnected == true && !IsReadOnly;
        CanDeleteRow = SelectedRowView != null && !IsLoading && !IsReadOnly;
        CanRefresh = !string.IsNullOrWhiteSpace(TableName) && !IsLoading && Session?.IsConnected == true;
        CanGoToNextPage = HasNextPage && !IsLoading && !HasChanges;
        CanGoToPreviousPage = HasPreviousPage && !IsLoading && !HasChanges;

        PagingNote = !CanPageByKey && PageIndex > 0
            ? "This table has no single-column primary key, so pages are counted from the start of "
              + "the table: the further in you go, the longer it takes."
            : null;
        
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

    protected override void OnSessionStatusChanged(bool isConnected)
    {
        // The tab is closed by WorkspaceTabsViewModel when ITS connection closes - and only then.
        UpdateStatus();
    }

    protected override void OnSessionChanged()
    {
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
    /// How many rows are on a page (WS-31).
    /// </summary>
    [Notify]
    public int PageSize { get; set; }

    /// <summary>
    /// Which page is shown, zero-based.
    /// </summary>
    [Notify]
    public int PageIndex { get; private set; }

    /// <summary>
    /// Whether there are rows after this page. Known because the page is fetched one row longer than
    /// it is shown - not from a count.
    /// </summary>
    [Notify]
    public bool HasNextPage { get; private set; }

    [Notify]
    public bool HasPreviousPage { get; private set; }

    [Notify]
    public bool CanGoToNextPage { get; private set; }

    [Notify]
    public bool CanGoToPreviousPage { get; private set; }

    /// <summary>
    /// True when this page is being reached by counting rows from the beginning of the table rather
    /// than by key - which is what OFFSET does, and what it costs.
    /// </summary>
    [Notify]
    public bool IsDeepPage { get; private set; }

    /// <summary>
    /// Says out loud why paging is slow here, when it is. A grey "next page" button that takes four
    /// seconds with no explanation is the thing this avoids.
    /// </summary>
    [Notify]
    public string? PagingNote { get; private set; }

    /// <summary>
    /// How the current page is being reached (WS-31), which is a statement about what it costs.
    /// </summary>
    [Notify]
    public GridPaging Paging { get; private set; }

    /// <summary>
    /// The filters and the sorting in words, for the footer: "2 filters: Total &gt; 100, Status
    /// contains ship · sorted by Total descending".
    /// </summary>
    [Notify]
    public string? ViewDescription { get; private set; }

    /// <summary>
    /// One filter box per column (4.3). The boxes exist whether or not anything is typed in them, so
    /// that the row of them is part of the grid rather than something that appears.
    /// </summary>
    public List<ColumnFilter> Filters { get; private set; } = null!;

    [Notify]
    public string? SortColumn { get; private set; }

    [Notify]
    public bool SortDescending { get; private set; }

    /// <summary>
    /// How many rows the view has - null until somebody asks, because asking costs a scan when there
    /// is a filter and reads a separate counter when there is not (4.2).
    /// </summary>
    [Notify]
    public long? TotalRows { get; private set; }

    /// <summary>
    /// 200 / 1000 / 5000 / everything, as the design offers. Zero is "everything", and it warns.
    /// </summary>
    public IReadOnlyList<int> PageSizes { get; private set; } = null!;

    /// <summary>
    /// True while a conflict is being shown, which is a question rather than a failure (WS-37).
    /// </summary>
    [Notify]
    public bool HasConflict { get; private set; }

    [Notify]
    public string? ConflictSummary { get; private set; }

    /// <summary>
    /// Column by column: what the user has, and what is in the database now.
    /// </summary>
    public List<ConflictedValue> ConflictColumns { get; private set; } = null!;

    /// <summary>
    /// Set only between "Apply over" and the commit it triggers: the version check is dropped, so the
    /// edits overwrite whatever is there now. Never the default, and never sticky.
    /// </summary>
    public bool OverwriteConflicts { get; private set; }

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

    public ICommand NextPageCommand { get; private set; } = null!;

    public ICommand PreviousPageCommand { get; private set; } = null!;

    public ICommand FirstPageCommand { get; private set; } = null!;

    public ICommand SortByCommand { get; private set; } = null!;

    public ICommand ApplyFiltersCommand { get; private set; } = null!;

    public ICommand ClearFiltersCommand { get; private set; } = null!;

    public ICommand CountRowsCommand { get; private set; } = null!;

    /// <summary>
    /// WS-32, one way: the view as a SELECT, in a query tab.
    /// </summary>
    public ICommand ShowViewSqlCommand { get; private set; } = null!;

    /// <summary>
    /// WS-32, the other: the edit buffer as the transaction it will become.
    /// </summary>
    public ICommand ShowChangesSqlCommand { get; private set; } = null!;

    public ICommand RereadCommand { get; private set; } = null!;

    public ICommand OverwriteCommand { get; private set; } = null!;

    #endregion

    #region Services

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
