using System.Text.RegularExpressions;

namespace OutWit.Database.Tests.Schema;

/// <summary>
/// Nothing outside the resolvers may parse schema that came out of the catalog.
/// </summary>
/// <remarks>
/// <para>
/// The catalog stores schema as trees from 9.0.0, and each stored tree has exactly one resolver -
/// <c>DefinitionView.ResolveQuery</c>, <c>DefinitionIndex.ResolveWhere</c>,
/// <c>DefinitionColumn.ResolveComputed</c> and the rest. A caller that parses the text form instead
/// is not merely slower: it reads the <b>rendering</b> of the schema rather than the schema, so it
/// silently inherits every clause the renderer cannot express.
/// </para>
/// <para>
/// This is checked mechanically because a review missed it once already. Converting this area, a
/// grep for <c>WitSql.ParseExpression</c> found 21 call sites and they were all converted - and
/// <b>ten more were hiding behind a caching wrapper</b>, <c>GetOrParseExpression</c>, which the grep
/// never saw. Every defect phase 7 found had this shape: a correct check that one route went around.
/// A test is the only thing that keeps finding them after the person stops looking.
/// </para>
/// <para>
/// Source is scanned rather than IL, because the point is to fail the moment the line is written.
/// </para>
/// </remarks>
[TestFixture]
[Category("Schema")]
public class StoredSchemaIsNeverReparsedTests
{
    #region Allowed

    /// <summary>
    /// The only places a string may become a tree, and why.
    /// </summary>
    private static readonly (string File, string Reason)[] ALLOWED =
    [
        ("WitSqlEngine.cs", "parses SQL handed in by the caller - not stored schema"),
        ("WitSqlEngine.Query.cs", "parses SQL handed in by the caller - not stored schema"),
        ("DefinitionView.cs", "ResolveQuery: the legacy fallback for a view written before 9.0.0"),
        ("DefinitionIndex.cs", "ResolveWhere / ResolveColumnExpression: legacy fallbacks"),
        ("DefinitionColumn.cs", "ResolveComputed / ResolveCheck / ResolveDefault: legacy fallbacks"),
        ("DefinitionNamedConstraint.cs", "ResolveCheck: the legacy fallback"),
        ("DefinitionTable.cs", "ResolveChecks: the legacy fallback"),
        ("DefinitionTrigger.cs", "ResolveWhen / ResolveStatements: legacy fallbacks"),
    ];

    #endregion

    #region Tests

    [Test]
    public void OnlyTheResolversTurnStoredSchemaBackIntoATreeTest()
    {
        var root = EngineSourceRoot();

        Assert.That(root, Is.Not.Null,
            "the engine sources must be findable from the test binary for this check to mean anything");

        var pattern = new Regex(@"WitSql\.Parse(Expression|Statement)?\s*\(", RegexOptions.Compiled);

        var offenders = Directory
            .EnumerateFiles(root!, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => ALLOWED.All(allowed => Path.GetFileName(file) != allowed.File))
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (File: Path.GetFileName(file), Number: index + 1, Text: line.Trim()))
                .Where(entry => pattern.IsMatch(entry.Text)))
            .Select(entry => $"{entry.File}:{entry.Number}  {entry.Text}")
            .OrderBy(entry => entry)
            .ToArray();

        Assert.That(offenders, Is.Empty,
            $"{offenders.Length} places parse SQL outside the resolvers. If this is stored schema, " +
            $"call the definition's Resolve… method instead; if it is genuinely caller-supplied SQL, " +
            $"add the file to {nameof(ALLOWED)} with the reason:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// The scan's own control. If the search finds nothing anywhere, the test above passes for the
    /// wrong reason - a wrong root path would make it permanently, silently green.
    /// </summary>
    [Test]
    public void TheScanCanActuallyFindAParseTest()
    {
        var root = EngineSourceRoot();

        Assert.That(root, Is.Not.Null);

        var found = Directory
            .EnumerateFiles(root!, "*.cs", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) == "WitSqlEngine.cs")
            .SelectMany(File.ReadAllLines)
            .Count(line => line.Contains("WitSql.Parse("));

        Assert.That(found, Is.GreaterThan(0),
            "the scan found no parse in WitSqlEngine.cs, where one certainly is - so it is looking " +
            "in the wrong place and the check above proves nothing");
    }

    #endregion

    #region Helpers

    private static string? EngineSourceRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Sources", "Engine", "OutWit.Database");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    #endregion
}
