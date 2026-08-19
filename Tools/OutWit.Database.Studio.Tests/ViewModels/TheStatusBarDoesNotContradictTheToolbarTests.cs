using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A message about a state the editor has left does not stay on screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Finding 3:</b> a message such as <i>Changes discarded</i> stayed until some later event replaced
/// it, so editing a cell straight afterwards left the bar reading <i>Changes discarded</i> while the
/// toolbar directly above it showed <b>Unsaved changes</b>. The two contradict each other in the same
/// frame - fatal for a screenshot, merely confusing at the keyboard.
/// </para>
/// <para>
/// <b>The rule taken here is the narrow one:</b> a message describes what the buffer was, so the next
/// change to the buffer takes it away. It is not a general expiry - a message about something that has
/// not been superseded stays, which is what the second case holds.
/// </para>
/// <para>
/// <b>And the old C1:</b> the status bar did not follow the language, because a message is written
/// into it from the catalogue at the moment of the event and nothing re-reads it. The 57 call sites
/// are the reason that is not a rewrite: what happens now is that a language change returns the bar to
/// its idle sentence, in the new language. A stale message in the language a person has just left is
/// worse than no message.
/// </para>
/// </remarks>
[TestFixture]
public class TheStatusBarDoesNotContradictTheToolbarTests
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
            "CREATE TABLE Notes (Id INTEGER PRIMARY KEY, Body VARCHAR(50))");
        await m_studio.Database.ExecuteNonQueryAsync(
            "INSERT INTO Notes (Id, Body) VALUES (1, 'first')");

        m_editor = await m_studio.Workspace.OpenTableEditTabAsync(m_studio.Database, "Notes");

        await m_editor.LoadDataAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Finding 3

    [Test]
    public void EditingAfterADiscardTakesTheDiscardMessageAwayTest()
    {
        var view = m_editor.CurrentView![0];

        view.Row["Body"] = "second";
        m_editor.CellEditedCommand.Execute(view);

        m_editor.RollbackCommand.Execute(null);

        Assume.That(m_editor.StatusMessage, Is.Not.Null.And.Not.Empty,
            "discarding says so, which is right");

        var reloaded = m_editor.CurrentView![0];

        reloaded.Row["Body"] = "third";
        m_editor.CellEditedCommand.Execute(reloaded);

        Assert.Multiple(() =>
        {
            Assert.That(m_editor.HasChanges, Is.True, "the toolbar says there are unsaved changes");

            Assert.That(m_editor.StatusMessage, Is.Null.Or.Empty,
                "so the bar cannot go on saying the changes were discarded");
        });
    }

    /// <summary>
    /// The control: a message that has not been contradicted stays.
    /// </summary>
    [Test]
    public void AMessageThatStillHoldsStaysTest()
    {
        m_editor.RollbackCommand.Execute(null);

        Assert.That(m_editor.StatusMessage, Is.Not.Null.And.Not.Empty,
            "nothing has happened since, so the message is still true");
    }

    #endregion

    #region The old C1

    [Test]
    public async Task ChangingTheLanguageDoesNotLeaveAMessageInTheOldOneTest()
    {
        var settings = await m_studio.Settings.LoadAsync();

        m_studio.App.MainWindowVm.StatusText = "Loaded 1 row";

        settings.Language = settings.Language == "ru" ? "en" : "ru";
        await m_studio.Settings.SaveAsync(settings);

        Assert.That(m_studio.App.MainWindowVm.StatusText,
            Is.EqualTo(m_studio.App.Localization["Status.Ready"]),
            "the bar returns to its idle sentence, in the language now chosen");
    }

    #endregion
}
