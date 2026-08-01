using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Parser.Tests.Grammar;

namespace OutWit.Database.Parser.Tests.Serialization;

/// <summary>
/// The phase-8 instrument: everything the catalog stores must come back out unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The schema catalog persists SQL - view bodies, trigger bodies, <c>CHECK</c> conditions, computed
/// columns, <c>DEFAULT</c> values, partial-index filters - and re-reads it on every use. So the
/// write half and the read half have to be exact inverses; a gap between them is not a formatting
/// nuisance, it is schema corruption on disk.
/// </para>
/// <para>
/// The old instrument asked whether serializing to <b>text</b> reached a fixpoint, and that property
/// is blind in a way this one is not: a fixpoint is idempotent under loss. When the serializer
/// dropped a <c>UNION</c>'s second branch, pass one produced text without it and pass two produced
/// the same text again, so the comparison passed while the statement had been destroyed. Measured
/// 2026-07-31: 21 of the 124 entries it called clean were losing a clause.
/// </para>
/// <para>
/// Comparing the <b>tree</b> before and after cannot be fooled that way, because there is no second
/// pass to launder the loss. Its own weak point is the comparison, which is hand-written per type -
/// that is what <see cref="AstStructuralEqualityTests"/> exists to guard, and it found three defects
/// in <c>Is</c> before this test was ever run in anger.
/// </para>
/// </remarks>
[TestFixture]
[Category("Grammar")]
public class AstMemoryPackRoundTripTests
{
    #region Tests

    [Test]
    public void EveryCorpusEntrySurvivesTheCatalogTest()
    {
        var broken = new List<string>();

        foreach (var sql in GrammarCorpus.All)
        {
            foreach (var statement in WitSql.Parse(sql))
            {
                var failure = RoundTrip(statement);

                if (failure is not null)
                    broken.Add($"{Flatten(sql)}{Environment.NewLine}    {failure}");
            }
        }

        Assert.That(broken, Is.Empty,
            $"{broken.Count} statements do not survive being stored and read back:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, broken.Take(30))}");
    }

    /// <summary>
    /// Records how much of the corpus survives, so the number is on the record rather than implied
    /// by a green test.
    /// </summary>
    [Test]
    public void RoundTripCoverageIsRecordedTest()
    {
        var statements = GrammarCorpus.All.SelectMany(WitSql.Parse).ToArray();
        var broken = statements.Count(statement => RoundTrip(statement) is not null);

        TestContext.Out.WriteLine($"statements       : {statements.Length}");
        TestContext.Out.WriteLine($"survive the catalog: {statements.Length - broken}");
        TestContext.Out.WriteLine($"lost or refused  : {broken}");

        Assert.Pass("characterisation only - see the printed result");
    }

    #endregion

    #region Functions

    /// <summary>Returns null when the statement survives, or a description of how it did not.</summary>
    private static string? RoundTrip(WitSqlStatement statement)
    {
        byte[] bytes;

        try
        {
            bytes = MemoryPackSerializer.Serialize(statement);
        }
        catch (Exception exception)
        {
            return $"could not be stored: {exception.GetType().Name}: {Flatten(exception.Message)}";
        }

        ModelBase? back;

        try
        {
            back = MemoryPackSerializer.Deserialize<WitSqlStatement>(bytes);
        }
        catch (Exception exception)
        {
            return $"stored as {bytes.Length} bytes, could not be read back: " +
                   $"{exception.GetType().Name}: {Flatten(exception.Message)}";
        }

        if (back is null)
            return $"stored as {bytes.Length} bytes and read back as null";

        return statement.Is(back)
            ? null
            : $"stored as {bytes.Length} bytes and came back different " +
              $"({back.GetType().Name} vs {statement.GetType().Name})";
    }

    private static string Flatten(string text) => text.ReplaceLineEndings(" ").Trim();

    #endregion
}
