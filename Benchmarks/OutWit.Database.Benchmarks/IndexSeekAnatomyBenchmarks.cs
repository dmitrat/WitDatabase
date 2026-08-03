using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Takes the unique-index seek apart, one suspect at a time.
/// </summary>
/// <remarks>
/// The phase 10 baseline found two ways to fetch one row by an indexed equality predicate that
/// differ by roughly 200x in the same engine, same configuration, same run:
///
///   WHERE Id  = @id   (primary key)          0.0025 ms,     5.4 KB per lookup
///   WHERE SKU = @sku  (UNIQUE secondary)     0.489  ms, 1,253    KB per lookup
///
/// The cost is flat in table size - 48.85 ms at 5,000 rows and 49.02 ms at 20,000, allocating
/// 125,327 KB at both - so it is a fixed per-seek cost and not a scan.
///
/// Every shape below fetches exactly one row out of the same table, 100 times, from the same seeded
/// sequence of keys, so the shapes differ only in the one property each is named for. Nothing here
/// is a cross-engine claim: the comparison that matters is WitDatabase against itself, which no
/// caveat about P/Invoke or document stores touches. SQLite is carried alongside purely as a control
/// - if a shape is expensive there too, the cost is inherent to the shape rather than to this engine.
///
/// What each shape rules in or out:
///
///   PK equality                  the known-good path, the floor
///   UNIQUE index, string key     the known-bad path, the subject
///   UNIQUE index, int key        is the cost the string key, or the secondary index?
///   Non-unique index, string key is the cost uniqueness, or being a secondary index at all?
///   UNIQUE index, narrow proj.   is the cost materialising the row, or finding it?
///   UNIQUE index, PK projection  same question, asking only for the key the index already holds
///   PK equality, narrow proj.    the projection control on the path that is already fast
///   No index, forced scan        does the "seek" actually cost less than the scan it replaces?
/// </remarks>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class IndexSeekAnatomyBenchmarks : IDisposable
{
    private const int Lookups = 100;

    /// <summary>
    /// Distinct values in the Bucket column. Chosen to match the shape of Users.Age, the column
    /// behind the unexplained 405 ms at 100,000 rows: an indexed integer with a few dozen distinct
    /// values, so its index is heavily non-unique.
    /// </summary>
    private const int BucketCount = 60;

    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private string m_witPath = null!;
    private string m_sqlitePath = null!;

    #endregion

    #region Parameters

    [ParamsSource(nameof(TableSizeValues))]
    public int TableSize { get; set; }

    public IEnumerable<int> TableSizeValues => BenchmarkSweep.Sizes(250, 500, 1000, 2000, 5000, 20000, 100000);

    [ParamsSource(nameof(EngineModeValues))]
    public WitDbEngineMode EngineMode { get; set; }

    public IEnumerable<WitDbEngineMode> EngineModeValues => BenchmarkSweep.Modes(
        WitDbEngineMode.Default, WitDbEngineMode.Memory, WitDbEngineMode.BTree, WitDbEngineMode.Lsm,
        WitDbEngineMode.BTreeParallelAuto, WitDbEngineMode.LsmParallelAuto);

    #endregion

    #region Setup/Cleanup

    [GlobalSetup]
    public void GlobalSetup()
    {
        var isLsm = EngineMode is WitDbEngineMode.Lsm or WitDbEngineMode.LsmParallelAuto;
        m_witPath = isLsm
            ? BenchmarkPathHelper.GenerateUniquePath("wit_anat_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_anat_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_anat") + ".db";

        Cleanup();
        SetupWitDb();
        SetupSqlite();
    }

    private void Cleanup()
    {
        BenchmarkPathHelper.SafeCleanup(m_witPath);
        BenchmarkPathHelper.SafeCleanup(m_witPath + "_indexes");
        BenchmarkPathHelper.SafeCleanup(m_sqlitePath);
    }

    private void SetupWitDb()
    {
        m_witConn = new WitDbConnection(WitDbConnectionHelper.BuildConnectionString(m_witPath, EngineMode));
        m_witConn.Open();

        using (var c = m_witConn.CreateCommand())
        {
            c.CommandText = @"
                CREATE TABLE Products (
                    Id BIGINT PRIMARY KEY AUTOINCREMENT,
                    SKU VARCHAR(50) NOT NULL,
                    AltInt INT NOT NULL,
                    AltStr VARCHAR(50) NOT NULL,
                    Name VARCHAR(200),
                    Price DOUBLE,
                    CategoryId INT,
                    Bucket INT NOT NULL
                )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE UNIQUE INDEX IX_Anat_SKU ON Products(SKU)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE UNIQUE INDEX IX_Anat_AltInt ON Products(AltInt)";
            c.ExecuteNonQuery();
            // Deliberately NOT unique, over values that happen to be distinct: selectivity is held
            // constant so the only thing that varies is the index's uniqueness.
            c.CommandText = "CREATE INDEX IX_Anat_AltStr ON Products(AltStr)";
            c.ExecuteNonQuery();
            // Low cardinality on purpose - about 60 distinct values, so many rows share a key.
            // This is the one property the refuted range experiment did not hold constant:
            // Users.Age, the column that produced the unexplained 405 ms, is shaped like this,
            // while AltInt above is unique.
            c.CommandText = "CREATE INDEX IX_Anat_Bucket ON Products(Bucket)";
            c.ExecuteNonQuery();
            // Name is deliberately left unindexed - it is the forced-scan shape.
        }

        var tx = (WitDbTransaction)m_witConn.BeginTransaction();
        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO Products (SKU, AltInt, AltStr, Name, Price, CategoryId, Bucket)
                              VALUES (@sku, @alti, @alts, @name, @price, @cat, @bucket)";

            var pSku = Add(c, "@sku");
            var pAltI = Add(c, "@alti");
            var pAltS = Add(c, "@alts");
            var pName = Add(c, "@name");
            var pPrice = Add(c, "@price");
            var pCat = Add(c, "@cat");
            var pBucket = Add(c, "@bucket");

            var rnd = new Random(42);
            for (int i = 0; i < TableSize; i++)
            {
                pSku.Value = $"SKU-{i:D8}";
                pAltI.Value = i;
                pAltS.Value = $"ALT-{i:D8}";
                pName.Value = $"Product {i}";
                pPrice.Value = Math.Round(rnd.NextDouble() * 1000, 2);
                pCat.Value = rnd.Next(1, 21);
                pBucket.Value = i % BucketCount;
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    private static System.Data.Common.DbParameter Add(System.Data.Common.DbCommand c, string name)
    {
        var p = c.CreateParameter();
        p.ParameterName = name;
        c.Parameters.Add(p);
        return p;
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
                    AltInt INTEGER NOT NULL,
                    AltStr TEXT NOT NULL,
                    Name TEXT,
                    Price REAL,
                    CategoryId INTEGER,
                    Bucket INTEGER NOT NULL
                )";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE UNIQUE INDEX IX_Anat_SKU ON Products(SKU)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE UNIQUE INDEX IX_Anat_AltInt ON Products(AltInt)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Anat_AltStr ON Products(AltStr)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Anat_Bucket ON Products(Bucket)";
            c.ExecuteNonQuery();
        }

        var tx = m_sqliteConn.BeginTransaction();
        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO Products (SKU, AltInt, AltStr, Name, Price, CategoryId, Bucket)
                              VALUES (@sku, @alti, @alts, @name, @price, @cat, @bucket)";

            var pSku = Add(c, "@sku");
            var pAltI = Add(c, "@alti");
            var pAltS = Add(c, "@alts");
            var pName = Add(c, "@name");
            var pPrice = Add(c, "@price");
            var pCat = Add(c, "@cat");
            var pBucket = Add(c, "@bucket");

            var rnd = new Random(42);
            for (int i = 0; i < TableSize; i++)
            {
                pSku.Value = $"SKU-{i:D8}";
                pAltI.Value = i;
                pAltS.Value = $"ALT-{i:D8}";
                pName.Value = $"Product {i}";
                pPrice.Value = Math.Round(rnd.NextDouble() * 1000, 2);
                pCat.Value = rnd.Next(1, 21);
                pBucket.Value = i % BucketCount;
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        m_witConn?.Close();
        m_witConn?.Dispose();
        m_witConn = null;
        m_sqliteConn?.Close();
        m_sqliteConn?.Dispose();
        m_sqliteConn = null;
        SqliteConnection.ClearAllPools();
        Cleanup();
    }

    #endregion

    #region Harness

    /// <summary>
    /// Runs the same lookup <see cref="Lookups"/> times against the given connection and returns how
    /// many of them found a row. Every shape goes through this, so the shapes differ only in their
    /// SQL and their parameter value - and the returned count makes each one verifiable: a shape
    /// that quietly stops finding rows would otherwise simply benchmark as fast.
    /// </summary>
    private int Probe(System.Data.Common.DbConnection connection, string sql, Func<int, object> key)
    {
        var found = 0;
        using var c = connection.CreateCommand();
        c.CommandText = sql;
        var p = Add(c, "@key");

        var rnd = new Random(42);
        for (int i = 0; i < Lookups; i++)
        {
            p.Value = key(rnd.Next(0, TableSize));
            using var r = c.ExecuteReader();
            if (r.Read())
                found++;
        }

        return found;
    }

    private static object Sku(int id) => $"SKU-{id:D8}";
    private static object Alt(int id) => $"ALT-{id:D8}";
    private static object Name(int id) => $"Product {id}";

    #endregion

    #region Benchmarks

    [Benchmark(Description = "PK equality x100 - WitDb")]
    public int PkWitDb() =>
        Probe(m_witConn!, "SELECT * FROM Products WHERE Id = @key", id => (long)id + 1);

    [Benchmark(Description = "PK equality x100 - SQLite")]
    public int PkSqlite() =>
        Probe(m_sqliteConn!, "SELECT * FROM Products WHERE Id = @key", id => (long)id + 1);

    [Benchmark(Description = "UNIQUE index string key x100 - WitDb")]
    public int UniqueStrWitDb() =>
        Probe(m_witConn!, "SELECT * FROM Products WHERE SKU = @key", Sku);

    [Benchmark(Description = "UNIQUE index string key x100 - SQLite")]
    public int UniqueStrSqlite() =>
        Probe(m_sqliteConn!, "SELECT * FROM Products WHERE SKU = @key", Sku);

    [Benchmark(Description = "UNIQUE index int key x100 - WitDb")]
    public int UniqueIntWitDb() =>
        Probe(m_witConn!, "SELECT * FROM Products WHERE AltInt = @key", id => id);

    [Benchmark(Description = "UNIQUE index int key x100 - SQLite")]
    public int UniqueIntSqlite() =>
        Probe(m_sqliteConn!, "SELECT * FROM Products WHERE AltInt = @key", id => id);

    [Benchmark(Description = "Non-unique index string key x100 - WitDb")]
    public int NonUniqueStrWitDb() =>
        Probe(m_witConn!, "SELECT * FROM Products WHERE AltStr = @key", Alt);

    [Benchmark(Description = "Non-unique index string key x100 - SQLite")]
    public int NonUniqueStrSqlite() =>
        Probe(m_sqliteConn!, "SELECT * FROM Products WHERE AltStr = @key", Alt);

    [Benchmark(Description = "UNIQUE index narrow projection x100 - WitDb")]
    public int UniqueStrNarrowWitDb() =>
        Probe(m_witConn!, "SELECT SKU FROM Products WHERE SKU = @key", Sku);

    [Benchmark(Description = "UNIQUE index narrow projection x100 - SQLite")]
    public int UniqueStrNarrowSqlite() =>
        Probe(m_sqliteConn!, "SELECT SKU FROM Products WHERE SKU = @key", Sku);

    [Benchmark(Description = "UNIQUE index PK projection x100 - WitDb")]
    public int UniqueStrPkProjWitDb() =>
        Probe(m_witConn!, "SELECT Id FROM Products WHERE SKU = @key", Sku);

    [Benchmark(Description = "UNIQUE index PK projection x100 - SQLite")]
    public int UniqueStrPkProjSqlite() =>
        Probe(m_sqliteConn!, "SELECT Id FROM Products WHERE SKU = @key", Sku);

    [Benchmark(Description = "PK equality narrow projection x100 - WitDb")]
    public int PkNarrowWitDb() =>
        Probe(m_witConn!, "SELECT Id FROM Products WHERE Id = @key", id => (long)id + 1);

    [Benchmark(Description = "PK equality narrow projection x100 - SQLite")]
    public int PkNarrowSqlite() =>
        Probe(m_sqliteConn!, "SELECT Id FROM Products WHERE Id = @key", id => (long)id + 1);

    /// <summary>
    /// Reads every row matching a range that selects about 75% of the table, once.
    /// </summary>
    /// <remarks>
    /// The pair below asks whether having an index can make a query *slower*. Both predicates
    /// select the same share of the table; one is on an indexed column and one is not. With no
    /// statistics the planner has no selectivity estimate, so an index it can use is an index it
    /// will use - and fetching 75% of the rows one index entry at a time is not obviously cheaper
    /// than reading them in order.
    ///
    /// Suggested by the 100,000-row sweep, where `SELECT * FROM Users WHERE Age > 30` (74% of the
    /// table, indexed column) cost 405 ms while an unfiltered `SELECT *` over the same table cost
    /// 129 ms. Reading fewer rows was three times more expensive.
    /// </remarks>
    private int Range(System.Data.Common.DbConnection connection, string sql, object threshold)
    {
        using var c = connection.CreateCommand();
        c.CommandText = sql;
        var p = Add(c, "@key");
        p.Value = threshold;

        using var r = c.ExecuteReader();

        var rows = 0;
        while (r.Read())
            rows++;

        return rows;
    }

    [Benchmark(Description = "Range 75% on INDEXED column - WitDb")]
    public int RangeIndexedWitDb() =>
        Range(m_witConn!, "SELECT * FROM Products WHERE AltInt > @key", TableSize / 4);

    [Benchmark(Description = "Range 75% on INDEXED column - SQLite")]
    public int RangeIndexedSqlite() =>
        Range(m_sqliteConn!, "SELECT * FROM Products WHERE AltInt > @key", TableSize / 4);

    [Benchmark(Description = "Range 75% on LOW-CARDINALITY index - WitDb")]
    public int RangeLowCardinalityWitDb() =>
        Range(m_witConn!, "SELECT * FROM Products WHERE Bucket > @key", BucketCount / 4);

    [Benchmark(Description = "Range 75% on LOW-CARDINALITY index - SQLite")]
    public int RangeLowCardinalitySqlite() =>
        Range(m_sqliteConn!, "SELECT * FROM Products WHERE Bucket > @key", BucketCount / 4);

    [Benchmark(Description = "Range 75% on UNINDEXED column - WitDb")]
    public int RangeUnindexedWitDb() =>
        Range(m_witConn!, "SELECT * FROM Products WHERE Price > @key", 250.0);

    [Benchmark(Description = "Range 75% on UNINDEXED column - SQLite")]
    public int RangeUnindexedSqlite() =>
        Range(m_sqliteConn!, "SELECT * FROM Products WHERE Price > @key", 250.0);

    [Benchmark(Description = "No index forced scan x100 - WitDb")]
    public int NoIndexWitDb() =>
        Probe(m_witConn!, "SELECT * FROM Products WHERE Name = @key", Name);

    [Benchmark(Description = "No index forced scan x100 - SQLite")]
    public int NoIndexSqlite() =>
        Probe(m_sqliteConn!, "SELECT * FROM Products WHERE Name = @key", Name);

    #endregion

    public void Dispose() => GlobalCleanup();
}
