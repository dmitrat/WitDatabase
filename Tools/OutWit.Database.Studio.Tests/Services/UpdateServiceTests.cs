using NUnit.Framework;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// A feed that answers whatever the case wants and REMEMBERS whether it was asked.
/// </summary>
/// <remarks>
/// The counter is the point of it: the claim that matters in 9.8 is not "nothing was shown" but
/// "nothing was sent", and only the feed can say whether it was asked at all.
/// </remarks>
internal sealed class ScriptedReleaseFeed : IReleaseFeed
{
    private readonly ReleaseInfo? m_answer;

    public ScriptedReleaseFeed(ReleaseInfo? answer = null) => m_answer = answer;

    public int Asked { get; private set; }

    public Task<ReleaseInfo?> LatestAsync(CancellationToken ct = default)
    {
        Asked++;

        return Task.FromResult(m_answer);
    }
}

/// <summary>
/// The update check as the application runs it (9.8, WS-70).
/// </summary>
/// <remarks>
/// What counts as an update is <c>UpdateCheckTests</c>'s subject. These cases are about the two
/// promises the design makes to the person: <b>nothing goes to the network unless they turned it on</b>,
/// and nothing is downloaded or run in any case.
/// </remarks>
[TestFixture]
public class UpdateServiceTests
{
    #region Setup

    private StudioFixture m_fixture = null!;
    private ScriptedDialogService m_dialogs = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(withSchema: false);

        // The double stands in for a PERSON here, as the fixture's two others do: what is asserted is
        // whether a window was put in front of somebody.
        m_dialogs = new ScriptedDialogService();
        m_fixture.App.Dialogs = m_dialogs;
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    private static OutWit.Database.Studio.Models.Settings Settings(bool check, string? skipped = null) =>
        new() { CheckForUpdates = check, SkippedUpdate = skipped };

    #endregion

    #region Tests

    /// <summary>
    /// With the setting off, the feed is NOT ASKED - not asked and ignored, not asked at all.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the check is off by default. A tool that reaches out from a machine
    /// holding somebody's working database has to ask once and explicitly: the database may be on a
    /// closed network, and the request itself is a fact about that machine. Asserting that nothing was
    /// SHOWN would pass just as well with the request already sent.
    /// </remarks>
    [Test]
    public async Task NothingIsSentWhenTheCheckIsOffTest()
    {
        var feed = new ScriptedReleaseFeed(new ReleaseInfo("studio-v99.0.0", "", "https://x.invalid", false));

        m_fixture.App.MainWindowVm.ReleaseFeed = feed;

        await m_fixture.App.MainWindowVm.CheckForUpdatesAsync(Settings(check: false));

        Assert.Multiple(() =>
        {
            Assert.That(feed.Asked, Is.Zero, "the feed was asked with the setting off");
            Assert.That(m_dialogs.Shown, Has.None.EqualTo("ShowUpdateAsync"));
        });
    }

    /// <summary>
    /// CONTROL: with the setting on, it IS asked - otherwise the case above passes for a check that
    /// never works at all.
    /// </summary>
    [Test]
    public async Task TheFeedIsAskedWhenTheCheckIsOnTest()
    {
        var feed = new ScriptedReleaseFeed(new ReleaseInfo("studio-v99.0.0", "notes", "https://x.invalid", false));

        m_fixture.App.MainWindowVm.ReleaseFeed = feed;

        await m_fixture.App.MainWindowVm.CheckForUpdatesAsync(Settings(check: true));

        Assert.Multiple(() =>
        {
            Assert.That(feed.Asked, Is.EqualTo(1));
            Assert.That(m_fixture.App.MainWindowVm.LastUpdateVerdict, Is.EqualTo(UpdateVerdict.Available));
            Assert.That(m_dialogs.Shown, Does.Contain("ShowUpdateAsync"));
        });
    }

    [Test]
    public async Task NothingIsShownWhenThereIsNothingNewerTest()
    {
        var feed = new ScriptedReleaseFeed(new ReleaseInfo("studio-v0.0.1", "", "https://x.invalid", false));

        m_fixture.App.MainWindowVm.ReleaseFeed = feed;

        await m_fixture.App.MainWindowVm.CheckForUpdatesAsync(Settings(check: true));

        Assert.Multiple(() =>
        {
            Assert.That(feed.Asked, Is.EqualTo(1), "it did ask");
            Assert.That(m_fixture.App.MainWindowVm.LastUpdateVerdict, Is.EqualTo(UpdateVerdict.UpToDate));
            Assert.That(m_dialogs.Shown, Has.None.EqualTo("ShowUpdateAsync"), "and said nothing");
        });
    }

    /// <summary>
    /// A feed that cannot be read says nothing to anybody.
    /// </summary>
    /// <remarks>
    /// Nobody asked for this at the moment it runs, so an error banner would be a report about a
    /// background task the person did not want.
    /// </remarks>
    [Test]
    public async Task AFeedThatAnswersNothingIsSilentTest()
    {
        m_fixture.App.MainWindowVm.ReleaseFeed = new ScriptedReleaseFeed(answer: null);

        await m_fixture.App.MainWindowVm.CheckForUpdatesAsync(Settings(check: true));

        Assert.Multiple(() =>
        {
            Assert.That(m_fixture.App.MainWindowVm.LastUpdateVerdict, Is.EqualTo(UpdateVerdict.NothingPublished));
            Assert.That(m_dialogs.Shown, Has.None.EqualTo("ShowUpdateAsync"));
        });
    }

    /// <summary>
    /// A pre-release is not offered even with the check on - which in this repository is the usual
    /// state of the newest tag.
    /// </summary>
    [Test]
    public async Task ADevTagIsNotOfferedToAnybodyTest()
    {
        m_fixture.App.MainWindowVm.ReleaseFeed =
            new ScriptedReleaseFeed(new ReleaseInfo("studio-v99.0.0-dev.1", "", "https://x.invalid", true));

        await m_fixture.App.MainWindowVm.CheckForUpdatesAsync(Settings(check: true));

        Assert.Multiple(() =>
        {
            Assert.That(m_fixture.App.MainWindowVm.LastUpdateVerdict, Is.EqualTo(UpdateVerdict.OnlyAPrerelease));
            Assert.That(m_dialogs.Shown, Has.None.EqualTo("ShowUpdateAsync"));
        });
    }

    /// <summary>
    /// Skip remembers the version, and only that version.
    /// </summary>
    [Test]
    public async Task SkipRemembersTheVersionItSkippedTest()
    {
        var decision = UpdateCheck.Decide(
            new ReleaseInfo("studio-v99.1.0", "", "https://x.invalid", false),
            UpdateViewModel.CurrentVersion,
            skippedVersion: null);

        var window = new UpdateViewModel(m_fixture.App, decision);

        // Awaited rather than fired at a command and slept on: a test that waits 200 ms for a
        // background task is asserting where that task happened to be.
        await window.SkipAsync();

        var settings = await m_fixture.App.Settings.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(settings.SkippedUpdate, Is.EqualTo("99.1.0"));

            // And the next check is quiet about that one...
            Assert.That(UpdateCheck.Decide(new ReleaseInfo("studio-v99.1.0", "", "u", false),
                UpdateViewModel.CurrentVersion, settings.SkippedUpdate).IsOffered, Is.False);

            // ...but not about the one after it.
            Assert.That(UpdateCheck.Decide(new ReleaseInfo("studio-v99.2.0", "", "u", false),
                UpdateViewModel.CurrentVersion, settings.SkippedUpdate).IsOffered, Is.True);
        });
    }

    #endregion
}
