using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The rebuild of 5.3, against a real database.
///
/// The case that matters most is <see cref="ARebuiltTableStillGeneratesItsKeysAsync"/>: it is the
/// control against the defect that decided the shape of the whole plan. The design's four steps end
/// with a rename, and a renamed table's next generated INSERT overwrites an existing row - so this
/// rebuild copies out and back instead, and this case is what says so.
/// </summary>
[TestFixture]
public class TableRebuildTests
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

    private IDatabaseSession Session => m_fixture.Database;

    private async Task<List<ColumnDraft>> DraftsAsync(string table)
    {
        var columns = await Session.GetColumnsAsync(table);

        return columns.Select(c => new ColumnDraft(c)).ToList();
    }

    #region The plan

    [Test]
    public async Task ThePlanIsWorkedOutBeforeAnythingRunsAsync()
    {
        var drafts = await DraftsAsync("Orders");
        drafts.First(d => d.Name == "Total").DataType = "INTEGER";

        var before = await m_fixture.CountRowsAsync("Orders");
        var plan = await TableRebuild.PlanAsync(Session, "Orders", drafts);

        Assert.Multiple(async () =>
        {
            Assert.That(plan.Steps, Has.Count.EqualTo(4), "Four steps, as the design says.");
            Assert.That(plan.Carrier, Is.EqualTo("Orders__old"));
            Assert.That(plan.RowCount, Is.EqualTo(StudioFixture.ORDER_COUNT));
            Assert.That(plan.Script, Does.Contain("CAST"), "The conversion is a CAST the user can read.");
            Assert.That(await m_fixture.CountRowsAsync("Orders"), Is.EqualTo(before),
                "Planning touches nothing.");
        });
    }

    [Test]
    public async Task ThePlanNamesWhatItWillPutBackAndWhatItWillNotAsync()
    {
        var drafts = await DraftsAsync("Orders");
        drafts.First(d => d.Name == "Total").DataType = "INTEGER";

        var plan = await TableRebuild.PlanAsync(Session, "Orders", drafts);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Recreated, Does.Contain("index IX_Orders_CustomerId"));
            Assert.That(plan.Recreated, Does.Contain("trigger TR_Orders_Audit"));

            // The fixture's view reads Orders, and a view is not repaired by a rebuild.
            Assert.That(plan.Dependencies.Any(d => d.Contains("ActiveOrders")), Is.True,
                "A view over the table has to be named: dropping the table leaves it in the catalogue, " +
                "failing at read time.");

            Assert.That(plan.Losses, Is.Not.Empty,
                "An index is recreated from what the catalogue publishes, which does not include the " +
                "sort direction or the included columns - the plan says so rather than pretending.");
        });
    }

    /// <summary>
    /// WS-41's number, and the reason it exists: this engine's CAST never fails, so without a count
    /// nobody would know anything had been lost.
    /// </summary>
    [Test]
    public async Task ThePlanCountsTheValuesThatWillNotSurviveAsync()
    {
        await Session.ExecuteNonQueryAsync("CREATE TABLE C (Id INTEGER PRIMARY KEY AUTOINCREMENT, V VARCHAR(30))");
        await Session.ExecuteNonQueryAsync("INSERT INTO C (V) VALUES ('42')");
        await Session.ExecuteNonQueryAsync("INSERT INTO C (V) VALUES ('not a number')");
        await Session.ExecuteNonQueryAsync("INSERT INTO C (V) VALUES ('3.9')");

        var drafts = await DraftsAsync("C");
        var value = drafts.First(d => d.Name == "V");
        value.DataType = "INTEGER";
        value.MaxLength = null;

        var plan = await TableRebuild.PlanAsync(Session, "C", drafts);

        Assert.That(plan.HasCasualties, Is.True);
        Assert.That(plan.Casualties[0], Does.Contain("2 value"),
            "'not a number' and '3.9' both fail to round trip; '42' survives.");
    }

    [Test]
    public async Task NothingIsCountedWhenNoTypeChangesAsync()
    {
        // The control for the case above: a plan with no type change must report no casualties, or the
        // count is measuring something other than the conversion.
        var drafts = await DraftsAsync("Orders");
        drafts.Add(new ColumnDraft { Name = "Note", DataType = "VARCHAR", MaxLength = 20 });

        var plan = await TableRebuild.PlanAsync(Session, "Orders", drafts);

        Assert.That(plan.Casualties, Is.Empty);
    }

    #endregion

    #region Running it

    [Test]
    public async Task ARebuildCarriesEveryRowAcrossAsync()
    {
        var before = await ReadAsync("SELECT Id, Total FROM Orders ORDER BY Id");

        var drafts = await DraftsAsync("Orders");
        var total = drafts.First(d => d.Name == "Total");
        total.NumericPrecision = 20;
        total.NumericScale = 4;

        var plan = await TableRebuild.PlanAsync(Session, "Orders", drafts);
        var report = await TableRebuild.RunAsync(Session, plan);

        Assert.That(report.IsComplete, Is.True, report.ErrorMessage);

        var after = await ReadAsync("SELECT Id, Total FROM Orders ORDER BY Id");

        Assert.That(after, Has.Count.EqualTo(before.Count), "Every row came back.");
        Assert.That(after.Select(r => r.Split('|')[0]), Is.EqualTo(before.Select(r => r.Split('|')[0])),
            "with the same keys.");

        var type = await ReadAsync(
            "SELECT NUMERIC_PRECISION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Orders' AND COLUMN_NAME = 'Total'");

        Assert.That(type[0], Is.EqualTo("20"), "and the new type is in the catalogue.");
    }

    /// <summary>
    /// THE CONTROL THIS WHOLE CLASS EXISTS FOR.
    ///
    /// The design's rebuild ends with a rename. Measured: after a rename the key generator restarts and
    /// the next generated INSERT overwrites the row at key 1, silently. So this rebuild copies out and
    /// back instead - and this case is what would go red if anyone put the rename back.
    /// </summary>
    [Test]
    public async Task ARebuiltTableStillGeneratesItsKeysAsync()
    {
        var drafts = await DraftsAsync("Customers");
        drafts.First(d => d.Name == "Name").MaxLength = 200;

        var plan = await TableRebuild.PlanAsync(Session, "Customers", drafts);

        Assert.That(plan.Script, Does.Not.Contain("RENAME"),
            "The plan must not rename the table: a rename loses the key generator on this engine.");

        var report = await TableRebuild.RunAsync(Session, plan);
        Assert.That(report.IsComplete, Is.True, report.ErrorMessage);

        var before = await ReadAsync("SELECT Id, Name FROM Customers ORDER BY Id");

        await Session.ExecuteNonQueryAsync("INSERT INTO Customers (Name) VALUES ('After the rebuild')");

        var after = await ReadAsync("SELECT Id, Name FROM Customers ORDER BY Id");

        Assert.That(after, Has.Count.EqualTo(before.Count + 1),
            "The insert added a row. If it overwrote one instead, the count is unchanged and the " +
            "rebuild has destroyed data.");

        Assert.That(after.Take(before.Count), Is.EqualTo(before),
            "and none of the rows that were there changed.");
    }

    [Test]
    public async Task TheTriggerAndTheIndexAreBackAfterwardsAsync()
    {
        var drafts = await DraftsAsync("Orders");
        drafts.First(d => d.Name == "Status").MaxLength = 64;

        var plan = await TableRebuild.PlanAsync(Session, "Orders", drafts);
        var report = await TableRebuild.RunAsync(Session, plan);

        Assert.That(report.IsComplete, Is.True, report.ErrorMessage);

        var indexes = await Session.GetTableIndexesAsync("Orders");
        var triggers = await Session.GetTableTriggersAsync("Orders");

        Assert.That(indexes.Select(i => i.Name), Does.Contain("IX_Orders_CustomerId"));
        Assert.That(triggers.Select(t => t.Name), Does.Contain("TR_Orders_Audit"));

        // and the trigger still fires - a trigger in the catalogue that does nothing would pass the
        // check above and fail the user.
        var auditBefore = await m_fixture.CountRowsAsync("OrdersAudit");
        await Session.ExecuteNonQueryAsync("INSERT INTO Orders (CustomerId, Total) VALUES (1, 5)");
        var auditAfter = await m_fixture.CountRowsAsync("OrdersAudit");

        Assert.That(auditAfter, Is.EqualTo(auditBefore + 1));
    }

    [Test]
    public async Task TheCarrierIsGoneWhenItIsOverAsync()
    {
        var drafts = await DraftsAsync("Logs");
        drafts.First(d => d.Name == "Message").MaxLength = 400;

        var plan = await TableRebuild.PlanAsync(Session, "Logs", drafts);
        await TableRebuild.RunAsync(Session, plan);

        var tables = await Session.GetTablesAsync();

        Assert.That(tables.Select(t => t.Name), Does.Not.Contain("Logs__old"));
    }

    #endregion

    #region When it stops

    /// <summary>
    /// WS-41's last paragraph: an interrupted rebuild says what is true now and what to run to get
    /// back. The interruption is arranged by making the second step impossible - the carrier already
    /// exists under the name the plan wants.
    /// </summary>
    [Test]
    public async Task AnInterruptedRebuildSaysWhatIsInTheDatabaseAsync()
    {
        var drafts = await DraftsAsync("Logs");
        drafts.First(d => d.Name == "Message").MaxLength = 400;

        var plan = await TableRebuild.PlanAsync(Session, "Logs", drafts);

        // Something is already sitting on the carrier's name, so the first step cannot create it.
        await Session.ExecuteNonQueryAsync("CREATE TABLE Logs__old (Nothing INTEGER)");

        var report = await TableRebuild.RunAsync(Session, plan);

        Assert.Multiple(async () =>
        {
            Assert.That(report.IsComplete, Is.False);
            Assert.That(report.StoppedAt, Is.Not.Null);
            Assert.That(report.Summary, Does.Contain("Logs is untouched"),
                "The report says what is true, not that an error occurred.");
            Assert.That(report.Recovery, Is.Not.Empty, "and what to run to clean up.");

            Assert.That(await m_fixture.CountRowsAsync("Logs"), Is.EqualTo(2),
                "and the table really is untouched.");
        });
    }

    [Test]
    public async Task TheStepsRecordWhereItGotToAsync()
    {
        var drafts = await DraftsAsync("Logs");
        drafts.First(d => d.Name == "Message").MaxLength = 400;

        var plan = await TableRebuild.PlanAsync(Session, "Logs", drafts);
        await Session.ExecuteNonQueryAsync("CREATE TABLE Logs__old (Nothing INTEGER)");

        await TableRebuild.RunAsync(Session, plan);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Steps[0].Outcome, Is.EqualTo(DdlOutcome.Failed));
            Assert.That(plan.Steps[1].Outcome, Is.EqualTo(DdlOutcome.NotReached));
            Assert.That(plan.Steps[3].Outcome, Is.EqualTo(DdlOutcome.NotReached));
        });
    }

    #endregion

    #region The button, which is armed again

    /// <summary>
    /// This case replaces <c>TheRebuildDialogWillNotRunItYetAsync</c>, which pinned the button
    /// DISARMED between 2026-08-06 and 2026-08-07. The disarming was right at the time and wrong about
    /// its subject: what left two files unreadable was Studio's exit never flushing the database, not
    /// the rebuild. Issue 10 in <c>Docs/KnownIssues.md</c> carries the six runs that settled it,
    /// including the one that matters - killing Studio right after a rebuild leaves the file READABLE,
    /// so a clean run of the rebuild alone proves nothing, and the workload that does break the file
    /// breaks it with no rebuild in sight.
    ///
    /// Both directions, because the only refusal left is an empty plan and a rule that can only pass
    /// is not a rule.
    /// </summary>
    [Test]
    public async Task TheRebuildDialogIsArmedAsync()
    {
        var drafts = await DraftsAsync("Orders");
        drafts.First(d => d.Name == "Total").DataType = "INTEGER";

        var plan = await TableRebuild.PlanAsync(Session, "Orders", drafts);

        var vm = new Studio.ViewModels.TableRebuildViewModel(m_fixture.App, Session, plan);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Steps, Is.Not.Empty, "the plan has work in it");
            Assert.That(vm.CanRebuild, Is.True, "so the button runs it");
            Assert.That(vm.Script, Does.Contain("CREATE TABLE"),
                "and the script is still there to be run by hand instead");
        });
    }

    /// <summary>
    /// The negative half: a plan with no steps cannot be run, and the dialog says why rather than
    /// showing a grey button with no explanation.
    /// </summary>
    [Test]
    public async Task ARebuildWithNothingToDoIsRefusedAsync()
    {
        // The drafts are the table as it already is, so nothing needs rewriting.
        var drafts = await DraftsAsync("Orders");

        var plan = await TableRebuild.PlanAsync(Session, "Orders", drafts);

        var empty = new TableRebuildPlan
        {
            Table = plan.Table,
            Carrier = plan.Carrier,
            Steps = []
        };

        var vm = new Studio.ViewModels.TableRebuildViewModel(m_fixture.App, Session, empty);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanRebuild, Is.False, "there is nothing to run");
            Assert.That(vm.NotArmedReason, Is.Not.Empty, "and the reason is on screen");
        });
    }

    #endregion

    #region Tools

    private async Task<List<string>> ReadAsync(string sql)
    {
        var result = await Session.ExecuteQueryAsync(sql);

        Assert.That(result.ErrorMessage, Is.Null.Or.Empty, sql);

        var rows = new List<string>();

        foreach (System.Data.DataRow row in result.Data!.Rows)
            rows.Add(string.Join("|", row.ItemArray.Select(v => v is null or DBNull ? "" : $"{v}")));

        return rows;
    }

    #endregion
}
