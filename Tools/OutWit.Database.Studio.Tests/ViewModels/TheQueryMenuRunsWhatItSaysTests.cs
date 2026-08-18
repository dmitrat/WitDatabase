using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The Query menu offers the three ways of running, each under its own name.
/// </summary>
/// <remarks>
/// <para>
/// Finding 37, and the question left open when the labels were corrected in 3.0.0-rc.2: the menu's
/// <b>Execute</b> carried <c>Ctrl+Shift+F5</c>, which is the whole script. The keyboard reference gives
/// <c>F5</c> to <i>run the statement under the cursor</i> and <c>Ctrl+Shift+F5</c> to <i>run the whole
/// script</i>, and the toolbar has had <b>Execute</b> and <b>Script</b> as separate buttons all along.
/// So the menu had two of the three, one of them under the other's name.
/// </para>
/// <para>
/// <b>The decision taken here:</b> <i>Execute</i> is the statement under the cursor, because that is
/// what <c>F5</c> does and what the toolbar's Execute does; the script gets its own item. The
/// alternative - leaving Execute on the script and renaming it - would have left the menu and the
/// toolbar disagreeing about the word <i>Execute</i>.
/// </para>
/// <para>
/// The gestures themselves are checked by <c>KeyboardMapTests</c>, which compares every printed
/// gesture against the command it is on. This fixture is about which command each item runs.
/// </para>
/// </remarks>
[TestFixture]
public class TheQueryMenuRunsWhatItSaysTests
{
    [TestCase("MainWindowExecute", "ExecuteCurrentStatementCommand", "F5")]
    [TestCase("MainWindowExecuteScript", "ExecuteQueryCommand", "Ctrl+Shift+F5")]
    [TestCase("MainWindowExecuteSelection", "ExecuteSelectionCommand", "Ctrl+Enter")]
    public void EachWayOfRunningHasItsOwnItemTest(string id, string command, string gesture)
    {
        var markup = Markup("Views/MainWindow.axaml");

        var item = Regex.Match(markup,
            @"<MenuItem[^>]*AutomationId=""" + id + @"""[^>]*(?:\r?\n[^>]*)*?>",
            RegexOptions.Multiline);

        Assert.That(item.Success, Is.True, $"the Query menu has an item called {id}");

        Assert.Multiple(() =>
        {
            Assert.That(item.Value, Does.Contain(command),
                "the item runs the command its name describes");

            Assert.That(item.Value, Does.Contain($"InputGesture=\"{gesture}\""),
                "and prints the gesture that runs the same thing");
        });
    }

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
}
