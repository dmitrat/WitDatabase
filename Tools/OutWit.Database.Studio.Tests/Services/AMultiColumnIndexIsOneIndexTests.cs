using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// One index over two columns is ONE index, wherever Studio counts them.
/// </summary>
/// <remarks>
/// <para>
/// <b>INFORMATION_SCHEMA.INDEXES publishes a row per indexed COLUMN</b> - the SQL Server and MySQL
/// convention, and not a defect. Three of Studio's four readers group those rows by index name; the
/// fourth, <c>GetIndexesAsync</c>, selected <c>INDEX_NAME</c> and returned whatever came back. So an
/// index over two columns was counted twice in the tree, listed twice in the Explorer filter and in
/// the palette, and - the part nobody reported - <b>written twice into a dump</b>.
/// </para>
/// <para>
/// Reported from two independent directions on the same database: the tree said 8 indexes where
/// <i>Verify by reading</i>, which goes through the grouped reader, said 7.
/// </para>
/// <para>
/// The third case is the control. A reader that answered "one index" by collapsing everything would
/// pass the first two and lose a database's second index, so the count of DIFFERENT indexes is
/// asserted beside the count of one index's columns.
/// </para>
/// </remarks>
[TestFixture]
public class AMultiColumnIndexIsOneIndexTests
{
    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync(withSchema: false);

        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, PlacedAt VARCHAR(30), Status VARCHAR(20))");

        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE INDEX IX_Orders_Customer_Placed ON Orders (CustomerId, PlacedAt)");
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region The catalogue

    /// <summary>
    /// What the tree, the filter and the palette all read.
    /// </summary>
    [Test]
    public async Task AnIndexOverTwoColumnsIsNamedOnceTest()
    {
        var indexes = await m_studio.Database.GetIndexesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(indexes, Has.Count.EqualTo(1),
                "an index over two columns is one index");

            Assert.That(indexes, Has.Exactly(1).EqualTo("IX_Orders_Customer_Placed"));
        });
    }

    /// <summary>
    /// The control: collapsing everything would also make this pass one index short.
    /// </summary>
    [Test]
    public async Task TwoDifferentIndexesAreStillTwoTest()
    {
        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE INDEX IX_Orders_Status ON Orders (Status)");

        var indexes = await m_studio.Database.GetIndexesAsync();

        Assert.That(indexes, Is.EquivalentTo(new[] { "IX_Orders_Customer_Placed", "IX_Orders_Status" }));
    }

    #endregion

    #region The dump

    /// <summary>
    /// The damage nobody reported: a script that creates the same index twice is refused on replay,
    /// which is worse than a wrong number on a screen.
    /// </summary>
    [Test]
    public async Task ADumpCreatesTheIndexOnceAndReplaysTest()
    {
        var script = await DatabaseDump.WriteAsync(m_studio.Database, new DumpOptions());

        // The claim a dump makes is that it RUNS, so that arm goes first: it fails on the defect
        // without reading the text, and what it says is what a user would have seen.
        var rebuilt = await m_studio.OpenAnotherAsync("rebuilt", withSchema: false);

        foreach (var statement in SqlScript.Split(script).Statements)
        {
            var result = await rebuilt.ExecuteQueryAsync(statement.Text);

            Assert.That(result.ErrorMessage, Is.Null.Or.Empty,
                $"the dumped script has to run: {statement.Text}");
        }

        var creations = SqlScript.Split(script).Statements
            .Count(statement => statement.Text.Contains("IX_Orders_Customer_Placed",
                StringComparison.OrdinalIgnoreCase));

        Assert.That(creations, Is.EqualTo(1), "the script creates the index once");

        var indexes = await rebuilt.GetIndexesAsync();

        Assert.That(indexes, Has.Exactly(1).EqualTo("IX_Orders_Customer_Placed"));
    }

    #endregion
}
