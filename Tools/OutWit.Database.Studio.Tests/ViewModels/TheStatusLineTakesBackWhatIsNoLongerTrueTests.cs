using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The status line may keep saying what HAPPENED; it may not keep saying what IS.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured in the running application on 2026-08-19</b>, with no editor open anywhere: the line
/// read <i>Editing table: Products</i>. The tab had been opened and closed again, and the sentence
/// stayed behind it.
/// </para>
/// <para>
/// Most of what this line carries is an event - «executed in 9 ms», «loaded 28 rows» - and an event
/// stays true after it happens, which is why the line is not simply cleared when anything changes.
/// «Editing table X» is not an event. It is a state, it belongs to the tab, and it ends when the tab
/// does.
/// </para>
/// </remarks>
[TestFixture]
public class TheStatusLineTakesBackWhatIsNoLongerTrueTests
{
    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        await m_studio.Explorer.RefreshAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region The rule

    [Test]
    public async Task ClosingTheEditorTakesEditingOffTheLineTest()
    {
        var ready = m_studio.App.Localization["Status.Ready"];

        var tab = await OpenTheEditorAsync();

        Assume.That(m_studio.MainWindow.StatusText, Does.Contain("Customers"),
            "the line says which table is being edited");

        await CloseAsync(tab);

        Assert.That(m_studio.MainWindow.StatusText, Is.EqualTo(ready),
            "nothing is being edited, so the line does not say that anything is - and what it "
            + "falls back to is the same sentence the window starts with, not whatever happened "
            + "to be there before the editor opened");
    }

    /// <summary>
    /// The other direction, and the reason the owner is remembered rather than the line being
    /// cleared: a message that arrived AFTER the editor opened belongs to whoever sent it, and
    /// closing the editor must not take it away.
    /// </summary>
    [Test]
    public async Task ClosingTheEditorLeavesSomebodyElsesMessageAloneTest()
    {
        var tab = await OpenTheEditorAsync();

        const string SINCE_THEN = "Something else happened";

        m_studio.MainWindow.StatusText = SINCE_THEN;

        await CloseAsync(tab);

        Assert.That(m_studio.MainWindow.StatusText, Is.EqualTo(SINCE_THEN),
            "the editor only takes back what is still its own sentence");
    }

    /// <summary>
    /// And an EVENT stays. This is the case that stops the rule above from becoming "clear the line
    /// whenever a tab closes", which would throw away the answer to the last thing the user did.
    /// </summary>
    [Test]
    public async Task ClosingAQueryTabLeavesWhatItReportedOnTheLineTest()
    {
        var query = m_studio.Workspace.OpenQueryTab("SELECT 1", "one", m_studio.Database);

        const string HAPPENED = "Query executed successfully in 9 ms";

        m_studio.MainWindow.StatusText = HAPPENED;

        await CloseAsync(query);

        Assert.That(m_studio.MainWindow.StatusText, Is.EqualTo(HAPPENED),
            "what happened stays true after the tab that did it has gone");
    }

    #endregion

    #region Tools

    private async Task<TableEditTabViewModel> OpenTheEditorAsync()
    {
        var tab = await m_studio.Workspace.OpenTableEditTabAsync(m_studio.Database, "Customers");

        // Through the explorer, because that is where the sentence is written.
        m_studio.Explorer.SelectedNode = m_studio.Explorer.Nodes
            .SelectMany(Flatten)
            .First(node => node.NodeType == Studio.Models.DatabaseNodeType.Table
                        && node.Name == "Customers");

        await m_studio.Explorer.OpenWhatItIsAsync();

        return tab;
    }

    private async Task CloseAsync(WorkspaceTabViewModel tab)
    {
        await StudioFixture.PressAsync(m_studio.Workspace.CloseTabCommand, tab);
    }

    private static IEnumerable<Studio.Models.DatabaseNode> Flatten(Studio.Models.DatabaseNode node)
    {
        yield return node;

        foreach (var child in node.Children.SelectMany(Flatten))
            yield return child;
    }

    #endregion
}
