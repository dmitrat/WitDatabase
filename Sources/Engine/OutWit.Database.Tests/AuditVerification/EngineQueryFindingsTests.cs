using System.Globalization;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Verification of the seven unverified <c>engine-query</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// Every test asserts the <b>correct</b> behaviour, so a failing test confirms the finding it is
/// named after and a passing one refutes it. Run 2026-07-27 against <c>main</c> at a668f73.
///
/// Six of the seven findings are confirmed and carry <c>[Ignore]</c> with the behaviour that was
/// actually observed - the convention this repository already uses for
/// <c>WitSqlEnginePrecedenceTests</c>'s BETWEEN tests. Remove the attribute when the defect is
/// fixed. The seventh is <b>latent</b>: the wrong code exists but no shipped path reaches it, so
/// its tests stay green and serve as the pins that would catch it becoming reachable.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class EngineQueryFindingsTests : WitSqlEngineTestsBase
{
    #region LIMIT is applied before DISTINCT

    [Test]
    public void DistinctWithLimitReturnsNDistinctRowsTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Category VARCHAR(10))");
        m_engine.Execute("INSERT INTO T (Id, Category) VALUES (1, 'a')");
        m_engine.Execute("INSERT INTO T (Id, Category) VALUES (2, 'a')");
        m_engine.Execute("INSERT INTO T (Id, Category) VALUES (3, 'a')");
        m_engine.Execute("INSERT INTO T (Id, Category) VALUES (4, 'b')");
        m_engine.Execute("INSERT INTO T (Id, Category) VALUES (5, 'c')");
        m_engine.Execute("INSERT INTO T (Id, Category) VALUES (6, 'd')");

        var rows = m_engine.Query("SELECT DISTINCT Category FROM T LIMIT 3");

        // Four distinct categories exist, so a LIMIT of 3 must yield exactly 3 of them.
        Assert.That(rows, Has.Count.EqualTo(3),
            "DISTINCT must be applied before LIMIT: the first three physical rows are all 'a'");
    }

    #endregion

    #region Default window frame

    [Test]
    public void WindowWithOrderByDefaultsToRunningTotalTest()
    {
        CreateSales();

        var rows = m_engine.Query(@"
            SELECT Id, SUM(Amount) OVER (PARTITION BY Product ORDER BY Id) AS RunningTotal
            FROM Sales WHERE Product = 'A' ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0]["RunningTotal"].AsDecimal(), Is.EqualTo(100m));
            Assert.That(rows[1]["RunningTotal"].AsDecimal(), Is.EqualTo(250m));
            Assert.That(rows[2]["RunningTotal"].AsDecimal(), Is.EqualTo(450m));
        });
    }

    [Test]
    [Ignore("KNOWN GAP, narrower than the defect it came from. The default frame is now correctly " +
            "UNBOUNDED PRECEDING .. CURRENT ROW, but it is typed RANGE and " +
            "CalculateFrameBoundIndex maps CURRENT ROW to the current index whatever the frame " +
            "type, so peers - rows with equal ORDER BY values - are not grouped as RANGE requires. " +
            "Under RANGE every peer shares the frame that ends at the LAST peer, so all three rows " +
            "below should read 450. Affects ties only. engine-query, " +
            "Iterators/IteratorWindow.Frame.cs")]
    public void WindowRangeFrameGroupsPeersTest()
    {
        // Three rows with the SAME ordering key, so they are peers of one another.
        m_engine.Execute(@"
            CREATE TABLE Peers (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Bucket INT NOT NULL,
                Amount DECIMAL NOT NULL)");
        m_engine.Execute("INSERT INTO Peers (Bucket, Amount) VALUES (1, 100)");
        m_engine.Execute("INSERT INTO Peers (Bucket, Amount) VALUES (1, 150)");
        m_engine.Execute("INSERT INTO Peers (Bucket, Amount) VALUES (1, 200)");

        var totals = m_engine.Query(
                "SELECT SUM(Amount) OVER (ORDER BY Bucket) AS T FROM Peers")
            .Select(r => r["T"].AsDecimal())
            .ToArray();

        Assert.That(totals, Is.EqualTo(new[] { 450m, 450m, 450m }),
            "under RANGE, peers share a frame that ends at the last peer");
    }

    [Test]
    public void WindowWithoutOrderByStaysWholePartitionTest()
    {
        // Passes. Pins what must NOT change: with no ORDER BY the frame really is the whole
        // partition, so the fix above must not turn every window into a running total.
        CreateSales();

        var rows = m_engine.Query(@"
            SELECT Id, SUM(Amount) OVER (PARTITION BY Product) AS Total
            FROM Sales WHERE Product = 'A' ORDER BY Id");

        Assert.That(rows.Select(r => r["Total"].AsDecimal()),
            Is.EqualTo(new[] { 450m, 450m, 450m }));
    }

    #endregion

    #region ORDER BY ... NULLS FIRST / NULLS LAST

    [Test]
    public void OrderByNullsFirstPutsNullsFirstTest()
    {
        // Passes, but only by coincidence: NULLs sort first by default, so the clause happens to
        // agree with the behaviour it is being ignored in favour of. Kept as the pin for the other
        // half of the fix - NULLS FIRST must still work once NULLS LAST starts working.
        CreateNullable();

        var ids = m_engine.Query("SELECT Id FROM N ORDER BY Value NULLS FIRST")
            .Select(r => r[0].AsInt64()).ToArray();

        Assert.That(ids.Take(2), Is.EquivalentTo(new long[] { 2, 4 }),
            "rows 2 and 4 hold NULL and must sort ahead of every non-NULL value");
    }

    [Test]
    public void OrderByNullsLastPutsNullsLastTest()
    {
        CreateNullable();

        var ids = m_engine.Query("SELECT Id FROM N ORDER BY Value NULLS LAST")
            .Select(r => r[0].AsInt64()).ToArray();

        Assert.That(ids.Skip(2), Is.EquivalentTo(new long[] { 2, 4 }),
            "rows 2 and 4 hold NULL and must sort behind every non-NULL value");
    }

    [Test]
    public void OrderByNullsLastIsIndependentOfDescendingTest()
    {
        // NULLS FIRST/LAST is orthogonal to ASC/DESC in SQL: reversing the direction must not
        // reverse where the NULLs go. This pins the half of the fix that is easy to get wrong by
        // resolving the null order after the direction has already been applied.
        CreateNullable();

        var ids = m_engine.Query("SELECT Id FROM N ORDER BY Value DESC NULLS LAST")
            .Select(r => r[0].AsInt64()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ids.Take(2), Is.EqualTo(new long[] { 3, 1 }),
                "DESC still orders the non-NULL values 30 then 10");
            Assert.That(ids.Skip(2), Is.EquivalentTo(new long[] { 2, 4 }),
                "NULLS LAST still puts the NULLs at the end");
        });
    }

    #endregion

    #region LIKE regex flags

    [Test]
    public void LikePercentCrossesANewlineTest()
    {
        CreateText("a\nb");

        var rows = m_engine.Query("SELECT Id FROM S WHERE Txt LIKE 'a%b'");

        Assert.That(rows, Has.Count.EqualTo(1), "% must match any sequence, newlines included");
    }

    [Test]
    public void LikeUnderscoreCrossesANewlineTest()
    {
        CreateText("a\nb");

        var rows = m_engine.Query("SELECT Id FROM S WHERE Txt LIKE 'a_b'");

        Assert.That(rows, Has.Count.EqualTo(1), "_ must match any single character, newline included");
    }

    [Test]
    public void LikeDoesNotTolerateATrailingNewlineTest()
    {
        CreateText("abc\n");

        var rows = m_engine.Query("SELECT Id FROM S WHERE Txt LIKE 'abc'");

        Assert.That(rows, Is.Empty, "'abc\\n' is not equal to 'abc', so LIKE 'abc' must not match it");
    }

    [Test]
    public void LikeIsNotCultureSensitiveTest()
    {
        CreateText("I");

        var invariant = QueryCount("SELECT Id FROM S WHERE Txt LIKE 'i'");

        var previous = CultureInfo.CurrentCulture;
        int turkish;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            turkish = QueryCount("SELECT Id FROM S WHERE Txt LIKE 'i'");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.That(turkish, Is.EqualTo(invariant),
            "LIKE must give the same answer regardless of the ambient culture");
    }

    #endregion

    #region NULL propagation in scalar functions

    // FIXED: all eleven propagate NULL. The suppression reason that used to live here was a FOSSIL -
    // the [TestCase]s stopped naming it when they were un-ignored, so the constant sat unreferenced
    // while its text still read "CONFIRMED 2026-07-27: returns a zero-value instead of NULL". Found by
    // the 2026-08-10 ledger census.

    [TestCase("LENGTH(NULL)")]
    [TestCase("UPPER(NULL)")]
    [TestCase("LOWER(NULL)")]
    [TestCase("TRIM(NULL)")]
    [TestCase("ROUND(NULL)")]
    [TestCase("YEAR(NULL)")]
    [TestCase("MONTH(NULL)")]
    [TestCase("SUBSTR(NULL, 1, 2)")]
    [TestCase("REPLACE(NULL, 'a', 'b')")]
    [TestCase("ABS(NULL)")]
    [TestCase("NULL || 'x'")]
    public void ScalarFunctionPropagatesNullTest(string expression)
    {
        m_engine.Execute("CREATE TABLE One (Id INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO One (Id) VALUES (1)");

        var value = m_engine.Query($"SELECT {expression} AS V FROM One")[0]["V"];

        Assert.That(value.IsNull, Is.True, $"{expression} must be NULL, was '{value.AsString()}'");
    }

    #endregion

    #region Equals / GetHashCode across numeric types

    [Test]
    [Ignore("CONFIRMED 2026-07-27: Equals returns true for Integer/Decimal, Integer/Real and " +
            "Real/Decimal while all three hash codes differ, so every hash-based operator " +
            "disagrees with `=`. engine-query, Values/WitSqlValue.Comparison.cs:68")]
    public void EqualValuesHaveEqualHashCodesTest()
    {
        var pairs = new (WitSqlValue Left, WitSqlValue Right, string Label)[]
        {
            (WitSqlValue.FromInt(1), WitSqlValue.FromDecimal(1m), "Integer/Decimal"),
            (WitSqlValue.FromInt(1), WitSqlValue.FromReal(1.0), "Integer/Real"),
            (WitSqlValue.FromReal(1.0), WitSqlValue.FromDecimal(1m), "Real/Decimal"),
        };

        Assert.Multiple(() =>
        {
            foreach (var (left, right, label) in pairs)
            {
                if (!left.Equals(right))
                    continue;

                Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()),
                    $"{label}: Equals returned true, so the hash codes must match");
            }
        });
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the UNION returns 2 rows although `1 = 1.0` evaluates to true. " +
            "This is the user-visible half of the Equals/GetHashCode defect above.")]
    public void DistinctAgreesWithEqualityAcrossNumericTypesTest()
    {
        m_engine.Execute("CREATE TABLE One (Id INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO One (Id) VALUES (1)");

        var equal = m_engine.Query("SELECT 1 AS Same FROM One WHERE 1 = 1.0");
        if (equal.Count == 0)
            Assert.Ignore("The engine does not consider 1 and 1.0 equal, so there is nothing to reconcile");

        var rows = m_engine.Query("SELECT 1 AS V FROM One UNION SELECT 1.0 AS V FROM One");

        Assert.That(rows, Has.Count.EqualTo(1),
            "`1 = 1.0` is true, so UNION must collapse them into one row");
    }

    #endregion

    #region Index selection ignores the table qualifier - LATENT

    // NOT REPRODUCIBLE 2026-07-27, and the reason is worth recording. OptimizerQuery's
    // FindMatchingPredicate (line 272) really does compare pred.ColumnName alone and never consults
    // the PredicateInfo.TableAlias it captured, so the defective code is exactly as described. But
    // no shipped path delivers a foreign qualifier to it:
    //
    //   - QueryPlanner declares an OptimizerQuery field and never calls it, so SELECT never reaches
    //     this optimizer at all;
    //   - the only callers are UPDATE and DELETE, and both bypass it the moment a second table
    //     appears (CreateUpdateIteratorWithFrom / CreateDeleteIteratorWithUsing);
    //   - ExtractPredicatesRecursive does not descend into subqueries, so an inner predicate cannot
    //     leak out either;
    //   - and DmlOptimizer only consults it past MIN_ROWS_FOR_OPTIMIZATION = 50 rows.
    //
    // So this is latent, like the B+Tree split the audit reclassified. All four probes below seed
    // past the 50-row floor so the optimizer is genuinely engaged, and all four pass. They stay
    // active: they are what would catch the defect the day one of those paths starts feeding it a
    // qualified predicate. engine-query, Optimizers/OptimizerQuery.cs:272

    [Test]
    public void DeleteUsingIsNotDrivenByTheJoinedTablesPredicateTest()
    {
        SeedTwoTables();

        m_engine.Execute("DELETE FROM A USING B WHERE A.Id = B.AId AND B.Value = 99");

        Assert.That(CountA(), Is.EqualTo(0),
            "every row of B carries Value = 99 and joins to an A row, so all of A must be deleted");
    }

    [Test]
    public void UpdateFromIsNotDrivenByTheJoinedTablesPredicateTest()
    {
        SeedTwoTables();

        m_engine.Execute("UPDATE A SET Value = 7 FROM B WHERE A.Id = B.AId AND B.Value = 99");

        Assert.That(CountA("WHERE Value = 7"), Is.EqualTo(60),
            "the predicate belongs to B, so it must not be matched against the index over A.Value");
    }

    [Test]
    public void DeleteWithASubqueryPredicateIsNotDrivenByTheInnerTableTest()
    {
        SeedTwoTables();

        m_engine.Execute("DELETE FROM A WHERE Id IN (SELECT AId FROM B WHERE Value = 99)");

        Assert.That(CountA(), Is.EqualTo(0),
            "the inner `Value = 99` belongs to B and must not drive a seek on the index over A.Value");
    }

    [Test]
    public void SelectIsNotDrivenByAnotherTablesPredicateTest()
    {
        SeedTwoTables();

        var count = m_engine.Query(
            "SELECT A.Id FROM A JOIN B ON A.Id = B.AId WHERE B.Value = 99").Count;

        Assert.That(count, Is.EqualTo(60),
            "no row of A has Value = 99, so a qualifier-blind index seek would return nothing");
    }

    #endregion

    #region Helpers

    private void CreateSales()
    {
        m_engine.Execute(@"
            CREATE TABLE Sales (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Product TEXT NOT NULL,
                Amount DECIMAL NOT NULL)");
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('A', 100)");
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('A', 150)");
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('A', 200)");
    }

    private void CreateNullable()
    {
        m_engine.Execute("CREATE TABLE N (Id INT PRIMARY KEY, Value INT)");
        m_engine.Execute("INSERT INTO N (Id, Value) VALUES (1, 10)");
        m_engine.Execute("INSERT INTO N (Id, Value) VALUES (2, NULL)");
        m_engine.Execute("INSERT INTO N (Id, Value) VALUES (3, 30)");
        m_engine.Execute("INSERT INTO N (Id, Value) VALUES (4, NULL)");
    }

    private void CreateText(string text)
    {
        m_engine.Execute("CREATE TABLE S (Id INT PRIMARY KEY, Txt VARCHAR(50))");
        m_engine.Execute($"INSERT INTO S (Id, Txt) VALUES (1, '{text}')");
    }

    private int QueryCount(string sql) => m_engine.Query(sql).Count;

    /// <summary>
    /// Two joined tables, both seeded past DmlOptimizer's 50-row floor so the index optimizer is
    /// actually engaged. No row of A holds Value = 99; every row of B does.
    /// </summary>
    private void SeedTwoTables()
    {
        m_engine.Execute("CREATE TABLE A (Id INT PRIMARY KEY, Value INT)");
        m_engine.Execute("CREATE TABLE B (Id INT PRIMARY KEY, AId INT, Value INT)");
        m_engine.Execute("CREATE INDEX IxAValue ON A (Value)");
        m_engine.Execute("CREATE INDEX IxBValue ON B (Value)");

        for (int i = 1; i <= 60; i++)
        {
            m_engine.Execute($"INSERT INTO A (Id, Value) VALUES ({i}, {i})");
            m_engine.Execute($"INSERT INTO B (Id, AId, Value) VALUES ({i}, {i}, 99)");
        }
    }

    private int CountA(string where = "") =>
        (int)m_engine.Query($"SELECT COUNT(*) FROM A {where}")[0][0].AsInt64();

    #endregion
}
