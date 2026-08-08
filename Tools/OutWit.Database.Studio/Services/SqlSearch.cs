using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Services;

/// <summary>One place in the text where the term was found.</summary>
/// <param name="Offset">Where it starts, in characters from the beginning of the whole text.</param>
/// <param name="Length">How long it is - which is NOT the term's length once a regex is involved.</param>
public sealed record SearchMatch(int Offset, int Length)
{
    public int End => Offset + Length;
}

/// <summary>
/// How to look (9.7): the three toggles of the band, plus the range to look in.
/// </summary>
/// <param name="MatchCase">The <c>Aa</c> toggle.</param>
/// <param name="UseRegex">The <c>.*</c> toggle - the term is a pattern rather than text.</param>
/// <param name="WholeWord">The <c>Слово</c> toggle.</param>
/// <param name="RangeStart">Where the search may start, for "only in the selection".</param>
/// <param name="RangeLength">How far it may go; zero means the rest of the text.</param>
public sealed record SearchOptions(
    bool MatchCase = false,
    bool UseRegex = false,
    bool WholeWord = false,
    int RangeStart = 0,
    int RangeLength = 0);

/// <summary>
/// What a search found, or why it could not look.
/// </summary>
/// <remarks>
/// <b>A bad pattern is an ANSWER, not an exception.</b> The <c>.*</c> toggle turns whatever is in the
/// box into a regular expression, and a half-typed one - <c>Stat(</c> - is the normal state of the box
/// while somebody is typing it. The band says the pattern is not finished yet; it does not throw, and
/// it does not silently report "no matches", which would read as "this text does not contain it".
/// </remarks>
/// <param name="Matches">Every match, in the order they appear in the text.</param>
/// <param name="PatternError">Why the pattern could not be used, or null.</param>
public sealed record SearchOutcome(IReadOnlyList<SearchMatch> Matches, string? PatternError = null)
{
    public bool IsPattern => PatternError == null;

    public static SearchOutcome Nothing { get; } = new([]);
}

/// <summary>
/// Finding and replacing inside the query editor's text (9.7).
///
/// <para>
/// Everything here is a function of the text: no editor, no control, no caret. That is what lets the
/// band's whole behaviour - the count, which match is current, what "only in the selection" covers,
/// what Replace All actually writes - be measured without a window, which is where every previous
/// stage found the defects a ViewModel test could not see.
/// </para>
/// </summary>
public static class SqlSearch
{
    #region Constants

    private const int MATCH_LIMIT = 100_000;

    #endregion

    #region Functions

    /// <summary>
    /// Every place the term appears, in order.
    /// </summary>
    public static SearchOutcome Find(string? text, string? term, SearchOptions? options = null)
    {
        options ??= new SearchOptions();

        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
            return SearchOutcome.Nothing;

        var (start, length) = Range(text, options);

        if (length <= 0)
            return SearchOutcome.Nothing;

        Regex regex;

        try
        {
            regex = Build(term, options);
        }
        catch (ArgumentException ex)
        {
            return new SearchOutcome([], ex.Message);
        }

        var matches = new List<SearchMatch>();

        // Matched inside the range only, so "only in the selection" is a property of WHERE it looked
        // rather than of what is filtered out afterwards - the difference shows up on a term that
        // straddles the edge of the selection.
        foreach (Match match in regex.Matches(text[start..(start + length)]))
        {
            if (match.Length == 0)
                continue;

            matches.Add(new SearchMatch(start + match.Index, match.Length));

            if (matches.Count >= MATCH_LIMIT)
                break;
        }

        return new SearchOutcome(matches);
    }

    /// <summary>
    /// The match a person means when the caret is where it is: the first one at or after the caret,
    /// wrapping to the first match when the caret is past the last one.
    /// </summary>
    /// <remarks>
    /// Zero when there are no matches, so the caller can treat "which one" and "how many" as one pair.
    /// Wrapping is not decoration - a search that stops at the end of the file makes a person scroll
    /// back to the top and press it again, and every editor they have used wraps.
    /// </remarks>
    public static int IndexAtOrAfter(IReadOnlyList<SearchMatch> matches, int caret)
    {
        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i].Offset >= caret)
                return i;
        }

        return matches.Count == 0 ? -1 : 0;
    }

    /// <summary>
    /// The text with ONE match replaced.
    /// </summary>
    /// <remarks>
    /// In regex mode the replacement honours the substitutions the pattern captured (<c>$1</c>), which
    /// is what the <c>.*</c> toggle is for; in ordinary mode it is written literally, because a person
    /// replacing <c>Total</c> with <c>$1.00</c> means those characters.
    /// </remarks>
    public static string ReplaceOne(string text, SearchMatch match, string? replacement, SearchOptions options,
        string? term = null)
    {
        replacement ??= string.Empty;

        var written = options.UseRegex && term != null
            ? Substitute(text, match, term, replacement, options)
            : replacement;

        return text[..match.Offset] + written + text[match.End..];
    }

    /// <summary>
    /// The text with every match replaced, and how many there were.
    /// </summary>
    /// <remarks>
    /// Walked from the END backwards, so each replacement leaves the offsets of the ones not yet done
    /// where they were. Forwards, every replacement of a different length moves the rest - which is
    /// the classic way a replace-all corrupts the tail of a file.
    /// </remarks>
    public static (string Text, int Count) ReplaceAll(string text, string? term, string? replacement,
        SearchOptions? options = null)
    {
        options ??= new SearchOptions();

        var outcome = Find(text, term, options);

        if (outcome.Matches.Count == 0)
            return (text, 0);

        var written = text;

        for (var i = outcome.Matches.Count - 1; i >= 0; i--)
            written = ReplaceOne(written, outcome.Matches[i], replacement, options, term);

        return (written, outcome.Matches.Count);
    }

    #endregion

    #region Tools

    /// <summary>
    /// The pattern the toggles add up to. A plain term is escaped, so <c>Total > 100</c> is text and
    /// not a broken pattern.
    /// </summary>
    private static Regex Build(string term, SearchOptions options)
    {
        var pattern = options.UseRegex ? term : Regex.Escape(term);

        if (options.WholeWord)
            pattern = $@"\b(?:{pattern})\b";

        var flags = RegexOptions.None;

        if (!options.MatchCase)
            flags |= RegexOptions.IgnoreCase;

        return new Regex(pattern, flags);
    }

    private static string Substitute(string text, SearchMatch match, string term, string replacement,
        SearchOptions options)
    {
        try
        {
            var regex = Build(term, options);
            var found = regex.Match(text, match.Offset, match.Length);

            return found.Success ? found.Result(replacement) : replacement;
        }
        catch (ArgumentException)
        {
            // A replacement naming a group the pattern does not have is the user's typo, not a crash.
            return replacement;
        }
    }

    /// <summary>
    /// The stretch of text to look in, clamped to what is actually there.
    /// </summary>
    private static (int Start, int Length) Range(string text, SearchOptions options)
    {
        if (options.RangeLength <= 0)
            return (0, text.Length);

        var start = Math.Clamp(options.RangeStart, 0, text.Length);
        var length = Math.Clamp(options.RangeLength, 0, text.Length - start);

        return (start, length);
    }

    #endregion
}
