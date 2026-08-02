using OutWit.Database.AdoNet;
using Xunit;

namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

/// <summary>
/// The corpus's own <c>WitDatabase</c> column, run against WitDatabase.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written during the phase-9d pre-release audit, because the column was never executed.</b>
/// <c>DialectCorpus.Entry</c> has carried a <c>WitDatabase</c> spelling since the oracle was built,
/// documented as <i>"how WitDatabase would spell it if it had it"</i> - ten sentences that nothing
/// ran. That is a record about the engine rather than a measurement of it, and this project has
/// found such records false ten times over.
/// </para>
/// <para>
/// It could not have been run before: every one of these shapes was genuinely absent when the corpus
/// was written. Phases 9a to 9d built them, so the claim is now checkable, and checking it is what
/// closes the loop the oracle only half draws - <c>DialectCoverageOracle</c> says the drop-in targets
/// accept a capability, and this says WitDatabase accepts it <b>in the same words the corpus wrote
/// down</b>.
/// </para>
/// <para>
/// It asserts rather than reports, unlike the oracle. The oracle measures servers this repository
/// does not control and so can only describe them; this measures the engine, where a capability the
/// corpus claims and the engine refuses is a defect in one of the two.
/// </para>
/// </remarks>
public class WitDatabaseCorpusTests
{
    [Fact]
    public void EveryCapabilityTheCorpusSpellsForWitDatabaseWorksTest()
    {
        var refused = new List<string>();
        var attempted = 0;

        foreach (var entry in DialectCorpus.All)
        {
            attempted++;

            using var connection = new WitDbConnection("Data Source=:memory:");
            connection.Open();

            foreach (var statement in DialectCorpus.Schema)
                Run(connection, statement);

            try
            {
                Run(connection, entry.WitDatabase);
            }
            catch (Exception exception)
            {
                refused.Add($"{entry.Capability}: {exception.Message.Split('\n')[0]}");
            }
        }

        // A loop over an empty corpus passes for the wrong reason, and this session has already
        // shipped one instrument that reported success because its probe found nothing at all.
        Assert.True(attempted >= 10,
            $"the corpus yielded {attempted} entries, so this test is measuring almost nothing");

        Assert.True(
            refused.Count == 0,
            "the corpus spells these for WitDatabase and WitDatabase refuses them - either the "
            + "capability regressed or the corpus is describing something that was never built:"
            + Environment.NewLine + string.Join(Environment.NewLine, refused));
    }

    /// <summary>
    /// The control: the probe must be able to tell a refusal from an acceptance.
    /// </summary>
    /// <remarks>
    /// Without this, a harness that swallowed every error would report the whole corpus as working -
    /// which is the exact failure <c>DialectProbe</c> was given positive and negative controls to
    /// avoid, and which this fixture would otherwise reintroduce beside it.
    /// </remarks>
    [Fact]
    public void TheProbeCanTellARefusalFromAnAcceptanceTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        foreach (var statement in DialectCorpus.Schema)
            Run(connection, statement);

        Run(connection, "SELECT Id FROM T");

        Assert.ThrowsAny<Exception>(() => Run(connection, "SELECT DELIBERATE NONSENSE FROM"));
    }

    private static void Run(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
