namespace OutWit.Database.Tests;

/// <summary>
/// <c>BETWEEN</c> in every position the boolean-layer split re-pointed, executed rather than parsed.
/// </summary>
/// <remarks>
/// <para>
/// The audit's §4.2 lists three shapes as unexecuted and implies they are broken: <c>NOT BETWEEN</c>
/// with a trailing <c>OR</c>, <c>BETWEEN</c> inside a <c>CASE</c>, and <c>BETWEEN</c> with subquery
/// bounds. <b>Measured against SQLite before anything was built, all three already agreed.</b> They
/// are not defects; each bounds the interior reference by other means — <c>OR</c> is a different
/// operator, <c>THEN</c> terminates the <c>WHEN</c>, and parentheses close the subquery.
/// </para>
/// <para>
/// So this fixture is <b>pins, not fixes</b>. Its job is to hold the shapes that already worked, and
/// — more usefully — the shapes that combine them with the one that did not: a <c>BETWEEN</c>
/// followed by <c>AND</c>, in each of the positions that used to reference the flat
/// <c>expression</c> rule.
/// </para>
/// <para>
/// Every expected value here is SQLite's answer for the same data, taken from the oracle rather than
/// reasoned out.
/// </para>
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineBetweenShapesTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute(@"
            CREATE TABLE People (
                Id INT PRIMARY KEY,
                Name VARCHAR(50) NOT NULL,
                Age INT NOT NULL,
                Flag INT NOT NULL
            )");

        Insert(1, "alice", 30, 1);
        Insert(2, "bob", 10, 1);
        Insert(3, "anna", 40, 0);
    }

    #endregion

    #region The three shapes the audit listed as unexecuted

    [Test]
    public void NotBetweenWithATrailingOrTest()
    {
        // Already correct before the split: OR is a different operator, so the upper bound was never
        // at risk of absorbing it.
        var ids = SelectIds(
            "SELECT Id FROM People WHERE Age NOT BETWEEN 18 AND 65 OR Flag = 1 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2 }),
            "bob is outside 18..65; alice has Flag = 1; anna satisfies neither");
    }

    [Test]
    public void BetweenInsideACaseTest()
    {
        // Already correct before the split: THEN terminates the WHEN, bounding the reference.
        var flags = SelectInts(
            "SELECT CASE WHEN Age BETWEEN 1 AND 35 THEN 1 ELSE 0 END FROM People ORDER BY Id");

        Assert.That(flags, Is.EqualTo(new long[] { 1, 1, 0 }));
    }

    [Test]
    public void BetweenWithSubqueryBoundsTest()
    {
        // Already correct before the split: the parentheses close the subquery.
        var ids = SelectIds(@"
            SELECT Id FROM People
            WHERE Age BETWEEN (SELECT MIN(Age) FROM People) AND (SELECT MAX(Age) FROM People)
            ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2, 3 }),
            "the bounds are the table's own extremes, so every row qualifies");
    }

    #endregion

    #region The same shapes combined with the one that WAS broken

    [Test]
    public void BetweenWithASubqueryBoundFollowedByAndTest()
    {
        var ids = SelectIds(@"
            SELECT Id FROM People
            WHERE Age BETWEEN (SELECT MIN(Age) FROM People) AND 35 AND Flag = 1
            ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2 }),
            "10..35 covers alice and bob, and both carry Flag = 1; anna is 40");
    }

    [Test]
    public void BetweenInsideACaseFollowedByAndTest()
    {
        var flags = SelectInts(@"
            SELECT CASE WHEN Age BETWEEN 1 AND 35 AND Flag = 1 THEN 1 ELSE 0 END
            FROM People ORDER BY Id");

        Assert.That(flags, Is.EqualTo(new long[] { 1, 1, 0 }));
    }

    [Test]
    public void TwoBetweensConjoinedTest()
    {
        var ids = SelectIds(
            "SELECT Id FROM People WHERE Age BETWEEN 1 AND 35 AND Flag BETWEEN 1 AND 2 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2 }),
            "the first BETWEEN must not absorb the second one's operands");
    }

    [Test]
    public void BetweenFollowedByAndInsideNotTest()
    {
        var ids = SelectIds(
            "SELECT Id FROM People WHERE NOT (Age BETWEEN 1 AND 35 AND Flag = 1) ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 3 }),
            "only anna fails the conjunction, so only anna survives its negation");
    }

    #endregion

    #region Positions other than WHERE

    [Test]
    [Ignore("Blocked by a PRE-EXISTING defect unrelated to the grammar, and confirmed pre-existing by " +
            "execution at parent commit 39d22e4: an aggregate inside BETWEEN or IN in a HAVING clause " +
            "raises InvalidOperationException 'COUNT(*) should be handled by aggregation iterator'. " +
            "The aggregation iterator only collects aggregates from comparison operands. Kept here " +
            "rather than deleted, so the HAVING position stays represented and this turns green when " +
            "the aggregation defect is fixed. See HavingAggregateFindingsTests.")]
    public void BetweenFollowedByAndInAHavingClauseTest()
    {
        var flags = SelectInts(@"
            SELECT Flag FROM People
            GROUP BY Flag
            HAVING COUNT(*) BETWEEN 1 AND 5 AND Flag = 1");

        Assert.That(flags, Is.EqualTo(new long[] { 1 }),
            "both groups have a count in 1..5, so the Flag = 1 conjunct must still be applied");
    }

    [Test]
    public void BetweenFollowedByAndInAJoinOnClauseTest()
    {
        m_engine.Execute("CREATE TABLE Scores (PersonId INT PRIMARY KEY, Points INT NOT NULL)");
        m_engine.Execute("INSERT INTO Scores (PersonId, Points) VALUES (1, 50)");
        m_engine.Execute("INSERT INTO Scores (PersonId, Points) VALUES (2, 50)");
        m_engine.Execute("INSERT INTO Scores (PersonId, Points) VALUES (3, 50)");

        var ids = SelectIds(@"
            SELECT People.Id FROM People
            INNER JOIN Scores ON People.Id = Scores.PersonId
                AND People.Age BETWEEN 1 AND 35 AND People.Flag = 1
            ORDER BY People.Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2 }),
            "the ON clause is a full search condition, so the trailing conjunct must survive");
    }

    [Test]
    public void BetweenFollowedByAndInAPartialIndexFilterTest()
    {
        // The filter is a search condition too. This asserts the index is usable rather than
        // inspecting the stored text, so it fails if the predicate was mangled either way.
        m_engine.Execute(@"
            CREATE INDEX IX_Young ON People (Name)
            WHERE Age BETWEEN 1 AND 35 AND Flag = 1");

        var ids = SelectIds("SELECT Id FROM People WHERE Age BETWEEN 1 AND 35 AND Flag = 1 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public void BetweenFollowedByAndInACheckConstraintTest()
    {
        // This exact shape was one of the seven entries the ambiguity harness flagged on the old
        // grammar, and it is a DDL position rather than a query one.
        m_engine.Execute(@"
            CREATE TABLE Bounded (
                Id INT PRIMARY KEY,
                Age INT,
                CHECK (Age BETWEEN 0 AND 150 AND Id > 0)
            )");

        m_engine.Execute("INSERT INTO Bounded (Id, Age) VALUES (1, 30)");

        Assert.Multiple(() =>
        {
            Assert.That(
                () => m_engine.Execute("INSERT INTO Bounded (Id, Age) VALUES (2, 200)"),
                Throws.Exception,
                "Age 200 is outside 0..150, so the first conjunct must reject it");

            Assert.That(
                SelectIds("SELECT Id FROM Bounded ORDER BY Id"), Is.EqualTo(new long[] { 1 }),
                "only the valid row may have been written");
        });
    }

    #endregion

    #region Update and delete

    [Test]
    public void BetweenFollowedByAndInAnUpdateTest()
    {
        m_engine.Execute("UPDATE People SET Name = 'changed' WHERE Age BETWEEN 1 AND 35 AND Flag = 1");

        var changed = SelectIds("SELECT Id FROM People WHERE Name = 'changed' ORDER BY Id");

        Assert.That(changed, Is.EqualTo(new long[] { 1, 2 }),
            "anna is outside the range, so her row must be untouched");
    }

    #endregion

    #region Helper Methods

    private void Insert(int id, string name, int age, int flag)
    {
        m_engine.Execute(
            $"INSERT INTO People (Id, Name, Age, Flag) VALUES ({id}, '{name}', {age}, {flag})");
    }

    private long[] SelectIds(string sql) => SelectInts(sql);

    private long[] SelectInts(string sql) =>
        m_engine.Query(sql).Select(row => row[0].AsInt64()).ToArray();

    #endregion
}
