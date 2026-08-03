using OutWit.Database.AdoNet;
using OutWit.Database.CrashRunner;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// Phase 11 instrument E - durability crossed with configuration. Does a committed transaction
/// survive a process kill under a transaction model, a journal, a store or an encryption setting
/// other than the default?
/// </summary>
/// <remarks>
/// <para>
/// The thirteen <c>Category=Crash</c> tests this project had all ran <b>one</b> configuration: a bare
/// <c>Data Source=</c>, which is MVCC with a synchronous commit. Durability is precisely the property
/// a configuration decides - the transaction model chooses what a commit writes, <c>Journal</c>
/// chooses whether there is a log to replay, <c>Synchronous Commit</c> chooses whether the commit
/// waits for the write, and the LSM store keeps a write-ahead log of its own. Every one of those was
/// unmeasured under a kill.
/// </para>
/// <para>
/// <b>The control comes first, per configuration.</b> A clean close must keep every row. A
/// configuration that cannot store 20 rows when nothing goes wrong says nothing at all about what a
/// crash costs it, and would otherwise report as spectacular data loss.
/// </para>
/// <para>
/// <b>Two promises, kept apart.</b> Where the configuration has a commit to make durable and does not
/// disclaim it, survival is <b>asserted</b>. Where it does not - <c>Transactions=false</c> has no
/// commit, and <c>Synchronous Commit=false</c> is documented as surviving only a clean shutdown -
/// what survives is <b>recorded, not asserted</b>: it is calibration, and a real defect has to lose
/// more than that or lose it differently. The scenario reports which of the two it took, so a run
/// that quietly fell back to autocommit cannot be read as a commit that held.
/// </para>
/// <para>
/// Rows are counted by <b>scanning them back</b>, never by <c>COUNT(*)</c>: that is a cached counter
/// on this engine, persisted separately from the rows, and phase 4 spent a session on a false report
/// of lost commits built on exactly that. The count is read too, and a disagreement between the two
/// is itself reported.
/// </para>
/// </remarks>
[TestFixture]
[Category("Crash")]
public sealed class DurabilityByConfigurationTests
{
    #region Types

    /// <param name="CommitIsPromisedDurable">
    /// Whether this configuration promises that a returned commit survives a process kill. False for
    /// the two that disclaim it, and their result is recorded rather than asserted.
    /// </param>
    public sealed record Configuration(string Label, string Settings, bool CommitIsPromisedDurable)
    {
        public override string ToString() => Label;
    }

    #endregion

    #region Constants

    private const int ROWS = 20;

    #endregion

    #region Fields

    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_durcfg_{Guid.NewGuid():N}");
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

    #region The configurations

    private static IEnumerable<Configuration> Configurations()
    {
        // The reference: what the other thirteen crash tests already measure, repeated here so the
        // table is readable on its own and a change to the default shows up in this fixture too.
        yield return new Configuration("default (mvcc)", "", CommitIsPromisedDurable: true);

        yield return new Configuration("locks", "MVCC=false", CommitIsPromisedDurable: true);
        yield return new Configuration("locks + wal", "MVCC=false;Journal=wal", CommitIsPromisedDurable: true);
        yield return new Configuration("locks + rollback", "MVCC=false;Journal=rollback", CommitIsPromisedDurable: true);
        yield return new Configuration("lsm", "Store=lsm", CommitIsPromisedDurable: true);
        yield return new Configuration("lsm + locks", "Store=lsm;MVCC=false", CommitIsPromisedDurable: true);

        yield return new Configuration(
            "encrypted",
            "Encryption=aes-gcm;Password=durability-secret;FastEncryption=true",
            CommitIsPromisedDurable: true);

        // Documented as trading durability for throughput: "a successful COMMIT survives only a clean
        // shutdown, not a process kill". Recorded, not asserted - and if it ever DID survive, that is
        // worth knowing too, because then the setting is buying nothing.
        yield return new Configuration("sync=off", "Synchronous Commit=false", CommitIsPromisedDurable: false);

        // No transaction layer at all, so there is no commit to keep. Calibration.
        yield return new Configuration("no transactions", "Transactions=false", CommitIsPromisedDurable: false);
    }

    #endregion

    #region Control - a clean close keeps everything

    /// <summary>
    /// Control, per configuration: nothing is killed, so every row must be there. If this fails the
    /// fixture is measuring a broken configuration rather than durability.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Configurations))]
    public void ControlACleanCloseKeepsEveryRowTest(Configuration configuration)
    {
        var result = CrashRunnerHarness.RunToCompletion(
            Scenarios.CONFIGURED_CONTROL_CLEAN, m_databasePath, ROWS, "T", configuration.Settings);

        var (scanned, counted) = RowsInReopenedDatabase(configuration);

        TestContext.Out.WriteLine(
            $"DURABILITY {configuration.Label,-18} clean close  mode={result.Facts.GetValueOrDefault("mode")}  " +
            $"scanned={scanned}  count(*)={counted}");

        Assert.That(scanned, Is.EqualTo(ROWS),
            $"{configuration.Label} closed cleanly and lost rows - no crash verdict for this " +
            "configuration means anything until that is fixed");
    }

    #endregion

    #region The probe

    /// <summary>
    /// Write the rows the strongest way the configuration allows, then kill the process with no close,
    /// no flush and no dispose - and reopen through the same configuration.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Configurations))]
    public void CommitSurvivesAProcessKillTest(Configuration configuration)
    {
        string mode;

        using (var run = CrashRunnerHarness.Start(
                   Scenarios.CONFIGURED_COMMIT_KILL, m_databasePath, ROWS, "T", configuration.Settings))
        {
            var facts = run.WaitFor(CrashProtocol.KILL_ME);
            mode = facts.GetValueOrDefault("mode", "(not reported)");

            run.Kill();
        }

        var (scanned, counted) = RowsInReopenedDatabase(configuration);

        TestContext.Out.WriteLine(
            $"DURABILITY {configuration.Label,-18} killed       mode={mode}  " +
            $"scanned={scanned}  count(*)={counted}  " +
            (configuration.CommitIsPromisedDurable ? "(asserted)" : "(recorded, not asserted)"));

        if (!configuration.CommitIsPromisedDurable)
        {
            Assert.That(scanned, Is.LessThanOrEqualTo(ROWS),
                $"{configuration.Label} returned more rows than were ever written, which is not a " +
                "durability result at all - the probe is measuring the wrong database");
            return;
        }

        Assert.That(mode, Is.EqualTo("transaction"),
            $"{configuration.Label} was expected to commit a transaction and wrote with {mode} instead - " +
            "asserting durability of a commit that never happened would be measuring the wrong promise");

        Assert.That(scanned, Is.EqualTo(ROWS),
            $"{configuration.Label}: the transaction committed and the process was then killed, and " +
            $"{scanned} of {ROWS} rows came back. This configuration promises a commit that survives a " +
            "process kill.");
    }

    #endregion

    #region Tools

    /// <summary>
    /// What the database returns when it is reopened through the same configuration that wrote it -
    /// which is now compulsory: 12.0.0 refuses an open whose transaction model does not match the file.
    /// </summary>
    /// <remarks>
    /// Both numbers are read on purpose. The rows are the subject; <c>COUNT(*)</c> is separate state
    /// with separate persistence on this engine, so the two can disagree after a crash - and that
    /// disagreement is a finding rather than a reason to distrust the scan.
    /// </remarks>
    private (int Scanned, long Counted) RowsInReopenedDatabase(Configuration configuration)
    {
        using var connection = new WitDbConnection(ConnectionString(configuration));
        connection.Open();

        // A kill can take the table with it - the CREATE is a write like any other - and that is a
        // result rather than an error: no table is no rows. It is kept distinct from "the table is
        // there and empty" only in the message, because to a consumer both are the data being gone.
        var scanned = 0;

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM T";

            using var reader = command.ExecuteReader();
            while (reader.Read())
                scanned++;
        }
        catch (Exception e) when (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0);
        }

        long counted;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM T";
            counted = Convert.ToInt64(command.ExecuteScalar());
        }

        return (scanned, counted);
    }

    private string ConnectionString(Configuration configuration)
    {
        return string.IsNullOrEmpty(configuration.Settings)
            ? $"Data Source={m_databasePath}"
            : $"Data Source={m_databasePath};{configuration.Settings}";
    }

    #endregion
}
