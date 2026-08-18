using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Opening a database leaves the same three things behind, whichever way it was opened.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tree, the recent list and the saved connections were filled by whoever opened the
/// session</b>, in three different places with three different sets of rules, and
/// <c>ConnectionsViewModel</c> did none of it. So connecting from <i>File ▸ Connections…</i> left the
/// Database Explorer empty until Refresh was pressed (finding 10) and never touched <i>Recent
/// Databases</i> (finding 12), while the same database opened through <i>Open Database…</i> did both.
/// </para>
/// <para>
/// <b>This has been the same defect three times.</b> The explorer subscribes to the CLOSING half of
/// the session events and deliberately not to the opening one - an <c>async void</c> handler has
/// nowhere to put a failure and would refresh a branch the opener has already built - so the rule
/// cannot be "the explorer listens". It is "there is one way to open a database", and the second case
/// here is what keeps it that way: a route that calls the manager directly is the shape that produced
/// every one of these findings, including the copy dialog's in August, which had its own comment
/// about it.
/// </para>
/// </remarks>
[TestFixture]
public class EveryRouteThatOpensADatabaseTests
{
    #region Routes

    public enum Route
    {
        /// <summary>File ▸ Open Database…</summary>
        OpenDialog,

        /// <summary>File ▸ Connections…, pick a row, Connect.</summary>
        ConnectionsWindow,

        /// <summary>The welcome screen's Recent Databases.</summary>
        RecentList
    }

    #endregion

    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync(connect: false);
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region The rule

    [TestCase(Route.OpenDialog)]
    [TestCase(Route.ConnectionsWindow)]
    [TestCase(Route.RecentList)]
    public async Task EveryRouteFillsTheTreeTheRecentListAndTheSavedConnectionsTest(Route route)
    {
        var path = Path.Combine(m_studio.Root, "route.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await OpenThroughAsync(route, path);

        var session = m_studio.Connections.Sessions.FirstOrDefault();

        Assert.That(session, Is.Not.Null, "the database has to be open before anything else is asked");

        var settings = await m_studio.Settings.LoadAsync();
        var profiles = await m_studio.Profiles.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Explorer.Nodes.Select(node => node.ConnectionId),
                Has.Exactly(1).EqualTo(session!.Id),
                "the tree has this connection's branch, without Refresh being pressed");

            Assert.That(settings.RecentFiles, Has.Exactly(1).EqualTo(path),
                "the database is in Recent Databases");

            Assert.That(profiles.Select(profile => profile.Path), Has.Exactly(1).EqualTo(path),
                "and it is offered again in the Connections window");
        });
    }

    /// <summary>
    /// The other half of the same rule: one branch, not two. This is the objection that kept the
    /// explorer from listening to the opening event, and it holds whatever fills the tree.
    /// </summary>
    [Test]
    public async Task TheTreeGetsOneRootPerConnectionTest()
    {
        var first = Path.Combine(m_studio.Root, "first.witdb");
        var second = Path.Combine(m_studio.Root, "second.witdb");

        StudioFixture.CreateDatabaseOnDisk(first);
        StudioFixture.CreateDatabaseOnDisk(second);

        await OpenThroughAsync(Route.OpenDialog, first);
        await OpenThroughAsync(Route.OpenDialog, second);

        Assert.That(m_studio.Explorer.Nodes, Has.Count.EqualTo(2),
            "two databases, two roots - a branch built twice would be two roots for one connection");
    }

    #endregion

    #region The name that was chosen

    /// <summary>
    /// Opening a saved connection again, with the dialog's name box empty, keeps the name it was
    /// given.
    /// </summary>
    /// <remarks>
    /// Finding 11: with <b>Sales</b> saved for <c>sales.witdb</c>, opening that path through
    /// <i>Open Database…</i> without typing a name replaced it with <b>sales</b>, from the file name.
    /// The saved name and colour are what a person chose; a name derived from the path is what the
    /// application falls back to when they did not, and it must not overwrite the other.
    /// </remarks>
    [Test]
    public async Task OpeningASavedDatabaseWithNoNameKeepsTheNameItWasGivenTest()
    {
        var path = Path.Combine(m_studio.Root, "sales.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await m_studio.Profiles.SaveAsync(new ConnectionProfile
        {
            Name = "Sales",
            Path = path,
            ColorIndex = 3
        });

        await OpenThroughAsync(Route.OpenDialog, path);

        var profiles = await m_studio.Profiles.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(profiles, Has.Count.EqualTo(1), "one database, one saved connection");

            Assert.That(profiles[0].Name, Is.EqualTo("Sales"),
                "the name the user gave it survives an open that named nothing");

            Assert.That(profiles[0].ColorIndex, Is.EqualTo(3),
                "and so does the colour, for the same reason");
        });
    }

    /// <summary>
    /// The control: a name typed into the dialog is a choice, and it wins.
    /// </summary>
    [Test]
    public async Task ANameTypedIntoTheDialogReplacesTheSavedOneTest()
    {
        var path = Path.Combine(m_studio.Root, "sales.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await m_studio.Profiles.SaveAsync(new ConnectionProfile { Name = "Sales", Path = path });

        m_studio.Connection.ResetForNewDialog();
        m_studio.Connection.ConnectionInfo.FilePath = path;
        m_studio.Connection.ConnectionInfo.DisplayName = "Sales, archived";

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);

        var profiles = await m_studio.Profiles.LoadAsync();

        Assert.That(profiles[0].Name, Is.EqualTo("Sales, archived"));
    }

    #endregion

    #region The shape

    /// <summary>
    /// There is one way to open a database, and the ViewModels take it.
    /// </summary>
    /// <remarks>
    /// A route that calls <c>Connections.OpenAsync</c> itself gets a session and none of what the
    /// application keeps beside it. That is finding 10, finding 12, and the copy dialog's empty tree
    /// in August - three instances of one shape, which is why this is a rule rather than three fixes.
    /// </remarks>
    [Test]
    public void OnlyTheApplicationViewModelOpensASessionTest()
    {
        var root = FindStudioProject();

        Assert.That(root, Is.Not.Null,
            "the Studio project was not found from " + AppContext.BaseDirectory);

        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root!, "ViewModels"), "*.cs",
                     SearchOption.AllDirectories))
        {
            scanned++;

            if (Path.GetFileName(file).Equals("ApplicationViewModel.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var line in File.ReadAllLines(file))
            {
                if (line.Contains("Connections.OpenAsync(", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(root!, file)}: {line.Trim()}");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that found no ViewModels would report no offenders either.
            Assert.That(scanned, Is.GreaterThan(15),
                "CONTROL: too few ViewModels scanned - the walk is looking in the wrong place");

            Assert.That(offenders, Is.Empty,
                "these open a session without the tree, the recent list or the saved connection that "
                + "goes with it:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        });
    }

    #endregion

    #region Tools

    private async Task OpenThroughAsync(Route route, string path)
    {
        switch (route)
        {
            case Route.OpenDialog:
                m_studio.Connection.ResetForNewDialog();
                m_studio.Connection.ConnectionInfo.FilePath = path;

                await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);
                break;

            case Route.ConnectionsWindow:
                // A saved connection is what this window offers; the row is what a person picks.
                await m_studio.Profiles.SaveAsync(new ConnectionProfile
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Path = path
                });

                using (var window = new ConnectionsViewModel(m_studio.App))
                {
                    await window.RefreshAsync();

                    window.Selected = window.Rows.FirstOrDefault(row => row.Path == path);

                    Assert.That(window.Selected, Is.Not.Null, "the saved connection is in the window");

                    await StudioFixture.PressAsync(window.ConnectCommand);
                }

                break;

            case Route.RecentList:
                await m_studio.Settings.AddRecentFileAsync(path);

                await StudioFixture.PressAsync(m_studio.App.MainWindowVm.OpenRecentCommand, path);
                break;
        }
    }

    private static string? FindStudioProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    #endregion
}
