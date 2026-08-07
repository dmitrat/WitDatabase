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
    // ListBoxItem was added in stage 9, after the running application announced the six colour
    // swatches of the Open dialog as "Avalonia.Controls.Border" - six identical unnamed items, which
    // is the same defect one element type further out again. CheckBox and TabItem are still outside.
    private static readonly Regex INTERACTIVE =
        new(@"<(Button|MenuItem|ToggleButton|RadioButton|ListBoxItem)(\s[^>]*?)?(/?)>",
            RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// An element that is only ever reached through the list it belongs to does not need an
    /// AutomationId of its own - the list has one, and the item is found by NAME inside it. The
    /// swatches are the case: six of them, one list.
    /// </summary>
    private static readonly HashSet<string> NAMED_BUT_NOT_IDENTIFIED = ["ListBoxItem"];

    #endregion

    #region Tests

    [Test]
    public void EveryButtonAndMenuItemCanBeNamedByAutomationTest()
    {
        var views = FindViewsFolder();

        Assert.That(views, Is.Not.Null, "the Views folder was not found from " + AppContext.BaseDirectory);

        var nameless = new List<string>();
        var unannounced = new List<string>();
        var interactive = 0;
        var withChildren = 0;

        foreach (var file in Directory.EnumerateFiles(views!, "*.axaml", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(file);

            foreach (Match match in INTERACTIVE.Matches(markup))
            {
                interactive++;

                var attributes = match.Groups[2].Value;
                var selfClosing = match.Groups[3].Value == "/";

                var line = markup[..match.Index].Count(c => c == '\n') + 1;
                var where = $"{Path.GetFileName(file)}:{line} <{match.Groups[1].Value}>";

                if (!NAMED_BUT_NOT_IDENTIFIED.Contains(match.Groups[1].Value)
                    && !attributes.Contains("AutomationProperties.AutomationId", StringComparison.Ordinal))
                    nameless.Add(where);

                // An element that announces itself does so from Content= or Header=. One whose content
                // is a PANEL has no text of its own and announces as the panel - which is the original
                // defect this guard exists for, "Avalonia.Controls.StackPanel", and the guard could not
                // see it because it only ever asked about the AutomationId.
                //
                // Found by running the application: the three storage cards of the Create dialog and
                // the six colour swatches of the Open dialog all carried Ids and announced as the panel
                // or the Border inside them.
                //
                // The rule is deliberately narrow - a panel, specifically. A first draft asked for a
                // Name from anything with children at all and named 40 menu items that announce
                // perfectly well from their Header.
                if (selfClosing
                    || attributes.Contains("Content=", StringComparison.Ordinal)
                    || attributes.Contains("Header=", StringComparison.Ordinal))
                    continue;

                if (!ContentIsAPanel(markup, match))
                    continue;

                withChildren++;

                if (!attributes.Contains("AutomationProperties.Name", StringComparison.Ordinal))
                    unannounced.Add(where);
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that found nothing would report nothing missing either.
            Assert.That(interactive, Is.GreaterThan(80),
                "CONTROL: too few interactive elements found - the walk is looking in the wrong place");

            // CONTROL for the second rule, for the same reason: if nothing has children, the rule
            // below is asserting about an empty set.
            Assert.That(withChildren, Is.GreaterThan(10),
                "CONTROL: too few elements whose content is markup - the second rule is measuring nothing");

            Assert.That(nameless, Is.Empty,
                "these cannot be found by UI automation:"
                + Environment.NewLine + string.Join(Environment.NewLine, nameless));

            Assert.That(unannounced, Is.Empty,
                "these have no text of their own, so a screen reader announces the panel inside them:"
                + Environment.NewLine + string.Join(Environment.NewLine, unannounced));
        });
    }

    /// <summary>
    /// Whether the element's content is a layout panel rather than text - which is what makes it
    /// announce as the panel. Reads up to the element's own closing tag; a nested element of the same
    /// name would cut it short, and that is acceptable here because everything that nests
    /// (<c>MenuItem</c>) is exempted by its Header before this is reached.
    /// </summary>
    private static bool ContentIsAPanel(string markup, Match element)
    {
        var closing = markup.IndexOf("</" + element.Groups[1].Value, element.Index, StringComparison.Ordinal);

        if (closing < 0)
            return false;

        var body = markup[(element.Index + element.Length)..closing];

        return body.Contains("<StackPanel", StringComparison.Ordinal)
            || body.Contains("<Grid", StringComparison.Ordinal)
            || body.Contains("<DockPanel", StringComparison.Ordinal)
            || body.Contains("<WrapPanel", StringComparison.Ordinal)
            || body.Contains("<Border", StringComparison.Ordinal);
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
