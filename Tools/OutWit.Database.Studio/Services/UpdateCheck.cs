using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Services;

/// <summary>A release as the feed describes it.</summary>
/// <param name="Tag">The tag, as published: <c>studio-v3.1.0</c>.</param>
/// <param name="Notes">What changed, in the publisher's words - never translated (WS-64).</param>
/// <param name="Url">The page to open. Studio downloads nothing itself.</param>
/// <param name="IsPrerelease">Whether the publisher marked it a pre-release.</param>
public sealed record ReleaseInfo(string Tag, string Notes, string Url, bool IsPrerelease);

/// <summary>Why the check answered the way it did.</summary>
public enum UpdateVerdict
{
    /// <summary>Nothing came back - no release, or the feed could not be read.</summary>
    NothingPublished,

    /// <summary>The newest release is a pre-release, and those are never offered.</summary>
    OnlyAPrerelease,

    /// <summary>The tag is not a version this can compare.</summary>
    Unreadable,

    /// <summary>What is published is what is running, or older.</summary>
    UpToDate,

    /// <summary>Newer, but this exact version was skipped by the user.</summary>
    Skipped,

    /// <summary>Newer, and worth saying so.</summary>
    Available
}

/// <summary>The answer, with the words the window needs.</summary>
public sealed record UpdateDecision(UpdateVerdict Verdict, string? Version = null, ReleaseInfo? Release = null)
{
    public bool IsOffered => Verdict == UpdateVerdict.Available;
}

/// <summary>
/// Whether a newer Studio is worth telling somebody about (9.8, WS-70).
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole decision is a function of three strings</b> - what the feed published, what is
/// running, and what the user has skipped - so every rule below is measurable without a network. The
/// service around it does one HTTP GET and nothing else: <b>Studio never downloads or runs anything</b>,
/// which is the design's decision and the reason there is no auto-update.
/// </para>
/// <para>
/// <b>A pre-release is never offered, and that is not a detail here.</b> Studio's own policy is
/// dev tags only until the interface reaches its final shape, so the newest tag in this repository is
/// usually something like <c>studio-v3.0.0-dev.1</c>. A check that offered it would push every user of
/// a released Studio onto a development build.
/// </para>
/// </remarks>
public static class UpdateCheck
{
    #region Constants

    /// <summary>
    /// <c>studio-v3.1.0</c>, <c>v3.1.0</c>, <c>3.1.0</c> - and the suffix of <c>3.0.0-dev</c>, which is
    /// what the running assembly reports, is cut before comparing.
    /// </summary>
    private static readonly Regex VERSION =
        new(@"(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.Compiled);

    #endregion

    #region Functions

    /// <summary>
    /// The verdict, given what the feed said, what is running, and what was skipped.
    /// </summary>
    public static UpdateDecision Decide(ReleaseInfo? latest, string? currentVersion, string? skippedVersion)
    {
        if (latest == null)
            return new UpdateDecision(UpdateVerdict.NothingPublished);

        if (latest.IsPrerelease)
            return new UpdateDecision(UpdateVerdict.OnlyAPrerelease);

        var published = Parse(latest.Tag);
        var running = Parse(currentVersion);

        if (published == null || running == null)
            return new UpdateDecision(UpdateVerdict.Unreadable);

        if (published <= running)
            return new UpdateDecision(UpdateVerdict.UpToDate, Text(published));

        // Skipping is per VERSION, not for good: a user who skipped 3.1.0 still hears about 3.2.0.
        // Skipping for good would be the same as turning the check off, and there is a setting for that.
        var skipped = Parse(skippedVersion);

        if (skipped != null && published <= skipped)
            return new UpdateDecision(UpdateVerdict.Skipped, Text(published));

        return new UpdateDecision(UpdateVerdict.Available, Text(published), latest);
    }

    /// <summary>
    /// The version inside a tag or an assembly's informational version.
    /// </summary>
    /// <remarks>
    /// Everything that is not the numbers is ignored on purpose: the running build calls itself
    /// <c>3.0.0-dev</c> and the tags are <c>studio-v*</c>. Comparing <c>3.0.0-dev</c> to <c>3.0.0</c> as
    /// STRINGS would offer a user the version they already have.
    /// </remarks>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = VERSION.Match(text);

        if (!match.Success)
            return null;

        return new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0,
            match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0);
    }

    private static string Text(Version version) =>
        version.Revision > 0
            ? version.ToString()
            : $"{version.Major}.{version.Minor}.{version.Build}";

    #endregion
}
