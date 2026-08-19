using System.Text.RegularExpressions;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The import wizard reports the file it read, and keeps every row the database refused.
/// </summary>
/// <remarks>
/// <para>
/// <b>Finding 25:</b> step 1 said <i>File contains approximately 13 rows   0 columns</i> for a CSV
/// with four, and <b>Refresh Preview</b> changed nothing. The count was read from
/// <c>ColumnMappings</c>, which is built on step 3 - so on step 1 it was asking a list that does not
/// exist yet. Catching a wrong delimiter before anything is written is what a preview is for, and
/// "0 columns" is exactly what a wrong delimiter would look like if the number meant anything.
/// </para>
/// <para>
/// <b>Finding 27 was half built, which is why it was reported as absent.</b> The CSV path has kept
/// every rejection since WS-36 and the window has a button that writes them to a file beside the
/// source. <b>The JSON path threw away everything past the tenth</b> - it filled the display list and
/// recorded nothing - so an import of a JSON file really did report "15 failed", name ten, and have
/// nothing to save. Both paths record now.
/// </para>
/// <para>
/// <b>Finding 26:</b> the result was painted across the line it replaces because two borders share
/// one grid cell. A cell holds one thing.
/// </para>
/// </remarks>
[TestFixture]
public class TheImportWizardSaysWhatItSawTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ImportViewModel m_import = null!;
    private string m_csv = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        m_csv = Path.Combine(m_studio.Root, "people.csv");

        await File.WriteAllTextAsync(m_csv,
            "Id,Name,Email,City" + Environment.NewLine
            + "1,Ada,ada@example.com,London" + Environment.NewLine
            + "2,Grace,grace@example.com,New York" + Environment.NewLine);

        m_import = new ImportViewModel(m_studio.App);

        await m_import.InitializeAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        m_import.Dispose();

        await m_studio.DisposeAsync();
    }

    #endregion

    #region Finding 25

    [Test]
    public async Task TheFirstStepCountsTheColumnsItReadTest()
    {
        m_import.InputPath = m_csv;

        await StudioFixture.PressAsync(m_import.PreviewCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_import.PreviewData, Is.Not.Null, "the file was parsed");

            Assert.That(m_import.PreviewData!.Columns, Has.Count.EqualTo(4));

            Assert.That(m_import.PreviewColumnsSummary, Does.Contain("4"),
                "the line says four - it used to ask the mapping list, which is built two steps later");
        });
    }

    [Test]
    public async Task TheRowsItReadCanBeLookedAtTest()
    {
        m_import.InputPath = m_csv;

        await StudioFixture.PressAsync(m_import.PreviewCommand);

        Assert.That(m_import.PreviewData!.Rows, Has.Count.EqualTo(2),
            "the parsed rows are there to be shown - a preview that shows nothing catches no delimiter");

        Assert.That(Markup("Views/Dialogs/ImportDialog.axaml"), Does.Contain("ImportPreviewRowsGrid"),
            "and the window draws them");
    }

    #endregion

    #region Finding 26

    [Test]
    public void TheResultDoesNotLandOnTopOfThePreviewTest()
    {
        // Comments stripped first: this fixture is about the markup, and the comment explaining
        // the fix mentions the cell it is about.
        var markup = Regex.Replace(Markup("Views/Dialogs/ImportDialog.axaml"),
            @"<!--[\s\S]*?-->", string.Empty);

        Assert.That(Regex.Matches(markup, @"Grid\.Row=""5"""), Has.Count.EqualTo(1),
            "one thing in the cell: two borders in the same one are drawn over each other, which is "
            + "how the result came to be painted across the line it replaces");
    }

    #endregion

    #region Finding 27

    /// <summary>
    /// Both import paths keep every rejection, so the button that writes them out has them all.
    /// </summary>
    /// <remarks>
    /// Asserted on the source, because reaching the JSON path here would mean importing into a table
    /// whose rows the engine refuses one at a time - a scenario that measures the engine rather than
    /// this wizard. What is checked is that neither path records ONLY the ten it shows.
    /// </remarks>
    [Test]
    public void NeitherImportPathThrowsAwayARejectionTest()
    {
        var source = Source("ViewModels/ImportViewModel.cs");

        var displayed = Regex.Matches(source, @"ImportErrors\.Add\(");
        var recorded = Regex.Matches(source, @"Rejected\.Add\(");

        Assert.Multiple(() =>
        {
            Assert.That(displayed, Has.Count.EqualTo(2), "CSV and JSON, one display list each");

            Assert.That(recorded, Has.Count.EqualTo(2),
                "and each of them records the rejection as well - the JSON path used to fill the "
                + "display list and keep nothing");
        });
    }

    [Test]
    public void TheButtonThatWritesThemOutFollowsTheRejectionsTest()
    {
        var markup = Markup("Views/Dialogs/ImportDialog.axaml");

        var button = Regex.Match(markup, @"<Button[^>]*ImportWriteReport[\s\S]*?/>");

        Assert.That(button.Success, Is.True, "the window offers to write them out");

        Assert.Multiple(() =>
        {
            Assert.That(button.Value, Does.Contain("WriteReportCommand"));

            Assert.That(button.Value, Does.Contain("IsVisible=\"{Binding Rejected.Count}\""),
                "and it is offered exactly when there is something to write");
        });
    }

    #endregion

    #region Tools

    private static string Markup(string relative) => Source(relative);

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
