using System.Collections.ObjectModel;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The two halves of the tree's binding, both of which were broken and neither of which a test could
/// see.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured by driving Studio on 2026-08-19</b>, with 1003 tests green. Phase 5 gave a table a
/// placeholder child so that it would draw an expander - and opening one showed an empty row and
/// nothing else. Two independent links were missing, one in each direction:
/// </para>
/// <list type="number">
/// <item><b>view → model.</b> The row's <c>IsExpanded</c> is bound to the node's in a STYLE setter,
/// and a binding in a style setter does not push back. The row opened; the model never heard; the
/// columns were never read.</item>
/// <item><b>model → view.</b> <c>Children</c> was a <c>List</c>, so replacing the placeholder with
/// the columns notified nobody and the row went on showing the placeholder.</item>
/// </list>
/// <para>
/// <b>Every test read <c>Children</c> directly and set <c>IsExpanded</c> itself</b> - which is the
/// ViewModel's side of a binding that only ever worked one way. This fixture holds both mechanisms,
/// because neither can be seen from where the tests stand.
/// </para>
/// </remarks>
[TestFixture]
public class TheTreeAndTheWindowAgreeTests
{
    #region model to view

    [Test]
    public void TheChildrenOfANodeNotifyWhenTheyChangeTest()
    {
        var node = new DatabaseNode { Name = "Orders", NodeType = DatabaseNodeType.Table };

        Assert.That(node.Children, Is.InstanceOf<ObservableCollection<DatabaseNode>>(),
            "a plain List binds once and is never heard from again - the columns were read and the "
            + "row went on showing the placeholder");
    }

    [Test]
    public async Task ReplacingThePlaceholderRaisesTheCollectionChangeTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync();

        var table = studio.Explorer.Nodes
            .SelectMany(root => root.Children)
            .Where(folder => folder.NodeType == DatabaseNodeType.TablesFolder)
            .SelectMany(folder => folder.Children)
            .First(node => node.Name == "Orders");

        var changes = 0;

        ((ObservableCollection<DatabaseNode>)table.Children).CollectionChanged += (_, _) => changes++;

        await studio.Explorer.ExpandNodeAsync(table);

        Assert.That(changes, Is.GreaterThan(0),
            "the window is told that the placeholder went and the columns arrived");
    }

    #endregion

    #region view to model

    /// <summary>
    /// The window tells the node when its row is opened, because the binding does not.
    /// </summary>
    [Test]
    public void TheWindowTellsTheNodeItWasOpenedTest()
    {
        var code = Source("Views/DatabaseExplorer.axaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("TreeViewItem.IsExpandedProperty.Changed"),
                "the view watches the row's own expansion");

            Assert.That(code, Does.Contain("node.IsExpanded = true"),
                "and writes it into the node, which is what starts the read");
        });
    }

    #endregion

    #region Tools

    private static string Source(string relative)
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
