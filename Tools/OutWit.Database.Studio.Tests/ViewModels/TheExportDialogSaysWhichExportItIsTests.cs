using System.Text.Json;
using System.Text.RegularExpressions;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Two exports, two names - and a progress panel that waits until it has a number.
/// </summary>
/// <remarks>
/// <para>
/// <b>Finding 5:</b> <i>Tools ▸ Export…</i> and <i>Export Results…</i> are not the same dialog - the
/// first exports a TABLE chosen from a list, the second exports the query result and carries the three
/// scopes with their row counts - and both were called <b>Export Data</b> in the title bar, under a
/// menu entry called <i>Export…</i>. Somebody looking in Tools for the scopes will not find them.
/// </para>
/// <para>
/// <b>Finding 32:</b> the first tick of a large export read <i>Exporting... 0 / 0 rows</i> at
/// <i>0.0 %</i>, because the panel opens when the export starts and the rows are fetched after that.
/// Zero out of zero is not a small number, it is no number - and it is the frame anybody
/// photographing an export in progress is most likely to catch.
/// </para>
/// <para>
/// <b>Finding 4:</b> the output path could only be set with <b>Browse…</b>, which opens the system
/// dialog on the user's own folders - awkward while a screen is being recorded, and slower than
/// typing a path somebody already knows.
/// </para>
/// </remarks>
[TestFixture]
public class TheExportDialogSaysWhichExportItIsTests
{
    #region Finding 5

    [TestCase("en")]
    [TestCase("ru")]
    public void TheTwoExportsHaveTwoNamesTest(string language)
    {
        using var catalogue = JsonDocument.Parse(Source($"Resources/Strings.{language}.json"));

        var table = catalogue.RootElement.GetProperty("Dialog.Export.Title.Table").GetString();
        var results = catalogue.RootElement.GetProperty("Dialog.Export.Title.Results").GetString();
        var menu = catalogue.RootElement.GetProperty("Menu.Export").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(table, Is.Not.EqualTo(results),
                "the two windows do not share a name, because they do not do the same thing");

            Assert.That(menu, Is.Not.Null.And.Not.Empty);

            Assert.That(menu!.Replace("_", string.Empty).TrimEnd('.', '…'),
                Does.Contain(table!.Split(' ')[^1]),
                "and the menu entry says which of the two it opens");
        });
    }

    [Test]
    public async Task TheWindowNamesItselfAfterWhatItIsExportingTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        using var export = new ExportViewModel(studio.App);

        var forTable = export.WindowTitle;

        // The same window, handed a query result: this is the call the result grid makes.
        using var data = new System.Data.DataTable();
        data.Columns.Add("Id");

        export.SetDataSource(data, "SELECT 1", null, 0);

        Assert.That(export.WindowTitle, Is.Not.EqualTo(forTable),
            "the same window used for the other job says so in its title");
    }

    #endregion

    #region Finding 32

    [Test]
    public async Task NoNumberIsShownBeforeThereIsOneTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        using var export = new ExportViewModel(studio.App);

        Assert.That(export.KnowsHowManyRows, Is.False,
            "nothing has been counted yet, so there is nothing to say");

        var markup = Source("Views/Dialogs/ExportDialog.axaml");

        var progress = Regex.Match(markup, @"<TextBlock[^>]*ExportProgressText[\s\S]*?/>");

        Assert.That(progress.Success, Is.True);

        Assert.That(progress.Value, Does.Contain("KnowsHowManyRows"),
            "and the panel's numbers wait for the total rather than opening at 0 / 0");
    }

    #endregion

    #region Finding 4

    [Test]
    public void TheOutputPathCanBeTypedTest()
    {
        var markup = Source("Views/Dialogs/ExportDialog.axaml");

        var box = Regex.Match(markup, @"<TextBox[^>]*Text=""\{Binding OutputPath\}""[\s\S]*?/>");

        Assert.That(box.Success, Is.True, "the dialog has a box for the path");

        Assert.That(box.Value, Does.Not.Contain("IsReadOnly=\"True\""),
            "a path can be typed as well as browsed for");
    }

    #endregion

    #region Finding 28

    [Test]
    public void TheCollisionPolicyIsOnTheStepAboutTheTargetTest()
    {
        var markup = Source("Views/Dialogs/ImportDialog.axaml");

        var block = Regex.Match(markup,
            @"<StackPanel[^>]*Grid\.Row=""3""[\s\S]*?ImportConflictSkip");

        Assert.That(block.Success, Is.True, "the collision policy is in the wizard");

        Assert.That(block.Value, Does.Contain("IsDestination"),
            "and on the step named after the thing it is about - it sat on «Columns», which left that "
            + "step holding two unrelated decisions");
    }

    #endregion

    #region Tools

    private static string Source(string relative)
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
