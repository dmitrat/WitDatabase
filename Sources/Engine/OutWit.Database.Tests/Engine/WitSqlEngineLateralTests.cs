namespace OutWit.Database.Tests;

/// <summary>
/// A correlated subquery in <c>FROM</c>: <c>LATERAL</c>, <c>CROSS APPLY</c>, <c>OUTER APPLY</c>.
/// </summary>
/// <remarks>
/// <para>
/// One capability with two spellings, and the dialect oracle is what showed that: read as three
/// separate items it looked like three one-dialect features, when in fact PostgreSQL writes
/// <c>LATERAL</c>, SQL Server writes <c>CROSS APPLY</c>, and <b>both targets have it</b>.
/// </para>
/// <para>
/// It was also priced as "real planner work" and measured to be much less. The engine already
/// evaluated a subquery per outer row in <c>EXISTS</c>, <c>IN</c> and a scalar position, through
/// <c>ContextExecution.OuterRow</c>; what was missing was reaching that from a table source.
/// </para>
/// </remarks>
[TestFixture]
[Category("Engine")]
public sealed class WitSqlEngineLateralTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(50))");
        m_engine.Execute("CREATE TABLE S (Id INT PRIMARY KEY, TId INT, Score INT)");

        m_engine.Execute("INSERT INTO T (Id, Name) VALUES (1, 'a')");
        m_engine.Execute("INSERT INTO T (Id, Name) VALUES (2, 'b')");
        m_engine.Execute("INSERT INTO T (Id, Name) VALUES (3, 'no scores')");

        m_engine.Execute("INSERT INTO S (Id, TId, Score) VALUES (1, 1, 100)");
        m_engine.Execute("INSERT INTO S (Id, TId, Score) VALUES (2, 1, 200)");
        m_engine.Execute("INSERT INTO S (Id, TId, Score) VALUES (3, 2, 300)");
    }

    #endregion

    #region The correlation itself

    [Test]
    public void LateralSeesTheRowBesideItTest()
    {
        var pairs = Pairs(@"
            SELECT T.Id, X.Score
            FROM T, LATERAL (SELECT Score FROM S WHERE S.TId = T.Id) AS X
            ORDER BY T.Id, X.Score");

        Assert.That(pairs, Is.EqualTo(new[] { (1L, 100L), (1L, 200L), (2L, 300L) }),
            "the subquery must be evaluated per outer row - a row with no match drops out, which is "
            + "the inner form");
    }

    [Test]
    public void CrossApplyIsTheSameThingSpeltDifferentlyTest()
    {
        var lateral = Pairs(@"
            SELECT T.Id, X.Score
            FROM T, LATERAL (SELECT Score FROM S WHERE S.TId = T.Id) AS X
            ORDER BY T.Id, X.Score");

        var apply = Pairs(@"
            SELECT T.Id, X.Score
            FROM T CROSS APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X
            ORDER BY T.Id, X.Score");

        Assert.That(apply, Is.EqualTo(lateral),
            "PostgreSQL's spelling and SQL Server's must answer identically - they are one capability");
    }

    #endregion

    #region Outer form

    [Test]
    public void OuterApplyKeepsARowWithNoMatchTest()
    {
        var rows = m_engine.Query(@"
            SELECT T.Id, X.Score
            FROM T OUTER APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X
            ORDER BY T.Id")
            .Select(row => (Id: row[0].AsInt64(), HasScore: !row[1].IsNull))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(4), "three matches plus the unmatched row");
            Assert.That(rows.Count(r => r.Id == 3 && !r.HasScore), Is.EqualTo(1),
                "the row with no scores is kept, with nulls - that is what makes it the outer form");
        });
    }

    #endregion

    #region Composition

    [Test]
    public void LateralTakesADerivedColumnListTest()
    {
        var row = m_engine.Query(@"
            SELECT X.Points
            FROM T CROSS APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X (Points)
            ORDER BY X.Points")[0];

        Assert.That(row[0].AsInt64(), Is.EqualTo(100));
    }

    [Test]
    public void LateralCanAggregateOverTheOuterRowTest()
    {
        var pairs = Pairs(@"
            SELECT T.Id, X.Best
            FROM T CROSS APPLY (SELECT MAX(Score) AS Best FROM S WHERE S.TId = T.Id) AS X
            ORDER BY T.Id");

        Assert.That(pairs.Take(2), Is.EqualTo(new[] { (1L, 200L), (2L, 300L) }),
            "the commonest reason to reach for this construct is a per-row aggregate");
    }

    [Test]
    public void LateralWithoutAnythingToCorrelateWithIsRefusedTest()
    {
        Assert.That(() => m_engine.Query("SELECT * FROM LATERAL (SELECT 1) AS X"),
            Throws.Exception,
            "LATERAL reads the row beside it, so there has to be one; refused with a reason rather "
            + "than resolving its outer columns against nothing");
    }

    #endregion

    #region Helpers

    private (long, long)[] Pairs(string sql) =>
        m_engine.Query(sql)
            .Select(row => (row[0].AsInt64(), row[1].AsInt64()))
            .ToArray();

    #endregion
}
