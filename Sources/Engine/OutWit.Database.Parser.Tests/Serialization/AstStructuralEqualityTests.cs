using OutWit.Common.Abstract;
using OutWit.Database.Parser.Tests.Grammar;

namespace OutWit.Database.Parser.Tests.Serialization;

/// <summary>
/// The control for the AST round-trip instrument: proves <c>ModelBase.Is</c> actually discriminates
/// on every value the AST stores.
/// </summary>
/// <remarks>
/// <para>
/// The round-trip instrument answers "did the AST survive being written to the catalog and read
/// back" by comparing with <c>Is</c>. Every <c>Is</c> in this assembly is hand-written, so a
/// forgotten property would make that instrument green and powerless - the same way a fixpoint
/// round-trip was blind to a clause the serializer dropped, because the loss was idempotent.
/// </para>
/// <para>
/// So: change one stored value anywhere in a parsed statement and require the comparison to notice,
/// <b>at the root</b>. Comparing at the mutated node would only prove that node's own <c>Is</c>
/// reads the property; comparing at the root also proves every ancestor propagates it.
/// </para>
/// <para>
/// Two independent parses are used rather than <c>Clone()</c>. A <c>Clone</c> that copies a list but
/// shares its elements would let a mutation reach both sides and report a false "undetected"; two
/// parses of the same text share nothing, so the confound cannot arise.
/// </para>
/// </remarks>
[TestFixture]
[Category("Grammar")]
public class AstStructuralEqualityTests
{
    #region Tests

    [Test]
    public void EveryStoredValueIsComparedByIsTest()
    {
        var undetected = new List<string>();
        var uncovered = new List<string>();
        var threw = new List<string>();
        var sites = 0;

        foreach (var sql in GrammarCorpus.All)
        {
            var original = Root(sql);
            var count = AstMutationSites.Count(original);

            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var mutant = Root(sql);
                var change = AstMutationSites.Mutate(mutant, ordinal);

                if (change is null)
                {
                    uncovered.Add($"{Flatten(sql)} [site {ordinal}]");
                    continue;
                }

                sites++;

                // A comparison that throws is reported, not allowed to abort the run: an Is that is
                // not null-tolerant is its own finding, and one of them would otherwise hide every
                // result after it.
                try
                {
                    if (original.Is(mutant))
                        undetected.Add($"{Flatten(sql)}{Environment.NewLine}    changed {change}");
                }
                catch (Exception exception)
                {
                    threw.Add($"{Flatten(sql)}{Environment.NewLine}    changed {change}" +
                              $"{Environment.NewLine}    Is threw {exception.GetType().Name}");
                }
            }
        }

        // Reported rather than asserted: a property with no mutation strategy is a gap in the
        // control's coverage, and a silent gap reads as "covered" when it is not.
        TestContext.Out.WriteLine($"mutations applied     : {sites}");
        TestContext.Out.WriteLine($"sites with no strategy: {uncovered.Count}");
        TestContext.Out.WriteLine($"comparisons that threw: {threw.Count}");

        Assert.Multiple(() =>
        {
            Assert.That(undetected, Is.Empty,
                $"{undetected.Count} changes to a stored value were NOT seen by ModelBase.Is, so an " +
                $"AST round-trip compared with Is cannot detect losing them:{Environment.NewLine}" +
                string.Join(Environment.NewLine, undetected.Take(40)));

            Assert.That(threw, Is.Empty,
                $"{threw.Count} comparisons threw instead of reporting a difference:" +
                $"{Environment.NewLine}{string.Join(Environment.NewLine, threw.Take(20))}");
        });
    }

    /// <summary>
    /// The control's own control: a statement compared against an untouched second parse of itself
    /// must be equal. If this fails, the harness is manufacturing differences and every result above
    /// is meaningless.
    /// </summary>
    [Test]
    public void UnmutatedParsesAreEqualTest()
    {
        var different = GrammarCorpus.All
            .Where(sql => !Root(sql).Is(Root(sql)))
            .Select(Flatten)
            .ToArray();

        Assert.That(different, Is.Empty,
            $"{different.Length} corpus entries do not compare equal to a second parse of themselves:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, different)}");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// The whole parse as one comparable node. Multi-statement entries are wrapped so nothing is
    /// dropped: comparing only the first statement would hide a difference in the second.
    /// </summary>
    private static ModelBase Root(string sql)
    {
        var statements = WitSql.Parse(sql);

        return statements.Count == 1
            ? statements[0]
            : new StatementList { Statements = statements };
    }

    private static string Flatten(string sql) => sql.ReplaceLineEndings(" ").Trim();

    #endregion

    #region Multi-statement wrapper

    private sealed class StatementList : ModelBase
    {
        public required IReadOnlyList<Parser.Statements.WitSqlStatement> Statements { get; init; }

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not StatementList list || list.Statements.Count != Statements.Count)
                return false;

            return !Statements.Where((statement, i) => !statement.Is(list.Statements[i], tolerance)).Any();
        }

        public override ModelBase Clone() => new StatementList { Statements = Statements };
    }

    #endregion
}
