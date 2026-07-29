using OutWit.Database.Core.Builder;
using OutWit.Database.CrashRunner;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// The <c>core-durability</c> finding the 2026-07 audit recorded as <b>not reproducible with the
/// current surface</b>: "auto-increment / rowid counters are written after the commit fsync and never
/// flushed, so after a crash the next INSERT reuses a live rowid"
/// (<c>WitSqlEngine.Transactions.cs:56</c>).
/// </summary>
/// <remarks>
/// The verdict was provisional for a mechanical reason, not a doubt about the claim: a file-backed
/// engine opens its storage with <c>FileShare.None</c>, so a second engine cannot be opened over a
/// live database - and disposing the first engine is exactly the operation that flushes the counters.
/// The out-of-process runner removes that obstacle.
///
/// <b>The mechanism, read out of the code on 2026-07-29 and sharper than the finding states:</b>
/// inside a transaction <c>SchemaCatalog.SaveTableRowId</c> writes <i>nothing</i> to the store - it
/// updates the in-memory cache only and leaves persistence to <c>PersistRowIdsToStore()</c>, which
/// <c>WitSqlEngine.Commit</c> calls <i>after</i> the transaction has committed, inside a
/// <c>try { } catch { }</c> that swallows every failure by design. So the rows can reach durable
/// storage while the counter that names them does not.
///
/// This test states the behaviour that must hold. Whether a bare process kill is enough to provoke
/// the loss is a separate question, and the C3 control in
/// <see cref="CrashHarnessControlTests"/> is what it has to be read against: on a platform where the
/// operating system writes its cache back after a kill, a kill alone may show nothing, and the
/// modelled power cut of instrument B is what would settle it.
/// </remarks>
[TestFixture]
[Category("Crash")]
public sealed class RowIdCounterCrashTests
{
    #region Fields

    private const int ROWS = 20;

    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_rowid_{Guid.NewGuid():N}");
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

    #region Tests

    [Test]
    public void RowIdIsNotReusedAfterACrashTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.ROWID_COMMIT_KILL, m_databasePath, ROWS))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        using var engine = new WitSqlEngine(WitDatabase.Open(m_databasePath), ownsStore: true);

        var survivingRows = Count(engine);
        var highestSurviving = HighestRowId(engine);

        // Nothing to say about identity reuse if no row survived the crash at all - that is the C3
        // baseline's business, not this test's, and asserting through it would report the wrong
        // defect.
        Assume.That(survivingRows, Is.GreaterThan(0),
            "no row survived the crash, so this test cannot say anything about counter reuse - see "
            + "the C3 control for what a kill costs on this platform");

        engine.Execute("INSERT INTO T (V) VALUES (999)");

        var assignedRowId = engine.LastInsertRowId;

        TestContext.Out.WriteLine(
            $"after the crash: {survivingRows} rows survived, highest surviving id {highestSurviving}, "
            + $"the next insert was given id {assignedRowId}");

        Assert.That(assignedRowId, Is.GreaterThan(highestSurviving),
            $"the crash left {survivingRows} rows behind with ids up to {highestSurviving}, and the "
            + $"next insert was handed {assignedRowId} - an identity that is already in use. The "
            + "counter is persisted after the commit returns and was never flushed, so recovery "
            + "reads a value that the surviving rows have already passed");

        Assert.That(Count(engine), Is.EqualTo(survivingRows + 1),
            "the new row must be an addition, not an overwrite of the row whose id it was given");
    }

    #endregion

    #region Tools

    // By scanning, not by COUNT(*): the engine answers COUNT(*) from a cached per-table counter that
    // is persisted separately from the rows, and after a crash the two disagree - see
    // CommitDurabilityCrashTests.RowCountAgreesWithTheRowsAfterACrashTest. Counting here would
    // measure that defect instead of this one.
    private static long Count(WitSqlEngine engine) =>
        engine.Query("SELECT Id FROM T").Count;

    private static long HighestRowId(WitSqlEngine engine) =>
        engine.Query("SELECT MAX(Id) AS M FROM T")[0]["M"].AsInt64();

    #endregion
}
