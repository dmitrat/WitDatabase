using OutWit.Database.Core.Builder;
using OutWit.Database.Core.LSM;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Performance comparison tests between B-Tree and LSM-Tree storage backends.
/// </summary>
[TestFixture]
public class StorageComparisonTests : IDisposable
{
    #region Fields

    private WitSqlEngine m_btreeEngine = null!;
    private WitSqlEngine m_lsmEngine = null!;
    private string m_lsmDirectory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        // Create BTree in-memory engine
        var btreeDatabase = WitDatabase.CreateInMemory();
        m_btreeEngine = new WitSqlEngine(btreeDatabase, ownsStore: true);

        // Create LSM engine in temp directory
        m_lsmDirectory = Path.Combine(Path.GetTempPath(), $"lsm_perf_test_{Guid.NewGuid():N}");
        var lsmDatabase = new WitDatabaseBuilder()
            .WithLsmTree(m_lsmDirectory, options =>
            {
                options.EnableWal = false;  // Disable WAL for fair comparison with in-memory BTree
                options.EnableBlockCache = true;
                options.BlockCacheSizeBytes = 10 * 1024 * 1024;
                options.MemTableSizeLimit = 4 * 1024 * 1024; // 4MB memtable
                options.Level0CompactionTrigger = 10;
                options.BackgroundCompaction = true;
            })
            .WithoutTransactions()
            .Build();
        m_lsmEngine = new WitSqlEngine(lsmDatabase, ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        m_btreeEngine?.Dispose();
        m_lsmEngine?.Dispose();
        
        try
        {
            if (Directory.Exists(m_lsmDirectory))
                Directory.Delete(m_lsmDirectory, recursive: true);
        }
        catch { }
    }

    #endregion

    #region Helpers

    private static TimeSpan MeasureTime(Action action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed;
    }

    private void CreateTestTable(WitSqlEngine engine)
    {
        engine.Execute(@"
            CREATE TABLE T (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Value DOUBLE
            )");
    }

    private void CreateTestTableWithExplicitPK(WitSqlEngine engine)
    {
        engine.Execute(@"
            CREATE TABLE T (
                Id INT PRIMARY KEY,
                Name VARCHAR(100),
                Value DOUBLE
            )");
    }

    #endregion

    #region INSERT Comparison - Auto-generated PK

    [Test]
    [Category("Performance")]
    public void InsertWithAutoIncrementComparisonTest()
    {
        const int ROW_COUNT = 1000;

        CreateTestTable(m_btreeEngine);
        CreateTestTable(m_lsmEngine);

        // BTree INSERT
        var btreeTime = MeasureTime(() =>
        {
            using var stmt = m_btreeEngine.Prepare("INSERT INTO T (Name, Value) VALUES (@name, @value)");
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        });

        // LSM INSERT
        var lsmTime = MeasureTime(() =>
        {
            using var stmt = m_lsmEngine.Prepare("INSERT INTO T (Name, Value) VALUES (@name, @value)");
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        });

        TestContext.Out.WriteLine($"=== INSERT {ROW_COUNT} rows (AUTOINCREMENT PK) ===");
        TestContext.Out.WriteLine($"BTree: {btreeTime.TotalMilliseconds:F2} ms ({btreeTime.TotalMilliseconds / ROW_COUNT:F4} ms/row)");
        TestContext.Out.WriteLine($"LSM:   {lsmTime.TotalMilliseconds:F2} ms ({lsmTime.TotalMilliseconds / ROW_COUNT:F4} ms/row)");
        TestContext.Out.WriteLine($"Ratio: LSM is {btreeTime.TotalMilliseconds / lsmTime.TotalMilliseconds:F2}x {(lsmTime < btreeTime ? "faster" : "slower")}");
    }

    #endregion

    #region INSERT Comparison - Explicit PK

    [Test]
    [Category("Performance")]
    public void InsertWithExplicitPKComparisonTest()
    {
        const int ROW_COUNT = 500;

        CreateTestTableWithExplicitPK(m_btreeEngine);
        CreateTestTableWithExplicitPK(m_lsmEngine);

        // BTree INSERT with explicit PK
        var btreeTime = MeasureTime(() =>
        {
            using var stmt = m_btreeEngine.Prepare("INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)");
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@id", i)
                    .SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        });

        // LSM INSERT with explicit PK
        var lsmTime = MeasureTime(() =>
        {
            using var stmt = m_lsmEngine.Prepare("INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)");
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@id", i)
                    .SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        });

        TestContext.Out.WriteLine($"=== INSERT {ROW_COUNT} rows (Explicit PK) ===");
        TestContext.Out.WriteLine($"BTree: {btreeTime.TotalMilliseconds:F2} ms ({btreeTime.TotalMilliseconds / ROW_COUNT:F4} ms/row)");
        TestContext.Out.WriteLine($"LSM:   {lsmTime.TotalMilliseconds:F2} ms ({lsmTime.TotalMilliseconds / ROW_COUNT:F4} ms/row)");
        TestContext.Out.WriteLine($"Ratio: LSM is {btreeTime.TotalMilliseconds / lsmTime.TotalMilliseconds:F2}x {(lsmTime < btreeTime ? "faster" : "slower")}");
    }

    #endregion

    #region SELECT Comparison

    [Test]
    [Category("Performance")]
    public void FullScanComparisonTest()
    {
        const int ROW_COUNT = 1000;

        CreateTestTable(m_btreeEngine);
        CreateTestTable(m_lsmEngine);

        // Insert test data
        using (var stmt = m_btreeEngine.Prepare("INSERT INTO T (Name, Value) VALUES (@name, @value)"))
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        }

        using (var stmt = m_lsmEngine.Prepare("INSERT INTO T (Name, Value) VALUES (@name, @value)"))
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        }

        // BTree SELECT *
        int btreeCount = 0;
        var btreeTime = MeasureTime(() =>
        {
            var rows = m_btreeEngine.Query("SELECT * FROM T");
            btreeCount = rows.Count;
        });

        // LSM SELECT *
        int lsmCount = 0;
        var lsmTime = MeasureTime(() =>
        {
            var rows = m_lsmEngine.Query("SELECT * FROM T");
            lsmCount = rows.Count;
        });

        Assert.That(btreeCount, Is.EqualTo(ROW_COUNT));
        Assert.That(lsmCount, Is.EqualTo(ROW_COUNT));

        TestContext.Out.WriteLine($"=== SELECT * FROM T ({ROW_COUNT} rows) ===");
        TestContext.Out.WriteLine($"BTree: {btreeTime.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"LSM:   {lsmTime.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"Ratio: LSM is {btreeTime.TotalMilliseconds / lsmTime.TotalMilliseconds:F2}x {(lsmTime < btreeTime ? "faster" : "slower")}");
    }

    [Test]
    [Category("Performance")]
    public void AggregationComparisonTest()
    {
        const int ROW_COUNT = 1000;

        CreateTestTable(m_btreeEngine);
        CreateTestTable(m_lsmEngine);

        // Insert test data
        using (var stmt = m_btreeEngine.Prepare("INSERT INTO T (Name, Value) VALUES (@name, @value)"))
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        }

        using (var stmt = m_lsmEngine.Prepare("INSERT INTO T (Name, Value) VALUES (@name, @value)"))
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        }

        // BTree COUNT
        var btreeTime = MeasureTime(() =>
        {
            m_btreeEngine.Query("SELECT COUNT(*) FROM T");
        });

        // LSM COUNT
        var lsmTime = MeasureTime(() =>
        {
            m_lsmEngine.Query("SELECT COUNT(*) FROM T");
        });

        TestContext.Out.WriteLine($"=== SELECT COUNT(*) FROM T ({ROW_COUNT} rows) ===");
        TestContext.Out.WriteLine($"BTree: {btreeTime.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"LSM:   {lsmTime.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"Ratio: LSM is {btreeTime.TotalMilliseconds / lsmTime.TotalMilliseconds:F2}x {(lsmTime < btreeTime ? "faster" : "slower")}");
    }

    #endregion

    #region Mixed Workload

    [Test]
    [Category("Performance")]
    public void MixedWorkloadComparisonTest()
    {
        const int OPERATIONS = 500;

        CreateTestTable(m_btreeEngine);
        CreateTestTable(m_lsmEngine);

        // BTree mixed workload
        var btreeTime = MeasureTime(() =>
        {
            using var insertStmt = m_btreeEngine.Prepare("INSERT INTO T (Name, Value) VALUES (@name, @value)");
            
            for (int i = 0; i < OPERATIONS; i++)
            {
                // Insert
                insertStmt.SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();

                // Read every 10th insert
                if (i % 10 == 0)
                {
                    m_btreeEngine.Query("SELECT COUNT(*) FROM T");
                }
            }
        });

        // LSM mixed workload
        var lsmTime = MeasureTime(() =>
        {
            using var insertStmt = m_lsmEngine.Prepare("INSERT INTO T (Name, Value) VALUES (@name, @value)");
            
            for (int i = 0; i < OPERATIONS; i++)
            {
                // Insert
                insertStmt.SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();

                // Read every 10th insert
                if (i % 10 == 0)
                {
                    m_lsmEngine.Query("SELECT COUNT(*) FROM T");
                }
            }
        });

        TestContext.Out.WriteLine($"=== Mixed Workload ({OPERATIONS} inserts + {OPERATIONS / 10} reads) ===");
        TestContext.Out.WriteLine($"BTree: {btreeTime.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"LSM:   {lsmTime.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"Ratio: LSM is {btreeTime.TotalMilliseconds / lsmTime.TotalMilliseconds:F2}x {(lsmTime < btreeTime ? "faster" : "slower")}");
    }

    #endregion
}
