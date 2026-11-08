using System.Data.Common;
using OutWit.Database.AdoNet;
using OutWit.Database.CrashRunner;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// The index entries of a committed transaction survive the process dying without closing the
/// database. A client application that never disposed its connection found the rows of every
/// session on disk and none of their index entries: the commit flushed the main store, and the
/// secondary indexes - separate stores - were written only on Dispose. The scenario runs two
/// sessions as that application did: the first closes cleanly and leaves an index file with
/// content, so the second session's index is believed rather than rebuilt; the second commits
/// and is killed. The probe is a row of the second session.
/// </summary>
[TestFixture]
[Category("Crash")]
public sealed class IndexCommitCrashTests
{
    #region Constants

    private const int ROWS = 20;

    /// <summary>A row of the first session, closed cleanly: found with or without the defect.</summary>
    private const int FIRST_SESSION_VALUE = 3;

    /// <summary>A row of the second session, committed and then killed: the subject.</summary>
    private const int PROBE_VALUE = ROWS - 1;

    private const string INDEX_NAME = "IX_T_V";

    #endregion

    #region Fields

    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_index_{Guid.NewGuid():N}");
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

    #region Control - a clean close keeps the index

    [Test]
    public void ControlIndexAnswersAfterACleanCloseTest()
    {
        CrashRunnerHarness.RunToCompletion(Scenarios.INDEX_CONTROL_CLEAN, m_databasePath, ROWS);

        using var connection = Connect();

        Assert.Multiple(() =>
        {
            Assert.That(RowsForValue(connection, PROBE_VALUE), Is.EqualTo(1),
                "the provider committed and closed cleanly, so the index must answer the probe - "
                + "if this fails the fixture is measuring a broken setup, not durability");

            Assert.That(QueryPlan(connection), Does.Contain("USING INDEX"),
                "and through the index, or this control is measuring a table scan and the crash "
                + "case below would pass with no index at all");
        });
    }

    #endregion

    #region The subject

    [Test]
    public void IndexEntriesOfACommittedTransactionSurviveAProcessCrashTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.INDEX_COMMIT_KILL, m_databasePath, ROWS))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        using var connection = Connect();

        var scanned = TotalRows(connection);
        var firstSession = RowsForValue(connection, FIRST_SESSION_VALUE);
        var throughTheIndex = RowsForValue(connection, PROBE_VALUE);
        var plan = QueryPlan(connection);

        TestContext.Out.WriteLine(
            $"after commit and a hard kill: {scanned} rows by scan, {firstSession} for V = {FIRST_SESSION_VALUE} "
            + $"(first session), {throughTheIndex} for V = {PROBE_VALUE} (second session); plan: {plan}");

        Assert.Multiple(() =>
        {
            Assert.That(scanned, Is.EqualTo(ROWS),
                "the rows are the control: adonet-commit-kill proves a committed transaction "
                + "survives a kill, so losing rows here is a different defect from the one measured");

            Assert.That(plan, Does.Contain("USING INDEX"),
                "the probe must be answered through the index, or the assertion below measures a "
                + "scan instead of the index content");

            Assert.That(firstSession, Is.EqualTo(1),
                "the first session closed cleanly, so its index entries are in the file whatever the "
                + "second session did - if this fails the index file itself did not survive, which "
                + "is a different defect");

            Assert.That(throughTheIndex, Is.EqualTo(1),
                "the transaction was committed and the row is on disk; the index entry that names "
                + "it must be too. Before 14.0.2 the index store was flushed only on Dispose, and a "
                + "lookup after a restart found none of the last session's rows");
        });
    }

    #endregion

    #region Tools

    private DbConnection Connect()
    {
        var connection = new WitDbConnection($"Data Source={m_databasePath}");
        connection.Open();
        return connection;
    }

    private static int TotalRows(DbConnection connection)
    {
        using var command = connection.CreateCommand();

        // Scanned, not counted: COUNT(*) is answered from a cached counter, which is separate state.
        command.CommandText = "SELECT Id FROM T";

        var rows = 0;

        using var reader = command.ExecuteReader();

        while (reader.Read())
            rows++;

        return rows;
    }

    private static int RowsForValue(DbConnection connection, int value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id FROM T WHERE V = {value}";

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

        var text = string.Join(" ", plan);

        Assert.That(text, Does.Contain(INDEX_NAME).IgnoreCase.Or.Contain("USING INDEX"),
            "the plan must name the index the scenario created, so a rename in the scenario is caught here");

        return text;
    }

    #endregion
}
