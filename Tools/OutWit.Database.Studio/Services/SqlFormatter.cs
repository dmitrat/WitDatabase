using System.Text;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Why a statement was left exactly as the user wrote it.
/// </summary>
/// <remarks>
/// A code and not a sentence, for the reason stage 10 named: a service that composes prose fixes the
/// language of every screen showing it, and this one is shown in the status bar and in the format
/// panel. The formatter knows WHICH limitation it hit; the words for it belong to the catalogue.
/// </remarks>
public enum FormatSkipReason
{
    /// <summary>The script as a whole does not parse, so there is nothing to format it with.</summary>
    ScriptDoesNotParse,

    /// <summary>The statement carries a comment, and the parser drops comments.</summary>
    HasComment,

    /// <summary>The statement does not parse on its own.</summary>
    DoesNotParse,

    /// <summary>The parser cannot write this kind of statement back out.</summary>
    CannotBeSerialized,

    /// <summary>Serializing and re-parsing does not give the same statement back.</summary>
    NoRoundTrip
}

/// <summary>
/// The result of formatting a script: the text, and an honest account of what was not touched.
/// </summary>
/// <param name="Text">The formatted script. Equal to the input when nothing could be done.</param>
/// <param name="Formatted">How many statements were rewritten.</param>
/// <param name="Skipped">How many were left exactly as the user wrote them.</param>
/// <param name="Reasons">Each limitation that was hit, once, in the order it was first hit.</param>
public sealed record FormattedScript(
    string Text, int Formatted, int Skipped, IReadOnlyList<FormatSkipReason> Reasons)
{
    public bool Changed => Formatted > 0;
}

/// <summary>
/// Formats SQL by parsing it and writing the syntax tree back out (the plan's item for stage 6).
///
/// There is no rule engine here and there is not going to be one: the parser already carries
/// <see cref="WitSqlStatementSerializer"/>, which is what renders a stored view or routine definition
/// for the inspector. Formatting is that serializer plus line breaks.
///
/// The whole difficulty is what it CANNOT do, and every one of these was measured rather than assumed:
///
/// - <b>Comments are gone from the tree.</b> The grammar skips <c>--</c> and <c>/* */</c> at the lexer,
///   so a statement carrying a comment would come back without it. Reformatting is not allowed to
///   delete anything a person wrote, so a statement with a comment in it is left alone.
/// - <b>DDL cannot be rendered at all.</b> The serializer handles SELECT, INSERT, UPDATE, DELETE and
///   CALL and throws <c>NotSupportedException</c> for everything else - measured on
///   <c>CREATE TABLE</c>, <c>CREATE INDEX</c> and <c>EXPLAIN</c>. Those are left alone too.
/// - <b>A round trip that is not stable is not a format.</b> Every rewritten statement is parsed again
///   and re-serialized; unless that gives the same text, the original stands.
///
/// The result is a formatter that changes less than a rule-based one and cannot lose anything, which
/// is the right side to be wrong on when the input is somebody's unsaved work.
/// </summary>
public static class SqlFormatter
{
    #region Constants

    /// <summary>
    /// Where a line starts, at nesting depth zero. The serializer writes one long line; these are the
    /// places a person expects to see a break.
    /// </summary>
    private static readonly string[][] BREAK_BEFORE =
    [
        ["FROM"], ["WHERE"], ["GROUP", "BY"], ["HAVING"], ["ORDER", "BY"], ["LIMIT"],
        ["INNER", "JOIN"], ["LEFT", "JOIN"], ["RIGHT", "JOIN"], ["FULL", "JOIN"], ["CROSS", "JOIN"],
        ["JOIN"], ["UNION"], ["INTERSECT"], ["EXCEPT"], ["VALUES"], ["SET"]
    ];

    private const string INDENT = "    ";

    #endregion

    #region Functions

    /// <summary>
    /// Formats a whole script, statement by statement.
    /// </summary>
    public static FormattedScript Format(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new FormattedScript(script, 0, 0, []);

        var split = SqlScript.Split(script);

        if (!split.IsSuccess)
        {
            // Correct behaviour rather than a limitation: there is nothing to format broken SQL WITH.
            // The parser is the formatter, so text it refuses cannot be rewritten by it.
            return new FormattedScript(script, 0, 0, [FormatSkipReason.ScriptDoesNotParse]);
        }

        if (split.Statements.Count == 0)
            return new FormattedScript(script, 0, 0, []);

        var spans = split.Statements;
        var builder = new StringBuilder();
        var reasons = new List<FormatSkipReason>();
        var formatted = 0;
        var skipped = 0;

        // Whatever comes before the first statement - a header comment, usually - belongs to nobody
        // and is copied across untouched.
        builder.Append(script[..spans[0].Offset]);

        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            var (body, terminator, tail) = SplitTail(span.Text);
            var (text, reason) = FormatStatement(body);

            if (text == null)
            {
                builder.Append(span.Text);
                skipped++;

                if (reason is { } limitation)
                    Once(reasons, limitation);
            }
            else
            {
                builder.Append(text).Append(terminator).Append(tail);
                formatted++;
            }

            // Everything between one statement and the next goes back exactly as it was: Split trims
            // the end of a span, and formatting one statement must not move another.
            var gapStart = span.Offset + span.Text.Length;
            var gapEnd = i + 1 < spans.Count ? spans[i + 1].Offset : script.Length;

            if (gapEnd > gapStart)
                builder.Append(script[gapStart..gapEnd]);
        }

        return new FormattedScript(builder.ToString(), formatted, skipped, reasons);
    }

    /// <summary>
    /// Formats one statement. Returns a null text - and the reason for it, in the language of the
    /// thing that was skipped rather than of the mechanism - when it must be left exactly as it is.
    /// </summary>
    private static (string? Text, FormatSkipReason? Reason) FormatStatement(string statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
            return (null, null);

        if (SqlLexer.HasComment(statement))
            return (null, FormatSkipReason.HasComment);

        var parsed = WitSql.TryParse(statement);

        if (!parsed.IsSuccess || parsed.Statements is not { Count: 1 })
            return (null, FormatSkipReason.DoesNotParse);

        var canonical = TrySerialize(parsed.Statements[0]);

        if (canonical == null)
            return (null, FormatSkipReason.CannotBeSerialized);

        // The round trip has to be stable, or the text that replaces the user's is not the same
        // statement. A shape that fails this simply keeps the text it came in with.
        var again = WitSql.TryParse(canonical);

        if (!again.IsSuccess || again.Statements is not { Count: 1 } || TrySerialize(again.Statements[0]) != canonical)
            return (null, FormatSkipReason.NoRoundTrip);

        return (Layout(canonical), null);
    }

    private static string? TrySerialize(WitSqlStatement statement)
    {
        try
        {
            return WitSqlStatementSerializer.Serialize(statement);
        }
        catch (NotSupportedException)
        {
            // CREATE, DROP, ALTER, EXPLAIN and the rest: the serializer has no case for them, and a
            // client cannot invent one without becoming a second implementation of the grammar.
            return null;
        }
    }

    /// <summary>
    /// Puts the canonical single line onto several.
    ///
    /// It only ever REPLACES the space in front of a clause with a line break, and copies everything
    /// else through character for character. Rebuilding the line from tokens was the first version and
    /// it got the spacing wrong in two places at once (<c>ON(x = y)</c>, <c>INNER\nJOIN</c>) - the
    /// serializer has already decided where the spaces go, and second-guessing it is how a formatter
    /// starts editing the SQL rather than laying it out.
    /// </summary>
    private static string Layout(string canonical)
    {
        var tokens = SqlLexer.Tokenize(canonical);
        var builder = new StringBuilder();
        var depth = 0;
        var copied = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Kind == SqlTokenKind.Punctuation)
            {
                if (token.Text == "(") depth++;
                if (token.Text == ")") depth = Math.Max(0, depth - 1);
            }

            if (depth != 0 || token.Start == 0 || !StartsClause(tokens, i))
                continue;

            // Everything up to the whitespace before this word, then the break instead of it.
            var breakAt = token.Start;

            while (breakAt > copied && char.IsWhiteSpace(canonical[breakAt - 1]))
                breakAt--;

            builder.Append(canonical[copied..breakAt]);
            builder.Append('\n');

            if (!IsTopLevel(tokens, i))
                builder.Append(INDENT);

            copied = token.Start;
        }

        builder.Append(canonical[copied..]);

        return builder.ToString();
    }

    private static bool StartsClause(IReadOnlyList<SqlToken> tokens, int index)
    {
        var token = tokens[index];

        if (token.Kind != SqlTokenKind.Word)
            return false;

        // A qualified join is one clause, not two: the break belongs in front of INNER, and JOIN on
        // its own is only a clause start when nothing qualifies it.
        if (token.Is("JOIN"))
        {
            var previous = PreviousWord(tokens, index);

            if (previous != null && IsJoinQualifier(previous.Value))
                return false;
        }

        foreach (var phrase in BREAK_BEFORE)
        {
            if (!token.Is(phrase[0]))
                continue;

            if (phrase.Length == 1)
                return true;

            var next = NextWord(tokens, index);

            if (next != null && next.Value.Is(phrase[1]))
                return true;
        }

        return false;
    }

    private static bool IsJoinQualifier(SqlToken token)
    {
        return token.Is("INNER") || token.Is("LEFT") || token.Is("RIGHT")
            || token.Is("FULL") || token.Is("CROSS") || token.Is("OUTER");
    }

    private static SqlToken? PreviousWord(IReadOnlyList<SqlToken> tokens, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (tokens[i].IsTrivia)
                continue;

            return tokens[i];
        }

        return null;
    }

    /// <summary>
    /// A JOIN is a continuation of the FROM above it, so it is indented; a clause is not.
    /// </summary>
    private static bool IsTopLevel(IReadOnlyList<SqlToken> tokens, int index)
    {
        var word = tokens[index];

        if (word.Is("JOIN"))
            return false;

        var next = NextWord(tokens, index);

        return next == null || !next.Value.Is("JOIN");
    }

    private static SqlToken? NextWord(IReadOnlyList<SqlToken> tokens, int index)
    {
        for (var i = index + 1; i < tokens.Count; i++)
        {
            if (tokens[i].IsTrivia)
                continue;

            return tokens[i];
        }

        return null;
    }

    /// <summary>
    /// Cuts a span into the statement, its terminator and whatever follows it - a trailing comment,
    /// usually, which belongs to the line the user put it on and must not be re-flowed.
    ///
    /// The terminator is separated because the serializer does not write one, so a formatted statement
    /// has to be given its semicolon back. Without that, formatting silently ran two statements
    /// together - caught by the case that checks the blank lines between them.
    /// </summary>
    private static (string Body, string Terminator, string Tail) SplitTail(string span)
    {
        var tokens = SqlLexer.Tokenize(span);

        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var token = tokens[i];

            if (token.Kind == SqlTokenKind.Punctuation && token.Text == ";")
                return (span[..token.Start], ";", span[token.End..]);
        }

        return (span, string.Empty, string.Empty);
    }

    private static void Once(List<FormatSkipReason> reasons, FormatSkipReason reason)
    {
        if (!reasons.Contains(reason))
            reasons.Add(reason);
    }

    #endregion
}
