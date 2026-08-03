using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Takes the LSM write path apart, one suspect at a time.
/// </summary>
/// <remarks>
/// The phase 10 mode sweep measured LSM at **12-20x B+Tree per row on every write shape and every
/// size**, worst on autocommit at a flat 9.4x - and it disproved the recorded claim that the cost is
/// non-linear in N. Per-row cost actually falls as the table grows (x0.86-0.89 from 1,000 to 5,000
/// rows, both passes), which is the healthy shape. So this is a large constant factor, not a scaling
/// defect, and the two need completely different work.
///
/// That matters because an LSM tree exists to be *good* at writes. Losing to a B+Tree by an order of
/// magnitude on the one workload the structure is chosen for is a defect signature, not a trade-off.
///
/// Each shape below writes the same rows the same way and differs in exactly one property, so the
/// difference between two rows of the table is the cost of that property:
///
///   Bare table          no primary key, no indexes - the raw cost of putting bytes in the store
///   PK only             adds the generated key and whatever uniqueness check it implies
///   PK + 1 index        adds one secondary index to maintain
///   PK + 3 indexes      three, to see whether index maintenance scales or has a fixed cost
///
/// Run it against BTree and Lsm together: the *ratio between the two engines* at each shape is the
/// measurement, and the shape where that ratio jumps is where the cost lives. Neither column alone
/// says anything.
///
/// Every shape returns what the engine claimed it wrote, and IterationCleanup checks that claim
/// against a scan - see <see cref="WriteVerification"/>. On this engine that is not ceremony: the
/// LSM store with a parallel mode once reported ten successful inserts and left 0-1 rows.
/// </remarks>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class LsmWriteAnatomyBenchmarks : IDisposable
{
    #region Fields

    private WitDbConnection? m_conn;
    private string m_path = null!;

    private int m_claimed = -1;
    private string? m_claimedTable;

    #endregion

    #region Parameters

    [ParamsSource(nameof(RowCountValues))]
    public int RowCount { get; set; }

    public IEnumerable<int> RowCountValues => BenchmarkSweep.Sizes(1000);

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
        m_path = isLsm
            ? BenchmarkPathHelper.GenerateUniquePath("wit_lsmwrite_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_lsmwrite_btree") + ".witdb";
    }

    [IterationSetup]
    public void IterationSetup()
    {
        CleanupPaths();

        m_conn = new WitDbConnection(WitDbConnectionHelper.BuildConnectionString(m_path, EngineMode));
        m_conn.Open();

        using var c = m_conn.CreateCommand();

        // No key and no index at all. Whatever this costs is the floor: serialising a row and
        // putting it in the store.
        c.CommandText = "CREATE TABLE Bare (A INT, B VARCHAR(50))";
        c.ExecuteNonQuery();

        c.CommandText = "CREATE TABLE Keyed (Id BIGINT PRIMARY KEY AUTOINCREMENT, A INT, B VARCHAR(50))";
        c.ExecuteNonQuery();

        c.CommandText = "CREATE TABLE Indexed1 (Id BIGINT PRIMARY KEY AUTOINCREMENT, A INT, B VARCHAR(50))";
        c.ExecuteNonQuery();
        c.CommandText = "CREATE INDEX IX_Indexed1_A ON Indexed1(A)";
        c.ExecuteNonQuery();

        c.CommandText = "CREATE TABLE Indexed3 (Id BIGINT PRIMARY KEY AUTOINCREMENT, A INT, B VARCHAR(50), C INT)";
        c.ExecuteNonQuery();
        c.CommandText = "CREATE INDEX IX_Indexed3_A ON Indexed3(A)";
        c.ExecuteNonQuery();
        c.CommandText = "CREATE INDEX IX_Indexed3_B ON Indexed3(B)";
        c.ExecuteNonQuery();
        c.CommandText = "CREATE INDEX IX_Indexed3_C ON Indexed3(C)";
        c.ExecuteNonQuery();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        VerifyLastWrite();

        m_conn?.Dispose();
        m_conn = null;

        CleanupPaths();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => IterationCleanup();

    private void CleanupPaths()
    {
        BenchmarkPathHelper.SafeCleanup(m_path);
        BenchmarkPathHelper.SafeCleanup(m_path + "_indexes");
    }

    /// <summary>
    /// Outside the timed region, and never via COUNT(*) - that is a cached counter here.
    /// </summary>
    private void VerifyLastWrite()
    {
        if (m_claimed < 0 || m_claimedTable == null || m_conn == null)
        {
            m_claimed = -1;
            m_claimedTable = null;
            return;
        }

        var claimed = m_claimed;
        var table = m_claimedTable;
        m_claimed = -1;
        m_claimedTable = null;

        // The Bare table has no Id column to project, so scan a column it does have.
        var column = table == "Bare" ? "A" : "Id";

        using var c = m_conn.CreateCommand();
        c.CommandText = $"SELECT {column} FROM {table}";

        using var r = c.ExecuteReader();

        var scanned = 0;
        while (r.Read())
            scanned++;

        WriteVerification.Verify($"{EngineMode}/{table}", claimed, scanned);
    }

    #endregion

    #region Harness

    private int Insert(string table, string columns, string values, Action<Dictionary<string, System.Data.Common.DbParameter>, int> bind)
    {
        var written = 0;

        var tx = (WitDbTransaction)m_conn!.BeginTransaction();
        using (var c = m_conn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = $"INSERT INTO {table} ({columns}) VALUES ({values})";

            var parameters = new Dictionary<string, System.Data.Common.DbParameter>();
            foreach (var name in values.Split(',').Select(v => v.Trim()))
            {
                var p = c.CreateParameter();
                p.ParameterName = name;
                c.Parameters.Add(p);
                parameters[name] = p;
            }

            for (int i = 0; i < RowCount; i++)
            {
                bind(parameters, i);
                written += c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();

        m_claimed = written;
        m_claimedTable = table;
        return written;
    }

    #endregion

    #region Benchmarks

    [Benchmark(Description = "Bare table, no key no index")]
    public int Bare() => Insert("Bare", "A, B", "@a, @b", (p, i) =>
    {
        p["@a"].Value = i;
        p["@b"].Value = $"Row_{i}";
    });

    [Benchmark(Description = "PK only")]
    public int Keyed() => Insert("Keyed", "A, B", "@a, @b", (p, i) =>
    {
        p["@a"].Value = i;
        p["@b"].Value = $"Row_{i}";
    });

    [Benchmark(Description = "PK + 1 secondary index")]
    public int Indexed1() => Insert("Indexed1", "A, B", "@a, @b", (p, i) =>
    {
        p["@a"].Value = i;
        p["@b"].Value = $"Row_{i}";
    });

    [Benchmark(Description = "PK + 3 secondary indexes")]
    public int Indexed3() => Insert("Indexed3", "A, B, C", "@a, @b, @c", (p, i) =>
    {
        p["@a"].Value = i;
        p["@b"].Value = $"Row_{i}";
        p["@c"].Value = i * 2;
    });

    #endregion

    public void Dispose() => GlobalCleanup();
}
