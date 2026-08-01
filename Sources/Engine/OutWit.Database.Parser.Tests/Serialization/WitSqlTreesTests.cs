using OutWit.Common.Abstract;
using OutWit.Database.Parser.Analysis;
using OutWit.Database.Parser.Tests.Grammar;

namespace OutWit.Database.Parser.Tests.Serialization;

/// <summary>
/// Pins <see cref="WitSqlTrees"/> against <c>ModelBase.Is</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>WitSqlTrees.SameIgnoringPositions</c> is a second implementation of equality over the AST,
/// and a second implementation of anything is the shape behind most of what this project has found:
/// a converter reachable from one caller, a partial second builder, an overload of the same name, a
/// fast path that became a second validator. It earns its existence only if it cannot disagree with
/// the first one about anything except the one thing it is meant to ignore.
/// </para>
/// <para>
/// So it is held to exactly that: same answer as <c>Is</c> everywhere, <b>except</b> that a change
/// to a source position must be visible to <c>Is</c> and invisible to it. Both halves are asserted;
/// asserting only the first would be satisfied by a method that returns whatever <c>Is</c> returns,
/// and asserting only the second by a method that always returns true.
/// </para>
/// </remarks>
[TestFixture]
[Category("Grammar")]
public class WitSqlTreesTests
{
    #region Agreement

    [Test]
    public void AgreesWithIsOnEveryChangeThatIsNotAPositionTest()
    {
        var disagreements = new List<string>();
        var compared = 0;

        foreach (var sql in GrammarCorpus.All)
        {
            var original = Root(sql);
            var count = AstMutationSites.Count(original);

            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var mutant = Root(sql);
                var change = AstMutationSites.Mutate(mutant, ordinal);

                if (change is null || IsPositionChange(change))
                    continue;

                compared++;

                var byIs = original.Is(mutant);
                var byTrees = WitSqlTrees.SameIgnoringPositions(original, mutant);

                if (byIs != byTrees)
                {
                    disagreements.Add($"{Flatten(sql)}{Environment.NewLine}    {change}" +
                                      $"{Environment.NewLine}    Is={byIs} SameIgnoringPositions={byTrees}");
                }
            }
        }

        TestContext.Out.WriteLine($"changes compared: {compared}");

        Assert.That(disagreements, Is.Empty,
            $"{disagreements.Count} changes are judged differently by the two comparisons:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, disagreements.Take(20))}");
    }

    #endregion

    #region The one deliberate difference

    [Test]
    public void APositionChangeIsSeenByIsAndIgnoredHereTest()
    {
        var seenByIs = 0;
        var ignoredHere = 0;

        foreach (var sql in GrammarCorpus.All)
        {
            var original = Root(sql);
            var count = AstMutationSites.Count(original);

            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var mutant = Root(sql);
                var change = AstMutationSites.Mutate(mutant, ordinal);

                if (change is null || !IsPositionChange(change))
                    continue;

                if (!original.Is(mutant))
                    seenByIs++;

                if (WitSqlTrees.SameIgnoringPositions(original, mutant))
                    ignoredHere++;
                else
                    Assert.Fail($"a source position was not ignored: {change}");
            }
        }

        TestContext.Out.WriteLine($"position changes: seen by Is {seenByIs}, ignored here {ignoredHere}");

        Assert.Multiple(() =>
        {
            Assert.That(seenByIs, Is.GreaterThan(0),
                "Is must still compare positions - they are stored, so a catalog round trip has to " +
                "bring them back");

            Assert.That(ignoredHere, Is.EqualTo(seenByIs),
                "every position change Is sees must be ignored here, or a faithful rendering gets " +
                "reported as unfaithful");
        });
    }

    #endregion

    #region Helpers

    private static bool IsPositionChange(string change) =>
        change.Contains(".Line:", StringComparison.Ordinal) ||
        change.Contains(".Column:", StringComparison.Ordinal);

    private static ModelBase Root(string sql) => WitSql.Parse(sql)[0];

    private static string Flatten(string sql) => sql.ReplaceLineEndings(" ").Trim();

    #endregion
}
