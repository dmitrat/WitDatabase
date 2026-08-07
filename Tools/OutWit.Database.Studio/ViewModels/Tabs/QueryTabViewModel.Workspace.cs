using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Input;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// The rest of the query workspace (section 3): completion, the error under the caret, the plan, the
/// history, formatting, and the manual transaction.
///
/// Kept apart from the execution half because it is a different kind of work - everything here is
/// about the text and what can be said ABOUT it, and none of it sends anything to the database except
/// the plan, which asks EXPLAIN.
/// </summary>
public partial class QueryTabViewModel : ISqlCompletionSource
{
    #region Constants

    /// <summary>
    /// How long the text has to stand still before it is parsed for the underline. The design says
    /// 400 ms; a parse per keystroke is what that number exists to prevent.
    /// </summary>
    public static readonly TimeSpan SyntaxCheckDelay = TimeSpan.FromMilliseconds(400);

    #endregion

    #region Fields

    private CancellationTokenSource? m_syntaxCts;

    #endregion

    #region Initialization

    private void InitWorkspace()
    {
        Plan = QueryPlan.Empty;
        History = [];
        Isolations = [IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead, IsolationLevel.Serializable, IsolationLevel.Snapshot];

        FormatCommand = new RelayCommand(Format);
        ShowPlanCommand = new RelayCommandAsync(ShowPlanAsync);
        RefreshHistoryCommand = new RelayCommandAsync(RefreshHistoryAsync);
        ClearHistoryCommand = new RelayCommandAsync(ClearHistoryAsync);
        UseHistoryEntryCommand = new RelayCommand<QueryHistoryEntry>(UseHistoryEntry);
        ApplySuggestionCommand = new RelayCommand(ApplySuggestion, () => ErrorSuggestion != null);
        BeginTransactionCommand = new RelayCommandAsync(BeginTransactionAsync);
        CommitTransactionCommand = new RelayCommandAsync(CommitTransactionAsync);
        RollbackTransactionCommand = new RelayCommandAsync(RollbackTransactionAsync);
    }

    #endregion

    #region Completion

    /// <summary>
    /// What to offer at the caret (WS-24). Reads the connection's cached schema, and loads the columns
    /// it is about to need first - which is the only part of completion that can take time, and the
    /// reason this is a task rather than a property.
    /// </summary>
    public async Task<IReadOnlyList<SqlCompletionItem>> SuggestAsync(string text, int caret)
    {
        var catalog = Session?.Catalog;

        if (catalog == null)
            return [];

        var context = SqlCompletion.Analyze(text, caret);

        if (context.Target == SqlCompletionTarget.None)
            return [];

        await catalog.LoadColumnsAsync(SqlCompletion.ObjectsToLoad(context));

        return SqlCompletion.Suggest(context, catalog);
    }

    /// <summary>
    /// Where an accepted suggestion starts replacing.
    /// </summary>
    public int CompletionStart(string text, int caret)
    {
        return SqlCompletion.Analyze(text, caret).ReplaceFrom;
    }

    #endregion

    #region Syntax

    /// <summary>
    /// Parses the text after it has stood still, and underlines the first thing the parser refuses
    /// (3.6). Nothing is sent to the database: a syntax error is knowable without it.
    /// </summary>
    private async Task CheckSyntaxAsync()
    {
        m_syntaxCts?.Cancel();
        m_syntaxCts?.Dispose();
        m_syntaxCts = new CancellationTokenSource();

        var token = m_syntaxCts.Token;
        var text = SqlText;

        try
        {
            await Task.Delay(SyntaxCheckDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || !ReferenceEquals(text, SqlText))
            return;

        SetSyntaxError(string.IsNullOrWhiteSpace(text) ? null : FirstError(text));
    }

    /// <summary>
    /// The same check without the wait, for a caller that already knows the text has settled.
    /// </summary>
    public void CheckSyntaxNow()
    {
        SetSyntaxError(string.IsNullOrWhiteSpace(SqlText) ? null : FirstError(SqlText));
    }

    private static SqlError? FirstError(string text)
    {
        var split = SqlScript.Split(text);

        return split.IsSuccess ? null : split.Errors.FirstOrDefault();
    }

    private void SetSyntaxError(SqlError? error)
    {
        SyntaxError = error;
        SyntaxErrorLine = error?.Line ?? 0;
        SyntaxErrorColumn = error?.Column ?? 0;
        SyntaxErrorMessage = error?.Message;

        UpdateUnderline();
    }

    /// <summary>
    /// Where the wavy line goes. Two kinds of error can put it there and only one of them can be true
    /// at a time: an execution error is about text that has already been sent, so a syntax error found
    /// since then is about newer text and wins.
    /// </summary>
    private void UpdateUnderline()
    {
        if (SyntaxErrorLine > 0)
        {
            UnderlineLine = SyntaxErrorLine;
            UnderlineColumn = SyntaxErrorColumn;
            UnderlineLength = 1;
            return;
        }

        UnderlineLine = ErrorLine;
        UnderlineColumn = ErrorColumn;
        UnderlineLength = Math.Max(1, ErrorLength);
    }

    #endregion

    #region Formatting

    /// <summary>
    /// Ctrl+Shift+F. Replaces the text only when something was actually rewritten, and says what was
    /// left alone and why - a formatter that silently declines reads as one that is broken.
    /// </summary>
    private void Format()
    {
        var text = SqlText;

        if (string.IsNullOrWhiteSpace(text))
            return;

        var result = SqlFormatter.Format(text);

        if (result.Changed)
            SqlText = result.Text;

        FormatSummary = result.Summary;
        ApplicationVm.MainWindowVm.StatusText = result.Changed
            ? result.Summary
            : $"Nothing was formatted: {string.Join("; ", result.Reasons)}";
    }

    #endregion

    #region Plan

    /// <summary>
    /// EXPLAIN for the statement under the caret, drawn as the tree it already is (WS-27).
    ///
    /// The statement under the caret rather than the whole script, for the same reason F5 is: a script
    /// of seven statements has seven plans, and the one being asked about is the one being looked at.
    /// </summary>
    private async Task ShowPlanAsync()
    {
        var session = Session;

        if (session is not { IsConnected: true })
        {
            PlanMessage = Localization["Common.NotConnected"];
            return;
        }

        var statement = StatementForPlan();

        if (string.IsNullOrWhiteSpace(statement))
        {
            PlanMessage = Localization["Query.NothingToExplain"];
            return;
        }

        var result = await session.ExecuteQueryAsync($"EXPLAIN {statement}");

        if (result.ErrorMessage != null)
        {
            // Measured 2026-08-06: this engine explains queries only - EXPLAIN UPDATE is a parse
            // error. Saying so beats an empty panel that looks like a failure of Studio.
            Plan = QueryPlan.Empty;
            PlanMessage = SqlScript.Shorten(result.ErrorMessage);
            return;
        }

        Plan = QueryPlanReader.Read(result.Data);
        PlanMessage = Plan.IsEmpty ? "This statement has no plan to show" : null;
        PlanStatement = statement;
    }

    private string? StatementForPlan()
    {
        if (!string.IsNullOrWhiteSpace(SelectedText))
            return SelectedText!.Trim();

        var text = SqlText ?? string.Empty;
        var split = SqlScript.Split(text);

        if (!split.IsSuccess)
            return null;

        var statement = SqlScript.At(split, Math.Clamp(CaretOffset, 0, text.Length));

        return statement?.Text.TrimEnd(';', ' ', '\r', '\n', '\t');
    }

    #endregion

    #region History

    private async Task RefreshHistoryAsync()
    {
        var history = ApplicationVm.History;

        HistoryMessage = history.IsAvailable ? null : $"History unavailable: {history.UnavailableReason}";

        var entries = await history.SearchAsync(HistorySearch);

        History.Clear();

        foreach (var entry in entries)
            History.Add(entry);

        if (history.IsAvailable && History.Count == 0)
            HistoryMessage = string.IsNullOrWhiteSpace(HistorySearch)
                ? "Nothing has been run yet"
                : $"Nothing in the history contains \"{HistorySearch}\"";
    }

    private async Task ClearHistoryAsync()
    {
        await ApplicationVm.History.ClearAsync();
        await RefreshHistoryAsync();
    }

    /// <summary>
    /// Puts a remembered query back in the editor. It replaces the text rather than running it: what
    /// came out of a history is usually about to be edited, and running it unasked would be a write
    /// nobody pressed anything for.
    /// </summary>
    private void UseHistoryEntry(QueryHistoryEntry? entry)
    {
        if (entry == null)
            return;

        SqlText = entry.Text;
        CheckSyntaxNow();
    }

    /// <summary>
    /// Writes what has just been executed into the history. Never the connection string and never a
    /// parameter value - only the text, the connection's name, and what happened (WS-29).
    /// </summary>
    private async Task RecordInHistoryAsync(string sql, string status, int rows)
    {
        await ApplicationVm.History.RecordAsync(sql, ConnectionName, ExecutionTimeMs, rows, status);
    }

    #endregion

    #region Transaction

    private async Task BeginTransactionAsync()
    {
        var session = Session;

        if (session is not { IsConnected: true } || session.HasOpenTransaction)
            return;

        session.Isolation = Isolation;

        await session.BeginTransactionAsync();

        ApplicationVm.MainWindowVm.StatusText = Localization.Format("Query.TransactionOpened", Isolation);
    }

    private async Task CommitTransactionAsync()
    {
        var session = Session;

        if (session == null)
            return;

        var count = session.TransactionStatementCount;

        await session.CommitTransactionAsync();

        ApplicationVm.MainWindowVm.StatusText = Localization.Format("Query.TransactionCommitted",
            Localization.Plural("Count.Statements", count));
    }

    private async Task RollbackTransactionAsync()
    {
        var session = Session;

        if (session == null)
            return;

        var count = session.TransactionStatementCount;

        await session.RollbackTransactionAsync();

        ApplicationVm.MainWindowVm.StatusText = Localization.Format("Query.TransactionRolledBack",
            Localization.Plural("Count.Statements", count));
    }

    /// <summary>
    /// Copies the session's transaction state onto the tab. It is read from the session and never
    /// stored there twice: two tabs of one connection share the transaction, and a tab holding its own
    /// idea of whether one is open would be wrong for one of them (WS-26).
    /// </summary>
    private void UpdateTransactionState()
    {
        var session = Session;

        HasOpenTransaction = session?.HasOpenTransaction == true;
        TransactionStatementCount = session?.TransactionStatementCount ?? 0;

        TransactionState = HasOpenTransaction
            ? Localization.Format("Query.TransactionOpen",
                Localization.Plural("Count.Statements", TransactionStatementCount))
            : Localization["Query.Autocommit"];
    }

    #endregion

    #region Errors

    /// <summary>
    /// Reads a failure that names an object and turns it into a place in the text and a suggestion
    /// (3.6). The engine gives no position for these - only <c>Table 'Ordres' not found</c> - so the
    /// name is looked for among the statement's own tokens.
    /// </summary>
    private void LocateObjectError(string? message, SqlStatementSpan? statement, (int Line, int Column) fragment)
    {
        ErrorSuggestion = null;
        ErrorName = null;

        var error = SqlDiagnostics.ObjectNotFound(message);

        if (error == null || statement == null)
            return;

        ErrorName = error.Name;

        var offset = SqlDiagnostics.LocateName(statement.Text, error.Name);

        if (offset != null)
        {
            var (line, column) = SqlDiagnostics.PositionOf(statement.Text, offset.Value);
            var (scriptLine, scriptColumn) = SqlScript.ToScriptPosition(statement, line, column);

            ErrorLine = scriptLine + fragment.Line;
            ErrorColumn = scriptLine == 1 ? scriptColumn + fragment.Column : scriptColumn;
            ErrorLength = error.Name.Length;

            // The message was written before the name was found, from the statement's own start -
            // so it said "line 1" while the underline was on line 2. Two answers to "where", one of
            // them wrong, is worse than either alone: found by running the application. And the
            // status bar was a THIRD, because it was given the message before this ran.
            RewriteErrorPosition();

            ApplicationVm.MainWindowVm.StatusText = ErrorMessage!;

            UpdateUnderline();
        }

        ErrorSuggestion = SqlDiagnostics.Nearest(error.Name, CandidatesFor(error.Kind));

        (ApplySuggestionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Puts the position now known into the message, wherever the message names one. The rest of it -
    /// "Statement 2 of 7", the engine's own words - is left exactly as it was.
    /// </summary>
    private void RewriteErrorPosition()
    {
        if (ErrorMessage == null)
            return;

        ErrorMessage = System.Text.RegularExpressions.Regex.Replace(
            ErrorMessage,
            @"[Ll]ine \d+, column \d+",
            $"line {ErrorLine}, column {ErrorColumn + 1}",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

        // The first letter of a message is a capital, and the sentence may now start with "line".
        if (ErrorMessage.Length > 0)
            ErrorMessage = char.ToUpperInvariant(ErrorMessage[0]) + ErrorMessage[1..];
    }

    private IEnumerable<string> CandidatesFor(string kind)
    {
        var catalog = Session?.Catalog;

        if (catalog == null)
            return [];

        if (!kind.Equals("Column", StringComparison.OrdinalIgnoreCase))
            return catalog.Tables.Concat(catalog.Views);

        // A column can be of any table the statement mentions - and of any table at all when it
        // mentions none we know about, which is the case worth being generous in.
        var columns = catalog.Tables
            .Concat(catalog.Views)
            .SelectMany(name => catalog.Columns(name).Select(column => column.Name));

        return columns.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the name the engine could not find with the one it probably was.
    /// </summary>
    private void ApplySuggestion()
    {
        if (ErrorName == null || ErrorSuggestion == null || string.IsNullOrEmpty(SqlText))
            return;

        var offset = SqlDiagnostics.LocateName(SqlText, ErrorName);

        if (offset == null)
            return;

        SqlText = SqlText[..offset.Value] + ErrorSuggestion + SqlText[(offset.Value + ErrorName.Length)..];

        ErrorSuggestion = null;
        ErrorName = null;
        ErrorMessage = null;
        ErrorLine = 0;
        ErrorLength = 0;

        (ApplySuggestionCommand as RelayCommand)?.RaiseCanExecuteChanged();

        CheckSyntaxNow();
    }

    #endregion

    #region Properties

    /// <summary>
    /// The plan of the last statement explained (WS-27). Empty until somebody asks.
    /// </summary>
    [Notify]
    public QueryPlan Plan { get; private set; } = null!;

    /// <summary>
    /// Why the plan panel has nothing in it, when it has nothing in it.
    /// </summary>
    [Notify]
    public string? PlanMessage { get; private set; }

    /// <summary>
    /// Which statement the plan belongs to, so a plan cannot be read as belonging to another.
    /// </summary>
    [Notify]
    public string? PlanStatement { get; private set; }

    /// <summary>
    /// What the parser says about the text as it stands, without anything having been executed.
    /// </summary>
    [Notify]
    public SqlError? SyntaxError { get; private set; }

    [Notify]
    public int SyntaxErrorLine { get; private set; }

    [Notify]
    public int SyntaxErrorColumn { get; private set; }

    [Notify]
    public string? SyntaxErrorMessage { get; private set; }

    /// <summary>
    /// How much of the text the execution error is about, so the underline covers the name rather than
    /// a single character.
    /// </summary>
    [Notify]
    public int ErrorLength { get; private set; }

    /// <summary>
    /// The name the engine could not find, and the nearest one this database does have.
    /// </summary>
    [Notify]
    public string? ErrorName { get; private set; }

    [Notify]
    public string? ErrorSuggestion { get; private set; }

    [Notify]
    public string? FormatSummary { get; private set; }

    /// <summary>
    /// What the editor underlines: 1-based line, 0-based column, and how many characters. Zero means
    /// nothing is wrong with the text.
    /// </summary>
    [Notify]
    public int UnderlineLine { get; private set; }

    [Notify]
    public int UnderlineColumn { get; private set; }

    [Notify]
    public int UnderlineLength { get; private set; }

    public ObservableCollection<QueryHistoryEntry> History { get; private set; } = null!;

    [Notify]
    public string? HistorySearch { get; set; }

    [Notify]
    public string? HistoryMessage { get; private set; }

    /// <summary>
    /// Whether the History panel is the one being looked at.
    ///
    /// It exists because opening the panel and finding it empty is what the application did: the list
    /// was only filled by the Search button, so the first thing anybody saw was a blank panel under a
    /// heading that promised a history. Found by running it - the ViewModel tests all called Refresh
    /// themselves, which is exactly the step a user does not take.
    /// </summary>
    [Notify]
    public bool IsHistorySelected { get; set; }

    /// <summary>
    /// The isolation the NEXT transaction opens at. Lives on the tab, as the design asks (WS-26); the
    /// transaction it opens lives on the connection, because that is where one can exist.
    /// </summary>
    [Notify]
    public IsolationLevel Isolation { get; set; } = IsolationLevel.ReadCommitted;

    public IReadOnlyList<IsolationLevel> Isolations { get; private set; } = null!;

    [Notify]
    public bool HasOpenTransaction { get; private set; }

    [Notify]
    public int TransactionStatementCount { get; private set; }

    [Notify]
    public string TransactionState { get; private set; } = string.Empty;

    #endregion

    #region Commands

    public ICommand FormatCommand { get; private set; } = null!;

    public ICommand ShowPlanCommand { get; private set; } = null!;

    public ICommand RefreshHistoryCommand { get; private set; } = null!;

    public ICommand ClearHistoryCommand { get; private set; } = null!;

    public ICommand UseHistoryEntryCommand { get; private set; } = null!;

    public ICommand ApplySuggestionCommand { get; private set; } = null!;

    public ICommand BeginTransactionCommand { get; private set; } = null!;

    public ICommand CommitTransactionCommand { get; private set; } = null!;

    public ICommand RollbackTransactionCommand { get; private set; } = null!;

    #endregion
}
