using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Regression tests for <c>ALTER TABLE … DROP COLUMN</c> and the surviving column values.
/// </summary>
/// <remarks>
/// Rows are serialized by ordinal: <c>SerializeValuesArray</c> reads each value's type from
/// <c>Columns[i]</c> positionally. DROP COLUMN removed the value from the array but re-serialized
/// with the *pre*-drop definition, so every column after the dropped ordinal was written under its
/// neighbour's type. Dropping the middle column of <c>(Id INT, Name VARCHAR, Age INT)</c> turned
/// <c>Age = 42</c> into <c>2</c> - silently, on data that had been correct a moment earlier.
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineDropColumnTests : WitSqlEngineTestsBase
{
    #region Value Preservation

    [Test]
    public void DroppingAMiddleColumnPreservesTheTrailingValuesTest()
    {
        m_engine.Execute(
            "CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(20) NOT NULL, Age INT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, Name, Age) VALUES (1, 'x', 42)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN Name");

        Assert.That(Scalar("SELECT Age FROM T WHERE Id = 1").AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void DroppingAMiddleColumnPreservesEveryRowTest()
    {
        m_engine.Execute(
            "CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(20) NOT NULL, Age INT NOT NULL)");
        for (var i = 1; i <= 5; i++)
            m_engine.Execute($"INSERT INTO T (Id, Name, Age) VALUES ({i}, 'n{i}', {i * 11})");

        m_engine.Execute("ALTER TABLE T DROP COLUMN Name");

        var ages = m_engine.Query("SELECT Age FROM T ORDER BY Id")
            .Select(row => row[0].AsInt64()).ToArray();

        Assert.That(ages, Is.EqualTo(new long[] { 11, 22, 33, 44, 55 }));
    }

    [Test]
    public void DroppingAColumnPreservesMixedTrailingTypesTest()
    {
        m_engine.Execute(@"
            CREATE TABLE T (
                Id INT PRIMARY KEY,
                Doomed VARCHAR(20) NOT NULL,
                Amount DECIMAL(18,4) NOT NULL,
                Flag BOOLEAN NOT NULL,
                Note VARCHAR(30) NOT NULL
            )");
        m_engine.Execute(
            "INSERT INTO T (Id, Doomed, Amount, Flag, Note) VALUES (1, 'gone', 1234.5678, TRUE, 'kept')");

        m_engine.Execute("ALTER TABLE T DROP COLUMN Doomed");

        Assert.Multiple(() =>
        {
            Assert.That(Scalar("SELECT Amount FROM T WHERE Id = 1").AsDecimal(), Is.EqualTo(1234.5678m));
            Assert.That(Scalar("SELECT Flag FROM T WHERE Id = 1").AsBool(), Is.True);
            Assert.That(Scalar("SELECT Note FROM T WHERE Id = 1").AsString(), Is.EqualTo("kept"));
        });
    }

    [Test]
    public void DroppingTheLastColumnPreservesTheLeadingValuesTest()
    {
        m_engine.Execute(
            "CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(20) NOT NULL, Age INT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, Name, Age) VALUES (1, 'x', 42)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN Age");

        Assert.That(Scalar("SELECT Name FROM T WHERE Id = 1").AsString(), Is.EqualTo("x"));
    }

    [Test]
    public void ValuesSurviveAReopenAfterDropColumnTest()
    {
        m_engine.Execute(
            "CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(20) NOT NULL, Age INT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, Name, Age) VALUES (1, 'x', 42)");
        m_engine.Execute("ALTER TABLE T DROP COLUMN Name");

        // A second read path: the row must decode the same way through a fresh statement plan.
        m_engine.Execute("INSERT INTO T (Id, Age) VALUES (2, 7)");

        var ages = m_engine.Query("SELECT Age FROM T ORDER BY Id")
            .Select(row => row[0].AsInt64()).ToArray();

        Assert.That(ages, Is.EqualTo(new long[] { 42, 7 }),
            "Rows written before and after the drop must decode identically");
    }

    #endregion

    #region Schema Effects

    [Test]
    public void DroppedColumnIsNoLongerSelectableTest()
    {
        m_engine.Execute(
            "CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(20) NOT NULL, Age INT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, Name, Age) VALUES (1, 'x', 42)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN Name");

        Assert.That(() => m_engine.Query("SELECT Name FROM T"), Throws.Exception);
    }

    [Test]
    public void DroppingAnUnknownColumnIsANoOpTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Age INT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, Age) VALUES (1, 42)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN Missing");

        Assert.That(Scalar("SELECT Age FROM T WHERE Id = 1").AsInt64(), Is.EqualTo(42));
    }

    #endregion

    #region Helper Methods

    private WitSqlValue Scalar(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0]).First();
    }

    #endregion
}
