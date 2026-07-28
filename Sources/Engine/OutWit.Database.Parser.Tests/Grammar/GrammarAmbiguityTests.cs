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
    /// The seven corpus entries the grammar cannot currently decide, measured 2026-07-28 before any
    /// rule was changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Six of the seven are the same shape</b>: <c>BETWEEN … AND …</c> followed by <c>AND</c>. The
    /// reported conflict is over the text starting at <c>BETWEEN</c>'s <c>AND</c> — literally the
    /// question of which <c>AND</c> belongs to the <c>BETWEEN</c>. This is the phase-3 defect,
    /// localised structurally by a tool that never executes a query.
    /// </para>
    /// <para>
    /// It agrees exactly with what the SQLite oracle found by running queries: shapes where
    /// <c>BETWEEN</c> is followed by <c>OR</c>, by <c>THEN</c>, by <c>AS</c>, or by end-of-clause are
    /// <b>not</b> ambiguous and <b>do</b> return SQLite's answer. Two independent instruments, same
    /// conclusion: the defect is <c>BETWEEN</c> followed by <c>AND</c>, not "the lower bound is
    /// interior".
    /// </para>
    /// <para>
    /// The seventh is unrelated and benign: <c>NOT EXISTS (…)</c> can be read as the <c>NOT</c> of
    /// <c>existsExpr</c> or as <c>notExpr</c> applied to <c>EXISTS</c>. Both mean the same thing, so
    /// nothing is currently wrong — but it is a real ambiguity resolved silently by alternative
    /// order, and the rework should remove it rather than inherit it.
    /// </para>
    /// </remarks>
    private static readonly string[] KnownAmbiguousEntries =
    [
        "CREATE TABLE F (Id INT, Age INT, CHECK (Age BETWEEN 0 AND 150 AND Id > 0))",
        "SELECT * FROM T WHERE NOT EXISTS (SELECT 1 FROM S WHERE S.Id = T.Id)",
        "SELECT * FROM T WHERE Age BETWEEN 1 AND 10 AND Flags = 1",
        "SELECT * FROM T WHERE Age NOT BETWEEN 1 AND 10 AND Flags = 1",
        "SELECT * FROM T WHERE Age BETWEEN 1 AND 10 AND Flags BETWEEN 1 AND 2",
        "DELETE FROM T WHERE Age NOT BETWEEN 1 AND 10 AND Id = 5",
        "UPDATE T SET Age = 0 WHERE Age BETWEEN 1 AND 10 AND Flags = 1",
    ];

    /// <summary>
    /// Pins the ambiguity baseline, so that the rework cannot introduce a <b>new</b> one unnoticed.
    /// </summary>
    /// <remarks>
    /// This is the test that matters while phase 3 is in flight. Making the layers mutually reachable
    /// is what risks fresh ambiguity, and ANTLR resolves such a conflict silently by alternative
    /// order — there is no parse error to notice. An exact-set assertion turns that into a build
    /// failure the moment it happens.
    /// </remarks>
    [Test]
    public void AmbiguousCorpusEntriesMatchTheRecordedBaselineTest()
    {
        var ambiguous = GrammarCorpus.All
            .Where(sql => Analyse(sql).Ambiguities.Count > 0)
            .ToArray();

        Assert.That(ambiguous, Is.EquivalentTo(KnownAmbiguousEntries),
            "the set of ambiguous corpus entries changed. A new entry means the grammar became less " +
            "decidable; a missing one means a defect was fixed and this baseline should shrink.");
    }

    /// <summary>
    /// The state phase 3 is aiming at. Prints the full-context and context-sensitivity counts too:
    /// neither is a defect, but a jump in either after the rework means the parser started doing
    /// markedly more work for the same input.
    /// </summary>
    [Test]
    [Ignore("CONFIRMED 2026-07-28: 7 of 193 corpus entries are ambiguous on the current grammar. " +
            "Six are BETWEEN followed by AND - the phase-3 defect - and the seventh is the benign " +
            "NOT EXISTS overlap between existsExpr and notExpr. Remove this marker in the PR that " +
            "lands the searchCondition/predicate/valueExpression split. " +
            "AmbiguousCorpusEntriesMatchTheRecordedBaselineTest pins the current set meanwhile.")]
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
