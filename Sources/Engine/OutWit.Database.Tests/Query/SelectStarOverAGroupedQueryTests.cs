using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// <b>PINS A DEFECT, NOT CORRECT BEHAVIOUR.</b> <c>SELECT * … GROUP BY</c> is neither refused nor
/// answered: it returns one group per row of output, each a single column holding NULL.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-10 alongside <c>Docs/KnownIssues.md</c> 15; recorded there as 17. A grouped row
/// is built out of the SELECT list, and a star is not an expression - so the group iterator writes
/// one NULL for it and names the column after its position.
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

    #endregion
}
