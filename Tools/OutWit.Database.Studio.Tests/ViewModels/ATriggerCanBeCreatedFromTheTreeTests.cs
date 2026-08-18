using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The tree can create a trigger, and it belongs to the table that was selected.
/// </summary>
/// <remarks>
/// <para>
/// Finding 22 said that nothing in Studio creates a trigger, and it was reported after looking in the
/// one place a person looks: the tree's <c>Create ▸</c> submenu, which offered Table, View and Index.
/// <b>The dialog was there all along</b> - <c>EditTriggerDialog</c>, opened by a button in the
/// Structure tab's Triggers section - so the Schema article was rewritten around an editor for
/// nothing.
/// </para>
/// <para>
/// <b>A trigger has an owner</b>, which is what makes this more than a menu entry: <c>CREATE TRIGGER</c>
/// names a table, so the item is offered on a table and on the Triggers folder under one, and nowhere
/// else. The control case is the connection root, where there is no table to name.
/// </para>
/// </remarks>
[TestFixture]
public class ATriggerCanBeCreatedFromTheTreeTests
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

    #region What it is offered on

    [Test]
    public void ATableOffersTheTriggerItemTest()
    {
        m_studio.Explorer.SelectedNode = NodeOf(DatabaseNodeType.Table, "Orders");

        Assert.That(m_studio.Explorer.CanCreateTrigger, Is.True,
            "a trigger is created on the table it belongs to");
    }

    [Test]
    public void TheConnectionRootDoesNotTest()
    {
        m_studio.Explorer.SelectedNode = m_studio.Explorer.Nodes[0];

        Assume.That(m_studio.Explorer.Nodes[0].NodeType, Is.EqualTo(DatabaseNodeType.Database));

        Assert.That(m_studio.Explorer.CanCreateTrigger, Is.False,
            "CREATE TRIGGER names a table, and there is none here");
    }

    #endregion

    #region The menu

    [Test]
    public void TheCreateSubmenuOffersItTest()
    {
        var markup = Markup("Views/DatabaseExplorer.axaml");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("S.Explorer.Menu.CreateTrigger"),
                "the Create submenu names it");

            Assert.That(markup, Does.Contain("CreateTriggerCommand"),
                "and pressing it opens the dialog that already existed");

            Assert.That(markup, Does.Contain("CanCreateTrigger"),
                "greyed where there is no table to attach one to");
        });
    }

    #endregion

    #region Tools

    private DatabaseNode NodeOf(DatabaseNodeType type, string name)
    {
        var found = Walk(m_studio.Explorer.Nodes)
            .FirstOrDefault(node => node.NodeType == type && node.Name == name);

        Assert.That(found, Is.Not.Null, $"the tree has a {type} called {name}");

        return found!;
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
