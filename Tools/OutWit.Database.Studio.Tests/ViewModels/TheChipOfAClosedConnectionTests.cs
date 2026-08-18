using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The connection chip on the toolbar says whether the connection is still there.
/// </summary>
/// <remarks>
/// <para>
/// Finding 14: with two connections open and a query tab belonging to the first, closing that first
/// connection left the chip reading <b>Sales</b> while the status bar had already moved on. The name
/// is deliberately KEPT - a query tab outlives its connection, and its text has to be able to say
/// where it came from - so the fix is not to clear it but to stop it reading as live.
/// </para>
/// <para>
/// <b>The words belong to the view, not to the tab.</b> A caption composed in the ViewModel at the
/// moment the connection closes would be frozen in the language of that moment, which is exactly what
/// the theme button did until 2026-08-15. The tab answers a bool; the window draws the marker from
/// the catalogue.
/// </para>
/// </remarks>
[TestFixture]
public class TheChipOfAClosedConnectionTests
{
    #region Tests

    [Test]
    public async Task ATabWhoseConnectionIsGoneSaysSoAndKeepsItsNameTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var session = studio.Database;
        var tab = studio.Workspace.OpenQueryTab("SELECT 1", session: session);

        Assert.Multiple(() =>
        {
            Assert.That(tab.ConnectionName, Is.EqualTo(session.DisplayName));
            Assert.That(tab.IsConnectionOpen, Is.True, "the connection is open to begin with");
        });

        await studio.Connections.CloseAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(tab.IsConnectionOpen, Is.False,
                "the chip must not read as live once the session is gone");

            Assert.That(tab.ConnectionName, Is.EqualTo(session.DisplayName),
                "and the name is kept, because the tab still has to say where its text came from");
        });
    }

    /// <summary>
    /// The marker reaches the window, and it is read from the catalogue rather than written in.
    /// </summary>
    [Test]
    public void TheWindowDrawsTheMarkerFromTheCatalogueTest()
    {
        var markup = Markup("Views/MainWindow.axaml");

        var chip = markup[markup.IndexOf("ToolbarConnectionChip", StringComparison.Ordinal)..];
        chip = chip[..chip.IndexOf("</Border>", StringComparison.Ordinal)];

        Assert.Multiple(() =>
        {
            Assert.That(chip, Does.Contain("S.Tab.ConnectionClosed"),
                "the marker is a catalogue key, so it follows the language");

            Assert.That(chip, Does.Contain("IsConnectionOpen"),
                "and it is shown only when the connection is gone");
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
