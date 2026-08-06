using System.Data;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Stage 2, and the question the whole stage exists to answer: with two databases open, does a tab run
/// in ITS connection (WS-3), and does closing one connection end only ITS tabs (WS-13)?
///
/// Everything here is the shipping graph over two real databases opened through ONE
/// <see cref="ConnectionManager"/> - the lifetime the application has. Studio held a single connection
/// and a single global status event until this stage, so none of these cases could even be written.
///
/// Every case that asserts "the rows landed in the first" also asserts that they did NOT land in the
/// second, and the readiness case runs the same scenario twice with the roles swapped. Without that,
/// an implementation that always used the active connection would pass whenever the active one
/// happened to be right.
/// </summary>
[TestFixture]
public class MultiConnectionTests
{
    #region Tools

    /// <summary>
    /// Selects a table of the given connection in the tree, exactly as a click does. This is the thing
    /// WS-3 says must NOT decide where a query goes.
    /// </summary>
    private static async Task SelectInTreeAsync(StudioFixture studio, IDatabaseSession session, string table)
    {
        await studio.Explorer.RefreshAsync(session);

        var root = studio.Explorer.Nodes.FirstOrDefault(node => node.ConnectionId == session.Id);

        Assert.That(root, Is.Not.Null, $"the tree has no branch for {session.DisplayName}");

        var node = root!.Children
            .FirstOrDefault(folder => folder.NodeType == DatabaseNodeType.TablesFolder)?
            .Children.FirstOrDefault(child => child.Name == table);

        Assert.That(node, Is.Not.Null, $"{table} is not in the tree of {session.DisplayName}");

        studio.Explorer.SelectedNode = node;
    }

    private static QueryTabViewModel OpenQueryTabIn(StudioFixture studio, IDatabaseSession session, string title)
    {
        // The connection the user is looking at is the one a new tab belongs to.
        studio.Connections.Active = session;

        var tab = studio.Workspace.OpenQueryTab(string.Empty, title);

        Assert.That(tab.Session, Is.SameAs(session), "setup: the tab was opened in the wrong connection");

        return tab;
    }

    private static async Task RunInTabAsync(StudioFixture studio, QueryTabViewModel tab, string sql)
    {
        tab.SqlText = sql;
        studio.Workspace.SelectedTab = tab;

        await StudioFixture.PressAsync(studio.Workspace.ExecuteQueryCommand);

        Assert.That(tab.ErrorMessage, Is.Null.Or.Empty, $"the statement failed: {tab.ErrorMessage}");
    }

    private static async Task RunInTabDirectlyAsync(QueryTabViewModel tab, string sql)
    {
        // The other execution path: the button inside the tab rather than the workspace toolbar. Both
        // had to be cut over, and a stage that cut only one would leave half a defect.
        tab.SqlText = sql;

        await StudioFixture.PressAsync(tab.ExecuteQueryCommand);

        Assert.That(tab.ErrorMessage, Is.Null.Or.Empty, $"the statement failed: {tab.ErrorMessage}");
    }

    /// <summary>
    /// Reads a value back out of one connection by scanning the rows - never through a count, which on
    /// this engine is separate state.
    /// </summary>
    private static async Task<string?> FirstNameAsync(IDatabaseSession session)
    {
        var result = await session.ExecuteQueryAsync("SELECT Name FROM Customers ORDER BY Id");

        Assert.That(result.ErrorMessage, Is.Null.Or.Empty, "reading back failed");

        return result.Data!.Rows[0][0] as string;
    }

    #endregion

    #region Readiness

    /// <summary>
    /// THE READINESS CASE for stage 2, in the plan's own words: two databases are open, an INSERT runs
    /// in a tab of the first, and the rows are in the first - while the tree has the SECOND selected.
    ///
    /// Run twice, once per role, because a tab that quietly used the active connection would pass the
    /// single-direction version half the time. Both execution paths are exercised: the workspace
    /// toolbar and the button inside the tab.
    /// </summary>
    [TestCase(0)]
    [TestCase(1)]
    public async Task AQueryRunsInItsOwnTabsConnectionTest(int writeInto)
    {
        await using var studio = await StudioFixture.CreateAsync();

        var first = studio.Database;
        var second = await studio.OpenAnotherAsync("second");

        var target = writeInto == 0 ? first : second;
        var other = writeInto == 0 ? second : first;

        var tab = OpenQueryTabIn(studio, target, "writer");

        // The user clicks a table of the OTHER database in the tree. This moves the focus, and by
        // WS-3 it must not move the target of a tab that is already open.
        await SelectInTreeAsync(studio, other, "Customers");

        await RunInTabAsync(studio, tab,
            "INSERT INTO Customers (Name, Email) VALUES ('from the tab', 'tab@example')");

        await RunInTabDirectlyAsync(tab,
            "INSERT INTO Customers (Name, Email) VALUES ('from the tab again', NULL)");

        var inTarget = await studio.CountRowsAsync("Customers", target);
        var inOther = await studio.CountRowsAsync("Customers", other);

        TestContext.Out.WriteLine(
            $"wrote through the tab of {target.DisplayName} while {other.DisplayName} was selected: "
            + $"{target.DisplayName}={inTarget}, {other.DisplayName}={inOther}");

        Assert.Multiple(() =>
        {
            // Controls: the scenario is only meaningful if there really are two live connections and
            // the selection really is on the other one.
            Assert.That(studio.Connections.Sessions, Has.Count.EqualTo(2), "CONTROL: two connections");
            Assert.That(first.IsConnected && second.IsConnected, Is.True, "CONTROL: both are open");
            Assert.That(studio.Connections.Active, Is.SameAs(other),
                "CONTROL: the tree selection made the OTHER connection active");
            Assert.That(tab.Session, Is.SameAs(target), "the tab never changed connection");

            Assert.That(inTarget, Is.EqualTo(StudioFixture.CUSTOMER_COUNT + 2),
                "the rows landed in the connection the tab belongs to");
            Assert.That(inOther, Is.EqualTo(StudioFixture.CUSTOMER_COUNT),
                "and not in the one selected in the tree");
        });
    }

    /// <summary>
    /// The same for the table editor: its buffer is applied to the connection its tab was opened in.
    /// The editor writes through a different path (ExecuteBatchAsync, one transaction), so it is a
    /// separate question from the query tab's.
    /// </summary>
    [Test]
    public async Task TheTableEditorAppliesItsBufferToItsOwnConnectionTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var first = studio.Database;
        var second = await studio.OpenAnotherAsync("second");

        var editor = await studio.Workspace.OpenTableEditTabAsync(first, "Customers");

        await SelectInTreeAsync(studio, second, "Customers");

        // Exactly what the grid does when a cell is committed.
        var rowView = new DataView(editor.EditableData!)[0];
        rowView.Row["Name"] = "edited in the first";
        editor.CellEditedCommand.Execute(rowView);

        await StudioFixture.PressAsync(editor.CommitCommand);

        var inFirst = await FirstNameAsync(first);
        var inSecond = await FirstNameAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(editor.Session, Is.SameAs(first));
            Assert.That(editor.ErrorMessage, Is.Null.Or.Empty, $"the commit failed: {editor.ErrorMessage}");
            Assert.That(studio.Connections.Active, Is.SameAs(second), "CONTROL: the other one is selected");

            Assert.That(inFirst, Is.EqualTo("edited in the first"), "the edit reached its own database");
            Assert.That(inSecond, Is.EqualTo("Northwind Trading"), "and not the selected one");
        });
    }

    /// <summary>
    /// The same table name in two databases is two different tables. The editor used to identify a tab
    /// by table name alone, which with two connections would hand the second database's user the first
    /// database's rows.
    /// </summary>
    [Test]
    public async Task TheSameTableInTwoDatabasesGetsTwoTabsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var first = studio.Database;
        var second = await studio.OpenAnotherAsync("second");

        var editorOne = await studio.Workspace.OpenTableEditTabAsync(first, "Customers");
        var editorTwo = await studio.Workspace.OpenTableEditTabAsync(second, "Customers");
        var editorOneAgain = await studio.Workspace.OpenTableEditTabAsync(first, "Customers");

        Assert.Multiple(() =>
        {
            Assert.That(editorTwo, Is.Not.SameAs(editorOne), "two connections, two tabs");
            Assert.That(editorOneAgain, Is.SameAs(editorOne),
                "CONTROL: asking twice for the same table in the same connection still reuses its tab");
            Assert.That(editorTwo.UniqueId, Is.Not.EqualTo(editorOne.UniqueId));
        });
    }

    #endregion

    #region WS-13 - disconnecting is local

    /// <summary>
    /// WS-13. Closing one connection closes ITS tabs. Until this stage the status event was global:
    /// disconnecting anything closed every data and structure tab of every database, which is the
    /// symptom that made this stage a single pass rather than several.
    /// </summary>
    [Test]
    public async Task ClosingOneConnectionClosesOnlyItsTabsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var first = studio.Database;
        var second = await studio.OpenAnotherAsync("second");

        var editorOne = await studio.Workspace.OpenTableEditTabAsync(first, "Customers");
        var structureOne = await studio.Workspace.OpenStructureTabAsync(first, "Orders", DatabaseNodeType.Table);
        var queryOne = OpenQueryTabIn(studio, first, "first's query");

        var editorTwo = await studio.Workspace.OpenTableEditTabAsync(second, "Customers");
        var structureTwo = await studio.Workspace.OpenStructureTabAsync(second, "Orders", DatabaseNodeType.Table);
        var queryTwo = OpenQueryTabIn(studio, second, "second's query");

        queryTwo.SqlText = "SELECT * FROM Customers";

        var before = studio.Workspace.Tabs.Count;

        await studio.Connections.CloseAsync(first);

        TestContext.Out.WriteLine(
            $"tabs before: {before}, after: {studio.Workspace.Tabs.Count} - "
            + string.Join(", ", studio.Workspace.Tabs.Select(tab => $"{tab.Title}[{tab.ConnectionName}]")));

        Assert.Multiple(() =>
        {
            Assert.That(studio.Workspace.Tabs, Does.Not.Contain(editorOne), "its editor is gone");
            Assert.That(studio.Workspace.Tabs, Does.Not.Contain(structureOne), "its structure tab is gone");

            Assert.That(studio.Workspace.Tabs, Does.Contain(editorTwo),
                "the other connection's editor stays - this is the whole of WS-13");
            Assert.That(studio.Workspace.Tabs, Does.Contain(structureTwo));
            Assert.That(editorTwo.Session, Is.SameAs(second), "and it still knows where it belongs");
            Assert.That(editorTwo.EditableData, Is.Not.Null, "with its data still loaded");

            // A query tab is kept even when its own connection goes: the text in it is usually the
            // only copy of it. It loses the connection and says so.
            Assert.That(studio.Workspace.Tabs, Does.Contain(queryOne));
            Assert.That(queryOne.Session, Is.Null);
            Assert.That(queryOne.CanBind, Is.False,
                "and it will not adopt someone else's connection behind the user's back");

            Assert.That(queryTwo.Session, Is.SameAs(second));
            Assert.That(queryTwo.SqlText, Is.EqualTo("SELECT * FROM Customers"), "untouched");
        });
    }

    /// <summary>
    /// And the other half: what the tab does when asked to run without a connection. Silence, or a
    /// query that goes somewhere else, would both be worse than a refusal that names the connection.
    /// </summary>
    [Test]
    public async Task ATabWhoseConnectionIsClosedRefusesToRunTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var first = studio.Database;
        var second = await studio.OpenAnotherAsync("second");

        var orphan = OpenQueryTabIn(studio, first, "first's query");
        orphan.SqlText = "INSERT INTO Customers (Name, Email) VALUES ('must not be written', NULL)";

        await studio.Connections.CloseAsync(first);

        // The second database is open and active. A tab that fell back to "whatever is connected"
        // would write into it.
        studio.Connections.Active = second;

        await StudioFixture.PressAsync(orphan.ExecuteQueryCommand);

        Assert.Multiple(async () =>
        {
            Assert.That(orphan.CanExecuteQuery, Is.False);
            Assert.That(orphan.ErrorMessage, Does.Contain("closed"),
                "the refusal has to say what is wrong");
            Assert.That(orphan.ErrorMessage, Does.Contain("studio"),
                "and name the connection the tab belongs to");

            Assert.That(await studio.CountRowsAsync("Customers", second),
                Is.EqualTo(StudioFixture.CUSTOMER_COUNT),
                "nothing was written into the connection that happened to be active");
        });
    }

    /// <summary>
    /// The tree is the other half of WS-13: one root per connection, and closing one takes its root
    /// with it and leaves the rest standing.
    /// </summary>
    [Test]
    public async Task TheTreeHasOneRootPerConnectionTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var first = studio.Database;
        var second = await studio.OpenAnotherAsync("second");

        await studio.Explorer.RefreshAsync();

        var rootsWhenBothOpen = studio.Explorer.Nodes.Select(node => node.Name).ToArray();

        await studio.Connections.CloseAsync(first);

        Assert.Multiple(() =>
        {
            Assert.That(rootsWhenBothOpen, Is.EqualTo(new[] { first.DisplayName, second.DisplayName }).AsCollection,
                "both connections are in the tree, in the order they were opened");

            Assert.That(studio.Explorer.Nodes.Select(node => node.Name),
                Is.EqualTo(new[] { second.DisplayName }).AsCollection,
                "closing one removes ITS branch and nothing else");

            Assert.That(studio.Explorer.Nodes[0].Children
                    .First(folder => folder.NodeType == DatabaseNodeType.TablesFolder)
                    .Children.Select(node => node.Name),
                Does.Contain("Customers"),
                "and the branch that stayed still has its schema");
        });
    }

    #endregion

    #region Bookkeeping

    /// <summary>
    /// A tab that has never had a connection adopts the first one opened - that is what keeps "start
    /// Studio, type a query, open a database, press Execute" working. It is deliberately NOT the same
    /// rule as re-adopting after a close, which would run a query against a database the user never
    /// chose for it.
    /// </summary>
    [Test]
    public async Task TheStartupTabAdoptsTheFirstConnectionAndOnlyThatOneTest()
    {
        await using var studio = await StudioFixture.CreateAsync(connect: false);

        var startupTab = studio.FirstQueryTab;

        Assert.Multiple(() =>
        {
            Assert.That(startupTab.Session, Is.Null, "nothing is open yet");
            Assert.That(startupTab.CanBind, Is.True);
        });

        var first = await studio.ConnectAsync();
        await StudioFixture.CreateSchemaAsync(first);

        Assert.That(startupTab.Session, Is.SameAs(first), "the tab that was waiting adopts the connection");

        var second = await studio.OpenAnotherAsync("second");

        Assert.Multiple(() =>
        {
            Assert.That(startupTab.Session, Is.SameAs(first),
                "and it stays there when a second database is opened (WS-3)");
            Assert.That(second, Is.Not.SameAs(first));
        });
    }

    /// <summary>
    /// Two databases with the same file name in different folders is the ordinary case. The tree and
    /// the tabs name connections, so the names have to be tellable apart.
    /// </summary>
    [Test]
    public async Task ConnectionsWithTheSameNameAreTellableApartTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        // The real shape of the collision: the same file name in two different folders.
        var left = Path.Combine(studio.Root, "left");
        var right = Path.Combine(studio.Root, "right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);

        var first = await studio.Connections.OpenAsync(
            new ConnectionInfo { FilePath = Path.Combine(left, "sales.witdb") });

        var second = await studio.Connections.OpenAsync(
            new ConnectionInfo { FilePath = Path.Combine(right, "sales.witdb") });

        Assert.Multiple(() =>
        {
            Assert.That(first!.DisplayName, Is.EqualTo("sales"));
            Assert.That(second!.DisplayName, Is.EqualTo("sales (2)"),
                "the second connection called 'sales' is named, not silently duplicated");
            Assert.That(second.ColorIndex, Is.Not.EqualTo(first.ColorIndex),
                "and it gets a colour of its own (WS-3)");
            Assert.That(second.Connection.FilePath, Is.Not.EqualTo(first.Connection.FilePath),
                "CONTROL: they really are two databases");
        });
    }

    #endregion
}
