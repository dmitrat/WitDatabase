using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Tests to investigate constraint validation performance.
/// These tests are designed to identify O(n²) behavior in UNIQUE constraint checking.
/// </summary>
[TestFixture]
public class Level3_ConstraintValidationTests
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

    #region Baseline Tests (No Constraints)

    /// <summary>
    /// Baseline: INSERT into table WITHOUT any constraints.
    /// This should be fast - no validation overhead.
    /// </summary>
    [Test]
    public void InsertNoConstraintsBaselineTest()
    {
        // Table without PK, UNIQUE, FK, CHECK - no validation needed
        m_engine.Execute(@"
            CREATE TABLE NoConstraints (
                Id INT,
                Name VARCHAR(100),
                Value DOUBLE
            )");

        var counts = new[] { 100, 500, 1000, 2000 };
        var times = new List<(int Count, double Ms)>();

        foreach (var count in counts)
        {
            m_engine.Execute("DELETE FROM NoConstraints");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                m_engine.Execute(
                    "INSERT INTO NoConstraints (Id, Name, Value) VALUES (@id, @name, @value)",
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

        TestContext.Out.WriteLine("=== INSERT No Constraints (Baseline) ===");
        foreach (var (count, ms) in times)
        {
            TestContext.Out.WriteLine($"  {count,5} rows: {ms,8:F2} ms ({ms / count:F4} ms/row)");
        }

        // Verify linear growth (not quadratic)
        // Time for 2000 should be roughly 4x time for 500 (2x scale = 4x time for O(n), 16x for O(n²))
        var ratio = times[3].Ms / times[1].Ms;
        TestContext.Out.WriteLine($"  Scaling ratio (2000/500): {ratio:F2}x (linear=4x, quadratic=16x)");
        
        Assert.That(ratio, Is.LessThan(8), "INSERT without constraints should scale linearly");
    }

    #endregion

    #region AUTOINCREMENT Tests

    /// <summary>
    /// INSERT with AUTOINCREMENT PK - UNIQUE check should be SKIPPED.
    /// This should be almost as fast as no constraints.
    /// </summary>
    [Test]
    public void InsertAutoIncrementPkTest()
    {
        m_engine.Execute(@"
            CREATE TABLE AutoPk (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Value DOUBLE
            )");

        var counts = new[] { 100, 500, 1000, 2000 };
        var times = new List<(int Count, double Ms)>();

        foreach (var count in counts)
        {
            // Recreate table to reset auto-increment
            m_engine.Execute("DROP TABLE AutoPk");
            m_engine.Execute(@"
                CREATE TABLE AutoPk (
                    Id BIGINT PRIMARY KEY AUTOINCREMENT,
                    Name VARCHAR(100),
                    Value DOUBLE
                )");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                // Don't specify Id - let AUTOINCREMENT generate it
                m_engine.Execute(
                    "INSERT INTO AutoPk (Name, Value) VALUES (@name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
            sw.Stop();
            
            times.Add((count, sw.Elapsed.TotalMilliseconds));
        }

        TestContext.Out.WriteLine("=== INSERT with AUTOINCREMENT PK ===");
        foreach (var (count, ms) in times)
        {
            TestContext.Out.WriteLine($"  {count,5} rows: {ms,8:F2} ms ({ms / count:F4} ms/row)");
        }

        var ratio = times[3].Ms / times[1].Ms;
        TestContext.Out.WriteLine($"  Scaling ratio (2000/500): {ratio:F2}x (linear=4x, quadratic=16x)");
        
        Assert.That(ratio, Is.LessThan(8), "INSERT with AUTOINCREMENT should scale linearly");
    }

    #endregion

    #region Explicit PK Tests (UNIQUE Check Required)

    /// <summary>
    /// INSERT with explicit PK value WITHOUT index.
    /// This is where O(n²) behavior is expected - full table scan per insert.
    /// </summary>
    [Test]
    public void InsertExplicitPkNoIndexTest()
    {
        var counts = new[] { 100, 200, 500, 1000 };
        var times = new List<(int Count, double Ms)>();
        var tableIdx = 0;

        foreach (var count in counts)
        {
            var tableName = $"ExplicitPk_{tableIdx++}";
            m_engine.Execute($@"
                CREATE TABLE {tableName} (
                    Id INT PRIMARY KEY,
                    Name VARCHAR(100),
                    Value DOUBLE
                )");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                m_engine.Execute(
                    $"INSERT INTO {tableName} (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
            sw.Stop();
            
            times.Add((count, sw.Elapsed.TotalMilliseconds));
            
            // Cleanup
            m_engine.Execute($"DROP TABLE {tableName}");
        }

        TestContext.Out.WriteLine("=== INSERT with Explicit PK (NO INDEX) ===");
        foreach (var (count, ms) in times)
        {
            TestContext.Out.WriteLine($"  {count,5} rows: {ms,8:F2} ms ({ms / count:F4} ms/row)");
        }

        // Check for O(n²) behavior
        // 1000 rows = 10x of 100 rows. Linear = 10x time, Quadratic = 100x time
        var ratio = times[3].Ms / times[0].Ms;
        TestContext.Out.WriteLine($"  Scaling ratio (1000/100): {ratio:F2}x (linear=10x, quadratic=100x)");
        
        // Document the behavior (this test may fail showing the problem)
        if (ratio > 50)
        {
            TestContext.Out.WriteLine("  ?? WARNING: O(n²) behavior detected!");
        }
    }

    /// <summary>
    /// INSERT with explicit PK value WITH UNIQUE index.
    /// This should be O(n log n) - index seek per insert.
    /// </summary>
    [Test]
    public void InsertExplicitPkWithIndexTest()
    {
        var counts = new[] { 100, 500, 1000, 2000 };
        var times = new List<(int Count, double Ms)>();
        var tableIdx = 0;

        foreach (var count in counts)
        {
            var tableName = $"ExplicitPkIdx_{tableIdx++}";
            m_engine.Execute($@"
                CREATE TABLE {tableName} (
                    Id INT PRIMARY KEY,
                    Name VARCHAR(100),
                    Value DOUBLE
                )");
        
            // Create UNIQUE index to speed up constraint checking
            m_engine.Execute($"CREATE UNIQUE INDEX IX_{tableName}_Id ON {tableName}(Id)");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                m_engine.Execute(
                    $"INSERT INTO {tableName} (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
            sw.Stop();
            
            times.Add((count, sw.Elapsed.TotalMilliseconds));
            
            // Cleanup
            m_engine.Execute($"DROP TABLE {tableName}");
        }

        TestContext.Out.WriteLine("=== INSERT with Explicit PK (WITH INDEX) ===");
        foreach (var (count, ms) in times)
        {
            TestContext.Out.WriteLine($"  {count,5} rows: {ms,8:F2} ms ({ms / count:F4} ms/row)");
        }

        var ratio = times[3].Ms / times[1].Ms;
        TestContext.Out.WriteLine($"  Scaling ratio (2000/500): {ratio:F2}x (linear=4x, O(n log n)?4.4x)");
        
        // Allow some variance in performance tests - anything under 12x is acceptable
        // (compared to 76x+ for O(n²) without index)
        Assert.That(ratio, Is.LessThan(12), "INSERT with index should scale significantly better than O(n²)");
    }

    #endregion

    #region Comparison Test

    /// <summary>
    /// Side-by-side comparison of all scenarios.
    /// </summary>
    [Test]
    [Category("Performance")]
    public void CompareAllScenariosTest()
    {
        const int rowCount = 500;

        // Scenario 1: No constraints
        m_engine.Execute("CREATE TABLE S1 (Id INT, Name VARCHAR(100), Value DOUBLE)");
        var t1 = MeasureInsertTime("S1", rowCount, includeId: true);
        m_engine.Execute("DROP TABLE S1");

        // Scenario 2: AUTOINCREMENT PK
        m_engine.Execute("CREATE TABLE S2 (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100), Value DOUBLE)");
        var t2 = MeasureInsertTime("S2", rowCount, includeId: false);
        m_engine.Execute("DROP TABLE S2");

        // Scenario 3: Explicit PK, no index
        m_engine.Execute("CREATE TABLE S3 (Id INT PRIMARY KEY, Name VARCHAR(100), Value DOUBLE)");
        var t3 = MeasureInsertTime("S3", rowCount, includeId: true);
        m_engine.Execute("DROP TABLE S3");

        // Scenario 4: Explicit PK with index
        m_engine.Execute("CREATE TABLE S4 (Id INT PRIMARY KEY, Name VARCHAR(100), Value DOUBLE)");
        m_engine.Execute("CREATE UNIQUE INDEX IX_S4_Id ON S4(Id)");
        var t4 = MeasureInsertTime("S4", rowCount, includeId: true);
        m_engine.Execute("DROP TABLE S4");

        TestContext.Out.WriteLine($"=== Comparison ({rowCount} rows) ===");
        TestContext.Out.WriteLine($"  No constraints:      {t1,8:F2} ms ({t1 / rowCount:F4} ms/row) [baseline]");
        TestContext.Out.WriteLine($"  AUTOINCREMENT PK:    {t2,8:F2} ms ({t2 / rowCount:F4} ms/row) [{t2 / t1:F2}x baseline]");
        TestContext.Out.WriteLine($"  Explicit PK (no idx):{t3,8:F2} ms ({t3 / rowCount:F4} ms/row) [{t3 / t1:F2}x baseline]");
        TestContext.Out.WriteLine($"  Explicit PK (w/ idx):{t4,8:F2} ms ({t4 / rowCount:F4} ms/row) [{t4 / t1:F2}x baseline]");

        // The bottleneck should be obvious from this comparison
        if (t3 > t1 * 10)
        {
            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine("?? BOTTLENECK IDENTIFIED: Explicit PK without index is significantly slower");
            TestContext.Out.WriteLine("   This suggests O(n²) full table scan for UNIQUE constraint validation");
        }
    }

    #endregion

    #region Detailed Constraint Overhead Tests

    /// <summary>
    /// Measure time spent specifically in constraint validation.
    /// </summary>
    [Test]
    public void ConstraintValidationOverheadTest()
    {
        const int rowCount = 200;

        // Insert rows first
        m_engine.Execute("CREATE TABLE CV (Id INT PRIMARY KEY, Name VARCHAR(100))");
        for (int i = 0; i < rowCount; i++)
        {
            m_engine.Execute($"INSERT INTO CV (Id, Name) VALUES ({i}, 'Name{i}')");
        }

        // Now measure time to validate one more insert (worst case - scan all existing rows)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        m_engine.Execute($"INSERT INTO CV (Id, Name) VALUES ({rowCount}, 'Name{rowCount}')");
        sw.Stop();

        TestContext.Out.WriteLine($"Single INSERT after {rowCount} rows: {sw.Elapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"This includes UNIQUE check scanning {rowCount} existing rows");
    }

    /// <summary>
    /// Test with multiple UNIQUE constraints.
    /// </summary>
    [Test]
    public void MultipleUniqueConstraintsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE MultiUnique (
                Id INT PRIMARY KEY,
                Code VARCHAR(20) UNIQUE,
                Email VARCHAR(100) UNIQUE,
                Name VARCHAR(100)
            )");

        const int rowCount = 200;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < rowCount; i++)
        {
            m_engine.Execute(
                "INSERT INTO MultiUnique (Id, Code, Email, Name) VALUES (@id, @code, @email, @name)",
                new Dictionary<string, object?>
                {
                    { "@id", i },
                    { "@code", $"CODE{i}" },
                    { "@email", $"user{i}@example.com" },
                    { "@name", $"Name{i}" }
                });
        }
        sw.Stop();

        TestContext.Out.WriteLine($"INSERT with 3 UNIQUE constraints: {sw.Elapsed.TotalMilliseconds:F2} ms for {rowCount} rows");
        TestContext.Out.WriteLine($"Per row: {sw.Elapsed.TotalMilliseconds / rowCount:F4} ms");
        TestContext.Out.WriteLine("(Each INSERT requires 3 full table scans without indexes)");
    }

    #endregion

    #region Helpers

    private double MeasureInsertTime(string tableName, int rowCount, bool includeId)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        if (includeId)
        {
            for (int i = 0; i < rowCount; i++)
            {
                m_engine.Execute(
                    $"INSERT INTO {tableName} (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
        }
        else
        {
            for (int i = 0; i < rowCount; i++)
            {
                m_engine.Execute(
                    $"INSERT INTO {tableName} (Name, Value) VALUES (@name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
        }
        
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    #endregion
}
