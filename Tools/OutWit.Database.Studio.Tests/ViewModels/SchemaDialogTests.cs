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

    #endregion
}
