using Antlr4.Runtime;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Analysis;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Every function the grammar admits must be one the engine admits, asked of the whole vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// <c>ExpressionFunctions.KNOWN</c> is what decides whether a <c>CHECK</c>, a computed column or an
/// index expression naming a function is accepted at declaration. A name missing from it would
/// <b>refuse a schema that works</b>, which is far worse than the defect it was added to close, so
/// the set needs a net that cannot be forgotten.
/// </para>
/// <para>
/// This is that net, and it is built the way <c>KeywordAsIdentifierCorpusTests</c> is built: the
/// vocabulary comes from the <b>generated lexer itself</b>, not from a list somebody maintains, so a
/// function added to the grammar tomorrow is covered without anyone remembering. Each token is
/// offered to the parser as a function call; the ones the grammar accepts in that position are
/// exactly the functions a consumer can spell, and every one of them must be known.
/// </para>
/// <para>
/// Failures are reported <b>by name</b>, never by count - a count goes green again the moment one
/// name is added and another removed, which is the failure mode <c>GrammarRoundTripTests</c> was
/// rebuilt to avoid.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class KnownFunctionCorpusTests
{
    #region Pinned exceptions

    /// <summary>
    /// Function tokens the grammar admits that the engine deliberately does not evaluate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CAST</c> appears in the grammar's <c>functionName</c> rule as well as having its own
    /// <c>CAST(x AS type)</c> form, so <c>SELECT CAST(1)</c> parses - and means nothing, because a
    /// cast without a target type is not a conversion. Measured: it reaches the evaluator and is
    /// refused with <i>"Function not supported: CAST"</i>. Refusing it is the right answer; the only
    /// fault is where the refusal happens, and moving that into the grammar is grammar work, which
    /// this project does after everything that touches it rather than in passing.
    /// </para>
    /// <para>
    /// Pinned by name and not by count. The list is an admission, not a target - if it grows, the
    /// entry needs a reason as specific as this one.
    /// </para>
    /// </remarks>
    private static readonly string[] AdmittedByTheGrammarAndNotEvaluated = ["CAST"];

    #endregion

    #region Tests

    /// <summary>
    /// The corpus: every lexer token that can head a function call must be a function the engine has.
    /// </summary>
    [Test]
    public void EveryFunctionTheGrammarAdmitsIsKnownToTheEngineTest()
    {
        var callable = new List<string>();
        var unknown = new List<string>();

        foreach (var word in LexerWords())
        {
            if (!IsAFunctionKeyword(word))
                continue;

            callable.Add(word);

            if (!ExpressionFunctions.IsKnown(word)
                && !AdmittedByTheGrammarAndNotEvaluated.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                unknown.Add(word);
            }
        }

        TestContext.Out.WriteLine($"{callable.Count} lexer tokens can head a function call");

        Assert.Multiple(() =>
        {
            Assert.That(callable, Is.Not.Empty,
                "the corpus found no function tokens at all, so it is measuring nothing - the "
                + "vocabulary or the probe has changed shape");

            Assert.That(unknown, Is.Empty,
                "these functions parse but the engine does not list them as known, so a CHECK or a "
                + "computed column using one would now be refused at declaration: "
                + string.Join(", ", unknown));
        });
    }

    /// <summary>
    /// The control: the corpus must be able to fail.
    /// </summary>
    /// <remarks>
    /// A corpus that answers "all known" because its probe never recognises anything is worse than
    /// no corpus, and this project has shipped that instrument before. A name that is deliberately
    /// not a function must come back unknown.
    /// </remarks>
    [Test]
    public void TheCorpusCanTellAnUnknownFunctionFromAKnownOneTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExpressionFunctions.IsKnown("UPPER"), Is.True);
            Assert.That(ExpressionFunctions.IsKnown("NoSuchFunctionAnywhere"), Is.False);
            Assert.That(IsAFunctionKeyword("UPPER"), Is.True);
            Assert.That(IsAFunctionKeyword("SELECT"), Is.False,
                "a keyword that is not a function is not a function name");
            Assert.That(IsAFunctionKeyword("NoSuchFunctionAnywhere"), Is.False,
                "and neither is an ordinary identifier, which functionName also admits - this is "
                + "the half whose absence made the corpus report the lexer's whitespace rule as a "
                + "function");
        });
    }

    /// <summary>
    /// And the walk over an expression must find a function wherever it sits.
    /// </summary>
    /// <remarks>
    /// The aggregate defect is the reason this is asked separately: a check that only looked at the
    /// top of the expression covered four of nineteen node types and answered "fine" for the rest.
    /// </remarks>
    [TestCase("NoSuchFunc(V)", TestName = "at the top")]
    [TestCase("V + NoSuchFunc(V)", TestName = "inside arithmetic")]
    [TestCase("CASE WHEN NoSuchFunc(V) > 1 THEN 1 ELSE 0 END", TestName = "inside a CASE")]
    [TestCase("V BETWEEN 1 AND NoSuchFunc(V)", TestName = "inside a BETWEEN")]
    [TestCase("V IN (1, NoSuchFunc(V))", TestName = "inside an IN list")]
    [TestCase("UPPER(NoSuchFunc(V))", TestName = "as an argument to a known function")]
    public void AnUnknownFunctionIsFoundWhereverItSitsTest(string sql)
    {
        // Case-insensitively: the parser normalises a function name, and what matters is which name
        // was reported, not how it was cased on the way through.
        Assert.That(ExpressionFunctions.FirstUnknownFunction(WitSql.ParseExpression(sql)),
            Is.EqualTo("NoSuchFunc").IgnoreCase);
    }

    #endregion

    #region Helpers

    private static IEnumerable<string> LexerWords()
    {
        // The generated lexer's own rule names, so the corpus grows with the grammar - the same
        // source KeywordAsIdentifierCorpusTests uses.
        //
        // Not the vocabulary's literal names: this grammar spells its keywords out of
        // case-insensitive fragments (UPPER : U P P E R), so ANTLR records no literal for any of
        // them and asking for one yields nothing at all. That is what the "found no function tokens"
        // guard below caught on the first run of this fixture - the corpus was measuring an empty
        // set and would have reported every function as known.
        //
        // A few rules carry a _FUNC suffix to avoid colliding with a parser rule of the same name,
        // and match the word without it: CONCAT_FUNC is CONCAT. Both spellings are offered, and the
        // probe keeps whichever the grammar actually accepts.
        foreach (var name in WitSqlLexer.ruleNames)
        {
            if (name.Length < 2 || !name.All(c => char.IsLetter(c) || c == '_'))
                continue;

            yield return name;

            if (name.EndsWith("_FUNC", StringComparison.Ordinal))
                yield return name[..^"_FUNC".Length];
        }
    }

    /// <summary>
    /// Whether the grammar has a function token of this name - as opposed to merely tolerating it.
    /// </summary>
    /// <remarks>
    /// <b>The word must lex to a keyword, not to <c>IDENTIFIER</c>.</b> Without that half, the probe
    /// answers yes for every word on earth: <c>functionName</c> admits <c>IDENTIFIER</c>, so
    /// <c>SELECT WS(1)</c> parses perfectly well and the corpus reported the lexer's whitespace rule
    /// as an unknown function. It is the same mistake the dialect oracle's first corpus entry made -
    /// measuring two things with one probe and attributing the result to the wrong one.
    /// </remarks>
    private static bool IsAFunctionKeyword(string word)
    {
        return LexesAsAKeyword(word) && ParsesAsACallTo(word);
    }

    private static bool LexesAsAKeyword(string word)
    {
        var lexer = new WitSqlLexer(CharStreams.fromString(word));
        lexer.RemoveErrorListeners();

        var tokens = lexer.GetAllTokens();

        return tokens.Count == 1
               && tokens[0].Type != WitSqlLexer.IDENTIFIER
               && tokens[0].Text.Length == word.Length;
    }

    private static bool ParsesAsACallTo(string word)
    {
        try
        {
            var statements = WitSql.Parse($"SELECT {word}(1) FROM T");

            if (statements.Count != 1 || statements[0] is not WitSqlStatementSelect select)
                return false;

            return WitSqlNodes.SelfAndDescendants(select.SelectList[0].Expression)
                .OfType<WitSqlExpressionFunctionCall>()
                .Any(call => string.Equals(call.FunctionName, word, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
