using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The query history (WS-29), in a real WitDatabase of its own.
///
/// Every case here uses the shipping service over a real store in a temporary folder - the feature IS
/// "it survives a restart", and a fake would answer that question by construction.
/// </summary>
[TestFixture]
public class QueryHistoryTests
{
    #region Fixture

    private string m_root = null!;
    private string m_path = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioHistory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);

        m_path = Path.Combine(m_root, "history.witdb");
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
            // a leaked handle is a finding, not a reason to fail the teardown
        }
    }

    private QueryHistoryService Service() => new(m_path, NullLogger<QueryHistoryService>.Instance);

    #endregion

    #region What it remembers

    [Test]
    public async Task AQueryIsRememberedWithWhatHappenedToItTest()
    {
        await using var history = Service();
        await history.InitializeAsync();

        Assert.That(history.IsAvailable, Is.True, history.UnavailableReason);

        await history.RecordAsync("SELECT * FROM Orders", "sales", 18.5, 312, "ok");

        var entries = await history.SearchAsync();

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Text, Is.EqualTo("SELECT * FROM Orders"));
        Assert.That(entries[0].Connection, Is.EqualTo("sales"));
        Assert.That(entries[0].Rows, Is.EqualTo(312));
        Assert.That(entries[0].Status, Is.EqualTo("ok"));
        Assert.That(entries[0].DurationMs, Is.EqualTo(18.5).Within(0.001));
        Assert.That(entries[0].Uses, Is.EqualTo(1));
    }

    /// <summary>
    /// The whole reason the history is in a database rather than in memory.
    /// </summary>
    [Test]
    public async Task TheHistorySurvivesARestartTest()
    {
        await using (var first = Service())
        {
            await first.InitializeAsync();
            await first.RecordAsync("SELECT * FROM Customers", "sales", 4, 3, "ok");
        }

        await using var second = Service();
        await second.InitializeAsync();

        var entries = await second.SearchAsync();

        Assert.That(entries.Select(entry => entry.Text), Does.Contain("SELECT * FROM Customers"));
    }

    [Test]
    public async Task TheSameQueryAgainIsOneEntryRaisedTest()
    {
        await using var history = Service();
        await history.InitializeAsync();

        await history.RecordAsync("SELECT 1 FROM Orders", "sales", 4, 1, "ok");
        await history.RecordAsync("SELECT 2 FROM Orders", "sales", 4, 1, "ok");
        await history.RecordAsync("SELECT 1 FROM Orders", "sales", 9, 1, "ok");

        var entries = await history.SearchAsync();

        Assert.That(entries, Has.Count.EqualTo(2), "a repeat is not a second row");

        var repeated = entries.Single(entry => entry.Text == "SELECT 1 FROM Orders");

        Assert.That(repeated.Uses, Is.EqualTo(2));
        Assert.That(repeated.DurationMs, Is.EqualTo(9).Within(0.001), "the newest run is the one described");
        Assert.That(entries[0].Text, Is.EqualTo("SELECT 1 FROM Orders"), "and it comes back to the top");
    }

    [Test]
    public async Task AFailedQueryIsRememberedAsFailedTest()
    {
        await using var history = Service();
        await history.InitializeAsync();

        await history.RecordAsync("SELECT * FROM Ordres", "sales", 2, 0, "error");

        var entries = await history.SearchAsync();

        Assert.That(entries[0].Status, Is.EqualTo("error"),
            "the query somebody wants back is often the one that failed");
    }

    #endregion

    #region Searching

    [Test]
    public async Task TheHistoryIsSearchedByTextTest()
    {
        await using var history = Service();
        await history.InitializeAsync();

        await history.RecordAsync("SELECT * FROM Orders WHERE Total > 100", "sales", 1, 1, "ok");
        await history.RecordAsync("SELECT * FROM Customers", "sales", 1, 1, "ok");
        await history.RecordAsync("UPDATE Orders SET Status = 'shipped'", "sales", 1, 1, "ok");

        var found = await history.SearchAsync("Orders");

        Assert.That(found, Has.Count.EqualTo(2));
        Assert.That(found.All(entry => entry.Text.Contains("Orders")), Is.True);

        Assert.That(await history.SearchAsync("Nothing like this"), Is.Empty);
    }

    [Test]
    public async Task TheNewestIsFirstTest()
    {
        await using var history = Service();
        await history.InitializeAsync();

        await history.RecordAsync("SELECT 1 FROM Orders", "sales", 1, 1, "ok");
        await Task.Delay(5);
        await history.RecordAsync("SELECT 2 FROM Orders", "sales", 1, 1, "ok");
        await Task.Delay(5);
        await history.RecordAsync("SELECT 3 FROM Orders", "sales", 1, 1, "ok");

        var entries = await history.SearchAsync();

        Assert.That(entries.Select(entry => entry.Text),
            Is.EqualTo(new[] { "SELECT 3 FROM Orders", "SELECT 2 FROM Orders", "SELECT 1 FROM Orders" }));
    }

    [Test]
    public async Task ClearingRemovesEverythingTest()
    {
        await using var history = Service();
        await history.InitializeAsync();

        await history.RecordAsync("SELECT * FROM Orders", "sales", 1, 1, "ok");
        await history.ClearAsync();

        Assert.That(await history.SearchAsync(), Is.Empty);
    }

    #endregion

    #region What must never be in it

    /// <summary>
    /// A password reached the log file once already (stage 0, B1). A store that survives restarts is a
    /// worse place for the same mistake, so the assertion is over the FILE.
    /// </summary>
    [Test]
    public async Task NoConnectionStringAndNoPasswordEverReachTheStoreTest()
    {
        await using (var history = Service())
        {
            await history.InitializeAsync();

            await history.RecordAsync("SELECT * FROM Orders", "sales", 1, 1, "ok");
        }

        var bytes = await File.ReadAllBytesAsync(m_path);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.That(text, Does.Contain("SELECT * FROM Orders"),
            "the positive control: we are reading the right file, and the query IS in it");
        Assert.That(text, Does.Not.Contain("Password"));
        Assert.That(text, Does.Not.Contain("Data Source="));
    }

    #endregion

    #region When it cannot be had

    /// <summary>
    /// The history is Studio being a consumer of its own engine, which means an engine defect breaks
    /// it. It must never be able to stop somebody running a query.
    /// </summary>
    [Test]
    public async Task AStoreThatCannotBeOpenedIsReportedAndChangesNothingTest()
    {
        var impossible = Path.Combine(m_path, "inside-a-file", "history.witdb");

        await File.WriteAllTextAsync(m_path, "this is not a database");

        await using var history = new QueryHistoryService(impossible, NullLogger<QueryHistoryService>.Instance);
        await history.InitializeAsync();

        Assert.That(history.IsAvailable, Is.False);
        Assert.That(history.UnavailableReason, Is.Not.Null.And.Not.Empty);

        // And every call is a no-op rather than a throw.
        await history.RecordAsync("SELECT * FROM Orders", "sales", 1, 1, "ok");

        Assert.That(await history.SearchAsync(), Is.Empty);

        await history.ClearAsync();
    }

    #endregion
}
