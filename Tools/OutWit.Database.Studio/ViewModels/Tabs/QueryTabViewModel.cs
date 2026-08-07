using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Windows.Input;
using Avalonia.Controls;
// Avalonia 12 moved SetTextAsync off IClipboard and onto ClipboardExtensions.
using Avalonia.Input.Platform;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.Locker;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Ui.Icons;
using OutWit.Database.Studio.Views.Dialogs;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// Represents a query editor tab with its content and state.
/// </summary>
public partial class QueryTabViewModel : WorkspaceTabViewModel
{
    #region Fields

    private CancellationTokenSource? m_executionCts;

    #endregion

    #region Constructors

    public QueryTabViewModel(ApplicationViewModel applicationViewModel, IDatabaseSession? session)
        : base(applicationViewModel, session)
    {
        InitDefaults();
        InitCommands();
        InitWorkspace();
        InitEvents();

        UpdateTransactionState();
    }

    #endregion

    #region Initialization

    private void InitDefaults()
    {
        // Initialize column settings for result grid
        ResultColumnSettings = new GridColumnSettings();

        Statements = [];
    }

    private void InitCommands()
    {
        ExecuteQueryCommand = new RelayCommandAsync(ExecuteQueryAsync);
        ExecuteSelectionCommand = new RelayCommandAsync(ExecuteSelectionAsync);
        ExecuteCurrentStatementCommand = new RelayCommandAsync(ExecuteCurrentStatementAsync);
        StopQueryCommand = new RelayCommand(StopQuery);
        ClearResultsCommand = new RelayCommand(ClearResults);
        CopyRowsCommand = new RelayCommandAsync(CopyRowsAsync);
        CopyRowsAsInsertCommand = new RelayCommandAsync(CopyRowsAsInsertAsync);
        CopyRowsAsCsvCommand = new RelayCommandAsync(CopyRowsAsCsvAsync);
        CopyAllRowsCommand = new RelayCommandAsync(CopyAllRowsAsync);
        CopyAllRowsAsInsertCommand = new RelayCommandAsync(CopyAllRowsAsInsertAsync);
        ExportResultsCommand = new RelayCommandAsync(ExportResultsAsync, () => HasResults);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
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

        m_syntaxCts?.Cancel();
        m_syntaxCts?.Dispose();
        m_syntaxCts = null;

        if (Session != null)
            Session.TransactionChanged -= OnTransactionChanged;

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

    /// <summary>
    /// F5: the statement the cursor is in, and only that one (WS-25).
    ///
    /// The alternative - F5 runs everything - is the shape of a mistake that cannot be undone: a
    /// cursor left in the middle of a script of forty statements is not a request to run forty. If
    /// the text has only one statement the two are the same thing, which is the common case and the
    /// reason nobody notices the difference until the day it matters.
    /// </summary>
    public async Task ExecuteCurrentStatementAsync()
    {
        var text = SqlText ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return;

        var split = SqlScript.Split(text);

        if (!split.IsSuccess)
        {
            // The whole text does not parse: report it where it is, exactly as running it would.
            await ExecuteSqlAsync(text);
            return;
        }

        var statement = SqlScript.At(split, Math.Clamp(CaretOffset, 0, text.Length));

        if (statement == null)
            return;

        CurrentStatementText = statement.Text;

        // The origin is known here, so it is passed rather than searched for: two identical
        // statements in one script would otherwise both be found at the position of the first.
        await ExecuteSqlAsync(statement.Text, (statement.Line - 1, statement.Column));
    }

    /// <summary>
    /// Runs the text as a SCRIPT: cut into statements by the parser and executed one at a time
    /// (WS-22). One command for the whole text is what this replaces, and it could report only one
    /// outcome for seven statements, no progress, and an error whose line number was of no use once
    /// the text had been sent somewhere else.
    ///
    /// A failure stops the run. The statements before it have already reached the database - each is
    /// its own transaction unless the script opened one - and saying which they were is the reason
    /// <see cref="Statements"/> exists.
    /// </summary>
    public async Task ExecuteSqlAsync(string sql, (int Line, int Column)? origin = null)
    {
        // The session this tab belongs to, never the one selected in the tree (WS-3).
        var session = Session;

        if (session == null || !session.IsConnected)
        {
            ErrorMessage = ConnectionName == null
                ? "Not connected to database"
                : $"The connection this tab belongs to ({ConnectionName}) is closed.";
            return;
        }

        IsExecuting = true;
        ErrorMessage = null;
        ErrorLine = 0;
        ErrorColumn = 0;
        ErrorLength = 0;
        ErrorSuggestion = null;
        ErrorName = null;
        Statements.Clear();
        DdlWasExecuted = false;

        m_executionCts?.Dispose();
        m_executionCts = new CancellationTokenSource();

        var split = SqlScript.Split(sql);

        // The offset of the fragment inside the tab's own text: executing a SELECTION means the
        // parser counts from the start of the selection, and an error found there still has to be
        // shown where the user is looking.
        var fragmentOffset = origin ?? FragmentOffset(sql);

        if (!split.IsSuccess)
        {
            var first = split.Errors[0];
            ReportError(first, fragmentOffset);
            IsExecuting = false;
            UpdateStatus();
            return;
        }

        StatementCount = split.Statements.Count;

        try
        {
            foreach (var statement in split.Statements)
            {
                CurrentStatementNumber = statement.Index + 1;

                var result = await session.ExecuteQueryAsync(
                    SqlStatement.Of(statement.Text), m_executionCts.Token);

                var outcome = new StatementOutcome
                {
                    Number = statement.Index + 1,
                    Summary = statement.Summary,
                    RowsAffected = result.RowsAffected,
                    ReturnedRows = result.ReturnedRows,
                    ExecutionTimeMs = result.ExecutionTimeMs,
                    ErrorMessage = result.ErrorMessage == null ? null : SqlScript.Shorten(result.ErrorMessage)
                };

                Statements.Add(outcome);
                ExecutionTimeMs = Statements.Sum(s => s.ExecutionTimeMs);

                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    // Where in the TAB, not where in the statement: the engine counts from the start
                    // of whatever it was given, which here is one statement of many.
                    var located = SqlScript.ErrorFor(statement, result.ErrorMessage)
                        ?? new SqlError(statement.Line, statement.Column,
                            SqlScript.Shorten(result.ErrorMessage), result.ErrorMessage);

                    ReportError(located, fragmentOffset, statement.Index + 1, split.Statements.Count);

                    // A missing table or column carries no position at all, so it is found in the
                    // text afterwards - and the nearest name this database does have is offered.
                    LocateObjectError(result.ErrorMessage, statement, fragmentOffset);

                    await RecordInHistoryAsync(sql, "error", 0);
                    return;
                }

                if (result.ReturnedRows)
                {
                    SetResultData(result.Data);
                    RowsAffected = result.RowsAffected;
                }
                else
                {
                    RowsAffected = result.RowsAffected;
                }

                if (statement.ChangesSchema)
                    DdlWasExecuted = true;
            }

            ApplicationVm.MainWindowVm.StatusText = split.Statements.Count == 1
                ? $"Query executed successfully in {ExecutionTimeMs:F2}ms"
                : $"{split.Statements.Count} statements executed in {ExecutionTimeMs:F2}ms";

            await RecordInHistoryAsync(sql, "ok", TotalRowCount);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = Localization.Format("Query.CancelledAt", CurrentStatementNumber, StatementCount);
            ApplicationVm.MainWindowVm.StatusText = Localization["Query.Cancelled"];

            await RecordInHistoryAsync(sql, "cancelled", 0);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Logger.LogError(ex, "Query execution failed");
            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Query.Failed", ex.Message);
        }
        finally
        {
            IsExecuting = false;
            CurrentStatementNumber = 0;
            UpdateStatus();
            UpdateTransactionState();
        }
    }

    /// <summary>
    /// Where the executed fragment begins inside the tab's text, as a line and a column. Executing
    /// the whole text gives (0, 0); executing a selection gives wherever the selection starts.
    /// </summary>
    private (int Line, int Column) FragmentOffset(string fragment)
    {
        var text = SqlText ?? string.Empty;

        if (string.IsNullOrEmpty(text) || ReferenceEquals(text, fragment))
            return (0, 0);

        var index = text.IndexOf(fragment, StringComparison.Ordinal);

        if (index < 0)
            return (0, 0);

        var before = text[..index];
        var line = before.Count(c => c == '\n');
        var lastBreak = before.LastIndexOf('\n');

        return (line, index - (lastBreak + 1));
    }

    private void ReportError(SqlError error, (int Line, int Column) fragment, int number = 0, int total = 0)
    {
        // The fragment's own offset is added last, so a selection three screens down underlines the
        // line the user is looking at rather than the third line of the file.
        ErrorLine = error.Line + fragment.Line;
        ErrorColumn = error.Line == 1 ? error.Column + fragment.Column : error.Column;

        var where = number > 0 && total > 1
            ? $"Statement {number} of {total}, line {ErrorLine}, column {ErrorColumn + 1}: "
            : $"Line {ErrorLine}, column {ErrorColumn + 1}: ";

        ErrorMessage = where + error.Message;
        ErrorDetail = error.Detail;
        ErrorLength = 1;

        UpdateUnderline();

        SetResultData(null);

        ApplicationVm.MainWindowVm.StatusText = ErrorMessage;
        Logger.LogWarning("Execution failed at line {Line}: {Message}", ErrorLine, error.Message);
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
        HasExecutionResult = false;

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

    private async Task ExportResultsAsync()
    {
        if (!HasResults || ResultData == null)
            return;

        var mainWindow = ApplicationVm.MainWindow;
        if (mainWindow == null)
            return;

        var exportVm = new ExportViewModel(ApplicationVm);
        exportVm.SetDataSource(ResultData, Title);

        await ExportDialog.ShowAsync(mainWindow, exportVm);
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
        HasExecutionResult = HasMessages || ExecutionTimeMs > 0;
        CanExecuteQuery = Session?.IsConnected == true && !IsExecuting && !string.IsNullOrWhiteSpace(SqlText);

        var selectedCount = SelectedRows?.Count ?? 0;
        CanCopyRows = HasResults && (selectedCount > 0 || CurrentView?.Count > 0);
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (GlobalLocker.IsLocked(nameof(QueryTabViewModel)))
            return;

        if (e.IsProperty((QueryTabViewModel vm) => vm.TotalRowCount) ||
            e.IsProperty((QueryTabViewModel vm) => vm.ErrorMessage) ||
            e.IsProperty((QueryTabViewModel vm) => vm.RowsAffected) ||
            e.IsProperty((QueryTabViewModel vm) => vm.CurrentView) ||
            e.IsProperty((QueryTabViewModel vm) => vm.SelectedRows) ||
            e.IsProperty((QueryTabViewModel vm) => vm.IsExecuting) ||
            e.IsProperty((QueryTabViewModel vm) => vm.SqlText))
        {
            UpdateStatus();
        }

        if (e.IsProperty((QueryTabViewModel vm) => vm.SqlText))
        {
            // The underline follows the text, on a delay (3.6). Fire and forget: the check cancels its
            // own previous run, and a keystroke must not wait for a parse.
            _ = CheckSyntaxAsync();
        }

        // Opening the History panel is the request to see the history (3.7).
        if (e.IsProperty((QueryTabViewModel vm) => vm.IsHistorySelected) && IsHistorySelected)
            _ = RefreshHistoryAsync();
    }

    protected override void OnSessionStatusChanged(bool isConnected)
    {
        UpdateStatus();
        UpdateTransactionState();
    }

    protected override void OnSessionChanged()
    {
        UpdateStatus();
        UpdateTransactionState();
        WatchTransaction();
    }

    /// <summary>
    /// Follows the connection's transaction, which another tab of the same connection may have opened
    /// or ended (WS-26).
    /// </summary>
    private void WatchTransaction()
    {
        if (Session == null)
            return;

        Session.TransactionChanged -= OnTransactionChanged;
        Session.TransactionChanged += OnTransactionChanged;
    }

    private void OnTransactionChanged(object? sender, EventArgs e)
    {
        UpdateTransactionState();
    }

    #endregion

    #region Properties

    /// <summary>
    /// SQL text content of the query.
    /// </summary>
    [Notify]
    public string SqlText { get; set; } = string.Empty;

    /// <summary>
    /// Where the caret is in <see cref="SqlText"/>, in characters. Written by the editor; read by
    /// F5 to find the statement under the cursor.
    /// </summary>
    [Notify]
    public int CaretOffset { get; set; }

    /// <summary>
    /// The same position for a person: 1-based line and 1-based column, as the status bar shows it.
    /// </summary>
    [Notify]
    public int CaretLine { get; set; } = 1;

    [Notify]
    public int CaretColumn { get; set; } = 1;

    /// <summary>
    /// The text of the statement F5 last ran. Kept so that what happened can be told from what was
    /// asked for.
    /// </summary>
    [Notify]
    public string? CurrentStatementText { get; set; }

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
    /// Error message from query execution, with the position it happened at.
    /// </summary>
    [Notify]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The engine's message in full. The short form goes in front of the user; this is what the
    /// Messages tab shows, because the expected-token set of a parse error runs to 1,595 characters
    /// and none of it belongs in a status bar (WS-11).
    /// </summary>
    [Notify]
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// Where the failure is, in the coordinates of THIS TAB - 1-based line, 0-based column. The
    /// engine reports a position inside the statement it was given, which for the sixth statement of
    /// a script is never the line the user wrote it on.
    /// </summary>
    [Notify]
    public int ErrorLine { get; set; }

    [Notify]
    public int ErrorColumn { get; set; }

    /// <summary>
    /// What each statement of the script did, in order. A script is executed one statement at a time
    /// (WS-22), and this is the record of it.
    /// </summary>
    public ObservableCollection<StatementOutcome> Statements { get; private set; } = null!;

    /// <summary>
    /// Which statement is running, 1-based, or 0 when nothing is.
    /// </summary>
    [Notify]
    public int CurrentStatementNumber { get; set; }

    [Notify]
    public int StatementCount { get; set; }

    /// <summary>
    /// True when the script changed the schema, so that whoever owns the tree knows to reload it.
    /// </summary>
    [Notify]
    public bool DdlWasExecuted { get; set; }

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

    /// <summary>
    /// Settings for result grid columns (persistence, visibility, order, etc.).
    /// </summary>
    [Notify]
    public GridColumnSettings ResultColumnSettings { get; private set; }

    /// <summary>
    /// Gets whether there is an execution result to show in status bar.
    /// </summary>
    [Notify]
    public bool HasExecutionResult { get; private set; }

    #endregion

    #region Commands

    public ICommand ExecuteQueryCommand { get; private set; } = null!;

    public ICommand ExecuteSelectionCommand { get; private set; } = null!;

    /// <summary>
    /// F5 (WS-25). <see cref="ExecuteQueryCommand"/> is the whole script, on Ctrl+Shift+F5.
    /// </summary>
    public ICommand ExecuteCurrentStatementCommand { get; private set; } = null!;

    public ICommand StopQueryCommand { get; private set; } = null!;

    public ICommand ClearResultsCommand { get; private set; } = null!;

    public ICommand CopyRowsCommand { get; private set; } = null!;

    public ICommand CopyRowsAsCsvCommand { get; private set; } = null!;

    public ICommand CopyRowsAsInsertCommand { get; private set; } = null!;

    public ICommand CopyAllRowsCommand { get; private set; } = null!;

    public ICommand CopyAllRowsAsInsertCommand { get; private set; } = null!;

    public ICommand ExportResultsCommand { get; private set; } = null!;

    #endregion

    #region Services

    private IExportService Export => ApplicationVm.Export;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion

    #region Localization

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
