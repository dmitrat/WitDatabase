using System.Data;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The grid says which rows have been changed and which are going to be deleted.
/// </summary>
/// <remarks>
/// <para>
/// Finding 30: an edited row looked exactly like every other row - no gutter mark, no colour, no
/// italics - and a deleted row <b>disappeared from the grid altogether</b>, with the count dropping
/// from 1000 to 999. What said anything was the tab's dot, the toolbar's badge and the commit button
/// lighting up, all of which are per-tab. Nothing pointed at the rows themselves, so discarding was
/// the only way to find out what had been changed.
/// </para>
/// <para>
/// <b>The row was never removed.</b> <c>DeleteSelectedRow</c> has always called <c>DataRow.Delete</c>,
/// which marks it; what hid it was the default <c>RowStateFilter</c> of the view built over the
/// table. A deleted row stays in place now, struck through and dimmed - decided 2026-08-18 - and the
/// count of rows still says what will be left.
/// </para>
/// <para>
/// The last case is the write path again: a marked row is still deleted from the database when the
/// set is applied.
/// </para>
/// </remarks>
[TestFixture]
public class ChangedAndDeletedRowsAreVisibleTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private TableEditTabViewModel m_editor = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        // Its own table, and not the fixture's Customers: those are referenced by Orders, so
        // deleting one is refused by the foreign key - which is a different case from this one.
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Marks (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await m_studio.Database.ExecuteNonQueryAsync(
            "INSERT INTO Marks (Id, Name) VALUES (1, 'Ada')");
        await m_studio.Database.ExecuteNonQueryAsync(
            "INSERT INTO Marks (Id, Name) VALUES (2, 'Grace')");

        m_editor = await m_studio.Workspace.OpenTableEditTabAsync(m_studio.Database, "Marks");

        await m_editor.LoadDataAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Tests

    [Test]
    public void ADeletedRowStaysOnScreenAndIsMarkedTest()
    {
        m_editor.SelectedRowView = m_editor.CurrentView![0];

        m_editor.DeleteRowCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(m_editor.CurrentView!.Count, Is.EqualTo(2),
                "the row is still drawn - it is going to be deleted, not gone");

            Assert.That(m_editor.CurrentView[0].Row.RowState, Is.EqualTo(DataRowState.Deleted),
                "and it is marked as the one that will go");

            Assert.That(m_editor.TotalRowCount, Is.EqualTo(1),
                "while the count says what will be left");

            Assert.That(m_editor.ChangeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void AChangedRowCarriesItsOwnMarkTest()
    {
        var view = m_editor.CurrentView![0];

        view.Row["Name"] = "Ada Lovelace";
        m_editor.CellEditedCommand.Execute(view);

        Assert.That(view.Row.RowState, Is.EqualTo(DataRowState.Modified),
            "the row knows it was changed, which is what the grid draws");
    }

    /// <summary>
    /// The grid has to draw what the row knows. Asserted on the markup as well as on the state,
    /// because a flag nothing reads is the shape this application keeps finding.
    /// </summary>
    [Test]
    public void TheGridDrawsTheMarksTest()
    {
        var markup = Markup("Views/Workspace/TableEditView.axaml");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("row-deleted"),
                "a row that will be deleted is drawn as one");

            Assert.That(markup, Does.Contain("row-changed"),
                "and so is a row that was changed");

            Assert.That(markup, Does.Contain("Strikethrough"),
                "struck through, which is the decision taken");
        });
    }

    /// <summary>
    /// The write path: a marked row is still deleted when the set is applied.
    /// </summary>
    [Test]
    public async Task AMarkedRowIsStillDeletedOnCommitTest()
    {
        m_editor.SelectedRowView = m_editor.CurrentView![0];

        m_editor.DeleteRowCommand.Execute(null);

        await StudioFixture.PressAsync(m_editor.CommitCommand);

        var result = await m_studio.Database.ExecuteQueryAsync("SELECT Name FROM Marks ORDER BY Id");

        Assert.Multiple(() =>
        {
            Assert.That(m_editor.HasError, Is.False, m_editor.StatusMessage);

            Assert.That(result.Data!.Rows, Has.Count.EqualTo(1),
                "the row that was marked is gone from the database");

            Assert.That(result.Data.Rows[0][0]?.ToString(), Is.EqualTo("Grace"));
        });
    }

    #endregion

    #region Tools

    private static string Markup(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
            {
                var path = Path.Combine(candidate, relative.Replace('/', Path.DirectorySeparatorChar));

                Assert.That(File.Exists(path), Is.True, $"{relative} must be where this fixture says");

                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new AssertionException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
