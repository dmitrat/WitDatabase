using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Benchmarks for SELECT query performance.
/// Tests various query patterns against different WitDb modes, SQLite and LiteDB.
/// </summary>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class QueryBenchmarks : IDisposable
{
    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private LiteDatabase? m_liteDb;
    private ILiteCollection<BenchmarkUser>? m_liteCollection;
    private string m_witPath = null!;
    private string m_sqlitePath = null!;
    private string m_liteDbPath = null!;
    private int[] m_randomIds = null!;

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
        // BTree needs .witdb extension to properly create _indexes sibling directory
        var isLsm = EngineMode is WitDbEngineMode.Lsm or WitDbEngineMode.LsmParallelAuto;
        m_witPath = isLsm 
            ? BenchmarkPathHelper.GenerateUniquePath("wit_query_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_query_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_query") + ".db";
        m_liteDbPath = BenchmarkPathHelper.GenerateUniquePath("lite_query") + ".db";

        SetupWitDb();
        SetupSqlite();
        SetupLiteDb();

        // Generate random IDs for point queries
        var rnd = new Random(42);
        m_randomIds = Enumerable.Range(0, 1000).Select(_ => rnd.Next(1, TableSize + 1)).ToArray();
    }

    private void CleanupPaths()
    {
        BenchmarkPathHelper.SafeCleanup(m_witPath);
        BenchmarkPathHelper.SafeCleanup(m_witPath + "_indexes"); // Clean up index directory
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
                CREATE TABLE Users (
                    Id BIGINT PRIMARY KEY AUTOINCREMENT,
                    Name VARCHAR(100) NOT NULL,
                    Email VARCHAR(255),
                    Age INT,
                    Balance DOUBLE,
                    CreatedAt DATETIME
                )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Users_Age ON Users(Age)";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Users_Name ON Users(Name)";
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
            c.CommandText = "INSERT INTO Users (Name, Email, Age, Balance, CreatedAt) VALUES (@n, @e, @a, @b, @c)";

            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pe = c.CreateParameter(); pe.ParameterName = "@e"; c.Parameters.Add(pe);
            var pa = c.CreateParameter(); pa.ParameterName = "@a"; c.Parameters.Add(pa);
            var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);
            var pc = c.CreateParameter(); pc.ParameterName = "@c"; c.Parameters.Add(pc);

            var rnd = new Random(42);
            var baseDate = new DateTime(2020, 1, 1);

            for (int i = 0; i < TableSize; i++)
            {
                pn.Value = $"User_{i}";
                pe.Value = $"user{i}@example.com";
                pa.Value = 18 + (i % 50);
                pb.Value = Math.Round(rnd.NextDouble() * 10000, 2);
                pc.Value = baseDate.AddDays(i % 365);
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
                CREATE TABLE Users (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Email TEXT,
                    Age INTEGER,
                    Balance REAL,
                    CreatedAt TEXT
                )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Users_Age ON Users(Age)";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Users_Name ON Users(Name)";
            c.ExecuteNonQuery();
        }

        var tx = m_sqliteConn.BeginTransaction();
        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO Users (Name, Email, Age, Balance, CreatedAt) VALUES (@n, @e, @a, @b, @c)";

            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pe = c.CreateParameter(); pe.ParameterName = "@e"; c.Parameters.Add(pe);
            var pa = c.CreateParameter(); pa.ParameterName = "@a"; c.Parameters.Add(pa);
            var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);
            var pc = c.CreateParameter(); pc.ParameterName = "@c"; c.Parameters.Add(pc);

            var rnd = new Random(42);
            var baseDate = new DateTime(2020, 1, 1);

            for (int i = 0; i < TableSize; i++)
            {
                pn.Value = $"User_{i}";
                pe.Value = $"user{i}@example.com";
                pa.Value = 18 + (i % 50);
                pb.Value = Math.Round(rnd.NextDouble() * 10000, 2);
                pc.Value = baseDate.AddDays(i % 365).ToString("yyyy-MM-dd");
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    private void SetupLiteDb()
    {
        m_liteDb = new LiteDatabase(m_liteDbPath);
        m_liteCollection = m_liteDb.GetCollection<BenchmarkUser>("users");
        m_liteCollection.EnsureIndex(x => x.Age);
        m_liteCollection.EnsureIndex(x => x.Name);

        var rnd = new Random(42);
        var baseDate = new DateTime(2020, 1, 1);
        var docs = new List<BenchmarkUser>(TableSize);

        for (int i = 0; i < TableSize; i++)
        {
            docs.Add(new BenchmarkUser
            {
                Id = i + 1,
                Name = $"User_{i}",
                Email = $"user{i}@example.com",
                Age = 18 + (i % 50),
                City = "City",
                IsActive = true,
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

    #region Benchmarks - Simple SELECT

    [Benchmark(Description = "SELECT * (full scan) - WitDb")]
    public int SelectAllWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    // No Baseline here on purpose. BenchmarkDotNet allows one baseline per class unless the
    // benchmarks are split into categories, so a single [Benchmark(Baseline = true)] made the Ratio
    // column compare every operation in the class against one unrelated operation - the January
    // report rated a 20-iteration seek "2.74x faster" than a 100-iteration one. Until the classes
    // carry [BenchmarkCategory] the honest report has no Ratio column at all; ratios are computed
    // per operation from the Mean column instead.
    [Benchmark(Description = "SELECT * (full scan) - SQLite")]
    public int SelectAllSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "FindAll (full scan) - LiteDB")]
    public int SelectAllLiteDb()
    {
        int cnt = 0;
        foreach (var doc in m_liteCollection!.FindAll())
            cnt++;
        return cnt;
    }

    #endregion

    #region Benchmarks - SELECT with WHERE

    [Benchmark(Description = "SELECT WHERE Age > 30 - WitDb")]
    public int SelectWhereWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users WHERE Age > 30";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "SELECT WHERE Age > 30 - SQLite")]
    public int SelectWhereSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users WHERE Age > 30";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Find(Age > 30) - LiteDB")]
    public int SelectWhereLiteDb()
    {
        int cnt = 0;
        foreach (var doc in m_liteCollection!.Find(x => x.Age > 30))
            cnt++;
        return cnt;
    }

    #endregion

    #region Benchmarks - SELECT with ORDER BY

    [Benchmark(Description = "SELECT ORDER BY Name - WitDb")]
    public int SelectOrderByWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users ORDER BY Name";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "SELECT ORDER BY Name - SQLite")]
    public int SelectOrderBySqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users ORDER BY Name";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "FindAll + OrderBy - LiteDB")]
    public int SelectOrderByLiteDb()
    {
        int cnt = 0;
        foreach (var doc in m_liteCollection!.FindAll().OrderBy(x => x.Name))
            cnt++;
        return cnt;
    }

    #endregion

    #region Benchmarks - SELECT with LIMIT

    [Benchmark(Description = "SELECT LIMIT 100 - WitDb")]
    public int SelectLimitWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users LIMIT 100";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "SELECT LIMIT 100 - SQLite")]
    public int SelectLimitSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users LIMIT 100";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "FindAll.Take(100) - LiteDB")]
    public int SelectLimitLiteDb()
    {
        int cnt = 0;
        foreach (var doc in m_liteCollection!.FindAll().Take(100))
            cnt++;
        return cnt;
    }

    #endregion

    #region Benchmarks - Point Query

    [Benchmark(Description = "Point Query by PK (100x) - WitDb")]
    public int PointQueryWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users WHERE Id = @id";
        var p = c.CreateParameter(); p.ParameterName = "@id"; c.Parameters.Add(p);

        for (int i = 0; i < 100; i++)
        {
            p.Value = m_randomIds[i];
            using var r = c.ExecuteReader();
            if (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Point Query by PK (100x) - SQLite")]
    public int PointQuerySqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Users WHERE Id = @id";
        var p = c.CreateParameter(); p.ParameterName = "@id"; c.Parameters.Add(p);

        for (int i = 0; i < 100; i++)
        {
            p.Value = m_randomIds[i];
            using var r = c.ExecuteReader();
            if (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "FindById (100x) - LiteDB")]
    public int PointQueryLiteDb()
    {
        int cnt = 0;
        for (int i = 0; i < 100; i++)
        {
            var doc = m_liteCollection!.FindById(m_randomIds[i]);
            if (doc != null) cnt++;
        }
        return cnt;
    }

    #endregion

    #region Benchmarks - Projection

    [Benchmark(Description = "SELECT Id, Name (projection) - WitDb")]
    public int SelectProjectionWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT Id, Name FROM Users";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "SELECT Id, Name (projection) - SQLite")]
    public int SelectProjectionSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT Id, Name FROM Users";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Select Id, Name (projection) - LiteDB")]
    public int SelectProjectionLiteDb()
    {
        int cnt = 0;
        foreach (var item in m_liteCollection!.FindAll().Select(x => new { x.Id, x.Name }))
            cnt++;
        return cnt;
    }

    #endregion

    #region IDisposable

    public void Dispose() => GlobalCleanup();

    #endregion
}
