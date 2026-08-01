using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Serializers;

namespace OutWit.Database.Parser.Tests.Serialization;

/// <summary>
/// The reserved-word answer comes from the grammar, and keeps coming from it.
/// </summary>
/// <remarks>
/// <para>
/// Phase 8's acceptance criterion names this directly: the set must be derived rather than
/// hand-maintained, so it cannot drift from the grammar again. It had drifted badly - 68 words held
/// against 170 reserved, and one word reserved that the grammar had deliberately released.
/// </para>
/// <para>
/// The test that matters is not "does the set contain X". It is the round trip: <b>write an
/// identifier out and read it back</b>. That is the only property anyone depends on, and it is
/// checked below over every keyword the lexer knows, not over a sample.
/// </para>
/// </remarks>
[TestFixture]
[Category("Grammar")]
public class ReservedWordsTests
{
    #region The property that matters

    /// <summary>
    /// Every keyword, used as a column name, survives being serialized and re-parsed.
    /// </summary>
    /// <remarks>
    /// This is the end the defect was found at: <c>Using</c>, <c>With</c>, <c>Row</c>,
    /// <c>Column</c>, <c>Cross</c>, <c>Interval</c> and <c>Partition</c> were written unquoted and
    /// then would not re-parse.
    /// </remarks>
    [Test]
    public void EveryKeywordSurvivesBeingWrittenAsAnIdentifierTest()
    {
        var broken = new List<string>();

        foreach (var word in Keywords())
        {
            var quoted = ReservedWords.NeedsQuoting(word) ? $"\"{word}\"" : word;

            try
            {
                WitSql.Parse($"SELECT {quoted} FROM T");
            }
            catch (Exception exception)
            {
                broken.Add($"{word} was written as <{quoted}> and does not re-parse: " +
                           $"{exception.GetType().Name}");
            }
        }

        Assert.That(broken, Is.Empty,
            $"{broken.Count} keywords do not survive a round trip as an identifier:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, broken.Take(30))}");
    }

    /// <summary>
    /// And nothing is quoted that does not need to be. Over-quoting is not a correctness bug, but it
    /// is how the previous list acquired <c>KEY</c> - a word the grammar had released - and an
    /// unexplained quote in a stored expression is a thing people later copy.
    /// </summary>
    [Test]
    public void NothingIsQuotedWithoutNeedTest()
    {
        var overQuoted = Keywords()
            .Where(ReservedWords.IsReserved)
            .Where(word => Parses($"SELECT {word} FROM T") && Parses($"SELECT * FROM {word}"))
            .ToArray();

        Assert.That(overQuoted, Is.Empty,
            $"{overQuoted.Length} words are treated as reserved although the grammar accepts them " +
            $"unquoted in both positions: {string.Join(", ", overQuoted)}");
    }

    #endregion

    #region Controls

    /// <summary>
    /// The derivation's control, in both directions. Without it every assertion above could be
    /// satisfied by a method that answers <c>true</c> to everything - quoting all identifiers also
    /// round-trips perfectly.
    /// </summary>
    [Test]
    public void TheDerivationDiscriminatesTest()
    {
        Assert.Multiple(() =>
        {
            // Reserved: the seven the phase-8 plan named, plus the obvious ones.
            foreach (var word in new[]
                     {
                         "SELECT", "FROM", "WHERE", "USING", "WITH", "ROW", "COLUMN", "CROSS",
                         "INTERVAL", "PARTITION"
                     })
            {
                Assert.That(ReservedWords.IsReserved(word), Is.True, $"{word} must be reserved");
            }

            // Not reserved: ordinary identifiers, and the non-reserved keywords the grammar admits
            // as column names on purpose - KEY was made one deliberately in phase 5.
            foreach (var word in new[] { "Name", "Age", "Customer", "KEY", "COUNT", "YEAR", "TYPE" })
            {
                Assert.That(ReservedWords.IsReserved(word), Is.False, $"{word} must not be reserved");
            }
        });
    }

    /// <summary>
    /// The keyword list the tests above iterate is not empty and is not tiny. A vocabulary read that
    /// silently returned nothing would make every test in this fixture pass over an empty set.
    /// </summary>
    [Test]
    public void TheKeywordSweepCoversTheGrammarTest()
    {
        var keywords = Keywords().ToArray();

        Assert.That(keywords, Has.Length.GreaterThan(200),
            "the lexer defines more than 200 keyword tokens; a much smaller number means the sweep " +
            "is reading the vocabulary wrongly and proves nothing");

        TestContext.Out.WriteLine($"keywords swept : {keywords.Length}");
        TestContext.Out.WriteLine($"of which reserved: {keywords.Count(ReservedWords.IsReserved)}");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Every word the lexer recognises as something other than an identifier - asked of the lexer,
    /// so a keyword added to the grammar joins this sweep with no edit here.
    /// </summary>
    private static IEnumerable<string> Keywords()
    {
        var lexer = new WitSqlLexer(null);
        var vocabulary = lexer.Vocabulary;

        for (var token = 1; token <= lexer.Atn.maxTokenType; token++)
        {
            var name = vocabulary.GetSymbolicName(token);

            if (string.IsNullOrEmpty(name) || !name.All(c => char.IsAsciiLetterUpper(c) || c == '_'))
                continue;

            // Anything that does not lex back to itself as a single token is not a keyword -
            // IDENTIFIER, STRING_LITERAL and friends are named this way but match text, not a word.
            if (LexesToOneToken(name, token))
                yield return name;
        }
    }

    private static bool LexesToOneToken(string word, int expected)
    {
        var lexer = new WitSqlLexer(new Antlr4.Runtime.AntlrInputStream(word));
        lexer.RemoveErrorListeners();

        var tokens = lexer.GetAllTokens();

        return tokens.Count == 1 && tokens[0].Type == expected;
    }

    private static bool Parses(string sql)
    {
        try
        {
            return WitSql.Parse(sql).Count > 0;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
