using OutWit.Common.MVVM.Commands;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Dropping an object asks first, and the question says what breaks (WS-20).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this replaces.</b> Until 2026-08-10 <c>DropObjectAsync</c> went straight from a
/// context-menu click to <c>ExecuteNonQueryAsync</c>: one click destroyed a table, with no question at
/// all, while the settings page showed a ticked "ask before dropping an object". The setting had no
/// reader anywhere in the application. <b>No test mentioned this path</b> - which is why 818 green
/// tests said nothing about it.
/// </para>
/// <para>
/// <b>The assertion that matters is on the QUESTION, not on the outcome.</b> "The table is still
/// there" passes for an application that refused the drop for any reason at all - a syntax error, a
/// lost connection, a disabled command. Asserting that a question was put in front of a person, and
/// what it said, is the only thing that distinguishes a confirmation from a failure.
/// </para>
/// </remarks>
[TestFixture]
public class ExplorerDropConfirmationTests
{
    #region Setup

    private StudioFixture m_fixture = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        await m_fixture.Explorer.RefreshAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region The question is asked

    [Test]
    public async Task DroppingATableAsksBeforeItRunsAnythingTest()
    {
        var explorer = m_fixture.Explorer;
        var confirmations = m_fixture.Confirmations;

        confirmations.AllowDestructive = false;

        explorer.SelectedNode = Find("Logs", DatabaseNodeType.Table);
        await DropAsync(explorer);

        Assert.Multiple(() =>
        {
            Assert.That(confirmations.DestructiveQuestions, Has.Count.EqualTo(1),
                "the user was never asked");

            Assert.That(confirmations.DestructiveQuestions[0].Kind,
                Is.EqualTo(ConfirmationKind.DroppingObject),
                "the question must be the one the DroppingObject setting governs, or the setting "
                + "cannot switch it off");
        });

        // And the refusal has to mean something.
        var tables = await m_fixture.Database.GetTablesAsync();

        Assert.That(tables.Select(t => t.Name), Contains.Item("Logs"),
            "the table was dropped although the question was answered no");
    }

    [Test]
    public async Task AnAllowedDropGoesThroughTest()
    {
        var explorer = m_fixture.Explorer;

        m_fixture.Confirmations.AllowDestructive = true;

        explorer.SelectedNode = Find("Logs", DatabaseNodeType.Table);
        await DropAsync(explorer);

        var tables = await m_fixture.Database.GetTablesAsync();

        // The control on the case above: without this, "the table survived" would pass for an
        // application that cannot drop anything at all.
        Assert.That(tables.Select(t => t.Name), Does.Not.Contain("Logs"),
            "the drop was allowed and did not happen");
    }

    #endregion

    #region The question says what breaks

    [Test]
    public async Task TheQuestionShowsTheStatementItWillRunTest()
    {
        var explorer = m_fixture.Explorer;

        m_fixture.Confirmations.AllowDestructive = false;

        explorer.SelectedNode = Find("Logs", DatabaseNodeType.Table);
        await DropAsync(explorer);

        var asked = m_fixture.Confirmations.DestructiveQuestions.Single();

        Assert.That(asked.Sql, Does.Contain("DROP TABLE").And.Contains("Logs"),
            "the canon's rule is that clicks assemble SQL and the SQL is shown");
    }

    [Test]
    public async Task TheQuestionNamesTheViewThatReadsTheTableTest()
    {
        var explorer = m_fixture.Explorer;

        m_fixture.Confirmations.AllowDestructive = false;

        // ActiveOrders reads Orders, and TR_Orders_Audit and IX_Orders_CustomerId belong to it.
        explorer.SelectedNode = Find("Orders", DatabaseNodeType.Table);
        await DropAsync(explorer);

        var consequences = m_fixture.Confirmations.DestructiveQuestions.Single().Consequences;

        Assert.Multiple(() =>
        {
            Assert.That(consequences.Any(c => c.Contains("ActiveOrders")), Is.True,
                "a view reads this table and the question did not say so: " + Join(consequences));

            Assert.That(consequences.Any(c => c.Contains("1")), Is.True,
                "the index and the trigger that go with the table were not counted: " + Join(consequences));
        });
    }

    [Test]
    public async Task TheQuestionNamesTheForeignKeyPointingAtTheTableTest()
    {
        var explorer = m_fixture.Explorer;

        m_fixture.Confirmations.AllowDestructive = false;

        // Orders.CustomerId references Customers.
        explorer.SelectedNode = Find("Customers", DatabaseNodeType.Table);
        await DropAsync(explorer);

        var consequences = m_fixture.Confirmations.DestructiveQuestions.Single().Consequences;

        Assert.That(consequences.Any(c => c.Contains("Orders") && c.Contains("CustomerId")), Is.True,
            "a foreign key points at this table and the question did not say so: " + Join(consequences));
    }

    /// <summary>
    /// The other direction, and it has to be here: an object nothing depends on names no other
    /// object.
    /// </summary>
    /// <remarks>
    /// Without this the consequence cases above would pass for an implementation that lists everything
    /// in the database for every object.
    ///
    /// <b>The first version of this case asserted an EMPTY list and was wrong</b> - <c>Logs</c> holds
    /// two rows and the question says so. That is not a dependency, and it is the most important thing
    /// on the list: "2 rows will be deleted" is what a person actually decides on. The case now
    /// measures what it meant to measure.
    /// </remarks>
    [Test]
    public async Task AnObjectNothingDependsOnNamesNoOtherObjectTest()
    {
        var explorer = m_fixture.Explorer;

        m_fixture.Confirmations.AllowDestructive = false;

        explorer.SelectedNode = Find("Logs", DatabaseNodeType.Table);
        await DropAsync(explorer);

        var consequences = m_fixture.Confirmations.DestructiveQuestions.Single().Consequences;

        var neighbours = new[] { "Orders", "Customers", "ActiveOrders", "IX_", "TR_" };

        Assert.That(consequences.Any(c => neighbours.Any(c.Contains)), Is.False,
            "nothing refers to Logs, so no other object may appear: " + Join(consequences));
    }

    /// <summary>
    /// How much data goes. The canon's destructive rule is about consequences, and the count of rows
    /// is the consequence a person weighs before anything else.
    /// </summary>
    [Test]
    public async Task TheQuestionSaysHowManyRowsWillBeDeletedTest()
    {
        var explorer = m_fixture.Explorer;

        m_fixture.Confirmations.AllowDestructive = false;

        explorer.SelectedNode = Find("Logs", DatabaseNodeType.Table);
        await DropAsync(explorer);

        var consequences = m_fixture.Confirmations.DestructiveQuestions.Single().Consequences;

        // The fixture writes two rows into Logs. Asserting the NUMBER rather than "a row count is
        // mentioned": a question that always said "0 rows" would pass the weaker form.
        Assert.That(consequences.Any(c => c.Contains("2")), Is.True,
            "the question did not say how many rows would go: " + Join(consequences));
    }

    #endregion

    #region Tools

    /// <summary>
    /// RelayCommandAsync is 'async void', so waiting is explicit rather than relying on the command
    /// happening to run to completion inline - the same reasoning as WorkspaceTabsViewModelTests.
    /// </summary>
    private static async Task DropAsync(OutWit.Database.Studio.ViewModels.DatabaseExplorerViewModel explorer)
    {
        var command = (RelayCommandAsync)explorer.DropObjectCommand;

        command.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (command.IsExecuting)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The drop command did not complete within 30 seconds.");

            await Task.Delay(5);
        }
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(empty)" : string.Join(" | ", values);

    private DatabaseNode Find(string name, DatabaseNodeType type)
    {
        var node = Walk(m_fixture.Explorer.Nodes)
            .FirstOrDefault(n => n.NodeType == type && n.Name == name);

        Assert.That(node, Is.Not.Null, $"{type} {name} is not in the tree.");

        return node!;
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

    #endregion
}
