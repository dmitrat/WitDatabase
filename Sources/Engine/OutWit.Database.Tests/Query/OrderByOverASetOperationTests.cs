using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// <c>ORDER BY</c>, <c>LIMIT</c> and <c>OFFSET</c> apply to the combined result of a set operation.
/// <c>Docs/KnownIssues.md</c> 18, fixed 2026-08-10.
/// </summary>
/// <remarks>
/// <para>
/// These cases PINNED the defect: <c>QueryPlanner.Plan</c> applied all three inside the
/// aggregate/non-aggregate branch and called <c>ApplySetOperations</c> afterwards, so each was
/// wrapped <b>by</b> the union rather than wrapping it. A sorted union came back sorted per arm, and
/// <c>LIMIT 1</c> over one returned everything the second arm had.
/// </para>
/// <para>
/// <b>Why the fixture's two arms overlap.</b> Two arms whose values do not interleave answer
/// correctly whatever the plan does - sorting each separately and concatenating gives the same list
/// as sorting the whole. The case that can fail needs the second arm to hold values that must come
/// BEFORE the first arm's, which is what these two do; the control below is the non-interleaving
/// shape, kept precisely because it cannot fail.
/// </para>
/// <para>
/// <b>`DISTINCT` is deliberately NOT deferred.</b> <c>SELECT DISTINCT a FROM t UNION ALL …</c>
/// de-duplicates the first arm - that is where SQL puts it - and a case pins that, because moving it
/// out with the other two would be the easy mistake.
/// </para>
/// <para>
/// <b>Measured against SQLite</b> before it was believed: it answers the doubled form of the first
/// case <c>a a b b c c d d</c> where this engine answered <c>a b c d c a b d</c>.
/// </para>
/// <para>
/// <b>Both directions measured, and separately.</b> With the old clause order restored - the arm
/// applies <c>ORDER BY</c> and <c>LIMIT</c>, the union does not - 9 of these 13 go red and four stay
/// green: the two controls that cannot fail, the derived-table form, and the out-of-range position,
/// which is out of range for one arm as well. With <c>DISTINCT</c> deferred along with the other two
/// - the easy mistake - exactly ONE goes red, and it is the case written for that mistake.
/// </para>
/// </remarks>
[TestFixture]
public class OrderByOverASetOperationTests
{
    #region Constants

    // The left arm holds c and d, the right a and b - so the right arm's values must come BEFORE
    // the left's, and "sort each arm and concatenate" cannot pass for "sort the whole".
    private const string LEFT = "SELECT Kind FROM T WHERE Amount > 25";
    private const string RIGHT = "SELECT Kind FROM T WHERE Amount < 25";

    #endregion

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
    /// on its own is right. So what the cases below measure is the two of them meeting.
    /// </summary>
    [Test]
    public void ControlTheUnionAndTheOrderingBothWorkAloneTest()
    {
        Assert.That(Answer($"{LEFT} UNION ALL {RIGHT}"), Is.EqualTo("Text:c|Text:d|Text:a|Text:b"));
        Assert.That(Answer("SELECT Kind FROM T ORDER BY Kind"), Is.EqualTo("Text:a|Text:b|Text:c|Text:d"));
    }

    /// <summary>
    /// CONTROL, and the reason the cases below use overlapping arms: two arms that do not interleave
    /// come out right whatever the plan does, because sorting each and concatenating is the same
    /// list. A fixture built only from this shape would have reported no defect at all.
    /// </summary>
    [Test]
    public void ControlArmsThatDoNotInterleaveAnswerCorrectlyEitherWayTest()
    {
        Assert.That(
            Answer($"{RIGHT} UNION ALL {LEFT} ORDER BY Kind"),
            Is.EqualTo("Text:a|Text:b|Text:c|Text:d"));
    }

    /// <summary>
    /// CONTROL: <c>DISTINCT</c> belongs to the ARM it is written in, and was not moved out with the
    /// other two. Six rows, not four: the first arm de-duplicates to four and the second adds its
    /// two back. Without this, deferring all three clauses together would look correct.
    /// </summary>
    [Test]
    public void ControlDistinctBelongsToItsOwnArmTest()
    {
        Assert.That(
            Answer($"SELECT DISTINCT Kind FROM T UNION ALL {RIGHT} ORDER BY Kind"),
            Is.EqualTo("Text:a|Text:a|Text:b|Text:b|Text:c|Text:d"));
    }

    #endregion

    #region ORDER BY over the combined result

    /// <summary>
    /// The shapes that used to answer arm by arm. <c>c d a b</c> is what sorting the first arm and
    /// concatenating gives; <c>a b c d</c> is what sorting the union gives.
    /// </summary>
    [TestCase("ORDER BY Kind", "Text:a|Text:b|Text:c|Text:d")]
    [TestCase("ORDER BY 1", "Text:a|Text:b|Text:c|Text:d")]
    [TestCase("ORDER BY Kind DESC", "Text:d|Text:c|Text:b|Text:a")]
    public void OrderingAppliesToTheWholeSetOperationTest(string orderBy, string expected)
    {
        Assert.That(Answer($"{LEFT} UNION ALL {RIGHT} {orderBy}"), Is.EqualTo(expected));
    }

    /// <summary>
    /// Three arms, one row each, written in an order that is neither sorted nor reversed - so a plan
    /// that ordered any single arm would leave all three where they were.
    /// </summary>
    [Test]
    public void OrderingReachesEveryArmNotJustTheFirstTwoTest()
    {
        Assert.That(
            Answer("SELECT Kind FROM T WHERE Amount = 40 UNION ALL SELECT Kind FROM T WHERE Amount = 30 "
                   + "UNION ALL SELECT Kind FROM T WHERE Amount = 10 ORDER BY Kind"),
            Is.EqualTo("Text:a|Text:c|Text:d"));
    }

    /// <summary>
    /// An aggregate arm: the ORDER BY is the union's, so the arm must neither sort by it nor - since
    /// <c>Docs/KnownIssues.md</c> 15 - carry a grouping key for it, which would widen the arm's
    /// schema and the set operation compares the two schemas.
    /// </summary>
    [Test]
    public void AnAggregateArmDoesNotCarryTheUnionsOrderByTest()
    {
        m_engine.Execute("CREATE TABLE T2 (Id BIGINT PRIMARY KEY AUTOINCREMENT, Kind VARCHAR(20))");

        foreach (var kind in new[] { "b", "e" })
            m_engine.Execute($"INSERT INTO T2 (Kind) VALUES ('{kind}')");

        Assert.That(
            Answer("SELECT Kind, COUNT(*) FROM T GROUP BY Kind "
                   + "UNION ALL SELECT Kind, COUNT(*) FROM T2 GROUP BY Kind ORDER BY 1"),
            Is.EqualTo("Text:a|Text:b|Text:b|Text:c|Text:d|Text:e"));

        // And the shape KnownIssues 15 is about, where the key is NOT selected: the union's ORDER BY
        // must not make the arm carry it.
        Assert.That(
            () => m_engine.Query("SELECT COUNT(*) FROM T GROUP BY Kind "
                                 + "UNION ALL SELECT COUNT(*) FROM T2 GROUP BY Kind ORDER BY 1"),
            Throws.Nothing);
    }

    #endregion

    #region LIMIT over the combined result

    /// <summary>
    /// This half used to LOSE ROWS rather than misplace them: the limit cut the first arm, so the
    /// union still returned everything the second arm had - a <c>LIMIT 1</c> answering three rows.
    /// </summary>
    [Test]
    public void ALimitAppliesToTheWholeSetOperationTest()
    {
        Assert.That(Answer($"{LEFT} UNION ALL {RIGHT} LIMIT 1"), Is.EqualTo("Text:c"));
        Assert.That(Answer($"{LEFT} UNION ALL {RIGHT} ORDER BY Kind LIMIT 2"), Is.EqualTo("Text:a|Text:b"));
        Assert.That(Answer($"{LEFT} UNION ALL {RIGHT} LIMIT 2 OFFSET 1"), Is.EqualTo("Text:d|Text:a"));
    }

    #endregion

    #region What the clause may name

    /// <summary>
    /// After a union there is no source row left to evaluate an expression against, so the clause is
    /// restricted to a result column or a position, as PostgreSQL restricts it. The message names
    /// the columns.
    /// </summary>
    /// <remarks>
    /// <b>This shape did not fail before</b> - the clause was applied to the first arm, whose source
    /// row still had <c>Amount</c>, so half the answer was quietly ordered by something the caller
    /// could not see. Without the check the failure is .NET's own "Failed to compare two elements in
    /// the array", which is the message <c>KnownIssues</c> 15 existed to get rid of.
    /// </remarks>
    [Test]
    public void OrderingByAColumnThatIsNotInTheResultIsRefusedTest()
    {
        Assert.That(
            () => m_engine.Query($"{LEFT} UNION ALL {RIGHT} ORDER BY Amount"),
            Throws.InvalidOperationException
                .With.Message.Contains("'Amount' is not a column of the result")
                .And.Message.Contains("The columns are: Kind"));
    }

    /// <summary>
    /// And a position outside the result is refused by the same rule that governs it everywhere
    /// else - <c>Docs/KnownIssues.md</c> 16.
    /// </summary>
    [Test]
    public void APositionOutsideTheResultIsRefusedTest()
    {
        Assert.That(
            () => m_engine.Query($"{LEFT} UNION ALL {RIGHT} ORDER BY 2"),
            Throws.InvalidOperationException.With.Message.Contains("position 2"));
    }

    #endregion

    #region The other set operations

    /// <summary>
    /// <c>UNION</c>, <c>INTERSECT</c> and <c>EXCEPT</c> take the same path, and the ordering is the
    /// union's in each. <c>e</c> comes from the second table, so it is the row that says the sort saw
    /// past the first arm.
    /// </summary>
    [Test]
    public void EverySetOperationOrdersItsCombinedResultTest()
    {
        m_engine.Execute("CREATE TABLE T2 (Id BIGINT PRIMARY KEY AUTOINCREMENT, Kind VARCHAR(20))");

        foreach (var kind in new[] { "e", "b" })
            m_engine.Execute($"INSERT INTO T2 (Kind) VALUES ('{kind}')");

        Assert.That(
            Answer("SELECT Kind FROM T WHERE Amount > 25 UNION SELECT Kind FROM T2 ORDER BY Kind"),
            Is.EqualTo("Text:b|Text:c|Text:d|Text:e"));

        Assert.That(
            Answer("SELECT Kind FROM T INTERSECT SELECT Kind FROM T2 ORDER BY Kind"),
            Is.EqualTo("Text:b"));

        Assert.That(
            Answer("SELECT Kind FROM T EXCEPT SELECT Kind FROM T2 ORDER BY Kind"),
            Is.EqualTo("Text:a|Text:c|Text:d"));
    }

    #endregion

    #region The workaround that is no longer needed

    /// <summary>
    /// Wrapping the union in a derived table was what `WitSQL.md` recommended while this was open.
    /// It still works, and it keeps its case: the reference told people to write it, so it must not
    /// stop working quietly.
    /// </summary>
    [Test]
    public void TheDerivedTableFormStillWorksTest()
    {
        Assert.That(
            Answer($"SELECT U.Kind FROM ({LEFT} UNION ALL {RIGHT}) AS U ORDER BY U.Kind"),
            Is.EqualTo("Text:a|Text:b|Text:c|Text:d"));

        Assert.That(
            Answer($"SELECT U.Kind FROM ({LEFT} UNION ALL {RIGHT}) AS U ORDER BY U.Kind LIMIT 1"),
            Is.EqualTo("Text:a"));
    }

    #endregion
}
