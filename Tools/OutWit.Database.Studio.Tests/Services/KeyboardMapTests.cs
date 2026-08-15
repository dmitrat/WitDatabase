using System.Text.RegularExpressions;
using NUnit.Framework;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Services.Localization;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The keyboard map is checked against the application, in both directions (9.6, WS-69).
/// </summary>
/// <remarks>
/// <para>
/// <b>A help window that disagrees with the keys is worse than none</b>, because it is the one place a
/// person goes when a key did not work. So the map is not allowed to be a description: every gesture
/// in it has to be bound somewhere that really handles it, and every binding the application declares
/// has to be in it.
/// </para>
/// <para>
/// The second direction is the one that rots without a test. Adding a shortcut is one line of markup,
/// and nothing about that line reminds anybody that a list of shortcuts exists somewhere else - which
/// is exactly how the phase-8 capability matrix started promising things nobody had re-measured.
/// </para>
/// </remarks>
[TestFixture]
public class KeyboardMapTests
{
    #region Constants

    /// <summary>A gesture declared in markup: <c>Gesture="Ctrl+K"</c>.</summary>
    private static readonly Regex MARKUP_GESTURE =
        new(@"<KeyBinding[^>]*Gesture=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// The menu's own <c>InputGesture</c>. It is a <c>KeyGesture</c> too and is parsed at the same
    /// moment, so it can kill the window in exactly the same way.
    /// </summary>
    private static readonly Regex MARKUP_INPUT_GESTURE =
        new(@"InputGesture=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// Gestures that are the SYSTEM's or a control's own and are deliberately not in the map: the
    /// menu's Alt+F4, and the copy/paste a text box brings with it. Naming them is what stops the
    /// completeness rule below from being widened until it means nothing.
    /// </summary>
    private static readonly HashSet<string> NOT_STUDIOS_TO_LIST = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alt+F4", "Ctrl+V"
    };

    /// <summary>
    /// A menu item as the markup writes it - everything up to the first <c>&gt;</c>, which is where
    /// its automation id, its command and its printed gesture all sit.
    /// </summary>
    private static readonly Regex MENU_ITEM =
        new(@"<MenuItem\b([^>]*?)/?>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>A whole <c>KeyBinding</c> element, so its gesture and its command are read together.</summary>
    private static readonly Regex KEY_BINDING =
        new(@"<KeyBinding\b([^>]*?)/?>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// What a command binding names. <c>CommandParameter</c> does not match - the attribute name has
    /// to end right there - which is why the <c>=</c> is part of the pattern.
    /// </summary>
    private static readonly Regex COMMAND_BINDING =
        new(@"\bCommand=""\{Binding\s+([^}]+)\}""", RegexOptions.Compiled);

    private static readonly Regex AUTOMATION_ID =
        new(@"AutomationProperties\.AutomationId=""([^""]+)""", RegexOptions.Compiled);

    private static readonly Regex GESTURE_ATTRIBUTE =
        new(@"\bGesture=""([^""]+)""", RegexOptions.Compiled);

    private static readonly Regex INPUT_GESTURE_ATTRIBUTE =
        new(@"\bInputGesture=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// The menu items whose printed gesture is deliberately NOT a Studio binding. Each one is a
    /// decision with a reason, keyed by the item AND the gesture: an exemption that covers whatever
    /// the item happens to print next is a hole in the rule wearing a comment's clothes.
    /// </summary>
    private static readonly Dictionary<(string Id, string Gesture), string> PRINTED_BY_SOMEBODY_ELSE = new()
    {
        [("MainWindowEXit", "Alt+F4")] =
            "the window manager closes the window; Studio has no binding for it and must not have one",
        [("MainWindowCopy", "Ctrl+C")] =
            "the focused control copies - the item carries no Command at all, and KeyboardMap lists Ctrl+C in the RESULT GRID's scope",
        [("MainWindowPaste", "Ctrl+V")] =
            "the focused control pastes - the item carries no Command at all",
        [("QueryEditorCopy", "Ctrl+C")] =
            "the result grid's own copy, scope Grid in KeyboardMap; a window binding would take Ctrl+C away from every text box"
    };

    /// <summary>
    /// How many gestures the menus print, all files counted. Asserted rather than left implicit,
    /// because a rule that matches only what is WRONG reads zero both when the work is done and when
    /// it is pointed at the wrong folder. If a menu item gained or lost one, change this number in the
    /// same commit - and check the new one prints the truth.
    /// </summary>
    private const int PRINTED_GESTURES = 16;

    #endregion

    #region Tests

    /// <summary>
    /// Every gesture the map lists is really handled - in the markup, or in the window's own key
    /// handler for the ones a KeyBinding cannot do.
    /// </summary>
    /// <remarks>
    /// <b>What this cannot promise, said out loud.</b> It shows that a branch for the key EXISTS, not
    /// that the operating system delivers that key here. <c>Ctrl+?</c> is the one where the difference
    /// matters and it could not be driven on this machine at all: the automation tool refuses to send
    /// <c>?</c>, and <c>SendKeys</c> does not reach an Avalonia window - measured, because <c>Ctrl+K</c>
    /// sent the same way did not open the palette either. So the keyboard window was opened from the
    /// MENU in the running application, and the shortcut itself rests on this rule plus the handler.
    /// </remarks>
    [Test]
    public void EveryListedGestureIsActuallyBoundTest()
    {
        var markup = Declared();

        // TWO FILES, because a KeyBinding on the window needs the event to bubble from a FOCUSED
        // element - so the keys that belong to the tree are handled on the tree. The rule read only
        // the shell and would have called F2 and Delete unbound while they worked.
        var code = string.Join("\n",
            File.ReadAllText(Path.Combine(StudioFolder(), "Views", "MainWindow.axaml.cs")),
            File.ReadAllText(Path.Combine(StudioFolder(), "Views", "DatabaseExplorer.axaml.cs")));

        var unbound = new List<string>();

        foreach (var shortcut in KeyboardMap.Shortcuts)
        {
            if (markup.Contains(shortcut.Gesture))
                continue;

            // The handler names them as Key.F / Key.Escape with modifiers alongside; a gesture is
            // "handled in code" when its final key appears in that file.
            var parts = shortcut.Gesture.Split('+');
            var key = parts[^1];

            // A few keys are written for a PERSON one way and named by the Key enum another. "?" is
            // the one that matters here: it is what a person presses and OemQuestion is what arrives.
            // "Enter" is the other: a person presses Enter and the enum calls it Return.
            key = key switch
            {
                "?" => "OemQuestion",
                "Enter" => "Return",
                _ => key
            };

            // THE MODIFIER HAS TO BE THERE TOO, and it is not pedantry: `Alt+Enter` passed this rule
            // the moment it was added, because some other handler in the file already tested
            // `Key.Enter`. A gesture that "passes" on a branch belonging to a different gesture is
            // exactly the false green this whole file exists to prevent.
            var modifiers = parts[..^1]
                .Select(m => m == "Ctrl" ? "Control" : m)
                .ToArray();

            var bound = Regex.IsMatch(code, $@"Key\.{Regex.Escape(key)}\b")
                        && modifiers.All(m => Regex.IsMatch(code, $@"KeyModifiers\.{Regex.Escape(m)}\b"));

            if (bound)
                continue;

            unbound.Add($"{shortcut.ActionKey} = {shortcut.Gesture}");
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a rule that reads no markup passes perfectly, and "everything is bound" looks
            // exactly like "the file moved".
            Assert.That(markup, Has.Count.GreaterThan(8),
                "CONTROL: almost no KeyBinding was read, so this rule is guarding nothing");

            Assert.That(unbound, Is.Empty,
                "the keyboard window would promise these and nothing would happen:"
                + Environment.NewLine + string.Join(Environment.NewLine, unbound));
        });
    }

    /// <summary>
    /// And the other direction: a shortcut the application declares is in the map.
    /// </summary>
    /// <remarks>
    /// This is the half that keeps the window complete. Adding a <c>KeyBinding</c> is one line, and
    /// nothing about that line says a catalogue of shortcuts exists elsewhere.
    /// </remarks>
    [Test]
    public void EveryDeclaredGestureIsInTheMapTest()
    {
        var listed = KeyboardMap.Shortcuts.Select(shortcut => shortcut.Gesture).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Declared()
            .Where(gesture => !listed.Contains(gesture) && !NOT_STUDIOS_TO_LIST.Contains(gesture))
            .Distinct()
            .ToList();

        Assert.That(missing, Is.Empty,
            "these are bound and the keyboard window does not know about them:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// The third direction: a gesture PRINTED in a menu is the gesture that item's command is really
    /// bound to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 17 established by execution that <c>InputGesture</c> on an Avalonia <c>MenuItem</c> is a
    /// LABEL and nothing else - it binds no key. So the two rules above cannot see this class at all:
    /// the printed text is in neither the map nor the list of bindings, and a menu item can advertise
    /// a key that runs something completely different with every test green.
    /// </para>
    /// <para>
    /// Two of the three things this rule has to get right were found by writing it, not by using the
    /// application. <b>Spelling:</b> the menu prints <c>Ctrl+Enter</c> where the binding says
    /// <c>Ctrl+Return</c> - the same key, and an unnormalised rule reports it as a defect on its first
    /// run. <b>The command's name:</b> the tab strip binds through
    /// <c>$parent[UserControl].((vm:WorkspaceTabsViewModel)DataContext).SaveTabCommand</c> and the File
    /// menu binds <c>WorkspaceTabsVm.SaveTabCommand</c>, which are the same command by two paths, so
    /// they are compared by the command's own name.
    /// </para>
    /// <para>
    /// And the failure message says what the printed key ACTUALLY does, because that is the sentence a
    /// user would have written the bug report with.
    /// </para>
    /// </remarks>
    [Test]
    public void EveryPrintedGestureIsTheOneThatCommandIsBoundToTest()
    {
        var bound = BindingsByCommand();
        var printed = PrintedGestures();

        var wrong = new List<string>();
        var checkedItems = 0;

        foreach (var item in printed)
        {
            if (PRINTED_BY_SOMEBODY_ELSE.ContainsKey((item.Id, item.Gesture)))
                continue;

            checkedItems++;

            if (item.Command == null)
            {
                wrong.Add($"{item.Where} prints {item.Gesture} and has no Command to compare it with");
                continue;
            }

            if (!bound.TryGetValue(item.Command, out var gestures))
            {
                wrong.Add($"{item.Where} prints {item.Gesture} and {item.Command} is bound to no key at all");
                continue;
            }

            if (gestures.Any(gesture => Same(gesture, item.Gesture)))
                continue;

            wrong.Add($"{item.Where} prints {item.Gesture} for {item.Command}, which is on "
                      + $"{string.Join(" / ", gestures)} - and {item.Gesture} runs {WhatRuns(bound, item.Gesture)}");
        }

        Assert.Multiple(() =>
        {
            // THE SURFACE, not the defect: with the two labels corrected this rule matches nothing, and
            // "nothing left to find" reads exactly like "the folder moved".
            Assert.That(printed, Has.Count.EqualTo(PRINTED_GESTURES),
                "CONTROL: the menus print a different number of gestures than this rule was measured "
                + "against, so either one was added without being checked or nothing was read");

            Assert.That(checkedItems, Is.EqualTo(PRINTED_GESTURES - PRINTED_BY_SOMEBODY_ELSE.Count),
                "CONTROL: the exemptions are not covering what they were written for");

            // An exemption for an item that no longer prints that gesture is a hole, so it has to
            // still match something.
            var stale = PRINTED_BY_SOMEBODY_ELSE.Keys
                .Where(key => !printed.Any(item => item.Id == key.Id && item.Gesture == key.Gesture))
                .Select(key => $"{key.Id} = {key.Gesture}")
                .ToList();

            Assert.That(stale, Is.Empty,
                "these exemptions no longer name anything, so they are only widening the rule:"
                + Environment.NewLine + string.Join(Environment.NewLine, stale));

            Assert.That(wrong, Is.Empty,
                "the menu promises these keys and another key is what does the work:"
                + Environment.NewLine + string.Join(Environment.NewLine, wrong));
        });
    }

    /// <summary>
    /// Every gesture the markup declares can be PARSED by Avalonia - which is not a formality.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written after a one-line gesture killed the application.</b> The design writes the keyboard
    /// window's shortcut as <c>Ctrl+?</c>, and that is what went into the markup;
    /// <c>KeyGesture.Parse</c> reads the <c>?</c> as the name of a MODIFIER and throws
    /// <i>"Requested value '?' was not found"</i>. The exception happens while the window is being
    /// constructed, so Studio did not start at all - and the whole suite, 769 cases, was green,
    /// because every one of them drives a ViewModel and none of them builds a window.
    /// </para>
    /// <para>
    /// This is the cheapest possible guard for that class: it asks Avalonia's own parser the same
    /// question the window will ask at startup, without needing a window. It cannot catch a binding
    /// that parses and does nothing - <see cref="EveryListedGestureIsActuallyBoundTest"/> is for that -
    /// but it does catch the one that stops the application existing.
    /// </para>
    /// </remarks>
    [Test]
    public void EveryDeclaredGestureCanBeParsedTest()
    {
        var broken = new List<string>();

        foreach (var gesture in Declared().Concat(DeclaredInputGestures()).Distinct())
        {
            try
            {
                Avalonia.Input.KeyGesture.Parse(gesture);
            }
            catch (Exception ex)
            {
                broken.Add($"{gesture}: {ex.Message}");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(Declared().Count + DeclaredInputGestures().Count, Is.GreaterThan(15),
                "CONTROL: almost no gesture was read, so this rule is guarding nothing");

            Assert.That(broken, Is.Empty,
                "these throw while the window is being built, which means Studio does not start:"
                + Environment.NewLine + string.Join(Environment.NewLine, broken));
        });
    }

    /// <summary>
    /// Every action in the map has words in every language.
    /// </summary>
    /// <remarks>
    /// The lesson of "a key built from a name fails by printing itself", which happened three times in
    /// one day on 2026-08-08: a window of catalogue keys shows the KEYS when one is missing, and
    /// <c>Keys.Palette</c> beside <c>Ctrl+K</c> is not help.
    /// </remarks>
    [Test]
    public void EveryActionHasWordsInEveryLanguageTest()
    {
        var localization = new LocalizationService();

        var missing = new List<string>();

        foreach (var language in localization.Available)
        {
            var texts = localization.Texts(language.Code);

            missing.AddRange(KeyboardMap.Shortcuts
                .Where(shortcut => !texts.ContainsKey(shortcut.ActionKey))
                .Select(shortcut => $"{language.Code}: {shortcut.ActionKey}"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(KeyboardMap.Shortcuts, Has.Count.GreaterThan(10),
                "CONTROL: an empty map would pass this perfectly");

            Assert.That(missing, Is.Empty,
                "the window would show the key itself:" + Environment.NewLine
                + string.Join(Environment.NewLine, missing));
        });
    }

    [Test]
    public void NoGestureIsListedTwiceTest()
    {
        var duplicates = KeyboardMap.Shortcuts
            .GroupBy(shortcut => (shortcut.Gesture, shortcut.Scope))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Gesture)
            .ToList();

        Assert.That(duplicates, Is.Empty,
            "two actions on one gesture in one scope means one of them does not happen");
    }

    /// <summary>
    /// On macOS the list says ⌘, because the design asks it to show what works on THIS machine rather
    /// than a universal table with a footnote.
    /// </summary>
    /// <remarks>
    /// Both answers are measured here, on whatever machine this runs: the platform is a parameter of
    /// the function, not a call inside it. A rule about macOS that only a Mac can check is how the
    /// release workflow shipped two platforms of three in silence.
    /// </remarks>
    [Test]
    public void TheModifierIsWrittenAsThisMachineWouldPressItTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KeyboardMap.Display("Ctrl+K", isMac: false), Is.EqualTo("Ctrl+K"));
            Assert.That(KeyboardMap.Display("Ctrl+K", isMac: true), Is.EqualTo("⌘+K"));
            Assert.That(KeyboardMap.Display("Ctrl+Shift+F5", isMac: true), Is.EqualTo("⌘+Shift+F5"));
            Assert.That(KeyboardMap.Display("F5", isMac: true), Is.EqualTo("F5"),
                "a key with no modifier is the same key everywhere");
        });
    }

    #endregion

    #region Tools

    /// <summary>Every gesture the markup declares, across all the views.</summary>
    private static List<string> Declared()
    {
        var views = Path.Combine(StudioFolder(), "Views");

        return Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(file => MARKUP_GESTURE.Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    private static List<string> DeclaredInputGestures()
    {
        var views = Path.Combine(StudioFolder(), "Views");

        return Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(file => MARKUP_INPUT_GESTURE.Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    /// <summary>One menu item that prints a gesture.</summary>
    /// <param name="Where">File and line, so a failure can be opened.</param>
    /// <param name="Id">Its automation id, which is what an exemption is keyed by.</param>
    /// <param name="Gesture">What it prints.</param>
    /// <param name="Command">The command's own name, or null when the item carries no command.</param>
    private sealed record PrintedGesture(string Where, string Id, string Gesture, string? Command);

    /// <summary>Every menu item in every view that prints a gesture.</summary>
    private static List<PrintedGesture> PrintedGestures()
    {
        var printed = new List<PrintedGesture>();

        foreach (var file in ViewFiles())
        {
            var markup = File.ReadAllText(file);

            foreach (Match match in MENU_ITEM.Matches(markup))
            {
                var attributes = match.Groups[1].Value;
                var gesture = INPUT_GESTURE_ATTRIBUTE.Match(attributes);

                if (!gesture.Success)
                    continue;

                var line = markup[..match.Index].Count(character => character == '\n') + 1;

                printed.Add(new PrintedGesture(
                    $"{Path.GetFileName(file)}:{line}",
                    AUTOMATION_ID.Match(attributes) is { Success: true } id ? id.Groups[1].Value : "(no id)",
                    gesture.Groups[1].Value,
                    CommandName(attributes)));
            }
        }

        return printed;
    }

    /// <summary>Command name -> the gestures really bound to it, across every view.</summary>
    private static Dictionary<string, List<string>> BindingsByCommand()
    {
        var bound = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var file in ViewFiles())
        {
            foreach (Match match in KEY_BINDING.Matches(File.ReadAllText(file)))
            {
                var attributes = match.Groups[1].Value;
                var gesture = GESTURE_ATTRIBUTE.Match(attributes);
                var command = CommandName(attributes);

                if (!gesture.Success || command == null)
                    continue;

                if (!bound.TryGetValue(command, out var gestures))
                    bound[command] = gestures = [];

                gestures.Add(gesture.Groups[1].Value);
            }
        }

        return bound;
    }

    /// <summary>
    /// The command's own name out of a binding path. The tab strip reaches its ViewModel through
    /// <c>$parent[UserControl].((vm:WorkspaceTabsViewModel)DataContext)</c> and the File menu reaches
    /// the same command through <c>WorkspaceTabsVm</c>; the tail is what they have in common.
    /// </summary>
    private static string? CommandName(string attributes)
    {
        var binding = COMMAND_BINDING.Match(attributes);

        if (!binding.Success)
            return null;

        var path = binding.Groups[1].Value.Trim();

        return path.Split('.')[^1].Trim();
    }

    /// <summary>
    /// Whether two gestures are the same key. <c>Enter</c> and <c>Return</c> are one key written two
    /// ways, and Avalonia parses both - so a rule that compares the text reports a defect that is not
    /// there.
    /// </summary>
    private static bool Same(string left, string right) =>
        Normalise(left).Equals(Normalise(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalise(string gesture) =>
        gesture.Trim().Replace("Enter", "Return", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the printed key really does, for the failure message: a person reading it wants to know
    /// what happened when they pressed it, not only that the label is wrong.
    /// </summary>
    private static string WhatRuns(Dictionary<string, List<string>> bound, string gesture)
    {
        var owners = bound
            .Where(pair => pair.Value.Any(candidate => Same(candidate, gesture)))
            .Select(pair => pair.Key)
            .ToList();

        return owners.Count == 0 ? "nothing" : string.Join(" / ", owners);
    }

    private static IEnumerable<string> ViewFiles() =>
        Directory.EnumerateFiles(Path.Combine(StudioFolder(), "Views"), "*.axaml", SearchOption.AllDirectories);

    private static string StudioFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
