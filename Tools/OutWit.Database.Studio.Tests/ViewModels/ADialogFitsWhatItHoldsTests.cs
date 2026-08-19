using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A dialog does not paint over itself, and a column header is not cut in half.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neither of these can be seen by an assertion</b> - the previous plan said so and it is right:
/// the string is complete in the ViewModel and complete in the accessibility tree, so nothing here
/// knows that <i>Nullable</i> was drawn as <i>Nullab</i>. What can be asserted is the MECHANISM that
/// makes it impossible, which is what this fixture does. The pixels are for driving Studio.
/// </para>
/// <para>
/// <b>Finding 1:</b> the export dialog opened 500x480 for content that needs about 900, could not be
/// resized, and did not scroll - so the <i>Output File</i> label and its box sat underneath the Cancel
/// and Export buttons. A panel that does not fit its row does not clip; it draws over whatever is
/// beneath.
/// </para>
/// <para>
/// <b>Finding 24:</b> the create-table grid gave its two text columns the room and left the narrow
/// ones at widths chosen against English - <i>Nullab</i>, <i>Auto I</i>, <i>Uniqu</i>, <i>Action</i>.
/// Russian is longer again: «Допускает NULL» against <i>Nullable</i>. They size themselves now.
/// </para>
/// </remarks>
[TestFixture]
public class ADialogFitsWhatItHoldsTests
{
    #region Finding 1

    [Test]
    public void TheExportDialogCannotPaintOverItsButtonsTest()
    {
        var markup = Markup("Views/Dialogs/ExportDialog.axaml");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("<ScrollViewer Grid.Row=\"0\""),
                "the content scrolls, so it cannot reach the row the buttons are in");

            Assert.That(markup, Does.Contain("CanResize=\"True\""),
                "and the window can be made bigger by the person looking at it");
        });
    }

    #endregion

    #region Finding 24

    [TestCase("S.Column.Nullable")]
    [TestCase("S.Column.PrimaryKey")]
    [TestCase("S.Column.AutoIncrement")]
    [TestCase("S.Column.Unique")]
    [TestCase("S.Common.Actions")]
    public void ANarrowHeaderSizesItselfTest(string header)
    {
        var markup = Markup("Views/Dialogs/CreateTableDialog.axaml");

        var column = Regex.Match(markup,
            @"<DataGrid\w*Column[^>]*" + Regex.Escape(header) + @"[^>]*?(?:/>|>)",
            RegexOptions.Singleline);

        Assert.That(column.Success, Is.True, $"the grid has a column headed {header}");

        Assert.That(column.Value, Does.Match(@"Width=""(SizeToHeader|Auto|\*)"""),
            "a width in pixels was chosen against one language and cuts the other in half");
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
