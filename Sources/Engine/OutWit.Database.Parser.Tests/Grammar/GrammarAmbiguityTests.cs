using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Sharpen;
using OutWit.Database.Parser.Generated;

namespace OutWit.Database.Parser.Tests.Grammar;

/// <summary>
/// Reports where the grammar cannot decide an input deterministically.
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 splits <c>expression</c> into <c>searchCondition</c>/<c>predicate</c>/<c>valueExpression</c>
/// and then makes the layers <b>mutually reachable</b> — <c>predicate</c> can be a bare
/// <c>valueExpression</c>, and <c>valueExpression</c> can be a parenthesised <c>searchCondition</c>.
/// That reachability is not optional: the serializer parenthesises every binary node, so its output
/// has to re-parse. But it means an input like <c>(x)</c> becomes derivable by more than one route.
/// </para>
/// <para>
/// ANTLR resolves such a conflict silently, by alternative order, and still returns a tree. So the
/// failure mode is not a parse error — it is a tree of an unexpected shape, and an edit years later
/// that flips which alternative wins. This fixture makes that visible.
/// </para>
/// <para>
/// It runs <b>before</b> the grammar changes, deliberately: the baseline it records is the answer to
/// "was the grammar already ambiguous", which cannot be recovered once the rework has landed.
/// </para>
/// </remarks>
[TestFixture]
[Category("Grammar")]
public class GrammarAmbiguityTests
{
    /// <summary>
    /// The corpus must be decided deterministically, end to end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Before the split, 7 of 193 entries were ambiguous. After it, none are.</b>
    /// </para>
    /// <para>
    /// Six of the seven were one shape — <c>BETWEEN … AND …</c> followed by <c>AND</c>, with the
    /// conflict reported over the text starting at <c>BETWEEN</c>'s own <c>AND</c>: literally the
    /// question of which <c>AND</c> belonged to the <c>BETWEEN</c>. Moving the bounds down to
    /// <c>valueExpression</c>, which cannot derive <c>AND</c>, removed the question rather than
    /// answering it.
    /// </para>
    /// <para>
    /// The seventh was <c>NOT EXISTS (…)</c>, derivable both as <c>existsExpr</c>'s own optional
    /// <c>NOT</c> and as <c>notExpr</c> applied to <c>EXISTS</c>. Dropping the optional <c>NOT</c>
    /// from the predicate and folding the negation back in the visitor removed that one too, with no
    /// change to the emitted AST.
    /// </para>
    /// <para>
    /// This assertion is now the guard for the rest of phase 3: the layers are mutually reachable, so
    /// a later grammar edit can reintroduce ambiguity, and ANTLR would resolve it silently by
    /// alternative order with no parse error to notice.
    /// </para>
    /// </remarks>
    [Test]
    public void CorpusIsFreeOfAmbiguityTest()
    {
        var ambiguous = new List<string>();
        var fullContext = 0;
        var contextSensitive = 0;

        foreach (var sql in GrammarCorpus.All)
        {
            var report = Analyse(sql);

            fullContext += report.FullContextAttempts;
            contextSensitive += report.ContextSensitivities;

            if (report.Ambiguities.Count > 0)
            {
                ambiguous.Add($"{sql}{Environment.NewLine}  " +
                              string.Join($"{Environment.NewLine}  ", report.Ambiguities));
            }
        }

        TestContext.Out.WriteLine($"corpus entries        : {GrammarCorpus.All.Count()}");
        TestContext.Out.WriteLine($"ambiguous entries     : {ambiguous.Count}");
        TestContext.Out.WriteLine($"full-context attempts : {fullContext}");
        TestContext.Out.WriteLine($"context sensitivities : {contextSensitive}");

        Assert.That(ambiguous, Is.Empty,
            $"{ambiguous.Count} corpus entries are ambiguous:{Environment.NewLine}" +
            string.Join(Environment.NewLine, ambiguous));
    }

    private static AmbiguityReport Analyse(string sql)
    {
        var lexer = new WitSqlLexer(new AntlrInputStream(sql));
        var parser = new WitSqlParser(new CommonTokenStream(lexer));

        // Exact ambiguity detection: the default SLL mode resolves conflicts without reporting them,
        // so it cannot answer the question this fixture asks.
        parser.Interpreter.PredictionMode = PredictionMode.LL_EXACT_AMBIG_DETECTION;

        var collector = new AmbiguityCollector();

        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        parser.AddErrorListener(collector);

        parser.script();

        return new AmbiguityReport(
            collector.Ambiguities,
            collector.FullContextAttempts,
            collector.ContextSensitivities);
    }

    private sealed record AmbiguityReport(
        IReadOnlyList<string> Ambiguities,
        int FullContextAttempts,
        int ContextSensitivities);

    /// <summary>
    /// Collects ANTLR's prediction diagnostics. Only <c>ReportAmbiguity</c> is a finding; the other
    /// two are recorded as workload indicators.
    /// </summary>
    private sealed class AmbiguityCollector : BaseErrorListener, IParserErrorListener
    {
        public List<string> Ambiguities { get; } = [];

        public int FullContextAttempts { get; private set; }

        public int ContextSensitivities { get; private set; }

        public void ReportAmbiguity(Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex,
            bool exact, BitSet ambigAlts, ATNConfigSet configs)
        {
            var rule = recognizer.RuleNames[dfa.atnStartState.ruleIndex];
            var text = recognizer.TokenStream.GetText(Interval.Of(startIndex, stopIndex));

            Ambiguities.Add(
                $"rule '{rule}' over <{text}>: alternatives {{{ambigAlts}}} " +
                $"({(exact ? "exact" : "approximate")})");
        }

        public void ReportAttemptingFullContext(Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex,
            int stopIndex, BitSet conflictingAlts, SimulatorState conflictState) =>
            FullContextAttempts++;

        public void ReportContextSensitivity(Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex,
            int stopIndex, int prediction, SimulatorState acceptState) =>
            ContextSensitivities++;
    }
}
