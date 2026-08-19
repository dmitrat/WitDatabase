using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The tree's own gestures (section 2.7): the middle click, and typing letters to walk the selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a gesture test can and cannot promise, said out loud.</b> Whether the middle BUTTON or the
/// letter <c>o</c> reaches this code is a question about Avalonia and the window, and it was answered
/// by driving the running application - the middle click opened «Customers - Top 100» behind the tab in
/// front, and F4 opened a structure. What lives here is everything after the event arrives, which is
/// where the behaviour actually is: <i>which</i> tab runs, and <i>which</i> node a prefix finds.
/// </para>
/// <para>
/// The keys themselves are checked from the other end by <c>KeyboardMapTests</c>, which reads this
/// project's two code-behind files and the markup and refuses a gesture the keyboard window promises
/// and nothing handles.
/// </para>
/// </remarks>
[TestFixture]
public class ExplorerTreeGesturesTests
{
    #region Fields

    private StudioFixture m_fixture = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        await m_fixture.Explorer.RefreshAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region The middle click

    /// <summary>
    /// The middle click opens the data in a tab that does NOT come to the front - and that tab has
    /// run, which is the half that is easy to lose.
    /// </summary>
    /// <remarks>
    /// <b>The failure this exists for is silent.</b> The shell's execute command runs the SELECTED
    /// tab, so a background tab told to run through it executes whatever is in front instead: the new
    /// tab shows SQL and no rows, the tab the person was reading quietly re-runs, and nothing anywhere
    /// says so. Both halves are asserted - the one that opened has rows, and the one in front is
    /// untouched.
    /// </remarks>
    [Test]
    public async Task TheMiddleClickOpensTheDataBehindWhateverIsInFrontTest()
    {
        var workspace = m_fixture.Workspace;
        var explorer = m_fixture.Explorer;

        var front = workspace.SelectedTab;

        explorer.SelectedNode = Find("Customers", DatabaseNodeType.Table);
        explorer.BrowseDataInBackground();

        var opened = await Ran("Customers - Top 100");

        Assert.Multiple(() =>
        {
            Assert.That(workspace.SelectedTab, Is.SameAs(front),
                "the middle click brought its tab to the front, which is what the double click is for");

            Assert.That(opened.ResultData, Is.Not.Null,
                "the tab opened holding SQL and never ran - the shell's execute ran the selected tab");

            Assert.That(opened.ResultData!.Rows, Has.Count.EqualTo(StudioFixture.CUSTOMER_COUNT));
        });
    }

    /// <summary>
    /// CONTROL, in the other direction: the menu's own «Первые 100» DOES bring its tab to the front.
    /// </summary>
    /// <remarks>
    /// Without this the case above passes just as well if nothing ever activates a tab - and "the tab
    /// did not come forward" would be a fact about the workspace rather than about the gesture.
    /// </remarks>
    [Test]
    public async Task TheMENUItemBringsTheSameTabToTheFrontTest()
    {
        var workspace = m_fixture.Workspace;
        var explorer = m_fixture.Explorer;

        var front = workspace.SelectedTab;

        explorer.SelectedNode = Find("Customers", DatabaseNodeType.Table);
        explorer.SelectTop100Command.Execute(null);

        var opened = await Ran("Customers - Top 100");

        Assert.Multiple(() =>
        {
            Assert.That(workspace.SelectedTab, Is.Not.SameAs(front));
            Assert.That(workspace.SelectedTab, Is.SameAs(opened));
        });
    }

    #endregion

    #region Type-ahead

    /// <summary>
    /// Letters find the first OPEN node whose name starts with them.
    /// </summary>
    [Test]
    public void TypingLettersWalksToTheFirstOpenNodeThatStartsWithThemTest()
    {
        var explorer = m_fixture.Explorer;
        var tables = Find("Customers", DatabaseNodeType.Table);

        explorer.SelectedNode = null;

        Assert.Multiple(() =>
        {
            Assert.That(explorer.JumpTo("cust"), Is.True);
            Assert.That(explorer.SelectedNode, Is.SameAs(tables));

            Assert.That(explorer.JumpTo("ord"), Is.True);
            Assert.That(explorer.SelectedNode!.Name, Is.EqualTo("Orders"),
                "and the case of what was typed is not the case of the name");
        });
    }

    /// <summary>
    /// A node inside a CLOSED branch cannot be jumped to.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole reason the walk is not a plain recursion over the tree.</b> A jump into a
    /// collapsed branch moves the selection to a row nobody can see: the inspector on the right starts
    /// describing an object that is not on the screen, the tree looks untouched, and the next key
    /// operates on something the person never chose.
    /// </remarks>
    [Test]
    public void ACollapsedBranchHidesItsChildrenFromTheSearchTest()
    {
        var explorer = m_fixture.Explorer;
        var logs = Find("Logs", DatabaseNodeType.Table);

        foreach (var node in Walk(explorer.Nodes))
            node.IsExpanded = false;

        explorer.SelectedNode = null;

        Assert.Multiple(() =>
        {
            Assert.That(explorer.JumpTo("logs"), Is.False,
                "a node in a closed branch was selected, and the tree would not have moved");

            Assert.That(explorer.SelectedNode, Is.Null, "and a search that found nothing moved nothing");
        });

        foreach (var node in Walk(explorer.Nodes))
            node.IsExpanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(explorer.JumpTo("logs"), Is.True, "CONTROL: the same node, once it is on screen");
            Assert.That(explorer.SelectedNode, Is.SameAs(logs));
        });
    }

    /// <summary>
    /// One letter pressed twice walks to the NEXT match; a growing prefix stays where it is.
    /// </summary>
    /// <remarks>
    /// The two behaviours are one rule seen from both ends - where the search starts. Both are here
    /// because either one alone looks like the other's bug: a jump that never moves is "type-ahead is
    /// broken", and a jump that always moves makes the second letter of a name walk away from it.
    /// </remarks>
    [Test]
    public void OneLetterWalksAndALongerPrefixStaysTest()
    {
        var explorer = m_fixture.Explorer;

        explorer.SelectedNode = null;

        // Three objects start with O in this schema: Orders, OrdersAudit and the index on Orders is
        // IX_..., so the walk is Orders -> OrdersAudit -> back to Orders.
        Assert.That(explorer.JumpTo("o"), Is.True);
        var first = explorer.SelectedNode!;

        Assert.That(explorer.JumpTo("o"), Is.True);
        var second = explorer.SelectedNode!;

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first),
                "the same letter pressed twice sat on the first match forever");

            Assert.That(second.Name, Does.StartWith("O").IgnoreCase);
        });

        // And now the other end: with "Orders" selected, typing the rest of its name leaves it alone
        // rather than hunting for another node that also matches.
        explorer.SelectedNode = Find("Orders", DatabaseNodeType.Table);

        Assert.That(explorer.JumpTo("orders"), Is.True);
        Assert.That(explorer.SelectedNode!.Name, Is.EqualTo("Orders"));
    }

    /// <summary>
    /// CONTROL: a prefix nothing starts with changes nothing and says so.
    /// </summary>
    [Test]
    public void APrefixThatMatchesNothingLeavesTheSelectionAloneTest()
    {
        var explorer = m_fixture.Explorer;

        explorer.SelectedNode = Find("Orders", DatabaseNodeType.Table);

        Assert.Multiple(() =>
        {
            Assert.That(explorer.JumpTo("zzz"), Is.False);
            Assert.That(explorer.SelectedNode!.Name, Is.EqualTo("Orders"));

            Assert.That(explorer.JumpTo(""), Is.False, "and nothing typed is not a search");
            Assert.That(explorer.SelectedNode!.Name, Is.EqualTo("Orders"));
        });
    }

    /// <summary>
    /// The order the search walks in is the order the tree DRAWS in: a node, then its children.
    /// </summary>
    /// <remarks>
    /// A depth-first walk that yielded the children first, or a flat pass over every collection in
    /// turn, would still find every name - and would jump to the wrong one of two matches without ever
    /// failing a test that only asked "was something found".
    /// </remarks>
    [Test]
    public async Task TheWalkIsInTheOrderTheTreeDrawsTest()
    {
        var explorer = m_fixture.Explorer;

        foreach (var node in Walk(explorer.Nodes))
            node.IsExpanded = true;

        // Opening a table READS its columns since 2026-08-18, and the read is asynchronous. The
        // two lists below straddled it and disagreed by four nodes, which is a race in the case
        // rather than a defect in the walk - a tree half way through loading is a real state, and
        // this case is about the ORDER of a settled one.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (Walk(explorer.Nodes).All(node => node.ChildrenLoaded || !node.IsExpanded))
                break;

            await Task.Delay(20);
        }

        var visible = explorer.VisibleNodes().ToList();
        var root = explorer.Nodes[0];

        Assert.Multiple(() =>
        {
            Assert.That(visible[0], Is.SameAs(root), "the root is drawn before anything under it");

            Assert.That(visible.IndexOf(root.Children[0]), Is.EqualTo(1),
                "and a node's first child comes immediately after it, not after its siblings");

            Assert.That(visible, Has.Count.EqualTo(Walk(explorer.Nodes).Count()),
                "CONTROL: everything expanded, the walk is the whole tree - so the case above measures "
                + "the ORDER and not a filter");
        });
    }

    #endregion

    #region Tools

    private DatabaseNode Find(string name, DatabaseNodeType type)
    {
        var node = Walk(m_fixture.Explorer.Nodes)
            .FirstOrDefault(n => n.NodeType == type && n.Name == name);

        Assert.That(node, Is.Not.Null, $"{type} {name} is not in the tree.");

        return node!;
    }

    private static IEnumerable<DatabaseNode> Walk(IEnumerable<DatabaseNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsPlaceholder)
                continue;

            yield return node;

            foreach (var child in Walk(node.Children))
                yield return child;
        }
    }

    /// <summary>
    /// The tab with this title, once it has finished running. Both gestures start the query without
    /// awaiting it, so the alternative is asserting on a tab that has not executed yet.
    /// </summary>
    private async Task<QueryTabViewModel> Ran(string title)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var tab = m_fixture.Workspace.Tabs
                .OfType<QueryTabViewModel>()
                .FirstOrDefault(candidate => candidate.Title == title);

            if (tab is { HasResults: true })
                return tab;

            await Task.Delay(10);
        }

        throw new TimeoutException($"The tab «{title}» never opened, or never ran.");
    }

    #endregion
}
