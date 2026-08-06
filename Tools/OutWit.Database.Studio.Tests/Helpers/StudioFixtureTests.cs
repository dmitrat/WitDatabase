namespace OutWit.Database.Studio.Tests.Helpers;

/// <summary>
/// The fixture is checked before anything is built on it.
///
/// A harness that quietly ships half a schema turns every test above it into a question about the
/// harness - and one that reports "no tables" for a database it never created would make an empty
/// explorer look correct. So: the schema really reaches the engine, on all three storages, and the
/// case that asks for no schema really has none.
/// </summary>
[TestFixture]
public class StudioFixtureTests
{
    #region Tests

    [TestCase(StudioStorage.BTree)]
    [TestCase(StudioStorage.Lsm)]
    [TestCase(StudioStorage.InMemory)]
    public async Task TheFixtureBuildsItsSchemaOnEveryStorageTest(StudioStorage storage)
    {
        await using var studio = await StudioFixture.CreateAsync(storage);

        var tables = await studio.Database.GetTablesAsync();
        var views = await studio.Database.GetViewsAsync();
        var indexes = await studio.Database.GetIndexesAsync();
        var triggers = await studio.Database.GetTriggersAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(studio.Database.IsConnected, Is.True);

            Assert.That(tables.Select(t => t.Name), Is.SupersetOf(new[] { "Customers", "Orders", "Logs" }),
                "the tables the harness promises its tests");
            Assert.That(views.Any(v => v.Contains("ActiveOrders", StringComparison.OrdinalIgnoreCase)), Is.True,
                "a view, because 'a view is not editable' is a case");
            Assert.That(indexes.Any(i => i.Contains("IX_Orders_CustomerId", StringComparison.OrdinalIgnoreCase)), Is.True,
                "an index, because the explorer has a folder for them");
            Assert.That(triggers.Any(t => t.Contains("TR_Orders_Audit", StringComparison.OrdinalIgnoreCase)), Is.True,
                "a trigger, for the same reason");

            Assert.That(await studio.CountRowsAsync("Customers"), Is.EqualTo(StudioFixture.CUSTOMER_COUNT));
            Assert.That(await studio.CountRowsAsync("Orders"), Is.EqualTo(StudioFixture.ORDER_COUNT));
        });
    }

    /// <summary>
    /// The primary key matters more than the row count: half the editor's behaviour is decided by
    /// whether Studio can name one row, so the harness carries a table that can be edited and one
    /// that cannot.
    /// </summary>
    [Test]
    public async Task TheFixtureCarriesATableWithAKeyAndOneWithoutTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var orders = await studio.Database.GetColumnsAsync("Orders");
        var logs = await studio.Database.GetColumnsAsync("Logs");

        Assert.Multiple(() =>
        {
            Assert.That(orders.Any(c => c.IsPrimaryKey), Is.True, "Orders has a key and can be edited");
            Assert.That(logs.Any(c => c.IsPrimaryKey), Is.False, "Logs has none and must open read-only");
        });
    }

    /// <summary>
    /// CONTROL. Without this, "the schema is there" would pass for a harness that reports whatever it
    /// was asked about, and an empty database - what a user sees straight after Create - would never
    /// be exercised.
    /// </summary>
    [Test]
    public async Task ControlAFixtureAskedForNoSchemaHasNoneTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        var tables = await studio.Database.GetTablesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(studio.Database.IsConnected, Is.True, "connected, but empty");
            Assert.That(tables, Is.Empty, "CONTROL: the schema is created by the fixture, not by the engine");
        });
    }

    /// <summary>
    /// The application keeps ONE DatabaseService alive for its whole run, and phase 13's worst defect
    /// only existed on the second use of a dirty one. The fixture has to reproduce that lifetime, not
    /// a cleaner one.
    /// </summary>
    [Test]
    public async Task TheFixtureReusesOneServiceAcrossConnectionsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var statuses = new List<bool>();
        studio.Database.ConnectionStatusChanged += (_, connected) => statuses.Add(connected);

        await studio.Database.DisconnectAsync();
        await studio.ConnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(studio.Database.IsConnected, Is.True);
            Assert.That(statuses, Is.EqualTo(new[] { false, true }),
                "the views bind to this event stream - it has to describe what actually happened");
        });
    }

    /// <summary>
    /// The settings service is the real one, pointed at the fixture's own folder. Checked, because a
    /// wrong path here would silently read and write the settings of whoever ran the suite.
    /// </summary>
    [Test]
    public async Task TheFixtureKeepsItsSettingsToItselfTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        await studio.Settings.AddRecentFileAsync(@"D:\somewhere\else.witdb");

        var written = Directory.GetFiles(studio.Root, "settings.json", SearchOption.AllDirectories);

        Assert.Multiple(() =>
        {
            Assert.That(written, Has.Length.EqualTo(1), "the settings file lives under the fixture root");
            Assert.That(File.ReadAllText(written[0]), Does.Contain("else.witdb"));
        });
    }

    #endregion
}
