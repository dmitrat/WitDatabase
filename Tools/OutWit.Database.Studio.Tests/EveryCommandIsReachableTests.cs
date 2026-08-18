using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// A command nobody can press is a feature nobody has.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because three findings turned out to be features nobody could find</b> - the trigger
/// dialog, the find band and the Database tab all exist, each reachable from exactly one place, and
/// none of them the place a person looks. Those ARE reachable, so this rule does not see them; what
/// it sees is the harder version of the same thing.
/// </para>
/// <para>
/// <b>Run red the first time, it named five commands nobody could press</b>, and not one of them had
/// been reported by anybody. <c>FirstPageCommand</c> and <c>RestoreColumnCommand</c> were gaps and
/// are drawn now - the second one mattered, because marking a column for deletion in the designer hid
/// its button and left no way back short of discarding every edit in the set. <c>LoadDataCommand</c>,
/// <c>RenameObjectCommand</c> and <c>ShowSectionCommand</c> were wrappers around methods the
/// application already calls directly, and are deleted. It also named two exemptions this fixture was
/// written with, both of which were wrong.
/// </para>
/// <para>
/// <b>What counts as reachable.</b> Named anywhere in the markup - a binding, a parent binding, a
/// keyboard gesture - or in a view's code-behind. This is deliberately loose: the question is whether
/// a person can reach the thing, and the narrow version of it would spend its life reporting the
/// binding syntaxes it had not been taught.
/// </para>
/// <para>
/// <b>The exemptions are the point of the fixture.</b> A command that is invoked from another
/// ViewModel, or that exists for a test, is not a defect - but it has to be written down here with a
/// reason, so that the next unreachable command is a line in a diff rather than a discovery two
/// months later.
/// </para>
/// </remarks>
[TestFixture]
public class EveryCommandIsReachableTests
{
    #region Exemptions

    /// <summary>
    /// Commands that are pressed by something other than a view, with the reason for each.
    /// </summary>
    private static readonly Dictionary<string, string> INVOKED_ELSEWHERE = new()
    {
        ["ExpandNodeCommand"] =
            "the tree's lazy loader. Phase 5 of the fix plan wires it to the expander; until then the "
            + "columns of a table cannot be reached from the tree at all (finding 15)",

    };

    #endregion

    #region Rule

    [Test]
    public void EveryCommandOnAViewModelCanBeReachedTest()
    {
        var root = FindStudioProject();

        Assert.That(root, Is.Not.Null,
            "the Studio project was not found from " + AppContext.BaseDirectory);

        var declarations = new Dictionary<string, string>();

        foreach (var file in Sources(Path.Combine(root!, "ViewModels"), "*.cs"))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file),
                         @"public\s+ICommand\??\s+(\w+Command)\b"))
            {
                declarations[match.Groups[1].Value] = Path.GetRelativePath(root!, file);
            }
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Sources(root!, "*.axaml").Concat(Sources(Path.Combine(root!, "Views"), "*.cs")))
        {
            var text = File.ReadAllText(file);

            foreach (var name in declarations.Keys)
            {
                if (text.Contains(name, StringComparison.Ordinal))
                    reachable.Add(name);
            }
        }

        var unreachable = declarations
            .Where(pair => !reachable.Contains(pair.Key))
            .Where(pair => !INVOKED_ELSEWHERE.ContainsKey(pair.Key))
            .Select(pair => $"{pair.Key} ({pair.Value})")
            .OrderBy(line => line)
            .ToList();

        var staleExemptions = INVOKED_ELSEWHERE.Keys
            .Where(name => !declarations.ContainsKey(name) || reachable.Contains(name))
            .ToList();

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that found no commands would report nothing unreachable either. This is
            // a FLOOR and not a count - it exists to catch a walk that looked in the wrong place, and
            // it tripped once for the right reason, when three commands were deleted and 152 became
            // 149. A number that has to be edited every time a command is added would be noise.
            Assert.That(declarations, Has.Count.GreaterThan(100),
                "CONTROL: too few commands found - the walk is looking in the wrong place");

            Assert.That(unreachable, Is.Empty,
                "these commands exist and nothing can press them:" + Environment.NewLine
                + string.Join(Environment.NewLine, unreachable));

            // An exemption that has become true is a line that now hides the next defect.
            Assert.That(staleExemptions, Is.Empty,
                "these exemptions are no longer needed and must be deleted:" + Environment.NewLine
                + string.Join(Environment.NewLine, staleExemptions));
        });
    }

    #endregion

    #region Tools

    private static IEnumerable<string> Sources(string root, string pattern)
    {
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

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

    #endregion
}
