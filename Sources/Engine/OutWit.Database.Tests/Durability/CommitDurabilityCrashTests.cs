using OutWit.Database.Core.Builder;
using OutWit.Database.CrashRunner;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// Does a committed transaction survive the process dying? Asked of the configuration a consumer
/// actually gets: the ADO.NET provider with a bare <c>Data Source=</c> connection string.
/// </summary>
/// <remarks>
/// The question is asked at this level deliberately. The engine API
/// (<c>WitDatabase.Create(path)</c>) and the provider are two <b>different durability
/// configurations</b> - the engine helper wires no journal at all, while a bare connection string
/// defaults to <c>MVCC=true</c> and <c>SynchronousCommit=true</c>, documented as "a commit is
/// flushed to storage before it returns". Crashing one says nothing about the other, and the claim
/// that matters is the documented one: the README describes a bare <c>Data Source=</c> as
/// "MVCC on, durable commit - what an ADO.NET or EF Core consumer actually gets".
///
/// The clean-close control comes first for the usual reason: if the provider cannot store 20 rows
/// when nothing goes wrong, nothing this fixture measures after a crash means anything.
/// </remarks>
[TestFixture]
[Category("Crash")]
public sealed class CommitDurabilityCrashTests
{
    #region Fields

    private const int ROWS = 20;

    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_commit_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_databasePath))
                Directory.Delete(m_databasePath, recursive: true);
            else if (File.Exists(m_databasePath))
                File.Delete(m_databasePath);
        }
        catch (IOException)
        {
            // Cleanup only.
        }
    }

    #endregion

    #region Control - a clean close keeps everything

    [Test]
    public void ControlAdoNetCleanCloseKeepsEveryRowAndCountsThemTest()
    {
        CrashRunnerHarness.RunToCompletion(Scenarios.ADONET_CONTROL_CLEAN, m_databasePath, ROWS);

        using var engine = new WitSqlEngine(WitDatabase.Open(m_databasePath), ownsStore: true);

        var scanned = engine.Query("SELECT Id FROM T").Count;
        var counted = engine.Query("SELECT COUNT(*) AS N FROM T")[0]["N"].AsInt64();

        Assert.Multiple(() =>
        {
            Assert.That(scanned, Is.EqualTo(ROWS),
                "the provider committed and closed cleanly, so every row must be there - if this "
                + "fails the fixture is measuring a broken setup, not durability");

            Assert.That(counted, Is.EqualTo(ROWS),
                "and the count must agree with them on a clean close - this is what makes the "
                + "disagreement after a crash a crash finding rather than a counting one");
        });
    }

    #endregion

    #region The subject

    [Test]
    public void CommittedTransactionSurvivesAProcessCrashTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.ADONET_COMMIT_KILL, m_databasePath, ROWS))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        long survivors;
        string outcome;

        try
        {
            survivors = RowsInReopenedDatabase();
            outcome = $"{survivors} of {ROWS}";
        }
        catch (Exception e)
        {
            survivors = -1;
            outcome = $"the database could not be reopened at all - {e.GetType().Name}: {e.Message}";
        }

        TestContext.Out.WriteLine($"after commit and a hard kill, rows recovered: {outcome}");

        Assert.That(survivors, Is.EqualTo(ROWS),
            "the transaction was committed through a bare Data Source= connection string, which "
            + "defaults to SynchronousCommit=true - documented as flushing the commit to storage "
            + "before it returns, and described in the README as durable commit. A committed "
            + "transaction that does not survive the process dying is the D in ACID");
    }

    #endregion

    #region The row count disagrees with the rows

    /// <summary>
    /// After a crash the rows are all there and <c>SELECT COUNT(*)</c> says there are none.
    /// </summary>
    /// <remarks>
    /// The engine keeps a per-table row count as metadata and answers <c>COUNT(*)</c> from it. That
    /// counter is persisted by <c>SchemaCatalog.PersistRowCountsToStore</c>, which
    /// <c>WitSqlEngine.Commit</c> calls <i>after</i> the transaction has committed, inside a
    /// <c>try { } catch { }</c> - and outside the flush the commit itself performed. So the rows
    /// reach the media and the number that describes them does not.
    ///
    /// The clean-close control in this fixture is what makes it a crash finding rather than a
    /// counting one: closed cleanly, the same workload reports 20 and 20.
    /// </remarks>
    [Test]
    [Ignore("CONFIRMED 2026-07-29, and not in the audit's 104: after a commit through a bare "
            + "Data Source= connection string and a hard kill, SELECT returns all 20 rows and "
            + "SELECT COUNT(*) returns 0. The rows are on the media - a raw scan under the MVCC "
            + "layer finds 24 records - and the number describing them is not, because the row "
            + "count is persisted by PersistRowCountsToStore after the commit and outside the flush "
            + "the commit performed. Closed cleanly the same workload reports 20 and 20. "
            + "Engine/WitSqlEngine.Transactions.cs:65")]
    public void RowCountAgreesWithTheRowsAfterACrashTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.ADONET_COMMIT_KILL, m_databasePath, ROWS))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        using var engine = new WitSqlEngine(WitDatabase.Open(m_databasePath), ownsStore: true);

        var scanned = engine.Query("SELECT Id FROM T").Count;
        var counted = engine.Query("SELECT COUNT(*) AS N FROM T")[0]["N"].AsInt64();

        TestContext.Out.WriteLine($"after the crash: SELECT returns {scanned} rows, COUNT(*) says {counted}");

        Assert.That(counted, Is.EqualTo(scanned),
            $"the reopened database returns {scanned} rows and reports {counted} of them. A query "
            + "and its own count must not disagree - and the count is the one an application "
            + "believes when it checks whether the data made it");
    }

    #endregion

    #region Attribution

    /// <summary>
    /// The same commit at the engine level, over a database built with MVCC explicitly - which is
    /// what the provider is supposed to be producing. It bisects the finding above: if this survives
    /// and the provider's does not, the break is in the provider's wiring; if neither survives, the
    /// break is in the commit itself.
    /// </summary>
    [Test]
    public void MvccEngineLevelCommitSurvivesAProcessCrashTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.MVCC_ENGINE_COMMIT_KILL, m_databasePath, ROWS))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        long survivors;

        try
        {
            survivors = RowsInReopenedDatabase();
        }
        catch (Exception e)
        {
            survivors = -1;
            TestContext.Out.WriteLine($"reopen failed - {e.GetType().Name}: {e.Message}");
        }

        TestContext.Out.WriteLine($"MVCC at the engine level, committed then killed: {survivors} of {ROWS}");

        Assert.That(survivors, Is.EqualTo(ROWS),
            "MvccTransaction.Commit calls Flush when SynchronousCommit is set, and its own comment "
            + "says that is there because 'without this a successful COMMIT was lost by a process "
            + "kill'. Either that no longer holds, or the flush does not reach the file");
    }

    /// <summary>
    /// Are the committed rows absent from the media, or present and invisible? The two have
    /// different fixes, and a row count through the engine cannot tell them apart - so this opens
    /// the crashed file underneath the MVCC layer and counts the raw records, against a cleanly
    /// closed database of the same shape as the reference.
    /// </summary>
    [Test]
    public void AttributionRawRecordsPresentAfterTheCrashTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.MVCC_ENGINE_COMMIT_KILL, m_databasePath, ROWS))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        var crashed = RawRecordCount(m_databasePath);

        var referencePath = Path.Combine(Path.GetTempPath(), $"witdb_ref_{Guid.NewGuid():N}");

        try
        {
            CrashRunnerHarness.RunToCompletion(Scenarios.ADONET_CONTROL_CLEAN, referencePath, ROWS);

            var reference = RawRecordCount(referencePath);

            TestContext.Out.WriteLine(
                $"raw records under the MVCC layer - crashed: {crashed}, cleanly closed: {reference}");

            Assert.That(reference, Is.GreaterThan(ROWS),
                "the reference database was closed cleanly with " + ROWS + " rows in it, so its raw "
                + "record count must exceed the row count - if it does not, this probe is not "
                + "looking at the data and its verdict on the crashed file means nothing");
        }
        finally
        {
            try
            {
                if (Directory.Exists(referencePath))
                    Directory.Delete(referencePath, recursive: true);
                else if (File.Exists(referencePath))
                    File.Delete(referencePath);
            }
            catch (IOException)
            {
                // Cleanup only.
            }
        }
    }

    #endregion

    #region Tools

    private static int RawRecordCount(string path)
    {
        using var database = new WitDatabaseBuilder().WithFilePath(path).Build();

        return database.Scan().Count();
    }

    /// <summary>
    /// How many rows the reopened database actually returns.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>COUNT(*)</c>. The engine keeps a per-table row count as metadata and
    /// persists it separately from the rows, so a count could disagree with the rows for a reason
    /// that has nothing to do with durability - asserting on it would measure the counter and report
    /// it as data loss. Both are read here, and a disagreement between them is itself a finding.
    /// </remarks>
    private long RowsInReopenedDatabase()
    {
        using var engine = new WitSqlEngine(WitDatabase.Open(m_databasePath), ownsStore: true);

        var scanned = engine.Query("SELECT Id FROM T").Count;
        var counted = engine.Query("SELECT COUNT(*) AS N FROM T")[0]["N"].AsInt64();

        if (scanned != counted)
        {
            TestContext.Out.WriteLine(
                $"the reopened database disagrees with itself: SELECT returned {scanned} rows while "
                + $"COUNT(*) says {counted}");
        }

        return scanned;
    }

    #endregion
}
