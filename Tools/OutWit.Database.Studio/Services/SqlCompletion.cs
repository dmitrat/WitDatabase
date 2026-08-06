using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// What a suggestion is, which decides its icon and its place in the list.
/// </summary>
public enum SqlCompletionKind
{
    Keyword,
    Function,
    DataType,
    Table,
    View,
    Routine,
    Column,
    Alias
}

/// <param name="Text">What is inserted.</param>
/// <param name="Detail">The short thing on the right - a type, or what kind of object this is.</param>
/// <param name="Description">The line underneath, when there is something worth saying.</param>
public sealed record SqlCompletionItem(
    string Text,
    SqlCompletionKind Kind,
    string? Detail = null,
    string? Description = null);

/// <summary>
/// What the caret is standing in front of.
/// </summary>
public enum SqlCompletionTarget
{
    /// <summary>Inside a string or a comment: nothing is a suggestion here.</summary>
    None,

    /// <summary>The start of a statement - keywords and objects both make sense.</summary>
    StatementStart,

    /// <summary>After FROM, JOIN, INTO, UPDATE: an object of the database.</summary>
    ObjectName,

    /// <summary>After <c>alias.</c> or <c>Table.</c>: the columns of exactly that one.</summary>
    Members,

    /// <summary>Anywhere else in a statement: the columns in scope, then keywords and functions.</summary>
    Expression
}

/// <param name="Prefix">What has been typed of the word being completed.</param>
/// <param name="ReplaceFrom">Where that word starts, so an accepted item replaces it.</param>
/// <param name="Qualifier">The name before the dot, for <see cref="SqlCompletionTarget.Members"/>.</param>
/// <param name="Scope">The objects named in the statement's FROM/JOIN/INTO/UPDATE, by alias.</param>
public sealed record SqlCompletionContext(
    SqlCompletionTarget Target,
    string Prefix,
    int ReplaceFrom,
    string? Qualifier,
    IReadOnlyDictionary<string, string> Scope);

/// <summary>
/// Who answers "what belongs at this caret".
///
/// The editor holds one of these rather than reaching for a ViewModel: the control knows about windows
/// and the ViewModel knows about the connection, and this is the one sentence they have to agree on.
/// </summary>
public interface ISqlCompletionSource
{
    Task<IReadOnlyList<SqlCompletionItem>> SuggestAsync(string text, int caret);

    /// <summary>
    /// Where an accepted suggestion starts replacing - the beginning of the word being typed.
    /// </summary>
    int CompletionStart(string text, int caret);
}

/// <summary>
/// Completion from the schema the connection already knows (WS-24).
///
/// It reads TOKENS, not a syntax tree, and that is the whole reason it exists as its own thing: the
/// text under a caret is half-written, so the parser refuses it, and <see cref="SqlScript"/> - which
/// is the parser's answer and the right one everywhere else - has nothing to say about
/// <c>SELECT * FROM Ord</c>. Tokens still work on text that does not parse.
///
/// What it will not do is guess at meaning. It resolves an alias to a table by finding the words
/// <c>FROM x a</c> in the same statement, and offers that table's columns; it does not attempt scope
/// rules, correlated subqueries or shadowing. A wrong suggestion in a list a person is reading costs
/// them a glance; a missing one costs them a trip to the tree, which is where they were before.
/// </summary>
public static class SqlCompletion
{
    #region Constants

    /// <summary>
    /// After one of these, the next word names an object of the database.
    /// </summary>
    private static readonly string[] BEFORE_OBJECT = ["FROM", "JOIN", "INTO", "UPDATE", "TABLE"];

    /// <summary>
    /// Words that introduce a table in a statement, for alias resolution.
    /// </summary>
    private static readonly string[] INTRODUCES_TABLE = ["FROM", "JOIN", "INTO", "UPDATE"];

    #endregion

    #region Analysis

    /// <summary>
    /// Reads the text around the caret and says what kind of thing belongs there.
    /// </summary>
    public static SqlCompletionContext Analyze(string text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);

        var statement = StatementAround(text, caret, out var statementStart);
        var tokens = SqlLexer.Tokenize(statement);
        var local = caret - statementStart;

        if (SqlLexer.IsInsideTextOrComment(tokens, local))
            return Empty(caret);

        var scope = ScopeOf(tokens);

        // The word being typed, if the caret is at the end of one.
        var prefix = string.Empty;
        var replaceFrom = caret;

        var current = tokens.FirstOrDefault(token =>
            token.Kind is SqlTokenKind.Word or SqlTokenKind.QuotedName
            && local > token.Start && local <= token.End);

        if (current.Kind is SqlTokenKind.Word or SqlTokenKind.QuotedName && current.Text.Length > 0)
        {
            prefix = statement[current.Start..local];
            replaceFrom = statementStart + current.Start;
        }

        var skip = prefix.Length > 0 ? 1 : 0;
        var previous = SqlLexer.Previous(tokens, local, skip);

        // After a dot: the columns of whatever the name in front of it denotes.
        if (previous is { Kind: SqlTokenKind.Punctuation } dot && dot.Text == ".")
        {
            var owner = SqlLexer.Previous(tokens, local, skip + 1);

            if (owner is { Kind: SqlTokenKind.Word or SqlTokenKind.QuotedName })
            {
                var name = Unquote(owner.Value.Text);
                var resolved = scope.TryGetValue(name, out var table) ? table : name;

                return new SqlCompletionContext(SqlCompletionTarget.Members, prefix, replaceFrom, resolved, scope);
            }

            return Empty(caret);
        }

        if (previous == null)
            return new SqlCompletionContext(SqlCompletionTarget.StatementStart, prefix, replaceFrom, null, scope);

        if (previous.Value.Kind == SqlTokenKind.Punctuation && previous.Value.Text == ";")
            return new SqlCompletionContext(SqlCompletionTarget.StatementStart, prefix, replaceFrom, null, scope);

        if (previous.Value.Kind == SqlTokenKind.Word && BEFORE_OBJECT.Any(word => previous.Value.Is(word)))
            return new SqlCompletionContext(SqlCompletionTarget.ObjectName, prefix, replaceFrom, null, scope);

        return new SqlCompletionContext(SqlCompletionTarget.Expression, prefix, replaceFrom, null, scope);
    }

    #endregion

    #region Suggestions

    /// <summary>
    /// The list to show, already ordered: an exact prefix match first, then the objects of this
    /// database, then the language.
    ///
    /// The design asks for the order inside a group to be "by how often it is used in this database".
    /// There is no such measurement anywhere in Studio, so the order inside a group is alphabetical
    /// and says so rather than inventing a ranking out of nothing.
    /// </summary>
    public static IReadOnlyList<SqlCompletionItem> Suggest(SqlCompletionContext context, ISchemaCatalog catalog)
    {
        var items = new List<SqlCompletionItem>();

        switch (context.Target)
        {
            case SqlCompletionTarget.None:
                return items;

            case SqlCompletionTarget.Members:
                AddColumns(items, catalog, context.Qualifier);
                break;

            case SqlCompletionTarget.ObjectName:
                AddObjects(items, catalog);
                break;

            case SqlCompletionTarget.StatementStart:
                AddKeywords(items);
                AddObjects(items, catalog);
                break;

            default:
                AddScopeColumns(items, catalog, context.Scope);
                AddAliases(items, context.Scope);
                AddKeywords(items);
                AddFunctions(items);
                AddObjects(items, catalog);
                break;
        }

        return Rank(items, context.Prefix);
    }

    /// <summary>
    /// The objects whose columns the suggestion list will need. Loading these is the caller's job,
    /// because it is the only part of completion that can take time.
    /// </summary>
    public static IReadOnlyList<string> ObjectsToLoad(SqlCompletionContext context)
    {
        if (context.Target == SqlCompletionTarget.Members && context.Qualifier != null)
            return [context.Qualifier];

        return context.Scope.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddObjects(List<SqlCompletionItem> items, ISchemaCatalog catalog)
    {
        foreach (var table in catalog.Tables)
            items.Add(new SqlCompletionItem(table, SqlCompletionKind.Table, "table"));

        foreach (var view in catalog.Views)
            items.Add(new SqlCompletionItem(view, SqlCompletionKind.View, "view"));

        foreach (var routine in catalog.Routines)
            items.Add(new SqlCompletionItem(routine.Name, SqlCompletionKind.Routine,
                routine.IsFunction ? "function" : "procedure",
                routine.IsFunction && routine.DataType != null ? $"returns {routine.DataType}" : null));
    }

    private static void AddColumns(List<SqlCompletionItem> items, ISchemaCatalog catalog, string? owner)
    {
        if (owner == null)
            return;

        foreach (var column in catalog.Columns(owner))
            items.Add(ColumnItem(column, owner));
    }

    private static void AddScopeColumns(List<SqlCompletionItem> items, ISchemaCatalog catalog,
        IReadOnlyDictionary<string, string> scope)
    {
        foreach (var table in scope.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var column in catalog.Columns(table))
                items.Add(ColumnItem(column, table));
        }
    }

    private static SqlCompletionItem ColumnItem(ColumnInfo column, string owner)
    {
        var marks = new List<string>();

        if (column.IsPrimaryKey) marks.Add("PK");
        if (!column.IsNullable) marks.Add("NOT NULL");

        var detail = marks.Count == 0 ? column.DataType : $"{column.DataType} · {string.Join(" · ", marks)}";

        return new SqlCompletionItem(column.Name, SqlCompletionKind.Column, detail, owner);
    }

    private static void AddAliases(List<SqlCompletionItem> items, IReadOnlyDictionary<string, string> scope)
    {
        foreach (var (alias, table) in scope)
        {
            if (alias.Equals(table, StringComparison.OrdinalIgnoreCase))
                continue;

            items.Add(new SqlCompletionItem(alias, SqlCompletionKind.Alias, "alias", table));
        }
    }

    private static void AddKeywords(List<SqlCompletionItem> items)
    {
        foreach (var keyword in SqlVocabulary.Keywords)
            items.Add(new SqlCompletionItem(keyword, SqlCompletionKind.Keyword, "keyword"));

        foreach (var type in SqlVocabulary.DataTypes)
            items.Add(new SqlCompletionItem(type, SqlCompletionKind.DataType, "type"));
    }

    private static void AddFunctions(List<SqlCompletionItem> items)
    {
        foreach (var function in SqlVocabulary.Functions)
            items.Add(new SqlCompletionItem(function, SqlCompletionKind.Function, "function"));
    }

    private static IReadOnlyList<SqlCompletionItem> Rank(List<SqlCompletionItem> items, string prefix)
    {
        var matching = string.IsNullOrEmpty(prefix)
            ? items
            : items.Where(item => item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        return matching
            .GroupBy(item => item.Text + "|" + item.Kind)
            .Select(group => group.First())
            .OrderBy(item => Exactness(item, prefix))
            .ThenBy(item => Priority(item.Kind))
            .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// An exact match goes first, and "exact" means the characters as typed.
    ///
    /// Case-insensitively it is not exact enough to be useful: typing <c>To</c> towards <c>Total</c>
    /// matched the keyword <c>TO</c> - the one from <c>ROLLBACK TO SAVEPOINT</c> - and put it above the
    /// column, which is the opposite of what the person is doing. Typing <c>TO</c> in capitals still
    /// gets the keyword first.
    /// </summary>
    private static int Exactness(SqlCompletionItem item, string prefix)
    {
        return !string.IsNullOrEmpty(prefix) && item.Text.Equals(prefix, StringComparison.Ordinal) ? 0 : 1;
    }

    /// <summary>
    /// What this database contains, before what the language contains: a person can look a keyword up
    /// and cannot look up the name of a column in a schema they have not seen.
    /// </summary>
    private static int Priority(SqlCompletionKind kind) => kind switch
    {
        SqlCompletionKind.Column => 0,
        SqlCompletionKind.Alias => 1,
        SqlCompletionKind.Table => 2,
        SqlCompletionKind.View => 3,
        SqlCompletionKind.Routine => 4,
        SqlCompletionKind.Keyword => 5,
        SqlCompletionKind.DataType => 6,
        _ => 7
    };

    #endregion

    #region Tools

    /// <summary>
    /// The statement the caret is in, found by semicolons the lexer has already placed outside strings
    /// and comments. Not by <see cref="SqlScript"/>, which needs the whole text to parse - and text
    /// being typed does not.
    /// </summary>
    private static string StatementAround(string text, int caret, out int start)
    {
        var tokens = SqlLexer.Tokenize(text);

        start = 0;
        var end = text.Length;

        foreach (var token in tokens)
        {
            if (token.Kind != SqlTokenKind.Punctuation || token.Text != ";")
                continue;

            if (token.End <= caret)
                start = token.End;
            else
            {
                end = token.Start;
                break;
            }
        }

        return text[start..Math.Max(start, end)];
    }

    /// <summary>
    /// The tables named in this statement, by every name they can be called: their own, and any alias.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ScopeOf(IReadOnlyList<SqlToken> tokens)
    {
        var scope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var words = tokens.Where(token => !token.IsTrivia).ToList();

        for (var i = 0; i < words.Count - 1; i++)
        {
            if (words[i].Kind != SqlTokenKind.Word || !INTRODUCES_TABLE.Any(word => words[i].Is(word)))
                continue;

            var nameToken = words[i + 1];

            if (nameToken.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedName))
                continue;

            var table = Unquote(nameToken.Text);
            scope[table] = table;

            // FROM Orders o, FROM Orders AS o - and nothing else counts as an alias, so a following
            // WHERE or JOIN cannot be mistaken for one.
            var next = i + 2 < words.Count ? words[i + 2] : default;

            if (next.Kind == SqlTokenKind.Word && next.Is("AS") && i + 3 < words.Count)
                next = words[i + 3];
            else if (next.Kind != SqlTokenKind.Word || IsClauseWord(next))
                continue;

            if (next.Kind == SqlTokenKind.Word && !IsClauseWord(next))
                scope[Unquote(next.Text)] = table;
        }

        return scope;
    }

    private static bool IsClauseWord(SqlToken token)
    {
        return token.Is("WHERE") || token.Is("JOIN") || token.Is("INNER") || token.Is("LEFT")
            || token.Is("RIGHT") || token.Is("FULL") || token.Is("CROSS") || token.Is("ON")
            || token.Is("GROUP") || token.Is("ORDER") || token.Is("HAVING") || token.Is("LIMIT")
            || token.Is("SET") || token.Is("VALUES") || token.Is("SELECT") || token.Is("AS");
    }

    private static string Unquote(string name)
    {
        if (name.Length < 2)
            return name;

        return name[0] switch
        {
            '"' when name[^1] == '"' => name[1..^1],
            '[' when name[^1] == ']' => name[1..^1],
            '`' when name[^1] == '`' => name[1..^1],
            _ => name
        };
    }

    private static SqlCompletionContext Empty(int caret)
    {
        return new SqlCompletionContext(SqlCompletionTarget.None, string.Empty, caret, null,
            new Dictionary<string, string>());
    }

    #endregion
}
