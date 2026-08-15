using System.Text.RegularExpressions;

namespace OutWit.Database.Core.Tests;

/// <summary>
/// The READMEs that ship inside the packages install the version that is being shipped.
/// </summary>
/// <remarks>
/// <para>
/// A README goes into the NuGet package, so it reaches people who never open the repository or the
/// site - and the first thing they copy out of it is the <c>PackageReference</c>. Eight of them across
/// five packages pinned <c>12.8.0</c> while every package was on 13.1.1: the number was written once,
/// per file, and nothing could notice it going stale.
/// </para>
/// <para>
/// <b>The rule is over the whole surface</b> - every README under <c>Sources</c>, every pinned
/// version in it - rather than over the one file somebody noticed, and it counts what it examined so
/// that "nothing left to find" cannot read like "the folder moved".
/// </para>
/// </remarks>
[TestFixture]
public class ShippedReadmesTests
{
    #region Constants

    /// <summary>An install snippet: <c>&lt;PackageReference Include="X" Version="Y" /&gt;</c>.</summary>
    private static readonly Regex PACKAGE_REFERENCE =
        new(@"<PackageReference\s+Include=""(?<package>OutWit\.[\w.]+)""\s+Version=""(?<version>[^""]+)""",
            RegexOptions.Compiled);

    /// <summary>The version a project declares: <c>&lt;Version&gt;13.1.1&lt;/Version&gt;</c>.</summary>
    private static readonly Regex PROJECT_VERSION =
        new(@"<Version>(?<version>[^<]+)</Version>", RegexOptions.Compiled);

    #endregion

    #region Tests

    [Test]
    public void EveryInstallSnippetNamesTheVersionThatShipsTest()
    {
        var versions = ProjectVersions();
        var stale = new List<string>();
        var examined = 0;

        foreach (var readme in Readmes())
        {
            var text = File.ReadAllText(readme);

            foreach (Match match in PACKAGE_REFERENCE.Matches(text))
            {
                examined++;

                var package = match.Groups["package"].Value;
                var pinned = match.Groups["version"].Value;

                if (!versions.TryGetValue(package, out var shipping))
                {
                    stale.Add($"{Path.GetFileName(Path.GetDirectoryName(readme))}/README.md installs "
                              + $"{package}, which is not a project in this repository");
                    continue;
                }

                if (pinned != shipping)
                {
                    stale.Add($"{Path.GetFileName(Path.GetDirectoryName(readme))}/README.md installs "
                              + $"{package} {pinned}; the package is {shipping}");
                }
            }
        }

        Assert.Multiple(() =>
        {
            // THE SURFACE. Eight snippets across five READMEs today, and a rule that read none of
            // them would pass exactly as loudly as one that read all of them.
            Assert.That(examined, Is.EqualTo(8),
                "the shipped READMEs carry a different number of install snippets than this rule was "
                + "measured against - check the new one, then change this number");

            Assert.That(versions, Has.Count.GreaterThan(5),
                "CONTROL: almost no project version was read, so nothing here is being compared");

            Assert.That(stale, Is.Empty,
                "these install a version that is not the one being shipped:"
                + Environment.NewLine + string.Join(Environment.NewLine, stale));
        });
    }

    #endregion

    #region Tools

    /// <summary>Package name -> the version its project declares.</summary>
    private static Dictionary<string, string> ProjectVersions()
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var project in Directory.EnumerateFiles(SourcesFolder(), "OutWit.*.csproj",
                     SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(project);

            if (name.EndsWith(".Tests", StringComparison.Ordinal))
                continue;

            var match = PROJECT_VERSION.Match(File.ReadAllText(project));

            if (match.Success)
                versions[name] = match.Groups["version"].Value;
        }

        return versions;
    }

    private static IEnumerable<string> Readmes() =>
        Directory.EnumerateFiles(SourcesFolder(), "README.md", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal));

    private static string SourcesFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Sources");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the Sources folder was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
