using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The command palette can be used and left with the mouse.
/// </summary>
/// <remarks>
/// <para>
/// Finding 18, found while automating the screenshots - where it stopped the run until a key could be
/// pressed by hand. Once open, the palette ignored the pointer for everything except moving the
/// highlight: clicking outside did not close it, clicking the header button again did not close it, a
/// single click on an entry moved the highlight and nothing else, and a double click did the same.
/// The only way out was <c>Esc</c>, and the only way in was <c>Enter</c>.
/// </para>
/// <para>
/// <b>A palette is a keyboard tool and nobody minds reaching for Esc</b> - what is wrong is a window
/// that traps the pointer with no visible way out. So the keyboard keeps everything it had, and the
/// mouse gets the two things it is entitled to: a click on an entry runs it, and a click outside
/// closes the window.
/// </para>
/// </remarks>
[TestFixture]
public class ThePaletteTakesTheMouseTests
{
    #region What the window wires

    [Test]
    public void AClickOutsideClosesItTest()
    {
        var markup = Markup("Views/MainWindow.axaml");
        var code = Markup("Views/MainWindow.axaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("PointerPressed=\"OnPaletteScrimPressed\""),
                "the scrim behind the palette answers the pointer");

            Assert.That(code, Does.Contain("OnPaletteScrimPressed"),
                "and the handler is there to answer with");

            Assert.That(code, Does.Contain("PaletteVm.CloseCommand"),
                "what it does is close the palette");
        });
    }

    [Test]
    public void AClickOnAnEntryRunsItTest()
    {
        var markup = Markup("Views/MainWindow.axaml");
        var code = Markup("Views/MainWindow.axaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("Tapped=\"OnPaletteItemTapped\""),
                "the list answers a click on one of its entries");

            Assert.That(code, Does.Contain("OnPaletteItemTapped"));

            Assert.That(code, Does.Contain("AcceptCommand"),
                "and a click runs the entry, which is what the palette is for");
        });
    }

    #endregion

    #region What the palette does

    [Test]
    public async Task ClosingItLeavesNothingBehindTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var palette = studio.App.PaletteVm;

        palette.OpenCommand.Execute(null);

        Assume.That(palette.IsOpen, Is.True);

        palette.CloseCommand.Execute(null);

        Assert.That(palette.IsOpen, Is.False,
            "the window a click outside closes is the same window Esc closes");
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
