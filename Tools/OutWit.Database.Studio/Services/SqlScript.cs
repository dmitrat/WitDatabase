using System.Text.RegularExpressions;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// One statement of a script, and where in the script it was written.
/// </summary>
/// <param name="Index">Position in the script, zero-based - "statement 6 of 7" is Index 5.</param>
/// <param name="Text">The statement's own text, ready to send on its own.</param>
/// <param name="Line">1-based line of the script where the statement starts.</param>
/// <param name="Column">0-based column of the script where the statement starts.</param>
/// <param name="WritesTable">
/// The table this statement writes rows into, or null when it writes none. It is what lets the
/// explorer re-count the one table a script touched instead of every table of the connection.
/// </param>
public sealed record SqlStatementSpan(int Index, string Text, int Line, int Column, bool ChangesSchema,
    int Offset = 0, string? WritesTable = null)
{
    /// <summary>
    /// One past the last character of the statement, in the script.
    /// </summary>
    public int End => Offset + Text.Length;

    /// <summary>
    /// Whether a caret at this offset is inside the statement. The end is included: a caret sitting
    /// just after the last character is still in the statement a person is looking at.
    /// </summary>
    public bool Contains(int offset) => offset >= Offset && offset <= End;

    /// <summary>
    /// A short label for a list of statements: the first line, cut to something readable.
    /// </summary>
    public string Summary
    {
        get
        {
            var firstLine = Text.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? string.Empty;

            return firstLine.Length <= 60 ? firstLine : firstLine[..57] + "...";
        }
    }
}

/// <summary>
/// A position in the SQL editor, and a message about it.
/// </summary>
/// <param name="Line">1-based line in the TAB's text, not in a statement.</param>
/// <param name="Column">0-based column in the tab's text.</param>
public sealed record SqlError(int Line, int Column, string Message, string Detail);

/// <summary>
/// The result of cutting a script up: either the statements, or the errors that stopped it.
/// </summary>
public sealed record SqlScriptSplit(IReadOnlyList<SqlStatementSpan> Statements, IReadOnlyList<SqlError> Errors)
{
    public bool IsSuccess => Errors.Count == 0;
}

/// <summary>
/// Cuts a script into statements and puts an error back on the line the user wrote it on (WS-22).
///
/// The cutting is the parser's, not ours: <see cref="WitSql.TryParse"/> tells us where each statement
/// starts, and the text between one start and the next is the statement. A hand-written splitter on
/// semicolons is the alternative, and it is wrong on the first string literal containing one - which
/// this project has in its own fixtures.
///
/// Why cut at all, rather than sending the whole script as one command: the engine takes it, but then
/// there is one result for seven statements, no progress, no per-statement message, and a cancel that
/// can only mean "all of it". Executing one at a time gives all four - at the price of having to move
/// error coordinates back, which is what <see cref="ToScriptPosition"/> is for.
/// </summary>
public static class SqlScript
{
    #region Constants

    /// <summary>
    /// How the engine reports a position: "Line 4:7 - mismatched input 'FROM' expecting {...}".
    /// Measured against the shipping provider, not assumed.
    /// </summary>
    private static readonly Regex ENGINE_POSITION = new(
        @"^\s*Line\s+(?<line>\d+)\s*:\s*(?<column>\d+)\s*-\s*(?<message>.*)$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// The engine's parse errors carry the whole expected-token set - measured at 1595 characters for
    /// a one-word mistake. The first sentence is the finding; the set is a detail, and it belongs
    /// somewhere a person can go and look rather than in a status bar (WS-11).
    /// </summary>
    private const string EXPECTING = " expecting {";

    #endregion

    #region Functions

    /// <summary>
    /// Cuts the script into statements. Comments and whitespace between statements travel with the
    /// statement above them; a comment before the first statement is not part of any.
    /// </summary>
    public static SqlScriptSplit Split(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new SqlScriptSplit([], []);

        var parsed = WitSql.TryParse(script);

        if (!parsed.IsSuccess)
        {
            // The parser reports positions in the whole script, which is what the editor needs -
            // measured, not assumed: a bad sixth statement is reported on the sixth line.
            var errors = (parsed.Errors ?? [])
                .Select(error => new SqlError(error.Line, error.Column, Shorten(error.Message), error.Message))
                .ToList();

            return new SqlScriptSplit([], errors);
        }

        var statements = parsed.Statements ?? [];

        if (statements.Count == 0)
            return new SqlScriptSplit([], []);

        var offsets = statements
            .Select(statement => OffsetOf(script, statement.Line, statement.Column))
            .ToList();

        var spans = new List<SqlStatementSpan>(statements.Count);

        for (var i = 0; i < statements.Count; i++)
        {
            var start = offsets[i];
            var end = i + 1 < offsets.Count ? offsets[i + 1] : script.Length;

            if (start < 0 || start >= script.Length || end <= start)
                continue;

            var text = script[start..end].TrimEnd();

            if (text.Length == 0)
                continue;

            spans.Add(new SqlStatementSpan(spans.Count, text, statements[i].Line, statements[i].Column,
                ChangesSchema(statements[i]), start, WritesTable(statements[i])));
        }

        return new SqlScriptSplit(spans, []);
    }

    /// <summary>
    /// The statement the caret is in, or the last one that starts before it when the caret is in the
    /// whitespace between two - which is where a caret ends up after pressing Enter at the end of a
    /// statement.
    ///
    /// This is what F5 runs (WS-25): the statement under the cursor, not the whole script. A person
    /// with a forty-line script and a cursor in the middle of it means one of them, and running all
    /// forty because the cursor happened to be somewhere is not a mistake that can be undone.
    /// </summary>
    public static SqlStatementSpan? At(SqlScriptSplit split, int offset)
    {
        if (split.Statements.Count == 0)
            return null;

        foreach (var statement in split.Statements)
        {
            if (statement.Contains(offset))
                return statement;
        }

        return split.Statements.LastOrDefault(statement => statement.Offset <= offset)
            ?? split.Statements[0];
    }

    /// <summary>
    /// Whether a statement changes the schema, and so the tree has to be reloaded after it.
    ///
    /// Asked of the PARSED statement rather than of its text. Studio used to look for a leading DDL
    /// keyword by hand, skipping comments and whitespace itself - forty lines that had to know that a
    /// block comment can contain a semicolon and that a keyword can be lower case. The parser has
    /// already answered the question by the time it gives a statement back.
    /// </summary>
    private static bool ChangesSchema(WitSqlStatement statement)
    {
        var name = statement.GetType().Name;

        return name.StartsWith("WitSqlStatementCreate", StringComparison.Ordinal)
            || name.StartsWith("WitSqlStatementDrop", StringComparison.Ordinal)
            || name.StartsWith("WitSqlStatementAlter", StringComparison.Ordinal)
            || name.StartsWith("WitSqlStatementTruncate", StringComparison.Ordinal)
            || name.StartsWith("WitSqlStatementRename", StringComparison.Ordinal);
    }

    /// <summary>
    /// The table a statement writes rows into, or null. Asked of the PARSED statement for the same
    /// reason as <see cref="ChangesSchema"/>: the name can be quoted three ways, the keyword can be in
    /// any case, and a leading comment can contain the word INSERT.
    /// </summary>
    /// <remarks>
    /// <c>TRUNCATE</c> is deliberately absent: it changes the schema by the classification above, so
    /// the tree is reloaded whole after it and the count comes back with the reload. And a
    /// <c>SELECT</c> writes nothing, which is what keeps a script of nothing but queries from costing
    /// a count of anything.
    /// </remarks>
    private static string? WritesTable(WitSqlStatement statement) => statement switch
    {
        WitSqlStatementInsert insert => insert.TableName,
        WitSqlStatementUpdate update => update.TableName,
        WitSqlStatementDelete delete => delete.TableName,
        WitSqlStatementMerge merge => merge.TargetTable,
        _ => null
    };

    /// <summary>
    /// Moves a position the engine reported about ONE statement back into the coordinates of the tab.
    ///
    /// The engine counts from the start of the text it was given, so the sixth statement of a script
    /// always reports "Line 1" when it is sent on its own. Without this the editor underlines the
    /// first line of the file and the user goes looking for a mistake that is not there.
    /// </summary>
    public static (int Line, int Column) ToScriptPosition(SqlStatementSpan statement, int line, int column)
    {
        // Line 1 of the statement starts at the statement's column; every line after it starts at
        // column 0 of its own line, so only the first one is shifted sideways.
        return line <= 1
            ? (statement.Line, statement.Column + column)
            : (statement.Line + line - 1, column);
    }

    /// <summary>
    /// Reads an engine error message and, if it carries a position, returns it in the coordinates of
    /// the tab. Returns null when the message says nothing about where - a constraint violation, for
    /// instance, which is about a row rather than about a place in the text.
    /// </summary>
    public static SqlError? ErrorFor(SqlStatementSpan statement, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var match = ENGINE_POSITION.Match(message);

        if (!match.Success)
            return null;

        var line = int.Parse(match.Groups["line"].Value);
        var column = int.Parse(match.Groups["column"].Value);
        var text = match.Groups["message"].Value;

        var (scriptLine, scriptColumn) = ToScriptPosition(statement, line, column);

        return new SqlError(scriptLine, scriptColumn, Shorten(text), text);
    }

    /// <summary>
    /// The first sentence of an engine message, without the expected-token set.
    /// </summary>
    public static string Shorten(string message)
    {
        var cut = message.IndexOf(EXPECTING, StringComparison.Ordinal);

        return (cut < 0 ? message : message[..cut]).Trim();
    }

    /// <summary>
    /// Turns a 1-based line and 0-based column into an offset in the text. Returns -1 when the
    /// position is not in the text at all, which the caller treats as "no statement here" rather than
    /// slicing something arbitrary.
    /// </summary>
    private static int OffsetOf(string text, int line, int column)
    {
        if (line < 1 || column < 0)
            return -1;

        var offset = 0;
        var currentLine = 1;

        while (currentLine < line)
        {
            var next = text.IndexOf('\n', offset);

            if (next < 0)
                return -1;

            offset = next + 1;
            currentLine++;
        }

        var position = offset + column;

        return position <= text.Length ? position : -1;
    }

    #endregion
}
