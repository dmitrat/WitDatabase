using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// An LSM database is a FOLDER, and the recent list is allowed to know that.
/// </summary>
/// <remarks>
/// <para>
/// Reported from the screenshot pass: <c>D:\demo\events</c> was opened, worked in and disconnected,
/// and never appeared in <i>Recent Databases</i>, while a <c>.witdb</c> file opened the same way did.
/// </para>
/// <para>
/// <b>It was written to the list and hidden on the way out.</b> <c>AddRecentFileAsync</c> stores any
/// path; the two places that read it asked <c>File.Exists</c>, which is false for a directory. So the
/// entry was invisible in the welcome screen, and clicking a folder database that HAD been made
/// visible would have taken it out of the list as a file that is gone.
/// </para>
/// <para>
/// The third case is the control. "Show everything" also passes the first two and turns the recent
/// list into a list of paths that no longer exist, which is what the removal is for.
/// </para>
/// </remarks>
[TestFixture]
public class RecentFilesKnowAFolderDatabaseTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private MainWindowViewModel m_main = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync(StudioStorage.Lsm, withSchema: false);

        m_main = m_studio.App.MainWindowVm;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Tests

    [Test]
    public async Task AFolderDatabaseIsShownInTheRecentListTest()
    {
        Assume.That(Directory.Exists(m_studio.DatabasePath),
            "the fixture's LSM database is a folder");

        await m_studio.Settings.AddRecentFileAsync(m_studio.DatabasePath);

        await m_main.InitializeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_main.RecentFiles.Select(file => file.FilePath),
                Has.Exactly(1).EqualTo(m_studio.DatabasePath));

            Assert.That(m_main.HasRecentFiles, Is.True);
        });
    }

    /// <summary>
    /// The second half, and the one that loses the entry rather than hiding it.
    /// </summary>
    [Test]
    public async Task OpeningAFolderDatabaseFromTheRecentListKeepsItThereTest()
    {
        await m_studio.Settings.AddRecentFileAsync(m_studio.DatabasePath);

        // The fixture holds the folder open, and this is the route that opens it again.
        await m_studio.Connections.CloseAllAsync();

        await StudioFixture.PressAsync(m_main.OpenRecentCommand, m_studio.DatabasePath);

        var settings = await m_studio.Settings.LoadAsync();

        Assert.That(settings.RecentFiles, Has.Exactly(1).EqualTo(m_studio.DatabasePath),
            "opening it must not take it out of the list");
    }

    /// <summary>
    /// The control: a path that really is gone still leaves the list.
    /// </summary>
    [Test]
    public async Task APathThatIsGoneIsStillRemovedTest()
    {
        var missing = Path.Combine(m_studio.Root, "went-away.witdb");

        await m_studio.Settings.AddRecentFileAsync(missing);

        await StudioFixture.PressAsync(m_main.OpenRecentCommand, missing);

        var settings = await m_studio.Settings.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(settings.RecentFiles, Has.None.EqualTo(missing));
            Assert.That(m_main.RecentFiles.Select(file => file.FilePath), Has.None.EqualTo(missing));
        });
    }

    #endregion
}
