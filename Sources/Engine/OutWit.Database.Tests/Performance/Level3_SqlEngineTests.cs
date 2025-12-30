using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using System.Diagnostics;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Tests for Level 3: SQL Engine performance.
/// Focuses on SQL parsing, execution planning, and row operations.
/// </summary>
[TestFixture]
public class Level3_SqlEngineTests
{
    #region Fields

    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        var database = WitDatabase.CreateInMemory();
        m_engine = new WitSqlEngine(database, ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region SQL Parsing Tests

    /// <summary>
    /// Measure pure SQL parsing time without execution.
    /// </summary>
    [Test]
    public void SqlParsingOverheadTest()
    {
        var sqls = new[]
        {
            "SELECT * FROM T",
            "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
            "UPDATE T SET Name = @name WHERE Id = @id",
            "DELETE FROM T WHERE Id = @id",
            "SELECT COUNT(*) FROM T WHERE Value > @min",
        };

        var counts = new[] { 1000, 5000, 10000 };

        TestContext.Out.WriteLine("=== SQL Parsing Overhead ===");

        foreach (var sql in sqls)
        {
            TestContext.Out.WriteLine($"\n  SQL: {sql}");
            
            foreach (var count in counts)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                {
                    Parser.WitSql.Parse(sql);
                }
                sw.Stop();

                TestContext.Out.WriteLine($"    {count,5}x: {sw.Elapsed.TotalMilliseconds,8:F2} ms ({sw.Elapsed.TotalMilliseconds / count:F4} ms/parse)");
            }
        }
    }

    /// <summary>
    /// Compare prepared statement vs direct execution.
    /// </summary>
    [Test]
    public void PreparedStatementVsDirectTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Name VARCHAR(100), Value DOUBLE)");

        const int rowCount = 1000;

        // Direct execution (parses each time)
        var swDirect = Stopwatch.StartNew();
        for (int i = 0; i < rowCount; i++)
        {
            m_engine.Execute(
                "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                new Dictionary<string, object?>
                {
                    { "@id", i },
                    { "@name", $"Name{i}" },
                    { "@value", i * 1.5 }
                });
        }
        swDirect.Stop();

        m_engine.Execute("DELETE FROM T");

        // Prepared statement (parses once)
        var swPrepared = Stopwatch.StartNew();
        using (var stmt = m_engine.Prepare("INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)"))
        {
            for (int i = 0; i < rowCount; i++)
            {
                stmt.SetParameter("@id", i)
                    .SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        }
        swPrepared.Stop();

        TestContext.Out.WriteLine($"=== Prepared Statement vs Direct ({rowCount} inserts) ===");
        TestContext.Out.WriteLine($"  Direct: {swDirect.Elapsed.TotalMilliseconds:F2} ms ({swDirect.Elapsed.TotalMilliseconds / rowCount:F4} ms/insert)");
        TestContext.Out.WriteLine($"  Prepared: {swPrepared.Elapsed.TotalMilliseconds:F2} ms ({swPrepared.Elapsed.TotalMilliseconds / rowCount:F4} ms/insert)");
        TestContext.Out.WriteLine($"  Speedup: {swDirect.Elapsed.TotalMilliseconds / swPrepared.Elapsed.TotalMilliseconds:F2}x");
    }

    #endregion

    #region Full Pipeline Tests

    /// <summary>
    /// Measure full INSERT pipeline: parse + plan + execute + serialize.
    /// </summary>
    [Test]
    public void FullInsertPipelineTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Name VARCHAR(100), Value DOUBLE)");

        var counts = new[] { 100, 500, 1000, 2000 };
        var times = new List<(int Count, double Ms)>();

        foreach (var count in counts)
        {
            m_engine.Execute("DELETE FROM T");

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                m_engine.Execute(
                    "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
            sw.Stop();

            times.Add((count, sw.Elapsed.TotalMilliseconds));
        }

        TestContext.Out.WriteLine("=== Full INSERT Pipeline (no constraints) ===");
        foreach (var (count, ms) in times)
        {
            TestContext.Out.WriteLine($"  {count,4} rows: {ms,8:F2} ms ({ms / count:F4} ms/row)");
        }

        var ratio = times[3].Ms / times[0].Ms;
        TestContext.Out.WriteLine($"  Scaling ratio (2000/100): {ratio:F2}x (expected 20x for linear)");
    }

    /// <summary>
    /// Measure INSERT with different batch sizes.
    /// </summary>
    [Test]
    public void InsertBatchSizesTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Name VARCHAR(100), Value DOUBLE)");

        const int totalRows = 1000;
        var batchSizes = new[] { 1, 10, 50, 100, 250 };
        var times = new List<(int BatchSize, double Ms)>();

        foreach (var batchSize in batchSizes)
        {
            m_engine.Execute("DELETE FROM T");

            var sw = Stopwatch.StartNew();
            var rowsInserted = 0;
            
            while (rowsInserted < totalRows)
            {
                var currentBatch = Math.Min(batchSize, totalRows - rowsInserted);
                
                for (int i = 0; i < currentBatch; i++)
                {
                    var id = rowsInserted + i;
                    m_engine.Execute(
                        "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                        new Dictionary<string, object?>
                        {
                            { "@id", id },
                            { "@name", $"Name{id}" },
                            { "@value", id * 1.5 }
                        });
                }
                
                rowsInserted += currentBatch;
            }
            sw.Stop();
            
            times.Add((batchSize, sw.Elapsed.TotalMilliseconds));
        }

        TestContext.Out.WriteLine($"=== INSERT Batch Sizes ({totalRows} total rows) ===");
        foreach (var (batchSize, ms) in times)
        {
            TestContext.Out.WriteLine($"  Batch {batchSize,3}: {ms,8:F2} ms ({ms / totalRows:F4} ms/row)");
        }
    }

    #endregion

    #region Query Execution Tests

    /// <summary>
    /// Measure SELECT performance.
    /// </summary>
    [Test]
    public void SelectPerformanceTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Name VARCHAR(100), Value DOUBLE)");

        // Populate
        for (int i = 0; i < 1000; i++)
        {
            m_engine.Execute(
                "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                new Dictionary<string, object?>
                {
                    { "@id", i },
                    { "@name", $"Name{i}" },
                    { "@value", i * 1.5 }
                });
        }

        // Full scan
        var swScan = Stopwatch.StartNew();
        var rows = m_engine.Query("SELECT * FROM T");
        swScan.Stop();

        // Point query
        var swPoint = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            m_engine.Query($"SELECT * FROM T WHERE Id = {i}");
        }
        swPoint.Stop();

        // Aggregation
        var swAgg = Stopwatch.StartNew();
        m_engine.ExecuteScalar("SELECT COUNT(*) FROM T");
        m_engine.ExecuteScalar("SELECT SUM(Value) FROM T");
        m_engine.ExecuteScalar("SELECT AVG(Value) FROM T");
        swAgg.Stop();

        TestContext.Out.WriteLine("=== SELECT Performance (1000 rows) ===");
        TestContext.Out.WriteLine($"  Full scan: {swScan.Elapsed.TotalMilliseconds:F2} ms ({rows.Count} rows)");
        TestContext.Out.WriteLine($"  Point query 100x: {swPoint.Elapsed.TotalMilliseconds:F2} ms ({swPoint.Elapsed.TotalMilliseconds / 100:F4} ms/query)");
        TestContext.Out.WriteLine($"  Aggregations (COUNT+SUM+AVG): {swAgg.Elapsed.TotalMilliseconds:F2} ms");
    }

    #endregion

    #region Memory Tests

    /// <summary>
    /// Measure memory allocation during INSERT operations.
    /// </summary>
    [Test]
    public void InsertMemoryAllocationTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Name VARCHAR(100), Value DOUBLE)");

        const int rowCount = 500;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);

        for (int i = 0; i < rowCount; i++)
        {
            m_engine.Execute(
                "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                new Dictionary<string, object?>
                {
                    { "@id", i },
                    { "@name", $"Name{i}" },
                    { "@value", i * 1.5 }
                });
        }

        var after = GC.GetTotalAllocatedBytes(precise: true);
        var allocated = after - before;

        TestContext.Out.WriteLine($"=== Memory Allocation ({rowCount} inserts) ===");
        TestContext.Out.WriteLine($"  Total allocated: {allocated / 1024.0:F2} KB");
        TestContext.Out.WriteLine($"  Per insert: {allocated / rowCount / 1024.0:F2} KB ({allocated / rowCount:F0} bytes)");
    }

    #endregion
}
