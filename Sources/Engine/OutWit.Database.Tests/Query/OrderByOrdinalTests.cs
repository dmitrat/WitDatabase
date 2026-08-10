using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// <b>PINS A DEFECT, NOT CORRECT BEHAVIOUR.</b> <c>ORDER BY &lt;position&gt;</c> - an integer naming an
/// output column - is accepted, ignored, and answers as if there were no <c>ORDER BY</c> at all.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-10 while fixing <c>Docs/KnownIssues.md</c> 15; recorded there as 16. The record
/// of 15 named <c>ORDER BY 1</c> as one of the shapes that "already works and must keep working" -
/// <b>it does not work and never did</b>, which is the fifth recorded finding in two days to be wrong
/// when measured. See <c>prove-defects-by-execution</c>.
/// </para>
/// <para>
/// <b>The mechanism.</b> The parser makes the integer an ordinary literal, nothing turns it into a
/// position, and <see cref="Iterators.IteratorSort"/> evaluates it per row: every row answers the
/// same number, every comparison is equal, and the sort is a no-op. PostgreSQL, SQL Server and SQLite
/// all implement the positional form, so a query written for any of them comes here and is quietly
/// answered in the wrong order. <b>That is worse in kind than 15 was</b>, which at least failed
/// loudly.
/// </para>
/// <para>
/// <b>Why the cases assert an EQUALITY rather than an order.</b> "The answer is not sorted" would be
/// satisfied by a sort that is merely wrong; what is claimed here is that the clause does exactly
/// nothing, so each case compares the answer WITH the clause against the answer WITHOUT it. When this
/// is fixed those comparisons go red and should be replaced by the sorted answer; the control below
/// stays as it is.
/// </para>
/// </remarks>
[TestFixture]
public class OrderByOrdinalTests
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

        // Written in an order that is neither ascending nor descending in either column, so an
        // answer that happens to look sorted cannot be the unsorted one.
        foreach (var (kind, amount) in new[] { ("c", 30), ("a", 10), ("b", 20), ("d", 40) })
            m_engine.Execute($"INSERT INTO T (Kind, Amount) VALUES ('{kind}', {amount})");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Functions

    private string Answer(string sql) =>
        string.Join("|", m_engine.Query(sql).Select(row =>
            string.Join(",", Enumerable.Range(0, row.ColumnCount).Select(i => row[i].ToString()))));

    #endregion

    #region Control

    /// <summary>
    /// CONTROL: the same queries ordered by NAME are sorted, and differently from how they were
    /// written. Without this, "the positional form changes nothing" would be equally consistent with
    /// a fixture whose rows happen to be in the asked-for order already.
    /// </summary>
    [Test]
    public void ControlOrderingByNameSortsAndTheFixtureIsNotSortedTest()
    {
        var unordered = Answer("SELECT Kind FROM T");
        var byName = Answer("SELECT Kind FROM T ORDER BY Kind");

        Assert.That(byName, Is.EqualTo("Text:a|Text:b|Text:c|Text:d"));
        Assert.That(unordered, Is.Not.EqualTo(byName), "the fixture is not written in sorted order");

        Assert.That(Answer("SELECT Kind, Amount FROM T ORDER BY Amount DESC"),
            Is.EqualTo("Text:d,Integer:40|Text:c,Integer:30|Text:b,Integer:20|Text:a,Integer:10"),
            "and DESC is honoured when the column is named");
    }

    #endregion

    #region The pins

    /// <summary>
    /// PINS A DEFECT. Each of these should sort by the numbered output column; every one of them
    /// answers exactly what the same query without any <c>ORDER BY</c> answers.
    /// </summary>
    [TestCase("SELECT Kind FROM T", "ORDER BY 1")]
    [TestCase("SELECT Kind, Amount FROM T", "ORDER BY 2 DESC")]
    [TestCase("SELECT Kind, Amount FROM T", "ORDER BY 1, 2")]
    [TestCase("SELECT Kind, COUNT(*) FROM T GROUP BY Kind", "ORDER BY 1")]
    [TestCase("SELECT Kind, COUNT(*) FROM T GROUP BY Kind", "ORDER BY 2, 1")]
    public void AnOrdinalOrderByDoesNothingTest(string query, string orderBy)
    {
        Assert.That(Answer($"{query} {orderBy}"), Is.EqualTo(Answer(query)),
            "PINS A DEFECT: the positional ORDER BY is accepted and ignored - when it is implemented "
            + "this goes red and should be replaced by the sorted answer");
    }

    /// <summary>
    /// PINS A DEFECT, and this is the half that says the clause is not merely unimplemented but
    /// unexamined: a position that names no output column is accepted too. PostgreSQL answers
    /// <i>"ORDER BY position 99 is not in select list"</i>.
    /// </summary>
    [Test]
    public void AnOrdinalOutsideTheSelectListIsNotRefusedTest()
    {
        Assert.That(() => m_engine.Query("SELECT Kind FROM T ORDER BY 99"), Throws.Nothing,
            "PINS A DEFECT: an out-of-range position should be refused - invert when it is");

        Assert.That(Answer("SELECT Kind FROM T ORDER BY 99"), Is.EqualTo(Answer("SELECT Kind FROM T")));
    }

    #endregion
}
