using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Stage 5, the Explorer and the object inspector: columns inside the tree (WS-15), a sixth folder
/// for routines (WS-21), row counts that are allowed to give up (WS-16), a filter that is not the
/// palette (WS-17), and a panel that says what an object is (WS-18).
///
/// Over the real ViewModel graph and a real database throughout: every one of these is a question
/// about what the catalogue actually answers.
/// </summary>
[TestFixture]
public class ExplorerTests
{
    #region Tools

    private static DatabaseNode Folder(StudioFixture studio, string name)
    {
        var root = studio.Explorer.Nodes.First(node => node.ConnectionId == studio.Database.Id);

        return root.Children.First(child => child.Name == name);
    }

    #endregion

    #region The tree

    /// <summary>
    /// A table opens into its columns (WS-15). This is the most frequent question anyone asks of a
    /// schema, and it needed a tab before.
    /// </summary>
    [Test]
    public async Task ATableOpensIntoItsColumnsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        var orders = Folder(studio, "Tables").Children.First(node => node.Name == "Orders");

        // A placeholder, and only a placeholder: it is what makes the expander appear, and until
        // 2026-08-18 there was none - so the node could not be opened and these columns could not
        // be reached from the tree at all.
        Assert.That(orders.Children.All(node => node.IsPlaceholder), Is.True,
            "nothing is READ until the node is opened");

        await studio.Explorer.ExpandNodeAsync(orders);

        var columns = orders.Children.Select(node => node.Name).ToList();

        TestContext.Out.WriteLine(string.Join(", ",
            orders.Children.Select(node => $"{node.Name}:{node.Detail}")));

        Assert.Multiple(() =>
        {
            Assert.That(columns, Does.Contain("Id").And.Contain("CustomerId").And.Contain("Total"));
            Assert.That(orders.Children.All(node => node.NodeType == DatabaseNodeType.Column), Is.True);
            Assert.That(orders.Children.First(node => node.Name == "Id").IsPrimaryKey, Is.True);
            Assert.That(orders.Children.First(node => node.Name == "CustomerId").IsForeignKey, Is.True,
                "a column that points at another table is marked as one");
            Assert.That(orders.Children.First(node => node.Name == "Total").Detail,
                Is.Not.Null.And.Not.Empty, "and each says its type");
        });
    }

    /// <summary>
    /// Opening the same node twice does not read it twice - and does not double its children, which
    /// is what a missing guard looks like from the outside.
    /// </summary>
    [Test]
    public async Task OpeningANodeTwiceReadsItOnceTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        var customers = Folder(studio, "Tables").Children.First(node => node.Name == "Customers");

        await studio.Explorer.ExpandNodeAsync(customers);
        var first = customers.Children.Count;

        await studio.Explorer.ExpandNodeAsync(customers);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(3), "Customers has three columns");
            Assert.That(customers.Children, Has.Count.EqualTo(first));
        });
    }

    /// <summary>
    /// The sixth folder (WS-21). The engine has had functions and procedures since phase 9d, and a
    /// tree that does not show them tells the user the database has none.
    /// </summary>
    [Test]
    public async Task RoutinesHaveAFolderOfTheirOwnTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Database.ExecuteNonQueryAsync(
            "CREATE FUNCTION AddOne(x INTEGER) RETURNS INTEGER AS BEGIN RETURN x + 1; END");

        await studio.Explorer.RefreshAsync(studio.Database);

        var routines = Folder(studio, "Routines");

        Assert.Multiple(() =>
        {
            Assert.That(routines.NodeType, Is.EqualTo(DatabaseNodeType.RoutinesFolder));
            Assert.That(routines.Children.Select(node => node.Name), Does.Contain("AddOne"));
            Assert.That(routines.Children.First().Detail, Does.Contain("function"),
                "and says what it is and what it returns");
        });
    }

    /// <summary>
    /// A folder keeps its place with a zero rather than disappearing (2.1): a node that vanishes
    /// breaks the muscle memory of everyone who knew where it was.
    /// </summary>
    [Test]
    public async Task AnEmptyFolderStaysWithAZeroTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        await studio.Explorer.RefreshAsync(studio.Database);

        var root = studio.Explorer.Nodes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(root.Children.Select(node => node.Name),
                Is.EqualTo(new[] { "Tables", "Views", "Indexes", "Triggers", "Sequences", "Routines" })
                    .AsCollection,
                "six folders, in a fixed order, whatever is in them");

            Assert.That(root.Children.All(folder => folder.Detail == "0"), Is.True,
                "each says how many objects it holds");
        });
    }

    /// <summary>
    /// The status line the refresh writes counts in words, and the words agree with the numbers at
    /// ONE - in both languages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven on rc.1 the line read <c>calib: 6 tables, 1 views, 2 indexes, 1 triggers, 0 sequences</c>
    /// while the Database tab, at the same moment and over the same counts, read <c>1 table</c>
    /// correctly. The difference was not the language: <c>Database.SchemaSummary</c> fills its slots
    /// with <c>Localization.Plural</c> and <c>Explorer.Summary</c> had the nouns written INSIDE the
    /// format string with raw numbers passed in - one string, of the whole application, skipping a
    /// mechanism that was already there.
    /// </para>
    /// <para>
    /// <b>The fixture is the reason this can be seen at all.</b> Its schema has exactly one view and
    /// exactly one trigger; every arrangement with none or several prints a plural that is right by
    /// accident. That is also why the Russian half is asserted here rather than trusted - «1 триггер»
    /// and «1 представление» are two different forms of the same rule.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheSummaryLineCountsInWordsThatAgreeAtOneTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        var english = studio.MainWindow.StatusText;

        studio.App.Localization.SetLanguage("ru");
        await studio.Explorer.RefreshAsync(studio.Database);

        var russian = studio.MainWindow.StatusText;

        TestContext.Out.WriteLine(english);
        TestContext.Out.WriteLine(russian);

        Assert.Multiple(() =>
        {
            // CONTROL: the line was written by THIS refresh. Without it every assertion below is
            // about a status bar that was never touched.
            Assert.That(english, Does.StartWith(studio.Database.DisplayName + ":"),
                "CONTROL: the summary of this connection is not on the status bar at all");

            Assert.That(english, Does.Contain("1 view,").And.Not.Contain("1 views"),
                "the fixture has exactly one view");
            Assert.That(english, Does.Contain("1 trigger,").And.Not.Contain("1 triggers"),
                "the fixture has exactly one trigger");
            Assert.That(english, Does.Contain("0 sequences"),
                "and zero is still the plural - the rule is not 'drop the s when it looks odd'");

            Assert.That(russian, Does.Contain("1 представление").And.Not.Contain("1 представления"),
                "Russian at one takes the singular too, and it is a different form from the English one");
            Assert.That(russian, Does.Contain("1 триггер,").And.Not.Contain("1 триггера"));
            Assert.That(russian, Does.Contain("0 последовательностей"),
                "Count.Sequences is the family that had to be added; without it the key prints itself");
        });
    }

    #endregion

    #region Counting (WS-16)

    /// <summary>
    /// The tree is drawn from names, and the numbers are filled in afterwards (2.2).
    ///
    /// This asserts the two ends - a usable tree without counts, and correct counts after the pass -
    /// and NOT the moment in between. The first version of this case asserted that no count had
    /// arrived by the time the refresh returned, which is a race: it passed on its own and failed in
    /// the whole suite, where the machine was slower and the background pass had already finished.
    /// A test of when a background task happens to be is not a test of anything.
    /// </summary>
    [Test]
    public async Task TheTreeIsBuiltFromNamesAndTheCountsFollowTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        var tables = Folder(studio, "Tables");
        var orders = tables.Children.First(node => node.Name == "Orders");

        Assert.That(tables.Children.Select(node => node.Name),
            Does.Contain("Orders").And.Contain("Customers"),
            "every name is there as soon as the tree is built");

        await studio.Explorer.CountRowsAsync(studio.Database);

        Assert.Multiple(() =>
        {
            Assert.That(orders.CountState, Is.EqualTo(RowCountState.Counted));
            Assert.That(orders.RowCount, Is.EqualTo(StudioFixture.ORDER_COUNT));
            Assert.That(orders.Detail, Is.EqualTo(StudioFixture.ORDER_COUNT.ToString("N0")));
        });
    }

    /// <summary>
    /// <b>The count follows a script that WRITES, and until 2026-08-09 it did not.</b> Fifty rows went
    /// in through the editor and the node kept the number it had until the database was reopened; it
    /// was written down as probably the deliberate laziness of <c>WS-16</c>, and measured it was that
    /// nothing asked. A script that changes the schema has reloaded the branch since stage 6, and one
    /// that only writes rows went past that test and had no other.
    /// </summary>
    [Test]
    public async Task TheCountFollowsAnInsertMadeInTheEditorAsync()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);
        await studio.Explorer.CountRowsAsync(studio.Database);

        var orders = Folder(studio, "Tables").Children.First(node => node.Name == "Orders");

        Assert.That(orders.RowCount, Is.EqualTo(StudioFixture.ORDER_COUNT), "the number before");

        var tab = studio.FirstQueryTab;
        tab.SqlText = "INSERT INTO Orders (Id, CustomerId, Total, Status) VALUES (9001, 1, 5, 'new')";

        // Through the WORKSPACE's Execute, which is the button: bringing the tree into line is the
        // frame's work, not the tab's.
        studio.Workspace.SelectedTab = tab;
        await StudioFixture.PressAsync(studio.Workspace.ExecuteQueryCommand);

        Assert.Multiple(() =>
        {
            Assert.That(tab.ErrorMessage, Is.Null);
            Assert.That(orders.RowCount, Is.EqualTo(StudioFixture.ORDER_COUNT + 1),
                "the tree says what the table holds, not what it held when it was drawn");
            Assert.That(orders.Detail, Is.EqualTo((StudioFixture.ORDER_COUNT + 1).ToString("N0")));
        });
    }

    /// <summary>
    /// CONTROL, and it is what keeps the fix from being "count everything after every query": a script
    /// that writes nothing leaves the counts alone, and a table nobody wrote to is not asked again.
    /// A count is a query, and forty tables would be forty of them.
    /// </summary>
    [Test]
    public async Task AQueryThatWritesNothingCostsNoCountAsync()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);
        await studio.Explorer.CountRowsAsync(studio.Database);

        var customers = Folder(studio, "Tables").Children.First(node => node.Name == "Customers");
        customers.RowCount = 4242;

        var tab = studio.FirstQueryTab;
        tab.SqlText = "INSERT INTO Orders (Id, CustomerId, Total, Status) VALUES (9002, 1, 5, 'new')";

        studio.Workspace.SelectedTab = tab;
        await StudioFixture.PressAsync(studio.Workspace.ExecuteQueryCommand);

        Assert.That(customers.RowCount, Is.EqualTo(4242),
            "the table the script never named was not counted again");

        tab.SqlText = "SELECT * FROM Orders";
        await StudioFixture.PressAsync(studio.Workspace.ExecuteQueryCommand);

        Assert.That(tab.TablesWritten, Is.Empty, "and a SELECT names nothing to count");
    }

    /// <summary>
    /// A count that is cut short reports "unknown" rather than throwing (WS-16). The tree calls this
    /// for every table it draws, and one cancelled count must not become an exception in the middle
    /// of a background pass.
    ///
    /// Cancelled deliberately rather than timed out: a deadline of zero against a count this engine
    /// answers instantly is a race, and the first version of this case lost it.
    /// </summary>
    [Test]
    public async Task ACountThatIsCutShortAnswersUnknownInsteadOfThrowingTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var count = await studio.Database.TryCountRowsAsync("Orders", TimeSpan.FromSeconds(5), cancelled.Token);

        Assert.That(count, Is.Null, "no answer, and no exception either");

        // CONTROL: the same call with a live token answers - so the null above is the cancellation
        // and not a broken query.
        var withTime = await studio.Database.TryCountRowsAsync("Orders", TimeSpan.FromSeconds(5));

        Assert.That(withTime, Is.EqualTo(StudioFixture.ORDER_COUNT));
    }

    #endregion

    #region The filter (WS-17)

    [Test]
    public async Task TheFilterFindsObjectsAcrossEveryOpenConnectionTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var second = await studio.OpenAnotherAsync("second");

        await studio.Explorer.RefreshAsync();

        studio.Explorer.Filter = "ord";

        TestContext.Out.WriteLine(string.Join("\n", studio.Explorer.FilterMatches.Select(m => m.ToString())));

        Assert.Multiple(() =>
        {
            Assert.That(studio.Explorer.IsFiltering, Is.True);
            Assert.That(studio.Explorer.FilterMatches.Select(match => match.Node.Name),
                Does.Contain("Orders").And.Contain("OrdersAudit"));

            Assert.That(studio.Explorer.FilterMatches.Any(match => match.Path.Contains(second.DisplayName)),
                Is.True, "matches come from every open connection");

            Assert.That(studio.Explorer.FilterSummary, Does.Contain("2 connections"));
        });
    }

    /// <summary>
    /// The panel is TOLD that the filter is on, which is the half the two cases either side of this
    /// one cannot see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Phase 17's premise, in this file.</b> Every other case here reads <c>IsFiltering</c> and
    /// finds it true - and it was true, and always had been, while nothing on the screen moved. It
    /// was a computed property: <c>=> !string.IsNullOrWhiteSpace(Filter)</c> gives the right answer
    /// to whoever asks and tells nobody, so the three bindings that DEPEND on it - the tree hiding,
    /// the match list appearing, the Esc button - asked once when the window was built and never
    /// again. Measured by typing «aspnet» into the box in the running application: fifteen tables
    /// still listed, no Esc button, and the matches computed correctly out of sight.
    /// </para>
    /// <para>
    /// So this asserts the NOTIFICATION and not the value. A test that reads the property cannot
    /// fail on this defect, which is why one existed for five phases and did not.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TurningTheFilterOnAndOffIsAnnouncedToThePanelTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        var announced = new List<string>();

        studio.Explorer.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? string.Empty);

        studio.Explorer.Filter = "Orders";

        Assert.That(announced, Does.Contain(nameof(studio.Explorer.IsFiltering)),
            "the matches are found and the panel is never told to show them");

        announced.Clear();

        studio.Explorer.ClearFilterCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(announced, Does.Contain(nameof(studio.Explorer.IsFiltering)),
                "and the tree never comes back");

            // CONTROL: a ViewModel that announced EVERYTHING on every change would pass both halves
            // above without meaning anything.
            Assert.That(announced, Does.Not.Contain(nameof(studio.Explorer.CountTimeout)));
        });
    }

    /// <summary>
    /// The filter holds its result until it is cleared - that is what makes it different from the
    /// palette, which is one jump and gone (WS-17).
    /// </summary>
    [Test]
    public async Task ClearingTheFilterPutsTheTreeBackTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        studio.Explorer.Filter = "Orders";
        var whileFiltering = studio.Explorer.FilterMatches.Count;

        studio.Explorer.ClearFilterCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(whileFiltering, Is.GreaterThan(0));
            Assert.That(studio.Explorer.IsFiltering, Is.False);
            Assert.That(studio.Explorer.FilterMatches, Is.Empty);
            Assert.That(studio.Explorer.FilterSummary, Is.Null);
            Assert.That(studio.Explorer.Nodes, Is.Not.Empty, "and the tree itself is untouched");
        });
    }

    [Test]
    public async Task TheFilterMatchesColumnsThatHaveBeenLoadedTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        var orders = Folder(studio, "Tables").Children.First(node => node.Name == "Orders");
        await studio.Explorer.ExpandNodeAsync(orders);

        studio.Explorer.Filter = "CustomerId";

        Assert.That(studio.Explorer.FilterMatches.Any(match =>
                match.Node.NodeType == DatabaseNodeType.Column && match.Path.Contains("Orders")),
            Is.True, "the name of a column is often all anyone remembers of a schema");
    }

    #endregion

    #region The inspector (WS-18)

    [Test]
    public async Task TheInspectorDescribesATableWithoutOpeningATabTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        var tabsBefore = studio.Workspace.Tabs.Count;

        studio.Explorer.SelectedNode = Folder(studio, "Tables").Children.First(node => node.Name == "Orders");

        await studio.App.InspectorVm.LoadAsync(studio.Explorer.SelectedNode);

        var inspector = studio.App.InspectorVm;

        Assert.Multiple(() =>
        {
            Assert.That(inspector.Title, Is.EqualTo("Orders"));
            Assert.That(inspector.Subtitle, Does.Contain(studio.Database.DisplayName).And.Contain("table"));
            Assert.That(inspector.ColumnCount, Is.EqualTo(4));
            Assert.That(inspector.PrimaryKey, Is.EqualTo("Id"));
            Assert.That(inspector.RowCount, Is.EqualTo(StudioFixture.ORDER_COUNT));

            Assert.That(inspector.References.Select(key => key.ToString()),
                Has.Some.Contains("Customers"), "what it points at");
            Assert.That(inspector.Indexes.Select(index => index.Name),
                Does.Contain("IX_Orders_CustomerId"));

            Assert.That(studio.Workspace.Tabs, Has.Count.EqualTo(tabsBefore),
                "and none of it opened a tab");
        });
    }

    /// <summary>
    /// The part of the inspector that knows the engine (2.5): which columns can be reached through an
    /// index, and - because this engine does not create one for a PRIMARY KEY - which key has none.
    /// </summary>
    [Test]
    public async Task TheInspectorSaysWhichColumnsHaveAnIndexTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        studio.Explorer.SelectedNode = Folder(studio, "Tables").Children.First(node => node.Name == "Orders");
        await studio.App.InspectorVm.LoadAsync(studio.Explorer.SelectedNode);

        var notes = studio.App.InspectorVm.DataAccess;

        TestContext.Out.WriteLine(string.Join("\n", notes.Select(note => note.ToString())));

        Assert.Multiple(() =>
        {
            Assert.That(notes.Select(note => note.Column), Is.SupersetOf(new[] { "Id", "CustomerId", "Total" }));
            Assert.That(notes.First(note => note.Column == "CustomerId").IsIndexed, Is.True,
                "IX_Orders_CustomerId covers it");
            Assert.That(notes.First(note => note.Column == "Total").IsIndexed, Is.False,
                "and nothing covers Total");
        });
    }

    /// <summary>
    /// A view whose definition the catalogue does not hold says so, rather than showing a
    /// reconstruction from its columns (2.5). Which of the two happens depends on the database's
    /// format, so this asserts the pair: either a definition, or the sentence explaining its absence.
    /// </summary>
    [Test]
    public async Task TheInspectorEitherShowsADefinitionOrSaysWhyItCannotTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync(studio.Database);

        var views = Folder(studio, "Views");

        studio.Explorer.SelectedNode = views.Children.First();
        await studio.App.InspectorVm.LoadAsync(studio.Explorer.SelectedNode);

        var inspector = studio.App.InspectorVm;

        TestContext.Out.WriteLine($"definition: {inspector.Definition ?? "(none)"} | summary: {inspector.Summary}");

        Assert.That(inspector.HasDefinition || inspector.Summary != null, Is.True,
            "one or the other, never silence");
    }

    /// <summary>
    /// The inspector follows the selection, and an object of another connection is described by that
    /// connection (WS-3 again, one level down).
    /// </summary>
    [Test]
    public async Task TheInspectorFollowsTheSelectionAcrossConnectionsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var second = await studio.OpenAnotherAsync("second");
        await studio.Explorer.RefreshAsync();

        var secondRoot = studio.Explorer.Nodes.First(node => node.ConnectionId == second.Id);
        var table = secondRoot.Children.First(child => child.Name == "Tables").Children.First();

        await studio.App.InspectorVm.LoadAsync(table);

        Assert.That(studio.App.InspectorVm.Subtitle, Does.Contain(second.DisplayName));
    }

    #endregion
}
