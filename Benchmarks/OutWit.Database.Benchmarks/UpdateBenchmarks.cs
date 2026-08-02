using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Document for update benchmarks.
/// </summary>
public class UpdateDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public int Status { get; set; }
}

/// <summary>
/// Benchmarks for UPDATE statement performance.
/// Tests various update patterns against different WitDb modes, SQLite and LiteDB.
/// </summary>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class UpdateBenchmarks : IDisposable
{
    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private LiteDatabase? m_liteDb;
    private ILiteCollection<UpdateDoc>? m_liteCollection;
    private string m_witPath = null!;
    private string m_sqlitePath = null!;
    private string m_liteDbPath = null!;

    #endregion

    #region Parameters

    [ParamsSource(nameof(RowCountValues))]
    public int RowCount { get; set; }

    public IEnumerable<int> RowCountValues => BenchmarkSweep.Sizes(100, 1000);

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
            ? BenchmarkPathHelper.GenerateUniquePath("wit_update_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_update_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_update") + ".db";
        m_liteDbPath = BenchmarkPathHelper.GenerateUniquePath("lite_update") + ".db";
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
                    Status INT
                )";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_T_Status ON T(Status)";
            c.ExecuteNonQuery();
        }

        // Insert test data
        var tx = (WitDbTransaction)m_witConn.BeginTransaction();
        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO T (Name, Value, Status) VALUES (@n, @v, @s)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            var ps = c.CreateParameter(); ps.ParameterName = "@s"; c.Parameters.Add(ps);

            for (int i = 0; i < RowCount; i++)
            {
                pn.Value = $"Item_{i}";
                pv.Value = i * 1.5;
                ps.Value = i % 5;
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();

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
                    Status INTEGER
                )";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_T_Status ON T(Status)";
            c.ExecuteNonQuery();
        }

        var txS = m_sqliteConn.BeginTransaction();
        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = txS;
            c.CommandText = "INSERT INTO T (Name, Value, Status) VALUES (@n, @v, @s)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            var ps = c.CreateParameter(); ps.ParameterName = "@s"; c.Parameters.Add(ps);

            for (int i = 0; i < RowCount; i++)
            {
                pn.Value = $"Item_{i}";
                pv.Value = i * 1.5;
                ps.Value = i % 5;
                c.ExecuteNonQuery();
            }
        }
        txS.Commit();
        txS.Dispose();

        // LiteDB
        BenchmarkPathHelper.SafeCleanup(m_liteDbPath);
        m_liteDb = new LiteDatabase(m_liteDbPath);
        m_liteCollection = m_liteDb.GetCollection<UpdateDoc>("t");
        m_liteCollection.EnsureIndex(x => x.Status);

        var docs = new List<UpdateDoc>(RowCount);
        for (int i = 0; i < RowCount; i++)
        {
            docs.Add(new UpdateDoc
            {
                Id = i + 1,
                Name = $"Item_{i}",
                Value = i * 1.5,
                Status = i % 5
            });
        }
        m_liteCollection.InsertBulk(docs);
    }

    private void CleanupPaths()
    {
        BenchmarkPathHelper.SafeCleanup(m_witPath);
        BenchmarkPathHelper.SafeCleanup(m_witPath + "_indexes");
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

    #region Benchmarks - UPDATE by PK

    [Benchmark(Description = "UPDATE by PK in tx - WitDb")]
    public void UpdateByPkWitDb()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using var c = m_witConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "UPDATE T SET Value = @v WHERE Id = @i";

        var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);

        for (int i = 1; i <= RowCount; i++)
        {
            pi.Value = i;
            pv.Value = i * 2.5;
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
    [Benchmark(Description = "UPDATE by PK in tx - SQLite")]
    public void UpdateByPkSqlite()
    {
        var tx = m_sqliteConn!.BeginTransaction();
        using var c = m_sqliteConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "UPDATE T SET Value = @v WHERE Id = @i";

        var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);

        for (int i = 1; i <= RowCount; i++)
        {
            pi.Value = i;
            pv.Value = i * 2.5;
            c.ExecuteNonQuery();
        }
        tx.Commit();
        tx.Dispose();
    }

    [Benchmark(Description = "UPDATE by PK in tx - LiteDB")]
    public void UpdateByPkLiteDb()
    {
        m_liteDb!.BeginTrans();
        for (int i = 1; i <= RowCount; i++)
        {
            var doc = m_liteCollection!.FindById(i);
            if (doc != null)
            {
                doc.Value = i * 2.5;
                m_liteCollection.Update(doc);
            }
        }
        m_liteDb.Commit();
    }

    #endregion

    #region Benchmarks - UPDATE by Index

    [Benchmark(Description = "UPDATE by indexed column - WitDb")]
    public int UpdateByIndexWitDb()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using var c = m_witConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "UPDATE T SET Value = Value * 1.1 WHERE Status = @s";

        var ps = c.CreateParameter(); ps.ParameterName = "@s"; c.Parameters.Add(ps);

        int total = 0;
        for (int s = 0; s < 5; s++)
        {
            ps.Value = s;
            total += c.ExecuteNonQuery();
        }
        tx.Commit();
        tx.Dispose();
        return total;
    }

    [Benchmark(Description = "UPDATE by indexed column - SQLite")]
    public int UpdateByIndexSqlite()
    {
        var tx = m_sqliteConn!.BeginTransaction();
        using var c = m_sqliteConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "UPDATE T SET Value = Value * 1.1 WHERE Status = @s";

        var ps = c.CreateParameter(); ps.ParameterName = "@s"; c.Parameters.Add(ps);

        int total = 0;
        for (int s = 0; s < 5; s++)
        {
            ps.Value = s;
            total += c.ExecuteNonQuery();
        }
        tx.Commit();
        tx.Dispose();
        return total;
    }

    [Benchmark(Description = "UPDATE by indexed column - LiteDB")]
    public int UpdateByIndexLiteDb()
    {
        int total = 0;
        m_liteDb!.BeginTrans();
        for (int s = 0; s < 5; s++)
        {
            var docs = m_liteCollection!.Find(x => x.Status == s).ToList();
            foreach (var doc in docs)
            {
                doc.Value *= 1.1;
                m_liteCollection.Update(doc);
                total++;
            }
        }
        m_liteDb.Commit();
        return total;
    }

    #endregion

    #region Benchmarks - Bulk UPDATE

    [Benchmark(Description = "Bulk UPDATE (all rows) - WitDb")]
    public int BulkUpdateWitDb()
    {
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "UPDATE T SET Value = Value + 1";
        return c.ExecuteNonQuery();
    }

    [Benchmark(Description = "Bulk UPDATE (all rows) - SQLite")]
    public int BulkUpdateSqlite()
    {
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "UPDATE T SET Value = Value + 1";
        return c.ExecuteNonQuery();
    }

    [Benchmark(Description = "Bulk UPDATE (all rows) - LiteDB")]
    public int BulkUpdateLiteDb()
    {
        int total = 0;
        foreach (var doc in m_liteCollection!.FindAll())
        {
            doc.Value += 1;
            m_liteCollection.Update(doc);
            total++;
        }
        return total;
    }

    #endregion

    #region Benchmarks - UPDATE RETURNING

    [Benchmark(Description = "UPDATE RETURNING - WitDb")]
    public int UpdateReturningWitDb()
    {
        int cnt = 0;
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using var c = m_witConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "UPDATE T SET Value = @v WHERE Id = @i RETURNING Id, Value";

        var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);

        int count = Math.Min(100, RowCount);
        for (int i = 1; i <= count; i++)
        {
            pi.Value = i;
            pv.Value = i * 3.0;
            using var r = c.ExecuteReader();
            if (r.Read()) cnt++;
        }
        tx.Commit();
        tx.Dispose();
        return cnt;
    }

    [Benchmark(Description = "UPDATE RETURNING - SQLite")]
    public int UpdateReturningSqlite()
    {
        int cnt = 0;
        var tx = m_sqliteConn!.BeginTransaction();
        using var c = m_sqliteConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "UPDATE T SET Value = @v WHERE Id = @i RETURNING Id, Value";

        var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
        var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);

        int count = Math.Min(100, RowCount);
        for (int i = 1; i <= count; i++)
        {
            pi.Value = i;
            pv.Value = i * 3.0;
            using var r = c.ExecuteReader();
            if (r.Read()) cnt++;
        }
        tx.Commit();
        tx.Dispose();
        return cnt;
    }

    [Benchmark(Description = "Update + FindById - LiteDB")]
    public int UpdateReturningLiteDb()
    {
        int cnt = 0;
        m_liteDb!.BeginTrans();
        int count = Math.Min(100, RowCount);
        for (int i = 1; i <= count; i++)
        {
            var doc = m_liteCollection!.FindById(i);
            if (doc != null)
            {
                doc.Value = i * 3.0;
                m_liteCollection.Update(doc);
                cnt++;
            }
        }
        m_liteDb.Commit();
        return cnt;
    }

    #endregion

    #region IDisposable

    public void Dispose() => GlobalCleanup();

    #endregion
}
