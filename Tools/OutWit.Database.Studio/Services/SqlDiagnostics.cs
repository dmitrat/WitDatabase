using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// An object the engine could not find, and what it was probably meant to be.
/// </summary>
/// <param name="Kind">"Table" or "Column", as the engine's own message spells it.</param>
/// <param name="Name">The name that was not found, exactly as it appears in the text.</param>
/// <param name="Suggestion">The nearest name this database does have, or null.</param>
public sealed record SqlObjectError(string Kind, string Name, string? Suggestion = null);

/// <summary>
/// Reads what the engine says about a statement it refused, and turns it into a place in the text.
///
/// A syntax error arrives with a line and a column (<see cref="SqlScript.ErrorFor"/> moves those into
/// the tab's coordinates). A SEMANTIC one does not: measured 2026-08-06, the engine answers
/// <c>Table 'Ordres' not found</c> and <c>Column 'Totl' not found</c> - the name, in quotes, and
/// nothing about where it was written. So Studio finds it: the name is a token of the statement it
/// just sent, and there is exactly one place a person is looking.
///
/// The suggestion is the same idea one step further. The schema is already loaded for completion, so
/// the nearest name in it can be offered - which turns "not found" from a verdict into an edit.
/// </summary>
public static class SqlDiagnostics
{
    #region Constants

    private static readonly Regex NOT_FOUND = new(
        @"(?<kind>Table|Column|Index|View|Trigger)\s+'(?<name>[^']+)'\s+not found",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    #endregion

    #region Functions

    /// <summary>
    /// Reads an engine message about a missing object, or returns null when it is about something else
    /// - a constraint violation, say, which is about a row rather than about a name in the text.
    /// </summary>
    public static SqlObjectError? ObjectNotFound(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var match = NOT_FOUND.Match(message);

        if (!match.Success)
            return null;

        var kind = match.Groups["kind"].Value;

        return new SqlObjectError(char.ToUpperInvariant(kind[0]) + kind[1..].ToLowerInvariant(),
            match.Groups["name"].Value);
    }

    /// <summary>
    /// Where that name is written, as an offset in the statement. Identifiers only - a name that also
    /// appears inside a string literal must not be underlined there.
    /// </summary>
    public static int? LocateName(string statement, string name)
    {
        if (string.IsNullOrEmpty(statement) || string.IsNullOrEmpty(name))
            return null;

        foreach (var token in SqlLexer.Tokenize(statement))
        {
            if (token.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedName))
                continue;

            if (token.Is(name))
                return token.Start;
        }

        return null;
    }

    /// <summary>
    /// Turns an offset into a 1-based line and a 0-based column, which is what the editor speaks.
    /// </summary>
    public static (int Line, int Column) PositionOf(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);

        var before = text[..offset];
        var line = before.Count(c => c == '\n') + 1;
        var lastBreak = before.LastIndexOf('\n');

        return (line, offset - (lastBreak + 1));
    }

    /// <summary>
    /// The nearest name among the ones this database has, or null when nothing is near enough.
    ///
    /// The bound is a third of the name's length, at least one: <c>Ordres</c> reaches <c>Orders</c>,
    /// and <c>Invoices</c> does not reach <c>Orders</c>. Offering a name that is merely the closest of
    /// a bad set is worse than offering nothing - it reads as a claim.
    /// </summary>
    public static string? Nearest(string name, IEnumerable<string> candidates)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var bound = Math.Max(1, name.Length / 3);
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate))
                continue;

            if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase))
                return null;

            var distance = Distance(name, candidate);

            if (distance > bound || distance >= bestDistance)
                continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// Levenshtein distance, case-insensitively - a wrong capital is a typo like any other.
    /// </summary>
    private static int Distance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToUpperInvariant(left[i - 1]) == char.ToUpperInvariant(right[j - 1]) ? 0 : 1;

                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    #endregion
}
