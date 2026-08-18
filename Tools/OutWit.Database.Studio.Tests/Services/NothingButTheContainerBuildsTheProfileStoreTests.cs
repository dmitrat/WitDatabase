namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The saved connections live in the user's own profile, so exactly one thing is allowed to build a
/// store over that path: the container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured 2026-08-16 on a developer machine: 2644 saved connections</b>, 2640 of them pointing
/// into <c>%TEMP%</c> and reporting <i>file not found</i>. The Connections window was unusable and
/// every one of those paths was a private one that would appear in any screenshot of it.
/// </para>
/// <para>
/// The cause was a default. <c>ApplicationViewModel</c> took every service as an optional parameter,
/// and where the harmless ones fell back to a null object - no history, no dialogs - the profile
/// store fell back to a REAL one over
/// <c>%AppData%\WitDatabase.Studio\connections.json</c>. `StudioFixture` passed an isolated store;
/// five other test sites built the ViewModel directly, isolated their settings file, and did not know
/// there was a second thing to isolate.
/// </para>
/// <para>
/// The parameter is required now, so the compiler is the first guard and this is the second: nothing
/// in the application constructs the store itself. The container has it registered, which is the one
/// route that is allowed to use the default path, and a test that wants one passes a path.
/// </para>
/// </remarks>
[TestFixture]
public class NothingButTheContainerBuildsTheProfileStoreTests
{
    [Test]
    public void TheApplicationNeverConstructsAProfileStoreItselfTest()
    {
        var root = FindStudioProject();

        Assert.That(root, Is.Not.Null,
            "the Studio project was not found from " + AppContext.BaseDirectory);

        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(root!, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            scanned++;

            var text = File.ReadAllText(file);

            // The declaration itself is where the type is written, and the registration in Program.cs
            // names the type without constructing it - which is the route that is allowed.
            if (Path.GetFileName(file).Equals("ConnectionProfileStore.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            if (text.Contains("new ConnectionProfileStore(", StringComparison.Ordinal))
                offenders.Add(Path.GetRelativePath(root!, file));
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that found nothing would report no offenders either.
            Assert.That(scanned, Is.GreaterThan(80),
                "CONTROL: too few files scanned - the walk is looking in the wrong place");

            Assert.That(offenders, Is.Empty,
                "these build a connection store over the user's real profile instead of taking the "
                + "one the container holds:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        });
    }

    /// <summary>
    /// Walks up from the test binaries to the application project - not to <c>Tools</c>, because the
    /// test project is allowed to build a store over its own path and does.
    /// </summary>
    private static string? FindStudioProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
