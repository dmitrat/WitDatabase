using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// One session of schema work, in order, against a real database - the stage's readiness criterion
/// written as a test, the way stage 6 did it for the query workspace.
///
/// <b>The criterion:</b> every edit is text the user saw before it ran; nothing the engine will refuse
/// is offered; and when a sequence stops, the report names what is already in the database.
///
/// Each case below is one step of that: the DDL appears while the edit is still being decided, the row
/// says how it will be carried out before Apply, an edit the engine cannot do is not written, and an
/// interrupted set says what landed.
/// </summary>
[TestFixture]
public class SchemaDesignerTests
{
    private StudioFixture m_fixture = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    private Task<StructureTabViewModel> OpenAsync(string table = "Orders") =>
        m_fixture.Workspace.OpenStructureTabAsync(m_fixture.Database, table, DatabaseNodeType.Table);

    #region The DDL section

    /// <summary>
    /// A table's DDL section shows the table's DDL, and says nothing about views.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both halves were wrong, and both were found by opening the section.</b> Driven on
    /// 2026-08-11: the DDL tab of <c>AspNetRoles</c> was <b>empty</b>, and under the empty space sat
    /// a paragraph explaining that «Каталог не может показать тело этого представления — для UNION и
    /// для подзапроса оно возвращается пустым» - on a table.
    /// </para>
    /// <para>
    /// The first is the silent-computed-property shape this phase has now met three times:
    /// <c>FullDdl</c> is computed from <c>TableDdl</c> and <c>PendingSql</c>, the markup binds to it,
    /// and <c>TableDdl</c> is assigned after an <c>await</c> - so the section was bound to a value
    /// that arrived later and nothing said it had. The second is a missing guard: the note was shown
    /// on <c>!CanEditView</c>, which is false for every table that ever existed.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ATablesDdlSectionShowsItsDdlAndSaysNothingAboutViewsAsync()
    {
        var tab = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tab.TableDdl, Is.Not.Empty,
                "CONTROL: the catalogue gave the tab a definition, so the next assertion is about "
                + "what the section READS rather than about the load");

            Assert.That(tab.FullDdl, Does.Contain("CREATE TABLE").And.Contain("Orders"),
                "the DDL section is bound to FullDdl and it computes the wrong thing");

            Assert.That(tab.ShowsViewNote, Is.False,
                "a table carries the note about a view whose body the catalogue could not return");
        });

        // The value being right is not the point and asserting it would have passed with the defect:
        // FullDdl computes correctly whenever it is ASKED, and the section is bound rather than asked.
        // What was missing is the announcement, so that is what this asserts - the same shape as the
        // error underline earlier in this phase.
        var announced = new List<string>();
        tab.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? string.Empty);

        await StudioFixture.PressAsync(tab.RefreshCommand);

        Assert.Multiple(() =>
        {
            Assert.That(announced, Does.Contain(nameof(tab.TableDdl)),
                "CONTROL: the reload has to move the definition, or the next assertion is vacuous");

            Assert.That(announced, Does.Contain(nameof(tab.FullDdl)),
                "the DDL arrives and the section bound to FullDdl is never told");
        });

        // And the markup has to READ that property. Asserting the ViewModel alone would have passed
        // with the defect in place - the note was bound to !CanEditView, which is false for every
        // table, and no property of the tab would have been wrong. Sabotage caught this: putting the
        // old binding back left the case above green.
        var markup = File.ReadAllText(Path.Combine(StudioFolder(), "Views", "Workspace", "StructureView.axaml"));

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("IsVisible=\"{Binding ShowsViewNote}\""),
                "the view note is not bound to the property that knows whether it applies");

            Assert.That(markup, Does.Not.Contain("IsVisible=\"{Binding !CanEditView}\""),
                "the note is back on a negation that is true for every table");
        });
    }

    private static string StudioFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion

    #region The trigger editor is reachable

    /// <summary>
    /// The structure tab opens the trigger editor - on an existing trigger and on a new one.
    /// </summary>
    /// <remarks>
    /// <b>Nothing in the application called `ShowEditTriggerAsync` until 2026-08-09.</b> The dialog, its
    /// window and six cases driving its ViewModel had been shipping since stage 8 with no command
    /// anywhere that opened it - found by looking for it in the running Studio. This case is the one
    /// that would have said so: it asserts a window was put in front of a person, which is what the
    /// scripted dialog service is for.
    /// </remarks>
    [Test]
    public async Task TheTriggerEditorCanBeOpenedFromTheStructureTabAsync()
    {
        var dialogs = new Helpers.ScriptedDialogService();
        m_fixture.App.Dialogs = dialogs;

        var tab = await OpenAsync();
        var existing = tab.Triggers.First(t => t.Name == "TR_Orders_Audit");

        await StudioFixture.PressAsync(tab.EditTriggerCommand, existing);

        Assert.That(dialogs.LastTrigger, Is.Not.Null, "the edit button has to open the editor");
        Assert.That(dialogs.LastTrigger!.Existing?.Name, Is.EqualTo("TR_Orders_Audit"),
            "and open it on the trigger the button belongs to");

        await StudioFixture.PressAsync(tab.CreateTriggerCommand);

        Assert.That(dialogs.LastTrigger!.Existing, Is.Null, "and the other button opens it on a new one");
        Assert.That(dialogs.LastTrigger.Table, Is.EqualTo("Orders"), "for the table the tab is about");
    }

    /// <summary>
    /// The line under a trigger's name is SQL, and it has to include the <c>UPDATE OF</c> clause: a
    /// trigger watching two columns of ten reads exactly like one watching all ten without it.
    /// </summary>
    [Test]
    public async Task TheTriggerRowSaysWhichColumnsItWatchesAsync()
    {
        await m_fixture.Database.ExecuteNonQueryAsync(
            "CREATE TRIGGER TR_Orders_Total AFTER UPDATE OF Total ON Orders FOR EACH ROW "
            + "BEGIN INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id); END");

        var tab = await OpenAsync();

        Assert.That(tab.Triggers.First(t => t.Name == "TR_Orders_Total").UpdateColumnsClause,
            Is.EqualTo("OF Total"));

        Assert.That(tab.Triggers.First(t => t.Name == "TR_Orders_Audit").UpdateColumnsClause,
            Is.Empty, "and a trigger that watches everything says nothing, as the SQL does");
    }

    #endregion

    #region 1. What is on screen

    [Test]
    public async Task TheTabOpensOnTheColumnsWithTheObjectAlreadyReadAsync()
    {
        var tab = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tab.SelectedSection, Is.EqualTo(StructureSection.Columns));
            Assert.That(tab.Columns.Select(c => c.Name),
                Is.EquivalentTo(new[] { "Id", "CustomerId", "Total", "Status" }));
            Assert.That(tab.Indexes.Select(i => i.Name), Does.Contain("IX_Orders_CustomerId"));
            Assert.That(tab.Triggers.Select(t => t.Name), Does.Contain("TR_Orders_Audit"));
            Assert.That(tab.Constraints.Select(c => c.Type), Does.Contain("PRIMARY KEY"));
            Assert.That(tab.TableDdl, Does.StartWith("CREATE TABLE"), "WS-38: the DDL is there from the start.");
            Assert.That(tab.HasPending, Is.False);
        });
    }

    [Test]
    public async Task AForeignKeyIsShownOnTheColumnItIsOnAsync()
    {
        var tab = await OpenAsync();
        var column = tab.Columns.First(c => c.Name == "CustomerId");

        Assert.That(column.ReferencesTable, Is.EqualTo("Customers"));
        Assert.That(column.ReferencesColumn, Is.EqualTo("Id"));
    }

    #endregion

    #region 2. An edit becomes text before it becomes a change (WS-38)

    [Test]
    public async Task AddingAColumnPutsItsStatementInThePanelAtOnceAsync()
    {
        var tab = await OpenAsync();

        tab.AddColumnCommand.Execute(null);

        var draft = tab.Columns.Last();
        draft.Name = "ShippedAt";
        draft.DataType = "DATETIME";
        draft.MaxLength = null;

        Assert.Multiple(() =>
        {
            Assert.That(tab.PendingSql, Does.Contain("ALTER TABLE Orders ADD COLUMN ShippedAt DATETIME"));
            Assert.That(tab.PendingCount, Is.EqualTo(1));
            Assert.That(tab.FullDdl, Does.Contain("pending"),
                "The DDL section shows the object as it is AND what is about to happen to it.");
        });
    }

    [Test]
    public async Task ChangingADefaultIsOneAlterAndSaysSoInTheRowAsync()
    {
        var tab = await OpenAsync();

        tab.Columns.First(c => c.Name == "Status").DefaultValue = "'pending'";

        var row = tab.Columns.First(c => c.Name == "Status");

        Assert.Multiple(() =>
        {
            // The CATEGORY is the claim; the words are how it is said in English. Both, because the
            // row used to carry only the words and the designer read them back to find the category.
            Assert.That(row.MarkerCategory, Is.EqualTo(SchemaEditCategory.InPlace),
                "WS-39: the category is in the row.");
            Assert.That(row.Marker, Is.EqualTo("in place"));
            Assert.That(row.MarkerReason, Does.Contain("rows are not touched"));
            Assert.That(tab.PendingSql, Does.Contain("ALTER COLUMN Status SET DEFAULT 'pending'"));
            Assert.That(tab.NeedsRebuild, Is.False);
        });
    }

    /// <summary>
    /// The row's marker follows the interface language, and the heaviest edit still wins after it has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things at once, and the second is the one that had a defect in it. The marker is text this
    /// ViewModel BUILDS, so it does not follow a language change the way a <c>DynamicResource</c> caption
    /// does - a tab left open across a switch kept saying "rebuild" over a Russian interface.
    /// </para>
    /// <para>
    /// The first half is red without the fix: with the subscription removed the marker still reads
    /// "rebuild" after the switch. <b>The second half is not, and it is worth saying why rather than
    /// implying otherwise.</b> The designer used to work out which category a row was already in by
    /// reading its marker WORD back - <c>"rebuild" =&gt; Rebuild</c>, a comparison against English - so
    /// in any other language every row answered <c>InPlace</c>. Measured by putting that code back:
    /// this case stays GREEN. The misread degrades to the LOWEST category, and the only categories a
    /// column row can carry are <c>InPlace</c> and <c>Rebuild</c>, so the <c>&gt;</c> comparison it
    /// feeds never changes its answer. It would have bitten the day a column edit landed in
    /// <c>DropCreate</c>. The row carries the category as a value now because a decision should not be
    /// taken by parsing a caption, not because a test could see it.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheMarkerFollowsTheLanguageAndTheHeaviestEditStillWinsAsync()
    {
        var tab = await OpenAsync();

        var total = tab.Columns.First(c => c.Name == "Total");

        // A rebuild first, then an in-place edit on the SAME row: the heavier one has to survive.
        total.NumericPrecision = 20;
        total.DefaultValue = "0";

        Assert.That(total.MarkerCategory, Is.EqualTo(SchemaEditCategory.Rebuild),
            "the lighter edit must not take the row's marker off the heavier one");

        var english = total.Marker;

        m_fixture.App.Localization.SetLanguage("ru");

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(total.Marker, Is.Not.EqualTo(english),
                    "the marker is built text and has to be rebuilt when the language changes");
                Assert.That(total.Marker, Is.EqualTo(m_fixture.App.Localization["Schema.Marker.Rebuild"]));
                Assert.That(total.MarkerCategory, Is.EqualTo(SchemaEditCategory.Rebuild),
                    "and the category is a value, so it does not depend on the language at all");
            });
        }
        finally
        {
            m_fixture.App.Localization.SetLanguage("en");
        }
    }

    /// <summary>
    /// WS-40 and WS-39 together: a type change is editable, and the row says immediately that it costs
    /// a rebuild - before Apply is pressed, which is the whole point.
    /// </summary>
    [Test]
    public async Task ChangingATypeIsMarkedAsARebuildBeforeApplyIsPressedAsync()
    {
        var tab = await OpenAsync();

        var total = tab.Columns.First(c => c.Name == "Total");
        total.NumericPrecision = 20;
        total.NumericScale = 4;

        Assert.Multiple(() =>
        {
            Assert.That(total.Marker, Is.EqualTo("rebuild"));
            Assert.That(total.MarkerReason, Does.Contain("without a word"),
                "and why: a value that will not convert is replaced without a word.");
            Assert.That(tab.NeedsRebuild, Is.True);
            Assert.That(tab.CanApply, Is.False, "Apply alone cannot carry it out.");
            Assert.That(tab.PendingSql, Does.Contain("--"),
                "and the panel shows a comment rather than a statement, because there is no statement " +
                "that would do this.");
        });
    }

    [Test]
    public async Task DroppingAColumnDropsTheIndexOnItFirstAsync()
    {
        var tab = await OpenAsync();

        tab.DeleteColumnCommand.Execute(tab.Columns.First(c => c.Name == "CustomerId"));

        var statements = tab.Pending!.InPlaceStatements;

        Assert.Multiple(() =>
        {
            Assert.That(statements, Has.Count.EqualTo(2));
            Assert.That(statements[0], Does.Contain("DROP INDEX IX_Orders_CustomerId"),
                "The index goes first: DROP COLUMN leaves it behind, naming a column that is gone.");
            Assert.That(statements[1], Does.Contain("DROP COLUMN CustomerId"));
            Assert.That(tab.Pending.Edits[0].Description, Does.Contain("1 index"));
        });
    }

    #endregion

    #region 3. What Studio will not write

    /// <summary>
    /// The one place the designer is stricter than the engine, and the refusal is not an error: nothing
    /// has been attempted.
    /// </summary>
    [Test]
    public async Task ANotNullColumnWithNoDefaultIsRefusedOnATableWithRowsAsync()
    {
        var tab = await OpenAsync();

        tab.AddColumnCommand.Execute(null);

        var draft = tab.Columns.Last();
        draft.Name = "Required";
        draft.DataType = "INTEGER";
        draft.MaxLength = null;
        draft.IsNullable = false;

        Assert.Multiple(() =>
        {
            Assert.That(tab.Refusals, Is.Not.Empty);
            Assert.That(tab.Refusals[0], Does.Contain("DEFAULT"));
            Assert.That(tab.PendingSql, Does.Not.Contain("Required"),
                "and the statement is not written at all.");
            Assert.That(tab.CanApply, Is.False);
        });
    }

    [Test]
    public async Task TheSameColumnWithADefaultIsWrittenAsync()
    {
        // The control: it is the missing DEFAULT that is refused, not NOT NULL.
        var tab = await OpenAsync();

        tab.AddColumnCommand.Execute(null);

        var draft = tab.Columns.Last();
        draft.Name = "Required";
        draft.DataType = "INTEGER";
        draft.MaxLength = null;
        draft.IsNullable = false;
        draft.DefaultValue = "0";

        Assert.Multiple(() =>
        {
            Assert.That(tab.Refusals, Is.Empty);
            Assert.That(tab.PendingSql, Does.Contain("ADD COLUMN Required INTEGER NOT NULL DEFAULT 0"));
            Assert.That(tab.CanApply, Is.True);
        });
    }

    [Test]
    public async Task OnAnEmptyTableNothingIsRefusedAsync()
    {
        await m_fixture.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Empty (Id INTEGER PRIMARY KEY AUTOINCREMENT, A VARCHAR(10))");

        var tab = await OpenAsync("Empty");

        tab.AddColumnCommand.Execute(null);

        var draft = tab.Columns.Last();
        draft.Name = "Required";
        draft.DataType = "INTEGER";
        draft.MaxLength = null;
        draft.IsNullable = false;

        Assert.That(tab.Refusals, Is.Empty, "The rule is about the rows, not about the statement.");
    }

    #endregion

    #region 4. Applying, and the report (WS-42)

    [Test]
    public async Task ApplyingPutsTheChangeInTheDatabaseAsync()
    {
        var tab = await OpenAsync();

        tab.AddColumnCommand.Execute(null);

        var draft = tab.Columns.Last();
        draft.Name = "ShippedAt";
        draft.DataType = "DATETIME";
        draft.MaxLength = null;

        await StudioFixture.PressAsync(tab.ApplyCommand);

        Assert.That(tab.ApplyReport!.IsComplete, Is.True, tab.ApplyReport.ErrorMessage);
        Assert.That(tab.ApplyReport.Summary, Is.EqualTo("1 change applied"));

        // Read it back from the database, not from the ViewModel that asked for it.
        var columns = await m_fixture.Database.GetColumnsAsync("Orders");

        Assert.That(columns.Select(c => c.Name), Does.Contain("ShippedAt"));
        Assert.That(tab.HasPending, Is.False, "and the tab has reloaded, so nothing is pending any more.");
    }

    /// <summary>
    /// <b>The Apply button and the executor ask one question, and until 2026-08-09 they asked two.</b>
    /// <c>ApplyAsync</c> ran only the in-place statements - a silent no-op for a whole category, fixed
    /// with issue 12 - and the structure tab's gate asked for <c>InPlace.Count > 0</c> in the same
    /// direction, so a change set made only of <c>DropCreate</c> edits would have left Apply grey in
    /// front of statements that were ready to run.
    ///
    /// <para>
    /// It is measured HERE rather than through the tab because the designer produces no such edit
    /// today - the trap is what a category added later would walk into - so this case pins the
    /// reading that made the gate wrong, and <c>CanApply</c> now asks
    /// <see cref="SchemaChangeSet.HasSomethingToRun"/>, which is the same property
    /// <c>ApplyAsync</c> returns on.
    /// </para>
    /// </summary>
    [Test]
    public void ASetOfNothingButDropCreateStillHasSomethingToRunTest()
    {
        var set = new SchemaChangeSet("Orders");

        set.Add(new SchemaEdit
        {
            Kind = SchemaEditKind.ReplaceTriggerBody,
            Table = "Orders",
            Description = "replace trigger TR_Orders_Audit",
            Statements =
            [
                "DROP TRIGGER TR_Orders_Audit",
                "CREATE TRIGGER TR_Orders_Audit AFTER INSERT ON Orders FOR EACH ROW BEGIN SELECT 1; END"
            ]
        });

        Assert.Multiple(() =>
        {
            Assert.That(set.InPlace, Is.Empty,
                "the reading the old gate asked for, and it is empty for a whole category");
            Assert.That(set.HasSomethingToRun, Is.True);
            Assert.That(set.ApplicableStatements, Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// CONTROL for the case above: a rebuild carries no statements, so there is nothing to run and the
    /// button stays grey. Without it, "has something to run" would be satisfied by a property that is
    /// always true.
    /// </summary>
    [Test]
    public void ASetOfNothingButARebuildHasNothingToRunTest()
    {
        var set = new SchemaChangeSet("Orders");

        set.Add(new SchemaEdit
        {
            Kind = SchemaEditKind.ChangeColumnType,
            Table = "Orders",
            Column = "Total",
            Description = "change Total to DECIMAL(20,4)",
            Statements = []
        });

        Assert.That(set.HasSomethingToRun, Is.False);
    }

    /// <summary>
    /// Dropping a key column is refused by the engine, and Studio knows it will be - so the row says
    /// "rebuild" while the user is still deciding, and Apply never sends a statement that cannot work.
    /// </summary>
    [Test]
    public async Task DroppingAKeyColumnIsMarkedARebuildRatherThanTriedAsync()
    {
        var tab = await OpenAsync();

        tab.DeleteColumnCommand.Execute(tab.Columns.First(c => c.Name == "Id"));

        Assert.Multiple(() =>
        {
            Assert.That(tab.Columns.First(c => c.Name == "Id").Marker, Is.EqualTo("rebuild"));
            Assert.That(tab.NeedsRebuild, Is.True);
            Assert.That(tab.Pending!.InPlaceStatements, Is.Empty,
                "Nothing is sent: the engine would refuse it and Studio already knows that.");
            Assert.That(tab.CanApply, Is.False);
            Assert.That(tab.CanRebuild, Is.True, "and the way forward is offered instead.");
        });
    }

    /// <summary>
    /// The heart of WS-42. Three statements, the third refused by the engine for a reason Studio cannot
    /// know in advance - Email holds a NULL, so it cannot be made NOT NULL. The first two are in the
    /// database afterwards and the report says so. There is no rollback to hide behind: measured, DDL
    /// survives one.
    /// </summary>
    [Test]
    public async Task AnInterruptedSetNamesWhatIsAlreadyInTheDatabaseAsync()
    {
        var tab = await OpenAsync("Customers");

        tab.AddColumnCommand.Execute(null);
        var added = tab.Columns.Last();
        added.Name = "Tier";
        added.DataType = "VARCHAR";
        added.MaxLength = 10;

        tab.Columns.First(c => c.Name == "Name").DefaultValue = "'unknown'";

        // The third. One of the fixture's three customers has no email.
        tab.Columns.First(c => c.Name == "Email").IsNullable = false;

        await StudioFixture.PressAsync(tab.ApplyCommand);

        var report = tab.ApplyReport!;

        Assert.Multiple(async () =>
        {
            Assert.That(report.IsComplete, Is.False);
            Assert.That(report.IsPartial, Is.True);
            Assert.That(report.AppliedCount, Is.EqualTo(2));
            Assert.That(report.Summary, Is.EqualTo("Applied 2 of 3"));
            Assert.That(report.Failure!.ErrorMessage, Does.Contain("NULL"));

            var columns = await m_fixture.Database.GetColumnsAsync("Customers");

            Assert.That(columns.Select(c => c.Name), Does.Contain("Tier"),
                "The two that ran are really in the database - nothing took them back.");
            Assert.That(columns.First(c => c.Name == "Name").DefaultValue, Does.Contain("unknown"));
            Assert.That(columns.First(c => c.Name == "Email").IsNullable, Is.True,
                "and the third really did not run.");
        });
    }

    [Test]
    public async Task AStatementAfterTheFailureIsMarkedNotReachedAsync()
    {
        var tab = await OpenAsync();

        // A column named after one that already exists: the engine refuses the ADD, and additions come
        // before property changes, so the statement behind it never runs.
        tab.AddColumnCommand.Execute(null);
        var clash = tab.Columns.Last();
        clash.Name = "Total";
        clash.DataType = "INTEGER";
        clash.MaxLength = null;

        tab.Columns.First(c => c.Name == "Status").DefaultValue = "'pending'";

        await StudioFixture.PressAsync(tab.ApplyCommand);

        var report = tab.ApplyReport!;

        Assert.Multiple(() =>
        {
            Assert.That(report.Outcomes[0].Outcome, Is.EqualTo(DdlOutcome.Failed));
            Assert.That(report.Outcomes[1].Outcome, Is.EqualTo(DdlOutcome.NotReached),
                "\"what did not happen\" is half the answer after an interrupted sequence.");
            Assert.That(report.AppliedCount, Is.Zero);
        });
    }

    [Test]
    public async Task RevertThrowsTheEditsAwayAsync()
    {
        var tab = await OpenAsync();

        tab.Columns.First(c => c.Name == "Status").DefaultValue = "'pending'";
        Assert.That(tab.HasPending, Is.True);

        await StudioFixture.PressAsync(tab.RefreshCommand);

        Assert.Multiple(async () =>
        {
            Assert.That(tab.HasPending, Is.False);

            var columns = await m_fixture.Database.GetColumnsAsync("Orders");

            Assert.That(columns.First(c => c.Name == "Status").DefaultValue, Does.Not.Contain("pending"),
                "and nothing reached the database.");
        });
    }

    #endregion

    #region 5. The key, in three states (WS-44)

    [Test]
    public async Task AnAutoincrementKeyIsToldNotToWorryAsync()
    {
        var tab = await OpenAsync("Customers");

        Assert.That(tab.KeyWarningIsSevere, Is.False);
        Assert.That(tab.KeyWarning, Does.Contain("AUTOINCREMENT"));
    }

    [Test]
    public async Task AKeySetByHandWithNoIndexIsWarnedAboutAsync()
    {
        await m_fixture.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Manual (Id GUID PRIMARY KEY, Name VARCHAR(50))");

        var tab = await OpenAsync("Manual");

        Assert.Multiple(() =>
        {
            Assert.That(tab.KeyWarningIsSevere, Is.True);
            Assert.That(tab.KeyWarning, Does.Contain("scans"));
            Assert.That(tab.KeyWarning, Does.Contain("UNIQUE index"));
        });
    }

    [Test]
    public async Task AKeySetByHandWithAnIndexIsNotAsync()
    {
        await m_fixture.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Manual2 (Id GUID PRIMARY KEY, Name VARCHAR(50))");
        await m_fixture.Database.ExecuteNonQueryAsync(
            "CREATE UNIQUE INDEX UX_Manual2_Id ON Manual2 (Id)");

        var tab = await OpenAsync("Manual2");

        Assert.That(tab.KeyWarningIsSevere, Is.False);
        Assert.That(tab.KeyWarning, Does.Contain("index of its own"));
    }

    [Test]
    public async Task ATableWithNoKeyAtAllSaysWhatThatCostsAsync()
    {
        var tab = await OpenAsync("Logs");

        Assert.That(tab.KeyWarningIsSevere, Is.True);
        Assert.That(tab.KeyWarning, Does.Contain("no primary key"));
    }

    #endregion

    #region 6. Views and indexes

    /// <summary>
    /// A view whose body the catalogue cannot render must not be offered for editing: editing is DROP
    /// and CREATE, and creating from a body Studio does not have would destroy it.
    /// </summary>
    [Test]
    public async Task AViewWhoseBodyCannotBeReadIsNotOfferedForEditingAsync()
    {
        await m_fixture.Database.ExecuteNonQueryAsync(
            "CREATE VIEW Unreadable AS SELECT Id FROM Orders UNION SELECT Id FROM Customers");

        var tab = await m_fixture.Workspace.OpenStructureTabAsync(
            m_fixture.Database, "Unreadable", DatabaseNodeType.View);

        Assert.Multiple(() =>
        {
            Assert.That(tab.ViewDefinition, Is.Null.Or.Empty,
                "The catalogue renders a UNION as nothing at all.");
            Assert.That(tab.CanEditView, Is.False);
        });
    }

    [Test]
    public async Task AViewWhoseBodyCanBeReadIsAsync()
    {
        var tab = await m_fixture.Workspace.OpenStructureTabAsync(
            m_fixture.Database, "ActiveOrders", DatabaseNodeType.View);

        Assert.That(tab.ViewDefinition, Does.Contain("SELECT"));
        Assert.That(tab.CanEditView, Is.True);
    }

    /// <summary>
    /// <b>And now it can actually be edited.</b> <c>SchemaEditKind.ReplaceViewBody</c> existed from
    /// stage 8 with a category and a catalogue sentence describing how it would be carried out, and
    /// <b>nothing ever constructed one</b>: <c>CreateViewViewModel</c> runs its own SQL and can only
    /// CREATE, so a view's body could not be changed from the interface at all. Named as a product
    /// decision while fixing issue 12 and decided on 2026-08-09.
    ///
    /// <para>
    /// The proof is the ROWS the view returns afterwards, not the text in the box: a DROP and a CREATE
    /// that both ran leave a view answering a different question.
    /// </para>
    /// </summary>
    [Test]
    public async Task AViewsBodyCanBeReplacedAndTheViewAnswersDifferentlyAsync()
    {
        var tab = await m_fixture.Workspace.OpenStructureTabAsync(
            m_fixture.Database, "ActiveOrders", DatabaseNodeType.View);

        var before = await m_fixture.Database.ExecuteQueryAsync(
            SqlStatement.Of("SELECT * FROM ActiveOrders"));

        Assert.That(tab.CanApply, Is.False, "nothing has been edited yet");

        tab.ViewDefinition = "SELECT Id, CustomerId, Total FROM Orders WHERE Status = 'archived'";

        Assert.Multiple(() =>
        {
            Assert.That(tab.PendingCount, Is.EqualTo(1));
            Assert.That(tab.Pending!.Edits[0].Kind, Is.EqualTo(SchemaEditKind.ReplaceViewBody));
            Assert.That(tab.PendingSql, Does.Contain("DROP VIEW").And.Contain("CREATE VIEW"));
            Assert.That(tab.CanApply, Is.True,
                "and the button is live for a set made of nothing but DropCreate");
        });

        await StudioFixture.PressAsync(tab.ApplyCommand);

        Assert.That(tab.ApplyReport!.IsComplete, Is.True, tab.ApplyReport.ErrorMessage);

        // The database, not the ViewModel that asked it.
        var after = await m_fixture.Database.ExecuteQueryAsync(
            SqlStatement.Of("SELECT * FROM ActiveOrders"));

        Assert.Multiple(() =>
        {
            Assert.That(before.Data!.Rows.Count, Is.GreaterThan(0), "CONTROL: it answered before too");
            Assert.That(after.Data!.Rows.Count, Is.Not.EqualTo(before.Data.Rows.Count),
                "the view answers the new question, so both statements ran");
        });
    }

    /// <summary>
    /// CONTROL: a body that is put back exactly as it was read is not a change. Without it, "editing
    /// makes a pending edit" would be satisfied by a tab that makes one for every keystroke, including
    /// the ones that undo each other.
    /// </summary>
    [Test]
    public async Task AViewBodyPutBackUnchangedIsNoChangeAsync()
    {
        var tab = await m_fixture.Workspace.OpenStructureTabAsync(
            m_fixture.Database, "ActiveOrders", DatabaseNodeType.View);

        var loaded = tab.ViewDefinition!;

        tab.ViewDefinition = loaded + " ";
        Assert.That(tab.PendingCount, Is.EqualTo(0), "trailing space is not a change");

        tab.ViewDefinition = "SELECT Id FROM Orders";
        Assert.That(tab.PendingCount, Is.EqualTo(1));

        tab.ViewDefinition = loaded;
        Assert.That(tab.PendingCount, Is.EqualTo(0), "and putting it back takes the edit away again");
    }

    [Test]
    public async Task RebuildingAnIndexIsADropAndACreateAndItSaysSoAsync()
    {
        var tab = await OpenAsync();
        var index = tab.Indexes.First(i => i.Name == "IX_Orders_CustomerId");

        await StudioFixture.PressAsync(tab.RecreateIndexCommand, index);

        Assert.That(tab.ApplyReport!.IsComplete, Is.True, tab.ApplyReport.ErrorMessage);
        Assert.That(tab.ApplyReport.Outcomes[0].Sql, Does.StartWith("DROP INDEX"));
        Assert.That(tab.ApplyReport.Outcomes[1].Sql, Does.StartWith("CREATE INDEX"));

        var indexes = await m_fixture.Database.GetTableIndexesAsync("Orders");

        Assert.That(indexes.Select(i => i.Name), Does.Contain("IX_Orders_CustomerId"),
            "and the index is there afterwards.");
    }

    #endregion
}
