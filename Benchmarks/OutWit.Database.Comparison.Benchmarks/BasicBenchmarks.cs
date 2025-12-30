using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Comparison.Benchmarks;

public class ComparisonBenchmarkConfig : ManualConfig
{
    public ComparisonBenchmarkConfig()
    {
        SummaryStyle = SummaryStyle.Default
            .WithRatioStyle(RatioStyle.Trend)
            .WithTimeUnit(Perfolizer.Horology.TimeUnit.Millisecond);
        HideColumns(Column.Error, Column.StdDev, Column.RatioSD);
    }
}

public enum DatabaseType { WitDb, SQLite, LiteDB }

#region Insert Benchmarks

[Config(typeof(ComparisonBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class InsertBenchmarks : IDisposable
{
    private WitDbConnection _witConn = null!;
    private SqliteConnection _sqliteConn = null!;
    private LiteDatabase _liteDb = null!;
    private string _witPath = null!;
    private string _sqlitePath = null!;
    private string _liteDbPath = null!;

    [Params(100, 1000, 10000)]
    public int RowCount { get; set; }

    [Params(DatabaseType.WitDb, DatabaseType.SQLite, DatabaseType.LiteDB)]
    public DatabaseType Database { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _witPath = Path.Combine(Path.GetTempPath(), $"wit_{Guid.NewGuid():N}");
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"sql_{Guid.NewGuid():N}.db");
        _liteDbPath = Path.Combine(Path.GetTempPath(), $"lite_{Guid.NewGuid():N}.db");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // WitDb with LSM-Tree storage via connection string
        // IMPORTANT: 
        // - MVCC=false for better INSERT performance (no version tracking overhead)
        // - SyncWrites=false to disable fsync per write (major performance impact!)
        _witConn = new WitDbConnection($"Data Source={_witPath};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false");
        _witConn.Open();
        using (var c = _witConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS T"; c.ExecuteNonQuery();
            // Use AUTOINCREMENT for fair comparison (SQLite INTEGER PRIMARY KEY is auto-increment)
            c.CommandText = "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, N VARCHAR(100), V DOUBLE)"; 
            c.ExecuteNonQuery();
        }

        // SQLite
        _sqliteConn = new SqliteConnection($"Data Source={_sqlitePath}");
        _sqliteConn.Open();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS T"; c.ExecuteNonQuery();
            c.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY, N TEXT, V REAL)"; c.ExecuteNonQuery();
        }

        // LiteDB
        _liteDb = new LiteDatabase(_liteDbPath);
        var col = _liteDb.GetCollection<BsonDocument>("T");
        col.DeleteAll();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _witConn?.Dispose();
        _sqliteConn?.Dispose();
        _liteDb?.Dispose();
        try { Directory.Delete(_witPath, true); } catch { }
        try { File.Delete(_sqlitePath); } catch { }
        try { File.Delete(_liteDbPath); } catch { }
    }

    [GlobalCleanup]
    public void GlobalCleanup() => IterationCleanup();

    [Benchmark(Description = "INSERT in tx (auto PK)", Baseline = true)]
    public void InsertInTxAutoPk()
    {
        if (Database == DatabaseType.WitDb)
        {
            var tx = (WitDbTransaction)_witConn.BeginTransaction();
            using var c = _witConn.CreateCommand();
            c.Transaction = tx;
            // Don't specify Id - let AUTOINCREMENT generate it
            c.CommandText = "INSERT INTO T (N, V) VALUES (@n, @v)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 0; i < RowCount; i++)
            {
                pn.Value = $"I{i}";
                pv.Value = i * 1.5;
                c.ExecuteNonQuery();
            }
            tx.Commit();
            tx.Dispose();
        }
        else if (Database == DatabaseType.SQLite)
        {
            var tx = _sqliteConn.BeginTransaction();
            using var c = _sqliteConn.CreateCommand();
            c.Transaction = tx;
            // SQLite auto-generates Id when not specified
            c.CommandText = "INSERT INTO T (N, V) VALUES (@n, @v)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 0; i < RowCount; i++)
            {
                pn.Value = $"I{i}";
                pv.Value = i * 1.5;
                c.ExecuteNonQuery();
            }
            tx.Commit();
            tx.Dispose();
        }
        else // LiteDB
        {
            var col = _liteDb.GetCollection<BsonDocument>("T");
            _liteDb.BeginTrans();
            for (int i = 0; i < RowCount; i++)
            {
                var doc = new BsonDocument
                {
                    ["N"] = $"I{i}",
                    ["V"] = i * 1.5
                };
                col.Insert(doc); // Auto-generates _id
            }
            _liteDb.Commit();
        }
    }

    public void Dispose() => IterationCleanup();
}

#endregion

#region Insert with Explicit PK Benchmarks

[Config(typeof(ComparisonBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class InsertExplicitPkBenchmarks : IDisposable
{
    private WitDbConnection _witConn = null!;
    private SqliteConnection _sqliteConn = null!;
    private LiteDatabase _liteDb = null!;
    private string _witPath = null!;
    private string _sqlitePath = null!;
    private string _liteDbPath = null!;

    [Params(100, 1000)]
    public int RowCount { get; set; }

    [Params(DatabaseType.WitDb, DatabaseType.SQLite, DatabaseType.LiteDB)]
    public DatabaseType Database { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _witPath = Path.Combine(Path.GetTempPath(), $"wit_{Guid.NewGuid():N}");
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"sql_{Guid.NewGuid():N}.db");
        _liteDbPath = Path.Combine(Path.GetTempPath(), $"lite_{Guid.NewGuid():N}.db");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // WitDb with LSM-Tree and UNIQUE index on PK
        // MVCC=false and SyncWrites=false for better INSERT performance
        _witConn = new WitDbConnection($"Data Source={_witPath};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false");
        _witConn.Open();
        using (var c = _witConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS T"; c.ExecuteNonQuery();
            c.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY, N VARCHAR(100), V DOUBLE)"; 
            c.ExecuteNonQuery();
            // Create UNIQUE index for fast uniqueness validation
            c.CommandText = "CREATE UNIQUE INDEX IX_T_Id ON T(Id)";
            c.ExecuteNonQuery();
        }

        // SQLite
        _sqliteConn = new SqliteConnection($"Data Source={_sqlitePath}");
        _sqliteConn.Open();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS T"; c.ExecuteNonQuery();
            c.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY, N TEXT, V REAL)"; c.ExecuteNonQuery();
        }

        // LiteDB
        _liteDb = new LiteDatabase(_liteDbPath);
        var col = _liteDb.GetCollection<BsonDocument>("T");
        col.DeleteAll();
        col.EnsureIndex("Id", unique: true);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _witConn?.Dispose();
        _sqliteConn?.Dispose();
        _liteDb?.Dispose();
        try { Directory.Delete(_witPath, true); } catch { }
        try { File.Delete(_sqlitePath); } catch { }
        try { File.Delete(_liteDbPath); } catch { }
    }

    [GlobalCleanup]
    public void GlobalCleanup() => IterationCleanup();

    [Benchmark(Description = "INSERT explicit PK in tx", Baseline = true)]
    public void InsertExplicitPkInTx()
    {
        if (Database == DatabaseType.WitDb)
        {
            var tx = (WitDbTransaction)_witConn.BeginTransaction();
            using var c = _witConn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT INTO T (Id, N, V) VALUES (@i, @n, @v)";
            var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 0; i < RowCount; i++)
            {
                pi.Value = i;
                pn.Value = $"I{i}";
                pv.Value = i * 1.5;
                c.ExecuteNonQuery();
            }
            tx.Commit();
            tx.Dispose();
        }
        else if (Database == DatabaseType.SQLite)
        {
            var tx = _sqliteConn.BeginTransaction();
            using var c = _sqliteConn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT INTO T (Id, N, V) VALUES (@i, @n, @v)";
            var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 0; i < RowCount; i++)
            {
                pi.Value = i;
                pn.Value = $"I{i}";
                pv.Value = i * 1.5;
                c.ExecuteNonQuery();
            }
            tx.Commit();
            tx.Dispose();
        }
        else // LiteDB
        {
            var col = _liteDb.GetCollection<BsonDocument>("T");
            _liteDb.BeginTrans();
            for (int i = 0; i < RowCount; i++)
            {
                var doc = new BsonDocument
                {
                    ["_id"] = i, // Explicit _id
                    ["Id"] = i,
                    ["N"] = $"I{i}",
                    ["V"] = i * 1.5
                };
                col.Insert(doc);
            }
            _liteDb.Commit();
        }
    }

    public void Dispose() => IterationCleanup();
}

#endregion

#region Select Benchmarks

[Config(typeof(ComparisonBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SelectBenchmarks : IDisposable
{
    private WitDbConnection _witConn = null!;
    private SqliteConnection _sqliteConn = null!;
    private LiteDatabase _liteDb = null!;
    private string _witPath = null!;
    private string _sqlitePath = null!;
    private string _liteDbPath = null!;
    private int[] _ids = null!;

    [Params(1000, 10000)]
    public int TableSize { get; set; }

    [Params(DatabaseType.WitDb, DatabaseType.SQLite, DatabaseType.LiteDB)]
    public DatabaseType Database { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _witPath = Path.Combine(Path.GetTempPath(), $"wit_{Guid.NewGuid():N}");
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"sql_{Guid.NewGuid():N}.db");
        _liteDbPath = Path.Combine(Path.GetTempPath(), $"lite_{Guid.NewGuid():N}.db");

        // WitDb with LSM-Tree, optimized settings for setup
        _witConn = new WitDbConnection($"Data Source={_witPath};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false");
        _witConn.Open();
        using (var c = _witConn.CreateCommand())
        {
            c.CommandText = "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, N VARCHAR(100), V DOUBLE)";
            c.ExecuteNonQuery();
        }
        var txW = (WitDbTransaction)_witConn.BeginTransaction();
        using (var c = _witConn.CreateCommand())
        {
            c.Transaction = txW;
            c.CommandText = "INSERT INTO T (N, V) VALUES (@n, @v)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 0; i < TableSize; i++)
            {
                pn.Value = $"I{i}";
                pv.Value = i * 1.5;
                c.ExecuteNonQuery();
            }
        }
        txW.Commit();
        txW.Dispose();
        // Create index for point queries
        using (var c = _witConn.CreateCommand())
        {
            c.CommandText = "CREATE INDEX IX_T_Id ON T(Id)";
            c.ExecuteNonQuery();
        }

        // SQLite
        _sqliteConn = new SqliteConnection($"Data Source={_sqlitePath}");
        _sqliteConn.Open();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY, N TEXT, V REAL)";
            c.ExecuteNonQuery();
        }
        var txS = _sqliteConn.BeginTransaction();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.Transaction = txS;
            c.CommandText = "INSERT INTO T (N, V) VALUES (@n, @v)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 0; i < TableSize; i++)
            {
                pn.Value = $"I{i}";
                pv.Value = i * 1.5;
                c.ExecuteNonQuery();
            }
        }
        txS.Commit();
        txS.Dispose();

        // LiteDB
        _liteDb = new LiteDatabase(_liteDbPath);
        var col = _liteDb.GetCollection<BsonDocument>("T");
        for (int i = 0; i < TableSize; i++)
        {
            col.Insert(new BsonDocument
            {
                ["Id"] = i,
                ["N"] = $"I{i}",
                ["V"] = i * 1.5
            });
        }
        col.EnsureIndex("Id");

        var rnd = new Random(42);
        _ids = Enumerable.Range(0, 1000).Select(_ => rnd.Next(1, TableSize + 1)).ToArray();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _witConn?.Dispose();
        _sqliteConn?.Dispose();
        _liteDb?.Dispose();
        try { Directory.Delete(_witPath, true); } catch { }
        try { File.Delete(_sqlitePath); } catch { }
        try { File.Delete(_liteDbPath); } catch { }
    }

    [Benchmark(Description = "Point Query 1000x")]
    public int PointQuery()
    {
        int cnt = 0;
        if (Database == DatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = "SELECT Id, N, V FROM T WHERE Id = @i";
            var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
            foreach (var id in _ids)
            {
                pi.Value = id;
                using var r = c.ExecuteReader();
                if (r.Read()) cnt++;
            }
        }
        else if (Database == DatabaseType.SQLite)
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = "SELECT Id, N, V FROM T WHERE Id = @i";
            var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
            foreach (var id in _ids)
            {
                pi.Value = id;
                using var r = c.ExecuteReader();
                if (r.Read()) cnt++;
            }
        }
        else // LiteDB
        {
            var col = _liteDb.GetCollection<BsonDocument>("T");
            foreach (var id in _ids)
            {
                var doc = col.FindOne(x => x["Id"] == id);
                if (doc != null) cnt++;
            }
        }
        return cnt;
    }

    [Benchmark(Description = "Full Scan")]
    public int FullScan()
    {
        int cnt = 0;
        if (Database == DatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = "SELECT * FROM T";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else if (Database == DatabaseType.SQLite)
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = "SELECT * FROM T";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else // LiteDB
        {
            var col = _liteDb.GetCollection<BsonDocument>("T");
            foreach (var doc in col.FindAll())
            {
                cnt++;
            }
        }
        return cnt;
    }

    [Benchmark(Description = "Aggregation COUNT")]
    public long AggregationCount()
    {
        if (Database == DatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = "SELECT COUNT(*) FROM T";
            return Convert.ToInt64(c.ExecuteScalar());
        }
        else if (Database == DatabaseType.SQLite)
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = "SELECT COUNT(*) FROM T";
            return Convert.ToInt64(c.ExecuteScalar());
        }
        else // LiteDB
        {
            var col = _liteDb.GetCollection<BsonDocument>("T");
            return col.Count();
        }
    }

    [Benchmark(Description = "Aggregation SUM")]
    public double AggregationSum()
    {
        if (Database == DatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = "SELECT SUM(V) FROM T";
            return Convert.ToDouble(c.ExecuteScalar());
        }
        else if (Database == DatabaseType.SQLite)
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = "SELECT SUM(V) FROM T";
            return Convert.ToDouble(c.ExecuteScalar());
        }
        else // LiteDB - no built-in SUM, must iterate
        {
            var col = _liteDb.GetCollection<BsonDocument>("T");
            double sum = 0;
            foreach (var doc in col.FindAll())
            {
                sum += doc["V"].AsDouble;
            }
            return sum;
        }
    }

    public void Dispose() => GlobalCleanup();
}

#endregion

#region Update/Delete Benchmarks

[Config(typeof(ComparisonBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class UpdateDeleteBenchmarks : IDisposable
{
    private WitDbConnection _witConn = null!;
    private SqliteConnection _sqliteConn = null!;
    private LiteDatabase _liteDb = null!;
    private string _witPath = null!;
    private string _sqlitePath = null!;
    private string _liteDbPath = null!;

    [Params(100, 1000)]
    public int RowCount { get; set; }

    [Params(DatabaseType.WitDb, DatabaseType.SQLite, DatabaseType.LiteDB)]
    public DatabaseType Database { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _witPath = Path.Combine(Path.GetTempPath(), $"wit_{Guid.NewGuid():N}");
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"sql_{Guid.NewGuid():N}.db");
        _liteDbPath = Path.Combine(Path.GetTempPath(), $"lite_{Guid.NewGuid():N}.db");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // WitDb with LSM-Tree, optimized settings
        _witConn = new WitDbConnection($"Data Source={_witPath};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false");
        _witConn.Open();
        using (var c = _witConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS T"; c.ExecuteNonQuery();
            c.CommandText = "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, N VARCHAR(100), V DOUBLE)"; 
            c.ExecuteNonQuery();
        }
        var txW = (WitDbTransaction)_witConn.BeginTransaction();
        using (var c = _witConn.CreateCommand())
        {
            c.Transaction = txW;
            c.CommandText = "INSERT INTO T (N, V) VALUES (@n, @v)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 0; i < RowCount; i++)
            {
                pn.Value = $"N{i}"; pv.Value = i * 1.5;
                c.ExecuteNonQuery();
            }
        }
        txW.Commit();
        txW.Dispose();

        // SQLite
        _sqliteConn = new SqliteConnection($"Data Source={_sqlitePath}");
        _sqliteConn.Open();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS T"; c.ExecuteNonQuery();
            c.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY, N TEXT, V REAL)"; c.ExecuteNonQuery();
        }
        var txS = _sqliteConn.BeginTransaction();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.Transaction = txS;
            c.CommandText = "INSERT INTO T (N, V) VALUES (@n, @v)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 0; i < RowCount; i++)
            {
                pn.Value = $"N{i}"; pv.Value = i * 1.5;
                c.ExecuteNonQuery();
            }
        }
        txS.Commit();
        txS.Dispose();

        // LiteDB
        _liteDb = new LiteDatabase(_liteDbPath);
        var col = _liteDb.GetCollection<BsonDocument>("T");
        col.DeleteAll();
        for (int i = 0; i < RowCount; i++)
        {
            col.Insert(new BsonDocument
            {
                ["Id"] = i,
                ["N"] = $"N{i}",
                ["V"] = i * 1.5
            });
        }
        col.EnsureIndex("Id");
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _witConn?.Dispose();
        _sqliteConn?.Dispose();
        _liteDb?.Dispose();
        try { Directory.Delete(_witPath, true); } catch { }
        try { File.Delete(_sqlitePath); } catch { }
        try { File.Delete(_liteDbPath); } catch { }
    }

    [GlobalCleanup]
    public void GlobalCleanup() => IterationCleanup();

    [Benchmark(Description = "UPDATE by PK in tx")]
    public void UpdateByPkInTx()
    {
        if (Database == DatabaseType.WitDb)
        {
            var tx = (WitDbTransaction)_witConn.BeginTransaction();
            using var c = _witConn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "UPDATE T SET V = @v WHERE Id = @i";
            var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 1; i <= RowCount; i++)
            {
                pi.Value = i;
                pv.Value = i * 2.0;
                c.ExecuteNonQuery();
            }
            tx.Commit();
            tx.Dispose();
        }
        else if (Database == DatabaseType.SQLite)
        {
            var tx = _sqliteConn.BeginTransaction();
            using var c = _sqliteConn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "UPDATE T SET V = @v WHERE Id = @i";
            var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
            var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
            for (int i = 1; i <= RowCount; i++)
            {
                pi.Value = i;
                pv.Value = i * 2.0;
                c.ExecuteNonQuery();
            }
            tx.Commit();
            tx.Dispose();
        }
        else // LiteDB
        {
            var col = _liteDb.GetCollection<BsonDocument>("T");
            _liteDb.BeginTrans();
            for (int i = 0; i < RowCount; i++)
            {
                var doc = col.FindOne(x => x["Id"] == i);
                if (doc != null)
                {
                    doc["V"] = i * 2.0;
                    col.Update(doc);
                }
            }
            _liteDb.Commit();
        }
    }

    [Benchmark(Description = "DELETE by PK in tx")]
    public void DeleteByPkInTx()
    {
        if (Database == DatabaseType.WitDb)
        {
            var tx = (WitDbTransaction)_witConn.BeginTransaction();
            using var c = _witConn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "DELETE FROM T WHERE Id = @i";
            var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
            for (int i = 1; i <= RowCount / 2; i++)
            {
                pi.Value = i;
                c.ExecuteNonQuery();
            }
            tx.Commit();
            tx.Dispose();
        }
        else if (Database == DatabaseType.SQLite)
        {
            var tx = _sqliteConn.BeginTransaction();
            using var c = _sqliteConn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "DELETE FROM T WHERE Id = @i";
            var pi = c.CreateParameter(); pi.ParameterName = "@i"; c.Parameters.Add(pi);
            for (int i = 1; i <= RowCount / 2; i++)
            {
                pi.Value = i;
                c.ExecuteNonQuery();
            }
            tx.Commit();
            tx.Dispose();
        }
        else // LiteDB
        {
            var col = _liteDb.GetCollection<BsonDocument>("T");
            _liteDb.BeginTrans();
            for (int i = 0; i < RowCount / 2; i++)
            {
                col.DeleteMany(x => x["Id"] == i);
            }
            _liteDb.Commit();
        }
    }

    public void Dispose() => GlobalCleanup();
}

#endregion
