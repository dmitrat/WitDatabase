using System.Data.Common;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// An index the catalogue names must be able to answer, or not be named.
/// </summary>
/// <remarks>
/// <para>
/// <b>A CREATE INDEX that failed leaves a database that answers queries with nothing.</b> The
/// catalogue entry is written - and since issue 10 flushed where it is written - before the index
/// holds anything, and the failure path only removes it for one kind of failure. Measured
/// 2026-08-09: a build that threw left the index in the catalogue, its file at one empty page, and
/// <c>WHERE V = 7</c> answering zero rows out of two. The database opens, the query succeeds, and
/// the answer is wrong - which is worse than the loud halves of the same window, where the database
/// refuses to open or a statement is visibly half applied.
/// </para>
/// <para>
/// Two things in <c>WitSqlEngine.BuildIndexFromExistingData</c> produce it. The <c>catch</c> reads
/// every <c>InvalidOperationException</c> as a unique violation - so an exhausted page cache is
/// reported as "UNIQUE constraint failed" - and it then calls <c>m_database.DropIndex</c> first,
/// which can throw for the same reason the build did, leaving <c>m_schema.DropIndex</c> on the next
/// line unreached.
/// </para>
/// <para>
/// The same end state is reachable by killing the process during the build, which is what
/// <see cref="IndexBuildAfterAKillTests"/> is for. This fixture needs no crash: it is the cheap,
/// deterministic half of one defect.
/// </para>
/// </remarks>
[TestFixture]
public sealed class HalfBuiltIndexTests
{
    #region Constants

    /// <summary>
    /// The smallest table that makes the build fail with <c>CacheSize=8</c>, measured rather than
    /// chosen: 500 and 1,000 rows both build successfully, 2,000 throws. Below the threshold the
    /// case would pass against the defect it exists for.
    /// </summary>
    private const int ROWS = 2_000;

    /// <summary>
    /// How many distinct values the indexed column carries. It matters twice: the index tree has to
    /// be big enough to exhaust the cache, and the filter has to match few enough rows that a
    /// missing answer and a correct one cannot be confused.
    /// </summary>
    private const int DISTINCT_VALUES = 1_000;

    private const int PROBE_VALUE = 7;

    private const int MATCHING_ROWS = ROWS / DISTINCT_VALUES;

    /// <summary>The rows the UNIQUE control writes - it fails on the data, not on the cache.</summary>
    private const int CONTROL_ROWS = 200;

    #endregion

    #region Fields

    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_halfidx_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in new[] { m_databasePath, m_databasePath + "_indexes" })
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // Cleanup only.
            }
        }
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: the same index over the same rows, built to the end, answers with every matching
    /// row. Without it, "the index answers nothing" is equally consistent with a table that never
    /// held those rows.
    /// </summary>
    [Test]
    public void ControlACompletedIndexAnswersEveryMatchingRowTest()
    {
        using (var connection = Connect(smallCache: false))
        {
            Seed(connection, ROWS, i => i % DISTINCT_VALUES);
            Execute(connection, "CREATE INDEX IX_T_V ON T (V)");
        }

        Assert.That(RowsAnsweredForTheProbeValue(), Is.EqualTo(MATCHING_ROWS),
            "an index built to the end does not answer with the rows it was built from, so this "
            + "fixture cannot tell a half-built index from a wrong query");
    }

    /// <summary>
    /// Control: the probe's query really goes through the index.
    /// </summary>
    /// <remarks>
    /// Written because a query the planner answers by scanning the table says nothing about what an
    /// index holds - and an earlier shape of this fixture passed for exactly that reason.
    /// </remarks>
    [Test]
    public void ControlTheProbeQueryIsPlannedThroughTheIndexTest()
    {
        using (var connection = Connect(smallCache: false))
        {
            Seed(connection, ROWS, i => i % DISTINCT_VALUES);
            Execute(connection, "CREATE INDEX IX_T_V ON T (V)");
        }

        var plan = ProbeQueryPlan();

        TestContext.Out.WriteLine($"plan: {plan}");

        Assert.That(plan, Does.Contain("IX_T_V"),
            "the probe's query is not planned through the index, so what it answers is a statement "
            + "about the table and this fixture is blind to its own subject");
    }

    /// <summary>
    /// Control: the one failure the build already cleans up after - a unique index over duplicate
    /// values - leaves nothing behind. It is what says the defect below is about the failure PATH
    /// rather than about failing at all.
    /// </summary>
    [Test]
    public void ControlAUniqueIndexRefusedByTheDataLeavesNothingBehindTest()
    {
        using (var connection = Connect(smallCache: false))
        {
            Seed(connection, CONTROL_ROWS, _ => PROBE_VALUE);

            Assert.That(() => Execute(connection, "CREATE UNIQUE INDEX IX_T_V ON T (V)"),
                Throws.Exception,
                "a unique index over duplicate values was not refused, so this control is not "
                + "measuring a failed build");
        }

        Assert.That(IndexIsInTheCatalogue(), Is.False,
            "the refused unique index is still in the catalogue");

        Assert.That(RowsAnsweredForTheProbeValue(), Is.EqualTo(CONTROL_ROWS),
            "the table stopped answering after an index creation that was refused");
    }

    #endregion

    #region The probe

    /// <summary>
    /// An index whose build failed must not be left where the planner can find it.
    /// </summary>
    [Test]
    public void AnIndexWhoseBuildFailedIsNotLeftBehindTest()
    {
        using (var connection = Connect(smallCache: true))
        {
            Seed(connection, ROWS, i => i % DISTINCT_VALUES);

            Assert.That(() => Execute(connection, "CREATE INDEX IX_T_V ON T (V)"),
                Throws.Exception,
                "the build did not fail, so this case is not measuring what it says it is - the "
                + "table may have grown too small for the cache to be exhausted");
        }

        // Asked of a REOPENED database, and the difference is the whole case: the connection that
        // ran the statement has the failed index out of its own registry, so it answers correctly
        // whatever is on disk. The catalogue entry is what persists, and an earlier version of this
        // case asked the same connection and passed against the defect.
        var answered = RowsAnsweredForTheProbeValue();

        TestContext.Out.WriteLine(
            $"after a failed build: {answered} of {MATCHING_ROWS} matching rows answered; "
            + $"catalogue names it={IndexIsInTheCatalogue()}; index file={IndexFileSize()} bytes");

        Assert.That(answered, Is.EqualTo(MATCHING_ROWS),
            $"the query answered {answered} rows where {MATCHING_ROWS} of them match. The CREATE "
            + "INDEX failed, and it left the index in the catalogue with nothing in it - so the "
            + "planner routes a query through an index that cannot answer, and the database returns "
            + "the wrong rows with no error anywhere.");
    }

    /// <summary>
    /// <b>PINS A DEFECT, NOT CORRECT BEHAVIOUR.</b> A failed index build still holds the index file
    /// for the life of the process, and the fix should invert the first assertion below.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fifth instance in this project of the same shape - something is constructed, the work
    /// then fails, and nothing disposes what was built. <c>IndexManager.DropIndex</c> now disposes
    /// the index whatever emptying it does (<c>IndexManagerDropTests</c>, measured both ways), and
    /// that is not enough: the dispose CHAIN under it flushes, and on the failure this case
    /// provokes - an exhausted page cache - the flush throws in its turn, so the file is abandoned
    /// one layer further down.
    /// </para>
    /// <para>
    /// Left pinned rather than fixed because the repair belongs to the store's dispose path, which
    /// every store shares, and this branch's subject is what a half-built index does to answers.
    /// <b>The reopen below already works</b>, which is what makes the damage recoverable in
    /// practice: nothing names that file any more.
    /// </para>
    /// </remarks>
    [Test]
    public void AFailedIndexBuildStillHoldsItsFileTest()
    {
        using (var connection = Connect(smallCache: true))
        {
            Seed(connection, ROWS, i => i % DISTINCT_VALUES);

            Assert.That(() => Execute(connection, "CREATE INDEX IX_T_V ON T (V)"),
                Throws.Exception,
                "the build did not fail, so this case is not measuring a failed build");
        }

        // Asked of the FILE rather than through a reopen: a reopen only meets the leak while the
        // catalogue still names the index, and it no longer does, so a handle nobody happens to
        // collide with would go unnoticed. PINS THE DEFECT - the fix inverts this to Throws.Nothing.
        Assert.That(() =>
            {
                using var _ = new FileStream(
                    Path.Combine(m_databasePath + "_indexes", "IX_T_V.idx"),
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);
            },
            Throws.InstanceOf<IOException>(),
            "the index file can be opened exclusively, so the leak this case pins is gone - invert "
            + "the assertion rather than deleting the case");

        Assert.That(() =>
            {
                using var reopened = new WitDbConnection($"Data Source={m_databasePath}");
                reopened.Open();
            },
            Throws.Nothing,
            "the database cannot be reopened after a CREATE INDEX that failed");
    }

    #endregion

    #region Tools

    /// <summary>
    /// <paramref name="smallCache"/> is what makes the build fail: the index tree needs more pages
    /// at once than eight, and the cache refuses rather than evicting a pinned one.
    /// </summary>
    private WitDbConnection Connect(bool smallCache)
    {
        var connection = new WitDbConnection(
            smallCache
                ? $"Data Source={m_databasePath};CacheSize=8"
                : $"Data Source={m_databasePath}");

        connection.Open();

        return connection;
    }

    private static void Seed(DbConnection connection, int rows, Func<int, int> value)
    {
        Execute(connection, "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, V INT)");

        using var transaction = connection.BeginTransaction();

        for (var i = 0; i < rows; i++)
            Execute(connection, $"INSERT INTO T (V) VALUES ({value(i)})", transaction);

        transaction.Commit();
    }

    /// <summary>
    /// What the reopened database answers for the probe's value. Deliberately not <c>COUNT(*)</c>: a
    /// count is separate state with separate persistence here, and the question is about the rows a
    /// query returns.
    /// </summary>
    private int RowsAnsweredForTheProbeValue()
    {
        using var connection = new WitDbConnection($"Data Source={m_databasePath}");
        connection.Open();

        return RowsAnsweredForTheProbeValue(connection);
    }

    private static int RowsAnsweredForTheProbeValue(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id FROM T WHERE V = {PROBE_VALUE}";

        using var reader = command.ExecuteReader();

        var rows = 0;

        while (reader.Read())
            rows++;

        return rows;
    }

    private string ProbeQueryPlan()
    {
        using var connection = new WitDbConnection($"Data Source={m_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN SELECT Id FROM T WHERE V = {PROBE_VALUE}";

        using var reader = command.ExecuteReader();

        var lines = new List<string>();

        while (reader.Read())
        {
            var parts = new string[reader.FieldCount];

            for (var i = 0; i < reader.FieldCount; i++)
                parts[i] = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString() ?? "";

            lines.Add(string.Join(" ", parts));
        }

        return string.Join(" | ", lines);
    }

    private bool IndexIsInTheCatalogue()
    {
        using var connection = new WitDbConnection($"Data Source={m_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IX_T_V'";

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private long IndexFileSize()
    {
        var file = Path.Combine(m_databasePath + "_indexes", "IX_T_V.idx");

        return File.Exists(file) ? new FileInfo(file).Length : -1;
    }

    // Through the ADO.NET base types deliberately: WitDbCommand shadows Transaction with its own
    // type, and this is where a drop-in provider is supposed to be exercised as one.
    private static void Execute(DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (transaction != null)
            command.Transaction = transaction;

        command.ExecuteNonQuery();
    }

    #endregion
}
