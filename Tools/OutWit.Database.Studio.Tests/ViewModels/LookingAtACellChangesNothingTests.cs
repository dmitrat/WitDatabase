using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A cell edit that changes no value changes nothing.
/// </summary>
/// <remarks>
/// <para>
/// Finding 2: double-clicking a cell to enter edit mode and leaving it without typing gave the tab its
/// dot, raised the <b>Unsaved changes</b> badge and lit <b>Commit</b> - with no value differing from
/// what was read. The reader is told that changes accumulate and that Ctrl+S applies them; a dirty
/// flag raised by looking at a cell teaches the opposite and invites a commit of nothing.
/// </para>
/// <para>
/// <b>The cause was that nothing compared.</b> The grid parsed the text, wrote it into the row and
/// raised its command whether or not the value differed, and the row was added to the modified set on
/// arrival.
/// </para>
/// <para>
/// <b>The third case is the one that matters most</b>, because this is the write path: a real change
/// still marks the row and still reaches the database. A fix that made the editor quieter by dropping
/// edits would pass the first two cases and lose somebody's work.
/// </para>
/// </remarks>
[TestFixture]
public class LookingAtACellChangesNothingTests
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

        await m_studio.Database.ExecuteNonQueryAsync(
            "INSERT INTO Customers (Name, Email) VALUES ('Ada', 'ada@example.com')");

        m_editor = await m_studio.Workspace.OpenTableEditTabAsync(m_studio.Database, "Customers");

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
    public void WritingTheSameValueMarksNothingTest()
    {
        var view = m_editor.CurrentView![0];
        var name = view.Row["Name"];

        view.Row["Name"] = name;
        m_editor.CellEditedCommand.Execute(view);

        Assert.Multiple(() =>
        {
            Assert.That(m_editor.HasChanges, Is.False,
                "nothing differs from what was read");

            Assert.That(m_editor.ChangeCount, Is.Zero);
            Assert.That(m_editor.IsModified, Is.False, "and the tab keeps its dot to itself");
            Assert.That(m_editor.CanCommit, Is.False, "there is nothing to commit");
        });
    }

    [Test]
    public void EditingARowBackToWhatItWasUnmarksItTest()
    {
        var view = m_editor.CurrentView![0];
        var name = view.Row["Name"];

        view.Row["Name"] = "Grace";
        m_editor.CellEditedCommand.Execute(view);

        Assume.That(m_editor.HasChanges, Is.True, "the row is changed");

        view.Row["Name"] = name;
        m_editor.CellEditedCommand.Execute(view);

        Assert.Multiple(() =>
        {
            Assert.That(m_editor.HasChanges, Is.False,
                "a row edited back to what it was is not a change");

            Assert.That(m_editor.ChangeCount, Is.Zero);
        });
    }

    /// <summary>
    /// The write path, which is what this phase is allowed to break and must not.
    /// </summary>
    [Test]
    public async Task ARealChangeIsStillMarkedAndStillCommittedTest()
    {
        var view = m_editor.CurrentView![0];

        view.Row["Name"] = "Grace";
        m_editor.CellEditedCommand.Execute(view);

        Assert.Multiple(() =>
        {
            Assert.That(m_editor.HasChanges, Is.True);
            Assert.That(m_editor.ChangeCount, Is.EqualTo(1));
            Assert.That(m_editor.CanCommit, Is.True);
        });

        await StudioFixture.PressAsync(m_editor.CommitCommand);

        var result = await m_studio.Database.ExecuteQueryAsync("SELECT Name FROM Customers");

        Assert.Multiple(() =>
        {
            Assert.That(m_editor.HasError, Is.False, m_editor.StatusMessage);

            Assert.That(result.Data!.Rows[0][0]?.ToString(), Is.EqualTo("Grace"),
                "the change reached the database");
        });
    }

    #endregion
}
