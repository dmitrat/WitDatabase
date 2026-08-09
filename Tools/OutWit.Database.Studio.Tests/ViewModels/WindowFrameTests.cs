using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Stage 4, the window frame: the command palette (WS-9), the contextual toolbar (WS-8), what the
/// status bar says (1.5), the keyboard map (1.7) and notifications instead of modal windows (WS-7).
///
/// All of it over the real ViewModel graph and a real database, because most of it is about what the
/// frame says about the connection and the tab - which is exactly what a double cannot answer.
/// </summary>
[TestFixture]
public class WindowFrameTests
{
    #region The command palette

    /// <summary>
    /// The palette is the answer to "where is that table" with three connections open and a folder
    /// per object type. It holds both commands and objects, and an object says which database it is
    /// in - without that, three tables called Customers are one line repeated three times.
    /// </summary>
    [Test]
    public async Task ThePaletteFindsAnObjectByNameAndSaysWhichConnectionItIsInTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var second = await studio.OpenAnotherAsync("second");

        await studio.Explorer.RefreshAsync();

        var palette = studio.App.PaletteVm;
        palette.Open();
        palette.Query = "Customers";

        TestContext.Out.WriteLine(string.Join("\n", palette.Items.Select(item => item.ToString())));

        Assert.Multiple(() =>
        {
            Assert.That(palette.IsOpen, Is.True);
            Assert.That(palette.Items, Has.Count.EqualTo(2),
                "one Customers per open connection");

            Assert.That(palette.Items.Select(item => item.Subtitle),
                Is.EquivalentTo(new[]
                {
                    $"table in {studio.Database.DisplayName}",
                    $"table in {second.DisplayName}"
                }),
                "and each says where it is");
        });
    }

    /// <summary>
    /// Choosing an object goes to it: the tree selects it, which also makes its connection the active
    /// one (WS-3). This is the "go to" half of the palette.
    /// </summary>
    [Test]
    public async Task ChoosingAnObjectSelectsItInTheTreeTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var second = await studio.OpenAnotherAsync("second");
        await studio.Explorer.RefreshAsync();

        // Look at the FIRST connection, then go to an object of the second one.
        studio.Connections.Active = studio.Database;

        var palette = studio.App.PaletteVm;
        palette.Open();
        palette.Query = "Orders";

        var target = palette.Items.First(item =>
            item.Subtitle != null && item.Subtitle.EndsWith(second.DisplayName, StringComparison.Ordinal));

        palette.SelectedItem = target;

        await palette.AcceptAsync();

        Assert.Multiple(() =>
        {
            Assert.That(palette.IsOpen, Is.False, "the palette closes when something is chosen");
            Assert.That(studio.Explorer.SelectedNode?.Name, Is.EqualTo("Orders"));
            Assert.That(studio.Connections.Active, Is.SameAs(second),
                "going to an object of another connection makes that connection the active one");
        });
    }

    /// <summary>
    /// The ranking is dull on purpose, and this is what dull means: an exact name beats a prefix,
    /// which beats a word in the middle.
    /// </summary>
    [Test]
    public async Task ThePaletteRanksExactThenPrefixThenAnywhereTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        await studio.Database.ExecuteNonQueryAsync("CREATE TABLE Ord (Id INTEGER PRIMARY KEY)");
        await studio.Database.ExecuteNonQueryAsync("CREATE TABLE Orders (Id INTEGER PRIMARY KEY)");
        await studio.Database.ExecuteNonQueryAsync("CREATE TABLE BackOrd (Id INTEGER PRIMARY KEY)");

        await studio.Explorer.RefreshAsync();

        var palette = studio.App.PaletteVm;
        palette.Open();
        palette.Query = "Ord";

        Assert.That(palette.Items.Select(item => item.Title).Take(3),
            Is.EqualTo(new[] { "Ord", "Orders", "BackOrd" }).AsCollection);
    }

    /// <summary>
    /// CONTROL: an empty prompt is not an empty list. A palette that shows nothing until something is
    /// typed looks broken on the first Ctrl+K of a session, and there is nothing to press.
    /// </summary>
    [Test]
    public async Task ControlAnEmptyPaletteStillOffersSomethingTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var palette = studio.App.PaletteVm;
        palette.Open();

        Assert.Multiple(() =>
        {
            Assert.That(palette.Query, Is.Empty);
            Assert.That(palette.Items, Is.Not.Empty, "commands are offered before anything is typed");
            Assert.That(palette.SelectedItem, Is.Not.Null, "and one of them is selected to press");
        });
    }

    [Test]
    public async Task MovingThroughThePaletteWrapsAtBothEndsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var palette = studio.App.PaletteVm;
        palette.Open();

        var first = palette.SelectedItem;

        palette.MoveUpCommand.Execute(null);
        var afterUp = palette.SelectedItem;

        palette.MoveDownCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(afterUp, Is.SameAs(palette.Items[^1]), "up from the first goes to the last");
            Assert.That(palette.SelectedItem, Is.SameAs(first), "and down from the last comes back");
        });
    }

    #endregion

    #region The contextual toolbar (WS-8)

    /// <summary>
    /// The toolbar belongs to the active tab and changes with it. There is no global panel of
    /// twenty icons, half of them grey.
    ///
    /// <para>
    /// <b>All FOUR kinds, since 2026-08-09.</b> The Database tab was not in this case and had no band
    /// of its own, so selecting it hid the query toolbar and put nothing in its place - an empty strip
    /// across the window, seen in the running application and carried forward from phase 10. Every kind
    /// of tab lights exactly one band now, which is a rule rather than three flags, so the next kind
    /// added has somewhere to fail.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheToolbarFollowsTheTypeOfTheActiveTabTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var query = studio.FirstQueryTab;
        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Customers");
        var structure = await studio.Workspace.OpenStructureTabAsync(
            studio.Database, "Orders", DatabaseNodeType.Table);
        var database = await studio.Workspace.OpenDatabaseTabAsync(studio.Database);

        studio.Workspace.SelectedTab = query;
        var onQuery = Bands(studio);

        studio.Workspace.SelectedTab = editor;
        var onEditor = Bands(studio);

        studio.Workspace.SelectedTab = structure;
        var onStructure = Bands(studio);

        studio.Workspace.SelectedTab = database;
        var onDatabase = Bands(studio);

        Assert.Multiple(() =>
        {
            Assert.That(onQuery, Is.EqualTo((true, false, false, false)));
            Assert.That(onEditor, Is.EqualTo((false, true, false, false)));
            Assert.That(onStructure, Is.EqualTo((false, false, true, false)));
            Assert.That(onDatabase, Is.EqualTo((false, false, false, true)));
        });
    }

    private static (bool Query, bool Editor, bool Structure, bool Database) Bands(StudioFixture studio) =>
        (studio.Workspace.IsQueryTabSelected, studio.Workspace.IsTableEditTabSelected,
            studio.Workspace.IsStructureTabSelected, studio.Workspace.IsDatabaseTabSelected);

    /// <summary>
    /// The chip on the right of the toolbar is the connection of the ACTIVE TAB - the one thing in
    /// the window that answers "where is this DELETE going" (WS-3). It follows the tab, not the tree.
    /// </summary>
    [Test]
    public async Task TheConnectionChipFollowsTheTabAndNotTheTreeTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var second = await studio.OpenAnotherAsync("second");
        await studio.Explorer.RefreshAsync();

        studio.Connections.Active = studio.Database;
        var tab = studio.Workspace.OpenQueryTab(string.Empty, "writer");

        // The user clicks something belonging to the other connection.
        var otherRoot = studio.Explorer.Nodes.First(node => node.ConnectionId == second.Id);
        studio.Explorer.SelectedNode = otherRoot;

        Assert.Multiple(() =>
        {
            Assert.That(studio.Connections.Active, Is.SameAs(second), "the focus moved");
            Assert.That(studio.Workspace.SelectedTab, Is.SameAs(tab));
            Assert.That(tab.ConnectionName, Is.EqualTo(studio.Database.DisplayName),
                "and the chip still names the tab's own connection");
        });
    }

    #endregion

    #region The status bar (1.5)

    [Test]
    public async Task TheStatusBarNamesTheConnectionAndWhatItIsMadeOfTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(studio.MainWindow.ConnectionSummary, Does.Contain(studio.Database.DisplayName));
            Assert.That(studio.MainWindow.ConnectionSummary, Does.Contain(studio.DatabasePath));
            Assert.That(studio.MainWindow.EngineSummary, Is.EqualTo("B-Tree"),
                "what the connection knows, and nothing it does not");
        });
    }

    /// <summary>
    /// Closing the last connection empties the status bar rather than leaving the name of a database
    /// that is no longer open.
    /// </summary>
    [Test]
    public async Task TheStatusBarEmptiesWhenTheLastConnectionClosesTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Connections.CloseAsync(studio.Connections.Sessions[0]);

        Assert.Multiple(() =>
        {
            Assert.That(studio.MainWindow.IsConnected, Is.False);
            Assert.That(studio.MainWindow.ConnectionSummary, Is.Empty);
            Assert.That(studio.MainWindow.EngineSummary, Is.Empty);
        });
    }

    [Test]
    public async Task TheStatusBarShowsWhereTheCursorIsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var tab = studio.FirstQueryTab;
        studio.Workspace.SelectedTab = tab;

        tab.CaretLine = 6;
        tab.CaretColumn = 42;

        Assert.That(studio.MainWindow.CaretSummary, Is.EqualTo("Ln 6, Col 42"));
    }

    #endregion

    #region The keyboard (1.7, WS-25)

    /// <summary>
    /// F5 runs the statement the cursor is in, and only that one (WS-25). A cursor left in the middle
    /// of a script is not a request to run the whole thing.
    /// </summary>
    [Test]
    public async Task F5RunsTheStatementUnderTheCursorAndNothingElseTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        await studio.Database.ExecuteNonQueryAsync("CREATE TABLE Steps (Id INTEGER PRIMARY KEY)");

        var tab = studio.FirstQueryTab;
        studio.Workspace.SelectedTab = tab;

        tab.SqlText = string.Join("\n",
        [
            "INSERT INTO Steps (Id) VALUES (1);",
            "INSERT INTO Steps (Id) VALUES (2);",
            "INSERT INTO Steps (Id) VALUES (3);"
        ]);

        // The cursor is somewhere inside the second statement.
        tab.CaretOffset = tab.SqlText.IndexOf("VALUES (2)", StringComparison.Ordinal) + 3;

        await StudioFixture.PressAsync(studio.Workspace.ExecuteCurrentStatementCommand);

        var stored = await studio.CountRowsAsync("Steps");

        Assert.Multiple(() =>
        {
            Assert.That(tab.ErrorMessage, Is.Null.Or.Empty, $"the statement failed: {tab.ErrorMessage}");
            Assert.That(stored, Is.EqualTo(1), "one statement ran, not three");
            Assert.That(tab.CurrentStatementText, Does.Contain("VALUES (2)"),
                "and it was the one the cursor was in");
        });
    }

    /// <summary>
    /// CONTROL for the case above: the same script through the whole-script command writes all three.
    /// Without it, "one row" would pass for a tab that fails to run anything at all.
    /// </summary>
    [Test]
    public async Task ControlTheWholeScriptCommandStillRunsEverythingTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        await studio.Database.ExecuteNonQueryAsync("CREATE TABLE Steps (Id INTEGER PRIMARY KEY)");

        var tab = studio.FirstQueryTab;
        studio.Workspace.SelectedTab = tab;

        tab.SqlText = string.Join("\n",
        [
            "INSERT INTO Steps (Id) VALUES (1);",
            "INSERT INTO Steps (Id) VALUES (2);",
            "INSERT INTO Steps (Id) VALUES (3);"
        ]);

        tab.CaretOffset = 0;

        await StudioFixture.PressAsync(studio.Workspace.ExecuteQueryCommand);

        Assert.That(await studio.CountRowsAsync("Steps"), Is.EqualTo(3));
    }

    /// <summary>
    /// An error inside the statement the cursor is in is still reported on the line of the TAB - the
    /// statement was sent on its own, so the engine counted from its first line.
    /// </summary>
    [Test]
    public async Task AnErrorInTheStatementUnderTheCursorIsReportedOnItsOwnLineTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        var tab = studio.FirstQueryTab;
        studio.Workspace.SelectedTab = tab;

        tab.SqlText = string.Join("\n",
        [
            "SELECT 1;",
            "SELECT 2;",
            "SELECT 3 FROM;"
        ]);

        tab.CaretOffset = tab.SqlText.IndexOf("SELECT 3", StringComparison.Ordinal) + 2;

        await StudioFixture.PressAsync(studio.Workspace.ExecuteCurrentStatementCommand);

        Assert.Multiple(() =>
        {
            Assert.That(tab.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(tab.ErrorLine, Is.EqualTo(3), "the third line of the tab, not the first");
        });
    }

    /// <summary>
    /// Ctrl+Shift+T brings back the tab that was just closed, with its text and in its own connection.
    /// The text of a query is usually the only copy of it.
    /// </summary>
    [Test]
    public async Task ClosingATabAndReopeningItKeepsItsTextAndItsConnectionTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var tab = studio.Workspace.OpenQueryTab("SELECT * FROM Customers", "worth keeping");

        await StudioFixture.PressAsync(studio.Workspace.CloseTabCommand, tab);

        Assert.That(studio.Workspace.Tabs, Does.Not.Contain(tab), "it really was closed");

        studio.Workspace.ReopenClosedTabCommand.Execute(null);

        var reopened = studio.Workspace.Tabs.OfType<QueryTabViewModel>()
            .FirstOrDefault(candidate => candidate.Title == "worth keeping");

        Assert.Multiple(() =>
        {
            Assert.That(reopened, Is.Not.Null);
            Assert.That(reopened!.SqlText, Is.EqualTo("SELECT * FROM Customers"));
            Assert.That(reopened.Session, Is.SameAs(studio.Database),
                "and in the connection it was opened in, not whichever is active now");
        });
    }

    #endregion

    #region Notifications (WS-7)

    [Test]
    public void ANotificationIsKeptUnreadUntilTheListIsOpenedTest()
    {
        var service = new NotificationService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationService>.Instance);

        service.Information("Import finished", "312 rows into Orders", "sales");
        service.Error("Background refresh failed", "the connection was closed", "sales");

        var beforeRead = service.UnreadCount;

        service.MarkAllRead();

        Assert.Multiple(() =>
        {
            Assert.That(service.Notifications, Has.Count.EqualTo(2));
            Assert.That(service.Notifications[0].Title, Is.EqualTo("Background refresh failed"),
                "newest first");
            Assert.That(beforeRead, Is.EqualTo(2));
            Assert.That(service.UnreadCount, Is.Zero);
        });
    }

    /// <summary>
    /// The list is bounded, because a session that imports a hundred files should not carry a hundred
    /// entries. The log is not bounded, which is what makes trimming safe - stated here so that the
    /// two facts stay together.
    /// </summary>
    [Test]
    public void TheNotificationListIsBoundedTest()
    {
        var service = new NotificationService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationService>.Instance);

        for (var i = 0; i < NotificationService.CAPACITY + 10; i++)
            service.Information($"event {i}");

        Assert.Multiple(() =>
        {
            Assert.That(service.Notifications, Has.Count.EqualTo(NotificationService.CAPACITY));
            Assert.That(service.Notifications[0].Title,
                Is.EqualTo($"event {NotificationService.CAPACITY + 9}"), "the newest is kept");
        });
    }

    /// <summary>
    /// The bell in the title bar follows the service, and opening the list clears the dot.
    /// </summary>
    [Test]
    public async Task TheBellFollowsTheNotificationsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        Assert.That(studio.MainWindow.HasUnreadNotifications, Is.False, "nothing has happened yet");

        studio.App.Notifications.Warning("A background refresh failed");

        Assert.That(studio.MainWindow.HasUnreadNotifications, Is.True);

        studio.MainWindow.ShowNotificationsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(studio.MainWindow.AreNotificationsVisible, Is.True);
            Assert.That(studio.MainWindow.HasUnreadNotifications, Is.False, "reading them clears the dot");
            Assert.That(studio.MainWindow.HasNotifications, Is.True, "and the list still has them");
        });
    }

    #endregion
}
