using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// How a page is being reached, which is a decision about cost and is shown to the user.
/// </summary>
public enum GridPaging
{
    /// <summary>The first page: LIMIT and nothing else, the one shape this engine answers early.</summary>
    First,

    /// <summary>WHERE key &gt; the last key seen. Constant cost, and correct while the table changes.</summary>
    Keyset,

    /// <summary>The engine walks and discards everything before the page. Said out loud (WS-31).</summary>
    Offset
}

/// <summary>
/// Everything the grid is currently showing, as a question.
/// </summary>
/// <param name="Table">The table being read.</param>
/// <param name="Filters">One condition per filtered column, already parsed.</param>
/// <param name="SortColumn">The column being sorted by, or null.</param>
/// <param name="SortDescending">Which way.</param>
/// <param name="KeyColumn">The single-column primary key, when there is one.</param>
/// <param name="PageIndex">Zero-based.</param>
/// <param name="PageSize">Rows on a page; 0 means everything.</param>
/// <param name="Anchor">The last key of the previous page, for keyset paging.</param>
public sealed record GridView(
    string Table,
    IReadOnlyList<GridFilterCondition> Filters,
    string? SortColumn,
    bool SortDescending,
    string? KeyColumn,
    int PageIndex,
    int PageSize,
    object? Anchor);

/// <param name="Statement">What to send.</param>
/// <param name="Paging">How the page is being reached.</param>
/// <param name="Description">Filters and sorting in words, for the footer.</param>
public sealed record GridPageQuery(SqlStatement Statement, GridPaging Paging, string Description);

/// <summary>
/// Builds the query behind what the grid is showing - the page, and the same thing without its page
/// (WS-30, WS-31, WS-32).
///
/// One place, because <b>Show SQL</b> has to render exactly what was sent. A second builder for the
/// displayed text would be a second implementation, and the first time the two disagreed the feature
/// would become a liability: its whole purpose is to explain what happened.
///
/// Sorting and filtering are a NEW QUERY, never work on the page already fetched (WS-30). Sorting a
/// thousand rows out of five thousand sorts a sample, not the table, and the user finds out too late.
/// </summary>
public static class GridQuery
{
    #region Functions

    /// <summary>
    /// The page. Fetched one row longer than it is shown, which is how "is there a next page" is
    /// answered without a count.
    /// </summary>
    public static GridPageQuery Page(GridView view, int extraRow = 1)
    {
        var table = $"[{SqlValueFormatter.EscapeIdentifier(view.Table)}]";
        var (where, parameters, described) = GridFilter.Combine(view.Filters);

        var limit = view.PageSize > 0 ? view.PageSize + extraRow : 0;
        var paging = PagingOf(view);

        var conditions = new List<string>();

        if (where != null)
            conditions.Add(where);

        var bound = new List<SqlParameter>(parameters);

        if (paging == GridPaging.Keyset && view.Anchor != null)
        {
            var key = $"[{SqlValueFormatter.EscapeIdentifier(view.KeyColumn!)}]";
            var comparison = view.SortDescending ? "<" : ">";

            conditions.Add($"{key} {comparison} @anchor");
            bound.Add(new SqlParameter("@anchor", view.Anchor));
        }

        var sql = $"SELECT * FROM {table}";

        if (conditions.Count > 0)
            sql += $" WHERE {string.Join(" AND ", conditions)}";

        var order = OrderBy(view);

        if (order != null)
            sql += $" ORDER BY {order}";

        if (limit > 0)
            sql += $" LIMIT {limit}";

        if (paging == GridPaging.Offset && view.PageIndex > 0 && view.PageSize > 0)
            sql += $" OFFSET {view.PageIndex * view.PageSize}";

        return new GridPageQuery(new SqlStatement(sql, bound), paging, Describe(view, described));
    }

    /// <summary>
    /// The same view without its page: what "Show SQL" opens in a query tab, so that the user can go
    /// on by hand from where the clicks left off (WS-32).
    /// </summary>
    public static SqlStatement Whole(GridView view)
    {
        var table = $"[{SqlValueFormatter.EscapeIdentifier(view.Table)}]";
        var (where, parameters, _) = GridFilter.Combine(view.Filters);

        var sql = $"SELECT * FROM {table}";

        if (where != null)
            sql += $" WHERE {where}";

        var order = OrderBy(view);

        if (order != null)
            sql += $" ORDER BY {order}";

        return new SqlStatement(sql, parameters);
    }

    /// <summary>
    /// How many rows the view has, when somebody asks for the number (4.2). Never asked for by itself:
    /// on this engine an unfiltered count is a counter kept beside the data rather than the data, and
    /// a filtered one is a scan.
    /// </summary>
    public static SqlStatement Count(GridView view)
    {
        var table = $"[{SqlValueFormatter.EscapeIdentifier(view.Table)}]";
        var (where, parameters, _) = GridFilter.Combine(view.Filters);

        return new SqlStatement(
            where == null
                ? $"SELECT COUNT(*) FROM {table}"
                : $"SELECT COUNT(*) FROM {table} WHERE {where}",
            parameters);
    }

    /// <summary>
    /// Keyset only when the sort IS the key: any other order needs a unique tie-break this does not
    /// have, and pages would overlap. Everything else counts from the start of the table and says so.
    /// </summary>
    public static GridPaging PagingOf(GridView view)
    {
        if (view.PageIndex == 0)
            return GridPaging.First;

        var sortedByKey = view.KeyColumn != null
            && string.Equals(view.SortColumn ?? view.KeyColumn, view.KeyColumn, StringComparison.OrdinalIgnoreCase);

        return sortedByKey ? GridPaging.Keyset : GridPaging.Offset;
    }

    private static string? OrderBy(GridView view)
    {
        var column = view.SortColumn ?? view.KeyColumn;

        if (column == null)
            return null;

        var direction = view.SortDescending ? " DESC" : " ASC";

        return $"[{SqlValueFormatter.EscapeIdentifier(column)}]{direction}";
    }

    private static string Describe(GridView view, string filters)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(filters))
            parts.Add(view.Filters.Count == 1 ? $"1 filter: {filters}" : $"{view.Filters.Count} filters: {filters}");

        if (view.SortColumn != null)
            parts.Add($"sorted by {view.SortColumn}{(view.SortDescending ? " descending" : "")}");
        else if (view.KeyColumn != null)
            parts.Add($"ordered by {view.KeyColumn}");
        else
            parts.Add("no order - the engine returns rows in insertion order, which it does not promise");

        return string.Join(" · ", parts);
    }

    #endregion
}
