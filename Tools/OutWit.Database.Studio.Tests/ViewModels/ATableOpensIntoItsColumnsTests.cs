using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A table in the tree opens into its columns.
/// </summary>
/// <remarks>
/// <para>
/// Finding 15, and the biggest of the set: an entire section of the Explorer article describes it -
/// <i>columns, not a folder of columns; keys carry a mark of their own and types sit beside the
/// names, so seeing what a table holds costs one click</i>. The build had no chevron beside a table,
/// gave it no children in the accessibility tree, and moved the selection to the next table when
/// Right was pressed.
/// </para>
/// <para>
/// <b>It was all written.</b> <c>ExpandNodeAsync</c> reads the columns and the foreign keys and marks
/// the key; <c>WatchForExpansion</c> subscribes to each table's <c>IsExpanded</c> and calls it. What
/// was missing is the one thing that makes either reachable: a node with no children draws no
/// expander, so <c>IsExpanded</c> could never become true, so the loader could never run. A
/// placeholder child is what breaks the circle.
/// </para>
/// <para>
/// <b>And the placeholder must not leak.</b> It exists to make an expander appear and for nothing
/// else, so the filter, the palette and the counts must never see it - which is where a lazy tree
/// usually goes wrong.
/// </para>
/// </remarks>
[TestFixture]
public class ATableOpensIntoItsColumnsTests
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

    #region Tests

    [Test]
    public void ATableCanBeExpandedAtAllTest()
    {
        var table = TableNode("Orders");

        Assert.Multiple(() =>
        {
            Assert.That(table.Children, Is.Not.Empty,
                "a node with no children draws no expander, so it could never be opened");

            Assert.That(table.Children.All(child => child.IsPlaceholder), Is.True,
                "and what is there is a placeholder, not a column read in advance");

            Assert.That(table.ChildrenLoaded, Is.False, "the columns have not been asked for yet");
        });
    }

    [Test]
    public async Task ExpandingItReadsTheColumnsTest()
    {
        var table = TableNode("Orders");

        await m_studio.Explorer.ExpandNodeAsync(table);

        var columns = table.Children.Select(child => child.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(table.Children.Any(child => child.IsPlaceholder), Is.False,
                "the placeholder goes when the real thing arrives");

            Assert.That(columns, Is.EquivalentTo(new[] { "Id", "CustomerId", "Total", "Status" }));

            var key = table.Children.First(child => child.Name == "Id");

            Assert.That(key.IsPrimaryKey, Is.True, "the key carries a mark of its own");
            Assert.That(key.Detail, Is.Not.Null.And.Not.Empty, "and the type sits beside the name");

            var foreign = table.Children.First(child => child.Name == "CustomerId");

            Assert.That(foreign.IsForeignKey, Is.True, "so does what points somewhere else");
        });
    }

    /// <summary>
    /// The path a person takes: the expander, which sets <c>IsExpanded</c>.
    /// </summary>
    [Test]
    public async Task OpeningTheNodeIsWhatLoadsThemTest()
    {
        var table = TableNode("Orders");

        table.IsExpanded = true;

        for (var attempt = 0; attempt < 100 && !table.ChildrenLoaded; attempt++)
            await Task.Delay(20);

        Assert.That(table.ChildrenLoaded, Is.True,
            "opening the node is what reads the columns - nothing else has to be pressed");
    }

    [Test]
    public async Task ThePlaceholderReachesNeitherTheFilterNorTheCountsTest()
    {
        var before = m_studio.Explorer.Nodes.Count;

        m_studio.Explorer.Filter = "Orders";

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Explorer.FilterMatches.Any(match => match.Node.IsPlaceholder), Is.False,
                "the filter never offers a placeholder");

            Assert.That(m_studio.Explorer.Nodes, Has.Count.EqualTo(before),
                "and it changes no count");
        });

        m_studio.Explorer.Filter = string.Empty;

        // And it is gone for good once the columns are there, so nothing can find it later either.
        await m_studio.Explorer.ExpandNodeAsync(TableNode("Orders"));

        m_studio.Explorer.Filter = "Total";

        Assert.That(m_studio.Explorer.FilterMatches.Any(match => match.Node.IsPlaceholder), Is.False);
    }

    #endregion

    #region Tools

    private DatabaseNode TableNode(string name)
    {
        var node = m_studio.Explorer.Nodes
            .SelectMany(root => root.Children)
            .Where(folder => folder.NodeType == DatabaseNodeType.TablesFolder)
            .SelectMany(folder => folder.Children)
            .FirstOrDefault(table => table.Name == name);

        Assert.That(node, Is.Not.Null, $"the tree has a table called {name}");

        return node!;
    }

    #endregion
}
