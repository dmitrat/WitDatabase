using NUnit.Framework;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The whole database as a WitSQL script (WS-51).
///
/// <para>
/// The claim a dump makes is that running it rebuilds the database, so the case that matters here
/// RUNS it - into a second, empty database, and then compares the rows. Reading the script and
/// agreeing that it looks right is what a dump with a wrong table order also passes.
/// </para>
/// </summary>
[TestFixture]
public class DatabaseDumpTests
{
    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync(withSchema: false);
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region The order

    /// <summary>
    /// A table comes after everything it references, so the script runs top to bottom. The fixture's
    /// order is deliberately the wrong way round.
    /// </summary>
    [Test]
    public void AReferencingTableComesAfterTheOneItReferencesTest()
    {
        var order = DatabaseDump.InDependencyOrder(
            ["Orders", "Customers"],
            new Dictionary<string, IReadOnlyList<string>> { ["Orders"] = ["Customers"] },
            out var cycle);

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new[] { "Customers", "Orders" }));
            Assert.That(cycle, Is.Empty);
        });
    }

    /// <summary>
    /// Two tables that reference each other cannot be ordered. The sort must not hang and must not
    /// LOSE either of them - a dump that silently dropped a table is a backup with a hole in it - so
    /// they are emitted anyway and named.
    /// </summary>
    [Test]
    public void ACycleIsReportedAndNoTableIsLostTest()
    {
        var order = DatabaseDump.InDependencyOrder(
            ["A", "B", "C"],
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["A"] = ["B"],
                ["B"] = ["A"]
            },
            out var cycle);

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EquivalentTo(new[] { "A", "B", "C" }), "every table is still there");
            Assert.That(cycle, Is.EquivalentTo(new[] { "A", "B" }), "and the pair that could not be ordered is named");
            Assert.That(order[0], Is.EqualTo("C"), "what CAN be ordered still is");
        });
    }

    /// <summary>
    /// A reference to a table that is not in the dump - which is what a partial selection produces -
    /// must not stop the one that references it from being written.
    /// </summary>
    [Test]
    public void AReferenceOutsideTheSelectionDoesNotBlockATableTest()
    {
        var order = DatabaseDump.InDependencyOrder(
            ["Orders"],
            new Dictionary<string, IReadOnlyList<string>> { ["Orders"] = ["Customers"] },
            out var cycle);

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new[] { "Orders" }));
            Assert.That(cycle, Is.Empty);
        });
    }

    #endregion

    #region And the script runs

    /// <summary>
    /// The measurement: dump a database with a foreign key, run the script into an EMPTY one, and read
    /// the rows back. This is what makes the order a fact rather than a claim - with the tables the
    /// wrong way round the CREATE TABLE for Orders is refused, and with the data in the wrong order the
    /// foreign key is.
    /// </summary>
    [Test]
    public async Task TheDumpRebuildsTheDatabaseAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER REFERENCES Customers(Id), Total DECIMAL(10,2))");
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Customers (Id, Name) VALUES (1, 'Northwind')");
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 1, 4812.50)");

        var script = await DatabaseDump.WriteAsync(m_studio.Database, new DumpOptions());

        var rebuilt = await m_studio.OpenAnotherAsync("rebuilt", withSchema: false);

        // Cut by the parser, which is what a query tab would do with the same text (stage 3).
        foreach (var statement in SqlScript.Split(script).Statements)
            await rebuilt.ExecuteNonQueryAsync(statement.Text);

        var customers = await rebuilt.ExecuteQueryAsync("SELECT Name FROM Customers");
        var orders = await rebuilt.ExecuteQueryAsync("SELECT Total FROM Orders");

        Assert.Multiple(() =>
        {
            Assert.That(customers.Data!.Rows[0][0]?.ToString(), Is.EqualTo("Northwind"));
            Assert.That(orders.Data!.Rows[0][0]?.ToString(), Is.EqualTo("4812.50"));
        });
    }

    /// <summary>
    /// CONTROL, and it was written as the opposite claim first. It asserted that a table referencing
    /// one that does not exist yet is REFUSED - and this engine <b>accepts</b> it. So the schema order
    /// is not what the dependency sort protects.
    ///
    /// <para>
    /// What it does protect is the DATA: an INSERT whose foreign key points at a row that is not there
    /// is refused, so the rows of the referenced table have to be written first. That is measured
    /// here, and it is why the sort orders the INSERT blocks as well as the CREATEs.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheOrderProtectsTheDataAndNotTheSchemaAsync()
    {
        var rebuilt = await m_studio.OpenAnotherAsync("order-probe", withSchema: false);

        // 1. A table referencing one that does not exist yet: accepted.
        await rebuilt.ExecuteNonQueryAsync(
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER REFERENCES Customers(Id))");

        await rebuilt.ExecuteNonQueryAsync("CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");

        // 2. A row whose key points at a row that is not there: refused.
        var refused = false;

        try
        {
            await rebuilt.ExecuteNonQueryAsync("INSERT INTO Orders (Id, CustomerId) VALUES (1, 99)");
        }
        catch
        {
            refused = true;
        }

        // 3. And the same INSERT once the referenced row exists: accepted. Without this the refusal
        //    above could be about anything.
        await rebuilt.ExecuteNonQueryAsync("INSERT INTO Customers (Id, Name) VALUES (99, 'there')");
        await rebuilt.ExecuteNonQueryAsync("INSERT INTO Orders (Id, CustomerId) VALUES (2, 99)");

        Assert.That(refused, Is.True,
            "the rows of a referenced table have to be written first - which is what the sort is for");
    }

    #endregion

    #region The objects that are not tables

    /// <summary>
    /// A TRIGGER survives the round trip, and the measurement is that it FIRES in the copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured red on 2026-08-08 against the unfixed code: the catalogue publishes a trigger's
    /// <c>ACTION_STATEMENT</c>, which is its BODY, and the dump wrote that verbatim - so the script
    /// ended with <c>INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);</c> as a statement of its own
    /// and the engine refused it with "Column 'Id' not found". There was no <c>CREATE TRIGGER</c>
    /// anywhere in the script.
    /// </para>
    /// <para>
    /// The other half of that measurement is worth keeping: <c>SqlScript.Split</c> is NOT implicated.
    /// A hand-written <c>CREATE TRIGGER … BEGIN … END;</c> with two statements in its body comes back
    /// from the splitter as ONE statement, and the engine accepts it - so the defect was entirely in
    /// what the definition contained. See <see cref="SqlScriptTests"/> for that case.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ATriggerFiresInTheRebuiltDatabaseAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY AUTOINCREMENT, Total DECIMAL(10,2))");
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE OrdersAudit (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderId INTEGER NOT NULL)");
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TRIGGER TR_Orders_Audit AFTER INSERT ON Orders FOR EACH ROW "
            + "BEGIN INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id); END");

        var rebuilt = await RunTheDumpIntoAnEmptyDatabaseAsync("trigger-target");

        // The trigger is only real if it does its work: insert into the COPY and read the audit table.
        await rebuilt.ExecuteNonQueryAsync("INSERT INTO Orders (Total) VALUES (10.00)");

        var audit = await rebuilt.ExecuteQueryAsync("SELECT OrderId FROM OrdersAudit");

        Assert.That(audit.Data!.Rows, Has.Count.EqualTo(1),
            "the trigger has to be in the copy and fire there - a dump that cannot rebuild a trigger "
            + "is a backup that quietly loses one");
    }

    /// <summary>
    /// A VIEW survives it too, and this is the half that failed in SILENCE.
    /// </summary>
    /// <remarks>
    /// The same defect one object along: <c>VIEW_DEFINITION</c> is the view's query, and the dump wrote
    /// it verbatim - so the script carried a bare <c>SELECT Id, CustomerId, Total FROM Orders WHERE …;</c>
    /// with no <c>CREATE VIEW … AS</c> in front of it. Unlike the trigger, that statement <b>runs</b>:
    /// the restore reported success on every line and the view was simply not there. Found in the same
    /// measurement, by reading the whole script rather than the line that failed.
    /// </remarks>
    [Test]
    public async Task AViewIsInTheRebuiltDatabaseAndAnswersAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Total DECIMAL(10,2), Status VARCHAR(20))");
        await m_studio.Database.ExecuteNonQueryAsync(
            "INSERT INTO Orders (Id, Total, Status) VALUES (1, 10.00, 'new')");
        await m_studio.Database.ExecuteNonQueryAsync(
            "INSERT INTO Orders (Id, Total, Status) VALUES (2, 20.00, 'archived')");
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE VIEW ActiveOrders AS SELECT Id, Total FROM Orders WHERE Status <> 'archived'");

        var rebuilt = await RunTheDumpIntoAnEmptyDatabaseAsync("view-target");

        var rows = await rebuilt.ExecuteQueryAsync("SELECT Id FROM ActiveOrders");

        Assert.That(rows.Data!.Rows, Has.Count.EqualTo(1),
            "the view has to be in the copy and answer there");
    }

    /// <summary>
    /// Every shape of trigger this engine accepts, dumped and rebuilt, compared through the catalogue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parts a <c>CREATE TRIGGER</c> is assembled from are separate catalogue columns, and dropping
    /// any one of them produces a trigger that is still a trigger - one that fires at the wrong time,
    /// on the wrong event, for every row instead of once, or with its <c>WHEN</c> gone. A single case
    /// over one shape cannot see that; this walks all of them and compares the row the target
    /// publishes against the row the source published.
    /// </para>
    /// <para>
    /// <b>What this instrument cannot see, measured rather than assumed:</b> <c>UPDATE OF V</c> is
    /// accepted, stored, and published by <c>INFORMATION_SCHEMA.TRIGGERS</c> as a plain <c>UPDATE</c> -
    /// there is no column for the column list - so the dump widens it to every column and both sides of
    /// this comparison agree. It changes no behaviour today because <b>the engine does not honour
    /// <c>UPDATE OF</c> either</b>: a trigger declared on one column fires on an update of another.
    /// That is pinned in the engine's own suite, and when it is fixed the dump's widening becomes a
    /// real loss that this fixture still will not see.
    /// </para>
    /// </remarks>
    [TestCase("AFTER INSERT ON Src FOR EACH ROW", TestName = "TriggerShape_AfterInsertPerRow")]
    [TestCase("BEFORE DELETE ON Src FOR EACH ROW", TestName = "TriggerShape_BeforeDelete")]
    [TestCase("INSTEAD OF INSERT ON Src FOR EACH ROW", TestName = "TriggerShape_InsteadOf")]
    [TestCase("AFTER UPDATE ON Src FOR EACH ROW", TestName = "TriggerShape_AfterUpdate")]
    [TestCase("AFTER INSERT ON Src", TestName = "TriggerShape_PerStatement")]
    [TestCase("AFTER INSERT ON Src FOR EACH ROW WHEN (NEW.V > 10)", TestName = "TriggerShape_WithWhen")]
    public async Task ATriggerKeepsItsShapeThroughTheDumpAsync(string header)
    {
        const string body = "BEGIN INSERT INTO Log (V) VALUES (1); INSERT INTO Log (Note) VALUES ('a;b'); END";

        await BuildTriggerFixtureAsync(m_studio.Database);
        await m_studio.Database.ExecuteNonQueryAsync($"CREATE TRIGGER TShape {header} {body}");

        var expected = await CatalogueRowAsync(m_studio.Database);

        var rebuilt = await RunTheDumpIntoAnEmptyDatabaseAsync($"shape-{header.GetHashCode():x}");

        Assert.That(await CatalogueRowAsync(rebuilt), Is.EqualTo(expected),
            "the rebuilt trigger has to be the same trigger, part for part");
    }

    private static async Task BuildTriggerFixtureAsync(IDatabaseSession session)
    {
        await session.ExecuteNonQueryAsync("CREATE TABLE Src (Id INTEGER PRIMARY KEY AUTOINCREMENT, V INTEGER)");
        await session.ExecuteNonQueryAsync(
            "CREATE TABLE Log (Id INTEGER PRIMARY KEY AUTOINCREMENT, V INTEGER, Note VARCHAR(50))");
    }

    /// <summary>
    /// The trigger as the catalogue publishes it: every part a CREATE TRIGGER is built from.
    /// </summary>
    private static async Task<string> CatalogueRowAsync(IDatabaseSession session)
    {
        var result = await session.ExecuteQueryAsync(
            "SELECT TRIGGER_NAME, ACTION_TIMING, EVENT_MANIPULATION, EVENT_OBJECT_TABLE, "
            + "ACTION_ORIENTATION, ACTION_CONDITION, ACTION_STATEMENT FROM INFORMATION_SCHEMA.TRIGGERS "
            + "WHERE TRIGGER_NAME = 'TShape'");

        if (result.Data == null || result.Data.Rows.Count == 0)
            return "<the trigger is not in this database at all>";

        return string.Join(" | ", result.Data.Rows[0].ItemArray.Select(value => value?.ToString() ?? "<null>"));
    }

    /// <summary>
    /// What the dump claims to be, done: a second, empty database with the script run into it, one
    /// statement at a time, the way a query tab would run it.
    /// </summary>
    private async Task<IDatabaseSession> RunTheDumpIntoAnEmptyDatabaseAsync(string name)
    {
        var script = await DatabaseDump.WriteAsync(m_studio.Database, new DumpOptions());

        var rebuilt = await m_studio.OpenAnotherAsync(name, withSchema: false);

        foreach (var statement in SqlScript.Split(script).Statements)
            await rebuilt.ExecuteNonQueryAsync(statement.Text);

        return rebuilt;
    }

    #endregion

    #region What it leaves out, and says so

    [Test]
    public async Task SchemaOnlyWritesNoRowsAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync("CREATE TABLE T (Id INTEGER PRIMARY KEY, V VARCHAR(10))");
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO T (Id, V) VALUES (1, 'one')");

        var script = await DatabaseDump.WriteAsync(m_studio.Database, new DumpOptions(Data: false));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("CREATE TABLE"));
            Assert.That(script, Does.Not.Contain("INSERT INTO"));
        });
    }

    [Test]
    public async Task DataOnlyWritesNoSchemaAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync("CREATE TABLE T (Id INTEGER PRIMARY KEY, V VARCHAR(10))");
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO T (Id, V) VALUES (1, 'one')");

        var script = await DatabaseDump.WriteAsync(m_studio.Database, new DumpOptions(Schema: false));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Not.Contain("CREATE TABLE"));
            Assert.That(script, Does.Contain("INSERT INTO"));
        });
    }

    /// <summary>
    /// A view whose body the catalogue cannot render comes back with a NULL definition - the phase-8
    /// rule working as designed, the renderer refusing to report a rendering that lost something. The
    /// dump NAMES it rather than dropping it, because a dump that quietly loses an object is worse
    /// than one that says it could not write it.
    /// </summary>
    [Test]
    public async Task AViewTheCatalogueCannotRenderIsNamedRatherThanDroppedAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync("CREATE TABLE A (Id INTEGER PRIMARY KEY)");
        await m_studio.Database.ExecuteNonQueryAsync("CREATE TABLE B (Id INTEGER PRIMARY KEY)");
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE VIEW Both AS SELECT Id FROM A UNION SELECT Id FROM B");

        var script = await DatabaseDump.WriteAsync(m_studio.Database, new DumpOptions());

        Assert.That(script, Does.Contain("Both"),
            "either as a CREATE VIEW or as a line saying it could not be written - never as nothing");
    }

    /// <summary>
    /// And a dump says what it is, because the difference from a byte copy is the thing people get
    /// wrong: this is text that must be executed again, a copy keeps the pages and the encryption.
    /// </summary>
    [Test]
    public async Task TheScriptSaysWhatItIsAsync()
    {
        var script = await DatabaseDump.WriteAsync(m_studio.Database, new DumpOptions());

        Assert.That(script, Does.Contain("byte copy"));
    }

    #endregion
}
