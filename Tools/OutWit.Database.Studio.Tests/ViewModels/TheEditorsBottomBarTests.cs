using System.Text.RegularExpressions;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The bar under the table editor: one message, one set of hints, and neither drawn over the other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Finding 31</b> - the conflict message was painted across <i>Ctrl+S: Commit   Del: Delete row</i>,
/// both at full opacity, visible in frame S-32. The message already asked to be trimmed with an
/// ellipsis and could not be: it sits in a horizontal <c>StackPanel</c>, which offers its children
/// infinite width, so the TextBlock never learns there is an edge to trim at and draws straight over
/// the column beside it.
/// </para>
/// <para>
/// <b>Finding 9 turned out the other way round.</b> It said the article promises <i>Ctrl+S to apply
/// and Esc to discard</i>, that Esc does discard, and that only the hint bar is silent about it.
/// Measured here: <b>Escape does not discard anything.</b> In the window it closes the find band, the
/// palette and the notification list, and stops a running query; in a cell it cancels that cell's
/// edit. The buffer is discarded by the toolbar's Discard button and by nothing else - so the hint
/// bar is right to be silent, and the sentence in the article is what has to change.
/// </para>
/// <para>
/// This case pins that, because the tempting fix is to make Escape discard: a destructive action on
/// the key every other panel uses to back out of something harmless.
/// </para>
/// </remarks>
[TestFixture]
public class TheEditorsBottomBarTests
{
    #region Finding 31

    [Test]
    public void AMessageCannotBeDrawnOverTheHintsTest()
    {
        var markup = Markup("Views/Workspace/TableEditView.axaml");

        var bars = Regex.Matches(markup, @"<Grid ColumnDefinitions=""\*,Auto"">");

        Assert.That(bars, Has.Count.GreaterThanOrEqualTo(3),
            "the three status bars - success, error and the default one - are built the same way");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Not.Contain(
                    "<StackPanel Grid.Column=\"0\" Orientation=\"Horizontal\" Spacing=\"8\">"),
                "a horizontal StackPanel gives its child infinite width, so a long message runs over "
                + "the hints beside it instead of being trimmed");

            Assert.That(Regex.Matches(markup, "TextTrimming=\"CharacterEllipsis\""),
                Has.Count.GreaterThanOrEqualTo(2),
                "and the message says what it wants to happen when it does not fit");
        });
    }

    #endregion

    #region Finding 9

    [Test]
    public void EscapeDiscardsNothingAndTheHintsDoNotSaySoTest()
    {
        var window = Markup("Views/MainWindow.axaml.cs");
        var view = Markup("Views/Workspace/TableEditView.axaml");
        var editor = Markup("ViewModels/Tabs/TableEditTabViewModel.cs");

        Assert.Multiple(() =>
        {
            Assert.That(window, Does.Not.Contain("RollbackCommand"),
                "the window's Escape handler does not reach the editor's buffer");

            Assert.That(view, Does.Not.Contain("S.Grid.Hint.Discard"),
                "so the hint bar does not offer a gesture that does not exist");

            // CONTROL: discarding IS reachable, by the button that says so.
            Assert.That(view, Does.Contain("TableEditDiscardChanges"),
                "CONTROL: the Discard button is what throws the buffer away");

            Assert.That(editor, Does.Contain("RollbackCommand = new RelayCommand(RollbackChanges)"),
                "CONTROL: and it is wired to the rollback");
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
