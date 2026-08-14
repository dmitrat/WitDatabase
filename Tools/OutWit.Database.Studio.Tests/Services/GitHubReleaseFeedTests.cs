using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Which release the feed picks out of the list GitHub returns.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the half of the update check that had no test.</b> <c>IReleaseFeed</c> is faked whole in
/// <c>UpdateServiceTests</c> - which is right for the question those cases ask, "was anything sent" -
/// so the JSON walk inside <c>GitHubReleaseFeed</c> was never exercised, and that is exactly where the
/// defect was.
/// </para>
/// <para>
/// <b>What it was:</b> the loop returned the FIRST release whose tag starts with <c>studio-v</c>, and
/// GitHub returns newest-first. Measured against the real repository on 2026-08-14:
/// </para>
/// <code>
/// studio-v3.0.0-dev.3   prerelease=True    &lt;- taken
/// studio-v3.0.0-dev.2   prerelease=True
/// studio-v3.0.0-dev.1   prerelease=True
/// studio-v2.0.0         prerelease=False   &lt;- the newest STABLE, invisible behind it
/// </code>
/// <para>
/// <c>UpdateCheck.Decide</c> then refuses a pre-release, so the verdict was <c>OnlyAPrerelease</c> and
/// nothing was ever offered - and it would have stayed that way for as long as a dev tag was newest,
/// which Studio's dev-tags-only policy guarantees. A person running Studio 1.x was never told 2.0.0
/// existed.
/// </para>
/// <para>
/// <b>A pre-release is still returned when there is nothing else</b>, rather than nothing at all. The
/// verdicts are what a background check says about itself in the log, and <c>OnlyAPrerelease</c> and
/// <c>NothingPublished</c> are different facts: the second also means "the feed could not be read".
/// </para>
/// </remarks>
[TestFixture]
public class GitHubReleaseFeedTests
{
    #region The rule

    /// <summary>
    /// The real repository's shape, as measured on 2026-08-14.
    /// </summary>
    [Test]
    public async Task TheNewestStableStudioReleaseIsChosenOverANewerPrereleaseAsync()
    {
        var feed = FeedReturning(
            ("studio-v3.0.0-dev.3", true),
            ("studio-v3.0.0-dev.2", true),
            ("studio-v3.0.0-dev.1", true),
            ("studio-v2.0.0", false),
            ("v1.0.1", false));

        var latest = await feed.LatestAsync();

        Assert.Multiple(() =>
        {
            Assert.That(latest, Is.Not.Null);
            Assert.That(latest!.Tag, Is.EqualTo("studio-v2.0.0"),
                "a dev tag in front of it must not hide a released Studio");
            Assert.That(latest.IsPrerelease, Is.False);
        });
    }

    /// <summary>
    /// And the consequence, stated where a person would meet it: somebody on Studio 1.9 is told about
    /// 2.0.0 instead of being told nothing at all.
    /// </summary>
    [Test]
    public async Task SomebodyOnAnOlderStudioIsActuallyOfferedTheReleaseAsync()
    {
        var feed = FeedReturning(
            ("studio-v3.0.0-dev.3", true),
            ("studio-v2.0.0", false));

        var decision = UpdateCheck.Decide(await feed.LatestAsync(), "1.9.0", skippedVersion: null);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.Available));
            Assert.That(decision.Version, Is.EqualTo("2.0.0"));
        });
    }

    #endregion

    #region Controls

    [Test]
    public async Task ControlTheNewestStudioReleaseIsChosenWhenItIsStableAsync()
    {
        var feed = FeedReturning(
            ("studio-v3.1.0", false),
            ("studio-v3.0.0", false));

        Assert.That((await feed.LatestAsync())!.Tag, Is.EqualTo("studio-v3.1.0"),
            "preferring a stable release must not mean preferring an OLD one");
    }

    /// <summary>
    /// Control: with nothing but pre-releases the feed still answers, so the verdict can say which of
    /// two different things happened.
    /// </summary>
    [Test]
    public async Task ControlOnlyPrereleasesStillAnswerSoTheVerdictCanSaySoAsync()
    {
        var feed = FeedReturning(
            ("studio-v3.0.0-dev.3", true),
            ("studio-v3.0.0-dev.1", true));

        var latest = await feed.LatestAsync();

        Assert.Multiple(() =>
        {
            Assert.That(latest, Is.Not.Null, "returning null here would report 'the feed could not be read'");
            Assert.That(latest!.Tag, Is.EqualTo("studio-v3.0.0-dev.3"));
            Assert.That(UpdateCheck.Decide(latest, "3.0.0-dev", null).Verdict,
                Is.EqualTo(UpdateVerdict.OnlyAPrerelease));
        });
    }

    /// <summary>
    /// Control: the ENGINE's own releases live in the same repository, and offering somebody
    /// "Studio 13.1.0" would be a memorable way to lose their trust.
    /// </summary>
    [Test]
    public async Task ControlAnEngineReleaseIsNeverOfferedAsStudioAsync()
    {
        var feed = FeedReturning(
            ("v13.1.0", false),
            ("v13.0.0", false),
            ("studio-v2.0.0", false));

        Assert.That((await feed.LatestAsync())!.Tag, Is.EqualTo("studio-v2.0.0"));
    }

    [Test]
    public async Task ControlNothingIsReturnedWhenNoStudioReleaseExistsAsync()
    {
        var feed = FeedReturning(("v13.1.0", false), ("v12.8.0", false));

        Assert.That(await feed.LatestAsync(), Is.Null);
    }

    /// <summary>
    /// Control: a feed that cannot be read answers null rather than throwing at the caller, which is
    /// what makes a failed background check silent.
    /// </summary>
    [Test]
    public async Task ControlAnUnreadableFeedAnswersNullAsync()
    {
        var feed = new GitHubReleaseFeed(NullLogger.Instance,
            new HttpClient(new CannedHandler("not json at all", HttpStatusCode.OK)));

        Assert.That(await feed.LatestAsync(), Is.Null);
    }

    #endregion

    #region Tools

    private static GitHubReleaseFeed FeedReturning(params (string Tag, bool Prerelease)[] releases)
    {
        var json = new StringBuilder("[");

        for (var i = 0; i < releases.Length; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append($$"""
                {"tag_name":"{{releases[i].Tag}}","body":"notes for {{releases[i].Tag}}",
                 "html_url":"https://example.invalid/{{releases[i].Tag}}",
                 "prerelease":{{(releases[i].Prerelease ? "true" : "false")}}}
                """);
        }

        json.Append(']');

        return new GitHubReleaseFeed(NullLogger.Instance,
            new HttpClient(new CannedHandler(json.ToString(), HttpStatusCode.OK)));
    }

    /// <summary>
    /// Answers one canned body, so the selection is measured without a network and without the real
    /// repository's list changing under the case.
    /// </summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string m_body;
        private readonly HttpStatusCode m_status;

        public CannedHandler(string body, HttpStatusCode status)
        {
            m_body = body;
            m_status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(m_status)
            {
                Content = new StringContent(m_body, Encoding.UTF8, "application/json")
            });
        }
    }

    #endregion
}
