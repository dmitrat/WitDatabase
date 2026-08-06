using System.Data;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The filter language and the query behind the grid (WS-30, WS-31, WS-32).
///
/// Every case sends what it builds to a real database and counts the rows that come back, because the
/// question a filter answers is "which rows", and a test of the generated TEXT answers a different
/// one - that was the whole finding of the phase-13 audit about this suite.
/// </summary>
[TestFixture]
public class GridQueryTests
{
    #region Fixture

    private StudioFixture m_fixture = null!;
    private IReadOnlyList<ColumnInfo> m_columns = null!;

    /// <summary>
    /// Ten orders: five 'Shipped', one 'shipped', four 'new'. The mixed-case pair is deliberate - it
    /// is what exposed how this engine compares strings.
    /// </summary>
    private static readonly string[] STATUSES =
        ["new", "Shipped", "new", "Shipped", "shipped", "Shipped", "new", "Shipped", "new", "Shipped"];

    [SetUp]
    public async Task SetUp()
    {
        m_fixture = await StudioFixture.CreateAsync();

        await m_fixture.Database.ExecuteNonQueryAsync("DELETE FROM Orders");

        for (var i = 0; i < STATUSES.Length; i++)
            await m_fixture.Database.ExecuteNonQueryAsync(
                $"INSERT INTO Orders (CustomerId, Total, Status) VALUES ({i % 3 + 1}, {100 + i}, '{STATUSES[i]}')");

        m_columns = await m_fixture.Database.GetColumnsAsync("Orders");
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    private ColumnInfo Column(string name) => m_columns.First(column => column.Name == name);

    /// <summary>
    /// Builds the filter, sends it, and counts what comes back.
    /// </summary>
    private async Task<int> RowsMatching(string column, string filter)
    {
        var condition = GridFilter.Parse(filter, Column(column), 0);

        Assert.That(condition, Is.Not.Null, $"\"{filter}\" produced no condition");

        var view = new GridView("Orders", [condition!], null, false, "Id", 0, 0, null);
        var result = await m_fixture.Database.ExecuteQueryAsync(GridQuery.Whole(view));

        Assert.That(result.ErrorMessage, Is.Null, GridQuery.Whole(view).Text);

        return result.Data?.Rows.Count ?? 0;
    }

    #endregion

    #region The filter language

    [Test]
    public async Task ABareWordIsASubstringTest()
    {
        // Six: five 'Shipped' and one 'shipped' - LIKE ignores case on this engine.
        Assert.That(await RowsMatching("Status", "ship"), Is.EqualTo(6));
        Assert.That(await RowsMatching("Status", "new"), Is.EqualTo(4));
        Assert.That(await RowsMatching("Status", "nothing like it"), Is.Zero);
    }

    [Test]
    public async Task ComparisonsAreSentToTheEngineTest()
    {
        Assert.That(await RowsMatching("Total", "> 105"), Is.EqualTo(4));
        Assert.That(await RowsMatching("Total", ">= 105"), Is.EqualTo(5));
        Assert.That(await RowsMatching("Total", "< 102"), Is.EqualTo(2));
        Assert.That(await RowsMatching("Total", "= 100"), Is.EqualTo(1));
    }

    [Test]
    public async Task ARangeIsBetweenTest()
    {
        Assert.That(await RowsMatching("Total", "102..105"), Is.EqualTo(4));
        Assert.That(await RowsMatching("Total", "100..100"), Is.EqualTo(1));
    }

    [Test]
    public async Task AListIsInTest()
    {
        Assert.That(await RowsMatching("Total", "IN (100, 101, 109)"), Is.EqualTo(3));
        Assert.That(await RowsMatching("Status", "IN ('new')"), Is.EqualTo(4));
    }

    [Test]
    public async Task NullAndNotNullTest()
    {
        // Customers, where one row has no Email.
        var columns = await m_fixture.Database.GetColumnsAsync("Customers");
        var email = columns.First(column => column.Name == "Email");

        foreach (var (filter, expected) in new[] { ("NULL", 1), ("NOT NULL", 2) })
        {
            var condition = GridFilter.Parse(filter, email, 0)!;
            var view = new GridView("Customers", [condition], null, false, "Id", 0, 0, null);
            var result = await m_fixture.Database.ExecuteQueryAsync(GridQuery.Whole(view));

            Assert.That(result.Data?.Rows.Count, Is.EqualTo(expected), filter);
        }
    }

    [Test]
    public async Task APatternIsPassedThroughTest()
    {
        Assert.That(await RowsMatching("Status", "LIKE 'Ship%'"), Is.EqualTo(6));
        Assert.That(await RowsMatching("Status", "LIKE 'n%'"), Is.EqualTo(4));
    }

    [Test]
    public void AnEmptyFilterIsNoFilterTest()
    {
        Assert.That(GridFilter.Parse(null, Column("Status"), 0), Is.Null);
        Assert.That(GridFilter.Parse("   ", Column("Status"), 0), Is.Null);
    }

    /// <summary>
    /// The value never passes through the language: a filter box is a text field a person types into,
    /// and this is the one place in the grid where their text meets SQL.
    /// </summary>
    [Test]
    public async Task AQuoteInAFilterIsAValueAndNotSyntaxTest()
    {
        await m_fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Customers (Name, Email) VALUES ('O''Brien; DROP TABLE Orders; --', 'x@y')");

        var columns = await m_fixture.Database.GetColumnsAsync("Customers");
        var condition = GridFilter.Parse("O'Brien", columns.First(column => column.Name == "Name"), 0)!;

        var view = new GridView("Customers", [condition], null, false, "Id", 0, 0, null);
        var result = await m_fixture.Database.ExecuteQueryAsync(GridQuery.Whole(view));

        Assert.That(result.ErrorMessage, Is.Null);
        Assert.That(result.Data?.Rows.Count, Is.EqualTo(1));

        // And the control that the injection attempt did nothing: the table is still there.
        Assert.That(await m_fixture.CountRowsAsync("Orders"), Is.EqualTo(10));
    }

    #endregion

    #region How this engine compares strings

    /// <summary>
    /// PINS THE ENGINE AS IT IS. Measured 2026-08-06 across six controlled runs: <c>=</c> is
    /// case-SENSITIVE and <c>&lt;&gt;</c> and <c>LIKE</c> are case-INSENSITIVE - so <c>= 'x'</c> and
    /// <c>&lt;&gt; 'x'</c> do not partition a table holding both 'Shipped' and 'shipped'. Five plus
    /// six is eleven, and there are ten rows.
    ///
    /// It decides what the filter row does: a bare word is LIKE, because "contains" is expected to
    /// ignore case, and <c>=</c> stays exact, because typing it is expected to mean exactly.
    /// </summary>
    [Test]
    public async Task EqualityIsCaseSensitiveAndInequalityIsNotTest()
    {
        var equal = await m_fixture.Database.ExecuteQueryAsync(
            "SELECT * FROM Orders WHERE Status = 'Shipped'");
        var notEqual = await m_fixture.Database.ExecuteQueryAsync(
            "SELECT * FROM Orders WHERE Status <> 'new'");
        var like = await m_fixture.Database.ExecuteQueryAsync(
            "SELECT * FROM Orders WHERE Status LIKE 'Shipped'");

        Assert.That(equal.Data?.Rows.Count, Is.EqualTo(5), "= excludes 'shipped'");
        Assert.That(notEqual.Data?.Rows.Count, Is.EqualTo(6), "<> includes it");
        Assert.That(like.Data?.Rows.Count, Is.EqualTo(6), "and so does LIKE, with no wildcard in it");
    }

    #endregion

    #region Pages

    [Test]
    public void HowAPageIsReachedIsADecisionAboutCostTest()
    {
        var keyed = new GridView("Orders", [], null, false, "Id", 0, 100, null);

        Assert.That(GridQuery.PagingOf(keyed), Is.EqualTo(GridPaging.First));
        Assert.That(GridQuery.PagingOf(keyed with { PageIndex = 3 }), Is.EqualTo(GridPaging.Keyset));

        // Sorted by something else: keyset would need a tie-break that does not exist.
        Assert.That(GridQuery.PagingOf(keyed with { PageIndex = 3, SortColumn = "Total" }),
            Is.EqualTo(GridPaging.Offset));

        // No key at all.
        Assert.That(GridQuery.PagingOf(keyed with { PageIndex = 3, KeyColumn = null }),
            Is.EqualTo(GridPaging.Offset));
    }

    [Test]
    public async Task PagesTileTheTableWithoutRepeatingOrDroppingARowTest()
    {
        var seen = new List<long>();
        object? anchor = null;

        for (var page = 0; page < 5; page++)
        {
            var view = new GridView("Orders", [], null, false, "Id", page, 3, anchor);
            var query = GridQuery.Page(view);
            var result = await m_fixture.Database.ExecuteQueryAsync(query.Statement);

            Assert.That(result.ErrorMessage, Is.Null, query.Statement.Text);

            var rows = result.Data!.Rows.Cast<DataRow>().Take(3).ToList();

            if (rows.Count == 0)
                break;

            seen.AddRange(rows.Select(row => System.Convert.ToInt64(row["Id"])));
            anchor = rows[^1]["Id"];
        }

        Assert.That(seen, Has.Count.EqualTo(10), "ten rows, seen once each");
        Assert.That(seen.Distinct().Count(), Is.EqualTo(10), "and no row twice");
        Assert.That(seen, Is.Ordered);
    }

    /// <summary>
    /// The first version of this case asserted only the row COUNT, and removing the WHERE from the
    /// page left it green: ten unfiltered rows cut to a page of five look exactly like six filtered
    /// ones cut to a page of five. What tells them apart is the VALUES.
    /// </summary>
    [Test]
    public async Task AFilteredPageIsFilteredAndPagedAtOnceTest()
    {
        var condition = GridFilter.Parse("ship", Column("Status"), 0)!;
        var view = new GridView("Orders", [condition], null, false, "Id", 0, 4, null);

        var query = GridQuery.Page(view);
        var result = await m_fixture.Database.ExecuteQueryAsync(query.Statement);

        Assert.That(result.ErrorMessage, Is.Null, query.Statement.Text);

        // Six match; a page of four is asked for as five, so five come back and there is a next page.
        Assert.That(result.Data!.Rows.Count, Is.EqualTo(5));

        var statuses = result.Data.Rows.Cast<DataRow>().Select(row => (string)row["Status"]).ToList();

        Assert.That(statuses.All(status => status.Contains("hip", StringComparison.OrdinalIgnoreCase)),
            Is.True, "every row on the page has to match the filter: " + string.Join(", ", statuses));
    }

    [Test]
    public async Task SortingIsDoneByTheEngineAndNotByTheGridTest()
    {
        var view = new GridView("Orders", [], "Total", true, "Id", 0, 3, null);
        var result = await m_fixture.Database.ExecuteQueryAsync(GridQuery.Page(view).Statement);

        var totals = result.Data!.Rows.Cast<DataRow>()
            .Take(3)
            .Select(row => System.Convert.ToDecimal(row["Total"]))
            .ToList();

        // The three LARGEST of the ten, which a client sorting the page it was given could not know.
        Assert.That(totals, Is.EqualTo(new[] { 109m, 108m, 107m }));
    }

    #endregion

    #region Show SQL

    /// <summary>
    /// WS-32. What is shown has to be what was sent, so it comes from the same builder - and it has
    /// to run: a query a user cannot execute explains nothing.
    /// </summary>
    [Test]
    public async Task TheSqlShownIsTheSqlThatRunsTest()
    {
        var condition = GridFilter.Parse("> 105", Column("Total"), 0)!;
        var view = new GridView("Orders", [condition], "Total", true, "Id", 0, 100, null);

        var shown = GridQuery.Whole(view);

        Assert.That(shown.Text, Does.Contain("FROM [Orders]"));
        Assert.That(shown.Text, Does.Contain("WHERE"));
        Assert.That(shown.Text, Does.Contain("ORDER BY [Total] DESC"));
        Assert.That(shown.Text, Does.Not.Contain("LIMIT"), "the view without its page");

        var result = await m_fixture.Database.ExecuteQueryAsync(shown);

        Assert.That(result.ErrorMessage, Is.Null, shown.Text);
        Assert.That(result.Data?.Rows.Count, Is.EqualTo(4));
    }

    [Test]
    public void TheDescriptionSaysWhatTheViewIsDoingTest()
    {
        var condition = GridFilter.Parse("> 105", Column("Total"), 0)!;
        var view = new GridView("Orders", [condition], "Total", true, "Id", 0, 100, null);

        var described = GridQuery.Page(view).Description;

        Assert.That(described, Does.Contain("1 filter"));
        Assert.That(described, Does.Contain("Total > 105"));
        Assert.That(described, Does.Contain("sorted by Total descending"));
    }

    #endregion

    #region Counting

    /// <summary>
    /// The total is asked for, never assumed (4.2). Both shapes are checked against a scan, because on
    /// this engine an unfiltered count is a counter kept beside the data and not the data itself.
    /// </summary>
    [Test]
    public async Task TheTotalIsAskedForAndAgreesWithTheRowsTest()
    {
        var all = new GridView("Orders", [], null, false, "Id", 0, 100, null);

        var counted = await m_fixture.Database.ExecuteScalarAsync(GridQuery.Count(all).Text);

        Assert.That(System.Convert.ToInt64(counted), Is.EqualTo(10));
        Assert.That(await m_fixture.CountRowsAsync("Orders"), Is.EqualTo(10),
            "the control: the same number by scanning the rows");

        var condition = GridFilter.Parse("ship", Column("Status"), 0)!;
        var filtered = new GridView("Orders", [condition], null, false, "Id", 0, 100, null);
        var result = await m_fixture.Database.ExecuteQueryAsync(GridQuery.Count(filtered));

        Assert.That(System.Convert.ToInt64(result.Data!.Rows[0][0]), Is.EqualTo(6));
    }

    #endregion
}
