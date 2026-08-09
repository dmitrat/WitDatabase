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
        var code = File.ReadAllText(Path.Combine(StudioFolder(), "Views", "MainWindow.axaml.cs"));

        var unbound = new List<string>();

        foreach (var shortcut in KeyboardMap.Shortcuts)
        {
            if (markup.Contains(shortcut.Gesture))
                continue;

            // The handler names them as Key.F / Key.Escape with modifiers alongside; a gesture is
            // "handled in code" when its final key appears in that file.
            var key = shortcut.Gesture.Split('+')[^1];

            // A few keys are written for a PERSON one way and named by the Key enum another. "?" is
            // the one that matters here: it is what a person presses and OemQuestion is what arrives.
            key = key switch
            {
                "?" => "OemQuestion",
                _ => key
            };

            if (Regex.IsMatch(code, $@"Key\.{Regex.Escape(key)}\b"))
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
