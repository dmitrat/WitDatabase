using System.Text.RegularExpressions;
using OutWit.Database.Studio.Services.Localization;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// No view carries its own text (WS-63).
///
/// <para>
/// A lint over the markup, in the same shape as <c>AutomationSurfaceTests</c> and for the same reason:
/// the defect it guards against is not one a running test can see. A hardcoded caption looks perfectly
/// correct in English and is simply never translated, and nobody notices until someone switches the
/// language and finds half a window in the wrong one.
/// </para>
/// <para>
/// <b>The not-yet-swept files are NAMED rather than implied.</b> The sweep of the older views is
/// mechanical and large; naming them means the rule is real today for everything already done - a new
/// hardcoded caption in a swept file goes red immediately - and the list is the work that is left,
/// which shrinks and can be seen shrinking. A lint that waits for the whole sweep before it guards
/// anything guards nothing for as long as the sweep takes.
/// </para>
/// <para>
/// <b>What this does NOT cover, said out loud.</b> It reads markup only. Text built in a ViewModel -
/// the status bar's "Ready", a notification's summary, a message put together from parts - is outside
/// it, and so is <c>AutomationProperties.Name</c>, which a screen reader announces and which is still
/// English in every view. Both were visible when the running application was switched to Russian: the
/// menu, the welcome screen and the recent list came out Russian and those did not.
/// </para>
/// </summary>
[TestFixture]
public class LocalizationCoverageTests
{
    #region Constants

    /// <summary>The attributes a person reads.</summary>
    private static readonly Regex USER_FACING =
        new(@"(Text|Content|Header|Watermark|ToolTip\.Tip|PlaceholderText)=""([^""{][^""]*)""",
            RegexOptions.Compiled);

    /// <summary>
    /// Views not yet swept into the catalogue. Every name here is work that is left, and the list only
    /// ever gets shorter. Deleting a name without doing the sweep turns this test red.
    /// </summary>
    private static readonly HashSet<string> NOT_YET_SWEPT = new(StringComparer.OrdinalIgnoreCase)
    {
        "ImportDialog.axaml",
        "QueryEditor.axaml",
        "TableEditView.axaml",
        "StructureView.axaml",
        "ExportDialog.axaml",
        "CreateIndexDialog.axaml",
        "DatabaseExplorer.axaml",
        "CreateTableDialog.axaml",
        "EditTriggerDialog.axaml",
        "WorkspaceTabStrip.axaml",
        "ObjectInspector.axaml",
        "CreateViewDialog.axaml",
        "TableRebuildDialog.axaml",
        "UnsavedChangesDialog.axaml"
    };

    /// <summary>
    /// Text that is NOT Studio's own and must not be translated (WS-64): the terms of the engine, the
    /// language and the formats. A caption that is exactly one of these is left in the markup, because
    /// putting it in the catalogue would invite a translator to translate it.
    /// </summary>
    private static readonly HashSet<string> NOT_STUDIO_TEXT = new(StringComparer.Ordinal)
    {
        "B-Tree", "LSM", "SQL", "CSV", "JSON", "MVCC", "WitDatabase", "WitDatabase Studio",
        "AES-256-GCM", "ChaCha20", "Apache 2.0", "SQL INSERT", "Markdown",
        "1 · File", "2 · Destination", "3 · Columns",

        // A key gesture is not translated either: Ctrl is Ctrl, and the one place a gesture DOES
        // change is macOS, where the whole map is rewritten rather than each label.
        "Ctrl+K", "Ctrl+O", "Ctrl+N", "Ctrl+T", "Ctrl+S", "Ctrl+F", "F5", "Esc"
    };

    #endregion

    #region Tests

    [Test]
    public void NoSweptViewCarriesItsOwnTextTest()
    {
        var views = FindViewsFolder();

        Assert.That(views, Is.Not.Null, "the Views folder was not found from " + AppContext.BaseDirectory);

        var hardcoded = new List<string>();
        var swept = 0;
        var checkedAttributes = 0;

        foreach (var file in Directory.EnumerateFiles(views!, "*.axaml", SearchOption.AllDirectories))
        {
            if (NOT_YET_SWEPT.Contains(Path.GetFileName(file)))
                continue;

            swept++;

            var markup = File.ReadAllText(file);

            foreach (Match match in USER_FACING.Matches(markup))
            {
                var value = match.Groups[2].Value.Trim();

                if (value.Length == 0 || NOT_STUDIO_TEXT.Contains(value))
                    continue;

                checkedAttributes++;

                var line = markup[..match.Index].Count(c => c == '\n') + 1;

                hardcoded.Add($"{Path.GetFileName(file)}:{line} {match.Groups[1].Value}=\"{value}\"");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: with every view on the not-yet-swept list this would pass having read nothing.
            Assert.That(swept, Is.GreaterThan(3),
                "CONTROL: almost every view is excused, so this rule is guarding nothing");

            Assert.That(hardcoded, Is.Empty,
                "these are Studio's own text and are not in the catalogue, so they will never be translated:"
                + Environment.NewLine + string.Join(Environment.NewLine, hardcoded));
        });
    }

    /// <summary>
    /// The list of unswept views only shrinks. A name for a file that no longer exists is a name
    /// somebody forgot to delete, and it would go on excusing a file that came back under it.
    /// </summary>
    [Test]
    public void EveryNameOnTheUnsweptListIsARealViewTest()
    {
        var views = FindViewsFolder();

        var present = Directory.EnumerateFiles(views!, "*.axaml", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.That(NOT_YET_SWEPT.Where(name => !present.Contains(name)), Is.Empty,
            "these are excused from the sweep and do not exist");
    }

    /// <summary>
    /// And the catalogue is what the swept views read from, so it has to be reachable from the test
    /// assembly too - the same embedded resources the application uses, not a copy.
    /// </summary>
    [Test]
    public void TheCatalogueTheViewsReadFromIsTheOneThatShipsTest()
    {
        var localization = new LocalizationService();

        Assert.Multiple(() =>
        {
            Assert.That(localization["Dialog.Open.Connect"], Is.EqualTo("Connect"));
            Assert.That(localization.Texts("ru"), Is.Not.Empty);
        });
    }

    #endregion

    #region Tools

    private static string? FindViewsFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio", "Views");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    #endregion
}
