using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Where the newest release comes from. An interface so that no test ever goes near a network, and so
/// that "did it even ask?" is a thing a test can assert.
/// </summary>
public interface IReleaseFeed
{
    Task<ReleaseInfo?> LatestAsync(CancellationToken ct = default);
}

/// <summary>
/// The newest <c>studio-v*</c> release, from GitHub (9.8, WS-70).
/// </summary>
/// <remarks>
/// One GET, no authentication, and nothing is downloaded: the answer is a version, some notes and a
/// URL for a button to open. The engine's own <c>v*</c> releases live in the same repository, so the
/// tag prefix is what tells them apart - offering a user Studio 12.2.0 because the ENGINE reached it
/// would be a memorable way to lose their trust.
/// </remarks>
public sealed class GitHubReleaseFeed : IReleaseFeed
{
    #region Constants

    private const string RELEASES = "https://api.github.com/repos/dmitrat/WitDatabase/releases?per_page=30";

    private const string STUDIO_TAG = "studio-v";

    #endregion

    #region Fields

    private readonly HttpClient m_client;
    private readonly ILogger m_logger;

    #endregion

    #region Constructors

    public GitHubReleaseFeed(ILogger logger, HttpClient? client = null)
    {
        m_logger = logger;
        m_client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // GitHub refuses a request with no user agent, which reads as a network error rather than as
        // the missing header it is.
        if (!m_client.DefaultRequestHeaders.UserAgent.Any())
            m_client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WitDatabaseStudio", "3.0"));

        m_client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    #endregion

    #region IReleaseFeed

    /// <summary>
    /// The newest release whose tag is Studio's, or null - including when the network is not there.
    /// </summary>
    /// <remarks>
    /// <b>A failed check is not an error a person needs.</b> Nobody asked for this at the moment it
    /// runs; it goes to the log and the interface says nothing. The one place a failure IS worth
    /// showing is the settings window, where somebody has just pressed "check now" and is waiting.
    /// </remarks>
    public async Task<ReleaseInfo?> LatestAsync(CancellationToken ct = default)
    {
        try
        {
            await using var stream = await m_client.GetStreamAsync(RELEASES, ct);

            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var tag = element.GetProperty("tag_name").GetString();

                if (tag == null || !tag.StartsWith(STUDIO_TAG, StringComparison.OrdinalIgnoreCase))
                    continue;

                return new ReleaseInfo(
                    tag,
                    element.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
                    element.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                    element.TryGetProperty("prerelease", out var pre) && pre.GetBoolean());
            }

            return null;
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "The update check could not read the release feed");

            return null;
        }
    }

    #endregion
}
