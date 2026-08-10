using System.Text.RegularExpressions;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// Every setting the application offers is read by something that acts on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this exists for.</b> On 2026-08-10 five settings had zero readers, and four of them
/// were safety questions: <c>AskBeforeDroppingObject</c>, <c>AskBeforeUnfilteredWrite</c>,
/// <c>AskBeforeLongScript</c>, <c>AskBeforeClosingEditedTab</c> and <c>DefaultRowLimit</c>. The
/// settings dialog showed four ticked checkboxes promising to ask before destructive work, and
/// <c>DatabaseExplorerViewModel.DropObjectAsync</c> executed <c>DROP TABLE</c> straight from a
/// context-menu click. A user who sees a ticked box concludes they are protected, and they were not.
/// </para>
/// <para>
/// <b>Why a lint rather than a test per setting.</b> The failure is invisible from inside the
/// application: nothing throws, nothing looks wrong, and 818 tests stayed green for months. It is the
/// same class as the localisation lint - a defect of ABSENCE, which only a rule over the whole
/// surface can see.
/// </para>
/// <para>
/// <b>The distinction the rule turns on, and the first version got it wrong.</b> "Is this property
/// mentioned anywhere" is satisfied by the settings dialog itself: every dead property WAS referenced,
/// by the checkbox that sets it. The question is not whether something MENTIONS the setting but
/// whether something ACTS on it, so the dialog, its ViewModel and the model are excluded from the
/// search. That is the "destinations, not literals" lesson from stage 10, one class along.
/// </para>
/// <para>
/// <b>It asserts on the surface as well as on the findings</b>, because a rule that matches only
/// unread properties matches nothing once the work is done - and "nothing left to find" and "the rule
/// is reading the wrong folder" produce the same number.
/// </para>
/// </remarks>
[TestFixture]
public class SettingsAreActedOnTests
{
    #region Constants

    /// <summary>
    /// Where a setting is DECLARED, OFFERED and BOUND rather than obeyed. A reference from any of
    /// these says nothing about whether the application does what the setting promises.
    /// </summary>
    private static readonly string[] NOT_CONSUMERS =
    [
        Path.Combine("Models", "Settings.cs"),
        Path.Combine("Views", "Dialogs", "SettingsDialog.axaml"),
        Path.Combine("ViewModels", "SettingsViewModel.cs")
    ];

    /// <summary>
    /// Properties that are state the application stores rather than behaviour it must obey, so
    /// "nothing acts on it" is the wrong question. Each one is named individually and with a reason,
    /// because an exemption written as a pattern is a hole in the rule wearing a comment's clothes.
    /// </summary>
    private static readonly Dictionary<string, string> STORED_STATE = new()
    {
        ["WindowWidth"] = "restored geometry, written by the shell on close",
        ["WindowHeight"] = "restored geometry, written by the shell on close",
        ["WindowState"] = "restored geometry, written by the shell on close",
        ["SkippedUpdate"] = "a remembered answer, not a preference the application obeys"
    };

    #endregion

    #region Tests

    [Test]
    public void EverySettingIsReadBySomethingThatActsOnItTest()
    {
        var properties = SettingsProperties();

        // THE SURFACE. Without this the rule passes on an empty read, which is what a wrong folder
        // and a finished sweep both look like.
        Assert.That(properties, Has.Count.GreaterThanOrEqualTo(25),
            "the rule found almost no settings to examine, which means it is reading the wrong file "
            + "rather than that everything is in order");

        var sources = ConsumerSources().ToList();

        Assert.That(sources, Has.Count.GreaterThanOrEqualTo(40),
            "the rule found almost no sources to search, so a green run would mean nothing");

        var unread = new List<string>();

        foreach (var property in properties)
        {
            if (STORED_STATE.ContainsKey(property))
                continue;

            // NO look-behind for a dot, and that is the whole correctness of this rule. A CONSUMER
            // reads a setting as `settings.Name` or `Settings.Current.Name` or
            // `nameof(Settings.Name)` - it is preceded by a dot almost every time. The first version
            // of this lint copied the localisation rule's `(?<![\w.])`, which exists there to stop
            // `SizeToContent="Height"` reading as a Content, and here it excluded every real reader:
            // it reported 21 dead settings when the true number is smaller, including ones verified
            // working against the live GitHub API. The instrument was wrong before its subject.
            var pattern = new Regex(@"(?<![\w])" + Regex.Escape(property) + @"(?![\w])");

            if (!sources.Any(source => pattern.IsMatch(File.ReadAllText(source))))
                unread.Add(property);
        }

        // NAMED AS A REMAINDER and asserted EXACTLY, which is this project's shape for work that is
        // known and not yet done. A new dead setting fails here; a listed one that gets wired must be
        // struck off or the test fails the other way, so the list cannot rot into a permanent excuse.
        //
        // Seven of class B were wired on 2026-08-10 and struck off here - which the rule made
        // compulsory rather than optional: it is asserted EQUIVALENT, so a remainder that gets fixed
        // and is left on the list fails just as loudly as a new dead setting. It did, on the run that
        // wired them.
        //
        // What is left is two different kinds of work and neither is wiring:
        //
        //   KeywordCase - the formatter regenerates SQL from the PARSE TREE, so the casing the user
        //       typed is gone by the time there is anything to case. "Upper" and "Lower" are a
        //       post-pass over keyword tokens; "AsTyped" means nothing there, and pretending it does
        //       would be a setting that lies in a third way.
        //
        //   DefaultRowLimit - WS-23 asks for a selector ON THE TAB that APPENDS `LIMIT` rather than
        //       truncating a result already fetched, with "no limit" an explicit choice. The setting
        //       is only the value a new tab starts at. That is a feature, not a wire.
        //
        //   C, the three Restore* - the feature DOES NOT EXIST. Studio has no session restore across
        //       restarts at all (only "reopen closed tab" inside one run), so these offer something
        //       that was never built. Build it or withdraw them: Dmitry's decision, outstanding.
        var knownUnwired = new[]
        {
            "KeywordCase", "DefaultRowLimit",
            "RestoreConnections", "RestoreTabs", "RestoreUnsavedTabs"
        };

        Assert.That(unread, Is.EquivalentTo(knownUnwired),
            "a setting is offered to the user and nothing acts on it - a promise the application does "
            + "not keep - or a listed remainder has been wired and not struck off: "
            + string.Join(", ", unread));
    }

    /// <summary>
    /// Every question in the catalogue is actually ASKED somewhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This rule exists because the first one went blind the moment the fix started.</b> Wiring the
    /// settings meant <c>ConfirmationService</c> gained a mapping from every <see cref="ConfirmationKind"/>
    /// to its setting - so all four "ask before" properties instantly had a reader, and rule one turned
    /// green for two questions that nothing asks. A setting being read is not the claim; the claim is
    /// that the question reaches a person.
    /// </para>
    /// <para>
    /// So this looks for the construction of a <c>DestructiveAction</c> naming each kind. That is the
    /// call site - the place a person is actually asked - and it cannot be satisfied by the plumbing.
    /// </para>
    /// </remarks>
    [Test]
    public void EveryConfirmationKindIsAskedSomewhereTest()
    {
        var kinds = Enum.GetNames<ConfirmationKind>();

        Assert.That(kinds, Has.Length.GreaterThanOrEqualTo(3),
            "the catalogue is smaller than expected, so a green run would mean little");

        var callSites = ConsumerSources()
            .Where(path => path.EndsWith(".cs"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .ToList();

        var neverAsked = kinds
            .Where(kind => !callSites.Any(text =>
                text.Contains("DestructiveAction(") && text.Contains("ConfirmationKind." + kind)))
            .ToList();

        // NAMED AS A REMAINDER rather than silently allowed, and asserted EXACTLY: a new question that
        // is never asked fails here, and a question that gets its call site must be struck off this
        // list or the test fails the other way. Stage 9's shape.
        var known = new[] { "UnfilteredWrite", "LongScript" };

        Assert.That(neverAsked, Is.EquivalentTo(known),
            "the catalogue promises a question that nothing asks, or a listed remainder has been "
            + "built and not struck off: " + string.Join(", ", neverAsked));
    }

    /// <summary>
    /// The other direction: the exemption list may not rot. A name on it that no longer exists is an
    /// exemption covering nothing, and the next person reads it as a rule that was considered.
    /// </summary>
    [Test]
    public void EveryStoredStateExemptionStillNamesARealSettingTest()
    {
        var properties = SettingsProperties();

        var stale = STORED_STATE.Keys.Where(name => !properties.Contains(name)).ToList();

        Assert.That(stale, Is.Empty,
            "these exemptions name settings that no longer exist: " + string.Join(", ", stale));
    }

    #endregion

    #region Tools

    private static List<string> SettingsProperties()
    {
        var text = File.ReadAllText(Path.Combine(StudioFolder(), "Models", "Settings.cs"));

        return Regex.Matches(text, @"public\s+[\w<>?\[\],\s]+?\s+(\w+)\s*\{\s*get;\s*set;")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    private static IEnumerable<string> ConsumerSources()
    {
        var studio = StudioFolder();

        return Directory
            .EnumerateFiles(studio, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs") || path.EndsWith(".axaml"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !NOT_CONSUMERS.Any(excluded => path.EndsWith(excluded)));
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
