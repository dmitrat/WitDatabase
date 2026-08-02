using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Sales document for aggregate benchmarks.
/// </summary>
public class SalesDoc
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int CategoryId { get; set; }
    public double Amount { get; set; }
    public int Quantity { get; set; }
    public DateTime SaleDate { get; set; }
    public string Region { get; set; } = string.Empty;
}

/// <summary>
/// Benchmarks for aggregate function performance.
/// Tests COUNT, SUM, AVG, MIN, MAX, GROUP BY patterns.
/// </summary>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class AggregateBenchmarks : IDisposable
{
    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private LiteDatabase? m_liteDb;
    private ILiteCollection<SalesDoc>? m_liteCollection;
    private string m_witPath = null!;
    private string m_sqlitePath = null!;
    private string m_liteDbPath = null!;

    #endregion

    #region Parameters

    [ParamsSource(nameof(TableSizeValues))]
    public int TableSize { get; set; }

    public IEnumerable<int> TableSizeValues => BenchmarkSweep.Sizes(1000, 10000);

    [ParamsSource(nameof(EngineModeValues))]
    public WitDbEngineMode EngineMode { get; set; }

    public IEnumerable<WitDbEngineMode> EngineModeValues => BenchmarkSweep.Modes(WitDbEngineMode.Default, WitDbEngineMode.BTree, WitDbEngineMode.Lsm, WitDbEngineMode.BTreeParallelAuto, WitDbEngineMode.LsmParallelAuto);

    #endregion

    #region Setup/Cleanup

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Use different path patterns for BTree (file .witdb) vs LSM (directory)
        var isLsm = EngineMode is WitDbEngineMode.Lsm or WitDbEngineMode.LsmParallelAuto;
        m_witPath = isLsm 
            ? BenchmarkPathHelper.GenerateUniquePath("wit_agg_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_agg_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_agg") + ".db";
        m_liteDbPath = BenchmarkPathHelper.GenerateUniquePath("lite_agg") + ".db";

        CleanupPaths();

        SetupWitDb();
        SetupSqlite();
        SetupLiteDb();
    }

    private void CleanupPaths()
    {
        BenchmarkPathHelper.SafeCleanup(m_witPath);
        BenchmarkPathHelper.SafeCleanup(m_witPath + "_indexes");
        BenchmarkPathHelper.SafeCleanup(m_sqlitePath);
        BenchmarkPathHelper.SafeCleanup(m_liteDbPath);
    }

    private void SetupWitDb()
    {
        var connStr = WitDbConnectionHelper.BuildConnectionString(m_witPath, EngineMode);
        m_witConn = new WitDbConnection(connStr);
        m_witConn.Open();

        using (var c = m_witConn.CreateCommand())
        {
            c.CommandText = @"
                CREATE TABLE Sales (
                    Id BIGINT PRIMARY KEY AUTOINCREMENT,
                    ProductId INT,
                    CategoryId INT,
                    Amount DOUBLE,
                    Quantity INT,
                    SaleDate DATE,
                    Region VARCHAR(50)
                )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Sales_Category ON Sales(CategoryId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Sales_Region ON Sales(Region)";
            c.ExecuteNonQuery();
        }

        InsertTestDataWit();
    }

    private void InsertTestDataWit()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO Sales (ProductId, CategoryId, Amount, Quantity, SaleDate, Region) 
                              VALUES (@pid, @cid, @amt, @qty, @date, @reg)";

            var pPid = c.CreateParameter(); pPid.ParameterName = "@pid"; c.Parameters.Add(pPid);
            var pCid = c.CreateParameter(); pCid.ParameterName = "@cid"; c.Parameters.Add(pCid);
            var pAmt = c.CreateParameter(); pAmt.ParameterName = "@amt"; c.Parameters.Add(pAmt);
            var pQty = c.CreateParameter(); pQty.ParameterName = "@qty"; c.Parameters.Add(pQty);
            var pDate = c.CreateParameter(); pDate.ParameterName = "@date"; c.Parameters.Add(pDate);
            var pReg = c.CreateParameter(); pReg.ParameterName = "@reg"; c.Parameters.Add(pReg);

            var rnd = new Random(42);
            var baseDate = new DateOnly(2024, 1, 1);
            string[] regions = { "North", "South", "East", "West", "Central" };

            for (int i = 0; i < TableSize; i++)
            {
                pPid.Value = rnd.Next(1, 101);
                pCid.Value = rnd.Next(1, 11);
                pAmt.Value = Math.Round(rnd.NextDouble() * 1000, 2);
                pQty.Value = rnd.Next(1, 50);
                pDate.Value = baseDate.AddDays(i % 365);
                pReg.Value = regions[i % regions.Length];
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    private void SetupSqlite()
    {
        m_sqliteConn = new SqliteConnection($"Data Source={m_sqlitePath}");
        m_sqliteConn.Open();

        using (var c = m_sqliteConn.CreateCommand())
        {
            c.CommandText = @"
                CREATE TABLE Sales (
                    Id INTEGER PRIMARY KEY,
                    ProductId INTEGER,
                    CategoryId INTEGER,
                    Amount REAL,
                    Quantity INTEGER,
                    SaleDate TEXT,
                    Region TEXT
                )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Sales_Category ON Sales(CategoryId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Sales_Region ON Sales(Region)";
            c.ExecuteNonQuery();
        }

        var tx = m_sqliteConn.BeginTransaction();
        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO Sales (ProductId, CategoryId, Amount, Quantity, SaleDate, Region) 
                              VALUES (@pid, @cid, @amt, @qty, @date, @reg)";

            var pPid = c.CreateParameter(); pPid.ParameterName = "@pid"; c.Parameters.Add(pPid);
            var pCid = c.CreateParameter(); pCid.ParameterName = "@cid"; c.Parameters.Add(pCid);
            var pAmt = c.CreateParameter(); pAmt.ParameterName = "@amt"; c.Parameters.Add(pAmt);
            var pQty = c.CreateParameter(); pQty.ParameterName = "@qty"; c.Parameters.Add(pQty);
            var pDate = c.CreateParameter(); pDate.ParameterName = "@date"; c.Parameters.Add(pDate);
            var pReg = c.CreateParameter(); pReg.ParameterName = "@reg"; c.Parameters.Add(pReg);

            var rnd = new Random(42);
            var baseDate = new DateOnly(2024, 1, 1);
            string[] regions = { "North", "South", "East", "West", "Central" };

            for (int i = 0; i < TableSize; i++)
            {
                pPid.Value = rnd.Next(1, 101);
                pCid.Value = rnd.Next(1, 11);
                pAmt.Value = Math.Round(rnd.NextDouble() * 1000, 2);
                pQty.Value = rnd.Next(1, 50);
                pDate.Value = baseDate.AddDays(i % 365).ToString("yyyy-MM-dd");
                pReg.Value = regions[i % regions.Length];
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    private void SetupLiteDb()
    {
        m_liteDb = new LiteDatabase(m_liteDbPath);
        m_liteCollection = m_liteDb.GetCollection<SalesDoc>("sales");
        m_liteCollection.EnsureIndex(x => x.CategoryId);
        m_liteCollection.EnsureIndex(x => x.Region);

        var rnd = new Random(42);
        var baseDate = new DateTime(2024, 1, 1);
        string[] regions = { "North", "South", "East", "West", "Central" };
        var docs = new List<SalesDoc>(TableSize);

        for (int i = 0; i < TableSize; i++)
        {
            docs.Add(new SalesDoc
            {
                Id = i + 1,
                ProductId = rnd.Next(1, 101),
                CategoryId = rnd.Next(1, 11),
                Amount = Math.Round(rnd.NextDouble() * 1000, 2),
                Quantity = rnd.Next(1, 50),
                SaleDate = baseDate.AddDays(i % 365),
                Region = regions[i % regions.Length]
            });
        }
        m_liteCollection.InsertBulk(docs);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        m_witConn?.Dispose();
        m_sqliteConn?.Dispose();
        m_liteDb?.Dispose();

        CleanupPaths();
    }

    #endregion

    #region Benchmarks - COUNT(*)

    [Benchmark(Description = "COUNT(*) - WitDb")]
    public long CountAllWitDb()
    {
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM Sales";
        return Convert.ToInt64(c.ExecuteScalar());
    }

    // No Baseline here on purpose. BenchmarkDotNet allows one baseline per class unless the
    // benchmarks are split into categories, so a single [Benchmark(Baseline = true)] made the Ratio
    // column compare every operation in the class against one unrelated operation - the January
    // report rated a 20-iteration seek "2.74x faster" than a 100-iteration one. Until the classes
    // carry [BenchmarkCategory] the honest report has no Ratio column at all; ratios are computed
    // per operation from the Mean column instead.
    [Benchmark(Description = "COUNT(*) - SQLite")]
    public long CountAllSqlite()
    {
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM Sales";
        return Convert.ToInt64(c.ExecuteScalar());
    }

    [Benchmark(Description = "Count() - LiteDB")]
    public long CountAllLiteDb()
    {
        return m_liteCollection!.Count();
    }

    #endregion

    #region Benchmarks - SUM/AVG

    [Benchmark(Description = "SUM(Amount) - WitDb")]
    public double SumWitDb()
    {
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT SUM(Amount) FROM Sales";
        return Convert.ToDouble(c.ExecuteScalar());
    }

    [Benchmark(Description = "SUM(Amount) - SQLite")]
    public double SumSqlite()
    {
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT SUM(Amount) FROM Sales";
        return Convert.ToDouble(c.ExecuteScalar());
    }

    [Benchmark(Description = "Sum(Amount) - LiteDB")]
    public double SumLiteDb()
    {
        return m_liteCollection!.FindAll().Sum(x => x.Amount);
    }

    [Benchmark(Description = "AVG(Amount) - WitDb")]
    public double AvgWitDb()
    {
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT AVG(Amount) FROM Sales";
        return Convert.ToDouble(c.ExecuteScalar());
    }

    [Benchmark(Description = "AVG(Amount) - SQLite")]
    public double AvgSqlite()
    {
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT AVG(Amount) FROM Sales";
        return Convert.ToDouble(c.ExecuteScalar());
    }

    [Benchmark(Description = "Average(Amount) - LiteDB")]
    public double AvgLiteDb()
    {
        return m_liteCollection!.FindAll().Average(x => x.Amount);
    }

    #endregion

    #region Benchmarks - MIN/MAX

    [Benchmark(Description = "MIN/MAX(Amount) - WitDb")]
    public (double min, double max) MinMaxWitDb()
    {
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT MIN(Amount), MAX(Amount) FROM Sales";
        using var r = c.ExecuteReader();
        r.Read();
        return (r.GetDouble(0), r.GetDouble(1));
    }

    [Benchmark(Description = "MIN/MAX(Amount) - SQLite")]
    public (double min, double max) MinMaxSqlite()
    {
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT MIN(Amount), MAX(Amount) FROM Sales";
        using var r = c.ExecuteReader();
        r.Read();
        return (r.GetDouble(0), r.GetDouble(1));
    }

    [Benchmark(Description = "Min/Max(Amount) - LiteDB")]
    public (double min, double max) MinMaxLiteDb()
    {
        var all = m_liteCollection!.FindAll().ToList();
        return (all.Min(x => x.Amount), all.Max(x => x.Amount));
    }

    #endregion

    #region Benchmarks - GROUP BY Single Column

    [Benchmark(Description = "GROUP BY single column - WitDb")]
    public int GroupBySingleWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT CategoryId, COUNT(*), SUM(Amount) FROM Sales GROUP BY CategoryId";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "GROUP BY single column - SQLite")]
    public int GroupBySingleSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT CategoryId, COUNT(*), SUM(Amount) FROM Sales GROUP BY CategoryId";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "GroupBy single column - LiteDB")]
    public int GroupBySingleLiteDb()
    {
        return m_liteCollection!.FindAll()
            .GroupBy(x => x.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count(), Sum = g.Sum(x => x.Amount) })
            .Count();
    }

    #endregion

    #region Benchmarks - GROUP BY Multiple Columns

    [Benchmark(Description = "GROUP BY multiple columns - WitDb")]
    public int GroupByMultipleWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT CategoryId, Region, COUNT(*), SUM(Amount) FROM Sales GROUP BY CategoryId, Region";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "GROUP BY multiple columns - SQLite")]
    public int GroupByMultipleSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT CategoryId, Region, COUNT(*), SUM(Amount) FROM Sales GROUP BY CategoryId, Region";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "GroupBy multiple columns - LiteDB")]
    public int GroupByMultipleLiteDb()
    {
        return m_liteCollection!.FindAll()
            .GroupBy(x => new { x.CategoryId, x.Region })
            .Select(g => new { g.Key.CategoryId, g.Key.Region, Count = g.Count(), Sum = g.Sum(x => x.Amount) })
            .Count();
    }

    #endregion

    #region Benchmarks - GROUP BY with HAVING

    [Benchmark(Description = "GROUP BY with HAVING - WitDb")]
    public int GroupByHavingWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = @"
            SELECT CategoryId, COUNT(*), SUM(Amount) 
            FROM Sales 
            GROUP BY CategoryId 
            HAVING COUNT(*) > 50";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "GROUP BY with HAVING - SQLite")]
    public int GroupByHavingSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = @"
            SELECT CategoryId, COUNT(*) AS cnt, SUM(Amount) AS total 
            FROM Sales 
            GROUP BY CategoryId 
            HAVING COUNT(*) > 50";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "GroupBy with Where (HAVING) - LiteDB")]
    public int GroupByHavingLiteDb()
    {
        return m_liteCollection!.FindAll()
            .GroupBy(x => x.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count(), Sum = g.Sum(x => x.Amount) })
            .Where(x => x.Count > 50)
            .Count();
    }

    #endregion

    #region Benchmarks - Complex Aggregation

    [Benchmark(Description = "Complex aggregation - WitDb")]
    public int ComplexAggWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = @"
            SELECT 
                Region,
                COUNT(*),
                SUM(Amount),
                AVG(Amount),
                MIN(Quantity),
                MAX(Quantity)
            FROM Sales
            GROUP BY Region
            ORDER BY SUM(Amount) DESC";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Complex aggregation - SQLite")]
    public int ComplexAggSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = @"
            SELECT 
                Region,
                COUNT(*) AS SalesCount,
                SUM(Amount) AS TotalAmount,
                AVG(Amount) AS AvgAmount,
                MIN(Quantity) AS MinQty,
                MAX(Quantity) AS MaxQty
            FROM Sales
            GROUP BY Region
            ORDER BY SUM(Amount) DESC";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Complex aggregation - LiteDB")]
    public int ComplexAggLiteDb()
    {
        return m_liteCollection!.FindAll()
            .GroupBy(x => x.Region)
            .Select(g => new
            {
                Region = g.Key,
                Count = g.Count(),
                Sum = g.Sum(x => x.Amount),
                Avg = g.Average(x => x.Amount),
                MinQty = g.Min(x => x.Quantity),
                MaxQty = g.Max(x => x.Quantity)
            })
            .OrderByDescending(x => x.Sum)
            .Count();
    }

    #endregion

    #region IDisposable

    public void Dispose() => GlobalCleanup();

    #endregion
}
