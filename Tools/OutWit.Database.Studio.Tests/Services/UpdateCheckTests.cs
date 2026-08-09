using NUnit.Framework;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Whether a newer Studio is worth telling somebody about (9.8, WS-70).
/// </summary>
/// <remarks>
/// The whole decision is a function of three strings, so every rule here is measured without a
/// network. What the service around it does - one HTTP GET, and only when the setting is on - has its
/// own cases in <c>UpdateServiceTests</c>.
/// </remarks>
[TestFixture]
public class UpdateCheckTests
{
    #region Constants

    private const string CURRENT = "3.0.0-dev";

    private static ReleaseInfo Release(string tag, bool prerelease = false) =>
        new(tag, "notes", "https://example.invalid/release", prerelease);

    #endregion

    #region Tests

    [Test]
    public void ANewerReleaseIsOfferedTest()
    {
        var decision = UpdateCheck.Decide(Release("studio-v3.1.0"), CURRENT, skippedVersion: null);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.Available));
            Assert.That(decision.IsOffered, Is.True);
            Assert.That(decision.Version, Is.EqualTo("3.1.0"));
            Assert.That(decision.Release!.Url, Is.EqualTo("https://example.invalid/release"),
                "the page is what the button opens - Studio downloads nothing itself");
        });
    }

    /// <summary>
    /// The running build calls itself <c>3.0.0-dev</c> and the tag is <c>studio-v3.0.0</c>. They are
    /// the same version.
    /// </summary>
    /// <remarks>
    /// Compared as STRINGS these differ, and the check would offer a person the Studio they are
    /// already running - every time it ran.
    /// </remarks>
    [Test]
    public void TheSameVersionWithASuffixIsNotAnUpdateTest()
    {
        var decision = UpdateCheck.Decide(Release("studio-v3.0.0"), CURRENT, skippedVersion: null);

        Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.UpToDate));
    }

    [Test]
    public void AnOlderReleaseIsNotAnUpdateTest()
    {
        var decision = UpdateCheck.Decide(Release("studio-v2.0.0"), CURRENT, skippedVersion: null);

        Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.UpToDate));
    }

    /// <summary>
    /// A PRE-RELEASE is never offered, and in this repository that is the common case.
    /// </summary>
    /// <remarks>
    /// Studio's own policy is dev tags only until the interface reaches its final shape, so the newest
    /// tag here is usually <c>studio-v3.0.0-dev.N</c>. A check that offered it would push every user of
    /// a released Studio onto a development build.
    /// </remarks>
    [Test]
    public void APrereleaseIsNeverOfferedTest()
    {
        var decision = UpdateCheck.Decide(Release("studio-v9.9.9-dev.1", prerelease: true), CURRENT, null);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.OnlyAPrerelease));
            Assert.That(decision.IsOffered, Is.False, "even though 9.9.9 is far newer than 3.0.0");
        });
    }

    [Test]
    public void NothingPublishedIsNotAnErrorTest()
    {
        var decision = UpdateCheck.Decide(null, CURRENT, null);

        Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.NothingPublished));
    }

    [Test]
    public void ATagThatIsNotAVersionIsRefusedRatherThanGuessedTest()
    {
        var decision = UpdateCheck.Decide(Release("nightly"), CURRENT, null);

        Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.Unreadable));
    }

    #endregion

    #region Skipping

    [Test]
    public void ASkippedVersionIsNotOfferedAgainTest()
    {
        var decision = UpdateCheck.Decide(Release("studio-v3.1.0"), CURRENT, skippedVersion: "3.1.0");

        Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.Skipped));
    }

    /// <summary>
    /// But skipping one version is not turning the check off: a LATER one is still offered.
    /// </summary>
    /// <remarks>
    /// This is the case that makes "skip" a different thing from the setting. Without it the button
    /// would quietly mean "never tell me again", which the checkbox in the settings already says
    /// properly and reversibly.
    /// </remarks>
    [Test]
    public void SkippingOneVersionDoesNotSkipTheNextTest()
    {
        var decision = UpdateCheck.Decide(Release("studio-v3.2.0"), CURRENT, skippedVersion: "3.1.0");

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(UpdateVerdict.Available));
            Assert.That(decision.Version, Is.EqualTo("3.2.0"));
        });
    }

    #endregion

    #region Reading a version out of a tag

    [TestCase("studio-v3.1.0", "3.1.0")]
    [TestCase("v3.1.0", "3.1.0")]
    [TestCase("3.1.0", "3.1.0")]
    [TestCase("3.0.0-dev", "3.0.0")]
    [TestCase("studio-v12.2.0", "12.2.0")]
    [TestCase("3.1", "3.1.0")]
    public void AVersionIsReadOutOfWhateverTheTagIsTest(string text, string expected)
    {
        Assert.That(UpdateCheck.Parse(text)?.ToString(3), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("nightly")]
    [TestCase("studio-latest")]
    public void SomethingWithNoVersionInItIsNullTest(string? text)
    {
        Assert.That(UpdateCheck.Parse(text), Is.Null);
    }

    #endregion
}
