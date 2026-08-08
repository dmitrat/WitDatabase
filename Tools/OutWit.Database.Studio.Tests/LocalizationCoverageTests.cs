using System.Text.RegularExpressions;
using OutWit.Database.Studio.Services.Localization;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// Nothing carries its own text (WS-63) - not the markup, not what a screen reader announces, and not
/// a ViewModel.
///
/// <para>
/// A lint, in the same shape as <c>AutomationSurfaceTests</c> and for the same reason: the defect it
/// guards against is not one a running test can see. A hardcoded caption looks perfectly correct in
/// English and is simply never translated, and nobody notices until someone switches the language and
/// finds half a window in the wrong one.
/// </para>
/// <para>
/// <b>There are three rules because there are three ways to write text into this application, and the
/// first version only had the first.</b> Stage 9's lint said so in its own summary and listed the
/// fifteen views it had not reached; the sweep is done and the list is gone. What it could not reach
/// then it reaches now:
/// </para>
/// <list type="number">
/// <item>markup - a caption, a header, a tooltip, a window title, a <c>StringFormat</c>;</item>
/// <item><c>AutomationProperties.Name</c> - which nobody sees and a blind person hears, which is
/// exactly why it was English in every view after the rest had been swept;</item>
/// <item>a string built in a ViewModel or in a view's code-behind - the status bar's "Ready", the text
/// of a notification, the title of a file picker.</item>
/// </list>
/// <para>
/// <b>What it still does not reach, said out loud.</b> It reads text, not a program: a caption assembled
/// from a helper the rules do not name, or handed in from a model, goes past it. The third rule is
/// written around DESTINATIONS - the properties a view binds and the services that show a message -
/// rather than around what a literal looks like, because the shape of a string cannot tell prose from a
/// log template and the destination can.
/// </para>
/// </summary>
[TestFixture]
public class LocalizationCoverageTests
{
    #region Constants

    /// <summary>
    /// The attributes a person reads. The look-behind is not decoration: without it
    /// <c>SizeToContent="Height"</c> is read as a <c>Content</c> of "Height", and a rule with a false
    /// positive in it gets an exemption written for the false positive.
    /// </summary>
    private static readonly Regex USER_FACING =
        new(@"(?<![\w.])(Text|Content|Header|Watermark|ToolTip\.Tip|PlaceholderText|Title)=""([^""{][^""]*)""",
            RegexOptions.Compiled);

    /// <summary>What a screen reader says. Rule 2.</summary>
    private static readonly Regex ANNOUNCED =
        new(@"(AutomationProperties\.Name)=""([^""{][^""]*)""", RegexOptions.Compiled);

    /// <summary>
    /// The same two attribute sets with the <c>{</c> allowed, i.e. INCLUDING the ones that already come
    /// from the catalogue.
    ///
    /// <para>
    /// This is what the controls count, and the distinction is the whole point of having them. The
    /// rules above match a literal only, so once a sweep is finished they match nothing - and "nothing
    /// left to find" and "the rule is looking in the wrong folder" produce exactly the same number.
    /// Counting the whole surface tells them apart.
    /// </para>
    /// </summary>
    private static readonly Regex USER_FACING_SURFACE =
        new(@"(?<![\w.])(Text|Content|Header|Watermark|ToolTip\.Tip|PlaceholderText|Title)=""[^""]*""",
            RegexOptions.Compiled);

    private static readonly Regex ANNOUNCED_SURFACE =
        new(@"AutomationProperties\.Name=""[^""]*""", RegexOptions.Compiled);

    /// <summary>
    /// A caption built inside a binding. It is markup, and no attribute rule can see it: the text is
    /// inside single quotes inside another attribute's value.
    ///
    /// <para>
    /// <c>TargetNullValue</c> and <c>FallbackValue</c> are here for the same reason and were added
    /// after the fact: the inspector's row count read <c>TargetNullValue='not counted'</c>, which
    /// survived the whole sweep because nothing was looking at that attribute.
    /// </para>
    /// </summary>
    private static readonly Regex STRING_FORMAT =
        new(@"(StringFormat|TargetNullValue|FallbackValue)='([^']*)'", RegexOptions.Compiled);

    /// <summary>
    /// Properties a view binds and a person therefore reads. Rule 3 is about where a string GOES, not
    /// what it looks like - which is the only way to tell a sentence from a log template, since both
    /// are prose with values in them.
    ///
    /// <para>
    /// <c>=>?</c>, not <c>=</c>: an EXPRESSION-BODIED property was invisible to this rule, because a
    /// destination is written <c>Foo => "text"</c> as often as <c>Foo = "text"</c> and the first is not
    /// an assignment. Found on 2026-08-07 by reading <c>TableRebuildViewModel</c> while arming the
    /// rebuild button - three of the rebuild dialog's own sentences were in English on a Russian
    /// interface, and this rule had been passing over them since the sweep.
    /// </para>
    /// <para>
    /// <b>And the literal does not have to come straight after the <c>=</c>.</b> The rule used to
    /// demand it, so <c>StatusText = count == 1 ? "one" : "many"</c> - a destination assigned a
    /// CONDITIONAL - was invisible: the literals are one token further along. Seventeen sites had that
    /// shape, among them the status bar's own "Query executed successfully", the explorer's
    /// "No matches" and the table editor's read-only reason. Found on 2026-08-08 by running the
    /// application in Russian and reading the status bar, which is the wrong way round for the third
    /// time; the same hole as the expression body above, one token along. Anything up to a statement
    /// boundary is now allowed between the two, and <c>(?!=)</c> is what keeps
    /// <c>if (State == "closed")</c> from becoming an assignment.
    /// </para>
    /// <para>
    /// <b>And the name is an ENDING, not a whole word.</b> The list was written as exact identifiers,
    /// and this application names its destinations compositionally - <c>FilterSummary</c>,
    /// <c>CaretSummary</c>, <c>PagingNote</c>, <c>ConnectionSummary</c> - so the look-behind that keeps
    /// the rule out of the middle of an identifier was also keeping it out of the front of one. The
    /// anchor that makes the prefix safe is the <c>=</c>: <c>MySubtitleValue</c> does not match,
    /// because the listed word has to be the last thing before the assignment.
    /// </para>
    /// </summary>
    private static readonly Regex VIEWMODEL_ASSIGNMENT =
        new(@"(?<!\w)(\w*(?:ErrorMessage|StatusText|StatusMessage|Summary|Heading|Title|Hint|Note|Caption"
            + @"|Description|Watermark|PlaceholderText|Warning|HeaderText|Subtitle|KeyNote|PlannerNote"
            + @"|UnindexedKeyWarning|ConflictSummary|PlanMessage|HistoryMessage|BackupWarning"
            + @"|NotArmedReason|LanguageNote|SaveText|ViewDescription|State|MarkerReason|ReadOnlyReason"
            + @"|RowCountText))"
            + @"(\.Text)?\s*=>?(?!=)[^;""]{0,160}?[$]?""((?:[^""\\]|\\.)*)""",
            RegexOptions.Compiled);

    /// <summary>
    /// The services that put a string in front of somebody: a notification, a confirmation, a file
    /// picker's title and the name of one of its filters.
    /// </summary>
    private static readonly Regex VIEWMODEL_CALL =
        new(@"(Notifications\.\w+|Confirmations?\.\w+|Dialogs\.\w+|new FileFilter"
            + @"|Set\w*Status|Describe\w*)\([^;]*?"
            + @"[$]?""((?:[^""\\]|\\.)*)""",
            RegexOptions.Compiled);

    /// <summary>
    /// Text that is NOT Studio's own and must not be translated (WS-64): the terms of the engine, the
    /// language and the formats. A caption that is exactly one of these is left in the markup, because
    /// putting it in the catalogue would invite a translator to translate it - and a translated
    /// keyword, operator or format name is one nobody can find in the documentation, in an issue or in
    /// the sources.
    /// </summary>
    private static readonly HashSet<string> NOT_STUDIO_TEXT = new(StringComparer.Ordinal)
    {
        "B-Tree", "LSM", "SQL", "CSV", "JSON", "MVCC", "WitDatabase", "WitDatabase Studio",
        "AES-256-GCM", "ChaCha20", "Apache 2.0", "SQL INSERT", "Markdown", "QueryResult",

        // SQL, whole or in part: a keyword, a clause, or an example of the language shown in a
        // placeholder. Translating one would be teaching the user a language the engine does not speak.
        "DDL", "EXPLAIN", "NOT NULL", "PRIMARY KEY", "AUTOINCREMENT", "UNIQUE", "INCLUDE",
        "FOR EACH ROW", "SELECT * FROM TableName WHERE ...", "NEW.Total > 100",
        "INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);", "Status <> 'archived'",
        "IX_Orders_CreatedAt", "Line,Reason,Text",

        // A key gesture is not translated either: Ctrl is Ctrl, and the one place a gesture DOES
        // change is macOS, where the whole map is rewritten rather than each label.
        "Ctrl+K", "Ctrl+O", "Ctrl+N", "Ctrl+T", "Ctrl+S", "Ctrl+F", "F5", "Esc"
    };

    /// <summary>
    /// The same destinations with any argument at all, literal or not - what rule 3's control counts,
    /// for the reason given on <see cref="USER_FACING_SURFACE"/>.
    /// </summary>
    private static readonly Regex VIEWMODEL_SURFACE =
        new(@"(?<!\w)\w*(ErrorMessage|StatusText|StatusMessage|Summary|Heading|Title|Hint|Note|Caption"
            + @"|Description|Watermark|PlaceholderText|Warning|HeaderText|Subtitle|KeyNote|PlannerNote"
            + @"|UnindexedKeyWarning|ConflictSummary|PlanMessage|HistoryMessage|BackupWarning"
            + @"|NotArmedReason|LanguageNote|SaveText|ViewDescription|State|MarkerReason|ReadOnlyReason"
            + @"|RowCountText)(\.Text)?\s*=>?(?!=)"
            + @"|(Notifications\.\w+|Confirmations?\.\w+|Dialogs\.\w+|new FileFilter"
            + @"|Set\w*Status|Describe\w*)\(",
            RegexOptions.Compiled);

    /// <summary>
    /// A lookup in the catalogue, which is the ANSWER to this rule rather than an instance of what it
    /// looks for. It is blanked out before the rules run, so that
    /// <c>Notifications.Information(Localization["X"], path)</c> is not reported for the key it asks
    /// for - the mistake this rule made on its own first run.
    /// </summary>
    private static readonly Regex CATALOGUE_LOOKUP =
        new(@"Localization(\[|\.\w+\()\s*""[^""]*""", RegexOptions.Compiled);

    /// <summary>
    /// The two places a literal in a ViewModel is not a caption: a log template, which is read by
    /// whoever opens the file and must stay in one language, and a name used for structured logging.
    /// </summary>
    private static readonly string[] NOT_A_CAPTION = ["Logger.", "Log.", "nameof("];

    /// <summary>
    /// Every source of text this rule reads. Rule 3 covers a view's code-behind as well as the
    /// ViewModels, because <c>UnsavedChangesDialog.AskAsync</c> built "1 unsaved change" there and no
    /// rule about ViewModels would ever have found it.
    ///
    /// <para>
    /// <c>Services</c> was added on 2026-08-07 and immediately named a class the rule had never been
    /// able to see: see <see cref="SENTENCES_BUILT_IN_A_SERVICE"/>.
    /// </para>
    /// </summary>
    private static readonly string[] CODE_FOLDERS = ["ViewModels", "Views", "Services"];

    /// <summary>
    /// The remainder, NAMED rather than finished - the same shape stage 9 used for the fourteen
    /// unswept views, so the rule bites everywhere else in the meantime.
    ///
    /// <para>
    /// These three services do not merely hold text, they COMPOSE it: <c>SchemaChangeSet.Description</c>
    /// writes the designer's per-row sentence and the DDL panel's comment, <c>TableRebuild.Title</c>
    /// writes the rebuild dialog's steps, and <c>QueryPlan.Warning</c> writes the plan panel's notes.
    /// Putting them in the catalogue is not a sweep: a service that renders is the defect
    /// (stage 10's "a model must not render itself"), so the plan has to carry the CHANGE and its
    /// parameters and let the ViewModel say it. That is a piece of work with its own tests - several
    /// cases in <c>TableRebuildTests</c> and <c>SchemaDesignerTests</c> assert on the English wording -
    /// and it is deliberately not done here.
    /// </para>
    /// <para>
    /// Measured 2026-08-07 while arming the rebuild button: the dialog came up with a Russian heading,
    /// a Russian row count and a Russian backup warning above four English step titles.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> SENTENCES_BUILT_IN_A_SERVICE = new(StringComparer.Ordinal)
    {
        "SchemaChangeSet.cs", "TableRebuild.cs", "QueryPlan.cs"
    };

    /// <summary>
    /// The log file's own level tags - <c>Warning = "WRN"</c> is the four-letter column in a text log,
    /// not a caption, and it happens to be spelled like one of rule 3's destinations. An exemption
    /// written for a false positive, and named as one.
    /// </summary>
    private static readonly HashSet<string> NOT_A_SURFACE_AT_ALL = new(StringComparer.Ordinal)
    {
        "FileLoggerProvider.cs"
    };

    #endregion

    #region Tests

    [Test]
    public void NoViewCarriesItsOwnTextTest()
    {
        var hardcoded = new List<string>();
        var read = 0;

        foreach (var file in Views())
        {
            var markup = File.ReadAllText(file);

            read += USER_FACING_SURFACE.Matches(markup).Count + STRING_FORMAT.Matches(markup).Count;

            foreach (var match in Matches(markup, USER_FACING, STRING_FORMAT))
            {
                if (!IsStudioText(match.Value))
                    continue;

                hardcoded.Add($"{Path.GetFileName(file)}:{Line(markup, match.Index)} " +
                    $"{match.Attribute}=\"{match.Value}\"");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a rule that read nothing passes perfectly. The number is the whole markup
            // surface, exemptions included, so it only moves when a view is added or deleted.
            Assert.That(read, Is.GreaterThan(50),
                "CONTROL: this rule read almost nothing, so it is guarding nothing");

            Assert.That(hardcoded, Is.Empty,
                "these are Studio's own text and are not in the catalogue, so they will never be "
                + "translated:" + Environment.NewLine + string.Join(Environment.NewLine, hardcoded));
        });
    }

    /// <summary>
    /// What a screen reader announces is text too (S10).
    ///
    /// <para>
    /// It is a rule of its own rather than another attribute in the first one because it fails
    /// differently: a hardcoded <c>Text</c> is visible to anyone who switches the language, and a
    /// hardcoded <c>AutomationProperties.Name</c> is visible to nobody at all. Stage 9 swept the
    /// captions of six windows and left all 85 of their announcements in English, and every test passed.
    /// </para>
    /// </summary>
    [Test]
    public void NoViewAnnouncesItsOwnTextTest()
    {
        var hardcoded = new List<string>();
        var read = 0;

        foreach (var file in Views())
        {
            var markup = File.ReadAllText(file);

            read += ANNOUNCED_SURFACE.Matches(markup).Count;

            foreach (var match in Matches(markup, ANNOUNCED))
            {
                if (!IsStudioText(match.Value))
                    continue;

                hardcoded.Add($"{Path.GetFileName(file)}:{Line(markup, match.Index)} " +
                    $"AutomationProperties.Name=\"{match.Value}\"");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: the announcements are the point, and a run that found none of them - a renamed
            // attached property, a moved folder - would pass in silence.
            Assert.That(read, Is.GreaterThan(20),
                "CONTROL: no announcement was read at all, so this rule is guarding nothing");

            Assert.That(hardcoded, Is.Empty,
                "a screen reader would announce these in English whatever the interface language is:"
                + Environment.NewLine + string.Join(Environment.NewLine, hardcoded));
        });
    }

    /// <summary>
    /// A string a person reads is not built in code (WS-63).
    ///
    /// <para>
    /// The status bar's "Ready", a notification's summary, the title of a file picker: markup gets its
    /// language for free from <c>DynamicResource</c>, and a ViewModel has to ask for it. This is the
    /// class the markup lint said, in its own summary, that it could not reach.
    /// </para>
    /// <para>
    /// <b>It reads destinations, not literals.</b> "Explorer refreshed {Connection}: {Tables} tables"
    /// and "Renamed {old} to {new}" are the same shape; the first is a log template that must stay in
    /// one language and the second is a sentence in the status bar. Nothing about the strings tells
    /// them apart - where they GO does.
    /// </para>
    /// </summary>
    [Test]
    public void NoViewModelBuildsATextTest()
    {
        var built = new List<string>();
        var remainder = new HashSet<string>(StringComparer.Ordinal);
        var read = 0;

        foreach (var file in Sources())
        {
            var name = Path.GetFileName(file);

            if (NOT_A_SURFACE_AT_ALL.Contains(name))
                continue;

            var code = File.ReadAllText(file);

            foreach (var line in Lines(code))
            {
                if (line.Text.TrimStart().StartsWith("//") || NOT_A_CAPTION.Any(line.Text.Contains))
                    continue;

                read += VIEWMODEL_SURFACE.Matches(line.Text).Count;

                // TWO lines, not one. The explorer's status summary is an assignment whose string
                // begins on the next line, and the one-line version of this rule walked straight past
                // it - found by switching the running application, which is the wrong way round.
                // A match is attributed to the line its DESTINATION is on, so nothing is counted twice.
                var window = CATALOGUE_LOOKUP.Replace(line.Text + "\n" + line.Next, "Localization.Key");

                foreach (var match in Matches(window, VIEWMODEL_ASSIGNMENT, VIEWMODEL_CALL))
                {
                    if (match.Index >= line.Text.Length || !IsStudioText(match.Value))
                        continue;

                    if (SENTENCES_BUILT_IN_A_SERVICE.Contains(name))
                    {
                        remainder.Add(name);
                        continue;
                    }

                    built.Add($"{name}:{line.Number} {match.Attribute} = \"{Shorten(match.Value)}\"");
                }
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: this rule finds nothing if the destinations it names have been renamed, and
            // "nothing hardcoded" and "nothing read" look identical from the outside.
            Assert.That(read, Is.GreaterThan(40),
                "CONTROL: no string reaching a person was read at all, so this rule is guarding nothing");

            Assert.That(built, Is.Empty,
                "these reach a person and are not in the catalogue, so they stay in one language "
                + "whatever the interface is set to:"
                + Environment.NewLine + string.Join(Environment.NewLine, built));

            // The named remainder, asserted EXACTLY: a fourth service composing sentences fails this
            // rule, and so does a listed one that has been fixed and left on the list. A remainder
            // nobody re-measures becomes a permanent excuse.
            Assert.That(remainder, Is.EquivalentTo(SENTENCES_BUILT_IN_A_SERVICE),
                "the remainder is not what it says it is - a service composing sentences has been "
                + "added, or one on the list no longer does and should come off it");
        });
    }

    /// <summary>
    /// The three rules, measured against text rather than against the repository.
    ///
    /// <para>
    /// A lint is only as good as what it can SEE, and every exemption it carries is a chance to have
    /// widened it too far. These are the cases that were actually got wrong while the sweep was being
    /// done - <c>SizeToContent="Height"</c> read as a caption, <c>&amp;lt;</c> read as the word "lt",
    /// a date pattern read as prose - plus one line of real text per rule that must still be caught.
    /// </para>
    /// </summary>
    [Test]
    public void EveryRuleCatchesWhatItIsForAndNothingElseTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Caught(USER_FACING, @"<TextBlock Text=""Not Connected""/>"), Is.True,
                "a hardcoded caption must be caught");
            Assert.That(Caught(USER_FACING, @"<Window SizeToContent=""Height""/>"), Is.False,
                "SizeToContent is not a Content");
            Assert.That(Caught(USER_FACING, @"<Button Content=""&lt;""/>"), Is.False,
                "an entity is punctuation, not the word lt");
            Assert.That(Caught(USER_FACING, @"<TextBlock Text=""{DynamicResource S.Query.NoRows}""/>"), Is.False,
                "a caption that comes from the catalogue is the point of the rule");
            Assert.That(Caught(USER_FACING, @"<TextBlock Text=""SQL""/>"), Is.False,
                "an engine term is deliberately left alone (WS-64)");

            Assert.That(Caught(STRING_FORMAT, "StringFormat='page {0}'"), Is.True,
                "a caption inside a binding must be caught");
            Assert.That(Caught(STRING_FORMAT, "StringFormat='{}{0:F1} %'"), Is.False,
                "a number's format specifier is not prose");
            Assert.That(Caught(STRING_FORMAT, "StringFormat='{}{0:yyyy-MM-dd HH:mm}'"), Is.False,
                "a date pattern is not prose");
            Assert.That(Caught(STRING_FORMAT, "Text=\"{Binding RowCount, TargetNullValue='not counted'}\""), Is.True,
                "a caption inside a binding's null value must be caught");

            Assert.That(Caught(ANNOUNCED, @"AutomationProperties.Name=""Clear filter"""), Is.True,
                "a hardcoded announcement must be caught");
            Assert.That(Caught(ANNOUNCED, @"AutomationProperties.Name=""{DynamicResource S.X}"""), Is.False,
                "an announcement from the catalogue is the point of the rule");

            Assert.That(Caught(VIEWMODEL_ASSIGNMENT, @"StatusText = ""Ready"";"), Is.True,
                "the status bar is read by a person");
            Assert.That(Caught(VIEWMODEL_ASSIGNMENT, @"ErrorMessage = $""Export failed: {ex.Message}"";"), Is.True,
                "an interpolated sentence is still a sentence");
            Assert.That(Caught(VIEWMODEL_ASSIGNMENT, @"StatusText = Localization[""Common.Ready""];"), Is.False,
                "a catalogue key is not a caption");
            Assert.That(Caught(VIEWMODEL_ASSIGNMENT, @"var sql = ""SELECT * FROM [Orders]"";"), Is.False,
                "a statement is not a caption");

            // The conditional, both directions. The first is the class seventeen sites were hiding in;
            // the second is what widening the rule could have cost, and it is why the assignment has to
            // refuse a comparison.
            Assert.That(Caught(VIEWMODEL_ASSIGNMENT,
                "StatusText = count == 1" + Environment.NewLine + @"    ? ""one row"""), Is.True,
                "a destination assigned a conditional is still a destination");
            Assert.That(Caught(VIEWMODEL_ASSIGNMENT, @"if (State == ""closed"") return;"), Is.False,
                "a comparison against a destination is not an assignment to it");
            Assert.That(Caught(VIEWMODEL_ASSIGNMENT,
                "Title = null;" + Environment.NewLine + @"    var sql = ""SELECT 1"";"), Is.False,
                "the search stops at the end of the statement, so the next line's code is not its text");

            // A composite destination, both directions. The listed word has to END the identifier,
            // which is what stops the prefix from turning the rule into "any name containing Note".
            Assert.That(Caught(VIEWMODEL_ASSIGNMENT, @"FilterSummary = ""No matches"";"), Is.True,
                "a destination named after what it summarises is still a destination");
            Assert.That(Caught(VIEWMODEL_ASSIGNMENT, @"var noteworthyThing = ""x y"";"), Is.False,
                "a name that merely contains a listed word is not a destination");

            Assert.That(Caught(VIEWMODEL_CALL, @"Notifications.Information(""Database dumped"", path);"), Is.True,
                "a notification is read by a person");
            Assert.That(Caught(VIEWMODEL_CALL, @"new FileFilter(""All Files"", [""*.*""]);"), Is.True,
                "the name of a filter is read by a person");
            Assert.That(Caught(VIEWMODEL_CALL, @"Notifications.Information(Localization[""X""], path);"), Is.False,
                "a notification from the catalogue is the point of the rule");
        });
    }

    /// <summary>
    /// And the catalogue is what the views read from, so it has to be reachable from the test
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

    private readonly record struct Hit(string Attribute, string Value, int Index);

    private readonly record struct SourceLine(string Text, string Next, int Number);

    private static IEnumerable<Hit> Matches(string text, params Regex[] rules)
    {
        foreach (var rule in rules)
        {
            foreach (Match match in rule.Matches(text))
            {
                // The last group is the value; the first is what it was written into. A rule with an
                // optional group in the middle (".Text") would otherwise shift the value's number.
                var value = match.Groups[match.Groups.Count - 1].Value;

                yield return new Hit(match.Groups[1].Value, Decode(value).Trim(), match.Index);
            }
        }
    }

    private static IEnumerable<SourceLine> Lines(string code)
    {
        var lines = code.Split('\n');

        for (var i = 0; i < lines.Length; i++)
            yield return new SourceLine(lines[i], i + 1 < lines.Length ? lines[i + 1] : string.Empty, i + 1);
    }

    /// <summary>
    /// Whether a value is Studio's own text, i.e. something a translator should be given.
    ///
    /// <para>
    /// The letters have to be OUTSIDE the placeholders: <c>{0:F1} %</c> and
    /// <c>{0:yyyy-MM-dd HH:mm}</c> are full of letters and none of them is a word. A value with no
    /// letters left is punctuation, a glyph or a number, and has no translation.
    /// </para>
    /// </summary>
    private static bool IsStudioText(string value)
    {
        if (value.Length == 0 || NOT_STUDIO_TEXT.Contains(value))
            return false;

        // A file mask, an extension or a catalogue key: one word with a dot in it and no space. A
        // caption is either a single word with no dot, or has spaces in it.
        if (Regex.IsMatch(value, @"^[\w*]*\.[\w*.]+$"))
            return false;

        var outsidePlaceholders = Regex.Replace(value, @"\{[^}]*\}", string.Empty);

        return outsidePlaceholders.Any(char.IsLetter);
    }

    /// <summary>
    /// Markup is XML, so <c>&amp;lt;</c> is a caption of one character. Read literally it is the word
    /// "lt", which is how a "&lt;" on a paging button came to look like untranslated text.
    /// </summary>
    private static string Decode(string value)
    {
        return value
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'")
            .Replace("&amp;", "&");
    }

    private static bool Caught(Regex rule, string line)
    {
        var text = CATALOGUE_LOOKUP.Replace(line, "Localization.Key");

        return Matches(text, rule).Any(hit => IsStudioText(hit.Value));
    }

    private static int Line(string text, int index) => text[..index].Count(c => c == '\n') + 1;

    private static string Shorten(string value) =>
        value.Length <= 70 ? value : value[..70] + "...";

    private static IEnumerable<string> Views()
    {
        var views = Path.Combine(StudioFolder(), "Views");

        return Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories);
    }

    private static IEnumerable<string> Sources()
    {
        var studio = StudioFolder();

        return CODE_FOLDERS
            .Select(folder => Path.Combine(studio, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories));
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

        throw new DirectoryNotFoundException(
            "the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
