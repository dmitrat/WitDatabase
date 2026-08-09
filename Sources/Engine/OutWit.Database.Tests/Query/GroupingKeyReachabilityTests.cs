using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// <b>PINS A DEFECT, NOT CORRECT BEHAVIOUR.</b> A column the query GROUPS BY cannot be reached from
/// <c>ORDER BY</c> or <c>HAVING</c> unless it is also in the SELECT list.
/// </summary>
/// <remarks>
/// <para>
/// A grouped row is built out of the SELECT list and nothing else, so an <c>ORDER BY</c> or
/// <c>HAVING</c> naming anything else is evaluated against a row that does not have it. Measured
/// 2026-08-09; `Docs/KnownIssues.md` 15.
/// </para>
/// <para>
/// <b>Two things the record got wrong and these cases fix.</b> It said the shape is "refused" - it
/// is not, it fails with <c>Column 'Kind' not found</c>, and from <c>ORDER BY</c> that arrives
/// wrapped in .NET's own <c>Failed to compare two elements in the array</c>, which says nothing to
/// anyone. And it named <c>ORDER BY</c> only: <c>HAVING</c> has the identical hole, which matters
/// more, because <c>HAVING</c> over a grouping column is an everyday shape that PostgreSQL, SQL
/// Server and SQLite all accept.
/// </para>
/// <para>
/// <b>When it is fixed, these cases invert</b> - the assertions become the answers written beside
/// them - and the controls stay exactly as they are.
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

        foreach (var (kind, amount) in new[] { ("c", 1), ("a", 2), ("b", 3), ("a", 4), ("c", 5), ("b", 6) })
            m_engine.Execute($"INSERT INTO T (Kind, Amount) VALUES ('{kind}', {amount})");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: with the grouping column IN the select list, ordering by it works and the order is
    /// right. Without this, "it fails when the column is not selected" would be equally consistent
    /// with grouping being broken altogether.
    /// </summary>
    [Test]
    public void ControlAGroupingColumnInTheSelectListCanBeOrderedByTest()
    {
        var result = m_engine.Query("SELECT Kind, COUNT(*) FROM T GROUP BY Kind ORDER BY Kind");

        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result[0][0].ToString(), Does.EndWith("a"));
        Assert.That(result[2][0].ToString(), Does.EndWith("c"));
    }

    /// <summary>
    /// Control: an aggregate is reachable from both clauses, so what the cases below measure is the
    /// grouping KEY rather than the clauses themselves.
    /// </summary>
    [Test]
    public void ControlAnAggregateIsReachableFromBothClausesTest()
    {
        Assert.That(
            () => m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY COUNT(*)"),
            Throws.Nothing);

        Assert.That(
            () => m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind HAVING COUNT(*) > 1"),
            Throws.Nothing);
    }

    #endregion

    #region The pins

    /// <summary>
    /// PINS A DEFECT. Should answer three rows ordered a, b, c.
    /// </summary>
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind")]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind DESC")]
    [TestCase("SELECT SUM(Amount) FROM T GROUP BY Kind ORDER BY Kind")]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY UPPER(Kind)")]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind, COUNT(*)")]
    public void OrderingByAGroupingColumnThatIsNotSelectedFailsTest(string sql)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => m_engine.Query(sql));

        // The message a user gets is .NET's, from the sort that could not compare two rows; what
        // went wrong is one level in. Both halves are asserted, because the outer one is what a
        // consumer reports and the inner one is what has to change.
        Assert.That(ex!.Message, Does.Contain("compare"),
            "PINS A DEFECT: the shape is not refused, it fails inside the sort - if it now says "
            + "something a user can act on, or answers correctly, invert this case");

        Assert.That(ex.InnerException, Is.TypeOf<KeyNotFoundException>());
        Assert.That(ex.InnerException!.Message, Does.Contain("Kind"));
    }

    /// <summary>
    /// PINS A DEFECT, and this is the half that matters more: <c>HAVING</c> over a grouping column
    /// is an everyday shape and all three target databases accept it. Should answer two rows.
    /// </summary>
    [Test]
    public void HavingOnAGroupingColumnThatIsNotSelectedFailsTest()
    {
        var ex = Assert.Throws<KeyNotFoundException>(
            () => m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind HAVING Kind > 'a'"));

        Assert.That(ex!.Message, Does.Contain("Kind"),
            "PINS A DEFECT: a grouped row carries only the select list, so HAVING cannot see the "
            + "column the query grouped by - invert this case when it can");
    }

    #endregion
}
