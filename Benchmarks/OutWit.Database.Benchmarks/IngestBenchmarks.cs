using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Event document for the ingest benchmark.
/// </summary>
public class IngestEvent
{
    public int Id { get; set; }
    public long EventTime { get; set; }
    public int Source { get; set; }
    public double Value { get; set; }
    public string Payload { get; set; } = string.Empty;
}

/// <summary>
/// Sustained high-volume ingest - the workload an LSM tree exists for, and the one the suite has
/// never had.
/// </summary>
/// <remarks>
/// Phase 10 could not answer "is there a workload where Store=lsm wins?" because nothing here
/// measured the shape LSM is designed for. Every existing write benchmark tops out at 5,000 rows in
/// transactions of 100, where a B+Tree's update-in-place is at its best and an LSM's advantage -
/// amortising many writes into one large sequential flush - has nothing to amortise over.
///
/// This writes tens to hundreds of thousands of rows in steady batches, which is what an event
/// pipeline does: WitAnalytics ingesting events, a metrics feed, an audit log. At this volume the
/// MemTable fills repeatedly, SSTables are actually produced, and the compactor has real work - so
/// the structure is finally being asked the question it was chosen to answer.
///
/// **Read the LSM and B+Tree columns against each other**, and both against LiteDB and SQLite. The
/// caveats from the phase-10 record still apply to the cross-engine columns: SQLite pays P/Invoke
/// per call and LiteDB is a document store with no SQL to parse. What no caveat touches is
/// WitDatabase's two storage engines measured against one another on identical SQL.
///
/// Every shape returns what the engine claimed it wrote and IterationCleanup checks that claim
/// against a scan - see <see cref="WriteVerification"/>. At this volume that is not ceremony: the
/// LSM store with a parallel mode once reported success for ten inserts and left 0-1 rows.
/// </remarks>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class IngestBenchmarks : IDisposable
{
    /// <summary>
    /// Rows per transaction. An ingest pipeline batches; it does not autocommit per event, and it
    /// does not hold one transaction open for the whole feed.
    /// </summary>
    private const int BatchSize = 1000;

    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private LiteDatabase? m_liteDb;
    private ILiteCollection<IngestEvent>? m_liteCollection;

    private string m_witPath = null!;
    private string m_sqlitePath = null!;
    private string m_liteDbPath = null!;

    private int m_claimed = -1;
    private string? m_claimedEngine;

    #endregion

    #region Parameters

    [ParamsSource(nameof(RowCountValues))]
    public int RowCount { get; set; }

    public IEnumerable<int> RowCountValues => BenchmarkSweep.Sizes(50_000, 200_000);

    [ParamsSource(nameof(EngineModeValues))]
    public WitDbEngineMode EngineMode { get; set; }

    public IEnumerable<WitDbEngineMode> EngineModeValues => BenchmarkSweep.Modes(
        WitDbEngineMode.BTree, WitDbEngineMode.Lsm,
        WitDbEngineMode.Default, WitDbEngineMode.BTreeParallelAuto, WitDbEngineMode.LsmParallelAuto);

    #endregion

    #region Setup/Cleanup

    [GlobalSetup]
    public void GlobalSetup()
    {
        var isLsm = EngineMode is WitDbEngineMode.Lsm or WitDbEngineMode.LsmParallelAuto;
        m_witPath = isLsm
            ? BenchmarkPathHelper.GenerateUniquePath("wit_ingest_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_ingest_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_ingest") + ".db";
        m_liteDbPath = BenchmarkPathHelper.GenerateUniquePath("lite_ingest") + ".db";
    }

    [IterationSetup]
    public void IterationSetup()
    {
        CleanupPaths();

        m_witConn = new WitDbConnection(WitDbConnectionHelper.BuildConnectionString(m_witPath, EngineMode));
        m_witConn.Open();
        using (var c = m_witConn.CreateCommand())
        {
            c.CommandText = @"
                CREATE TABLE Events (
                    Id BIGINT PRIMARY KEY AUTOINCREMENT,
                    EventTime BIGINT,
                    Source INT,
                    Value DOUBLE,
                    Payload VARCHAR(100)
                )";
            c.ExecuteNonQuery();
        }

        m_sqliteConn = new SqliteConnection($"Data Source={m_sqlitePath}");
        m_sqliteConn.Open();
        using (var c = m_sqliteConn.CreateCommand())
        {
            c.CommandText = @"
                CREATE TABLE Events (
                    Id INTEGER PRIMARY KEY,
                    EventTime INTEGER,
                    Source INTEGER,
                    Value REAL,
                    Payload TEXT
                )";
            c.ExecuteNonQuery();
        }

        m_liteDb = new LiteDatabase(m_liteDbPath);
        m_liteCollection = m_liteDb.GetCollection<IngestEvent>("events");
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        VerifyLastWrite();

        m_witConn?.Dispose(); m_witConn = null;
        m_sqliteConn?.Dispose(); m_sqliteConn = null;
        m_liteDb?.Dispose(); m_liteDb = null;
        m_liteCollection = null;
        SqliteConnection.ClearAllPools();

        CleanupPaths();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => IterationCleanup();

    private void CleanupPaths()
    {
        BenchmarkPathHelper.SafeCleanup(m_witPath);
        BenchmarkPathHelper.SafeCleanup(m_witPath + "_indexes");
        BenchmarkPathHelper.SafeCleanup(m_sqlitePath);
        BenchmarkPathHelper.SafeCleanup(m_liteDbPath);
    }

    /// <summary>
    /// Outside the timed region. Never COUNT(*) - that is a cached counter on this engine.
    /// </summary>
    private void VerifyLastWrite()
    {
        if (m_claimed < 0 || m_claimedEngine == null)
        {
            m_claimed = -1;
            m_claimedEngine = null;
            return;
        }

        var claimed = m_claimed;
        var engine = m_claimedEngine;
        m_claimed = -1;
        m_claimedEngine = null;

        var scanned = engine switch
        {
            "WitDb" when m_witConn != null =>
                WriteVerification.CountRowsByScan(m_witConn, "Events"),
            "SQLite" when m_sqliteConn != null =>
                WriteVerification.CountRowsByScan(m_sqliteConn, "Events"),
            "LiteDB" when m_liteCollection != null =>
                m_liteCollection.FindAll().Count(),
            _ => claimed
        };

        WriteVerification.Verify($"{engine}/{EngineMode}", claimed, scanned);
    }

    private int Claim(string engine, int written)
    {
        m_claimed = written;
        m_claimedEngine = engine;
        return written;
    }

    #endregion

    #region Benchmarks

    [Benchmark(Description = "Ingest in batches - WitDb")]
    public int IngestWitDb()
    {
        var written = 0;
        var baseTime = DateTime.UtcNow.Ticks;

        for (int start = 0; start < RowCount; start += BatchSize)
        {
            var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
            using (var c = m_witConn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "INSERT INTO Events (EventTime, Source, Value, Payload) VALUES (@t, @s, @v, @p)";
                var pt = c.CreateParameter(); pt.ParameterName = "@t"; c.Parameters.Add(pt);
                var ps = c.CreateParameter(); ps.ParameterName = "@s"; c.Parameters.Add(ps);
                var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
                var pp = c.CreateParameter(); pp.ParameterName = "@p"; c.Parameters.Add(pp);

                var end = Math.Min(start + BatchSize, RowCount);
                for (int i = start; i < end; i++)
                {
                    pt.Value = baseTime + i;
                    ps.Value = i % 64;
                    pv.Value = i * 0.5;
                    pp.Value = $"event-{i}";
                    written += c.ExecuteNonQuery();
                }
            }
            tx.Commit();
            tx.Dispose();
        }

        return Claim("WitDb", written);
    }

    [Benchmark(Description = "Ingest in batches - SQLite")]
    public int IngestSqlite()
    {
        var written = 0;
        var baseTime = DateTime.UtcNow.Ticks;

        for (int start = 0; start < RowCount; start += BatchSize)
        {
            var tx = m_sqliteConn!.BeginTransaction();
            using (var c = m_sqliteConn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "INSERT INTO Events (EventTime, Source, Value, Payload) VALUES (@t, @s, @v, @p)";
                var pt = c.CreateParameter(); pt.ParameterName = "@t"; c.Parameters.Add(pt);
                var ps = c.CreateParameter(); ps.ParameterName = "@s"; c.Parameters.Add(ps);
                var pv = c.CreateParameter(); pv.ParameterName = "@v"; c.Parameters.Add(pv);
                var pp = c.CreateParameter(); pp.ParameterName = "@p"; c.Parameters.Add(pp);

                var end = Math.Min(start + BatchSize, RowCount);
                for (int i = start; i < end; i++)
                {
                    pt.Value = baseTime + i;
                    ps.Value = i % 64;
                    pv.Value = i * 0.5;
                    pp.Value = $"event-{i}";
                    written += c.ExecuteNonQuery();
                }
            }
            tx.Commit();
            tx.Dispose();
        }

        return Claim("SQLite", written);
    }

    [Benchmark(Description = "Ingest in batches - LiteDB")]
    public int IngestLiteDb()
    {
        var written = 0;
        var baseTime = DateTime.UtcNow.Ticks;
        var batch = new List<IngestEvent>(BatchSize);

        for (int start = 0; start < RowCount; start += BatchSize)
        {
            batch.Clear();
            var end = Math.Min(start + BatchSize, RowCount);
            for (int i = start; i < end; i++)
            {
                batch.Add(new IngestEvent
                {
                    EventTime = baseTime + i,
                    Source = i % 64,
                    Value = i * 0.5,
                    Payload = $"event-{i}"
                });
            }

            written += m_liteCollection!.InsertBulk(batch);
        }

        return Claim("LiteDB", written);
    }

    #endregion

    public void Dispose() => GlobalCleanup();
}
