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

    /// <summary>
    /// Replaces the fixture's schema with one that has no trigger in it.
    /// </summary>
    /// <remarks>
    /// Not tidiness: a trigger cannot be carried by the dump at all yet (see the case above), and a
    /// migration case built on the fixture's own schema would only ever be measuring that.
    /// </remarks>
    private async Task BuildSchemaWithoutTriggersAsync()
    {
        foreach (var sql in new[]
                 {
                     "DROP TRIGGER TR_Orders_Audit",
                     "DROP VIEW ActiveOrders"
                 })
        {
            try
            {
                await m_fixture.Database.ExecuteQueryAsync(sql);
            }
            catch
            {
                // Already gone is the state this wants.
            }
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region Tests

    /// <summary>
    /// PINS A DEFECT, NOT CORRECT BEHAVIOUR. A database with a TRIGGER in it cannot be migrated,
    /// because the dump it is carried by cannot be executed back.
    ///
    /// <para>
    /// Measured 2026-08-08, the first time a dump was ever run into an empty database: the script ends
    /// with <c>INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);</c> as a statement of its own - the
    /// BODY of the trigger, cut loose from it - and the engine refuses it with "Column 'Id' not found".
    /// So the dump is not round-trippable, which is the one thing a dump is for.
    /// </para>
    /// <para>
    /// When that is fixed this case goes RED and should be replaced by the ordinary one: a database
    /// with a trigger migrates, and the trigger is in the copy.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADatabaseWithATriggerCannotBeMigratedYetAsync()
    {
        var result = await DatabaseMigrator.MigrateAsync(m_fixture.Database,
            new ConnectionInfo { FilePath = Path.Combine(m_fixture.Root, "with-trigger.witdb") });

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(MigrationOutcome.Failed));

            // The statement, not just the message - which is what makes this report usable at all.
            Assert.That(result.EngineMessage, Does.Contain("NEW.Id"));
            Assert.That(result.EngineMessage, Does.Contain("of"));
        });
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
        await BuildSchemaWithoutTriggersAsync();

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
        await BuildSchemaWithoutTriggersAsync();

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
    /// The steps are reported as they happen, and each has words in every language.
    /// </summary>
    [Test]
    public async Task TheStepsAreReportedAndEveryOneHasWordsAsync()
    {
        await BuildSchemaWithoutTriggersAsync();

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
