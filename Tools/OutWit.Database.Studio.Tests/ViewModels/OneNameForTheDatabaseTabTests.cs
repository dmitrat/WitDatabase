using System.Text.Json;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// One thing, one name, and a way in that is not a right-click.
/// </summary>
/// <remarks>
/// <para>
/// Finding 40: the context-menu entry said <b>Storage…</b>, the tab it opened was called
/// <b>sales - Database</b>, and the heading inside that tab said <b>Storage</b> again. The
/// documentation calls it the Database tab, after the tab.
/// </para>
/// <para>
/// And one of the five reported in chat: <i>there is no dialog with information about the database -
/// size, location, statistics</i>. There is, and it answers all of that and more; it was called
/// something else and reachable only by right-clicking the connection's own node in the tree, so from
/// outside it did not exist. <b>A capability nobody can name is a capability nobody has.</b>
/// </para>
/// <para>
/// The name chosen is <b>Database</b>, after the tab and after the documentation. This fixture holds
/// all three places to it, in both languages, and the second case is the way in.
/// </para>
/// </remarks>
[TestFixture]
public class OneNameForTheDatabaseTabTests
{
    #region The name

    [TestCase("en", "Database")]
    [TestCase("ru", "База данных")]
    public void TheMenuTheTabAndTheHeadingAgreeTest(string language, string name)
    {
        using var catalogue = JsonDocument.Parse(Markup($"Resources/Strings.{language}.json"));

        var menu = Value(catalogue, "Explorer.Menu.Database");
        var heading = Value(catalogue, "Database.Title");
        var tab = Value(catalogue, "Tab.DatabaseOf");

        // Case is not the question: a Russian caption capitalises its first word and a tab title
        // carries the same name in the middle of a phrase. The question is whether it IS the same name.
        Assert.Multiple(() =>
        {
            Assert.That(menu, Does.Contain(name).IgnoreCase, "the menu entry that opens it");
            Assert.That(heading, Does.Contain(name).IgnoreCase, "the heading inside it");
            Assert.That(tab, Does.Contain(name).IgnoreCase, "and the tab it opens");
        });
    }

    #endregion

    #region The way in

    [Test]
    public async Task TheMenuOpensTheDatabaseTabOfTheOpenConnectionTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await StudioFixture.PressAsync(studio.App.MainWindowVm.DatabaseTabCommand);

        Assert.That(studio.Workspace.Tabs.Any(tab => tab.TabType == WorkspaceTabType.Database),
            Is.True,
            "the Database tab is reachable without right-clicking the tree");
    }

    [Test]
    public void TheMenuBarOffersItTest()
    {
        var markup = Markup("Views/MainWindow.axaml");

        Assert.That(markup, Does.Contain("MainWindowVm.DatabaseTabCommand"),
            "the menu bar can open it, not only the tree's context menu");
    }

    #endregion

    #region Tools

    private static string Value(JsonDocument catalogue, string key)
    {
        return catalogue.RootElement.GetProperty(key).GetString() ?? string.Empty;
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
