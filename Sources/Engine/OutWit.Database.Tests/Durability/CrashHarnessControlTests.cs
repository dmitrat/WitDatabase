using OutWit.Database.Core.Builder;
using OutWit.Database.CrashRunner;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// The controls for the out-of-process crash harness. They assert nothing about durability - they
/// assert that the instrument works, and they are what makes every other crash result attributable.
/// </summary>
/// <remarks>
/// Phase 3 had two instruments that reported confidently and wrongly, and a deliberate control case
/// caught both. The rule that came out of it: every comparison harness carries an input whose answer
/// is known and not in dispute, and a red control means suspect the harness before the subject.
///
/// <list type="number">
/// <item><b>C1</b> - nothing is killed, so nothing may be lost. A red C1 means the harness cannot
/// even run a database, and no crash result taken with it means anything.</item>
/// <item><b>C2</b> - the durable path is taken in full and only then is the process killed. A red C2
/// means the kill itself is destroying data, which would make every later measurement a false
/// positive.</item>
/// <item><b>C3</b> - nothing is flushed before the kill. This one <b>records</b> rather than asserts:
/// it is the baseline cost of a process kill on this platform, and a real finding has to lose more
/// than this, or lose it differently.</item>
/// </list>
/// </remarks>
[TestFixture]
[Category("Crash")]
public sealed class CrashHarnessControlTests
{
    #region Fields

    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_crash_{Guid.NewGuid():N}");
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

    #region C1 - a clean run loses nothing

    [Test]
    public void ControlCleanShutdownLosesNothingTest()
    {
        var result = CrashRunnerHarness.RunToCompletion(Scenarios.CONTROL_CLEAN, m_databasePath, rows: 20);

        Assert.That(result.Facts["rows"], Is.EqualTo("20"));
        Assert.That(result.Facts["lastRowId"], Is.EqualTo("20"),
            "the twentieth insert must get row id 20 - if it does not, the fixture's assumptions "
            + "about AUTOINCREMENT are wrong and every rowid result is meaningless");

        Assert.That(RowsInReopenedDatabase(), Is.EqualTo(20),
            "a cleanly shut down database must hold every row it accepted - if this fails the "
            + "harness is broken, and no crash measurement taken with it can be believed");
    }

    #endregion

    #region C2 - a kill after the durable path loses nothing

    [Test]
    public void ControlKillAfterCommitAndFlushLosesNothingTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.CONTROL_DURABLE_KILL, m_databasePath, rows: 20))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        Assert.That(RowsInReopenedDatabase(), Is.EqualTo(20),
            "the transaction was committed and the engine flushed before the kill, so every row "
            + "must survive - if it does not, the kill itself is destroying data and every crash "
            + "scenario measured with this harness is a false positive");
    }

    #endregion

    #region C3 - what a kill costs when nothing was flushed

    /// <summary>
    /// Autocommit writes, killed without any explicit flush - and every row survives.
    /// </summary>
    /// <remarks>
    /// <b>This was the C3 calibration and it is now an assertion, because what it measures changed.</b>
    /// It used to record the baseline cost of a process kill when nothing had been flushed, and that
    /// cost was total: not merely the rows but <i>the table itself</i> - the reopen failed with
    /// <c>Table 'T' not found</c>, because autocommit opened no transaction, so nothing was ever
    /// committed and nothing had reached the file at all. The operating system's write-back cache
    /// never entered into it.
    ///
    /// A statement now runs inside an implicit transaction, so it commits, and committing flushes.
    /// The baseline is no longer a measure of unavoidable loss - it is a property, and it is asserted
    /// as one.
    /// </remarks>
    [Test]
    public void AutocommitWritesSurviveAProcessCrashTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.CONTROL_AUTOCOMMIT_KILL, m_databasePath, rows: 20))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        long survivors;
        string outcome;

        try
        {
            survivors = RowsInReopenedDatabase();
            outcome = $"{survivors} of 20 rows survived";
        }
        catch (Exception e)
        {
            survivors = -1;
            outcome = $"the database could not be reopened at all: {e.GetType().Name}: {e.Message}";
        }

        TestContext.Out.WriteLine($"autocommit, no explicit flush, killed: {outcome}");

        Assert.That(survivors, Is.EqualTo(20),
            "a statement that returned successfully must survive the process dying. Autocommit used "
            + "to open no transaction at all, so nothing was committed and nothing reached the file - "
            + "this same scenario lost every row and the table with them");
    }

    #endregion

    #region The harness fails loudly

    [Test]
    public void UnknownScenarioIsAHarnessFailureNotAResultTest()
    {
        Assert.That(
            () => CrashRunnerHarness.RunToCompletion("no-such-scenario", m_databasePath),
            Throws.TypeOf<CrashHarnessException>(),
            "a scenario the runner does not know must fail as a harness problem - never quietly as "
            + "a database that lost everything");
    }

    #endregion

    #region Tools

    // Scanned rather than counted. The engine answers COUNT(*) from a cached per-table counter which
    // is persisted separately from the rows, so after a crash the two disagree - see
    // CommitDurabilityCrashTests.RowCountAgreesWithTheRowsAfterACrashTest. A control that counted
    // would report a lost counter as lost data, which is exactly the misreading a control exists to
    // prevent.
    private long RowsInReopenedDatabase()
    {
        using var engine = new WitSqlEngine(WitDatabase.Open(m_databasePath), ownsStore: true);

        return engine.Query("SELECT Id FROM T").Count;
    }

    #endregion
}
