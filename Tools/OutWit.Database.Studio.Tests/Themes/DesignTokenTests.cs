using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using OutWit.Database.Studio;
using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Tests.Themes;

[assembly: AvaloniaTestApplication(typeof(TokenHost))]

namespace OutWit.Database.Studio.Tests.Themes;

/// <summary>
/// The application that carries the resources under test. It is Studio's own <see cref="App"/>, so
/// what is measured here is the dictionary the shipping executable loads - not a copy of it written
/// for the test.
///
/// <para>
/// <c>Program.BuildAvaloniaApp</c> cannot be reused: it calls <c>UsePlatformDetect</c> and hangs an
/// <c>AfterSetup</c> on the DI container. The headless lifetime is not
/// <c>IClassicDesktopStyleApplicationLifetime</c>, so <c>OnFrameworkInitializationCompleted</c> does
/// nothing and no window, no service and no settings file is touched. <c>Initialize()</c> still runs,
/// which is the part that loads <c>App.axaml</c> and with it the three token dictionaries.
/// </para>
/// </summary>
public static class TokenHost
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Phase 16, stage V0. The palette, the type scale and the metric table of section 8 exist as
/// resources, and every one of them resolves in <b>both</b> theme variants.
///
/// <para>
/// <b>WHY THIS ONE HAS TO EXECUTE.</b> Every other lint in this project reads source text, and for
/// this subject that would be worthless. A resource key is not a symbol: nothing checks it, the
/// compiler is happy, and a <c>{DynamicResource Wit.Ink}</c> that resolves to nothing leaves the
/// property at whatever it already had. Stage 10 met the sharper version of the same thing - a
/// <c>FallbackValue={DynamicResource S.X}</c> did not evaluate at all and Avalonia assigned the
/// markup extension OBJECT, so the status bar displayed a type name. Reading the markup cannot tell
/// any of that from correct code. Asking Avalonia can.
/// </para>
/// <para>
/// <b>The blindness this instrument would otherwise have.</b> A walk that finds no dictionaries
/// passes every assertion about them. So the token names are asserted as a NAMED SET rather than a
/// count - a list you can read against section 8.1 - and one token is asserted to hold a DIFFERENT
/// value in each variant. Without that second case, both lookups could be reading the same
/// dictionary and every "resolves in both variants" assertion would still be green.
/// </para>
/// <para>
/// <b>Both sabotages were run and each gives a different red set</b>, which is the only way to know
/// the four cases are measuring four things. Deleting <c>Wit.Color.Ink.Null</c> from the Light
/// dictionary alone reddens three - the symmetry case, the resolution case and the markup case - and
/// collapsing the accent split reddens exactly one. <b>What the first sabotage also established is
/// the failure mode itself: a dangling <c>{StaticResource}</c> inside a ThemeDictionary does not
/// throw and does not stop the application loading.</b> The brush was simply not there, and every
/// other case stayed green.
/// </para>
/// <para>
/// <b>It has already caught a defect the BUILD could not, and it is the phase-14 shape exactly.</b>
/// Stage V2's control styles used <c>{StaticResource Wit.Height.Field}</c>; <c>Application.Styles</c>
/// is populated <b>before</b> <c>Application.Resources</c>, so the lookup threw inside
/// <c>App.Initialize()</c> and <b>Studio would not have started at all</b>. The build was clean and
/// every one of the other 842 cases stayed green - they construct ViewModels, and none constructs an
/// <c>Application</c>. This is `Ctrl+?` again, a one-line change that killed the executable while the
/// suite said nothing, and this time a test caught it instead of a launch. A style setter must use
/// <c>DynamicResource</c>, which resolves when the style is applied rather than when it is parsed.
/// </para>
/// <para>
/// <b>What it does not prove, said out loud.</b> That a token is USED, that it is used in the right
/// place, or that the result is legible. The first is the census below, the second and third are the
/// running application in both themes, which is stage V4 and the only real control this phase has.
/// </para>
/// </summary>
[TestFixture]
public class DesignTokenTests
{
    #region Constants

    /// <summary>
    /// The palette of section 8.1 and 8.4, named. Thirty-three roles; every one of them is defined
    /// in both variants and most of them differ between the two.
    ///
    /// <para>
    /// This is a LIST and not a count on purpose. A count goes green again when one token is dropped
    /// and another added, and the sentence "0 of 33 tokens are in use" that opened this phase was
    /// itself a count of CSS variables in the canon's stylesheet rather than of anything Studio
    /// could hold. The list is what a reader can check against the document.
    /// </para>
    /// <para>
    /// <c>Scrim</c> is the one role section 8 does not name. It was added during the sweep because
    /// the application has NINE overlays that dim a window while work happens, in two different
    /// weights, and a colour with nine sites and no name is exactly what this phase is removing.
    /// </para>
    /// </summary>
    private static readonly string[] COLOUR_TOKENS =
    [
        "Accent", "Accent.Dim", "Accent.Ink", "Accent.Text",
        "Brand.Navy", "Brand.Neon",
        "Danger.Text",
        "Error", "Error.Border", "Error.Surface", "Error.Text",
        "Ink", "Ink.Data", "Ink.Muted", "Ink.Null", "Ink.Secondary",
        "Label.Cyan", "Label.Off", "Label.Violet",
        "Line", "Line.Inner",
        "Ok", "Ok.Border", "Ok.Surface", "Ok.Text",
        "Scrim",
        "Surface.Bar", "Surface.Base", "Surface.Panel", "Surface.Sunken",
        "Warn", "Warn.Border", "Warn.Surface", "Warn.Text",
    ];

    /// <summary>The nine steps of section 8.2, in the order the canon prints them.</summary>
    private static readonly (string Key, double Size)[] TYPE_SCALE =
    [
        ("Wit.Font.WindowTitle", 18),
        ("Wit.Font.SectionTitle", 14),
        ("Wit.Font.BlockTitle", 12.5),
        ("Wit.Font.Body", 12),
        ("Wit.Font.Control", 11.5),
        ("Wit.Font.Caption", 10.5),
        ("Wit.Font.Mono.Label", 9.5),
        ("Wit.Font.Mono.Data", 11),
        ("Wit.Font.Mono.Sql", 13),
    ];

    /// <summary>The metric table of section 8.2.</summary>
    private static readonly (string Key, double Value)[] METRICS =
    [
        ("Wit.Height.TitleBar", 38),
        ("Wit.Height.Toolbar", 36),
        ("Wit.Height.Tab", 34),
        ("Wit.Height.StatusBar", 25),
        ("Wit.Height.TreeRow", 25),
        ("Wit.Height.GridRow", 26),
        ("Wit.Height.DesignerRow", 31),
        ("Wit.Height.Field", 28),
        ("Wit.Width.Connections", 250),
        ("Wit.Width.Inspector", 310),
        ("Wit.Width.SettingsNav", 172),
        ("Wit.Icon.Stroke", 1.7),
    ];

    #endregion

    #region Rule 1 - every token resolves, in both variants

    /// <summary>
    /// Every colour role of section 8.1 exists as a <c>Color</c> and as a <c>SolidColorBrush</c>, and
    /// both resolve under Dark and under Light.
    ///
    /// <para>
    /// The brush is not redundant. Markup binds brushes; the SQL editor and the highlighting
    /// definition need colours. A palette that publishes one of the two forces the other to be
    /// written by hand, which is where the hexes came from in the first place.
    /// </para>
    /// </summary>
    [AvaloniaTest]
    public void EveryColourTokenResolvesInBothVariantsTest()
    {
        var missing = new List<string>();

        foreach (var role in COLOUR_TOKENS)
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            if (Resolve($"Wit.{role}", variant) is not ISolidColorBrush)
                missing.Add($"{variant}: Wit.{role} does not resolve to a brush");

            if (Resolve($"Wit.Color.{role}", variant) is not Color)
                missing.Add($"{variant}: Wit.Color.{role} does not resolve to a colour");
        }

        Assert.That(missing, Is.Empty, string.Join("\n", missing));
    }

    /// <summary>
    /// The control on the walk above, and the reason it is here: <b>the theme variant is actually
    /// being honoured</b>.
    ///
    /// <para>
    /// #4CC13C on white is about 2 : 1 - enough as a fill under dark text, not enough as text. So the
    /// accent splits: the fill is the same colour in both themes and the TEXT moves to #2E7D22 in the
    /// light one. That makes this pair the one place where a variant-blind lookup is visible, and it
    /// is why the case exists. Point both dictionaries at the same value and every assertion in the
    /// test above stays green.
    /// </para>
    /// </summary>
    [AvaloniaTest]
    public void TheAccentSplitsBetweenFillAndTextInTheLightThemeTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Resolve("Wit.Color.Accent.Text", ThemeVariant.Dark),
                Is.EqualTo(Color.Parse("#4CC13C")), "dark: accent text is the accent");
            Assert.That(Resolve("Wit.Color.Accent.Text", ThemeVariant.Light),
                Is.EqualTo(Color.Parse("#2E7D22")), "light: accent text is the darker green");

            // The fill does not move, in either direction. Half of this pair changing alone is the
            // mistake, not the change.
            Assert.That(Resolve("Wit.Color.Accent", ThemeVariant.Dark),
                Is.EqualTo(Resolve("Wit.Color.Accent", ThemeVariant.Light)), "the fill is invariant");
            Assert.That(Resolve("Wit.Color.Accent.Ink", ThemeVariant.Dark),
                Is.EqualTo(Resolve("Wit.Color.Accent.Ink", ThemeVariant.Light)), "so is the text on it");
        });
    }

    /// <summary>
    /// The two variants define exactly the same keys.
    ///
    /// <para>
    /// A key present in one dictionary and absent from the other is the failure this whole file is
    /// built around, and it is silent: no warning, no exception, the property simply keeps what it
    /// had. It is also the easy mistake, because the two dictionaries are edited one after the other
    /// and the second is a hundred lines below the first.
    /// </para>
    /// </summary>
    [AvaloniaTest]
    public void BothVariantsDefineTheSameKeysTest()
    {
        var dark = VariantKeys(ThemeVariant.Dark);
        var light = VariantKeys(ThemeVariant.Light);

        Assert.Multiple(() =>
        {
            Assert.That(dark.Except(light), Is.Empty, "defined in Dark and missing from Light");
            Assert.That(light.Except(dark), Is.Empty, "defined in Light and missing from Dark");

            // The surface. Without it, a walk that finds no theme dictionaries at all satisfies both
            // assertions above with two empty sets.
            Assert.That(dark, Has.Count.EqualTo(COLOUR_TOKENS.Length * 2),
                "a colour and a brush for each role of section 8.1");
        });
    }

    /// <summary>
    /// The nine-step scale and the metric table, which do not vary with the theme and are therefore
    /// asserted by VALUE. A number that drifts from the canon is the defect here, not a missing key.
    /// </summary>
    [AvaloniaTest]
    public void TheTypeScaleAndTheMetricTableMatchTheCanonTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (key, size) in TYPE_SCALE)
                Assert.That(Resolve(key, ThemeVariant.Dark), Is.EqualTo(size), key);

            foreach (var (key, value) in METRICS)
                Assert.That(Resolve(key, ThemeVariant.Dark), Is.EqualTo(value), key);

            // Both families are lists rather than one name: Cascadia Code ships with Windows
            // Terminal and with nothing else, so on a bare Linux box the fallbacks are what draws.
            foreach (var key in new[] { "Wit.FontFamily.Ui", "Wit.FontFamily.Mono" })
            {
                Assert.That(Resolve(key, ThemeVariant.Dark), Is.InstanceOf<FontFamily>(), key);
                Assert.That(((FontFamily)Resolve(key, ThemeVariant.Dark)!).FamilyNames.Count,
                    Is.GreaterThan(1), $"{key} has no fallback");
            }

            foreach (var key in new[]
                     {
                         "Wit.Radius.Check", "Wit.Radius.Button", "Wit.Radius.Field",
                         "Wit.Radius.Block", "Wit.Radius.Window", "Wit.Radius.Chip",
                     })
                Assert.That(Resolve(key, ThemeVariant.Dark), Is.InstanceOf<CornerRadius>(), key);
        });
    }

    #endregion

    #region Rule 2 - every token the markup asks for exists

    /// <summary>
    /// Every <c>Wit.*</c> key named in a view resolves.
    ///
    /// <para>
    /// This is the rule that grows teeth as the sweep proceeds: today the markup asks for almost
    /// nothing, and by the end of stage V1 it asks for the whole palette. A typo in a resource key
    /// is invisible in every other way - the build passes, the view loads, and one property silently
    /// keeps its inherited value.
    /// </para>
    /// <para>
    /// The surface is counted for the reason stage 10 wrote down: a rule that matches only what is
    /// WRONG matches nothing once the work is done, and "nothing left to find" and "the regex is
    /// reading the wrong folder" produce the same number.
    /// </para>
    /// </summary>
    [AvaloniaTest]
    public void EveryTokenTheMarkupAsksForResolvesTest()
    {
        var referenced = new SortedSet<string>();

        foreach (var file in MarkupFiles())
        foreach (Match match in Regex.Matches(File.ReadAllText(file),
                     @"\{(?:Dynamic|Static)Resource\s+(Wit\.[A-Za-z.]+)\s*\}"))
            referenced.Add(match.Groups[1].Value);

        var unresolved = referenced
            .Where(key => Resolve(key, ThemeVariant.Dark) is null || Resolve(key, ThemeVariant.Light) is null)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty, string.Join(", ", unresolved));

            // The control. It is zero at V0 by construction - the dictionaries exist and nothing
            // consumes them yet - so the assertion is on the FILES, which is what tells a rule
            // reading the right folder from one reading none.
            Assert.That(MarkupFiles(), Has.Length.EqualTo(33), "the markup files the rule reads");
        });
    }

    /// <summary>
    /// The swatch row in the Open dialog draws the SAME six colours, in the same order, as
    /// <see cref="ConnectionColors.Palette"/>.
    ///
    /// <para>
    /// <b>This was found by the sweep and it is a real one.</b> <c>ConnectionColors</c> opens by
    /// saying it is "one palette, in one place, used by three things that must agree: the swatch row
    /// in the Open dialog where the colour is chosen, the stripe down the side of a tab where it is
    /// read, and the connection chip in the toolbar" - and the swatch row was a <b>fourth,
    /// hand-written copy</b>, six hexes typed into the markup. It agreed by luck. Nothing would have
    /// said so if it stopped agreeing, and the symptom would be a person picking violet and getting
    /// a cyan stripe on the tab their query is about to go to.
    /// </para>
    /// <para>
    /// Tokenising the swatches did not fix that - it moved the copy from six literals to six token
    /// names. This case is what closes it: the markup is read, in order, and each token is resolved
    /// and compared to the palette entry at the same index. It is red if either side changes alone,
    /// which is the only failure worth guarding.
    /// </para>
    /// </summary>
    [AvaloniaTest]
    public void TheSwatchRowDrawsTheConnectionPaletteInOrderTest()
    {
        var dialog = MarkupFiles().Single(f => f.EndsWith("OpenDatabaseDialog.axaml"));

        var swatches = Regex.Matches(File.ReadAllText(dialog),
                @"S\.Color\.[A-Za-z]+}""><Border[^>]*Background=""\{DynamicResource (Wit\.[A-Za-z.]+)\}""")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(swatches, Has.Length.EqualTo(ConnectionColors.Palette.Count),
                "the row offers a different number of colours than the palette holds");

            for (var index = 0; index < swatches.Length; index++)
                Assert.That((Resolve(swatches[index], ThemeVariant.Dark) as ISolidColorBrush)?.Color,
                    Is.EqualTo(ConnectionColors.Palette[index]),
                    $"swatch {index} draws {swatches[index]}");
        });
    }

    /// <summary>
    /// The palette handed to <c>FluentTheme</c> is the same palette as the tokens.
    ///
    /// <para>
    /// <b>Why it is written twice at all.</b> A <c>ColorPaletteResources</c> is consumed while the
    /// theme is being constructed, which is before <c>Application.Resources</c> exists - so a
    /// <c>{StaticResource}</c> there is a load-order gamble, not a reference. The hexes are written
    /// out, and that means the two sides CAN disagree, silently, in one theme.
    /// </para>
    /// <para>
    /// This case is what makes the duplication safe. Each pair below is the Fluent slot and the token
    /// it must equal; the slot is read back through the brush Fluent DERIVES from it, so what is
    /// asserted is the value that actually reaches a control rather than the text in the file.
    /// Duplication with an instrument on it is fine; duplication with a comment on it is what the
    /// connection swatches were.
    /// </para>
    /// </summary>
    [AvaloniaTest]
    public void ThePaletteHandedToFluentIsTheCanonPaletteTest()
    {
        // Fluent's derived brush -> the token it has to agree with. These four are the ones a person
        // sees: the window, the panel a control sits on, body text and the accent.
        (string Fluent, string Token)[] agreements =
        [
            ("SystemAccentColor", "Wit.Color.Accent"),
            ("SystemControlBackgroundChromeMediumLowBrush", "Wit.Color.Surface.Panel"),
            ("SystemControlForegroundBaseHighBrush", "Wit.Color.Ink"),
            ("SystemControlForegroundBaseMediumBrush", "Wit.Color.Ink.Secondary"),
        ];

        Assert.Multiple(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            foreach (var (fluent, token) in agreements)
            {
                var fromTheme = Resolve(fluent, variant);
                var expected = (Color)Resolve(token, variant)!;

                Assert.That(fromTheme, Is.Not.Null, $"{variant}: Fluent publishes no {fluent}");
                Assert.That(fromTheme is Color colour ? colour : ((ISolidColorBrush)fromTheme!).Color,
                    Is.EqualTo(expected), $"{variant}: {fluent} is not {token}");
            }
        });
    }

    #endregion

    #region The census - what stage V1 has to strike off

    /// <summary>
    /// The ledger of hard-coded values, asserted EXACTLY and in both directions.
    ///
    /// <para>
    /// This is phase 15's discipline, and the reason for it is that phase's own experience: a
    /// remainder asserted as "no more than" lets a fixed item stay on the list, and one asserted as
    /// "at least" lets a new one be added. Both happened. So the numbers below are equalities, and
    /// the test fails whether the sweep progresses without the ledger being updated OR a new literal
    /// is written into a view while the sweep is in progress.
    /// </para>
    /// <para>
    /// <b>A hex in a view is theme-blind by construction.</b> There is no correct value to write:
    /// whatever it is, it was chosen while looking at one of the two themes. That is why the target
    /// for the hex column is zero and the target for the other two is not - a size or a height
    /// written by hand is merely off the scale, which is a different and lesser thing.
    /// </para>
    /// <para>
    /// <b>These are not the numbers the phase plan opens with</b> (106 hex, 197 heights), and the
    /// difference is the regex rather than the code: the plan counted differently and never wrote
    /// its expression down. These three are reproducible - they are computed here, from the files in
    /// <c>Views</c>, by the patterns on the lines below. A census that cannot be re-run is a
    /// recollection.
    /// </para>
    /// </summary>
    [Test]
    public void TheCensusOfHardCodedValuesIsWhatItWasMeasuredToBeTest()
    {
        var views = MarkupFiles().Where(f => f.Contains("Views")).ToArray();

        var hex = views.Sum(f => Regex.Matches(File.ReadAllText(f), @"#[0-9A-Fa-f]{3,8}\b").Count);
        var fontSize = views.Sum(f => Regex.Matches(File.ReadAllText(f), @"FontSize=""[0-9]").Count);
        var height = views.Sum(f => Regex.Matches(File.ReadAllText(f), @"\b(?:Min|Max)?Height=""[0-9]").Count);

        Assert.Multiple(() =>
        {
            // ONE, and it is named: the pointer-over tint on the tab close button. A hover state is
            // a control style's business and belongs to stage V2, not to the palette - putting it on
            // a severity or an accent token would be a colour chosen to satisfy a rule.
            Assert.That(hex, Is.EqualTo(1), "hex colours written into a view");
            Assert.That(HexSites(), Is.EqualTo(new[] { "WorkspaceTabStrip.axaml:#22000000" }));

            Assert.That(fontSize, Is.EqualTo(283), "font sizes written by hand");
            Assert.That(height, Is.EqualTo(173), "heights written by hand");
        });
    }

    /// <summary>
    /// The remainder itself, by file and value rather than as a number.
    ///
    /// <para>
    /// A count of one is satisfied by any one hex anywhere. Naming it means a new literal appearing
    /// while an old one is removed cannot pass, which is the direction a bare count is blind in.
    /// </para>
    /// </summary>
    private static string[] HexSites() =>
        MarkupFiles()
            .Where(f => f.Contains("Views"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"#[0-9A-Fa-f]{3,8}\b")
                .Select(m => $"{Path.GetFileName(f)}:{m.Value}"))
            .OrderBy(s => s)
            .ToArray();

    #endregion

    #region Helpers

    private static object? Resolve(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var value) ? value : null;

    /// <summary>
    /// The keys each variant dictionary defines, read out of the merged dictionaries rather than
    /// asked for by name - which is what makes the symmetry question answerable at all.
    /// </summary>
    private static List<string> VariantKeys(ThemeVariant variant)
    {
        var keys = new List<string>();

        foreach (var provider in Application.Current!.Resources.MergedDictionaries)
        {
            var dictionary = provider switch
            {
                ResourceInclude include => include.Loaded as ResourceDictionary,
                ResourceDictionary direct => direct,
                _ => null,
            };

            if (dictionary?.ThemeDictionaries.TryGetValue(variant, out var themed) == true &&
                themed is ResourceDictionary themedDictionary)
                keys.AddRange(themedDictionary.Keys.Select(k => k.ToString()!));
        }

        return keys;
    }

    /// <summary>
    /// The markup as it is written, not as it is built. <c>bin</c> and <c>obj</c> hold copies, and a
    /// rule that reads them counts the same literal twice and reports progress that has not
    /// happened.
    /// </summary>
    private static string[] MarkupFiles() =>
        Directory.GetFiles(StudioSourceRoot(), "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f)
            .ToArray();

    private static string StudioSourceRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Tools")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "the repository root was not found from the test directory");

        return Path.Combine(directory!.FullName, "Tools", "OutWit.Database.Studio");
    }

    #endregion
}
