using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// <c>ORDER BY</c> over a grouped query, when the thing being ordered by is an EXPRESSION rather than
/// an aggregate or a bare column name.
/// </summary>
/// <remarks>
/// <para>
/// A grouped row carries only the SELECT list, so the ORDER BY is evaluated against a row that has no
/// source columns in it. The planner knew two shapes - an aggregate call, and a column whose name
/// matches a select alias - and let everything else through unchanged, where it met the grouped row
/// and answered <c>Column 'DeviceType' not found</c>.
/// </para>
/// <para>
/// Found on 2026-08-09 while fixing <c>Docs/KnownIssues.md</c> 3, by running the query the issue
/// names rather than by reading the planner: EF Core emits
/// <c>ORDER BY CAST("e"."DeviceType" AS TEXT)</c> for
/// <c>GroupBy(x =&gt; x.Type).Select(g =&gt; g.Key.ToString())</c>, and every such query failed.
/// </para>
/// </remarks>
[TestFixture]
public sealed class GroupedOrderByExpressionTests
{
    #region Fields

    private string m_databasePath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_grouped_orderby_{Guid.NewGuid():N}");

        m_engine = new WitSqlEngine(WitDatabase.Create(m_databasePath), ownsStore: true);

        m_engine.Execute("CREATE TABLE Events (Id INT NOT NULL PRIMARY KEY, DeviceType INT NOT NULL)");

        // 42 twice and 7 once: the counts differ, so ordering by the count and ordering by the key
        // give different answers and neither can pass for the other.
        m_engine.Execute("INSERT INTO Events (Id, DeviceType) VALUES (1, 42)");
        m_engine.Execute("INSERT INTO Events (Id, DeviceType) VALUES (2, 42)");
        m_engine.Execute("INSERT INTO Events (Id, DeviceType) VALUES (3, 7)");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
        m_engine = null!;

        if (!Directory.Exists(m_databasePath))
            return;

        try
        {
            Directory.Delete(m_databasePath, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only - a locked file must not fail the run.
        }
    }

    #endregion

    #region Tests

    /// <summary>
    /// The shape EF Core emits, quoted and aliased exactly as it writes it.
    /// </summary>
    [Test]
    public void OrderingByAnExpressionThatIsAlsoSelectedWorksTest()
    {
        var keys = Keys(
            "SELECT CAST(\"e\".\"DeviceType\" AS TEXT) AS \"Key\", COUNT(*) AS \"Count\" "
            + "FROM \"Events\" AS \"e\" GROUP BY \"e\".\"DeviceType\" "
            + "ORDER BY CAST(\"e\".\"DeviceType\" AS TEXT)");

        // '4' sorts before '7' as text, which is what ordering by the CAST means - and is the answer
        // that says the sort ran on the converted value rather than on the number.
        Assert.That(keys, Is.EqualTo(new[] { "42", "7" }));
    }

    /// <summary>
    /// The same without quoting or aliasing, so that neither is what makes it work.
    /// </summary>
    [Test]
    public void TheSameWithoutQuotingOrAliasesTest()
    {
        var keys = Keys("SELECT CAST(DeviceType AS TEXT) AS K, COUNT(*) AS C FROM Events "
                        + "GROUP BY DeviceType ORDER BY CAST(DeviceType AS TEXT)");

        Assert.That(keys, Is.EqualTo(new[] { "42", "7" }));
    }

    /// <summary>
    /// DESC as well, because a resolution that lost the direction would still pass a case that only
    /// checks membership.
    /// </summary>
    [Test]
    public void TheDirectionSurvivesTheResolutionTest()
    {
        var keys = Keys("SELECT CAST(DeviceType AS TEXT) AS K, COUNT(*) AS C FROM Events "
                        + "GROUP BY DeviceType ORDER BY CAST(DeviceType AS TEXT) DESC");

        Assert.That(keys, Is.EqualTo(new[] { "7", "42" }));
    }

    /// <summary>
    /// An arithmetic expression rather than a cast: the fix is about "the same expression as a select
    /// item", not about CAST, and one case per shape is what says so.
    /// </summary>
    [Test]
    public void AnArithmeticExpressionResolvesTooTest()
    {
        var rows = m_engine
            .Query("SELECT DeviceType * 2 AS Doubled, COUNT(*) AS C FROM Events "
                   + "GROUP BY DeviceType ORDER BY DeviceType * 2")
            .Select(row => row["Doubled"].AsInt64())
            .ToArray();

        Assert.That(rows, Is.EqualTo(new[] { 14L, 84L }));
    }

    /// <summary>
    /// CONTROL: the two shapes that always worked still do - an aggregate, and the select alias. If
    /// the new resolution had displaced them, these would be the cases to say so.
    /// </summary>
    [Test]
    public void TheShapesThatAlreadyWorkedStillDoTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Keys("SELECT CAST(DeviceType AS TEXT) AS K, COUNT(*) AS C FROM Events "
                     + "GROUP BY DeviceType ORDER BY K"),
                Is.EqualTo(new[] { "42", "7" }), "by the select alias");

            Assert.That(
                Keys("SELECT CAST(DeviceType AS TEXT) AS K, COUNT(*) AS C FROM Events "
                     + "GROUP BY DeviceType ORDER BY COUNT(*)"),
                Is.EqualTo(new[] { "7", "42" }), "by an aggregate - and the other way round, which "
                                                 + "is what says the ORDER BY decided the answer");
        });
    }

    /// <summary>
    /// Ordering by the grouping column itself, when that column is NOT in the SELECT list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This case PINNED the refusal until 2026-08-10 - a grouped row carried only what was selected,
    /// so there was nothing to sort on, and the caller was handed the sort's own <i>"Failed to
    /// compare two elements in the array"</i>. Its own text said that making the group iterator carry
    /// its key would turn it red and that it should then assert the ORDER; that is what it does now.
    /// <c>Docs/KnownIssues.md</c> 15, and <see cref="GroupingKeyReachabilityTests"/> is the fix's own
    /// fixture.
    /// </para>
    /// <para>
    /// The answer here is <c>7, 42</c> where every other case in this file answers <c>42, 7</c>:
    /// ordering by the NUMBER is not ordering by the cast, and that is what says the clause reached
    /// the grouping column rather than falling back on the select item that renders it.
    /// </para>
    /// </remarks>
    [Test]
    public void OrderingByAGroupingColumnThatIsNotSelectedWorksTest()
    {
        var keys = Keys("SELECT CAST(DeviceType AS TEXT) AS K, COUNT(*) AS C FROM Events "
                        + "GROUP BY DeviceType ORDER BY DeviceType");

        Assert.That(keys, Is.EqualTo(new[] { "7", "42" }));
    }

    /// <summary>
    /// And the width is unchanged: the carried key is the planner's, and a query that asked for two
    /// columns gets two. Asserted on a file-backed database rather than an in-memory one, so that
    /// nothing about the fix rests on the store.
    /// </summary>
    [Test]
    public void TheCarriedKeyDoesNotWidenTheResultTest()
    {
        var rows = m_engine.Query("SELECT CAST(DeviceType AS TEXT) AS K, COUNT(*) AS C FROM Events "
                                  + "GROUP BY DeviceType ORDER BY DeviceType");

        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(rows.Select(row => row.ColumnCount), Is.All.EqualTo(2));
    }

    #endregion

    #region Tools

    private string[] Keys(string sql) =>
        m_engine.Query(sql).Select(row => row[0].AsString()).ToArray();

    #endregion
}
