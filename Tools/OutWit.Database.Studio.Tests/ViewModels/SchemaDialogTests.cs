using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The index dialog (WS-43, WS-44) and the trigger editor (WS-45), over a real database.
///
/// Both are about the same thing: offering exactly what the engine does, and saying what an option
/// costs. Every claim below was measured before it was written into the dialog, and the SQL each one
/// produces is executed here rather than compared with an expected string.
/// </summary>
[TestFixture]
public class SchemaDialogTests
{
    private StudioFixture m_fixture = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();
        // The dialogs act on the connection the tree is pointing at (WS-3), so it has to be the active
        // one - which it is in the application, and is not by default in a bare fixture.
        m_fixture.App.Connections.Active = m_fixture.Database;
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    #region The index dialog

    private async Task<CreateIndexViewModel> IndexDialogAsync(string table = "Orders")
    {
        var vm = new CreateIndexViewModel(m_fixture.App);

        await vm.LoadForTableAsync(table);

        return vm;
    }

    [Test]
    public async Task EveryShapeTheDialogCanProduceIsAcceptedByTheEngineAsync()
    {
        var refused = new List<string>();

        // One index per option the dialog offers, all of them together at the end - if any of these
        // stops parsing, an option in the dialog has become a button that fails.
        var drafts = new List<IndexDraft>
        {
            new() { Name = "IX_A", Table = "Orders", Columns = [new IndexColumn("Status")] },
            new() { Name = "IX_B", Table = "Orders", Columns = [new IndexColumn("Total", true)] },
            new()
            {
                Name = "IX_C", Table = "Orders",
                Columns = [new IndexColumn("Status"), new IndexColumn("Total", true)]
            },
            new()
            {
                Name = "IX_D", Table = "Orders", IsUnique = true,
                Columns = [new IndexColumn("Id")]
            },
            new()
            {
                Name = "IX_E", Table = "Orders", Columns = [new IndexColumn("Status")],
                FilterCondition = "Status <> 'archived'"
            },
            new()
            {
                Name = "IX_F", Table = "Orders", Columns = [new IndexColumn("Status")],
                IncludedColumns = ["Total"]
            },
            new() { Name = "IX_G", Table = "Customers", Columns = [new IndexColumn("LOWER(Name)")] },
            new()
            {
                Name = "IX_H", Table = "Orders", IsUnique = true,
                Columns = [new IndexColumn("Status"), new IndexColumn("Total", true)],
                IncludedColumns = ["CustomerId"], FilterCondition = "Total > 0"
            }
        };

        foreach (var draft in drafts)
        {
            var sql = DdlWriter.CreateIndex(draft);

            try
            {
                await m_fixture.Database.ExecuteNonQueryAsync(sql);
            }
            catch (Exception ex)
            {
                refused.Add($"{sql} -> {ex.Message.Split('\n')[0]}");
            }
        }

        Assert.That(refused, Is.Empty, string.Join("\n", refused));
    }

    [Test]
    public async Task TheDdlIsWrittenWhileTheDialogIsBeingFilledInAsync()
    {
        var vm = await IndexDialogAsync();

        vm.IndexName = "IX_Orders_Status";
        vm.AddColumnCommand.Execute("Status");

        Assert.That(vm.GeneratedDdl, Is.EqualTo("CREATE INDEX IX_Orders_Status ON Orders (Status);"));

        vm.ToggleDirectionCommand.Execute(vm.SelectedColumns[0]);
        Assert.That(vm.GeneratedDdl, Does.Contain("Status DESC"));

        vm.IsUnique = true;
        Assert.That(vm.GeneratedDdl, Does.StartWith("CREATE UNIQUE INDEX"));

        vm.AddIncludedCommand.Execute("Total");
        Assert.That(vm.GeneratedDdl, Does.Contain("INCLUDE (Total)"));

        vm.FilterCondition = "Total > 0";
        Assert.That(vm.GeneratedDdl, Does.Contain("WHERE Total > 0"));
    }

    /// <summary>
    /// Two of the options are stored by the engine and ignored by the planner. They are still offered -
    /// a database is not read only by Studio - and the dialog says so instead of hiding it.
    /// </summary>
    [Test]
    public async Task TheOptionsThatBuyNothingSaySoAsync()
    {
        var vm = await IndexDialogAsync();

        vm.AddColumnCommand.Execute("Status");
        Assert.That(vm.PlannerNote, Is.Null, "A plain index buys what it says it buys.");

        vm.FilterCondition = "Total > 0";
        Assert.That(vm.PlannerNote, Does.Contain("partial"));

        vm.ToggleDirectionCommand.Execute(vm.SelectedColumns[0]);
        Assert.That(vm.PlannerNote, Does.Contain("direction"));

        vm.AddColumnCommand.Execute("LOWER(Status)");
        Assert.That(vm.PlannerNote, Does.Contain("$expr0"));
    }

    [Test]
    public async Task TheKeyNoteIsAboutTheTableTheDialogWasOpenedOnAsync()
    {
        var withGenerator = await IndexDialogAsync("Customers");

        Assert.That(withGenerator.KeyNoteIsSevere, Is.False);
        Assert.That(withGenerator.KeyNote, Does.Contain("AUTOINCREMENT"));

        await m_fixture.Database.ExecuteNonQueryAsync("CREATE TABLE Items (Id GUID PRIMARY KEY, N VARCHAR(20))");

        var byHand = await IndexDialogAsync("Items");

        Assert.That(byHand.KeyNoteIsSevere, Is.True);
        Assert.That(byHand.KeyNote, Does.Contain("scans"));
    }

    [Test]
    public async Task CreatingTheIndexPutsItInTheDatabaseAsync()
    {
        var vm = await IndexDialogAsync();

        vm.IndexName = "IX_Orders_Status";
        vm.AddColumnCommand.Execute("Status");

        await StudioFixture.PressAsync(vm.CreateIndexCommand);

        var indexes = await m_fixture.Database.GetTableIndexesAsync("Orders");

        Assert.That(indexes.Select(i => i.Name), Does.Contain("IX_Orders_Status"));
    }

    #endregion

    #region The trigger editor

    private EditTriggerViewModel TriggerEditor(TriggerInfo? existing = null) =>
        new(m_fixture.App, m_fixture.Database, "Orders", existing);

    [Test]
    public void ABodyOutsideTheLanguageIsRefusedBeforeTheEngineSeesIt()
    {
        var vm = TriggerEditor();
        vm.Name = "TR_Test";
        vm.Body = "CREATE TABLE T2 (Id INTEGER);";

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsValid, Is.False);
            Assert.That(vm.Problems[0], Does.Contain("only SELECT, INSERT, UPDATE, DELETE and MERGE"));
        });
    }

    /// <summary>
    /// The one the design asks about by name: a BEFORE trigger that fills in a column. It does not
    /// parse on this engine, and the engine's own message for it names TRANSACTION, which explains
    /// nothing. The editor says what is actually wrong.
    /// </summary>
    [Test]
    public void AssigningToNewIsExplainedRatherThanLeftToTheParser()
    {
        var vm = TriggerEditor();
        vm.Name = "TR_Fill";
        vm.Body = "SET NEW.Status = 'new';";

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsValid, Is.False);
            Assert.That(vm.Problems[0], Does.Contain("NEW"));
            Assert.That(vm.Problems[0], Does.Contain("SET TRANSACTION"),
                "and why the engine's message says something else entirely.");
        });
    }

    [Test]
    public void AGoodBodyIsAcceptedAndTheConditionGetsItsBrackets()
    {
        var vm = TriggerEditor();
        vm.Name = "TR_Audit";
        vm.Body = "INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);";
        vm.Condition = "NEW.Total > 100";

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsValid, Is.True, string.Join("; ", vm.Problems));
            Assert.That(vm.GeneratedDdl, Does.Contain("WHEN (NEW.Total > 100)"),
                "Unbracketed, this is a parse error - which is why the editor writes the brackets.");
            Assert.That(vm.GeneratedDdl, Does.Contain("FOR EACH ROW"));
        });
    }

    [Test]
    public async Task WhatTheEditorWritesIsAcceptedByTheEngineAsync()
    {
        var vm = TriggerEditor();
        vm.Name = "TR_Audit2";
        vm.Body = "INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);";
        vm.Condition = "NEW.Total > 100";

        await StudioFixture.PressAsync(vm.SaveCommand);

        Assert.That(vm.ErrorMessage, Is.Null);

        var triggers = await m_fixture.Database.GetTableTriggersAsync("Orders");
        var saved = triggers.FirstOrDefault(t => t.Name == "TR_Audit2");

        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Condition, Does.Contain("NEW.Total"));

        // and it fires only when the condition holds - a trigger in the catalogue that never runs
        // would pass every check above.
        var before = await m_fixture.CountRowsAsync("OrdersAudit");

        await m_fixture.Database.ExecuteNonQueryAsync("INSERT INTO Orders (CustomerId, Total) VALUES (1, 5)");
        var afterSmall = await m_fixture.CountRowsAsync("OrdersAudit");

        await m_fixture.Database.ExecuteNonQueryAsync("INSERT INTO Orders (CustomerId, Total) VALUES (1, 500)");
        var afterBig = await m_fixture.CountRowsAsync("OrdersAudit");

        Assert.That(afterSmall, Is.EqualTo(before + 1), "the fixture's own trigger fires for both");
        Assert.That(afterBig, Is.EqualTo(afterSmall + 2), "and this one only for the second.");
    }

    /// <summary>
    /// A statement trigger is written by leaving FOR EACH ROW out: FOR EACH STATEMENT is a parse error
    /// on this engine.
    /// </summary>
    [Test]
    public async Task AStatementTriggerLeavesTheClauseOutAsync()
    {
        var vm = TriggerEditor();
        vm.Name = "TR_Statement";
        vm.Body = "INSERT INTO OrdersAudit (OrderId) VALUES (1);";
        vm.ForEachRow = false;

        Assert.That(vm.GeneratedDdl, Does.Not.Contain("FOR EACH"));

        await StudioFixture.PressAsync(vm.SaveCommand);

        Assert.That(vm.ErrorMessage, Is.Null);

        var triggers = await m_fixture.Database.GetTableTriggersAsync("Orders");

        Assert.That(triggers.First(t => t.Name == "TR_Statement").Orientation, Is.EqualTo("STATEMENT"),
            "and the catalogue agrees that is what it is.");
    }

    [Test]
    public async Task ReplacingATriggerIsADropAndACreateAndTheButtonSaysSoAsync()
    {
        var existing = (await m_fixture.Database.GetTableTriggersAsync("Orders"))
            .First(t => t.Name == "TR_Orders_Audit");

        var vm = TriggerEditor(existing);

        Assert.Multiple(() =>
        {
            Assert.That(vm.SaveText, Is.EqualTo("Drop and create"));
            Assert.That(vm.Body, Does.Contain("OrdersAudit"), "and it opens on the body it already has.");
            Assert.That(vm.BuildSql(), Does.StartWith("DROP TRIGGER"));
        });

        vm.Body = "INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);";

        await StudioFixture.PressAsync(vm.SaveCommand);

        Assert.That(vm.ErrorMessage, Is.Null);

        var triggers = await m_fixture.Database.GetTableTriggersAsync("Orders");

        Assert.That(triggers.Count(t => t.Name == "TR_Orders_Audit"), Is.EqualTo(1),
            "One trigger afterwards, not two and not none.");
    }

    /// <summary>
    /// Replacing a trigger has to CHANGE it. Found 2026-08-09 while measuring whether the new
    /// UPDATE OF case could fail: it could not, because the replacement never ran at all.
    /// </summary>
    /// <remarks>
    /// <c>SchemaChangeSet.ApplyAsync</c> ran <c>InPlaceStatements</c>, and a trigger replacement is
    /// categorised <c>DropCreate</c> - so its two statements were silently left out, the report came
    /// back empty, an empty report <b>is complete</b>, and the dialog said the trigger was replaced and
    /// closed. Every earlier case here asserted the trigger COUNT afterwards, which one trigger left
    /// untouched satisfies exactly as well as one replaced.
    /// </remarks>
    [Test]
    public async Task ReplacingATriggerActuallyReplacesItAsync()
    {
        var existing = (await m_fixture.Database.GetTableTriggersAsync("Orders"))
            .First(t => t.Name == "TR_Orders_Audit");

        var vm = TriggerEditor(existing);
        vm.Body = "INSERT INTO OrdersAudit (OrderId) VALUES (777);";

        await StudioFixture.PressAsync(vm.SaveCommand);

        Assert.That(vm.ErrorMessage, Is.Null);

        var replaced = (await m_fixture.Database.GetTableTriggersAsync("Orders"))
            .First(t => t.Name == "TR_Orders_Audit");

        Assert.That(replaced.Body, Does.Contain("777"),
            "the trigger in the database has to be the one the dialog wrote");
    }

    /// <summary>
    /// The column list of <c>UPDATE OF</c> is written, and only for an UPDATE trigger - the grammar
    /// allows <c>OF</c> nowhere else.
    /// </summary>
    [Test]
    public void TheWatchedColumnsAreWrittenForAnUpdateTriggerOnly()
    {
        var vm = TriggerEditor();
        vm.Name = "TR_Watch";
        vm.Body = "INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);";
        vm.UpdateColumnsText = "Total, Status";

        Assert.That(vm.IsUpdateEvent, Is.False, "the dialog opens on INSERT");
        Assert.That(vm.GeneratedDdl, Does.Not.Contain(" OF "),
            "and OF after INSERT does not parse, so a list typed there must not reach the SQL");

        vm.Event = "UPDATE";

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsUpdateEvent, Is.True, "which is what shows the field");
            Assert.That(vm.GeneratedDdl, Does.Contain("UPDATE OF Total, Status"));
        });
    }

    /// <summary>
    /// The case this field exists for, and the one that would lose a real behaviour without it: a
    /// trigger that watches one column is opened, saved unchanged, and must still watch that column.
    /// The save is a DROP and a CREATE, so anything the dialog does not carry is gone.
    /// </summary>
    [Test]
    public async Task ReplacingATriggerKeepsTheColumnsItWatchesAsync()
    {
        await m_fixture.Database.ExecuteNonQueryAsync(
            "CREATE TRIGGER TR_Orders_Total AFTER UPDATE OF Total ON Orders FOR EACH ROW "
            + "BEGIN INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id); END");

        var existing = (await m_fixture.Database.GetTableTriggersAsync("Orders"))
            .First(t => t.Name == "TR_Orders_Total");

        Assert.That(existing.UpdateColumns, Is.EqualTo(new[] { "Total" }),
            "the catalogue has to publish the list before the dialog can carry it");

        var vm = TriggerEditor(existing);

        Assert.That(vm.UpdateColumnsText, Is.EqualTo("Total"), "and the dialog opens on it");

        await StudioFixture.PressAsync(vm.SaveCommand);

        Assert.That(vm.ErrorMessage, Is.Null);

        var replaced = (await m_fixture.Database.GetTableTriggersAsync("Orders"))
            .First(t => t.Name == "TR_Orders_Total");

        Assert.That(replaced.UpdateColumns, Is.EqualTo(new[] { "Total" }),
            "and after the drop and create it still watches Total, not every column");

        // The catalogue is a proxy; this is the behaviour. A widened trigger would fire here.
        var before = await m_fixture.CountRowsAsync("OrdersAudit");

        await m_fixture.Database.ExecuteNonQueryAsync("UPDATE Orders SET Status = 'x' WHERE Id = 1");

        Assert.That(await m_fixture.CountRowsAsync("OrdersAudit"), Is.EqualTo(before),
            "an update of Status must not reach a trigger that watches Total");
    }

    #endregion
}
