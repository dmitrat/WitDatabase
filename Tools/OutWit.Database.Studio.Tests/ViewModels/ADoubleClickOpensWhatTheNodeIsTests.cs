using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A double click opens the thing the node IS.
/// </summary>
/// <remarks>
/// <para>
/// One rule rather than three exceptions. A table IS its rows, so it opens the editor (WS-19); a view
/// IS the rows it selects; and the connection IS the database, so it opens the tab that describes it
/// - the same one <i>Database…</i> opens in the menu. Asked for in chat on 2026-08-19: <i>would it
/// not be logical for a double click on the database to open its properties, as the menu item does?</i>
/// </para>
/// <para>
/// <b>The decision is in the ViewModel and this fixture is why.</b> It used to be a switch in the
/// code-behind, where the double click had already been broken once and repaired onto a route that
/// does not exist, with 1014 tests unable to say a word about any of it. A gesture belongs to the
/// view; which node opens what is a rule, and a rule written where no test can read it is a rule that
/// holds only until someone edits it.
/// </para>
/// </remarks>
[TestFixture]
public class ADoubleClickOpensWhatTheNodeIsTests
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

    #region What each node opens

    [Test]
    public async Task ATableOpensItsRowsTest()
    {
        Select(DatabaseNodeType.Table, "Customers");

        var opened = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.InstanceOf<TableEditTabViewModel>(),
                "a table IS its rows, and they are opened for editing rather than for reading");

            Assert.That(((TableEditTabViewModel)opened!).TableName, Is.EqualTo("Customers"));
        });
    }

    [Test]
    public async Task AViewOpensTheRowsItSelectsTest()
    {
        Select(DatabaseNodeType.View, "ActiveOrders");

        var opened = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.InstanceOf<QueryTabViewModel>(),
                "a view has no rows of its own to edit, so its query is opened instead");

            Assert.That(opened!.Title, Does.Contain("ActiveOrders"));
        });
    }

    /// <summary>
    /// The one this fixture was written for.
    /// </summary>
    [Test]
    public async Task TheConnectionOpensTheTabThatDescribesItTest()
    {
        Select(DatabaseNodeType.Database);

        var opened = await OpenAsync();

        Assert.That(opened, Is.InstanceOf<DatabaseTabViewModel>(),
            "the connection IS the database, and the double click opens what «Database…» opens");
    }

    /// <summary>
    /// And the same tab as the menu item, not a second one beside it.
    /// </summary>
    [Test]
    public async Task TheDoubleClickAndTheMenuItemOpenTheSameTabTest()
    {
        Select(DatabaseNodeType.Database);

        var fromTheDoubleClick = await OpenAsync();

        await m_studio.Explorer.OpenWhatItIsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Workspace.Tabs.OfType<DatabaseTabViewModel>().Count(), Is.EqualTo(1),
                "opening it twice is opening it once");

            Assert.That(m_studio.Workspace.Tabs, Does.Contain(fromTheDoubleClick));
        });
    }

    #endregion

    #region What no node opens

    [Test]
    public async Task NothingElseOpensAnythingTest()
    {
        var nothing = new[]
        {
            DatabaseNodeType.TablesFolder, DatabaseNodeType.ViewsFolder,
            DatabaseNodeType.IndexesFolder, DatabaseNodeType.TriggersFolder,
            DatabaseNodeType.SequencesFolder, DatabaseNodeType.RoutinesFolder,
            DatabaseNodeType.Index, DatabaseNodeType.Trigger, DatabaseNodeType.Column
        };

        var offenders = new List<string>();

        foreach (var type in nothing)
        {
            Select(type);

            var before = m_studio.Workspace.Tabs.Count;

            if (m_studio.Explorer.CanOpenWhatItIs)
                offenders.Add($"{type}: says it has something to open");

            await m_studio.Explorer.OpenWhatItIsAsync();

            if (m_studio.Workspace.Tabs.Count != before)
                offenders.Add($"{type}: opened a tab anyway");
        }

        Assert.Multiple(() =>
        {
            Assert.That(offenders, Is.Empty, string.Join(Environment.NewLine, offenders));

            // CONTROL: the other direction, in the same case. A property that answered false to
            // everything would satisfy every assertion above.
            Select(DatabaseNodeType.Table, "Customers");
            Assert.That(m_studio.Explorer.CanOpenWhatItIs, Is.True,
                "CONTROL: a table does have something to open");
        });
    }

    /// <summary>
    /// A node that cannot be reached because its connection has gone is not offered either - the
    /// same distinction the menu makes between «does not apply» and «cannot right now».
    /// </summary>
    [Test]
    public async Task ADisconnectedTreeOpensNothingTest()
    {
        Select(DatabaseNodeType.Table, "Customers");

        Assume.That(m_studio.Explorer.CanOpenWhatItIs, Is.True);

        await m_studio.Connections.CloseAllAsync();

        Assert.That(m_studio.Explorer.CanOpenWhatItIs, Is.False,
            "there is nothing left to open it in");
    }

    #endregion

    #region Tools

    /// <summary>Opens what the selected node is, and answers with the tab that appeared.</summary>
    private async Task<WorkspaceTabViewModel?> OpenAsync()
    {
        var before = m_studio.Workspace.Tabs.ToList();

        Assert.That(m_studio.Explorer.CanOpenWhatItIs, Is.True,
            "this node says it has nothing to open");

        await m_studio.Explorer.OpenWhatItIsAsync();

        return m_studio.Workspace.Tabs.Except(before).FirstOrDefault();
    }

    private void Select(DatabaseNodeType type, string? named = null)
    {
        var node = Walk(m_studio.Explorer.Nodes).FirstOrDefault(candidate =>
            candidate.NodeType == type && (named == null || candidate.Name == named));

        Assert.That(node, Is.Not.Null, $"the tree has a {type} node{(named == null ? "" : " called " + named)}");

        m_studio.Explorer.SelectedNode = node;
    }

    private static IEnumerable<DatabaseNode> Walk(IEnumerable<DatabaseNode> nodes)
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
