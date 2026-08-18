using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A node is offered what applies to it, and not what does not.
/// </summary>
/// <remarks>
/// <para>
/// Finding 39: right-clicking the <c>Tables</c> folder produced the table menu - <i>Database…</i>,
/// <i>Create ▸</i>, <i>Select Data</i>, <i>Edit Data…</i>, <i>View Structure…</i>,
/// <i>View Definition</i>, <i>Refresh</i>, <i>Rename</i>, <i>Empty the table…</i>, <i>Drop…</i> - with
/// everything inapplicable greyed rather than absent. A folder offering <i>Empty the table…</i> and
/// <i>Drop…</i>, even greyed, reads as though the folder could be emptied or dropped.
/// </para>
/// <para>
/// <b>Greyed and absent are two different statements</b>, and the application needs both: an item that
/// does not apply to this KIND of node is absent, and an item that applies but cannot run right now -
/// a connection that has gone away - is greyed. So the two questions are two properties.
/// <c>Shows…</c> is about the node type alone; <c>Can…</c> keeps the connection in it.
/// </para>
/// <para>
/// The connection root gains <b>Close connection</b> with the same change, which is the second of the
/// five reported in chat: a database could be closed from <c>File</c> and from nowhere near the tree
/// it is drawn in.
/// </para>
/// </remarks>
[TestFixture]
public class EachNodeGetsItsOwnMenuTests
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

    #region What each kind is offered

    [Test]
    public void AFolderIsOfferedNothingThatBelongsToATableTest()
    {
        Select(DatabaseNodeType.TablesFolder);

        var explorer = m_studio.Explorer;

        Assert.Multiple(() =>
        {
            Assert.That(explorer.ShowsCreate, Is.True, "a folder is where objects are created");

            Assert.That(explorer.ShowsEditData, Is.False, "a folder holds no rows");
            Assert.That(explorer.ShowsTruncate, Is.False, "and cannot be emptied");
            Assert.That(explorer.ShowsDrop, Is.False, "and cannot be dropped");
            Assert.That(explorer.ShowsRename, Is.False);
            Assert.That(explorer.ShowsDatabaseActions, Is.False);
        });
    }

    [Test]
    public void ATableIsOfferedWhatATableHasTest()
    {
        Select(DatabaseNodeType.Table);

        var explorer = m_studio.Explorer;

        Assert.Multiple(() =>
        {
            Assert.That(explorer.ShowsBrowseData, Is.True);
            Assert.That(explorer.ShowsEditData, Is.True);
            Assert.That(explorer.ShowsTruncate, Is.True);
            Assert.That(explorer.ShowsDrop, Is.True);
            Assert.That(explorer.ShowsRename, Is.True);

            Assert.That(explorer.ShowsDatabaseActions, Is.False,
                "a table is not the connection");
        });
    }

    [Test]
    public void TheConnectionRootIsOfferedTheConnectionsOwnActionsTest()
    {
        Select(DatabaseNodeType.Database);

        var explorer = m_studio.Explorer;

        Assert.Multiple(() =>
        {
            Assert.That(explorer.ShowsDatabaseActions, Is.True,
                "the Database tab and closing the connection belong here");

            Assert.That(explorer.ShowsCreate, Is.True);

            Assert.That(explorer.ShowsEditData, Is.False);
            Assert.That(explorer.ShowsDrop, Is.False, "a connection is not an object to drop");
        });
    }

    /// <summary>
    /// Measured while writing this fixture: closing the connection does not grey the table's items,
    /// it takes the whole branch away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case was written the other way round - that a table keeps its items and greys them once the
    /// connection is gone - and it went red. The explorer subscribes to <c>SessionClosed</c> and
    /// removes the root, so there is no node left to offer anything, and the selection is cleared with
    /// it.
    /// </para>
    /// <para>
    /// <b>That does not make the <c>Can…</c> half redundant.</b> It is what the ITEM is enabled by,
    /// and it answers between the moment a connection drops and the moment the tree hears about it -
    /// the window that produced WS-13. The distinction stands; this is the measurement of where it
    /// does not show.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ClosingTheConnectionTakesTheBranchRatherThanGreyingItTest()
    {
        Select(DatabaseNodeType.Table);

        Assume.That(m_studio.Explorer.CanEditData, Is.True, "it can be edited while connected");

        await m_studio.Connections.CloseAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Explorer.Nodes, Is.Empty,
                "the branch goes with the connection");

            Assert.That(m_studio.Explorer.SelectedNode, Is.Null,
                "and nothing is selected, so nothing is offered");

            Assert.That(m_studio.Explorer.CanEditData, Is.False);
        });
    }

    #endregion

    #region The menu

    [Test]
    public void TheMenuHidesRatherThanGreysWhatDoesNotApplyTest()
    {
        var markup = Markup("Views/DatabaseExplorer.axaml");

        Assert.Multiple(() =>
        {
            foreach (var property in new[]
                     {
                         "ShowsDatabaseActions", "ShowsCreate", "ShowsBrowseData", "ShowsEditData",
                         "ShowsViewStructure", "ShowsViewDefinition", "ShowsRename", "ShowsTruncate",
                         "ShowsDrop"
                     })
            {
                Assert.That(markup, Does.Contain(property),
                    $"{property} decides whether its item is drawn at all");
            }

            Assert.That(markup, Does.Contain("CloseDatabaseCommand"),
                "and the connection can be closed from the tree it is drawn in");
        });
    }

    #endregion

    #region Tools

    private void Select(DatabaseNodeType type)
    {
        var node = Walk(m_studio.Explorer.Nodes).FirstOrDefault(candidate => candidate.NodeType == type);

        Assert.That(node, Is.Not.Null, $"the tree has a {type} node");

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

    private static string Markup(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
            {
                var path = Path.Combine(candidate, relative.Replace('/', Path.DirectorySeparatorChar));

                Assert.That(File.Exists(path), Is.True, $"{relative} must be where this fixture says");

                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new AssertionException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
