using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.CrashRunner;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// The exclusive database lock, tested across a process boundary — the only place it can be tested.
/// </summary>
/// <remarks>
/// 5.0.0 enforces one engine per database with a <c>.lock</c> sidecar, and two claims were written into
/// <c>WitSQL.md</c> § 15.0 and into <see cref="DatabaseAlreadyOpenException"/>'s own message:
///
/// <list type="number">
/// <item>a second <b>process</b> is refused;</item>
/// <item>the operating system releases the handle when the owning process exits, so a process that dies
/// without shutting down cleanly does <b>not</b> leave the database permanently unopenable.</item>
/// </list>
///
/// Both were prose. Neither is provable in one process: the first because the guard would be arguing with
/// itself, and the second because <b>a crash runs no cleanup</b> — so nothing in the dying process's own
/// code can be what releases the lock, and any in-process test would be measuring <c>Dispose</c> instead.
/// Phase 4 built the out-of-process runner for exactly this class of claim, and this is the missing
/// scenario the phase-5 plan named under question 4.
/// </remarks>
[TestFixture]
[Category("Crash")]
public class DatabaseLockAcrossProcessesTests
{
    #region Fields

    private string m_testDir = null!;
    private string m_databasePath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_lock_proc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
        m_databasePath = Path.Combine(m_testDir, "locked.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Tests

    /// <summary>
    /// While another process holds the database, this one is refused; once that process is killed, this
    /// one can open it.
    /// </summary>
    /// <remarks>
    /// Both halves in one test on purpose. Split in two they could both pass while meaning nothing — a
    /// "refused" test passes if opening never works at all, and a "reopens" test passes if the lock was
    /// never taken. Together, each is the other's control.
    /// </remarks>
    [Test]
    public void SecondProcessIsRefusedWhileTheFirstHoldsTheLockAndSucceedsAfterItDiesTest()
    {
        DatabaseAlreadyOpenException? refused;

        using (var run = CrashRunnerHarness.Start(Scenarios.LOCK_HELD_KILL, m_databasePath))
        {
            var parked = run.WaitFor(CrashProtocol.KILL_ME);

            Assert.That(File.Exists(m_databasePath + ".lock"), Is.True,
                "the other process should be holding the lock sidecar");
            Assert.That(parked["lockPath"], Is.EqualTo(m_databasePath + ".lock"),
                "and the scenario should agree about which file that is");

            // The first half: refused, by a lock held in another process.
            refused = Assert.Throws<DatabaseAlreadyOpenException>(() => OpenHere().Dispose(),
                "another process holds this database, so opening it here must be refused");

            run.Kill();
            run.WaitForExit();
        }

        Assert.That(refused!.DatabasePath, Is.EqualTo(m_databasePath),
            "the exception must name the database it is talking about");

        // The second half: the owner died without running Dispose, and the lock is gone anyway - which
        // is the operating system's doing, not the database's.
        using var reopened = OpenHere();

        Assert.That(reopened, Is.Not.Null,
            "the process holding the lock was killed, so this one must be able to open the database");
    }

    /// <summary>
    /// And the data the killed process had committed is still there — so the lock being released does not
    /// come at the price of the database being unusable.
    /// </summary>
    [Test]
    public void DatabaseIsUsableAfterTheProcessHoldingItsLockIsKilledTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.LOCK_HELD_KILL, m_databasePath))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
            run.WaitForExit();
        }

        using var database = OpenHere();

        // Reading through the engine rather than trusting a count: on this engine COUNT(*) is a cached
        // counter, and phase 4 published a false catastrophe by believing one after a crash.
        using var engine = new Engine.WitSqlEngine(database);
        using var result = engine.Execute("SELECT Id, V FROM T");
        var rows = result.ReadAll();

        Assert.That(rows, Has.Count.EqualTo(1),
            "the row the killed process committed must still be readable");
    }

    /// <summary>
    /// Control: with no other process involved, this one opens the same database happily. If this fails,
    /// the refusal above is not evidence of a lock - it is evidence of something else being wrong.
    /// </summary>
    [Test]
    public void ControlOpeningWithNobodyElseHoldingItSucceedsTest()
    {
        using (var first = OpenHere())
        {
            Assert.That(first, Is.Not.Null);
        }

        using var second = OpenHere();

        Assert.That(second, Is.Not.Null,
            "opening, closing and reopening in one process must work - the lock is released on dispose");
    }

    #endregion

    #region Tools

    private WitDatabase OpenHere() =>
        new WitDatabaseBuilder()
            .WithFilePath(m_databasePath)
            .WithBTree()
            .WithTransactions()
            .WithMvcc()
            .Build();

    #endregion
}
