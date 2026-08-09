using NUnit.Framework;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The keyboard window (9.6): a reference with a search over it.
/// </summary>
/// <remarks>
/// <b>The criterion:</b> the window shows what the application really does, in the reader's language,
/// and the search finds a row from either end - the action or the gesture. Whether the LIST is true is
/// <c>KeyboardMapTests</c>'s job, which checks it against the markup and the key handler in both
/// directions; these cases are about the window over it.
/// </remarks>
[TestFixture]
public class KeyboardHelpTests
{
    #region Setup

    private StudioFixture m_fixture = null!;

    private KeyboardHelpViewModel Open() => new(m_fixture.App);

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(withSchema: false);
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region Tests

    [Test]
    public void EveryShortcutIsOnScreenToBeginWithTest()
    {
        var window = Open();

        Assert.Multiple(() =>
        {
            Assert.That(window.Rows, Has.Count.EqualTo(KeyboardMap.Shortcuts.Count));
            Assert.That(window.IsEmpty, Is.False);

            // In words, not in keys - the failure mode of a catalogue lookup is to print itself.
            Assert.That(window.Rows.Select(row => row.Action), Has.None.StartWith("Keys."));
            Assert.That(window.Rows.Select(row => row.Scope), Has.None.StartWith("Keys."));
        });
    }

    /// <summary>
    /// The search works from either end, because a person arrives from either end.
    /// </summary>
    [Test]
    public void TheSearchFindsAnActionAndAGestureTest()
    {
        var window = Open();

        window.Filter = "Ctrl+R";

        Assert.That(window.Rows.Select(row => row.Gesture), Has.All.Contain("Ctrl+R"),
            "asked by gesture");

        window.Filter = "palette";

        Assert.Multiple(() =>
        {
            Assert.That(window.Rows, Has.Count.EqualTo(1), "asked by action");
            Assert.That(window.Rows[0].Gesture, Is.EqualTo("Ctrl+K"));
        });
    }

    [Test]
    public void ASearchThatFindsNothingSaysSoTest()
    {
        var window = Open();

        window.Filter = "there is no such key";

        Assert.Multiple(() =>
        {
            Assert.That(window.Rows, Is.Empty);
            Assert.That(window.IsEmpty, Is.True);
        });
    }

    [Test]
    public void ClearingTheSearchBringsThemAllBackTest()
    {
        var window = Open();

        window.Filter = "palette";
        window.Filter = string.Empty;

        Assert.That(window.Rows, Has.Count.EqualTo(KeyboardMap.Shortcuts.Count));
    }

    /// <summary>
    /// Copy takes what is ON SCREEN, which is what a person means by "copy this".
    /// </summary>
    /// <remarks>
    /// Asserted on the text the command produced rather than on the clipboard: a headless run has no
    /// clipboard, and "the command did not throw" is not evidence that anything was copied.
    /// </remarks>
    [Test]
    public void CopyTakesTheFilteredListTest()
    {
        var window = Open();

        window.Filter = "palette";
        window.CopyCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(window.CopiedText, Does.Contain("Ctrl+K"));
            Assert.That(window.CopiedText, Does.Not.Contain("Ctrl+O"),
                "the filter is part of what was asked for");
            Assert.That(window.CopiedText!.Split(Environment.NewLine), Has.Length.EqualTo(1));
        });
    }

    /// <summary>
    /// The window says why it does not let a shortcut be reassigned.
    /// </summary>
    /// <remarks>
    /// The design asks for rebinding (WS-69) and it is deliberately absent: the gestures are declared
    /// in the shell rather than read from <c>KeyboardMap</c>, so a field here would take a key and
    /// change nothing. A named remainder in front of the user, in their language - the shape stage 9
    /// used for the unswept views.
    /// </remarks>
    [Test]
    public void TheAbsenceOfRebindingIsExplainedInTheReadersLanguageTest()
    {
        var window = Open();

        var english = window.RebindingNote;

        m_fixture.App.Localization.SetLanguage("ru");

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(english, Is.Not.Empty.And.Not.StartWith("Keys."));
                Assert.That(window.RebindingNote, Is.Not.EqualTo(english),
                    "and it is a sentence from the catalogue, not one built here");
            });
        }
        finally
        {
            m_fixture.App.Localization.SetLanguage("en");
        }
    }

    #endregion
}
