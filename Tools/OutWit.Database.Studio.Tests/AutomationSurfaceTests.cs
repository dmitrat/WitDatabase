using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// Studio has to be drivable by UI automation, and this checks that it stays that way.
///
/// Before phase 14 it was not: most buttons announced as <c>Avalonia.Controls.StackPanel</c> - the
/// name of the panel holding their icon - and the SQL editor was not an element at all, because
/// AvaloniaEdit ships no automation peer. A screen reader could read neither. Nor could a test: the
/// entire redesign ahead would have had no verification except looking at screenshots.
///
/// So every button and menu item in the shipping views carries an AutomationId, and the ones with no
/// text of their own carry a Name as well. This is a lint over the markup rather than a run of the
/// application - it cannot prove a peer works, only that nothing was added without a handle. The
/// application is driven for real separately.
/// </summary>
[TestFixture]
public class AutomationSurfaceTests
{
    #region Constants

    // RadioButton was added in stage 8, after the running application announced the structure tab's
    // section strip as "Avalonia.Controls.StackPanel" - the same defect this guard exists for, in an
    // element type it was not looking at. CheckBox and TabItem are still outside it; see the phase
    // document.
    private static readonly Regex INTERACTIVE = new(@"<(Button|MenuItem|ToggleButton|RadioButton)(\s[^>]*?)?(/?)>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    #endregion

    #region Tests

    [Test]
    public void EveryButtonAndMenuItemCanBeNamedByAutomationTest()
    {
        var views = FindViewsFolder();

        Assert.That(views, Is.Not.Null, "the Views folder was not found from " + AppContext.BaseDirectory);

        var nameless = new List<string>();
        var interactive = 0;

        foreach (var file in Directory.EnumerateFiles(views!, "*.axaml", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(file);

            foreach (Match match in INTERACTIVE.Matches(markup))
            {
                interactive++;

                var attributes = match.Groups[2].Value;

                if (attributes.Contains("AutomationProperties.AutomationId", StringComparison.Ordinal))
                    continue;

                var line = markup[..match.Index].Count(c => c == '\n') + 1;
                nameless.Add($"{Path.GetFileName(file)}:{line} <{match.Groups[1].Value}>");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that found nothing would report nothing missing either.
            Assert.That(interactive, Is.GreaterThan(80),
                "CONTROL: too few interactive elements found - the walk is looking in the wrong place");

            Assert.That(nameless, Is.Empty,
                "these cannot be found by UI automation or announced by a screen reader:"
                + Environment.NewLine + string.Join(Environment.NewLine, nameless));
        });
    }

    /// <summary>
    /// The SQL editor is the one control that needed code rather than markup: TextEditor has no peer,
    /// so text sent to the focused element went nowhere at all.
    /// </summary>
    [Test]
    public void TheSqlEditorExposesAnAutomationPeerTest()
    {
        var peer = typeof(Studio.Controls.SqlEditorAutomationPeer);

        Assert.Multiple(() =>
        {
            Assert.That(typeof(Avalonia.Automation.Provider.IValueProvider).IsAssignableFrom(peer), Is.True,
                "the peer has to expose the query as a value, or automation can read it but not write it");

            var factory = typeof(Studio.Controls.SqlEditor).GetMethod(
                "OnCreateAutomationPeer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(factory?.DeclaringType, Is.EqualTo(typeof(Studio.Controls.SqlEditor)),
                "the editor has to hand that peer out - a peer nothing returns is not a peer");
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
