using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Regression tests for SQL's three-valued logic.
/// </summary>
/// <remarks>
/// Comparisons went straight to the <c>WitSqlValue</c> operators, which impose a total order so that
/// ORDER BY has somewhere to put NULL. Used as predicates that made <c>NULL &lt; 5</c>,
/// <c>NULL &lt;&gt; 5</c> and even <c>NULL = NULL</c> come out TRUE, so rows with a NULL column
/// leaked through ordinary WHERE filters - <c>Where(u =&gt; u.Age &lt; 18)</c> returned people with no
/// recorded age. The guard belongs in the evaluator, not in <c>CompareTo</c>, precisely so that
/// sorting keeps its total order.
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineNullLogicTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Age INT NULL, Name VARCHAR(20) NULL)");
        m_engine.Execute("INSERT INTO T (Id, Age, Name) VALUES (1, NULL, NULL)");
        m_engine.Execute("INSERT INTO T (Id, Age, Name) VALUES (2, 3, 'bob')");
        m_engine.Execute("INSERT INTO T (Id, Age, Name) VALUES (3, 30, 'ann')");
    }

    #endregion

    #region Comparisons Against NULL

    [Test]
    public void LessThanDoesNotMatchNullTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age < 5 ORDER BY Id"),
            Is.EqualTo(new long[] { 2 }));
    }

    [Test]
    public void GreaterThanDoesNotMatchNullTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age > 5 ORDER BY Id"),
            Is.EqualTo(new long[] { 3 }));
    }

    [Test]
    public void NotEqualDoesNotMatchNullTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age <> 5 ORDER BY Id"),
            Is.EqualTo(new long[] { 2, 3 }));
    }

    [Test]
    public void EqualDoesNotMatchNullTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age = 3 ORDER BY Id"),
            Is.EqualTo(new long[] { 2 }));
    }

    [Test]
    public void NullEqualsNullIsUnknownTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age = NULL"), Is.Empty);
    }

    [Test]
    public void NullNotEqualsNullIsUnknownTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age <> NULL"), Is.Empty);
    }

    [Test]
    public void StringComparisonDoesNotMatchNullTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Name <> 'zzz' ORDER BY Id"),
            Is.EqualTo(new long[] { 2, 3 }));
    }

    [Test]
    public void NullLeaksThroughAnIndexedComparisonTooTest()
    {
        m_engine.Execute("CREATE INDEX IX_T_Age ON T (Age)");

        Assert.That(SelectIds("SELECT Id FROM T WHERE Age < 5 ORDER BY Id"),
            Is.EqualTo(new long[] { 2 }),
            "An index seek must agree with the filter about NULL");
    }

    #endregion

    #region IS NULL Still Works

    [Test]
    public void IsNullMatchesTheNullRowTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age IS NULL"), Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public void IsNotNullMatchesTheOthersTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age IS NOT NULL ORDER BY Id"),
            Is.EqualTo(new long[] { 2, 3 }));
    }

    #endregion

    #region AND / OR Truth Tables

    [Test]
    public void FalseDominatesAndEvenAgainstNullTest()
    {
        // NULL AND FALSE is FALSE, so the row must not match - and neither must anything else.
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age = 999 AND Id = 1"), Is.Empty);
    }

    [Test]
    public void TrueDominatesOrEvenAgainstNullTest()
    {
        // NULL OR TRUE is TRUE, so the NULL row must still match on the second disjunct.
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age > 5 OR Id = 1 ORDER BY Id"),
            Is.EqualTo(new long[] { 1, 3 }));
    }

    [Test]
    public void NullOrFalseIsUnknownTest()
    {
        Assert.That(SelectIds("SELECT Id FROM T WHERE Age > 5 OR Id = 999 ORDER BY Id"),
            Is.EqualTo(new long[] { 3 }));
    }

    [Test]
    public void NotOfUnknownStaysUnknownTest()
    {
        // NOT (NULL < 5) is NOT UNKNOWN = UNKNOWN, so row 1 must not appear.
        Assert.That(SelectIds("SELECT Id FROM T WHERE NOT Age < 5 ORDER BY Id"),
            Is.EqualTo(new long[] { 3 }));
    }

    #endregion

    #region Ordering Keeps A Total Order

    [Test]
    public void OrderByStillPlacesNullDeterministicallyTest()
    {
        var ids = SelectIds("SELECT Id FROM T ORDER BY Age");

        Assert.That(ids, Has.Length.EqualTo(3),
            "Guarding comparisons must not remove NULL from the sort order");
        Assert.That(ids, Does.Contain(1L));
    }

    [Test]
    public void AggregatesStillIgnoreNullTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Scalar("SELECT COUNT(Age) FROM T").AsInt64(), Is.EqualTo(2));
            Assert.That(Scalar("SELECT COUNT(*) FROM T").AsInt64(), Is.EqualTo(3));
            Assert.That(Scalar("SELECT SUM(Age) FROM T").AsInt64(), Is.EqualTo(33));
        });
    }

    #endregion

    #region Helper Methods

    private long[] SelectIds(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0].AsInt64()).ToArray();
    }

    private WitSqlValue Scalar(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0]).First();
    }

    #endregion
}
