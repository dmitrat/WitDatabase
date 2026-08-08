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
