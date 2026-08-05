using System.Text;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// The build must not depend on the platform's default codepage.
///
/// This exists because of a real failure, 2026-08-05. `WorkspaceTabViewModel` and
/// `QueryTabViewModelTests` both carried a bullet as a single Windows-1252 byte (0x95) in files with
/// no BOM. Roslyn falls back to the system codepage on Windows, so both read `•` and the comparison
/// between them passed; on Linux there is no such fallback, both became U+FFFD, and the comparison
/// passed there too - for the wrong reason. Converting ONE of the two files to UTF-8 made them agree
/// on Windows and disagree on Linux, and CI went red on a string that no commit had touched.
///
/// A lone high byte is invisible in a diff and produces a failure that reads like a logic change. So
/// it is checked, over the whole of Studio, rather than remembered.
/// </summary>
[TestFixture]
public class SourceEncodingTests
{
    #region Constants

    private static readonly string[] EXTENSIONS = [".cs", ".axaml", ".csproj", ".md", ".xshd"];

    #endregion

    #region Tests

    [Test]
    public void EverySourceFileInStudioIsValidUtf8Test()
    {
        var root = FindStudioRoot();

        // Not Ignore: if the tree cannot be found the check has not run, and a check that passes when
        // it cannot run is exactly the thing this fixture exists to catch.
        Assert.That(root, Is.Not.Null,
            "the Studio source tree was not found from " + AppContext.BaseDirectory);

        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(root!, "*", SearchOption.AllDirectories))
        {
            if (!EXTENSIONS.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue;

            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            scanned++;

            var bytes = File.ReadAllBytes(file);

            try
            {
                strict.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                offenders.Add($"{Path.GetRelativePath(root!, file)} - byte 0x{ex.BytesUnknown?[0]:X2} "
                    + $"at offset {ex.Index}");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a scan that found nothing to look at would report no offenders either.
            Assert.That(scanned, Is.GreaterThan(50),
                "CONTROL: too few files scanned - the walk is looking in the wrong place");

            Assert.That(offenders, Is.Empty,
                "these files are not UTF-8, so the compiler reads them differently on Windows and on "
                + "Linux:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Walks up from the test binaries to the folder holding both Studio projects.
    /// </summary>
    private static string? FindStudioRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(candidate))
                return Path.Combine(directory.FullName, "Tools");

            directory = directory.Parent;
        }

        return null;
    }

    #endregion
}
