using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// A column the query GROUPS BY is reachable from <c>ORDER BY</c> and from <c>HAVING</c> whether or
/// not it is also in the SELECT list. <c>Docs/KnownIssues.md</c> 15, fixed 2026-08-10.
/// </summary>
/// <remarks>
/// <para>
/// These cases used to PIN the defect: a grouped row was built out of the SELECT list and nothing
/// else, so either clause naming anything else was evaluated against a row that does not have it -
/// from <c>ORDER BY</c> that surfaced as .NET's own <c>Failed to compare two elements in the
/// array</c>, which tells a consumer nothing at all. The planner carries the grouping expressions on
/// the grouped row now and drops them again after the sort; every case below is the inversion of the
/// one that pinned it.
/// </para>
/// <para>
/// <b>The fixture's shape is what gives the cases power.</b> The four kinds have DIFFERENT row
/// counts (a:1, b:2, c:3, d:1), so the order is observable through <c>COUNT(*)</c> alone - which is
/// the only thing on the row once the carried key is dropped. And the insertion order (c, a, b, d)
/// is not the sorted order, so a sort that did nothing would be caught rather than agreed with.
/// </para>
/// <para>
/// <b>Both directions measured, part by part.</b> With the carrying removed altogether, 15 of these
/// 18 go red and the three controls stay green. With the TRIM removed and everything else in place,
/// four go red - the width, DISTINCT, the HAVING count and the plan. With the HAVING rewrite
/// removed, exactly one does: the grouping EXPRESSION, because a plain grouping COLUMN is served by
/// the carried column's own name and needs no rewrite at all. With the carrying made unconditional,
/// only the plan case goes red, which is what makes it the control on the fix's cost.
/// </para>
/// </remarks>
[TestFixture]
public class GroupingKeyReachabilityTests
{
    #region Fields

    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_engine = new WitSqlEngine(WitDatabase.CreateInMemory(), ownsStore: true);

        m_engine.Execute(
            "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Kind VARCHAR(20), Amount INT)");

        // Scrambled on purpose - c, a, b, d is what an unsorted answer looks like.
        var rows = new[]
        {
            ("c", 30), ("a", 10), ("b", 20), ("d", 40), ("c", 31), ("b", 21), ("c", 32)
        };

        foreach (var (kind, amount) in rows)
            m_engine.Execute($"INSERT INTO T (Kind, Amount) VALUES ('{kind}', {amount})");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Functions

    private IReadOnlyList<long> QueryFirstColumn(string sql)
    {
        return [.. m_engine.Query(sql).Select(row => row[0].AsInt64())];
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: with the grouping column IN the select list, ordering by it works and the order is
    /// right. Without this, "it works when the column is not selected" would be equally consistent
    /// with grouping being broken altogether.
    /// </summary>
    [Test]
    public void ControlAGroupingColumnInTheSelectListCanBeOrderedByTest()
    {
        var result = m_engine.Query("SELECT Kind, COUNT(*) FROM T GROUP BY Kind ORDER BY Kind");

        Assert.That(result.Count, Is.EqualTo(4));
        Assert.That(result[0][0].ToString(), Does.EndWith("a"));
        Assert.That(result[3][0].ToString(), Does.EndWith("d"));
    }

    /// <summary>
    /// Control: an aggregate is reachable from both clauses, so what the cases below measure is the
    /// grouping KEY rather than the clauses themselves.
    /// </summary>
    [Test]
    public void ControlAnAggregateIsReachableFromBothClausesTest()
    {
        Assert.That(
            QueryFirstColumn("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY COUNT(*)"),
            Is.EqualTo(new long[] { 1, 1, 2, 3 }));

        Assert.That(
            m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind HAVING COUNT(*) > 1").Count,
            Is.EqualTo(2));
    }

    /// <summary>
    /// Control: a column that is neither grouped by nor aggregated stays unreachable from both
    /// clauses, which is what all three target databases do with it. Without this, the fix could be
    /// "every column of some row in the group is reachable" - an arbitrary row's value wearing an
    /// answer's clothes - and every case above would pass just the same.
    /// </summary>
    [Test]
    public void ControlANonGroupingColumnIsReachableFromNeitherClauseTest()
    {
        Assert.That(
            () => m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Amount"),
            Throws.Exception);

        Assert.That(
            () => m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind HAVING Amount > 1"),
            Throws.Exception);
    }

    #endregion

    #region ORDER BY

    /// <summary>
    /// The five shapes that used to fail. Each answers with the counts in the order the clause asks
    /// for, and the counts are the only thing on the row - the carried key has been dropped by then.
    /// </summary>
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind", new long[] { 1, 2, 3, 1 })]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind DESC", new long[] { 1, 3, 2, 1 })]
    [TestCase("SELECT SUM(Amount) FROM T GROUP BY Kind ORDER BY Kind", new long[] { 10, 41, 93, 40 })]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY UPPER(Kind)", new long[] { 1, 2, 3, 1 })]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind, COUNT(*)", new long[] { 1, 2, 3, 1 })]
    public void AGroupingColumnCanBeOrderedByWithoutBeingSelectedTest(string sql, long[] expected)
    {
        Assert.That(QueryFirstColumn(sql), Is.EqualTo(expected));
    }

    /// <summary>
    /// The grouping key rides on the row for the sort's benefit and is gone by the time anyone sees
    /// it. Asserted as a WIDTH, because that is the one thing the values cannot say: a query that
    /// asked for one column and got two is a wrong shape however right the numbers are.
    /// </summary>
    [Test]
    public void TheCarriedGroupingKeyIsNotInTheResultTest()
    {
        var result = m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind");

        Assert.That(result.Count, Is.EqualTo(4));
        Assert.That(result.Select(row => row.ColumnCount), Is.All.EqualTo(1));
        Assert.That(m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind").First()[0].AsInt64(),
            Is.EqualTo(1));
    }

    /// <summary>
    /// And the width is not the only thing that would go wrong: DISTINCT compares whole rows, so a
    /// carried key left on the row would make every group distinct from every other and the query
    /// would answer four where three is right. This is the case that fails if the trim is moved to
    /// after DISTINCT rather than before it.
    /// </summary>
    [Test]
    public void DistinctCountsTheSelectedColumnsAndNotTheCarriedKeyTest()
    {
        // Four groups, three distinct counts: a and d both have one row.
        Assert.That(
            QueryFirstColumn("SELECT DISTINCT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind"),
            Is.EqualTo(new long[] { 1, 2, 3 }));
    }

    /// <summary>
    /// LIMIT and OFFSET cut the ordered result, not the carried key - same trim, the other consumer.
    /// </summary>
    [Test]
    public void ALimitAppliesToTheOrderedGroupsTest()
    {
        Assert.That(
            QueryFirstColumn("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind LIMIT 2"),
            Is.EqualTo(new long[] { 1, 2 }));

        Assert.That(
            QueryFirstColumn("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind DESC LIMIT 2"),
            Is.EqualTo(new long[] { 1, 3 }));
    }

    #endregion

    #region HAVING

    /// <summary>
    /// The half that matters more: <c>HAVING</c> over a grouping column is an everyday shape and all
    /// three target databases accept it.
    /// </summary>
    [Test]
    public void AGroupingColumnCanBeFilteredInHavingWithoutBeingSelectedTest()
    {
        var result = m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind HAVING Kind > 'a'");

        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result.Select(row => row.ColumnCount), Is.All.EqualTo(1));
    }

    /// <summary>
    /// A key and an aggregate in one predicate, both ways round - the rewrite has to leave the
    /// aggregate alone while resolving the key beside it.
    /// </summary>
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind HAVING Kind > 'a' AND COUNT(*) > 1")]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind HAVING COUNT(*) > 1 AND Kind > 'a'")]
    public void AKeyAndAnAggregateFilterTogetherInHavingTest(string sql)
    {
        // b and c are both > 'a' and have more than one row; d is > 'a' with one row.
        Assert.That(QueryFirstColumn(sql), Is.EquivalentTo(new long[] { 2, 3 }));
    }

    #endregion

    #region A grouping EXPRESSION

    /// <summary>
    /// The key does not have to be a column. <c>GROUP BY UPPER(Kind)</c> is reachable from both
    /// clauses under the same rule, and this is the shape that needs the rewrite rather than the
    /// carried column's name: the row carries the VALUE of <c>UPPER(Kind)</c>, not <c>Kind</c>.
    /// </summary>
    [Test]
    public void AGroupingExpressionIsReachableFromBothClausesTest()
    {
        Assert.That(
            QueryFirstColumn("SELECT COUNT(*) FROM T GROUP BY UPPER(Kind) ORDER BY UPPER(Kind)"),
            Is.EqualTo(new long[] { 1, 2, 3, 1 }));

        Assert.That(
            m_engine.Query("SELECT COUNT(*) FROM T GROUP BY UPPER(Kind) HAVING UPPER(Kind) > 'A'").Count,
            Is.EqualTo(3));
    }

    /// <summary>
    /// An expression OVER a grouping column - not the key itself - resolves too, because the carried
    /// column keeps the key's own name and ordinary evaluation finds it there.
    /// </summary>
    [Test]
    public void AnExpressionOverAGroupingColumnCanBeOrderedByTest()
    {
        Assert.That(
            QueryFirstColumn("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind || 'x'"),
            Is.EqualTo(new long[] { 1, 2, 3, 1 }));
    }

    /// <summary>
    /// Two grouping columns, and the second one is what the query orders by.
    /// </summary>
    [Test]
    public void EveryGroupingColumnIsCarriedNotJustTheFirstTest()
    {
        Assert.That(
            QueryFirstColumn("SELECT COUNT(*) FROM T GROUP BY Kind, Amount ORDER BY Amount"),
            Is.EqualTo(new long[] { 1, 1, 1, 1, 1, 1, 1 }));

        var result = m_engine.Query(
            "SELECT Amount, COUNT(*) FROM T GROUP BY Kind, Amount ORDER BY Kind, Amount");

        Assert.That(result.Select(row => row[0].AsInt64()),
            Is.EqualTo(new long[] { 10, 20, 21, 30, 31, 32, 40 }));
    }

    #endregion

    #region The plan

    /// <summary>
    /// Nothing is carried when nothing needs it, so the commonest grouped query keeps exactly the
    /// plan it had. This is the control on the fix's COST: without it, "the query answers correctly"
    /// would be equally true of a planner that carries and trims on every grouped query there is.
    /// </summary>
    [Test]
    public void AQueryThatSelectsItsGroupingKeyCarriesNothingTest()
    {
        Assert.That(
            Explain("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind"),
            Does.Contain("HIDE GROUPING KEYS"));

        Assert.That(
            Explain("SELECT Kind, COUNT(*) FROM T GROUP BY Kind ORDER BY Kind"),
            Does.Not.Contain("HIDE GROUPING KEYS"));

        Assert.That(
            Explain("SELECT COUNT(*) FROM T GROUP BY Kind"),
            Does.Not.Contain("HIDE GROUPING KEYS"));
    }

    private string Explain(string sql)
    {
        var plan = m_engine.Query($"EXPLAIN {sql}");
        return string.Join("\n", plan.Select(row => string.Join(" ",
            Enumerable.Range(0, row.ColumnCount).Select(i => row[i].ToString()))));
    }

    #endregion
}
