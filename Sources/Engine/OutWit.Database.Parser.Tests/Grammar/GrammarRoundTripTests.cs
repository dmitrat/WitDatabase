using OutWit.Database.Parser.Serializers;

namespace OutWit.Database.Parser.Tests.Grammar;

/// <summary>
/// The instrument that turns "the rework changed nothing else" into a measurement.
/// </summary>
/// <remarks>
/// <para>
/// The property checked is a fixpoint: parse the SQL, serialize the AST, parse that, serialize again,
/// and the two serializations must be identical. It is stronger than "the output re-parses" — a
/// serializer that dropped an <c>ESCAPE</c> clause or a <c>NOT</c> would still re-parse happily, and
/// only the second serialization reveals that the AST lost something.
/// </para>
/// <para>
/// Why this is the right net for phase 3 specifically: the restructure changes the shape of the parse
/// tree everywhere, and the visitor is rewritten against it, but the AST contract is meant to be
/// untouched. Comparing serializations compares the ASTs without needing structural equality on
/// twenty expression types. If the rework silently re-associates an operator, re-orders a
/// <c>BETWEEN</c>'s bounds, or drops a flag, the fixpoint breaks.
/// </para>
/// <para>
/// It also protects a real requirement rather than a hypothetical one:
/// <c>WitSqlExpressionSerializer</c> parenthesises every binary node unconditionally, so its output
/// is exactly the deeply-parenthesised shape the new grammar has to keep accepting.
/// </para>
/// </remarks>
[TestFixture]
[Category("Grammar")]
public class GrammarRoundTripTests
{
    /// <summary>
    /// The test that does the work while phase 3 is in flight: every round-trip failure must be one
    /// of the <b>two known pre-existing causes</b>, and anything else fails the build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured 2026-07-28, before any grammar change: 69 of 193 corpus entries do not round-trip,
    /// and every one of them is explained by exactly two defects that have nothing to do with phase 3:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>The serializer handles DML only.</b> <c>CREATE TABLE</c>, <c>ALTER TABLE</c>,
    /// <c>CREATE INDEX</c>, <c>CREATE VIEW</c>, <c>CREATE TRIGGER</c>, <c>DROP …</c> and <c>MERGE</c>
    /// all raise <c>NotSupportedException</c>. Already on record — <c>ParserFindingsEngineTests</c>
    /// cites the same exception.
    /// </description></item>
    /// <item><description>
    /// <b>Every subquery is replaced by the literal text <c>SELECT ...</c></b>. Found here, recorded
    /// in <c>SerializerSubqueryFindingsTests</c>, and confirmed to corrupt persisted schema.
    /// </description></item>
    /// </list>
    /// <para>
    /// Classifying rather than counting is what makes this useful: a plain "69 failures" pin would go
    /// green again if the rework broke a DML round-trip while some unrelated fix repaired a DDL one.
    /// </para>
    /// </remarks>
    [Test]
    public void EveryRoundTripFailureHasAKnownCauseTest()
    {
        var unexplained = GrammarCorpus.All
            .Select(sql => (Sql: sql, Failure: RoundTrip(sql)))
            .Where(entry => entry.Failure is not null && !IsKnownCause(entry.Failure!))
            .Select(entry => entry.Failure!)
            .ToArray();

        Assert.That(unexplained, Is.Empty,
            $"{unexplained.Length} corpus entries fail to round-trip for a reason that is NOT one of " +
            $"the two recorded pre-existing causes:{Environment.NewLine}" +
            string.Join(Environment.NewLine, unexplained));
    }

    /// <summary>
    /// The state the serializer should reach. Kept as an executable specification rather than a
    /// comment, following the convention used throughout <c>AuditVerification/</c>.
    /// </summary>
    [Test]
    [Ignore("CONFIRMED 2026-07-28: 69 of 193 corpus entries do not round-trip, from two pre-existing " +
            "defects - the serializer supports DML only (NotSupportedException for every DDL and for " +
            "MERGE), and every subquery is emitted as the literal text 'SELECT ...'. Neither is a " +
            "phase-3 defect. EveryRoundTripFailureHasAKnownCauseTest pins both meanwhile, so a NEW " +
            "round-trip failure still fails the build.")]
    public void CorpusReachesASerializationFixpointTest()
    {
        var broken = GrammarCorpus.All
            .Select(RoundTrip)
            .Where(outcome => outcome is not null)
            .ToArray();

        Assert.That(broken, Is.Empty,
            $"{broken.Length} of {GrammarCorpus.All.Count()} corpus entries do not round-trip:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, broken)}");
    }

    /// <summary>
    /// The two pre-existing causes, matched on the observed text rather than on a count.
    /// </summary>
    private static bool IsKnownCause(string failure) =>
        // 1 - the serializer covers DML only.
        failure.Contains("Statement serialization not supported", StringComparison.Ordinal) ||
        // 2 - every subquery becomes the literal text "SELECT ...".
        failure.Contains("SELECT ...", StringComparison.Ordinal);

    /// <summary>
    /// Reports how much of the corpus round-trips, so the number is on record rather than implied by
    /// a green test. If the rework has to concede ground somewhere, the size of the concession is
    /// visible.
    /// </summary>
    [Test]
    public void RoundTripCoverageIsRecordedTest()
    {
        var total = GrammarCorpus.All.Count();
        var failures = GrammarCorpus.All.Count(sql => RoundTrip(sql) is not null);

        TestContext.Out.WriteLine($"corpus entries   : {total}");
        TestContext.Out.WriteLine($"round-trip clean : {total - failures}");
        TestContext.Out.WriteLine($"round-trip broken: {failures}");

        Assert.Pass("characterisation only - see the printed result");
    }

    /// <summary>
    /// Returns <c>null</c> when the entry round-trips, or a description of how it failed.
    /// </summary>
    private static string? RoundTrip(string sql)
    {
        string first;

        try
        {
            first = Serialize(sql);
        }
        catch (Exception exception)
        {
            return $"{sql}{Environment.NewLine}  first parse/serialize threw: " +
                   $"{exception.GetType().Name}: {Flatten(exception.Message)}";
        }

        string second;

        try
        {
            second = Serialize(first);
        }
        catch (Exception exception)
        {
            return $"{sql}{Environment.NewLine}  serialized to <{first}>{Environment.NewLine}" +
                   $"  which does NOT re-parse: {exception.GetType().Name}: {Flatten(exception.Message)}";
        }

        return string.Equals(first, second, StringComparison.Ordinal)
            ? null
            : $"{sql}{Environment.NewLine}  pass 1: <{first}>{Environment.NewLine}  pass 2: <{second}>";
    }

    private static string Serialize(string sql) =>
        string.Join("; ", WitSql.Parse(sql).Select(WitSqlStatementSerializer.Serialize));

    private static string Flatten(string message) => message.ReplaceLineEndings(" ").Trim();
}
