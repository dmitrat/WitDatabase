using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Benchmarks for INSERT statement performance.
/// Tests single and bulk insert patterns against different WitDb modes, SQLite and LiteDB.
/// </summary>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class InsertBenchmarks : IDisposable
{
    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private LiteDatabase? m_liteDb;
    private ILiteCollection<BenchmarkDoc>? m_liteCollection;
    private string m_witPath = null!;
    private string m_sqlitePath = null!;
    private string m_liteDbPath = null!;

    #endregion

    #region Parameters

    [ParamsSource(nameof(RowCountValues))]
    public int RowCount { get; set; }

    public IEnumerable<int> RowCountValues => BenchmarkSweep.Sizes(100, 1000, 5000);

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
            ? BenchmarkPathHelper.GenerateUniquePath("wit_insert_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_insert_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_insert") + ".db";
        m_liteDbPath = BenchmarkPathHelper.GenerateUniquePath("lite_insert") + ".db";
    }

    [IterationSetup]
    public void IterationSetup()
    {
        CleanupPaths();

        // WitDb
        var connStr = WitDbConnectionHelper.BuildConnectionString(m_witPath, EngineMode);
        m_witConn = new WitDbConnection(connStr);
        m_witConn.Open();

        using (var c = m_witConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS T";
            c.ExecuteNonQuery();
            c.CommandText = @"
                CREATE TABLE T (
                    Id BIGINT PRIMARY KEY AUTOINCREMENT,
                    Name VARCHAR(100),
                    Value DOUBLE,
                    CreatedAt DATETIME
                )";
            c.ExecuteNonQuery();
        }

        // SQLite
        m_sqliteConn = new SqliteConnection($"Data Source={m_sqlitePath}");
        m_sqliteConn.Open();

        using (var c = m_sqliteConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS T";
            c.ExecuteNonQuery();
            c.CommandText = @"
                CREATE TABLE T (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT,
                    Value REAL,
                    CreatedAt TEXT
                )";
            c.ExecuteNonQuery();
        }

        // LiteDB
        BenchmarkPathHelper.SafeCleanup(m_liteDbPath);
        m_liteDb = new LiteDatabase(m_liteDbPath);
        m_liteCollection = m_liteDb.GetCollection<BenchmarkDoc>("t");
    }

    private void CleanupPaths()
    {
        BenchmarkPathHelper.SafeCleanup(m_witPath);
        BenchmarkPathHelper.SafeCleanup(m_sqlitePath);
        BenchmarkPathHelper.SafeCleanup(m_liteDbPath);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        m_witConn?.Dispose(); m_witConn = null;
        m_sqliteConn?.Dispose(); m_sqliteConn = null;
        m_liteDb?.Dispose(); m_liteDb = null;
        m_liteCollection = null;

        CleanupPaths();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => IterationCleanup();

    #endregion

    #region Benchmarks - Single INSERT in Transaction

    [Benchmark(Description = "INSERT in transaction - WitDb")]
    public void InsertInTxWitDb()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using var c = m_witConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO T (Name, Value, CreatedAt) VALUES (@n, @v, @d)";

        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
        var pd = c.CreateParameter(); pd.ParameterName = "@d"; c.Parameters.Add(pd);

        var now = DateTime.UtcNow;
        for (int i = 0; i < RowCount; i++)
        {
            pn.Value = $"Item_{i}";
            pv.Value = i * 1.5;
            pd.Value = now;
            c.ExecuteNonQuery();
        }
        tx.Commit();
        tx.Dispose();
    }

    // No Baseline here on purpose. BenchmarkDotNet allows one baseline per class unless the
    // benchmarks are split into categories, so a single [Benchmark(Baseline = true)] made the Ratio
    // column compare every operation in the class against one unrelated operation - the January
    // report rated a 20-iteration seek "2.74x faster" than a 100-iteration one. Until the classes
    // carry [BenchmarkCategory] the honest report has no Ratio column at all; ratios are computed
    // per operation from the Mean column instead.
    [Benchmark(Description = "INSERT in transaction - SQLite")]
    public void InsertInTxSqlite()
    {
        var tx = m_sqliteConn!.BeginTransaction();
        using var c = m_sqliteConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO T (Name, Value, CreatedAt) VALUES (@n, @v, @d)";

        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
        var pd = c.CreateParameter(); pd.ParameterName = "@d"; c.Parameters.Add(pd);

        var now = DateTime.UtcNow;
        for (int i = 0; i < RowCount; i++)
        {
            pn.Value = $"Item_{i}";
            pv.Value = i * 1.5;
            pd.Value = now.ToString("o");
            c.ExecuteNonQuery();
        }
        tx.Commit();
        tx.Dispose();
    }

    [Benchmark(Description = "INSERT in transaction - LiteDB")]
    public void InsertInTxLiteDb()
    {
        m_liteDb!.BeginTrans();
        var now = DateTime.UtcNow;
        for (int i = 0; i < RowCount; i++)
        {
            m_liteCollection!.Insert(new BenchmarkDoc
            {
                Name = $"Item_{i}",
                Value = i * 1.5,
                CreatedAt = now
            });
        }
        m_liteDb.Commit();
    }

    #endregion

    #region Benchmarks - INSERT without Transaction

    [Benchmark(Description = "INSERT no transaction (100 rows) - WitDb")]
    public void InsertNoTxWitDb()
    {
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "INSERT INTO T (Name, Value, CreatedAt) VALUES (@n, @v, @d)";

        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
        var pd = c.CreateParameter(); pd.ParameterName = "@d"; c.Parameters.Add(pd);

        var now = DateTime.UtcNow;
        int count = Math.Min(100, RowCount);
        for (int i = 0; i < count; i++)
        {
            pn.Value = $"Item_{i}";
            pv.Value = i * 1.5;
            pd.Value = now;
            c.ExecuteNonQuery();
        }
    }

    [Benchmark(Description = "INSERT no transaction (100 rows) - SQLite")]
    public void InsertNoTxSqlite()
    {
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "INSERT INTO T (Name, Value, CreatedAt) VALUES (@n, @v, @d)";

        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
        var pd = c.CreateParameter(); pd.ParameterName = "@d"; c.Parameters.Add(pd);

        var now = DateTime.UtcNow;
        int count = Math.Min(100, RowCount);
        for (int i = 0; i < count; i++)
        {
            pn.Value = $"Item_{i}";
            pv.Value = i * 1.5;
            pd.Value = now.ToString("o");
            c.ExecuteNonQuery();
        }
    }

    [Benchmark(Description = "INSERT no transaction (100 rows) - LiteDB")]
    public void InsertNoTxLiteDb()
    {
        var now = DateTime.UtcNow;
        int count = Math.Min(100, RowCount);
        for (int i = 0; i < count; i++)
        {
            m_liteCollection!.Insert(new BenchmarkDoc
            {
                Name = $"Item_{i}",
                Value = i * 1.5,
                CreatedAt = now
            });
        }
    }

    #endregion

    #region Benchmarks - INSERT RETURNING / Bulk

    [Benchmark(Description = "INSERT RETURNING - WitDb")]
    public void InsertReturningWitDb()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using var c = m_witConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO T (Name, Value) VALUES (@n, @v) RETURNING Id";

        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);

        int count = Math.Min(500, RowCount);
        for (int i = 0; i < count; i++)
        {
            pn.Value = $"Item_{i}";
            pv.Value = i * 1.5;
            c.ExecuteScalar();
        }
        tx.Commit();
        tx.Dispose();
    }

    [Benchmark(Description = "INSERT RETURNING - SQLite")]
    public void InsertReturningSqlite()
    {
        var tx = m_sqliteConn!.BeginTransaction();
        using var c = m_sqliteConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO T (Name, Value) VALUES (@n, @v) RETURNING Id";

        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);

        int count = Math.Min(500, RowCount);
        for (int i = 0; i < count; i++)
        {
            pn.Value = $"Item_{i}";
            pv.Value = i * 1.5;
            c.ExecuteScalar();
        }
        tx.Commit();
        tx.Dispose();
    }

    [Benchmark(Description = "InsertBulk - LiteDB")]
    public void InsertBulkLiteDb()
    {
        var now = DateTime.UtcNow;
        var docs = new List<BenchmarkDoc>(RowCount);
        for (int i = 0; i < RowCount; i++)
        {
            docs.Add(new BenchmarkDoc
            {
                Name = $"Item_{i}",
                Value = i * 1.5,
                CreatedAt = now
            });
        }
        m_liteCollection!.InsertBulk(docs);
    }

    #endregion

    #region IDisposable

    public void Dispose() => GlobalCleanup();

    #endregion
}
