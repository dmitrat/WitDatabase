using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A setting changes what the application DOES, not just what the settings file says.
/// </summary>
/// <remarks>
/// <para>
/// The lint next door (<c>SettingsAreActedOnTests</c>) asks whether anything reads each setting. That
/// is a structural question and it cannot tell a reader that obeys from one that reads and ignores.
/// These cases drive the behaviour through the real <c>SettingsService</c> the fixture builds.
/// </para>
/// <para>
/// <b>Each one asserts both positions</b> - the setting on and the setting off - because "the count is
/// absent" is also what a broken count looks like, and "the underline is clear" is also what correct
/// SQL looks like. Only the pair distinguishes obeying the setting from not working at all.
/// </para>
/// </remarks>
[TestFixture]
public class SettingsChangeBehaviourTests
{
    #region Setup

    private StudioFixture m_fixture = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region Counting rows in the tree (WS-16)

    [Test]
    public async Task TheTreeCountsRowsWhenTheSettingIsOnTest()
    {
        m_fixture.App.Settings.Current.CountRowsAutomatically = true;

        await m_fixture.Explorer.RefreshAsync();
        await WaitForCountsAsync();

        Assert.That(CountedTables(), Is.Not.Empty,
            "with counting on, at least one table must end up with a row count");
    }

    [Test]
    public async Task TheTreeDoesNotCountRowsWhenTheSettingIsOffTest()
    {
        m_fixture.App.Settings.Current.CountRowsAutomatically = false;

        await m_fixture.Explorer.RefreshAsync();
        await WaitForCountsAsync();

        Assert.That(CountedTables(), Is.Empty,
            "with counting off, nothing may count - this is the one thing the tree does that touches "
            + "every table, and it is what someone with a large database switches off");
    }

    #endregion

    #region The syntax underline (3.6)

    [Test]
    public async Task BrokenSqlIsUnderlinedWhileTypingWhenTheSettingIsOnTest()
    {
        m_fixture.App.Settings.Current.CheckSyntaxAsYouType = true;

        var tab = m_fixture.FirstQueryTab;
        tab.SqlText = "SELECT FROM WHERE";

        await WaitUntilAsync(() => tab.SyntaxError != null);

        Assert.That(tab.SyntaxError, Is.Not.Null, "broken SQL must be reported while it is typed");
    }

    [Test]
    public async Task NothingIsUnderlinedWhileTypingWhenTheSettingIsOffTest()
    {
        var tab = m_fixture.FirstQueryTab;

        // Start from the state the other case proves is reachable, so this measures the setting being
        // obeyed rather than a check that never ran.
        m_fixture.App.Settings.Current.CheckSyntaxAsYouType = true;
        tab.SqlText = "SELECT FROM WHERE";
        await WaitUntilAsync(() => tab.SyntaxError != null);

        m_fixture.App.Settings.Current.CheckSyntaxAsYouType = false;
        tab.SqlText = "SELECT FROM WHERE ORDER";

        await Task.Delay(400);

        Assert.That(tab.SyntaxError, Is.Null,
            "with the check off nothing may be underlined, and what was underlined before must go - "
            + "a mark that no longer follows the text points at a position that has since moved");
    }

    /// <summary>
    /// What the setting does NOT switch off, stated as a case because it is a decision: executing a
    /// statement still reports why it was refused.
    /// </summary>
    [Test]
    public async Task ExecutingStillReportsASyntaxErrorWithTheSettingOffTest()
    {
        m_fixture.App.Settings.Current.CheckSyntaxAsYouType = false;

        var tab = m_fixture.FirstQueryTab;
        tab.SqlText = "SELECT FROM WHERE";

        await Task.Delay(200);
        tab.CheckSyntaxNow();

        Assert.That(tab.SyntaxError, Is.Not.Null,
            "the setting is about being corrected WHILE TYPING, not about being told why a query "
            + "cannot run");
    }

    #endregion

    #region The grid's page size

    [Test]
    public async Task ANewEditorTabOpensAtTheConfiguredPageSizeTest()
    {
        m_fixture.App.Settings.Current.GridPageSize = 200;

        var tab = await m_fixture.Workspace.OpenTableEditTabAsync(m_fixture.Database, "Orders");

        Assert.Multiple(() =>
        {
            Assert.That(tab.PageSize, Is.EqualTo(200), "the tab did not start at the configured size");

            Assert.That(tab.PageSizes, Contains.Item(200),
                "the selector must offer the size the tab is showing, or the tab opens on a value the "
                + "list has no entry for");
        });
    }

    /// <summary>
    /// The control on the case above: a different value gives a different tab, so the first one is not
    /// passing on a coincidence between the default and the number asked for.
    /// </summary>
    [Test]
    public async Task ADifferentPageSizeGivesADifferentTabTest()
    {
        m_fixture.App.Settings.Current.GridPageSize = 5000;

        var tab = await m_fixture.Workspace.OpenTableEditTabAsync(m_fixture.Database, "Orders");

        Assert.That(tab.PageSize, Is.EqualTo(5000));
    }

    #endregion

    #region Tools

    private IReadOnlyList<string> CountedTables()
    {
        return Walk(m_fixture.Explorer.Nodes)
            .Where(node => node.NodeType == OutWit.Database.Studio.Models.DatabaseNodeType.Table)
            .Where(node => node.RowCount != null)
            .Select(node => node.Name)
            .ToList();
    }

    private async Task WaitForCountsAsync()
    {
        // Long enough for the counts to arrive on this fixture, which answers them instantly; the
        // negative case waits the same time, so neither is measuring the delay.
        await WaitUntilAsync(() => CountedTables().Count > 0, TimeSpan.FromSeconds(3));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }
    }

    private static IEnumerable<OutWit.Database.Studio.Models.DatabaseNode> Walk(
        IEnumerable<OutWit.Database.Studio.Models.DatabaseNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Walk(node.Children))
                yield return child;
        }
    }

    #endregion
}
