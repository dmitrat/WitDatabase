using System.Data.Common;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// A database is a SET of files, and an index whose content is not among them must not be answered
/// from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Copying a `.witdb` without its `_indexes` directory makes an indexed column answer zero rows,
/// silently.</b> Measured 2026-08-14, encrypted and plain alike, with the un-indexed arm of the same
/// probe answering correctly. `EXPLAIN` says `SEARCH TABLE Orders USING INDEX …` and the index is
/// empty, so the query succeeds and the answer is wrong - the worst of the three shapes this defect
/// has, because the loud ones at least refuse.
/// </para>
/// <para>
/// <b>It is not a new defect.</b> It is <c>KnownIssues</c> 14's own named remainder - <i>"an index
/// that exists and holds SOME entries is trusted… its comment promises a lazy rebuild that does not
/// happen"</i> - met from the outside, by a person copying a file. The chain, all of it at open:
/// </para>
/// <list type="number">
/// <item><description><c>WitDatabase.RestoreIndexesFromMetadata</c> calls
/// <c>IndexManager.CreateIndex</c> for every index the metadata names, which <b>creates</b> the
/// physical index - so a missing directory is made, holding an empty <c>.idx</c>;</description></item>
/// <item><description><c>WitSqlEngine.EnsurePhysicalIndexesExist</c> then finds an index and leaves
/// it: <i>"If physical index exists (even if empty), don't rebuild."</i></description></item>
/// <item><description>the planner uses the catalogue's index, which is empty.</description></item>
/// </list>
/// <para>
/// <b>Why "rebuild anything empty" is the wrong rule</b>, and this is what stopped the fix being one
/// line: <c>FillIndexFromExistingData</c> skips rows whose indexed columns are NULL and rows outside
/// a partial index's condition. An index over an all-NULL column is therefore <b>legitimately</b>
/// empty, and a rule keyed on emptiness would rescan the whole table on every open, for ever. What
/// distinguishes the two is not how much the index holds - it is whether its content was THERE.
/// </para>
/// <para>
/// Both directions are measured. The sibling <see cref="HalfBuiltIndexTests"/> covers the other face
/// of the same mechanism, where the build failed rather than the file went missing.
/// </para>
/// </remarks>
[TestFixture]
public sealed class MissingIndexContentTests
{
    #region Constants

    private const int ROWS = 200;

    private const int DISTINCT_VALUES = 20;

    private const int PROBE_VALUE = 7;

    private const int MATCHING_ROWS = ROWS / DISTINCT_VALUES;

    private const string INDEX_SUFFIX = "_indexes";

    #endregion

    #region Fields

    private string m_root = null!;

    private string m_original = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"MissingIndexContent_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);

        m_original = Path.Combine(m_root, "original.witdb");

        using var connection = new WitDbConnection($"Data Source={m_original}");
        connection.Open();

        Execute(connection, "CREATE TABLE T (Id INTEGER PRIMARY KEY AUTOINCREMENT, V INT NOT NULL)");
        Execute(connection, "CREATE INDEX IX_T_V ON T (V)");

        for (var i = 0; i < ROWS; i++)
            Execute(connection, $"INSERT INTO T (V) VALUES ({i % DISTINCT_VALUES})");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_root, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region The rule

    /// <summary>
    /// The database file alone, opened, must still answer for an indexed column.
    /// </summary>
    /// <remarks>
    /// RED before the fix: 0 rows where 10 are there, and no error. The rows are all present - the
    /// case asserts that too, because "the index is empty" and "the data is gone" are different
    /// disasters and only one of them is happening.
    /// </remarks>
    [Test]
    public void ADatabaseCopiedWithoutItsIndexDirectoryStillAnswersTest()
    {
        var copy = CopyDatabaseFileAlone("no-index-dir.witdb");

        using var connection = new WitDbConnection($"Data Source={copy}");
        connection.Open();

        Assert.Multiple(() =>
        {
            Assert.That(TotalRows(connection), Is.EqualTo(ROWS),
                "the rows are in the database file and must all be there - this case is about the "
                + "index, not about data loss");

            Assert.That(RowsForTheProbeValue(connection), Is.EqualTo(MATCHING_ROWS),
                $"WHERE V = {PROBE_VALUE} must answer {MATCHING_ROWS} rows; an index whose content "
                + "was never copied must be rebuilt rather than believed");
        });
    }

    /// <summary>
    /// The same, one level in: the directory is there and the index file inside it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This was written as a positive control and it went RED, which is the finding.</b> The
    /// record - <c>KnownIssues</c> 14 - says <c>EnsurePhysicalIndexesExist</c> rebuilds a
    /// <i>missing</i> index, and that reading is what made this look like the arm the engine already
    /// handles. It does not, and the reason is one line earlier:
    /// <c>RestoreIndexesFromMetadata</c> has already called <c>CreateIndex</c>, which MAKES the
    /// file - so <c>GetIndex</c> is non-null by the time the rebuild branch is reached, for every
    /// index the metadata names, which is all of them.
    /// </para>
    /// <para>
    /// So the missing-index branch is unreachable on this path, and the phase-17 note that a killed
    /// build "usually repairs itself" describes the narrower case where the metadata does not name
    /// the index yet.
    /// </para>
    /// </remarks>
    [Test]
    public void ADatabaseWhoseIndexFileIsMissingStillAnswersTest()
    {
        var copy = CopyDatabaseAsASet("empty-index-dir.witdb");

        foreach (var file in Directory.GetFiles(copy + INDEX_SUFFIX))
            File.Delete(file);

        using var connection = new WitDbConnection($"Data Source={copy}");
        connection.Open();

        Assert.That(RowsForTheProbeValue(connection), Is.EqualTo(MATCHING_ROWS),
            "an index whose file is not there must be rebuilt rather than believed");
    }

    /// <summary>
    /// The positive control the one above turned out not to be: the machinery that fills an index
    /// from the rows already in the table WORKS when it is reached.
    /// </summary>
    /// <remarks>
    /// Creating an index over a populated table is the one route that reaches
    /// <c>BuildIndexFromExistingData</c> today. Without this, a red rule above could equally mean
    /// "the condition never fires" or "the rebuild is broken", and those need different fixes.
    /// </remarks>
    [Test]
    public void ControlAnIndexCreatedOverExistingRowsAnswersTest()
    {
        var path = Path.Combine(m_root, "built-over-rows.witdb");

        using var connection = new WitDbConnection($"Data Source={path}");
        connection.Open();

        Execute(connection, "CREATE TABLE T (Id INTEGER PRIMARY KEY AUTOINCREMENT, V INT NOT NULL)");

        for (var i = 0; i < ROWS; i++)
            Execute(connection, $"INSERT INTO T (V) VALUES ({i % DISTINCT_VALUES})");

        // The rows are there first, so this build has to read them.
        Execute(connection, "CREATE INDEX IX_T_V ON T (V)");

        Assert.Multiple(() =>
        {
            Assert.That(RowsForTheProbeValue(connection), Is.EqualTo(MATCHING_ROWS),
                "an index built over rows that were already there must answer for them");
            Assert.That(QueryPlan(connection), Does.Contain("USING INDEX"),
                "and through the index, or this control is measuring a table scan");
        });
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: the fixture built an index that answers, so a wrong answer elsewhere is about what
    /// the case did to it.
    /// </summary>
    [Test]
    public void ControlTheOriginalAnswersTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_original}");
        connection.Open();

        Assert.Multiple(() =>
        {
            Assert.That(RowsForTheProbeValue(connection), Is.EqualTo(MATCHING_ROWS));
            Assert.That(QueryPlan(connection), Does.Contain("USING INDEX"),
                "and it must be answered THROUGH the index, or this fixture is measuring a table "
                + "scan and would pass with no index at all");
        });
    }

    /// <summary>
    /// Control: copied as a set, it answers - so the difference the rule is about is the sidecar and
    /// not the copying.
    /// </summary>
    [Test]
    public void ControlADatabaseCopiedAsASetAnswersTest()
    {
        var copy = CopyDatabaseAsASet("whole.witdb");

        using var connection = new WitDbConnection($"Data Source={copy}");
        connection.Open();

        Assert.That(RowsForTheProbeValue(connection), Is.EqualTo(MATCHING_ROWS));
    }

    /// <summary>
    /// Control on the COST of the fix, and the reason it is not "rebuild anything empty": an index
    /// over a column that is NULL in every row is legitimately empty, and must still answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GREEN before the fix and after it. What it does NOT measure is whether such an index is
    /// rebuilt needlessly on every open - that costs a full table scan and is invisible from here.
    /// The mechanism-level guard for that is
    /// <c>MissingIndexContentSourceTests</c>, which asserts the fact the fix keys on rather than its
    /// effect; this case exists so that the ANSWER is pinned as well as the mechanism.
    /// </para>
    /// </remarks>
    [Test]
    public void ControlAnIndexOverAnAllNullColumnStillAnswersTest()
    {
        var path = Path.Combine(m_root, "all-null.witdb");

        using (var connection = new WitDbConnection($"Data Source={path}"))
        {
            connection.Open();

            Execute(connection, "CREATE TABLE N (Id INTEGER PRIMARY KEY AUTOINCREMENT, V INT)");
            Execute(connection, "CREATE INDEX IX_N_V ON N (V)");

            for (var i = 0; i < 50; i++)
                Execute(connection, "INSERT INTO N (V) VALUES (NULL)");
        }

        using (var reopened = new WitDbConnection($"Data Source={path}"))
        {
            reopened.Open();

            using var command = reopened.CreateCommand();
            command.CommandText = "SELECT Id FROM N";

            var rows = 0;

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    rows++;
            }

            Assert.That(rows, Is.EqualTo(50),
                "an index that is legitimately empty must not stop the table answering");
        }
    }

    #endregion

    #region Tools

    private string CopyDatabaseFileAlone(string name)
    {
        var destination = Path.Combine(m_root, name);
        File.Copy(m_original, destination);

        return destination;
    }

    private string CopyDatabaseAsASet(string name)
    {
        var destination = CopyDatabaseFileAlone(name);
        var indexes = m_original + INDEX_SUFFIX;

        Assert.That(Directory.Exists(indexes), Is.True,
            "the original must have an index sidecar, or every case here is about nothing");

        Directory.CreateDirectory(destination + INDEX_SUFFIX);

        foreach (var file in Directory.GetFiles(indexes))
            File.Copy(file, Path.Combine(destination + INDEX_SUFFIX, Path.GetFileName(file)));

        return destination;
    }

    private static int TotalRows(DbConnection connection)
    {
        using var command = connection.CreateCommand();

        // Scanned, not counted: on this engine COUNT(*) is answered from a cached per-table counter,
        // which is separate state and has disagreed with the rows before.
        command.CommandText = "SELECT Id FROM T";

        var rows = 0;

        using var reader = command.ExecuteReader();

        while (reader.Read())
            rows++;

        return rows;
    }

    private static int RowsForTheProbeValue(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id FROM T WHERE V = {PROBE_VALUE}";

        var rows = 0;

        using var reader = command.ExecuteReader();

        while (reader.Read())
            rows++;

        return rows;
    }

    private static string QueryPlan(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN SELECT Id FROM T WHERE V = {PROBE_VALUE}";

        var plan = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            for (var i = 0; i < reader.FieldCount; i++)
                plan.Add($"{reader.GetValue(i)}");
        }

        return string.Join(" ", plan);
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    #endregion
}
