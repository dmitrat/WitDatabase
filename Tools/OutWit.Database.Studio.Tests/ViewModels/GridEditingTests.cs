using System.Data;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The grid as an editor (4.6, WS-36, WS-37): the buffer, the one transaction, and what happens when
/// something else got to the row first.
///
/// The conflict cases change the row through the database rather than by adjusting the buffer: a test
/// that simulated the conflict would be testing the simulation. The change goes through this
/// database's own connection, and the reason is measured below - the engine holds the file
/// exclusively, so a second connection to it is not a state Studio can be in.
/// </summary>
[TestFixture]
public class GridEditingTests
{
    #region Fixture

    private StudioFixture m_fixture = null!;
    private TableEditTabViewModel m_editor = null!;

    [SetUp]
    public async Task SetUp()
    {
        m_fixture = await StudioFixture.CreateAsync();

        m_editor = await m_fixture.Workspace.OpenTableEditTabAsync(m_fixture.Database, "Orders");
        await m_editor.LoadDataAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    private static void EditCell(TableEditTabViewModel editor, int row, string column, object value)
    {
        var view = editor.CurrentView![row];

        view.Row[column] = value;
        editor.CellEditedCommand.Execute(view);
    }

    /// <summary>
    /// A SECOND connection to the same database file. Measured to be possible in
    /// <see cref="ASecondConnectionToTheSameFileOpensAndSeesTheSameRowsTest"/>.
    /// </summary>
    private async Task<IDatabaseSession> SecondConnectionAsync()
    {
        var session = await m_fixture.Connections.OpenAsync(new OutWit.Database.Studio.Models.ConnectionInfo
        {
            FilePath = m_fixture.DatabasePath,
            StorageEngine = "btree"
        });

        Assert.That(session, Is.Not.Null, "the fixture could not open a second connection");

        return session!;
    }

    /// <summary>
    /// Changes a row from another connection, which is the condition WS-37 exists for: the row is no
    /// longer the one that was read into this editor's buffer.
    /// </summary>
    private async Task ChangeBehindTheEditorAsync(string sql)
    {
        var other = await SecondConnectionAsync();

        await other.ExecuteNonQueryAsync(sql);
    }

    #endregion

    #region What "another connection" can mean here

    /// <summary>
    /// PINS THE ENGINE AS IT IS. The assumption going in was that this would be refused - 12.2.0 holds
    /// a database under an exclusive file lock - and it is not: a second connection to the same file
    /// opens, and sees what the first one wrote.
    ///
    /// That is what makes WS-37 a real question rather than a theoretical one, and it is why the
    /// conflict cases below use a genuine second connection instead of arranging the condition by hand.
    /// </summary>
    [Test]
    public async Task ASecondConnectionToTheSameFileOpensAndSeesTheSameRowsTest()
    {
        var second = await SecondConnectionAsync();

        Assert.That(second, Is.Not.Null);

        var rows = await second.ExecuteQueryAsync("SELECT Status FROM Orders WHERE Id = 1");

        Assert.That(rows.Data!.Rows[0][0], Is.EqualTo("new"),
            "and it reads the rows the first connection put there");
    }

    #endregion

    #region The conflict

    /// <summary>
    /// WS-37. The editor reads a row, somebody else changes it, and the editor's edit is refused -
    /// with both values in front of the user rather than "the transaction was rejected".
    /// </summary>
    [Test]
    public async Task AnEditOfARowSomebodyElseChangedIsRefusedAndShowsBothSidesTest()
    {
        EditCell(m_editor, 0, "Status", "mine");

        await ChangeBehindTheEditorAsync("UPDATE Orders SET Status = 'theirs' WHERE Id = 1");

        await StudioFixture.PressAsync(m_editor.CommitCommand);

        Assert.That(m_editor.HasConflict, Is.True, m_editor.ErrorMessage);
        Assert.That(m_editor.HasChanges, Is.True, "the buffer is kept: nothing has to be retyped");
        Assert.That(m_editor.ConflictSummary, Does.Contain("Id = 1"));

        var status = m_editor.ConflictColumns.FirstOrDefault(value => value.Column == "Status");

        Assert.That(status, Is.Not.Null, "the column that differs is named");
        Assert.That(status!.Mine, Does.Contain("mine"));
        Assert.That(status.Theirs, Does.Contain("theirs"));

        // And the artifact: the other connection's value is what is in the database.
        var live = await m_fixture.Database.ExecuteQueryAsync("SELECT Status FROM Orders WHERE Id = 1");

        Assert.That(live.Data!.Rows[0][0], Is.EqualTo("theirs"),
            "nothing of the edit reached the database");
    }

    /// <summary>
    /// The control, and the one that proves the check above is not simply refusing everything: the
    /// same edit with nobody else touching the row goes in.
    /// </summary>
    [Test]
    public async Task AnEditOfAnUntouchedRowGoesInTest()
    {
        EditCell(m_editor, 0, "Status", "mine");

        await StudioFixture.PressAsync(m_editor.CommitCommand);

        Assert.That(m_editor.HasConflict, Is.False, m_editor.ErrorMessage);
        Assert.That(m_editor.HasChanges, Is.False);

        var live = await m_fixture.Database.ExecuteQueryAsync("SELECT Status FROM Orders WHERE Id = 1");

        Assert.That(live.Data!.Rows[0][0], Is.EqualTo("mine"));
    }

    [Test]
    public async Task ARowSomebodyElseDeletedIsReportedAsDeletedTest()
    {
        EditCell(m_editor, 0, "Status", "mine");

        await ChangeBehindTheEditorAsync("DELETE FROM Orders WHERE Id = 1");

        await StudioFixture.PressAsync(m_editor.CommitCommand);

        Assert.That(m_editor.HasConflict, Is.True);
        Assert.That(m_editor.ConflictSummary, Does.Contain("DELETED"));
    }

    /// <summary>
    /// "Apply over" is a decision the user makes, and it is a separate press for that reason: it
    /// overwrites work somebody else did.
    /// </summary>
    [Test]
    public async Task ApplyingOverAConflictOverwritesTheOtherValueTest()
    {
        EditCell(m_editor, 0, "Status", "mine");

        await ChangeBehindTheEditorAsync("UPDATE Orders SET Status = 'theirs' WHERE Id = 1");

        await StudioFixture.PressAsync(m_editor.CommitCommand);

        Assert.That(m_editor.HasConflict, Is.True);

        await StudioFixture.PressAsync(m_editor.OverwriteCommand);

        Assert.That(m_editor.HasConflict, Is.False, m_editor.ErrorMessage);
        Assert.That(m_editor.HasChanges, Is.False);

        var live = await m_fixture.Database.ExecuteQueryAsync("SELECT Status FROM Orders WHERE Id = 1");

        Assert.That(live.Data!.Rows[0][0], Is.EqualTo("mine"));
    }

    [Test]
    public async Task RereadingThrowsTheBufferAwayAndShowsWhatIsThereTest()
    {
        EditCell(m_editor, 0, "Status", "mine");

        await ChangeBehindTheEditorAsync("UPDATE Orders SET Status = 'theirs' WHERE Id = 1");

        await StudioFixture.PressAsync(m_editor.CommitCommand);
        await StudioFixture.PressAsync(m_editor.RereadCommand);

        Assert.That(m_editor.HasConflict, Is.False);
        Assert.That(m_editor.HasChanges, Is.False);
        Assert.That(m_editor.CurrentView![0].Row["Status"], Is.EqualTo("theirs"));
    }

    /// <summary>
    /// The version check must not turn an ordinary edit of a row with a NULL in it into a conflict:
    /// a NULL is compared with IS NULL, not with =, and getting that wrong would make every row with
    /// an empty column uneditable.
    /// </summary>
    [Test]
    public async Task ARowWithANullInItIsStillEditableTest()
    {
        var customers = await m_fixture.Workspace.OpenTableEditTabAsync(m_fixture.Database, "Customers");
        await customers.LoadDataAsync();

        // 'Vector Supply' is seeded with no Email.
        var row = customers.CurrentView!.Cast<DataRowView>()
            .First(candidate => candidate.Row["Email"] == DBNull.Value);

        row.Row["Name"] = "Vector Supply Ltd";
        customers.CellEditedCommand.Execute(row);

        await StudioFixture.PressAsync(customers.CommitCommand);

        Assert.That(customers.HasConflict, Is.False, customers.ErrorMessage);
        Assert.That(customers.HasChanges, Is.False, customers.ErrorMessage);
    }

    #endregion

    #region Show SQL

    /// <summary>
    /// WS-32, the direction that matters most: what will be sent, BEFORE it is sent.
    /// </summary>
    [Test]
    public async Task TheEditBufferCanBeReadAsTheTransactionItWillBecomeTest()
    {
        EditCell(m_editor, 0, "Status", "shipped");

        m_editor.ShowChangesSqlCommand.Execute(null);

        var tab = (QueryTabViewModel)m_fixture.Workspace.SelectedTab!;

        Assert.That(tab.SqlText, Does.StartWith("BEGIN TRANSACTION;"));
        Assert.That(tab.SqlText, Does.Contain("UPDATE [Orders]"));
        Assert.That(tab.SqlText, Does.Contain("'shipped'"), "the values, as SQL would write them");
        Assert.That(tab.SqlText, Does.EndWith("COMMIT;"));

        // And nothing has been applied by looking at it.
        var live = await m_fixture.Database.ExecuteQueryAsync("SELECT Status FROM Orders WHERE Id = 1");

        Assert.That(live.Data!.Rows[0][0], Is.EqualTo("new"));
    }

    [Test]
    public async Task TheViewCanBeReadAsTheSelectItIsAndThatSelectRunsTest()
    {
        m_editor.Filters.First(filter => filter.Column == "Status").Text = "new";

        await StudioFixture.PressAsync(m_editor.ApplyFiltersCommand);

        m_editor.ShowViewSqlCommand.Execute(null);

        var tab = (QueryTabViewModel)m_fixture.Workspace.SelectedTab!;

        Assert.That(tab.SqlText, Does.Contain("FROM [Orders]"));
        Assert.That(tab.SqlText, Does.Contain("LIKE"));

        await tab.ExecuteSqlAsync(tab.SqlText);

        Assert.That(tab.ErrorMessage, Is.Null, tab.SqlText);
        Assert.That(tab.TotalRowCount, Is.EqualTo(2), "the two orders whose status contains 'new'");
    }

    #endregion

    #region Sorting and filtering

    [Test]
    public async Task FilteringAsksTheEngineAndNarrowsTheGridTest()
    {
        Assert.That(m_editor.CurrentView!.Count, Is.EqualTo(3));

        m_editor.Filters.First(filter => filter.Column == "Total").Text = "> 2000";

        await StudioFixture.PressAsync(m_editor.ApplyFiltersCommand);

        Assert.That(m_editor.CurrentView!.Count, Is.EqualTo(2));
        Assert.That(m_editor.ViewDescription, Does.Contain("Total > 2000"));

        await StudioFixture.PressAsync(m_editor.ClearFiltersCommand);

        Assert.That(m_editor.CurrentView!.Count, Is.EqualTo(3));
    }

    /// <summary>
    /// 4.7. An empty table and a filter that matches nothing look identical on screen and are not the
    /// same thing: one has no rows, the other has rows the user cannot see.
    /// </summary>
    [Test]
    public async Task NothingMatchingTheFilterIsItsOwnStateTest()
    {
        Assert.That(m_editor.IsEmptyByFilter, Is.False, "a table with rows in it is not empty");

        m_editor.Filters.First(filter => filter.Column == "Total").Text = "> 999999";

        await StudioFixture.PressAsync(m_editor.ApplyFiltersCommand);

        Assert.That(m_editor.CurrentView!.Count, Is.Zero);
        Assert.That(m_editor.IsEmptyByFilter, Is.True);

        await StudioFixture.PressAsync(m_editor.ClearFiltersCommand);

        Assert.That(m_editor.IsEmptyByFilter, Is.False);
    }

    /// <summary>
    /// WS-30. The proof that the sort is the engine's is that it reaches beyond the page: sorting a
    /// page of one client-side could never put the largest row on it.
    /// </summary>
    [Test]
    public async Task SortingIsDoneByTheEngineAndReachesBeyondThePageTest()
    {
        m_editor.PageSize = 1;

        await StudioFixture.PressAsync(m_editor.SortByCommand, "Total");

        Assert.That(m_editor.CurrentView!.Count, Is.EqualTo(1));
        Assert.That(Convert.ToDecimal(m_editor.CurrentView[0].Row["Total"]), Is.EqualTo(1204.00m),
            "the smallest of the three, which is not the first row of the unsorted page");

        // Pressing the same column again turns it around.
        await StudioFixture.PressAsync(m_editor.SortByCommand, "Total");

        Assert.That(m_editor.SortDescending, Is.True);
        Assert.That(Convert.ToDecimal(m_editor.CurrentView![0].Row["Total"]), Is.EqualTo(4812.50m));
    }

    [Test]
    public async Task SortingByAColumnThatIsNotTheKeyPagesByOffsetAndSaysSoTest()
    {
        m_editor.PageSize = 1;

        await StudioFixture.PressAsync(m_editor.SortByCommand, "Total");
        await StudioFixture.PressAsync(m_editor.NextPageCommand);

        Assert.That(m_editor.Paging, Is.EqualTo(GridPaging.Offset));
        Assert.That(m_editor.IsDeepPage, Is.True);
    }

    #endregion

    #region The total

    /// <summary>
    /// The total is asked for, never assumed (4.2): every page would otherwise pay for a label.
    /// </summary>
    [Test]
    public async Task TheTotalIsUnknownUntilSomebodyAsksTest()
    {
        Assert.That(m_editor.TotalRows, Is.Null);

        await StudioFixture.PressAsync(m_editor.CountRowsCommand);

        Assert.That(m_editor.TotalRows, Is.EqualTo(3));

        // And it is the total of the VIEW, not of the table.
        m_editor.Filters.First(filter => filter.Column == "Total").Text = "> 2000";

        await StudioFixture.PressAsync(m_editor.ApplyFiltersCommand);

        Assert.That(m_editor.TotalRows, Is.Null, "a new view has a new total, and nobody has asked yet");

        await StudioFixture.PressAsync(m_editor.CountRowsCommand);

        Assert.That(m_editor.TotalRows, Is.EqualTo(2));
    }

    #endregion
}
