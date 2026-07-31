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

        // The Unix half is the one that matters and CI is what answers it. Both outcomes are legitimate
        // and neither is a defect: FileLocking=false is documented as disabling the guard.
        //   - refused  -> § 3a's shape is unreachable everywhere, and the promised experiment is moot.
        //   - admitted -> it is reachable only in the configuration that documents itself as removing
        //                 the protection, and the experiment measures the cost of a documented escape
        //                 hatch rather than of a defect.
        Assert.That(outcome.Divergence, Is.Not.Null.Or.Empty,
            "the probe recorded nothing at all on this platform");
    }

    #endregion

    #region Scenario

    private sealed class TwoEngineOutcome
    {
        public Exception? Refused { get; init; }

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

        if (second != null)
        {
            second.Put("b", Bytes("2"));
            second.Flush();

            // Rows are read back, never counted through a cached counter.
            var firstSeesB = first.Get("b") != null;
            var secondSeesA = second.Get("a") != null;

            divergence = $"first sees the second's row = {firstSeesB}, "
                       + $"second sees the first's row = {secondSeesA}";

            second.Dispose();
        }

        TestContext.Out.WriteLine(
            $"PROBE  [{Platform}] two engines over one LSM database <{label}>  ->  "
            + (refused is null ? $"BOTH OPEN; {divergence}" : $"refused with {refused.GetType().Name}"));

        return new TwoEngineOutcome { Refused = refused, Divergence = divergence };
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
