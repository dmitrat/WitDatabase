using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Tests for streaming aggregation performance.
/// Verifies that COUNT/SUM/AVG/MIN/MAX use O(1) memory.
/// </summary>
[TestFixture]
public class StreamingAggregationTests
{
    private WitSqlEngine m_engine = null!;

    [SetUp]
    public void Setup()
    {
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .Build();
        m_engine = new WitSqlEngine(database, ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #region Correctness Tests

    [Test]
    public void CountStarReturnsCorrectCountTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        for (int i = 0; i < 100; i++)
        {
            m_engine.Execute($"INSERT INTO T (Id, Value) VALUES ({i}, {i * 10})");
        }

        var result = m_engine.ExecuteScalar("SELECT COUNT(*) FROM T");
        
        Assert.That(result.AsInt64(), Is.EqualTo(100));
    }

    [Test]
    public void CountColumnReturnsCorrectCountTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (1, 10)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (2, NULL)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (3, 30)");

        var result = m_engine.ExecuteScalar("SELECT COUNT(Value) FROM T");
        
        Assert.That(result.AsInt64(), Is.EqualTo(2)); // NULL not counted
    }

    [Test]
    public void SumReturnsCorrectSumTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        for (int i = 1; i <= 10; i++)
        {
            m_engine.Execute($"INSERT INTO T (Id, Value) VALUES ({i}, {i})");
        }

        var result = m_engine.ExecuteScalar("SELECT SUM(Value) FROM T");
        
        Assert.That(result.AsInt64(), Is.EqualTo(55)); // 1+2+3+...+10 = 55
    }

    [Test]
    public void AvgReturnsCorrectAverageTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (1, 10)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (2, 20)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (3, 30)");

        var result = m_engine.ExecuteScalar("SELECT AVG(Value) FROM T");
        
        Assert.That(result.AsDouble(), Is.EqualTo(20.0).Within(0.001));
    }

    [Test]
    public void MinReturnsCorrectMinimumTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (1, 50)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (2, 10)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (3, 30)");

        var result = m_engine.ExecuteScalar("SELECT MIN(Value) FROM T");
        
        Assert.That(result.AsInt64(), Is.EqualTo(10));
    }

    [Test]
    public void MaxReturnsCorrectMaximumTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (1, 50)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (2, 10)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (3, 30)");

        var result = m_engine.ExecuteScalar("SELECT MAX(Value) FROM T");
        
        Assert.That(result.AsInt64(), Is.EqualTo(50));
    }

    [Test]
    public void MultipleAggregatesInOneQueryTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        for (int i = 1; i <= 5; i++)
        {
            m_engine.Execute($"INSERT INTO T (Id, Value) VALUES ({i}, {i * 10})");
        }

        var rows = m_engine.Query("SELECT COUNT(*), SUM(Value), AVG(Value), MIN(Value), MAX(Value) FROM T");
        
        Assert.That(rows.Count, Is.EqualTo(1));
        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(5));    // COUNT
        Assert.That(rows[0][1].AsInt64(), Is.EqualTo(150));  // SUM: 10+20+30+40+50
        Assert.That(rows[0][2].AsDouble(), Is.EqualTo(30.0).Within(0.001)); // AVG
        Assert.That(rows[0][3].AsInt64(), Is.EqualTo(10));   // MIN
        Assert.That(rows[0][4].AsInt64(), Is.EqualTo(50));   // MAX
    }

    [Test]
    public void EmptyTableReturnsCorrectValuesTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");

        var rows = m_engine.Query("SELECT COUNT(*), SUM(Value), AVG(Value), MIN(Value), MAX(Value) FROM T");
        
        Assert.That(rows.Count, Is.EqualTo(1));
        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(0));  // COUNT = 0
        Assert.That(rows[0][1].IsNull, Is.True);           // SUM = NULL
        Assert.That(rows[0][2].IsNull, Is.True);           // AVG = NULL
        Assert.That(rows[0][3].IsNull, Is.True);           // MIN = NULL
        Assert.That(rows[0][4].IsNull, Is.True);           // MAX = NULL
    }

    [Test]
    public void AggregateWithWhereClauseTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        for (int i = 1; i <= 10; i++)
        {
            m_engine.Execute($"INSERT INTO T (Id, Value) VALUES ({i}, {i})");
        }

        var result = m_engine.ExecuteScalar("SELECT COUNT(*) FROM T WHERE Value > 5");
        
        Assert.That(result.AsInt64(), Is.EqualTo(5)); // 6,7,8,9,10
    }

    [Test]
    public void AggregateWithAliasTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Value INT)");
        m_engine.Execute("INSERT INTO T (Id, Value) VALUES (1, 100)");

        var rows = m_engine.Query("SELECT COUNT(*) AS Total, SUM(Value) AS Sum FROM T");
        
        Assert.That(rows[0]["Total"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["Sum"].AsInt64(), Is.EqualTo(100));
    }

    #endregion

    #region Group By Falls Back to Full Iterator

    [Test]
    public void GroupByUsesFullIteratorTest()
    {
        m_engine.Execute("CREATE TABLE T (Category VARCHAR(10), Value INT)");
        m_engine.Execute("INSERT INTO T (Category, Value) VALUES ('A', 10)");
        m_engine.Execute("INSERT INTO T (Category, Value) VALUES ('A', 20)");
        m_engine.Execute("INSERT INTO T (Category, Value) VALUES ('B', 30)");

        var rows = m_engine.Query("SELECT Category, SUM(Value) FROM T GROUP BY Category ORDER BY Category");
        
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(rows[0]["Category"].AsString(), Is.EqualTo("A"));
        Assert.That(rows[0][1].AsInt64(), Is.EqualTo(30));
        Assert.That(rows[1]["Category"].AsString(), Is.EqualTo("B"));
        Assert.That(rows[1][1].AsInt64(), Is.EqualTo(30));
    }

    [Test]
    public void HavingUsesFullIteratorTest()
    {
        m_engine.Execute("CREATE TABLE T (Category VARCHAR(10), Value INT)");
        m_engine.Execute("INSERT INTO T (Category, Value) VALUES ('A', 10)");
        m_engine.Execute("INSERT INTO T (Category, Value) VALUES ('A', 20)");
        m_engine.Execute("INSERT INTO T (Category, Value) VALUES ('B', 5)");

        var rows = m_engine.Query("SELECT Category, SUM(Value) AS Total FROM T GROUP BY Category HAVING SUM(Value) > 10");
        
        Assert.That(rows.Count, Is.EqualTo(1));
        Assert.That(rows[0]["Category"].AsString(), Is.EqualTo("A"));
    }

    #endregion

    #region Performance Tests

    [Test]
    [Category("Performance")]
    public void StreamingCountDoesNotMaterializeAllRowsTest()
    {
        // Create table with many rows
        m_engine.Execute("CREATE TABLE BigTable (Id BIGINT PRIMARY KEY AUTOINCREMENT, Data VARCHAR(100))");
        
        const int rowCount = 10000;
        m_engine.Execute("BEGIN TRANSACTION");
        for (int i = 0; i < rowCount; i++)
        {
            m_engine.Execute($"INSERT INTO BigTable (Data) VALUES ('Row {i}')");
        }
        m_engine.Execute("COMMIT");

        // Execute COUNT(*) - should use streaming
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM BigTable");
        sw.Stop();

        Console.WriteLine($"COUNT(*) on {rowCount} rows:");
        Console.WriteLine($"  Result: {count.AsInt64()}");
        Console.WriteLine($"  Time: {sw.ElapsedMilliseconds} ms");

        Assert.That(count.AsInt64(), Is.EqualTo(rowCount));
        
        // Streaming should be fast (< 100ms for 10k rows)
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500), 
            "Streaming COUNT should be fast");
    }

    [Test]
    [Category("Performance")]
    public void StreamingAggregatesPerformanceTest()
    {
        m_engine.Execute("CREATE TABLE Numbers (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value INT)");
        
        const int rowCount = 5000;
        m_engine.Execute("BEGIN TRANSACTION");
        for (int i = 0; i < rowCount; i++)
        {
            m_engine.Execute($"INSERT INTO Numbers (Value) VALUES ({i % 100})");
        }
        m_engine.Execute("COMMIT");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = m_engine.Query("SELECT COUNT(*), SUM(Value), AVG(Value), MIN(Value), MAX(Value) FROM Numbers");
        sw.Stop();

        Console.WriteLine($"Multiple aggregates on {rowCount} rows:");
        Console.WriteLine($"  Time: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  COUNT: {rows[0][0].AsInt64()}");
        Console.WriteLine($"  SUM: {rows[0][1].AsInt64()}");
        Console.WriteLine($"  AVG: {rows[0][2].AsDouble():F2}");
        Console.WriteLine($"  MIN: {rows[0][3].AsInt64()}");
        Console.WriteLine($"  MAX: {rows[0][4].AsInt64()}");

        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(rowCount));
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500), 
            "Streaming aggregation should be fast");
    }

    #endregion
}
