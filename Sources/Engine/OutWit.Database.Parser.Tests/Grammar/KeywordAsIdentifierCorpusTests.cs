using System.Text;
using OutWit.Database.Parser.Generated;

namespace OutWit.Database.Parser.Tests.Grammar;

/// <summary>
/// A corpus over the whole lexer vocabulary: which keywords can be used as a column name?
/// </summary>
/// <remarks>
/// Phase 5 found <c>CREATE TABLE T (Key TEXT)</c> unparseable, and found it by accident - the failure
/// had been recorded for months against <c>Parallel Mode=Buffered</c>, in a concurrency fixture, with
/// nobody suspecting the grammar. A 104-finding audit had missed the whole class. This fixture exists
/// so it cannot be missed again: it asks the question of **every** token the lexer defines, rather
/// than of the one name somebody happened to type.
///
/// The list of tokens is taken from the generated lexer's own vocabulary, not from a hand-written
/// list, so a keyword added to the grammar tomorrow is covered without anyone remembering to add it
/// here.
///
/// <b>Failures are pinned by name, never by count.</b> A bare count goes green again the moment one
/// keyword starts working and another stops, which is exactly the failure mode
/// <c>GrammarRoundTripTests</c> was rebuilt to avoid.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class KeywordAsIdentifierCorpusTests
{
    #region Pinned expectations

    /// <summary>
    /// Keywords that cannot be used as a column name, as measured on 2026-07-30 after <c>KEY</c> was
    /// added to <c>nonReservedKeyword</c>.
    /// </summary>
    /// <remarks>
    /// This is a record of the current state, NOT a statement that the state is right. Every name
    /// here is a keyword a consumer might reasonably use as a column name and cannot. Reducing the
    /// list is progress; the test fails either way, so neither direction can pass unnoticed.
    ///
    /// Populated by running <see cref="ReportEveryKeywordAsAColumnNameTest"/> and reading its output.
    /// </remarks>
    private static readonly string[] KnownUnusableAsColumnName = LoadPinnedList();

    #endregion

    #region Tests

    /// <summary>
    /// The corpus: every lexer keyword, tried as a column name, with the failures compared against
    /// the pinned list by name.
    /// </summary>
    [Test]
    public void EveryKeywordIsEitherUsableOrPinnedAsUnusableTest()
    {
        var rejected = new List<string>();
        var accepted = new List<string>();

        foreach (var keyword in KeywordTokens())
        {
            if (CanBeAColumnName(keyword))
                accepted.Add(keyword);
            else
                rejected.Add(keyword);
        }

        TestContext.Out.WriteLine(
            $"{accepted.Count} of {accepted.Count + rejected.Count} keywords are usable as a column name");

        var newlyRejected = rejected.Except(KnownUnusableAsColumnName).OrderBy(x => x).ToArray();
        var nowAccepted = KnownUnusableAsColumnName.Except(rejected).OrderBy(x => x).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(newlyRejected, Is.Empty,
                "these keywords stopped working as column names - a grammar regression");
            Assert.That(nowAccepted, Is.Empty,
                "these keywords now work as column names - remove them from the pinned list, which "
                + "is a record of the current state and not a target");
        });
    }

    /// <summary>
    /// <c>KEY</c> specifically, because it is the one this phase fixed and the one whose absence was
    /// misattributed to parallel mode for months.
    /// </summary>
    [Test]
    [TestCase("CREATE TABLE T (Key TEXT PRIMARY KEY, Value TEXT)")]
    [TestCase("CREATE TABLE T (Key TEXT)")]
    [TestCase("SELECT Key FROM T")]
    [TestCase("SELECT Key, Value FROM T WHERE Key = 'a'")]
    [TestCase("INSERT INTO T (Key, Value) VALUES ('a', 'b')")]
    [TestCase("UPDATE T SET Value = 'c' WHERE Key = 'a'")]
    [TestCase("CREATE TABLE Key (Id BIGINT PRIMARY KEY)")]
    public void KeyIsUsableAsAnIdentifierTest(string sql)
    {
        var result = WitSql.TryParse(sql);

        Assert.That(result.Errors, Is.Empty,
            $"<{sql}> did not parse: {string.Join("; ", result.Errors)}");
    }

    /// <summary>
    /// The control: <c>PRIMARY KEY</c> and <c>FOREIGN KEY</c> still mean what they meant. Making
    /// <c>KEY</c> available as an identifier must not have made it stop being a keyword.
    /// </summary>
    [Test]
    [TestCase("CREATE TABLE T (Id BIGINT PRIMARY KEY)")]
    [TestCase("CREATE TABLE T (Id BIGINT, PRIMARY KEY (Id))")]
    [TestCase("CREATE TABLE T (Id BIGINT, Other BIGINT, PRIMARY KEY (Id, Other))")]
    [TestCase("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT)")]
    [TestCase("CREATE TABLE C (Id BIGINT, FOREIGN KEY (Id) REFERENCES T (Id))")]
    [TestCase("CREATE TABLE C (Id BIGINT, CONSTRAINT fk FOREIGN KEY (Id) REFERENCES T (Id))")]
    public void KeyStillWorksAsAKeywordTest(string sql)
    {
        var result = WitSql.TryParse(sql);

        Assert.That(result.Errors, Is.Empty,
            $"<{sql}> stopped parsing: {string.Join("; ", result.Errors)}");
    }

    /// <summary>
    /// The hard case the two above do not cover on their own: a column actually named <c>Key</c>
    /// that is also the primary key, which puts the identifier and the keyword in one statement and
    /// two tokens apart. This is the marker's own shape.
    /// </summary>
    [Test]
    public void KeyAsBothColumnNameAndKeywordInOneStatementTest()
    {
        var result = WitSql.TryParse("CREATE TABLE T (Key TEXT, Value TEXT, PRIMARY KEY (Key))");

        Assert.That(result.Errors, Is.Empty,
            $"did not parse: {string.Join("; ", result.Errors)}");
    }

    /// <summary>
    /// Not an assertion - a report, so that the pinned list above can be regenerated by reading the
    /// output rather than by guessing. Always passes.
    /// </summary>
    [Test]
    public void ReportEveryKeywordAsAColumnNameTest()
    {
        var rejected = KeywordTokens().Where(k => !CanBeAColumnName(k)).OrderBy(x => x).ToArray();

        var text = new StringBuilder();
        text.AppendLine($"{rejected.Length} keywords cannot be used as a column name:");

        foreach (var chunk in rejected.Chunk(8))
            text.AppendLine("    " + string.Join(", ", chunk));

        TestContext.Out.WriteLine(text.ToString());
        Assert.Pass();
    }

    #endregion

    #region Tools

    /// <summary>
    /// Keyword-shaped tokens from the generated lexer's own vocabulary. Operators, literals and the
    /// hidden channel are excluded: they are not names anybody would try to use as a column.
    /// </summary>
    private static IEnumerable<string> KeywordTokens()
    {
        // The generated lexer's own rule names, so a keyword added to the grammar tomorrow is
        // covered without anyone remembering to update this fixture. Lexer fragments appear here
        // too, and in this grammar every fragment is a single letter plus DIGIT - hence the length
        // filter rather than a hand-maintained exclusion list.
        var skipped = new HashSet<string>(StringComparer.Ordinal)
        {
            "IDENTIFIER", "QUOTED_IDENTIFIER", "BRACKET_IDENTIFIER", "BACKTICK_IDENTIFIER",
            "STRING_LITERAL", "INTEGER_LITERAL", "DECIMAL_LITERAL", "FLOAT_LITERAL",
            "HEX_LITERAL", "BLOB_LITERAL", "PARAMETER", "NAMED_PARAMETER",
            "WS", "COMMENT", "LINE_COMMENT", "BLOCK_COMMENT", "EOF", "DIGIT"
        };

        foreach (var name in WitSqlLexer.ruleNames)
        {
            if (name.Length < 2 || skipped.Contains(name))
                continue;

            // Keywords are the all-letter tokens; anything else is an operator alias or a literal
            // category, and no consumer would try to use it as a column name.
            if (!name.All(c => char.IsAsciiLetterUpper(c) || c == '_'))
                continue;

            yield return name;
        }
    }

    /// <summary>
    /// Asks the real parser, rather than reasoning about the grammar: can this keyword be a column
    /// name in a <c>CREATE TABLE</c>?
    /// </summary>
    private static bool CanBeAColumnName(string keyword)
    {
        var pascal = keyword.Length == 1
            ? keyword
            : keyword[0] + keyword[1..].ToLowerInvariant();

        return WitSql.TryParse($"CREATE TABLE T ({pascal} TEXT)").Errors.Count == 0;
    }

    /// <summary>
    /// The pinned list lives in a file rather than inline, because it is long, it is data rather than
    /// logic, and a diff to it should read as "this many keywords changed status" rather than as a
    /// code change.
    /// </summary>
    private static string[] LoadPinnedList()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Grammar", "keywords-unusable-as-column-name.txt");

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"the pinned keyword list is missing at {path}; regenerate it from "
                + $"{nameof(ReportEveryKeywordAsAColumnNameTest)}", path);

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
    }

    #endregion
}
