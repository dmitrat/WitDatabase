using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// <b>PINS A DEFECT, NOT CORRECT BEHAVIOUR.</b> <c>ORDER BY</c> and <c>LIMIT</c> over a
/// <c>UNION</c> apply to the FIRST arm alone, not to the combined result.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-10 while fixing <c>Docs/KnownIssues.md</c> 16; recorded there as 18. It is
/// **pre-existing and independent of 16** - the same thing happens when the clause names the column
/// - and 16 is only what made it visible, because until then the positional form sorted nothing
/// anywhere.
/// </para>
/// <para>
/// <b>The mechanism is the order of the plan.</b> <c>QueryPlanner.Plan</c> applies <c>ORDER BY</c>,
/// <c>LIMIT</c> and <c>DISTINCT</c> inside the aggregate/non-aggregate branch and only then calls
/// <c>ApplySetOperations</c>, so every one of them is wrapped by the union rather than wrapping it.
/// The parser is not at fault: it hangs the clauses on the outer statement, which is where SQL puts
/// them.
/// </para>
/// <para>
/// <b>Measured against SQLite</b> rather than assumed: it answers
/// <c>SELECT Kind FROM T UNION ALL SELECT Kind FROM T ORDER BY Kind</c> as
/// <c>a a b b c c d d</c>, this engine as <c>a b c d c a b d</c> - the left arm sorted and the right
/// left where it was.
/// </para>
/// <para>
/// <b>Why the fixture's two arms overlap.</b> Two arms whose values do not interleave answer
/// correctly by accident - sorting each separately and concatenating gives the same list as sorting
/// the whole. The case that can fail needs the second arm to hold values that must come BEFORE the
/// first arm's, which is what these two do.
/// </para>
/// </remarks>
[TestFixture]
public class OrderByOverASetOperationTests
{
    #region Fields

    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_engine = new WitSqlEngine(WitDatabase.CreateInMemory(), ownsStore: true);

        m_engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Kind VARCHAR(20), Amount INT)");

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
        string.Join("|", m_engine.Query(sql).Select(row => row[0].ToString()));

    #endregion

    #region Controls

    /// <summary>
    /// CONTROL: the union itself is right - both arms are there, in arm order - and ordering one arm
    /// on its own is right. So what the pins measure is the two of them meeting.
    /// </summary>
    [Test]
    public void ControlTheUnionAndTheOrderingBothWorkAloneTest()
    {
        Assert.That(Answer("SELECT Kind FROM T WHERE Amount > 25 UNION ALL SELECT Kind FROM T WHERE Amount < 25"),
            Is.EqualTo("Text:c|Text:d|Text:a|Text:b"));

        Assert.That(Answer("SELECT Kind FROM T ORDER BY Kind"), Is.EqualTo("Text:a|Text:b|Text:c|Text:d"));
    }

    /// <summary>
    /// CONTROL, and the reason the pins below use overlapping arms: two arms that do not interleave
    /// come out right whatever the plan does, because sorting each and concatenating is the same
    /// list. A fixture built only from this shape would report no defect at all.
    /// </summary>
    [Test]
    public void ControlArmsThatDoNotInterleaveAnswerCorrectlyEitherWayTest()
    {
        Assert.That(
            Answer("SELECT Kind FROM T WHERE Amount < 25 UNION ALL SELECT Kind FROM T WHERE Amount > 25 "
                   + "ORDER BY Kind"),
            Is.EqualTo("Text:a|Text:b|Text:c|Text:d"));
    }

    #endregion

    #region The pins

    /// <summary>
    /// PINS A DEFECT. Should answer <c>c c d d a a b b</c> sorted into <c>a a b b c c d d</c>;
    /// answers the first arm sorted followed by the second arm untouched.
    /// </summary>
    [TestCase("ORDER BY Kind")]
    [TestCase("ORDER BY 1")]
    public void OrderingOverAUnionSortsOnlyTheFirstArmTest(string orderBy)
    {
        var sql = "SELECT Kind FROM T WHERE Amount > 25 UNION ALL SELECT Kind FROM T WHERE Amount < 25 "
                  + orderBy;

        Assert.That(Answer(sql), Is.EqualTo("Text:c|Text:d|Text:a|Text:b"),
            "PINS A DEFECT: SQLite answers a|a|b|b for the doubled form of this and a|b|c|d here - "
            + "when ORDER BY is moved outside the set operation this goes red and should assert "
            + "Text:a|Text:b|Text:c|Text:d");
    }

    /// <summary>
    /// The workaround `WitSQL.md` recommends, asserted rather than asserted-of. A documented
    /// workaround that nobody has run is the most misleading thing a reference can carry - see
    /// <c>prove-defects-by-execution</c> - so it lives here beside the defect it works around, and
    /// goes red the day it stops working.
    /// </summary>
    [Test]
    public void WrappingTheUnionInADerivedTableOrdersTheWholeResultTest()
    {
        Assert.That(
            Answer("SELECT U.Kind FROM (SELECT Kind FROM T WHERE Amount > 25 "
                   + "UNION ALL SELECT Kind FROM T WHERE Amount < 25) AS U ORDER BY U.Kind"),
            Is.EqualTo("Text:a|Text:b|Text:c|Text:d"));

        Assert.That(
            Answer("SELECT U.Kind FROM (SELECT Kind FROM T WHERE Amount > 25 "
                   + "UNION ALL SELECT Kind FROM T WHERE Amount < 25) AS U ORDER BY U.Kind LIMIT 1"),
            Is.EqualTo("Text:a"));
    }

    /// <summary>
    /// PINS A DEFECT, and this half loses rows rather than misplacing them: the limit cuts the first
    /// arm, so the union still returns everything the second arm has.
    /// </summary>
    [Test]
    public void ALimitOverAUnionCutsOnlyTheFirstArmTest()
    {
        var sql = "SELECT Kind FROM T WHERE Amount > 25 UNION ALL SELECT Kind FROM T WHERE Amount < 25 LIMIT 1";

        Assert.That(Answer(sql), Is.EqualTo("Text:c|Text:a|Text:b"),
            "PINS A DEFECT: a LIMIT 1 over a union must answer ONE row - invert when it does");
    }

    #endregion
}
