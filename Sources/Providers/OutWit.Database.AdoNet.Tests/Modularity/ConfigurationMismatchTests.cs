using System.Text;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 11 instrument C - a database created by one configuration, opened by another.
/// </summary>
/// <remarks>
/// <para>
/// The matrix (instrument B) creates and reopens each database with the <b>same</b> connection string,
/// which is the easy half. A consumer edits a connection string; a deployment ships a different one; a
/// config file drifts. The database file records provider keys in its header - and this phase already
/// proved that header <b>can lie</b>, because <c>Cache=lru</c> was written into it while a sharded
/// clock was built. So "what happens when the two disagree" is not a rhetorical question.
/// </para>
/// <para>
/// Three outcomes, and the third is the one that matters: <b>opens and answers correctly</b>,
/// <b>refuses with a legible message</b>, or <b>opens and answers something else</b>. Silent data
/// divergence across a configuration change is the worst failure this project has a name for.
/// </para>
/// <para>
/// <b>Controls, both directions.</b> Every configuration opened by itself must work - that is the
/// diagonal, and if it fails the harness is broken rather than the engine. And an encrypted database
/// opened without its password must NOT come back readable; if it does, the encryption is not
/// encrypting, which would make every other verdict here beside the point.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class ConfigurationMismatchTests
{
    #region Types

    public sealed record Setup(string Label, string Settings)
    {
        public override string ToString() => Label;
    }

    private enum Outcome
    {
        /// <summary>Opened and returned exactly what was written.</summary>
        Correct,

        /// <summary>Refused at <c>Open</c>. A construction kit may have illegal shapes if it says so.</summary>
        RefusedAtOpen,

        /// <summary>
        /// Opened without complaint, and the data is not there. The dangerous middle: a consumer reads
        /// "Table 'X' not found" as "this database is empty", and the natural next step - create the
        /// schema - writes over a database that was perfectly intact.
        /// </summary>
        OpensAndDataIsGone,

        /// <summary>Opened, answered, and answered something else.</summary>
        Wrong
    }

    /// <param name="SurvivesTheOriginal">
    /// Whether the creator's own configuration can still read the database <b>after</b> the mismatched
    /// open. The attribution half: it separates "unreadable by this configuration" from "destroyed",
    /// and phase 4 established that asking it is what stops a false catastrophe report.
    /// </param>
    private sealed record Verdict(Outcome Outcome, string Detail, bool SurvivesTheOriginal, string SurvivalReason);

    #endregion

    #region Constants

    /// <summary>What the workload writes, read back by scanning rather than counting.</summary>
    private const string EXPECTED = "1:row1|2:row2|3:row3|4:row4|5:row5|6:row6|7:row7|8:row8";

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_mismatch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
        m_sequence = 0;
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region The configurations

    /// <summary>
    /// Configurations that differ in one decision each, so a failing pair names its own cause.
    /// </summary>
    private static readonly Setup[] SETUPS =
    [
        new("default", ""),
        new("aes", "Encryption=aes-gcm;Password=mismatch-secret;FastEncryption=true"),
        new("pagesize", "PageSize=16384"),
        new("cache-lru", "Cache=lru"),
        new("cachesize", "CacheSize=64"),
        new("locks", "MVCC=false"),
        new("no-tx", "Transactions=false"),
        new("lsm", "Store=lsm")
    ];

    private static IEnumerable<TestCaseData> Pairs()
    {
        foreach (var creator in SETUPS)
        foreach (var opener in SETUPS)
            yield return new TestCaseData(creator, opener).SetName($"{{m}}({creator.Label} -> {opener.Label})");
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: every configuration can read back what it wrote itself. The diagonal of the grid.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(SETUPS))]
    public void ControlAConfigurationReadsItsOwnDataTest(Setup setup)
    {
        var verdict = CreateThenOpen(setup, setup);

        Assert.That(verdict.Outcome, Is.EqualTo(Outcome.Correct),
            $"{setup.Label} could not read its own database back ({verdict.Detail}) - the harness is " +
            "broken, not the engine, and no verdict in the grid may be believed.");
    }

    /// <summary>
    /// Control: an encrypted database is not readable without its password. If this ever passes as
    /// <c>Correct</c>, encryption is not encrypting and the rest of this fixture is beside the point.
    /// </summary>
    [Test]
    public void ControlAnEncryptedDatabaseIsNotReadableWithoutThePasswordTest()
    {
        var verdict = CreateThenOpen(SETUPS.Single(s => s.Label == "aes"), SETUPS.Single(s => s.Label == "default"));

        Assert.That(verdict.Outcome, Is.Not.EqualTo(Outcome.Correct),
            $"an encrypted database opened with no password returned its rows: {verdict.Detail}");
    }

    #endregion

    #region The grid

    /// <summary>
    /// Probe: every configuration's database opened by every configuration. Reported in full; the
    /// assertion is only that nothing lands in the silent-divergence category.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Pairs))]
    public void OpeningWithADifferentConfigurationTest(Setup creator, Setup opener)
    {
        var verdict = CreateThenOpen(creator, opener);

        TestContext.Out.WriteLine(
            $"MISMATCH {creator.Label,-10} -> {opener.Label,-10} {verdict.Outcome,-18} " +
            $"original {(verdict.SurvivesTheOriginal ? "survives" : "LOST")}  {verdict.Detail}" +
            (verdict.SurvivesTheOriginal ? "" : $"  || creator now sees: {verdict.SurvivalReason}"));

        // Refusing AT OPEN is a legitimate answer - a construction kit may have illegal shapes as long as
        // it says so. Opening without complaint and then not having the data is not: that reads to a
        // consumer as an empty database, and the natural next step writes over one that is intact.
        Assert.That(verdict.Outcome, Is.AnyOf(Outcome.Correct, Outcome.RefusedAtOpen),
            $"<{creator.Label}> -> <{opener.Label}>: {verdict.Outcome}. {verdict.Detail}. The data " +
            (verdict.SurvivesTheOriginal
                ? "IS still readable by the configuration that wrote it."
                : "is NOT readable even by the configuration that wrote it any more."));

        // Survival was printed by every cell and asserted by none, and a cell duly went red in prose
        // rather than in the run: opening an LSM database with a wrong password refused, and left the
        // write-ahead log open, so the creator's own configuration then met "the process cannot access
        // the file". A wrong connection string may be refused; it may not cost the owner its database.
        Assert.That(verdict.SurvivesTheOriginal, Is.True,
            $"<{creator.Label}> -> <{opener.Label}>: after this open the database is no longer readable " +
            $"by the configuration that wrote it - {verdict.SurvivalReason}");
    }

    /// <summary>
    /// Probe: a database written under one transaction model is REFUSED by the other, rather than
    /// opening and reporting its tables missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This pinned twelve grid cases and now asserts the fix - the last of the three defects this
    /// fixture found. A database written with MVCC (the default) opened perfectly under
    /// <c>MVCC=false</c> or <c>Transactions=false</c> and answered <c>Table 'Mismatch' not found</c>,
    /// in both directions. The rows were never lost: the creator read them back afterwards, which is
    /// what the <c>SurvivesTheOriginal</c> half of every verdict is for. The danger was the next step -
    /// a consumer meeting an apparently empty database creates the schema, over one that was intact.
    /// </para>
    /// <para>
    /// The fix compares the <c>Mvcc</c> and <c>Transactions</c> flags the header has always recorded
    /// against the configuration now asking to open, and refuses when the MVCC layer is present on one
    /// side only. <c>MVCC=false</c> and <c>Transactions=false</c> write the same layout as each other,
    /// so that pair still opens - the grid measures it, and refusing it would refuse something that
    /// works.
    /// </para>
    /// <para>
    /// The third assertion is the one the phase has earned three times over: a refusal must not leave
    /// the data file open. Two handle leaks and a refused encrypted open all had exactly this shape.
    /// </para>
    /// </remarks>
    [TestCase("default", "MVCC=false", TestName = "{m}(mvcc database opened with mvcc off)")]
    [TestCase("default", "Transactions=false", TestName = "{m}(mvcc database opened with transactions off)")]
    [TestCase("locks", "MVCC=true", TestName = "{m}(lock-based database opened with mvcc on)")]
    [TestCase("no-tx", "MVCC=true", TestName = "{m}(transactionless database opened with mvcc on)")]
    public void ANamedTransactionModelThatConflictsIsRefusedAtOpenTest(string creatorLabel, string openerSettings)
    {
        var creator = SETUPS.Single(s => s.Label == creatorLabel);
        var opener = new Setup($"named:{openerSettings}", openerSettings);

        var verdict = CreateThenOpen(creator, opener);

        Assert.That(verdict.Outcome, Is.EqualTo(Outcome.RefusedAtOpen),
            $"<{creatorLabel}> -> <{openerSettings}>: {verdict.Outcome}. {verdict.Detail}");

        Assert.That(verdict.Detail, Does.Contain("MVCC"),
            "the refusal does not name the setting that caused it, which is what a consumer has to act on: " +
            verdict.Detail);

        Assert.That(verdict.SurvivesTheOriginal, Is.True,
            "after the refusal the database is no longer readable by the configuration that wrote it: " +
            verdict.SurvivalReason);
    }

    /// <summary>
    /// Probe: a connection string that does not name the transaction model gets the one the database was
    /// created with, rather than a refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This replaces two cases that used to expect a refusal, and the change is the point of phase
    /// 12.</b> 12.0.0 refused every transaction-model disagreement because refusing was the best answer
    /// available: nothing could tell a caller who had chosen <c>MVCC=true</c> from one who had merely
    /// not mentioned it, so both were treated as a choice. A connection string knows which of its
    /// keywords were written, and a database records the model it was made with, so the second case can
    /// now be answered by opening correctly.
    /// </para>
    /// <para>
    /// The refusal is not weakened: naming a conflicting model is still refused, which is what
    /// <see cref="ANamedTransactionModelThatConflictsIsRefusedAtOpenTest"/> asserts on the same pairs.
    /// The two tests are the two directions of one rule, and either alone would let the other's
    /// behaviour regress unnoticed.
    /// </para>
    /// </remarks>
    [TestCase("locks", TestName = "{m}(lock-based database)")]
    [TestCase("no-tx", TestName = "{m}(transactionless database)")]
    public void AnUnnamedTransactionModelIsRestoredFromTheDatabaseTest(string creatorLabel)
    {
        var creator = SETUPS.Single(s => s.Label == creatorLabel);
        var opener = new Setup("says nothing", "");

        var verdict = CreateThenOpen(creator, opener);

        Assert.That(verdict.Outcome, Is.EqualTo(Outcome.Correct),
            $"<{creatorLabel}> opened by a connection string that names no transaction model: " +
            $"{verdict.Outcome}. {verdict.Detail}");
    }

    // The grid pinned two more defects and no longer reproduces either; the record, because the shapes
    // recur. A larger PageSize REINITIALISED the header - opening a 4,096-byte database with
    // PageSize=16384 showed nothing and afterwards the ORIGINAL configuration got "Page size mismatch:
    // file has 16384 bytes", which is destruction rather than invisibility and the most serious thing
    // this fixture found; StorageFile now refuses a file too short to hold one page of the size being
    // asked for. And the transaction model changed the layout with nothing to say so - see
    // ADifferentTransactionModelIsRefusedAtOpenTest above, which asserts the refusal that replaced it.

    /// <summary>
    /// Probe: an open that is REFUSED leaves the data file open, so the database cannot be opened again
    /// in this process - correctly or otherwise.
    /// </summary>
    /// <remarks>
    /// This pinned a defect and now asserts the fix: a mistyped password used to lock the database out
    /// for the life of the process, under a message that named the wrong problem entirely ("the process
    /// cannot access the file"). The storage is opened lazily and disposed when the store that was going
    /// to own it throws, so a refused open now leaves nothing behind - which is the same shape as the
    /// two handle leaks fixed earlier in this phase.
    /// </remarks>
    [Test]
    public void ARefusedOpenLeavesNothingOpenTest()
    {
        var plain = SETUPS.Single(s => s.Label == "default");
        var encrypted = SETUPS.Single(s => s.Label == "aes");

        var verdict = CreateThenOpen(plain, encrypted);

        TestContext.Out.WriteLine($"MISMATCH refused open -> creator sees: {verdict.SurvivalReason}");

        Assert.That(verdict.Outcome, Is.EqualTo(Outcome.RefusedAtOpen),
            "the wrong password was not refused at open - this probe is measuring something else now");

        Assert.That(verdict.SurvivesTheOriginal, Is.True,
            "after a refused open the database is not readable by the configuration that wrote it: " +
            $"{verdict.SurvivalReason}");
    }

    #endregion

    #region Tools

    private Verdict CreateThenOpen(Setup creator, Setup opener)
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);

        var dataSource = Path.Combine(directory, "mismatch.witdb");

        try
        {
            using var creating = new WitDbConnection(Compose(dataSource, creator.Settings));
            creating.Open();
            Write(creating);
        }
        catch (Exception e)
        {
            return new Verdict(Outcome.RefusedAtOpen, $"the CREATOR failed: {Short(e)}", false, "");
        }

        Outcome outcome;
        string detail;

        using (var opening = new WitDbConnection(Compose(dataSource, opener.Settings)))
        {
            try
            {
                opening.Open();
            }
            catch (Exception e)
            {
                var refusedSurvives = StillReadable(dataSource, creator, out var refusedReason);
                return new Verdict(Outcome.RefusedAtOpen, Short(e), refusedSurvives, refusedReason);
            }

            try
            {
                var scan = Scan(opening);

                outcome = scan == EXPECTED ? Outcome.Correct : Outcome.Wrong;
                detail = scan == EXPECTED ? "" : $"read back [{scan}]";
            }
            catch (Exception e)
            {
                // The open succeeded. Whatever this is, the consumer now holds an open connection to a
                // database that does not answer for its own contents.
                outcome = Outcome.OpensAndDataIsGone;
                detail = Short(e);
            }
        }

        var survives = StillReadable(dataSource, creator, out var reason);
        return new Verdict(outcome, detail, survives, reason);
    }

    /// <summary>
    /// Can the configuration that wrote the database still read it, after the mismatched open?
    /// </summary>
    private static bool StillReadable(string dataSource, Setup creator)
    {
        return StillReadable(dataSource, creator, out _);
    }

    /// <summary>
    /// Can the creator still read it, and if not, <b>why</b> - which is the difference between a
    /// damaged file and a handle somebody failed to release.
    /// </summary>
    private static bool StillReadable(string dataSource, Setup creator, out string reason)
    {
        try
        {
            using var connection = new WitDbConnection(Compose(dataSource, creator.Settings));
            connection.Open();

            var scan = Scan(connection);
            reason = scan == EXPECTED ? "" : $"read back [{scan}]";
            return scan == EXPECTED;
        }
        catch (Exception e)
        {
            reason = $"{e.GetType().Name}: {Short(e)}";
            return false;
        }
    }

    private static void Write(WitDbConnection connection)
    {
        Execute(connection, "CREATE TABLE Mismatch (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");

        for (var i = 1; i <= 8; i++)
            Execute(connection, $"INSERT INTO Mismatch (Id, Name) VALUES ({i}, 'row{i}')");
    }

    private static string Scan(WitDbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM Mismatch ORDER BY Id";

        using var reader = command.ExecuteReader();
        var builder = new StringBuilder();

        while (reader.Read())
        {
            if (builder.Length > 0)
                builder.Append('|');

            builder.Append($"{reader.GetInt64(0)}:{reader.GetString(1)}");
        }

        return builder.ToString();
    }

    private static void Execute(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Compose(string dataSource, string settings)
    {
        return string.IsNullOrEmpty(settings)
            ? $"Data Source={dataSource}"
            : $"Data Source={dataSource};{settings}";
    }

    /// <summary>
    /// One line per verdict, and the whole message on it. Taking only the FIRST line used to be enough;
    /// a refusal that lists what it disagrees with puts its reason on the second one, which made the
    /// report read "Database configuration mismatch:" and stop.
    /// </summary>
    private static string Short(Exception exception)
    {
        var line = string.Join(" / ",
            exception.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return line.Length > 240 ? line[..240] : line;
    }

    #endregion
}
