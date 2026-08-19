using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A function and a procedure are objects in the tree, not labels in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Known issue 27, found by clicking through 3.1.1.</b> The sixth folder arrived with WS-21 and
/// nothing else did: a routine's context menu offered one item, <i>Refresh</i>, while the engine has
/// had <c>DROP FUNCTION</c> and <c>DROP PROCEDURE</c> since phase 9d and the catalogue already
/// carried the body - the inspector on the right was showing it.
/// </para>
/// <para>
/// <b>And the dump had the same hole, which is worse than a missing menu item</b>: it wrote views,
/// indexes and triggers, so a database with routines dumped to a script that restored without them
/// and said nothing about it.
/// </para>
/// <para>
/// The assertion that matters here is not "a definition is produced" but that the definition RUNS
/// BACK: the routine is dropped and the text Studio wrote is executed, and the routine is there
/// again and still answers.
/// </para>
/// </remarks>
[TestFixture]
public class ARoutineIsAnObjectLikeAnyOtherTests
{
    #region Constants

    private const string FUNCTION = "CREATE FUNCTION DiscountedTotal(Amount DECIMAL(18,2), Percent INT) "
        + "RETURNS DECIMAL(18,2) AS BEGIN RETURN (Amount - ((Amount * Percent) / 100)); END";

    private const string PROCEDURE = "CREATE PROCEDURE ArchiveOld() AS BEGIN "
        + "UPDATE Orders SET Status = 'archived' WHERE Status = 'new'; END";

    #endregion

    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        await m_studio.Database.ExecuteNonQueryAsync(FUNCTION);
        await m_studio.Database.ExecuteNonQueryAsync(PROCEDURE);

        await m_studio.Explorer.RefreshAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region What the tree offers

    [Test]
    public void ARoutineIsOfferedItsDefinitionAndItsRemovalTest()
    {
        Select("DiscountedTotal");

        var explorer = m_studio.Explorer;

        Assert.Multiple(() =>
        {
            Assert.That(explorer.ShowsViewDefinition, Is.True, "the catalogue holds its body");
            Assert.That(explorer.ShowsDrop, Is.True, "and the engine has DROP FUNCTION");

            Assert.That(explorer.CanViewDefinition, Is.True);
            Assert.That(explorer.CanDropObject, Is.True);

            // What a routine still does NOT have, so that this is not a licence to offer everything.
            Assert.That(explorer.ShowsRename, Is.False, "there is no ALTER FUNCTION in this language");
            Assert.That(explorer.ShowsEditData, Is.False);
            Assert.That(explorer.ShowsBrowseData, Is.False);
        });
    }

    [Test]
    public void TheTreeKnowsWhichOfTheTwoEachOneIsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Node("DiscountedTotal").IsFunction, Is.True);
            Assert.That(Node("ArchiveOld").IsFunction, Is.False);

            // A fact rather than a rendering: the label is what the fact is drawn as, not the other
            // way round.
            Assert.That(Node("DiscountedTotal").Detail, Does.StartWith("function"));
            Assert.That(Node("ArchiveOld").Detail, Is.EqualTo("procedure"));
        });
    }

    #endregion

    #region The definition runs back

    [Test]
    public async Task TheDefinitionOfAFunctionRunsBackIntoTheDatabaseTest()
    {
        await ItRunsBackAsync("DiscountedTotal", isFunction: true);
    }

    [Test]
    public async Task TheDefinitionOfAProcedureRunsBackIntoTheDatabaseTest()
    {
        await ItRunsBackAsync("ArchiveOld", isFunction: false);
    }

    /// <summary>
    /// A function's parameters and return type are part of it. A definition without them parses and
    /// creates a DIFFERENT routine, which is the failure this case exists to catch.
    /// </summary>
    [Test]
    public async Task TheDefinitionCarriesTheParametersAndTheReturnTypeTest()
    {
        var definition = await m_studio.Database.GetRoutineDefinitionAsync("DiscountedTotal");

        Assert.That(definition, Is.Not.Null);

        // The SIGNATURE, not the whole text: the parameter names also appear in the body, so
        // «the definition contains Amount» passes on a definition with no parameters at all -
        // measured, by removing them.
        var signature = definition![..definition.IndexOf("RETURNS", StringComparison.Ordinal)];

        Assert.Multiple(() =>
        {
            Assert.That(signature, Does.Contain("Amount"));
            Assert.That(signature, Does.Contain("Percent"));
            Assert.That(signature, Does.Contain("DECIMAL(18,2)"),
                "a parameter carries its type WITH its precision - DECIMAL alone restores a "
                + "routine that rounds differently from the one in the database");

            Assert.That(definition, Does.Contain("RETURNS DECIMAL"), "and the function its return type");
        });
    }

    #endregion

    #region Dropping one

    [Test]
    public async Task DroppingAFunctionTakesItOutOfTheDatabaseAndTheTreeTest()
    {
        Select("DiscountedTotal");

        m_studio.Confirmations.AllowDestructive = true;

        await StudioFixture.PressAsync(m_studio.Explorer.DropObjectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(Routines(), Does.Not.Contain("DiscountedTotal"));
            Assert.That(Walk().Any(node => node.Name == "DiscountedTotal"), Is.False,
                "and the tree was refreshed");
        });
    }

    [Test]
    public async Task DroppingAProcedureTakesItOutTooTest()
    {
        Select("ArchiveOld");

        m_studio.Confirmations.AllowDestructive = true;

        await StudioFixture.PressAsync(m_studio.Explorer.DropObjectCommand);

        Assert.That(Routines(), Does.Not.Contain("ArchiveOld"));
    }

    /// <summary>
    /// The other direction: a refused question leaves the routine alone. DROP PROCEDURE and DROP
    /// FUNCTION are different statements, and a menu that asks and drops anyway would be worse than
    /// one that never offered.
    /// </summary>
    [Test]
    public async Task ARefusedQuestionLeavesTheRoutineAloneTest()
    {
        Select("DiscountedTotal");

        m_studio.Confirmations.AllowDestructive = false;

        await StudioFixture.PressAsync(m_studio.Explorer.DropObjectCommand);

        Assert.That(Routines(), Does.Contain("DiscountedTotal"));
    }

    /// <summary>
    /// The line under the tree counts what the tree draws. It named five folders while the tree
    /// drew six, so a database whose only objects are routines was summarised as empty.
    /// </summary>
    [Test]
    public async Task TheSummaryCountsTheSixthFolderTest()
    {
        await m_studio.Explorer.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.MainWindow.StatusText, Does.Contain("2 routines"));

            // CONTROL: the same line, still naming what it named before.
            Assert.That(m_studio.MainWindow.StatusText, Does.Contain("1 trigger"));
        });
    }

    #endregion

    #region The dump

    [Test]
    public async Task ADumpCarriesTheRoutinesTest()
    {
        var script = await Studio.Services.DatabaseDump.WriteAsync(
            m_studio.Database, new Studio.Services.DumpOptions());

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("CREATE FUNCTION"), "a dump without them restores without them");
            Assert.That(script, Does.Contain("DiscountedTotal"));

            Assert.That(script, Does.Contain("CREATE PROCEDURE"));
            Assert.That(script, Does.Contain("ArchiveOld"));

            // CONTROL: the dump is the real one, with the objects that were already written.
            Assert.That(script, Does.Contain("CREATE TABLE"));
            Assert.That(script, Does.Contain("CREATE TRIGGER"));
        });
    }

    #endregion

    #region Tools

    private async Task ItRunsBackAsync(string name, bool isFunction)
    {
        var definition = await m_studio.Database.GetRoutineDefinitionAsync(name);

        Assert.That(definition, Is.Not.Null, $"{name} has a definition");

        await m_studio.Database.ExecuteNonQueryAsync(
            $"DROP {(isFunction ? "FUNCTION" : "PROCEDURE")} {name}");

        Assume.That(Routines(), Does.Not.Contain(name), "it is gone before the definition is run");

        await m_studio.Database.ExecuteNonQueryAsync(definition!);

        Assert.That(Routines(), Does.Contain(name),
            "the definition Studio wrote does not restore the routine:" + Environment.NewLine + definition);
    }

    private IReadOnlyList<string> Routines()
    {
        return m_studio.Database.GetRoutinesAsync().GetAwaiter().GetResult()
            .Select(routine => routine.Name)
            .ToList();
    }

    private DatabaseNode Node(string name)
    {
        var node = Walk().FirstOrDefault(candidate =>
            candidate.NodeType == DatabaseNodeType.Routine && candidate.Name == name);

        Assert.That(node, Is.Not.Null, $"the tree has a routine called {name}");

        return node!;
    }

    private void Select(string name)
    {
        m_studio.Explorer.SelectedNode = Node(name);
    }

    private IEnumerable<DatabaseNode> Walk()
    {
        return m_studio.Explorer.Nodes.SelectMany(Flatten);
    }

    private static IEnumerable<DatabaseNode> Flatten(DatabaseNode node)
    {
        yield return node;

        foreach (var child in node.Children.SelectMany(Flatten))
            yield return child;
    }

    #endregion
}
