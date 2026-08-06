using System.Data;
using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// What an operator of the plan does, as far as it matters to a person reading it.
/// </summary>
public enum PlanOperatorKind
{
    Other,
    TableScan,
    IndexAccess,
    Filter,
    Sort,
    Limit,
    Aggregate,
    Join,
    Alias,
    Projection,
    VirtualTable
}

/// <summary>
/// One node of the plan.
/// </summary>
public sealed class PlanNode
{
    public required int Id { get; init; }

    public required int ParentId { get; init; }

    /// <summary>
    /// What the engine wrote, with its indentation and its column list removed - those are the tree
    /// and a detail, and drawing them as text is what this replaces.
    /// </summary>
    public required string Operator { get; init; }

    public PlanOperatorKind Kind { get; init; }

    /// <summary>
    /// The columns the operator produces, as the engine listed them, or null for a plan asked for with
    /// EXPLAIN QUERY PLAN - which does not include them.
    /// </summary>
    public string? Columns { get; init; }

    public string? TableName { get; init; }

    public string? IndexName { get; init; }

    /// <summary>
    /// Why this node is worth looking at, or null when it is not. Never a guess about cost: the engine
    /// returns no row estimates, so every warning here is about the SHAPE of the plan.
    /// </summary>
    public string? Warning { get; set; }

    public List<PlanNode> Children { get; } = [];
}

/// <summary>
/// A plan, and what it is honest to say about it.
/// </summary>
public sealed record QueryPlan(IReadOnlyList<PlanNode> Roots, IReadOnlyList<PlanNode> All)
{
    public static QueryPlan Empty { get; } = new([], []);

    public bool IsEmpty => All.Count == 0;

    public IEnumerable<PlanNode> Warnings => All.Where(node => node.Warning != null);

    /// <summary>
    /// The tables this plan reads with a full scan under a filter - the ones an index would change.
    /// </summary>
    public IEnumerable<string> ScannedTables => All
        .Where(node => node.Kind == PlanOperatorKind.TableScan && node.Warning != null && node.TableName != null)
        .Select(node => node.TableName!)
        .Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Turns what EXPLAIN returns into a tree (WS-27).
///
/// The engine answers three columns - <c>id</c>, <c>parent</c>, <c>detail</c> - which is already a
/// tree; Studio has been showing it as three columns of text, which is the same information arranged
/// so that nobody can read it.
///
/// <b>What this cannot show, measured 2026-08-06.</b> The design asks for estimated row counts marked
/// with a tilde and labelled "an estimate, not a measurement" (WS-28). The engine returns <b>no
/// numbers at all</b>: no estimated rows, no cost, and - since EXPLAIN builds the plan without running
/// it - no facts either. So there is nothing to mark with a tilde, and every highlight below is about
/// the SHAPE of the plan rather than about its size. When the engine gains EXPLAIN ANALYZE, that is
/// where the numbers come from and not before.
///
/// <b>What it can show</b> is real and was measured against this engine:
/// <list type="bullet">
/// <item>a full table scan underneath a filter, which an index turns into a seek - the engine does
/// produce <c>SEARCH TABLE x USING INDEX y</c>, but only for a table of at least ten rows;</item>
/// <item>a sort underneath a limit, which is the planner not pushing the limit into the sort - the
/// finding stage 3 measured at 1,327 ms for a page of 200 rows out of 400,000.</item>
/// </list>
/// </summary>
public static class QueryPlanReader
{
    #region Constants

    private static readonly Regex SCAN = new(@"^SCAN\s+TABLE\s+(?<table>\S+)", RegexOptions.Compiled);

    private static readonly Regex SEARCH = new(
        @"^SEARCH\s+TABLE\s+(?<table>\S+)\s+USING\s+INDEX\s+(?<index>\S+)", RegexOptions.Compiled);

    private const string COLUMNS_MARK = " -> [";

    #endregion

    #region Functions

    /// <summary>
    /// Reads the result of an EXPLAIN. Anything that is not that shape gives an empty plan rather than
    /// an exception - the panel says it has nothing to show, which is true.
    /// </summary>
    public static QueryPlan Read(DataTable? table)
    {
        if (table == null || table.Rows.Count == 0)
            return QueryPlan.Empty;

        if (!table.Columns.Contains("id") || !table.Columns.Contains("parent") || !table.Columns.Contains("detail"))
            return QueryPlan.Empty;

        var nodes = new List<PlanNode>();
        var byId = new Dictionary<int, PlanNode>();

        foreach (DataRow row in table.Rows)
        {
            var node = ReadNode(row);

            if (node == null)
                continue;

            nodes.Add(node);
            byId[node.Id] = node;
        }

        var roots = new List<PlanNode>();

        foreach (var node in nodes)
        {
            if (byId.TryGetValue(node.ParentId, out var parent) && !ReferenceEquals(parent, node))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        var plan = new QueryPlan(roots, nodes);

        Explain(plan);

        return plan;
    }

    private static PlanNode? ReadNode(DataRow row)
    {
        if (row["detail"] is not string detail)
            return null;

        var id = ToInt(row["id"]);
        var parent = ToInt(row["parent"]);

        // The engine indents the detail to show depth; the tree carries that now, so it goes.
        var text = detail.Trim();
        string? columns = null;

        var mark = text.IndexOf(COLUMNS_MARK, StringComparison.Ordinal);

        if (mark >= 0)
        {
            columns = text[(mark + COLUMNS_MARK.Length)..].TrimEnd(']');
            text = text[..mark];
        }

        var scan = SCAN.Match(text);
        var search = SEARCH.Match(text);

        return new PlanNode
        {
            Id = id,
            ParentId = parent,
            Operator = text,
            Columns = columns,
            Kind = KindOf(text),
            TableName = search.Success ? search.Groups["table"].Value
                : scan.Success ? scan.Groups["table"].Value
                : null,
            IndexName = search.Success ? search.Groups["index"].Value : null
        };
    }

    private static PlanOperatorKind KindOf(string text)
    {
        if (text.StartsWith("SCAN TABLE", StringComparison.Ordinal)) return PlanOperatorKind.TableScan;
        if (text.StartsWith("SEARCH TABLE", StringComparison.Ordinal)) return PlanOperatorKind.IndexAccess;
        if (text.StartsWith("FILTER", StringComparison.Ordinal)) return PlanOperatorKind.Filter;
        if (text.StartsWith("SORT", StringComparison.Ordinal)) return PlanOperatorKind.Sort;
        if (text.StartsWith("LIMIT", StringComparison.Ordinal)) return PlanOperatorKind.Limit;
        if (text.StartsWith("AGGREGATE", StringComparison.Ordinal)) return PlanOperatorKind.Aggregate;
        if (text.StartsWith("DISTINCT", StringComparison.Ordinal)) return PlanOperatorKind.Aggregate;
        if (text.StartsWith("WINDOW", StringComparison.Ordinal)) return PlanOperatorKind.Aggregate;
        if (text.Contains("JOIN", StringComparison.Ordinal)) return PlanOperatorKind.Join;
        if (text.StartsWith("ALIAS", StringComparison.Ordinal)) return PlanOperatorKind.Alias;
        if (text.StartsWith("PROJECT", StringComparison.Ordinal)) return PlanOperatorKind.Projection;
        if (text.StartsWith("ExcludeInternal", StringComparison.Ordinal)) return PlanOperatorKind.Projection;
        if (text.StartsWith("VIRTUAL TABLE", StringComparison.Ordinal)) return PlanOperatorKind.VirtualTable;

        return PlanOperatorKind.Other;
    }

    /// <summary>
    /// Marks the two shapes worth a person's attention. Both are structural, and both were measured
    /// against this engine rather than taken from what a plan viewer usually highlights.
    /// </summary>
    private static void Explain(QueryPlan plan)
    {
        var parents = new Dictionary<int, PlanNode>();

        foreach (var node in plan.All)
        {
            foreach (var child in node.Children)
                parents[child.Id] = node;
        }

        foreach (var node in plan.All)
        {
            switch (node.Kind)
            {
                case PlanOperatorKind.TableScan when HasAbove(node, parents, PlanOperatorKind.Filter):
                    node.Warning = $"every row of {node.TableName} is read and then filtered. An index on " +
                                   "the column the filter uses would make this a seek";
                    break;

                case PlanOperatorKind.Sort when HasAbove(node, parents, PlanOperatorKind.Limit):
                    node.Warning = "the LIMIT is not pushed into this SORT, so the whole result is " +
                                   "sorted before the first rows are taken - once per page";
                    break;
            }
        }
    }

    /// <summary>
    /// Whether an operator of this kind is somewhere above the node. Above, not directly above: the
    /// engine puts ALIAS and ExcludeInternal in between, and they change nothing about the question.
    /// </summary>
    private static bool HasAbove(PlanNode node, IReadOnlyDictionary<int, PlanNode> parents, PlanOperatorKind kind)
    {
        var current = node;

        while (parents.TryGetValue(current.Id, out var parent))
        {
            if (parent.Kind == kind)
                return true;

            current = parent;
        }

        return false;
    }

    private static int ToInt(object? value)
    {
        return value switch
        {
            int number => number,
            long number => (int)number,
            null or DBNull => -1,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : -1
        };
    }

    #endregion
}
