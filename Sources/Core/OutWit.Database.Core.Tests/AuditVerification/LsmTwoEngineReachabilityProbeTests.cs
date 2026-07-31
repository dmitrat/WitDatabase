using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Exceptions;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Phase 5 closure - is the shape § 3a promised an experiment about still reachable?
/// </summary>
/// <remarks>
/// <para>
/// § 3a found that an LSM database admitted a second engine on Linux, because .NET maps
/// <c>FileShare.Read</c> to a <b>shared</b> advisory lock there, and the two engines then diverged. It
/// named a follow-up experiment — two engines interleaving flushes and a compaction over overlapping key
/// ranges — and said it belonged with "whatever mechanism closes § 3a". That mechanism is the exclusive
/// <c>.lock</c> sidecar taken in <c>WitDatabaseBuilder.Build</c>.
/// </para>
/// <para>
/// So the experiment's premise has to be re-checked before the experiment is worth running: <b>can two
/// engines still exist over one LSM database at all?</b> In the default configuration
/// <c>ProbeLsmWithoutWalIsStillExclusiveTest</c> already answers no, on both platforms and whatever files
/// the store creates. This fixture asks the one remaining route — <c>FileLocking=false</c>, which
/// <c>WitSQL.md</c> § 15.0 documents as <i>disabling the guard</i> for filesystems where advisory locking
/// is unreliable.
/// </para>
/// <para>
/// <b>The answer is platform-dependent and this machine cannot give it.</b> On Windows the second engine
/// is refused even with the guard off, because the write-ahead log's own share mode refuses it — which is
/// exactly the asymmetry § 3a is about. The Linux half is what CI answers, and the probe reports rather
/// than assumes.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class LsmTwoEngineReachabilityProbeTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_3a_reach_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
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

    #region Probes

    /// <summary>
    /// Probe: the default configuration must refuse a second engine over one LSM database, on both
    /// platforms. This is the model's promise, and § 3a's shape depends on it being false.
    /// </summary>
    [Test]
    public void ProbeTheDefaultConfigurationRefusesASecondLsmEngineTest()
    {
        var outcome = OpenTwoEngines("default", fileLocking: true);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Refused, Is.Not.Null,
                $"a second engine opened one LSM database on {Platform} - § 3a's shape is reachable "
                + "in the DEFAULT configuration, which the model forbids");
            Assert.That(outcome.Refused, Is.TypeOf<DatabaseAlreadyOpenException>(),
                "the refusal must come from the engine's own guard, not from an OS sharing violation");
        });
    }

    /// <summary>
    /// Probe: and with the guard explicitly turned off, which is the only remaining route to two
    /// engines. Reported per platform and asserted only where the answer is known, because § 3a exists
    /// precisely because a Windows measurement was generalised once already.
    /// </summary>
    [Test]
    public void ProbeFileLockingOffIsTheOnlyRouteLeftToTwoEnginesTest()
    {
        var outcome = OpenTwoEngines("FileLocking=false", fileLocking: false);

        if (OperatingSystem.IsWindows())
        {
            // OBSERVATION, NOT A GUARANTEE. With the guard off, what refuses the second engine is the
            // write-ahead log's own share mode - FileShare.Read, which Windows reads as "no second
            // writer". That is the very mechanism § 3a showed does NOT hold on Unix, so this assertion
            // deliberately does not generalise.
            Assert.That(outcome.Refused, Is.Not.Null,
                "Windows used to refuse this through the write-ahead log's share mode");
            return;
        }

        // PINS A DOCUMENTED SHARP EDGE, NOT A DEFECT. Answered by the Linux runner 2026-07-31, and it
        // is § 3a's original finding reproduced exactly: with the guard off, .NET maps the write-ahead
        // log's FileShare.Read to a SHARED advisory lock, both engines open, and the two disagree.
        //
        // The asymmetry is not arbitrary. The second engine replays wal.log when it opens, which
        // already holds the first engine's row, so it sees it; the first engine cannot see the
        // second's row at all, because that row lives in a memtable belonging to another engine and
        // nothing invalidates or notifies. Both writes survive - this is divergence, not loss.
        //
        // If a later change makes this refuse instead, that is an improvement and this pin should be
        // inverted rather than deleted: it is the only executed statement of what FileLocking=false
        // costs on the platform the deployment target runs on.
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Refused, Is.Null,
                "the guard is off, so nothing was expected to refuse the second engine here");
            Assert.That(outcome.FirstSeesSecondsRow, Is.False,
                "the first engine can now see a row written into another engine's memtable");
            Assert.That(outcome.SecondSeesFirstsRow, Is.True,
                "the second engine no longer replays the log it opened on top of");
        });
    }

    #endregion

    #region Scenario

    private sealed class TwoEngineOutcome
    {
        public Exception? Refused { get; init; }

        public bool FirstSeesSecondsRow { get; init; }

        public bool SecondSeesFirstsRow { get; init; }

        public string Divergence { get; init; } = "";
    }

    /// <summary>
    /// Builds one engine over an LSM directory, writes a row, and tries to build a second. If the second
    /// opens, it also records whether the two agree about each other's rows - the § 3a symptom.
    /// </summary>
    private TwoEngineOutcome OpenTwoEngines(string label, bool fileLocking)
    {
        var dir = Path.Combine(m_testDir, $"lsm_{fileLocking}");

        using var first = Build(dir, fileLocking);
        first.Put("a", Bytes("1"));
        first.Flush();

        Exception? refused = null;
        WitDatabase? second = null;

        try
        {
            second = Build(dir, fileLocking);
        }
        catch (Exception e)
        {
            refused = e;
        }

        var divergence = "";
        var firstSeesB = false;
        var secondSeesA = false;

        if (second != null)
        {
            second.Put("b", Bytes("2"));
            second.Flush();

            // Rows are read back, never counted through a cached counter.
            firstSeesB = first.Get("b") != null;
            secondSeesA = second.Get("a") != null;

            divergence = $"first sees the second's row = {firstSeesB}, "
                       + $"second sees the first's row = {secondSeesA}";

            second.Dispose();
        }

        TestContext.Out.WriteLine(
            $"PROBE  [{Platform}] two engines over one LSM database <{label}>  ->  "
            + (refused is null ? $"BOTH OPEN; {divergence}" : $"refused with {refused.GetType().Name}"));

        return new TwoEngineOutcome
        {
            Refused = refused,
            FirstSeesSecondsRow = firstSeesB,
            SecondSeesFirstsRow = secondSeesA,
            Divergence = divergence
        };
    }

    private static WitDatabase Build(string dir, bool fileLocking)
    {
        var builder = new WitDatabaseBuilder().WithLsmTree(dir).WithoutTransactions();

        if (!fileLocking)
            builder = builder.WithoutFileLocking();

        return builder.Build();
    }

    #endregion

    #region Tools

    private static string Platform => OperatingSystem.IsWindows() ? "windows" : "unix";

    private static byte[] Bytes(string s) => TextEncoding.UTF8.GetBytes(s);

    #endregion
}
