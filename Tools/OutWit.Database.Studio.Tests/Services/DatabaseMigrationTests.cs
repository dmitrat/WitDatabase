using OutWit.Database.AdoNet;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Changing the password is a migration (WS-58), and the migration has to be verified.
/// </summary>
[TestFixture]
public class DatabaseMigrationTests
{
    #region Fields

    private StudioFixture m_fixture = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_fixture = await StudioFixture.CreateAsync();
    }

    // Every case here runs on the fixture's WHOLE schema, trigger and view included. There used to be
    // a BuildSchemaWithoutTriggersAsync that dropped both, and its reason was honest while it lasted:
    // a trigger could not be carried by the dump at all, so a case built on the fixture's own schema
    // would only ever have been measuring that. Taking it out is the census the fix earns - three
    // cases that used to migrate a cut-down database now migrate the real one.

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region Tests

    /// <summary>
    /// A database with a TRIGGER in it migrates, and the trigger is in the copy - where it fires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces <c>ADatabaseWithATriggerCannotBeMigratedYetAsync</c>, which pinned the defect
    /// rather than the behaviour and said in its own summary what should replace it. The defect: the
    /// catalogue publishes a trigger's BODY and the dump wrote that verbatim, so the script ended with
    /// <c>INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);</c> as a statement of its own and the
    /// engine refused it with "Column 'Id' not found". Measured on 2026-08-08, the pin went from
    /// <c>Failed</c> to <c>Transferred</c> the moment the definition became a whole
    /// <c>CREATE TRIGGER</c> - which is the proof the fix works.
    /// </para>
    /// <para>
    /// The trigger is checked by USING it rather than by finding its name in the catalogue: one that
    /// is listed and does not fire is exactly the failure this is about.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ADatabaseWithATriggerIsMigratedAndTheTriggerFiresAsync()
    {
        var target = new ConnectionInfo { FilePath = Path.Combine(m_fixture.Root, "with-trigger.witdb") };

        var result = await DatabaseMigrator.MigrateAsync(m_fixture.Database, target);

        Assert.That(result.Outcome, Is.EqualTo(MigrationOutcome.Transferred), result.EngineMessage);

        await using var connection = new WitDbConnection(target.BuildConnectionString());
        await connection.OpenAsync();

        var before = await CountAsync(connection, "OrdersAudit");

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Orders (CustomerId, Total, Status) VALUES (1, 5.00, 'new')";
            await command.ExecuteNonQueryAsync();
        }

        Assert.That(await CountAsync(connection, "OrdersAudit"), Is.EqualTo(before + 1),
            "the trigger has to be in the copy and do its work there");
    }

    /// <summary>
    /// A migrated database can take a new row: its key counters followed the rows that arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was a pin (<c>AGeneratedKeyIsRefusedInAMigratedDatabaseAsync</c>) for as long as it took to
    /// find the cause. The symptom: every dump and every password change produced a database that
    /// refused the first row it was asked to generate a key for, while the migration report said the
    /// transfer was complete. Found by the trigger case above, which fired the trigger in the copy and
    /// got <i>"the table's key counter is behind its rows"</i>.
    /// </para>
    /// <para>
    /// <b>It was two layers down and had nothing to do with dumps</b> - the MVCC key encoding is not
    /// prefix-free, so writing <c>Orders</c>' row-id counter marked <c>OrdersAudit</c>'s deleted. See
    /// <c>KnownIssues.md</c> issue 11, <c>MvccPrefixKeyTests</c> and
    /// <c>RowIdCounterPrefixTests</c>. This case is kept because it is the one that noticed, and it is
    /// the shape a user meets it in.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AMigratedDatabaseCanTakeANewRowAsync()
    {
        var target = new ConnectionInfo { FilePath = Path.Combine(m_fixture.Root, "counter.witdb") };

        var result = await DatabaseMigrator.MigrateAsync(m_fixture.Database, target);

        Assert.That(result.Outcome, Is.EqualTo(MigrationOutcome.Transferred), result.EngineMessage);

        await using var connection = new WitDbConnection(target.BuildConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO OrdersAudit (OrderId) VALUES (99)";

        Assert.That(async () => await command.ExecuteNonQueryAsync(), Throws.Nothing,
            "a transfer that reports itself complete has to leave a database that can be written to");
    }

    private static async Task<int> CountAsync(WitDbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id FROM [{table}]";

        await using var reader = await command.ExecuteReaderAsync();

        var rows = 0;

        while (await reader.ReadAsync())
            rows++;

        return rows;
    }

    /// <summary>
    /// An unencrypted database is migrated into an encrypted one, the rows arrive, and the ORIGINAL is
    /// left exactly as it was.
    ///
    /// <para>
    /// The last part is the half that matters: the whole reason a password change is a migration rather
    /// than an edit is that the source stays openable, so a migration that quietly damaged it would
    /// have taken away the only safety this operation has.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheDataArrivesEncryptedAndTheSourceIsUntouchedAsync()
    {
        var target = new ConnectionInfo
        {
            FilePath = Path.Combine(m_fixture.Root, "encrypted.witdb"),
            IsEncrypted = true,
            Password = "correct horse",
            EncryptionProvider = "aes-gcm"
        };

        var result = await DatabaseMigrator.MigrateAsync(m_fixture.Database, target);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(MigrationOutcome.Transferred), result.EngineMessage);
            Assert.That(result.Verification, Is.Not.Empty);
            Assert.That(result.Mismatches, Is.Empty);

            Assert.That(result.Verification.Single(check => check.Table == "Customers").InTarget,
                Is.EqualTo(StudioFixture.CUSTOMER_COUNT));
        });

        // The new database needs the password - which is the thing that was actually changed, and the
        // only assertion here that could tell an encrypted copy from a plain one.
        await using (var connection = new WitDbConnection($"Data Source={target.FilePath}"))
        {
            Assert.That(async () => await connection.OpenAsync(), Throws.Exception,
                "the copy is encrypted, so opening it without the password must fail");
        }

        await using (var connection = new WitDbConnection(target.BuildConnectionString()))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM Customers";

            var rows = 0;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                rows++;

            Assert.That(rows, Is.EqualTo(StudioFixture.CUSTOMER_COUNT));
        }

        // And the source, which nothing was supposed to touch.
        Assert.That(await m_fixture.CountRowsAsync("Customers"),
            Is.EqualTo(StudioFixture.CUSTOMER_COUNT));
    }

    /// <summary>
    /// The verification counts every table on both sides, and it counts by READING them.
    /// </summary>
    /// <remarks>
    /// The design asks for one <c>COUNT(*)</c> per table. On this engine that is a number kept beside
    /// the rows, so a target whose rows never arrived could still answer with the right count; the
    /// check scans instead. Asserted here as coverage - every table the source has appears in the
    /// report - because a verification that silently skipped a table is the failure this exists to
    /// prevent.
    /// </remarks>
    [Test]
    public async Task EveryTableIsCountedOnBothSidesAsync()
    {
        var target = new ConnectionInfo
        {
            FilePath = Path.Combine(m_fixture.Root, "counted.witdb")
        };

        var result = await DatabaseMigrator.MigrateAsync(m_fixture.Database, target);

        var tables = await m_fixture.Database.GetTablesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Verification.Select(check => check.Table),
                Is.EquivalentTo(tables.Select(table => table.Name)));

            foreach (var check in result.Verification)
                Assert.That(check.InTarget, Is.EqualTo(check.InSource), check.Table);
        });
    }

    /// <summary>
    /// A path with something already at it is refused before anything is created - a migration that
    /// wrote into an existing database would be doing the one thing this operation promises not to.
    /// </summary>
    [Test]
    public async Task APathThatIsAlreadyTakenIsRefusedAsync()
    {
        var occupied = Path.Combine(m_fixture.Root, "occupied.witdb");

        StudioFixture.CreateDatabaseOnDisk(occupied);

        var before = new FileInfo(occupied).Length;

        var result = await DatabaseMigrator.MigrateAsync(m_fixture.Database,
            new ConnectionInfo { FilePath = occupied });

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(MigrationOutcome.Failed));
            Assert.That(new FileInfo(occupied).Length, Is.EqualTo(before),
                "and the database that was there is untouched");
        });
    }

    /// <summary>
    /// The copy a password change produces is in the CURRENT encryption format, which is what makes
    /// this window the documented migration for a database written before that format existed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 18 gave encrypted databases a plaintext preamble carrying a random salt, the iteration
    /// count and a nonce sequence that survives the file. It cannot repair a file already on disk:
    /// that file's salt IS its password's hash and its nonce counter restarts on every open, and
    /// both are properties of bytes nobody can change from here. What can be done is to write a NEW
    /// database, and that is exactly what this window already does.
    /// </para>
    /// <para>
    /// So the claim in <c>Docs/PHASE18-ENCRYPTION-PLAN.md</c> - "Studio's password change is the
    /// documented migration" - is answerable here rather than being a sentence in a document.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ThePasswordChangeWritesTheCurrentEncryptionFormatAsync()
    {
        const string PASSWORD = "correct horse battery staple";

        var target = new ConnectionInfo
        {
            FilePath = Path.Combine(m_fixture.Root, "migrated-format.witdb"),
            IsEncrypted = true,
            Password = PASSWORD,
            EncryptionProvider = "aes-gcm"
        };

        var result = await DatabaseMigrator.MigrateAsync(m_fixture.Database, target);

        Assert.That(result.Outcome, Is.EqualTo(MigrationOutcome.Transferred), result.EngineMessage);

        var bytes = await File.ReadAllBytesAsync(target.FilePath);
        var derived = OutWit.Database.Core.Utils.CryptoUtils.DerivePasswordSalt(PASSWORD);

        Assert.Multiple(() =>
        {
            Assert.That(OutWit.Database.Core.Encryption.CryptoHeader.TryReadFrom(bytes, out var header),
                Is.True, "the copy must carry a crypto preamble");

            Assert.That(header.Salt, Is.Not.EqualTo(derived),
                "and its salt must be the file's own, not the password's hash");

            Assert.That(header.Iterations, Is.GreaterThan(0),
                "and the iteration count must be recorded in the file rather than remembered by the "
                + "caller");
        });
    }

    /// <summary>
    /// The steps are reported as they happen, and each has words in every language.
    /// </summary>
    [Test]
    public async Task TheStepsAreReportedAndEveryOneHasWordsAsync()
    {
        var steps = new List<MigrationStep>();

        var target = new ConnectionInfo { FilePath = Path.Combine(m_fixture.Root, "stepped.witdb") };

        await DatabaseMigrator.MigrateAsync(m_fixture.Database, target,
            new Progress<MigrationStep>(step => steps.Add(step)));

        var localization = new OutWit.Database.Studio.Services.Localization.LocalizationService();

        Assert.Multiple(() =>
        {
            Assert.That(steps, Is.Not.Empty);

            foreach (var language in localization.Available)
            {
                var texts = localization.Texts(language.Code);

                foreach (var step in steps)
                    Assert.That(texts.ContainsKey(step.Key), Is.True, $"{language.Code}: {step.Key}");
            }
        });
    }

    #endregion
}

