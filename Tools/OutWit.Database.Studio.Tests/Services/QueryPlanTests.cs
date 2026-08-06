using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The plan panel (WS-27, WS-28), read from what the engine actually returns.
///
/// The first version of this measurement was wrong, and the reason is worth keeping: run against the
/// fixture's three-row Orders table, EXPLAIN never once used an index, and the conclusion "this engine
/// has no index access" was one sentence away from being written down. The planner refuses to consider
/// an index below ten rows (<c>MIN_ROWS_FOR_INDEX</c>), so the instrument's table was the finding. Every
/// case here fills the table first.
/// </summary>
[TestFixture]
public class QueryPlanTests
{
    #region Fixture

    private StudioFixture m_fixture = null!;

    /// <summary>
    /// Above the planner's threshold for looking at an index at all.
    /// </summary>
    private const int ROWS = 40;

    [SetUp]
    public async Task SetUp()
    {
        m_fixture = await StudioFixture.CreateAsync();

        for (var i = 0; i < ROWS; i++)
            await m_fixture.Database.ExecuteNonQueryAsync(
                $"INSERT INTO Orders (CustomerId, Total, Status) VALUES ({i % 3 + 1}, {100 + i}, 'new')");
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    private async Task<QueryPlan> PlanOf(string sql)
    {
        var result = await m_fixture.Database.ExecuteQueryAsync($"EXPLAIN {sql}");

        Assert.That(result.ErrorMessage, Is.Null, sql);

        return QueryPlanReader.Read(result.Data);
    }

    #endregion

    #region The tree

    [Test]
    public async Task ThePlanIsATreeAndNotThreeColumnsTest()
    {
        var plan = await PlanOf("SELECT * FROM Orders");

        Assert.That(plan.IsEmpty, Is.False);
        Assert.That(plan.Roots, Has.Count.EqualTo(1), "one plan, one root");

        // The engine's own shape: ExcludeInternal <- ALIAS <- SCAN TABLE.
        var root = plan.Roots[0];

        Assert.That(root.Children, Has.Count.EqualTo(1));
        Assert.That(Depth(root), Is.EqualTo(3));

        var scan = plan.All.Single(node => node.Kind == PlanOperatorKind.TableScan);

        Assert.That(scan.TableName, Is.EqualTo("Orders"));
        Assert.That(scan.Operator, Is.EqualTo("SCAN TABLE Orders"),
            "the operator, without the engine's indentation and without its column list");
        Assert.That(scan.Columns, Does.Contain("Total"), "which is kept, as a detail");
    }

    [Test]
    public async Task AnEmptyOrUnrelatedResultIsAnEmptyPlanRatherThanAThrowTest()
    {
        Assert.That(QueryPlanReader.Read(null).IsEmpty, Is.True);

        var notAPlan = await m_fixture.Database.ExecuteQueryAsync("SELECT * FROM Orders LIMIT 1");

        Assert.That(QueryPlanReader.Read(notAPlan.Data).IsEmpty, Is.True,
            "a result that is not a plan is not a plan, and the panel says so");
    }

    #endregion

    #region What it highlights

    /// <summary>
    /// The one the design draws in amber, and it is real: an index turns this scan into a seek.
    /// </summary>
    [Test]
    public async Task AScanUnderAFilterIsMarkedTest()
    {
        var plan = await PlanOf("SELECT * FROM Orders WHERE Total = 105");

        var scan = plan.All.Single(node => node.Kind == PlanOperatorKind.TableScan);

        Assert.That(scan.Warning, Is.Not.Null);
        Assert.That(scan.Warning, Does.Contain("Orders"));
        Assert.That(plan.ScannedTables, Does.Contain("Orders"));
    }

    /// <summary>
    /// The measurement that makes the mark above worth showing: creating the index CHANGES the plan.
    /// Without this the panel would be pointing at something nobody can do anything about.
    /// </summary>
    [Test]
    public async Task CreatingTheIndexTurnsTheScanIntoASeekTest()
    {
        var before = await PlanOf("SELECT * FROM Orders WHERE Total = 105");

        Assert.That(before.All.Any(node => node.Kind == PlanOperatorKind.TableScan), Is.True);
        Assert.That(before.All.Any(node => node.Kind == PlanOperatorKind.IndexAccess), Is.False);

        await m_fixture.Database.ExecuteNonQueryAsync("CREATE INDEX IX_Orders_Total ON Orders (Total)");

        var after = await PlanOf("SELECT * FROM Orders WHERE Total = 105");

        var seek = after.All.SingleOrDefault(node => node.Kind == PlanOperatorKind.IndexAccess);

        Assert.That(seek, Is.Not.Null, "the plan must change, or the advice is empty");
        Assert.That(seek!.IndexName, Is.EqualTo("IX_Orders_Total"));
        Assert.That(seek.TableName, Is.EqualTo("Orders"));
        Assert.That(seek.Warning, Is.Null, "index access is the good case and is not marked");
        Assert.That(after.All.Any(node => node.Kind == PlanOperatorKind.TableScan), Is.False);
    }

    /// <summary>
    /// The engine finding stage 3 measured, drawn where somebody will see it: a limit is not pushed
    /// into a sort, so paging a large table sorts all of it, once per page.
    /// </summary>
    [Test]
    public async Task ASortUnderALimitIsMarkedTest()
    {
        var plan = await PlanOf("SELECT * FROM Orders ORDER BY Total LIMIT 5");

        var sort = plan.All.Single(node => node.Kind == PlanOperatorKind.Sort);

        Assert.That(sort.Warning, Is.Not.Null);
        Assert.That(sort.Warning, Does.Contain("LIMIT"));
    }

    /// <summary>
    /// The negative control for both marks: a query that asks for the whole table gets a scan, and
    /// there is nothing wrong with that. A panel that marks every scan tells nobody anything.
    /// </summary>
    [Test]
    public async Task APlainScanAndAPlainSortAreNotMarkedTest()
    {
        var wholeTable = await PlanOf("SELECT * FROM Orders");

        Assert.That(wholeTable.Warnings, Is.Empty,
            "reading a table the query asked for in full is not a finding");

        var sortWithoutLimit = await PlanOf("SELECT * FROM Orders ORDER BY Total");

        Assert.That(sortWithoutLimit.All.Single(node => node.Kind == PlanOperatorKind.Sort).Warning,
            Is.Null, "a sort the query asked for is not a finding either");
    }

    [Test]
    public async Task AJoinIsRecognisedTest()
    {
        var plan = await PlanOf(
            "SELECT c.Name, o.Total FROM Customers c JOIN Orders o ON o.CustomerId = c.Id");

        Assert.That(plan.All.Any(node => node.Kind == PlanOperatorKind.Join), Is.True);
    }

    #endregion

    #region What it does not claim

    /// <summary>
    /// WS-28 says an estimate must not be passed off as a measurement. Measured 2026-08-06: this engine
    /// returns id, parent and detail and NO numbers of any kind - so there is not even an estimate to
    /// label. Pinned here so that the day EXPLAIN ANALYZE arrives, this case goes red and the panel is
    /// told to start showing what it now can.
    /// </summary>
    [Test]
    public async Task TheEngineReturnsNoRowEstimatesAtAllTest()
    {
        var result = await m_fixture.Database.ExecuteQueryAsync(
            "EXPLAIN SELECT c.Name, SUM(o.Total) FROM Customers c JOIN Orders o ON o.CustomerId = c.Id " +
            "WHERE o.Total > 100 GROUP BY c.Name ORDER BY c.Name LIMIT 5");

        Assert.That(result.Data!.Columns.Cast<System.Data.DataColumn>().Select(column => column.ColumnName),
            Is.EquivalentTo(new[] { "id", "parent", "detail" }),
            "three columns: no rows, no cost, no time. PINS THE ENGINE AS IT IS, not as it should be");
    }

    #endregion

    #region Tools

    private static int Depth(PlanNode node)
    {
        return node.Children.Count == 0 ? 1 : 1 + node.Children.Max(Depth);
    }

    #endregion
}
