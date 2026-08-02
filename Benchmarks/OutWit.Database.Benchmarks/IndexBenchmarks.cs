using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Product document for index benchmarks.
/// </summary>
public class ProductDoc
{
    public int Id { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Price { get; set; }
    public int CategoryId { get; set; }
    public int Stock { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Benchmarks for index performance.
/// Tests index seek, range scan, and covering index patterns.
/// </summary>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class IndexBenchmarks : IDisposable
{
    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private LiteDatabase? m_liteDb;
    private ILiteCollection<ProductDoc>? m_liteCollection;
    private string m_witPath = null!;
    private string m_sqlitePath = null!;
    private string m_liteDbPath = null!;

    #endregion

    #region Parameters

    [ParamsSource(nameof(TableSizeValues))]
    public int TableSize { get; set; }

    public IEnumerable<int> TableSizeValues => BenchmarkSweep.Sizes(5000, 20000);

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
            ? BenchmarkPathHelper.GenerateUniquePath("wit_idx_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_idx_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_idx") + ".db";
        m_liteDbPath = BenchmarkPathHelper.GenerateUniquePath("lite_idx") + ".db";

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
                CREATE TABLE Products (
                    Id BIGINT PRIMARY KEY AUTOINCREMENT,
                    SKU VARCHAR(50) NOT NULL,
                    Name VARCHAR(200),
                    Price DOUBLE,
                    CategoryId INT,
                    Stock INT,
                    CreatedAt DATE
                )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE UNIQUE INDEX IX_Products_SKU ON Products(SKU)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_CategoryId ON Products(CategoryId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_Price ON Products(Price)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_Category_Price ON Products(CategoryId, Price)";
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
            c.CommandText = @"INSERT INTO Products (SKU, Name, Price, CategoryId, Stock, CreatedAt) 
                              VALUES (@sku, @name, @price, @catId, @stock, @date)";

            var pSku = c.CreateParameter(); pSku.ParameterName = "@sku"; c.Parameters.Add(pSku);
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            var pPrice = c.CreateParameter(); pPrice.ParameterName = "@price"; c.Parameters.Add(pPrice);
            var pCatId = c.CreateParameter(); pCatId.ParameterName = "@catId"; c.Parameters.Add(pCatId);
            var pStock = c.CreateParameter(); pStock.ParameterName = "@stock"; c.Parameters.Add(pStock);
            var pDate = c.CreateParameter(); pDate.ParameterName = "@date"; c.Parameters.Add(pDate);

            var rnd = new Random(42);
            var baseDate = new DateOnly(2024, 1, 1);

            for (int i = 0; i < TableSize; i++)
            {
                pSku.Value = $"SKU-{i:D8}";
                pName.Value = $"Product {i}";
                pPrice.Value = Math.Round(rnd.NextDouble() * 1000, 2);
                pCatId.Value = rnd.Next(1, 21);
                pStock.Value = rnd.Next(0, 1000);
                pDate.Value = baseDate.AddDays(i % 365);
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
                CREATE TABLE Products (
                    Id INTEGER PRIMARY KEY,
                    SKU TEXT NOT NULL,
                    Name TEXT,
                    Price REAL,
                    CategoryId INTEGER,
                    Stock INTEGER,
                    CreatedAt TEXT
                )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE UNIQUE INDEX IX_Products_SKU ON Products(SKU)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_CategoryId ON Products(CategoryId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_Price ON Products(Price)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_Category_Price ON Products(CategoryId, Price)";
            c.ExecuteNonQuery();
        }

        var tx = m_sqliteConn.BeginTransaction();
        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO Products (SKU, Name, Price, CategoryId, Stock, CreatedAt) 
                              VALUES (@sku, @name, @price, @catId, @stock, @date)";

            var pSku = c.CreateParameter(); pSku.ParameterName = "@sku"; c.Parameters.Add(pSku);
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            var pPrice = c.CreateParameter(); pPrice.ParameterName = "@price"; c.Parameters.Add(pPrice);
            var pCatId = c.CreateParameter(); pCatId.ParameterName = "@catId"; c.Parameters.Add(pCatId);
            var pStock = c.CreateParameter(); pStock.ParameterName = "@stock"; c.Parameters.Add(pStock);
            var pDate = c.CreateParameter(); pDate.ParameterName = "@date"; c.Parameters.Add(pDate);

            var rnd = new Random(42);
            var baseDate = new DateOnly(2024, 1, 1);

            for (int i = 0; i < TableSize; i++)
            {
                pSku.Value = $"SKU-{i:D8}";
                pName.Value = $"Product {i}";
                pPrice.Value = Math.Round(rnd.NextDouble() * 1000, 2);
                pCatId.Value = rnd.Next(1, 21);
                pStock.Value = rnd.Next(0, 1000);
                pDate.Value = baseDate.AddDays(i % 365).ToString("yyyy-MM-dd");
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    private void SetupLiteDb()
    {
        m_liteDb = new LiteDatabase(m_liteDbPath);
        m_liteCollection = m_liteDb.GetCollection<ProductDoc>("products");
        m_liteCollection.EnsureIndex(x => x.SKU, true);
        m_liteCollection.EnsureIndex(x => x.CategoryId);
        m_liteCollection.EnsureIndex(x => x.Price);

        var rnd = new Random(42);
        var baseDate = new DateTime(2024, 1, 1);
        var docs = new List<ProductDoc>(TableSize);

        for (int i = 0; i < TableSize; i++)
        {
            docs.Add(new ProductDoc
            {
                Id = i + 1,
                SKU = $"SKU-{i:D8}",
                Name = $"Product {i}",
                Price = Math.Round(rnd.NextDouble() * 1000, 2),
                CategoryId = rnd.Next(1, 21),
                Stock = rnd.Next(0, 1000),
                CreatedAt = baseDate.AddDays(i % 365)
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

    #region Benchmarks - Index Seek (Equality)

    [Benchmark(Description = "Index Seek (unique) x100 - WitDb")]
    public int IndexSeekUniqueWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE SKU = @sku";
        var p = c.CreateParameter(); p.ParameterName = "@sku"; c.Parameters.Add(p);

        var rnd = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            int id = rnd.Next(0, TableSize);
            p.Value = $"SKU-{id:D8}";
            using var r = c.ExecuteReader();
            if (r.Read()) cnt++;
        }
        return cnt;
    }

    // No Baseline here on purpose. BenchmarkDotNet allows one baseline per class unless the
    // benchmarks are split into categories, so a single [Benchmark(Baseline = true)] made the Ratio
    // column compare every operation in the class against one unrelated operation - the January
    // report rated a 20-iteration seek "2.74x faster" than a 100-iteration one. Until the classes
    // carry [BenchmarkCategory] the honest report has no Ratio column at all; ratios are computed
    // per operation from the Mean column instead.
    [Benchmark(Description = "Index Seek (unique) x100 - SQLite")]
    public int IndexSeekUniqueSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE SKU = @sku";
        var p = c.CreateParameter(); p.ParameterName = "@sku"; c.Parameters.Add(p);

        var rnd = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            int id = rnd.Next(0, TableSize);
            p.Value = $"SKU-{id:D8}";
            using var r = c.ExecuteReader();
            if (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Index Seek (unique) x100 - LiteDB")]
    public int IndexSeekUniqueLiteDb()
    {
        int cnt = 0;
        var rnd = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            int id = rnd.Next(0, TableSize);
            // The interpolation has to happen outside the predicate: LiteDB translates the lambda
            // to BSON and throws NotImplementedException on an interpolated string inside it. With
            // it inline this benchmark threw on every run and reported NA from January onwards.
            var sku = $"SKU-{id:D8}";
            var doc = m_liteCollection!.FindOne(x => x.SKU == sku);
            if (doc != null) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Index Seek (non-unique) x20 - WitDb")]
    public int IndexSeekNonUniqueWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE CategoryId = @catId";
        var p = c.CreateParameter(); p.ParameterName = "@catId"; c.Parameters.Add(p);

        for (int catId = 1; catId <= 20; catId++)
        {
            p.Value = catId;
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Index Seek (non-unique) x20 - SQLite")]
    public int IndexSeekNonUniqueSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE CategoryId = @catId";
        var p = c.CreateParameter(); p.ParameterName = "@catId"; c.Parameters.Add(p);

        for (int catId = 1; catId <= 20; catId++)
        {
            p.Value = catId;
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Index Seek (non-unique) x20 - LiteDB")]
    public int IndexSeekNonUniqueLiteDb()
    {
        int cnt = 0;
        for (int catId = 1; catId <= 20; catId++)
        {
            foreach (var doc in m_liteCollection!.Find(x => x.CategoryId == catId))
                cnt++;
        }
        return cnt;
    }

    #endregion

    #region Benchmarks - Index Range Scan

    [Benchmark(Description = "Index Range Scan (BETWEEN) - WitDb")]
    public int IndexRangeScanWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE Price BETWEEN @min AND @max";
        var pMin = c.CreateParameter(); pMin.ParameterName = "@min"; pMin.Value = 100.0; c.Parameters.Add(pMin);
        var pMax = c.CreateParameter(); pMax.ParameterName = "@max"; pMax.Value = 200.0; c.Parameters.Add(pMax);

        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Index Range Scan (BETWEEN) - SQLite")]
    public int IndexRangeScanSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE Price BETWEEN @min AND @max";
        var pMin = c.CreateParameter(); pMin.ParameterName = "@min"; pMin.Value = 100.0; c.Parameters.Add(pMin);
        var pMax = c.CreateParameter(); pMax.ParameterName = "@max"; pMax.Value = 200.0; c.Parameters.Add(pMax);

        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Index Range Scan (BETWEEN) - LiteDB")]
    public int IndexRangeScanLiteDb()
    {
        int cnt = 0;
        foreach (var doc in m_liteCollection!.Find(x => x.Price >= 100.0 && x.Price <= 200.0))
            cnt++;
        return cnt;
    }

    [Benchmark(Description = "Index Range Scan (> threshold) - WitDb")]
    public int IndexRangeScanGtWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE Price > @price";
        var p = c.CreateParameter(); p.ParameterName = "@price"; p.Value = 900.0; c.Parameters.Add(p);

        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Index Range Scan (> threshold) - SQLite")]
    public int IndexRangeScanGtSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE Price > @price";
        var p = c.CreateParameter(); p.ParameterName = "@price"; p.Value = 900.0; c.Parameters.Add(p);

        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Index Range Scan (> threshold) - LiteDB")]
    public int IndexRangeScanGtLiteDb()
    {
        int cnt = 0;
        foreach (var doc in m_liteCollection!.Find(x => x.Price > 900.0))
            cnt++;
        return cnt;
    }

    #endregion

    #region Benchmarks - Composite Index

    [Benchmark(Description = "Composite Index Query - WitDb")]
    public int CompositeIndexWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE CategoryId = @catId AND Price < @price";
        var pCat = c.CreateParameter(); pCat.ParameterName = "@catId"; c.Parameters.Add(pCat);
        var pPrice = c.CreateParameter(); pPrice.ParameterName = "@price"; pPrice.Value = 500.0; c.Parameters.Add(pPrice);

        for (int catId = 1; catId <= 10; catId++)
        {
            pCat.Value = catId;
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Composite Index Query - SQLite")]
    public int CompositeIndexSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE CategoryId = @catId AND Price < @price";
        var pCat = c.CreateParameter(); pCat.ParameterName = "@catId"; c.Parameters.Add(pCat);
        var pPrice = c.CreateParameter(); pPrice.ParameterName = "@price"; pPrice.Value = 500.0; c.Parameters.Add(pPrice);

        for (int catId = 1; catId <= 10; catId++)
        {
            pCat.Value = catId;
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Composite Index Query - LiteDB")]
    public int CompositeIndexLiteDb()
    {
        int cnt = 0;
        for (int catId = 1; catId <= 10; catId++)
        {
            foreach (var doc in m_liteCollection!.Find(x => x.CategoryId == catId && x.Price < 500.0))
                cnt++;
        }
        return cnt;
    }

    #endregion

    #region Benchmarks - Full Scan vs Index

    [Benchmark(Description = "Full Scan (no index) - WitDb")]
    public int FullScanWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE Stock > 500";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Full Scan (no index) - SQLite")]
    public int FullScanSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Products WHERE Stock > 500";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Full Scan (no index) - LiteDB")]
    public int FullScanLiteDb()
    {
        int cnt = 0;
        foreach (var doc in m_liteCollection!.Find(x => x.Stock > 500))
            cnt++;
        return cnt;
    }

    #endregion

    #region IDisposable

    public void Dispose() => GlobalCleanup();

    #endregion
}
