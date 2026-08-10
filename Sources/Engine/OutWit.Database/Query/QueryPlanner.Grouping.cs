using OutWit.Database.Iterators;
using OutWit.Database.Parser.Analysis;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;

namespace OutWit.Database.Query;

/// <summary>
/// What a grouped query may select, order by and filter on - and what a <c>*</c> stands for.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>Docs/KnownIssues.md</c> 17.</b> A select item that is a <c>*</c> carries no expression, and
/// both the projection and the group iterator wrote a single NULL for it. So
/// <c>SELECT * FROM T GROUP BY Kind</c> answered one NULL column per group and
/// <c>SELECT *, Amount * 2 FROM T</c> lost its first column - with the row and group COUNTS right,
/// which is what made the answer look like data.
/// </para>
/// <para>
/// Underneath it sat a larger hole with no star in it at all:
/// <c>SELECT Kind, Amount FROM T GROUP BY Kind</c> answered <c>Amount</c> from an arbitrary row of
/// each group, and <c>SELECT Kind, COUNT(*) FROM T</c> - no <c>GROUP BY</c> anywhere - answered one
/// row with <c>Kind</c> taken from the first. Both are silent wrong answers and both are far likelier
/// to be written than the star.
/// </para>
/// <para>
/// <b>Two changes, and only one of them was a decision.</b> A star is expanded into the columns it
/// stands for, which is what all three reference databases do and needed no choosing. And every
/// column that is neither grouped by nor aggregated is refused, which is PostgreSQL's and SQL
/// Server's rule - Dmitry's decision, 2026-08-10, taken with the cost measured first: adopting it
/// turns ONE test red across the engine, ADO.NET, EF, Studio and the 8,145-case EF specification
/// suite, and that one is the pin recording the defect.
/// </para>
/// <para>
/// <b>The strict form, deliberately.</b> PostgreSQL also accepts a column that is functionally
/// dependent on a grouped PRIMARY KEY - <c>SELECT * FROM T GROUP BY Id</c> - and SQL Server does not.
/// The stricter reading is implemented because it is the simpler promise and because widening it
/// later cannot break a query that works today, while narrowing it could.
/// </para>
/// </remarks>
public sealed partial class QueryPlanner
{
    #region Star expansion

    /// <summary>
    /// The select list with every <c>*</c> replaced by the columns it stands for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A star has to become real select items because that is the only shape the projection and the
    /// group iterator can evaluate - an item with no expression is what they wrote NULL for.
    /// </para>
    /// <para>
    /// <b>The single bare star of an ordinary query never reaches here</b>, because
    /// <see cref="ApplyProjection"/> answers that one with <see cref="IteratorExcludeInternal"/>
    /// before asking. It is NOT skipped here as well, and the difference matters: an aggregate query
    /// has no projection step at all - the group iterator is the projection - so
    /// <c>SELECT * FROM T GROUP BY Kind</c> would keep answering NULL. Measured, by skipping it and
    /// watching the pin for exactly that query stay green.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ClauseSelectItem> ExpandStars(
        IReadOnlyList<ClauseSelectItem> selectList,
        IReadOnlyList<WitSqlColumnInfo> sourceSchema)
    {
        if (!selectList.Any(item => item.IsStar))
            return selectList;

        var expanded = new List<ClauseSelectItem>(selectList.Count + sourceSchema.Count);

        foreach (var item in selectList)
        {
            if (!item.IsStar)
            {
                expanded.Add(item);
                continue;
            }

            foreach (var column in sourceSchema)
            {
                // The internal row id is on every scanned row and is not part of any result.
                if (IteratorExcludeInternal.IsInternalColumn(column.Name))
                    continue;

                // A qualified star names one table. A joined row carries its columns both bare and
                // qualified, so matching the prefix is what keeps `t.*` to that table's half - and
                // an unqualified star takes the bare names only, or every column would appear twice.
                if (item.TableName is { } table)
                {
                    if (!column.Name.StartsWith($"{table}.", StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                else if (column.Name.Contains('.'))
                {
                    continue;
                }

                expanded.Add(new ClauseSelectItem
                {
                    Expression = new WitSqlExpressionColumnRef { ColumnName = column.Name },
                    Alias = column.Name
                });
            }
        }

        return expanded;
    }

    #endregion

    #region What a grouped query may name

    /// <summary>
    /// Refuses a query that names a column no group can answer for: one that is neither in the
    /// <c>GROUP BY</c> clause nor inside an aggregate.
    /// </summary>
    /// <remarks>
    /// Applies to an aggregate query only, which includes one with aggregates and no <c>GROUP BY</c>
    /// at all - there the whole table is one group, so no bare column is answerable either.
    /// </remarks>
    private static void ValidateGroupedQuery(
        IReadOnlyList<ClauseSelectItem> selectList,
        WitSqlStatementSelect select)
    {
        var grouping = select.GroupByClause ?? [];

        foreach (var item in selectList)
            CheckGroupedExpression(item.Expression, grouping, []);

        // ORDER BY and HAVING may also name an OUTPUT alias, which is not a source column at all and
        // is answered from the projected row. Every reference database allows it, and so did this
        // one before the check existed - measured: without this, four working cases go red.
        var aliases = selectList
            .Select(item => item.Alias)
            .OfType<string>()
            .ToArray();

        CheckGroupedExpression(select.HavingClause, grouping, aliases);

        foreach (var order in select.OrderByClause ?? [])
            CheckGroupedExpression(order.Expression, grouping, aliases);
    }

    private static void CheckGroupedExpression(
        WitSqlExpression? expression,
        IReadOnlyList<WitSqlExpression> grouping,
        IReadOnlyList<string> aliases)
    {
        if (expression == null)
            return;

        // Being a grouping expression settles it, whatever shape it is - and it settles everything
        // inside it too, which is why this is asked before anything descends.
        if (MatchesGroupingExpression(expression, grouping))
            return;

        // An aggregate answers for a whole group, so what it reads is its own business.
        if (expression is WitSqlExpressionFunctionCall call && IsAggregateFunction(call))
            return;

        if (expression is WitSqlExpressionColumnRef column)
        {
            if (!IsGroupedColumn(column.ColumnName, grouping) && !IsSelectAlias(column.ColumnName, aliases))
                throw ColumnIsNotGrouped(column.ColumnName);

            return;
        }

        if (expression is WitSqlExpressionBinary binary)
        {
            CheckGroupedExpression(binary.Left, grouping, aliases);
            CheckGroupedExpression(binary.Right, grouping, aliases);
            return;
        }

        if (expression is WitSqlExpressionUnary unary)
        {
            CheckGroupedExpression(unary.Operand, grouping, aliases);
            return;
        }

        // Anything else is walked generically, so a node type cannot be forgotten and a new one is
        // covered the day the grammar gains it. The walk stops at a nested statement, so a column a
        // subquery reads is the subquery's business.
        foreach (var node in WitSqlNodes.SelfAndDescendants(expression).Skip(1).OfType<WitSqlExpression>())
        {
            if (node is not WitSqlExpressionColumnRef nested)
                continue;

            if (IsGroupedColumn(nested.ColumnName, grouping) || IsSelectAlias(nested.ColumnName, aliases))
                continue;

            if (IsInside(nested, expression, grouping))
                continue;

            throw ColumnIsNotGrouped(nested.ColumnName);
        }
    }

    /// <summary>
    /// Whether <paramref name="target"/> sits inside an aggregate call or inside a grouping
    /// expression somewhere under <paramref name="root"/> - either of which answers for it.
    /// </summary>
    private static bool IsInside(
        WitSqlExpression target,
        WitSqlExpression root,
        IReadOnlyList<WitSqlExpression> grouping)
    {
        foreach (var node in WitSqlNodes.SelfAndDescendants(root).OfType<WitSqlExpression>())
        {
            if (ReferenceEquals(node, target))
                continue;

            var answersForIt = node is WitSqlExpressionFunctionCall call && IsAggregateFunction(call)
                               || MatchesGroupingExpression(node, grouping);

            if (!answersForIt)
                continue;

            if (WitSqlNodes.SelfAndDescendants(node).Any(inner => ReferenceEquals(inner, target)))
                return true;
        }

        return false;
    }

    private static bool MatchesGroupingExpression(
        WitSqlExpression expression,
        IReadOnlyList<WitSqlExpression> grouping)
    {
        var text = Render(expression);

        return text != null && grouping.Any(key => Render(key) == text);
    }

    /// <summary>
    /// Compared on the column NAME alone, so that <c>u.Name</c> and <c>GROUP BY Name</c> agree. The
    /// qualification is deliberately ignored: a check that refuses more than it understands would
    /// turn a working query into an error, which is the one outcome worse than the defect.
    /// </summary>
    private static bool IsGroupedColumn(string columnName, IReadOnlyList<WitSqlExpression> grouping) =>
        grouping.Any(key => key is WitSqlExpressionColumnRef column
                            && string.Equals(column.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));

    private static bool IsSelectAlias(string columnName, IReadOnlyList<string> aliases) =>
        aliases.Any(alias => string.Equals(alias, columnName, StringComparison.OrdinalIgnoreCase));

    private static InvalidOperationException ColumnIsNotGrouped(string columnName) =>
        new($"Column '{columnName}' must appear in the GROUP BY clause or be used in an aggregate "
            + "function.");

    #endregion
}
