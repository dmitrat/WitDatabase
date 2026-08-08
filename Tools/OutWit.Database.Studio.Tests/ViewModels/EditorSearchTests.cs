using NUnit.Framework;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// One session of finding and replacing in the editor, in order (9.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>The criterion, written first because the design gives none:</b> the band tells the truth about
/// the text at every moment - how many matches there are, which one you are on, and what a
/// replacement did - and the editor's selection agrees with what the band says.
/// </para>
/// <para>
/// What a match IS belongs to <c>SqlSearchTests</c>, which asks the text directly. These cases are
/// about WHEN the question is asked: on a keystroke, on a toggle, after a replacement, and after the
/// query itself is edited underneath an open band.
/// </para>
/// </remarks>
[TestFixture]
public class EditorSearchTests
{
    #region Setup

    private const string SCRIPT = """
                                  SELECT Id, Total, Status
                                  FROM Orders
                                  WHERE Status = 'new'
                                  """;

    private StudioFixture m_fixture = null!;

    /// <summary>
    /// The fixture's own query tab - not a fresh one. The band belongs to the tab, and the tab is what
    /// the application keeps alive.
    /// </summary>
    private QueryTabViewModel Tab => m_fixture.Workspace.Tabs.OfType<QueryTabViewModel>().First();

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();
        Tab.SqlText = SCRIPT;
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region Opening

    [Test]
    public void TheBandIsClosedUntilItIsAskedForTest()
    {
        Assert.That(Tab.Search.IsOpen, Is.False);
    }

    [Test]
    public void OpeningTakesTheTermFromTheSelectionTest()
    {
        Tab.SelectionStart = SCRIPT.IndexOf("Status", StringComparison.Ordinal);
        Tab.SelectionLength = "Status".Length;

        Tab.OpenSearch(replace: false);

        Assert.Multiple(() =>
        {
            Assert.That(Tab.Search.IsOpen, Is.True);
            Assert.That(Tab.Search.Term, Is.EqualTo("Status"),
                "Ctrl+F on a word is one keystroke in every editor a person has used");
            Assert.That(Tab.Search.Summary, Is.EqualTo("1 of 2"));
        });
    }

    /// <summary>
    /// A selection spanning lines means "search in here", not "search for all of this".
    /// </summary>
    [Test]
    public void AMultiLineSelectionIsNotATermTest()
    {
        Tab.SelectionStart = 0;
        Tab.SelectionLength = SCRIPT.Length;

        Tab.OpenSearch(replace: false);

        Assert.That(Tab.Search.Term, Is.Null.Or.Empty);
    }

    #endregion

    #region Walking the matches

    [Test]
    public void TheCountAndThePlaceFollowTheTermTest()
    {
        var search = Tab.Search;

        Tab.OpenSearch(replace: false);
        search.Term = "Status";

        Assert.Multiple(() =>
        {
            Assert.That(search.Matches, Has.Count.EqualTo(2));
            Assert.That(search.Summary, Is.EqualTo("1 of 2"));

            search.FindNextCommand.Execute(null);
            Assert.That(search.Summary, Is.EqualTo("2 of 2"));

            search.FindNextCommand.Execute(null);
            Assert.That(search.Summary, Is.EqualTo("1 of 2"), "and it wraps");

            search.FindPreviousCommand.Execute(null);
            Assert.That(search.Summary, Is.EqualTo("2 of 2"), "backwards too");
        });
    }

    /// <summary>
    /// The selection in the editor is on the match the band says it is on.
    /// </summary>
    /// <remarks>
    /// The band's counter and the editor's highlight are two claims about the same thing, and stage 6
    /// shipped a defect where a message and an underline disagreed about a position. Asserting the
    /// TEXT under the selection is what makes them one claim.
    /// </remarks>
    [Test]
    public void TheEditorSelectsTheMatchTheBandIsOnTest()
    {
        var search = Tab.Search;

        Tab.OpenSearch(replace: false);
        search.Term = "Status";
        search.FindNextCommand.Execute(null);

        var selected = Tab.SqlText.Substring(Tab.SelectionStart, Tab.SelectionLength);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.EqualTo("Status"));
            Assert.That(Tab.SelectionStart,
                Is.EqualTo(Tab.SqlText.LastIndexOf("Status", StringComparison.Ordinal)),
                "the SECOND one, which is what '2 of 2' says");
        });
    }

    [Test]
    public void ATermThatIsNotThereSaysSoTest()
    {
        Tab.OpenSearch(replace: false);
        Tab.Search.Term = "Nonexistent";

        Assert.Multiple(() =>
        {
            Assert.That(Tab.Search.Summary, Is.EqualTo("no matches"));
            Assert.That(Tab.Search.FindNextCommand.CanExecute(null), Is.False);
        });
    }

    /// <summary>
    /// The buttons are live the moment there is something to walk to.
    /// </summary>
    /// <remarks>
    /// This is the defect class that has come out of the running application three times in this
    /// project: <c>RelayCommand</c> does not re-ask <c>CanExecute</c> unless it is told, and
    /// <c>HasMatches</c> is computed, so it raises nothing of its own. Without the wiring at the top of
    /// the ViewModel, Next stays grey in front of a box with two matches in it.
    /// </remarks>
    [Test]
    public void TheButtonsWakeUpWhenTheTermFindsSomethingTest()
    {
        var search = Tab.Search;

        Tab.OpenSearch(replace: false);

        Assert.That(search.FindNextCommand.CanExecute(null), Is.False, "nothing typed yet");

        var raised = 0;
        search.FindNextCommand.CanExecuteChanged += (_, _) => raised++;

        search.Term = "Status";

        Assert.Multiple(() =>
        {
            Assert.That(search.FindNextCommand.CanExecute(null), Is.True);
            Assert.That(raised, Is.GreaterThan(0),
                "and it SAID so - a command that is executable and never announced it leaves a grey button");
        });
    }

    #endregion

    #region Replacing

    [Test]
    public void ReplacingOneChangesOneAndStaysWhereItWasTest()
    {
        var search = Tab.Search;

        Tab.OpenSearch(replace: true);
        search.Term = "Status";
        search.Replacement = "State";

        search.ReplaceCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(Tab.SqlText, Does.Contain("State"));
            Assert.That(search.Matches, Has.Count.EqualTo(1), "one of the two is gone");
            Assert.That(search.Summary, Is.EqualTo("1 of 1"));
        });
    }

    [Test]
    public void ReplaceAllChangesAllOfThemAndSaysHowManyTest()
    {
        var search = Tab.Search;

        Tab.OpenSearch(replace: true);
        search.Term = "Status";
        search.Replacement = "State";

        search.ReplaceAllCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(search.Replaced, Is.EqualTo(2));
            Assert.That(Tab.SqlText, Does.Not.Contain("Status"));
            Assert.That(Tab.SqlText.Split("State"), Has.Length.EqualTo(3), "both of them");
            Assert.That(search.Summary, Is.EqualTo("no matches"), "and the band says so afterwards");
        });
    }

    /// <summary>
    /// Replace is not offered while the band is only finding.
    /// </summary>
    [Test]
    public void ReplaceIsNotOfferedInFindModeTest()
    {
        var search = Tab.Search;

        Tab.OpenSearch(replace: false);
        search.Term = "Status";

        Assert.Multiple(() =>
        {
            Assert.That(search.ReplaceCommand.CanExecute(null), Is.False);

            search.ToggleReplaceCommand.Execute(null);

            Assert.That(search.IsReplaceMode, Is.True);
            Assert.That(search.ReplaceCommand.CanExecute(null), Is.True);
        });
    }

    #endregion

    #region The text moves underneath

    /// <summary>
    /// The band stays open while the query is edited, so its count has to follow the text.
    /// </summary>
    /// <remarks>
    /// A count that was true a moment ago is worse than no count: it is the number a person acts on
    /// when they press Replace All.
    /// </remarks>
    [Test]
    public void TheCountFollowsTheTextTest()
    {
        var search = Tab.Search;

        Tab.OpenSearch(replace: false);
        search.Term = "Status";

        Assert.That(search.Matches, Has.Count.EqualTo(2));

        Tab.SqlText += "\nAND Status IS NOT NULL";

        Assert.That(search.Matches, Has.Count.EqualTo(3),
            "the band is still open and the text has a third one in it now");
    }

    #endregion

    #region In the selection only

    [Test]
    public void InSelectionSearchesOnlyTheSelectionTest()
    {
        var search = Tab.Search;

        var from = SCRIPT.IndexOf("FROM", StringComparison.Ordinal);

        Tab.OpenSearch(replace: false);
        search.Term = "Status";

        Assert.That(search.Matches, Has.Count.EqualTo(2), "both, to begin with");

        Tab.SelectionStart = from;
        Tab.SelectionLength = SCRIPT.Length - from;
        search.InSelection = true;

        Assert.Multiple(() =>
        {
            Assert.That(search.Matches, Has.Count.EqualTo(1), "only the one in the WHERE line");
            Assert.That(search.Matches[0].Offset, Is.GreaterThan(from));
        });
    }

    #endregion
}
