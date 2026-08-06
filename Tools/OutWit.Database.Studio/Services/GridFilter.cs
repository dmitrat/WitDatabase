using System.Globalization;
using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// One column's filter, turned into a condition and the values to bind to it.
/// </summary>
/// <param name="Clause">The SQL, with placeholders. Never a value written into the text.</param>
/// <param name="Parameters">What to bind.</param>
/// <param name="Description">What it does, in words, for the footer.</param>
public sealed record GridFilterCondition(
    string Clause,
    IReadOnlyList<SqlParameter> Parameters,
    string Description);

/// <summary>
/// The little language of the filter row (4.3): one syntax for every type, and no builder of
/// conditions.
///
/// <code>
/// text        substring, case-insensitive       LIKE '%text%'
/// = 'new'     exactly                           = 'new'
/// &gt; 1000      compared                          &gt; 1000
/// 10..500     a range                           BETWEEN 10 AND 500
/// NULL        empty                             IS NULL
/// IN (1,2,3)  one of                            IN (1, 2, 3)
/// LIKE 'A%'   a pattern as written              LIKE 'A%'
/// </code>
///
/// Several filters are joined with AND. Anything that needs OR is what <b>Show SQL</b> is for
/// (WS-32): inventing a condition builder in a grid means building a second query language beside the
/// real one, and the real one is two clicks away.
///
/// <b>Case, measured 2026-08-06 and worth knowing before writing a filter:</b> on this engine
/// <c>=</c> is case-SENSITIVE while <c>&lt;&gt;</c> and <c>LIKE</c> are case-INSENSITIVE. So
/// <c>Status = 'x'</c> and <c>Status &lt;&gt; 'x'</c> do not partition a table that holds both
/// <c>'Shipped'</c> and <c>'shipped'</c>. The bare-text filter is therefore <c>LIKE</c>, which is what
/// "contains" is expected to mean; <c>=</c> stays exact, which is what typing it is expected to mean.
/// </summary>
public static class GridFilter
{
    #region Functions

    /// <summary>
    /// Reads one filter box. Returns null when it is empty or cannot be understood - an unusable
    /// filter narrows nothing rather than refusing the page.
    /// </summary>
    public static GridFilterCondition? Parse(string? text, ColumnInfo column, int index)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var name = $"[{SqlValueFormatter.EscapeIdentifier(column.Name)}]";
        var trimmed = text.Trim();
        var parameter = $"@f{index}";

        if (trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return new GridFilterCondition($"{name} IS NULL", [], $"{column.Name} is empty");

        if (trimmed.Equals("NOT NULL", StringComparison.OrdinalIgnoreCase))
            return new GridFilterCondition($"{name} IS NOT NULL", [], $"{column.Name} is not empty");

        if (trimmed.StartsWith("IN", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('('))
            return InList(trimmed, column, name, parameter);

        if (trimmed.StartsWith("LIKE", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = Unquote(trimmed[4..].Trim());

            return new GridFilterCondition($"{name} LIKE {parameter}",
                [new SqlParameter(parameter, pattern)], $"{column.Name} like {pattern}");
        }

        var range = Range(trimmed, column, name, parameter);

        if (range != null)
            return range;

        var comparison = Comparison(trimmed, column, name, parameter);

        if (comparison != null)
            return comparison;

        // Nothing else matched: a substring, which is what somebody typing a word into a filter box
        // means every time.
        return new GridFilterCondition($"{name} LIKE {parameter}",
            [new SqlParameter(parameter, $"%{trimmed}%")], $"{column.Name} contains {trimmed}");
    }

    /// <summary>
    /// Joins the filters of every column with AND, and says what they add up to.
    /// </summary>
    public static (string? Where, List<SqlParameter> Parameters, string Description) Combine(
        IEnumerable<GridFilterCondition> conditions)
    {
        var all = conditions.ToList();

        if (all.Count == 0)
            return (null, [], string.Empty);

        var parameters = all.SelectMany(condition => condition.Parameters).ToList();

        return (string.Join(" AND ", all.Select(condition => condition.Clause)),
            parameters,
            string.Join(", ", all.Select(condition => condition.Description)));
    }

    #endregion

    #region Shapes

    private static GridFilterCondition? Comparison(string text, ColumnInfo column, string name, string parameter)
    {
        var (op, rest) = text switch
        {
            _ when text.StartsWith(">=", StringComparison.Ordinal) => (">=", text[2..]),
            _ when text.StartsWith("<=", StringComparison.Ordinal) => ("<=", text[2..]),
            _ when text.StartsWith("<>", StringComparison.Ordinal) => ("<>", text[2..]),
            _ when text.StartsWith("!=", StringComparison.Ordinal) => ("<>", text[2..]),
            _ when text.StartsWith('>') => (">", text[1..]),
            _ when text.StartsWith('<') => ("<", text[1..]),
            _ when text.StartsWith('=') => ("=", text[1..]),
            _ => (null, null)
        };

        if (op == null || rest == null)
            return null;

        var value = Convert(Unquote(rest.Trim()), column);

        return new GridFilterCondition($"{name} {op} {parameter}",
            [new SqlParameter(parameter, value)], $"{column.Name} {op} {Show(value)}");
    }

    private static GridFilterCondition? Range(string text, ColumnInfo column, string name, string parameter)
    {
        var separator = text.IndexOf("..", StringComparison.Ordinal);

        if (separator <= 0 || separator + 2 >= text.Length)
            return null;

        var low = Convert(text[..separator].Trim(), column);
        var high = Convert(text[(separator + 2)..].Trim(), column);

        return new GridFilterCondition(
            $"{name} BETWEEN {parameter}a AND {parameter}b",
            [new SqlParameter($"{parameter}a", low), new SqlParameter($"{parameter}b", high)],
            $"{column.Name} between {Show(low)} and {Show(high)}");
    }

    private static GridFilterCondition? InList(string text, ColumnInfo column, string name, string parameter)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');

        if (open < 0 || close <= open)
            return null;

        var items = text[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => Convert(Unquote(item), column))
            .ToList();

        if (items.Count == 0)
            return null;

        var parameters = items
            .Select((value, i) => new SqlParameter($"{parameter}_{i}", value))
            .ToList();

        return new GridFilterCondition(
            $"{name} IN ({string.Join(", ", parameters.Select(p => p.Name))})",
            parameters,
            $"{column.Name} one of {string.Join(", ", items.Select(Show))}");
    }

    #endregion

    #region Values

    /// <summary>
    /// Turns what was typed into a value of the column's own type, so that the engine compares like
    /// with like. A value that will not convert goes as text and the engine decides - refusing to
    /// filter because a number was mistyped is worse than letting the database say so.
    /// </summary>
    private static object? Convert(string text, ColumnInfo column)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var type = (column.DataType ?? string.Empty).ToUpperInvariant();

        if (type.Contains("INT"))
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number : text;

        if (type.Contains("DECIMAL") || type.Contains("NUMERIC") || type.Contains("MONEY"))
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value : text;

        if (type.Contains("DOUBLE") || type.Contains("FLOAT") || type.Contains("REAL"))
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value : text;

        if (type.Contains("BOOL"))
            return bool.TryParse(text, out var flag) ? flag : text;

        if (type.Contains("DATE") || type.Contains("TIME"))
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
                ? moment : text;

        if (type.Contains("GUID") || type.Contains("UNIQUEIDENTIFIER"))
            return Guid.TryParse(text, out var guid) ? guid : text;

        return text;
    }

    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
            return text[1..^1].Replace("''", "'");

        return text;
    }

    private static string Show(object? value)
    {
        return value switch
        {
            null => "NULL",
            string text => $"'{text}'",
            DateTime moment => $"'{moment:yyyy-MM-dd HH:mm:ss}'",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    #endregion
}
