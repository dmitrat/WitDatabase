using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Searching the editor can be reached with the mouse.
/// </summary>
/// <remarks>
/// <para>
/// Finding 38, and one of the five reported in chat: <c>Ctrl+F</c> opened the find band and nothing
/// else did. <c>Edit</c> held Copy, Paste and Settings; the toolbar had no search; the command palette
/// could not reach it. It is the one frame of the documentation set that could not be taken without a
/// key being pressed by hand.
/// </para>
/// <para>
/// <b>The gesture stays where it is.</b> The window handles <c>Ctrl+F</c> in code-behind on purpose -
/// the band's own box, the editor and the result grid take focus in turn, and a KeyBinding would
/// answer for only one of them. The menu item opens the same band through the same ViewModel, and the
/// window keeps the focusing to itself.
/// </para>
/// </remarks>
[TestFixture]
public class FindIsInTheMenuTests
{
    #region The command

    [Test]
    public async Task FindOpensTheBandOfTheTabInFrontTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var tab = studio.FirstQueryTab;

        Assume.That(tab.Search.IsOpen, Is.False, "the band starts closed");

        StudioFixture.PressAsync(studio.Workspace.FindCommand).Wait();

        Assert.That(tab.Search.IsOpen, Is.True, "the menu opens the band of the tab in front");
    }

    #endregion

    #region The menu

    [Test]
    public void TheEditMenuOffersFindTest()
    {
        var markup = Markup("Views/MainWindow.axaml");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("S.Menu.Find"),
                "the Edit menu names the find band");

            Assert.That(markup, Does.Contain("WorkspaceTabsVm.FindCommand"),
                "and pressing it opens the band rather than announcing a gesture");
        });
    }

    #endregion

    #region Tools

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
