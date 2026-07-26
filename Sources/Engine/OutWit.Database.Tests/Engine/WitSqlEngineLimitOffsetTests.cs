using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Regression tests for LIMIT/OFFSET, including the shape EF Core's <c>Skip(n)</c> produces.
/// </summary>
/// <remarks>
/// <c>IteratorLimit</c> compared <c>m_returned &gt;= m_limit</c>, so a negative limit - the SQLite
/// convention for "unbounded", and what a relational provider emits for an OFFSET with no LIMIT -
/// made <c>0 &gt;= -1</c> true and returned zero rows. The grammar also had no OFFSET-without-LIMIT
/// form, and the planner dropped the offset entirely whenever the count was absent.
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineLimitOffsetTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT NOT NULL)");
        for (var i = 1; i <= 5; i++)
            m_engine.Execute($"INSERT INTO T (Id, V) VALUES ({i}, {i * 10})");
    }

    #endregion

    #region Negative Limit

    [Test]
    public void NegativeLimitReturnsEveryRowTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id LIMIT -1"),
            Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void NegativeLimitWithOffsetReturnsTheRemainderTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id LIMIT -1 OFFSET 2"),
            Is.EqualTo(new long[] { 3, 4, 5 }));
    }

    #endregion

    #region Offset Without Limit

    [Test]
    public void OffsetWithoutLimitParsesAndSkipsTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id OFFSET 2"),
            Is.EqualTo(new long[] { 3, 4, 5 }));
    }

    [Test]
    public void OffsetBeyondTheRowCountReturnsNothingTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id OFFSET 99"), Is.Empty);
    }

    [Test]
    public void OffsetZeroReturnsEveryRowTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id OFFSET 0"),
            Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
    }

    #endregion

    #region Ordinary Shapes

    [Test]
    public void LimitBoundsTheResultTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id LIMIT 2"),
            Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public void LimitWithOffsetPaginatesTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id LIMIT 2 OFFSET 1"),
            Is.EqualTo(new long[] { 2, 3 }));
    }

    [Test]
    public void LimitZeroReturnsNothingTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id LIMIT 0"), Is.Empty,
            "Zero must stay a real bound - only a negative limit means unbounded");
    }

    [Test]
    public void LimitLargerThanTheRowCountReturnsEveryRowTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id LIMIT 100"),
            Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
    }

    #endregion

    #region Helper Methods

    private long[] SelectIds(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0].AsInt64()).ToArray();
    }

    #endregion
}
