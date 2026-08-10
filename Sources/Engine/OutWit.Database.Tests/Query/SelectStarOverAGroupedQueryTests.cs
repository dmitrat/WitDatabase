using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// <b>PINS A DEFECT, NOT CORRECT BEHAVIOUR.</b> A <c>*</c> is expanded only when it is the ONLY
/// select item. Anywhere else it becomes a single column holding NULL.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-10 alongside <c>Docs/KnownIssues.md</c> 15 and 16; recorded there as 17. A star
/// is a select item with no expression, and both the projection and the group iterator write one
/// NULL for such an item - so <c>SELECT * … GROUP BY</c> is the whole result, and
/// <c>SELECT *, Amount * 2</c> loses its first column.
/// </para>
/// <para>
/// <b>This engine matches nobody.</b> PostgreSQL and SQL Server REFUSE the query, naming the first
/// column that is neither grouped by nor aggregated; SQLite ACCEPTS it and answers with the columns
/// of an arbitrary row from each group. Either would be defensible. Counting the groups correctly and
/// then describing each of them as NULL is a wrong answer wearing a right shape - and the count is
/// right, which is what makes it look like data.
/// </para>
/// <para>
/// <b>The decision this needs is which of the two to become</b>, and it is Dmitry's rather than a
/// mechanical repair: refusing is the safer reading and would turn a silent answer into a loud one,
/// while answering the way SQLite does keeps a shape that existing callers may have written. Whichever
/// lands, this case goes red and is replaced by the chosen behaviour.
/// </para>
/// </remarks>
[TestFixture]
public class SelectStarOverAGroupedQueryTests
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

        foreach (var (kind, amount) in new[] { ("c", 30), ("a", 10), ("b", 20), ("c", 31) })
            m_engine.Execute($"INSERT INTO T (Kind, Amount) VALUES ('{kind}', {amount})");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Control

    /// <summary>
    /// CONTROL: the star itself works, and the grouping itself works. So what the pin below measures
    /// is the two of them meeting, rather than either being broken on its own.
    /// </summary>
    [Test]
    public void ControlTheStarAndTheGroupingBothWorkAloneTest()
    {
        var all = m_engine.Query("SELECT * FROM T");

        Assert.That(all.Count, Is.EqualTo(4));
        Assert.That(all[0].ColumnCount, Is.EqualTo(3), "Id, Kind, Amount");

        var grouped = m_engine.Query("SELECT Kind, COUNT(*) FROM T GROUP BY Kind");

        Assert.That(grouped.Count, Is.EqualTo(3));
        Assert.That(grouped.Select(row => row[1].AsInt64()), Is.EquivalentTo(new long[] { 1, 1, 2 }));
    }

    #endregion

    #region The pin

    /// <summary>
    /// PINS A DEFECT. Should either be refused (PostgreSQL, SQL Server) or answer three rows of three
    /// columns (SQLite). It answers three rows of ONE column, and that column is NULL.
    /// </summary>
    [Test]
    public void SelectStarOverAGroupedQueryAnswersNullsTest()
    {
        var result = m_engine.Query("SELECT * FROM T GROUP BY Kind");

        Assert.That(result.Count, Is.EqualTo(3), "the groups themselves are counted correctly");

        Assert.That(result.Select(row => row.ColumnCount), Is.All.EqualTo(1),
            "PINS A DEFECT: the star expanded to nothing - three columns were asked for");

        Assert.That(result.Select(row => row[0].IsNull), Is.All.True,
            "PINS A DEFECT: and the one column carries no value at all - invert this case when the "
            + "query is either refused or answered");
    }

    /// <summary>
    /// PINS A DEFECT, and this is the half with no decision attached: a star sharing its select list
    /// with other items should expand, as it does in SQLite, and instead becomes one NULL column.
    /// Found 2026-08-10 while measuring <c>Docs/KnownIssues.md</c> 16 - it is what makes
    /// <c>ORDER BY 4</c> unresolvable over such a list.
    /// </summary>
    [Test]
    public void AStarSharingItsSelectListIsNotExpandedTest()
    {
        var result = m_engine.Query("SELECT *, Amount * 2 FROM T");

        Assert.That(result.Count, Is.EqualTo(4));

        Assert.That(result.Select(row => row.ColumnCount), Is.All.EqualTo(2),
            "PINS A DEFECT: the star is one column here, where it should be three - invert to 4");

        Assert.That(result.Select(row => row[0].IsNull), Is.All.True,
            "PINS A DEFECT: and that column is NULL on every row");

        Assert.That(result.Select(row => row[1].AsInt64()), Is.EquivalentTo(new long[] { 60, 20, 40, 62 }),
            "the item BESIDE the star is computed correctly, which is what makes the loss silent");
    }

    #endregion
}
