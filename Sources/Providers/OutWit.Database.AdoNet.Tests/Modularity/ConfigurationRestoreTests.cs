using System.Text;
using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 12 instrument F - what does a database remember about how it was made?
/// </summary>
/// <remarks>
/// <para>
/// Phase 11 asked whether a setting <b>reaches</b> the engine (the census) and whether a configuration
/// that disagrees with the file is <b>refused</b> (instrument C). Neither asks the question a consumer
/// actually meets: a database is created once, with a carefully written connection string, and then
/// opened a thousand times afterwards - by another process, another deployment, another version of the
/// application. <b>How much of that connection string does the file remember?</b>
/// </para>
/// <para>
/// The header has a place to remember it. <c>ProviderMetadata</c> occupies bytes 48-99 and records the
/// store key, the encryption key and four feature flags; <c>CacheProviderKey</c> and
/// <c>JournalProviderKey</c> are declared on the struct, carry the comment <i>"Not persisted - always
/// uses default on reopen"</i>, and 12 bytes are reserved for them. So the question is not whether the
/// idea exists - it is which settings actually make the round trip.
/// </para>
/// <para>
/// <b>The method.</b> Create with <c>Data Source=X;Setting=V</c>, run a workload, close. Then open the
/// same file twice: once with the same connection string (the <b>reference</b> - what the engine looks
/// like when the setting is spelled out) and once with <c>Data Source=X</c> and nothing else (the
/// <b>bare</b> reopen - what a consumer gets who trusted the file to remember). Compare the structural
/// fingerprints. Identical means the file remembered; different means the default was silently used.
/// </para>
/// <para>
/// <b>What this instrument cannot say on its own, and the trap it has to avoid.</b> A setting that
/// reaches nothing produces the same engine either way, so it would report RESTORED for free. Every
/// subject here is a keyword the census (instrument A) proved REACHES the engine, and the two fixtures
/// have to be read together. That is why the controls below are the shape they are: one setting known
/// <b>not</b> to be persisted, which must come back LOST, and one known to be persisted, which must not.
/// </para>
/// <para>
/// <b>And the answers are checked, not only the shape.</b> Every reopen scans the rows back - never a
/// count, which this engine keeps as separate state. A bare reopen that quietly loses the data is a
/// worse finding than one that quietly loses a setting, and it is a different verdict.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class ConfigurationRestoreTests
{
    #region Types

    private enum Role
    {
        /// <summary>An unproved setting - what this instrument exists to classify.</summary>
        Subject,

        /// <summary>
        /// Deliberately not restored - a safety or session setting, which a file may not decide for a
        /// caller who said nothing about it. A RESTORED verdict here means either that the rule was
        /// broken or that the fingerprint cannot see this part of the engine, and both make every other
        /// RESTORED verdict in the run worthless.
        /// </summary>
        MustBeLost,

        /// <summary>
        /// Recorded in the header. LOST here means the file's own record was ignored - and since 12.1.0
        /// refuses a transaction-model mismatch, REFUSED is an acceptable answer too.
        /// </summary>
        MustNotBeLost,

        /// <summary>
        /// Nothing was set, so the bare reopen and the reference are the same connection string. LOST
        /// here means the comparison is reporting run-to-run noise as a difference.
        /// </summary>
        MustBeRestored,

        /// <summary>
        /// Created with a value that IS the default, deliberately. The vacuity check must say so - a
        /// check nobody has watched fire is a claim rather than a control.
        /// </summary>
        MustBeVacuous
    }

    private enum Verdict
    {
        /// <summary>The bare reopen built the same engine as the explicit one, and answered the same.</summary>
        Restored,

        /// <summary>It opened, it answered correctly, and it is a different engine - the default was used.</summary>
        Lost,

        /// <summary>The bare reopen threw. The setting is not restored, but nothing silently disagrees.</summary>
        Refused,

        /// <summary>It opened and the rows are not there.</summary>
        DataIsGone,

        /// <summary>It opened, it answered, and it answered something else.</summary>
        Wrong
    }

    /// <param name="Created">The settings the database is created with, as connection-string text.</param>
    /// <param name="Minimum">
    /// What the bare reopen is still allowed to say. Empty for almost everything - the point is
    /// <c>Data Source=</c> and nothing else. <c>Store=lsm</c> for the LSM sub-settings, where the
    /// question is "given the store, is this remembered" rather than "is the store remembered".
    /// </param>
    private sealed record Case(string Name, string Created, Role Role = Role.Subject, string Minimum = "");

    /// <param name="Vacuous">
    /// The setting was created with the value that is already the default, so the bare reopen agrees
    /// with the reference whether or not anything was restored. A RESTORED verdict on a vacuous case is
    /// not evidence, and this fixture found one on its first run: <c>SyncWrites</c> defaults to
    /// <c>false</c>, and the case created it with <c>false</c>.
    /// </param>
    private sealed record Result(
        Case Case,
        Verdict Verdict,
        IReadOnlyList<string> Differences,
        int NoiseCount,
        string Note,
        bool Vacuous = false);

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
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_restore_{Guid.NewGuid():N}");
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

    #region The cases

    /// <summary>
    /// Every setting the census proved reaches the engine, plus the three controls. Ordered as
    /// <c>WitSQL.md</c> § 14.10 lists them, so the two can be read side by side.
    /// </summary>
    private static readonly Case[] CASES =
    [
        // ---- controls -------------------------------------------------------------------------
        new("nothing set (control)", "", Role.MustBeRestored),
        // The must-be-lost control used to be Cache=lru, which was documented as never persisted. It is
        // persisted now, so the control moved to a setting that is deliberately NOT restored: a file may
        // not decide durability for a caller who said nothing about it.
        new("Synchronous Commit=false (control)", "Synchronous Commit=false", Role.MustBeLost),
        new("MVCC=false (control)", "MVCC=false", Role.MustNotBeLost),
        new("SyncWrites=false (control)", "Store=lsm;SyncWrites=false", Role.MustBeVacuous, "Store=lsm"),

        // ---- the page cache ---------------------------------------------------------------------
        new("Cache=lru", "Cache=lru"),

        // ---- the store ------------------------------------------------------------------------
        new("Store=lsm", "Store=lsm"),

        // ---- layout, recorded in the header ---------------------------------------------------
        new("PageSize=16384", "PageSize=16384"),
        new("Encryption=aes-gcm", "Encryption=aes-gcm;Password=restore-secret"),

        // ---- behaviour ------------------------------------------------------------------------
        new("Transactions=false", "Transactions=false"),
        new("CacheSize=64", "CacheSize=64"),
        new("Journal=wal", "MVCC=false;Journal=wal", Minimum: "MVCC=false"),
        new("Journal=rollback", "MVCC=false;Journal=rollback", Minimum: "MVCC=false"),
        // Not restored, by decision - see WitDatabaseBuilderOptions.RestoreStoredConfiguration. They are
        // kept as cases rather than removed, so the rule is measured every run instead of asserted once.
        new("FileLocking=false", "FileLocking=false", Role.MustBeLost),
        new("Isolation Level=Serializable", "Isolation Level=Serializable", Role.MustBeLost),

        // ---- LSM, given the store ---------------------------------------------------------------
        new("MemTableSize=1024", "Store=lsm;MemTableSize=1024", Minimum: "Store=lsm"),
        new("BlockSize=16384", "Store=lsm;BlockSize=16384", Minimum: "Store=lsm"),
        new("CompactionTrigger=9", "Store=lsm;CompactionTrigger=9", Minimum: "Store=lsm"),
        new("EnableWal=false", "Store=lsm;EnableWal=false", Minimum: "Store=lsm"),
        // SyncWrites=TRUE. The first run of this fixture wrote `false`, which is the default, and the
        // case reported RESTORED without being able to report anything else.
        new("SyncWrites=true", "Store=lsm;SyncWrites=true", Minimum: "Store=lsm"),
        new("EnableBlockCache=false", "Store=lsm;EnableBlockCache=false", Minimum: "Store=lsm"),
        new("BlockCacheSize=1048576", "Store=lsm;BlockCacheSize=1048576", Minimum: "Store=lsm")
    ];

    #endregion

    #region Probes

    /// <summary>
    /// Probe: for every setting, does opening the database without it rebuild what it built?
    /// Reported in full - the work order for this phase comes out of it.
    /// </summary>
    [Test]
    public void ProbeWhatAReopenRestoresTest()
    {
        var results = CASES.Select(Measure).ToList();

        foreach (var result in results)
        {
            var verdict = result.Vacuous ? "VACUOUS" : result.Verdict.ToString().ToUpperInvariant();

            TestContext.Out.WriteLine(
                $"{verdict,-10} {result.Case.Name,-32} " +
                $"[created: {result.Case.Created}] noise={result.NoiseCount}  {result.Note}");

            foreach (var difference in result.Differences.Take(4))
                TestContext.Out.WriteLine($"           {difference}");
        }

        TestContext.Out.WriteLine("");

        foreach (var group in results.GroupBy(r => r.Verdict).OrderBy(g => g.Key))
            TestContext.Out.WriteLine($"TOTAL   {group.Key}: {group.Count()}");

        AssertControlsHeld(results);
    }

    /// <summary>
    /// Probe: the other route. <see cref="WitDatabase.Open(string)"/> reads the header through
    /// <c>StorageDetector</c> and configures a builder from it, which is exactly what the
    /// connection-string route does not do. What it restores is worth measuring next to what the
    /// connection string restores, because a consumer who writes <c>Data Source=</c> is asking for the
    /// same thing and getting the other answer.
    /// </summary>
    [Test]
    public void ProbeWhatTheStaticOpenRestoresTest()
    {
        foreach (var subject in CASES)
        {
            var path = Create(subject);
            string outcome;

            try
            {
                using var database = WitDatabase.Open(path);
                using var engine = new WitSqlEngine(database, ownsStore: false);

                var rows = ReadBack(engine);
                outcome = rows == EXPECTED ? "opens, rows intact" : $"opens, rows = {rows}";
            }
            catch (Exception e)
            {
                outcome = $"refused: {Short(e)}";
            }

            TestContext.Out.WriteLine($"{subject.Name,-32} {outcome}");
        }
    }

    /// <summary>
    /// <see cref="WitDatabase.Open(string)"/> reads an LSM database that was created with the default
    /// configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test was written red, as a pin, and inverted by the fix.</b> Until 12.2.0 it opened
    /// without complaint and reported every table as missing, with the rows intact underneath - the
    /// exact shape 12.0.0 fixed for the B+Tree store, surviving here because that fix was a comparison
    /// against a header the LSM store did not have. The detector fills in no features for a directory,
    /// so <c>HasTransactions</c> came back as the default of a field nobody had set and <c>Open</c>
    /// built a store with no transaction layer over a database whose every value sits under a versioned
    /// MVCC key.
    /// </para>
    /// <para>
    /// The second assertion is what the first one used to need: the rows read through the configuration
    /// the database was created with. It stays because it is what separates "this route is fixed" from
    /// "this route now agrees with a broken one", and because a failure of the first alone would
    /// otherwise be indistinguishable from data loss.
    /// </para>
    /// </remarks>
    [Test]
    public void StaticOpenReadsAnLsmDatabaseTest()
    {
        var path = Create(new Case("lsm", "Store=lsm"));

        string throughOpen;
        string throughMvcc;

        using (var database = WitDatabase.Open(path))
        using (var engine = new WitSqlEngine(database, ownsStore: false))
        {
            throughOpen = ReadBack(engine);
        }

        using (var database = new WitDatabaseBuilder().WithLsmTree(path).WithMvcc().Build())
        using (var engine = new WitSqlEngine(database, ownsStore: false))
        {
            throughMvcc = ReadBack(engine);
        }

        Assert.Multiple(() =>
        {
            Assert.That(throughOpen, Is.EqualTo(EXPECTED),
                "WitDatabase.Open must build the transaction model the LSM directory records. Before " +
                "12.2.0 this answered \"Table 'Restore' not found\" with the rows intact underneath.");

            Assert.That(throughMvcc, Is.EqualTo(EXPECTED),
                "The attribution half: the rows must be there when read through the configuration the " +
                "database was created with. If this fails the finding is data loss, not invisibility.");
        });
    }

    /// <summary>
    /// The instrument's own controls, asserted rather than printed - a blind or a noisy comparison
    /// invalidates every verdict in the run.
    /// </summary>
    private static void AssertControlsHeld(IReadOnlyList<Result> results)
    {
        var blind = results
            .Where(r => r.Case.Role == Role.MustBeLost && r.Verdict != Verdict.Lost)
            .Select(r => $"{r.Case.Name} reported {r.Verdict}")
            .ToList();

        var ignored = results
            .Where(r => r.Case.Role == Role.MustNotBeLost && r.Verdict is not (Verdict.Restored or Verdict.Refused))
            .Select(r => $"{r.Case.Name} reported {r.Verdict}")
            .ToList();

        var noisy = results
            .Where(r => r.Case.Role == Role.MustBeRestored && r.Verdict != Verdict.Restored)
            .Select(r => $"{r.Case.Name} reported {r.Verdict} ({string.Join(", ", r.Differences.Take(5))})")
            .ToList();

        // The control that this fixture's first run needed and did not have.
        var vacuous = results
            .Where(r => r.Case.Role == Role.Subject && r.Vacuous)
            .Select(r => r.Case.Name)
            .ToList();

        var blindToVacuity = results
            .Where(r => r.Case.Role == Role.MustBeVacuous && !r.Vacuous)
            .Select(r => $"{r.Case.Name} reported {r.Verdict}, not vacuous")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(blindToVacuity, Is.Empty,
                "A case deliberately created with the default value was not reported as vacuous - the " +
                "vacuity check cannot fire, so no RESTORED verdict in this run may be believed.");

            Assert.That(vacuous, Is.Empty,
                "A subject was created with the value that is already the default, so it agrees with a " +
                "bare reopen whether or not anything is restored. The case proves nothing and has to be " +
                "rewritten with a value the default is not.");

            Assert.That(blind, Is.Empty,
                "A setting documented as not persisted came back as restored - the fingerprint cannot " +
                "see it, so no RESTORED verdict in this run may be believed.");

            Assert.That(ignored, Is.Empty,
                "A setting the header records was neither restored nor refused - the file's own record " +
                "was ignored.");

            Assert.That(noisy, Is.Empty,
                "Two opens of the same database with the same connection string differ - the comparison " +
                "is reporting noise, so no LOST verdict in this run may be believed.");
        });
    }

    #endregion

    #region Measurement

    private Result Measure(Case subject)
    {
        var path = Create(subject);

        SortedDictionary<string, string> referenceOne;
        SortedDictionary<string, string> referenceTwo;
        string referenceRows;

        try
        {
            (referenceOne, referenceRows) = Fingerprint(Compose(path, subject.Created));
            (referenceTwo, _) = Fingerprint(Compose(path, subject.Created));
        }
        catch (Exception e)
        {
            // The database was created by this connection string a moment ago, so it refusing to reopen
            // itself is a defect in the subject rather than a verdict about restoration.
            return new Result(subject, Verdict.Wrong, [], 0,
                $"the creating connection string cannot reopen its own database: {Short(e)}");
        }

        SortedDictionary<string, string> bare;
        string bareRows;

        try
        {
            (bare, bareRows) = Fingerprint(Compose(path, subject.Minimum));
        }
        catch (Exception e)
        {
            return new Result(subject, Verdict.Refused, [], 0, Short(e));
        }

        var noise = EngineFingerprint.Noise(referenceOne, referenceTwo);

        var differences = referenceOne.Keys.Union(bare.Keys)
            .Where(key => !noise.Contains(key))
            .Select(key =>
            {
                referenceOne.TryGetValue(key, out var expected);
                bare.TryGetValue(key, out var actual);
                return (Path: key, Expected: expected ?? "<absent>", Actual: actual ?? "<absent>");
            })
            .Where(d => d.Expected != d.Actual)
            .Select(d => $"{d.Path}: reference {d.Expected} -> bare {d.Actual}")
            .ToList();

        if (bareRows != referenceRows)
        {
            var verdict = string.IsNullOrEmpty(bareRows) || bareRows.StartsWith("!", StringComparison.Ordinal)
                ? Verdict.DataIsGone
                : Verdict.Wrong;

            return new Result(subject, verdict, differences, noise.Count,
                $"reference read {referenceRows}, bare read {bareRows}");
        }

        if (differences.Count > 0)
        {
            return new Result(subject, Verdict.Lost, differences, noise.Count,
                $"{differences.Count} structural difference(s); the rows are intact");
        }

        // The engines agree - and before that can be called restoration, the case has to be capable of
        // reporting anything else. A setting created with the value that is already the default agrees
        // by construction.
        var vacuous = IsVacuous(subject, referenceOne, noise, out var vacuityNote);

        return new Result(subject, Verdict.Restored, [], noise.Count,
            vacuous ? vacuityNote : "the reopened engine is identical", vacuous);
    }

    /// <summary>
    /// Whether the case could have reported anything other than RESTORED: a setting whose created value
    /// is already the default builds the same engine either way.
    /// </summary>
    /// <remarks>
    /// Measured rather than reasoned about, because a default is a fact about the product and this
    /// fixture would otherwise be pinning the one it assumed. Two more databases are created with the
    /// case's <c>Minimum</c> settings alone - the same configuration, in two directories - and their
    /// difference is the cross-database noise that a single path-dependent value would otherwise look
    /// like. If the reference agrees with a default-configured database everywhere outside that noise,
    /// the setting was never distinguishable in the first place.
    /// </remarks>
    private bool IsVacuous(Case subject, SortedDictionary<string, string> reference,
        HashSet<string> noise, out string note)
    {
        SortedDictionary<string, string> defaultOne;
        SortedDictionary<string, string> defaultTwo;

        try
        {
            (defaultOne, _) = Fingerprint(Compose(Create(subject with { Created = subject.Minimum }), subject.Minimum));
            (defaultTwo, _) = Fingerprint(Compose(Create(subject with { Created = subject.Minimum }), subject.Minimum));
        }
        catch (Exception e)
        {
            note = $"the reopened engine is identical (vacuity unmeasured: {Short(e)})";
            return false;
        }

        var crossNoise = EngineFingerprint.Noise(defaultOne, defaultTwo);

        var distinguishing = reference.Keys.Union(defaultOne.Keys)
            .Where(key => !noise.Contains(key) && !crossNoise.Contains(key))
            .Where(key =>
            {
                reference.TryGetValue(key, out var a);
                defaultOne.TryGetValue(key, out var b);
                return a != b;
            })
            .ToList();

        if (distinguishing.Count > 0)
        {
            note = "the reopened engine is identical";
            return false;
        }

        note = "VACUOUS - the created value is the default, so this case cannot tell restoration " +
               "from coincidence. Rewrite it with a non-default value.";
        return true;
    }

    /// <summary>
    /// Creates the database with its full connection string, writes the workload and closes it. What
    /// happens after this point is the whole question, so nothing here may be left open.
    /// </summary>
    private string Create(Case subject)
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "restore.witdb");

        using var connection = new WitDbConnection(Compose(path, subject.Created));
        connection.Open();

        Execute(connection, "CREATE TABLE Restore (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");
        Execute(connection, "CREATE INDEX IX_Restore_Name ON Restore (Name)");

        for (var i = 1; i <= 8; i++)
            Execute(connection, $"INSERT INTO Restore (Id, Name) VALUES ({i}, 'row{i}')");

        return path;
    }

    /// <summary>
    /// Opens the database with the given connection string, reads the rows back and fingerprints the
    /// engine. Anything thrown out of <c>Open</c> propagates - a refusal is a verdict of its own.
    /// </summary>
    private static (SortedDictionary<string, string> Values, string Rows) Fingerprint(string connectionString)
    {
        using var connection = new WitDbConnection(connectionString);
        connection.Open();

        var rows = ReadBack(connection);

        var values = EngineFingerprint.Take(
            ("database", EngineFingerprint.Field(connection, "m_database")),
            ("engine", EngineFingerprint.Field(connection, "m_engine")));

        return (values, rows);
    }

    private static string Compose(string dataSource, string settings)
    {
        var builder = new StringBuilder($"Data Source={dataSource}");

        if (!string.IsNullOrEmpty(settings))
            builder.Append(';').Append(settings);

        return builder.ToString();
    }

    private static void Execute(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Scans the rows back. Never <c>COUNT(*)</c>: this engine answers that from a cached per-table
    /// counter, which is separate state with separate persistence, and phase 4 had it manufacture a
    /// false report of lost commits.
    /// </summary>
    private static string ReadBack(WitDbConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name FROM Restore ORDER BY Id";

            using var reader = command.ExecuteReader();
            var rows = new List<string>();

            while (reader.Read())
                rows.Add($"{reader.GetInt64(0)}:{reader.GetString(1)}");

            return string.Join("|", rows);
        }
        catch (Exception e)
        {
            return $"!{Short(e)}";
        }
    }

    private static string ReadBack(WitSqlEngine engine)
    {
        try
        {
            var rows = engine.Query("SELECT Id, Name FROM Restore ORDER BY Id");

            return string.Join("|", rows.Select(r => $"{r["Id"].AsInt64()}:{r["Name"].AsString()}"));
        }
        catch (Exception e)
        {
            return $"!{Short(e)}";
        }
    }

    private static string Short(Exception exception)
    {
        var line = exception.Message.Split('\n')[0].Trim();
        return line.Length > 140 ? line[..140] : line;
    }

    #endregion
}
