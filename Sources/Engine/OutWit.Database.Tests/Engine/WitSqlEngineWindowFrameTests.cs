namespace OutWit.Database.Tests;

/// <summary>
/// Tests for Window Frame Clause (ROWS/RANGE BETWEEN).
/// </summary>
[TestFixture]
public sealed class WitSqlEngineWindowFrameTests : WitSqlEngineTestsBase
{
    #region Setup

    private void CreateSalesTable()
    {
        m_engine.Execute(@"
            CREATE TABLE Sales (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Product TEXT NOT NULL,
                Amount DECIMAL NOT NULL
            )");
    }

    private void InsertSalesData()
    {
        // Insert in specific order so Id = SaleOrder
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('A', 100)");  // Id 1
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('A', 150)");  // Id 2
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('A', 200)");  // Id 3
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('A', 120)");  // Id 4
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('A', 180)");  // Id 5
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('B', 50)");   // Id 6
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('B', 75)");   // Id 7
        m_engine.Execute("INSERT INTO Sales (Product, Amount) VALUES ('B', 60)");   // Id 8
    }

    #endregion

    #region ROWS BETWEEN Tests

    [Test]
    public void RowsBetweenUnboundedPrecedingAndCurrentRowTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Product,
                Amount,
                SUM(Amount) OVER (
                    PARTITION BY Product 
                    ORDER BY Id 
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                ) AS RunningTotal
            FROM Sales
            ORDER BY Product, Id");

        Assert.That(rows, Has.Count.EqualTo(8));
        
        // Product A: cumulative sums
        Assert.That(rows[0]["RunningTotal"].AsDecimal(), Is.EqualTo(100)); // 100
        Assert.That(rows[1]["RunningTotal"].AsDecimal(), Is.EqualTo(250)); // 100+150
        Assert.That(rows[2]["RunningTotal"].AsDecimal(), Is.EqualTo(450)); // 100+150+200
        Assert.That(rows[3]["RunningTotal"].AsDecimal(), Is.EqualTo(570)); // 100+150+200+120
        Assert.That(rows[4]["RunningTotal"].AsDecimal(), Is.EqualTo(750)); // 100+150+200+120+180
        
        // Product B: cumulative sums
        Assert.That(rows[5]["RunningTotal"].AsDecimal(), Is.EqualTo(50));  // 50
        Assert.That(rows[6]["RunningTotal"].AsDecimal(), Is.EqualTo(125)); // 50+75
        Assert.That(rows[7]["RunningTotal"].AsDecimal(), Is.EqualTo(185)); // 50+75+60
    }

    [Test]
    public void RowsBetween1PrecedingAnd1FollowingTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Product,
                Amount,
                AVG(Amount) OVER (
                    PARTITION BY Product 
                    ORDER BY Id 
                    ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING
                ) AS MovingAvg
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Row 0: only has current + 1 following = (100+150)/2 = 125
        Assert.That(rows[0]["MovingAvg"].AsDouble(), Is.EqualTo(125.0).Within(0.01));
        
        // Row 1: 1 preceding + current + 1 following = (100+150+200)/3 = 150
        Assert.That(rows[1]["MovingAvg"].AsDouble(), Is.EqualTo(150.0).Within(0.01));
        
        // Row 2: (150+200+120)/3 = 156.67
        Assert.That(rows[2]["MovingAvg"].AsDouble(), Is.EqualTo(156.67).Within(0.01));
        
        // Row 3: (200+120+180)/3 = 166.67
        Assert.That(rows[3]["MovingAvg"].AsDouble(), Is.EqualTo(166.67).Within(0.01));
        
        // Row 4: only 1 preceding + current = (120+180)/2 = 150
        Assert.That(rows[4]["MovingAvg"].AsDouble(), Is.EqualTo(150.0).Within(0.01));
    }

    [Test]
    public void RowsBetweenCurrentRowAndUnboundedFollowingTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Product,
                Amount,
                COUNT(*) OVER (
                    PARTITION BY Product 
                    ORDER BY Id 
                    ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING
                ) AS RemainingRows
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Row 0: current + all following = 5
        Assert.That(rows[0]["RemainingRows"].AsInt64(), Is.EqualTo(5));
        
        // Row 1: 4 rows remaining
        Assert.That(rows[1]["RemainingRows"].AsInt64(), Is.EqualTo(4));
        
        // Row 4: only current = 1
        Assert.That(rows[4]["RemainingRows"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void RowsBetween2PrecedingAndCurrentRowTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Amount,
                SUM(Amount) OVER (
                    ORDER BY Id 
                    ROWS BETWEEN 2 PRECEDING AND CURRENT ROW
                ) AS Rolling3Sum
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Row 0: only current = 100
        Assert.That(rows[0]["Rolling3Sum"].AsDecimal(), Is.EqualTo(100));
        
        // Row 1: 1 preceding + current = 100+150 = 250
        Assert.That(rows[1]["Rolling3Sum"].AsDecimal(), Is.EqualTo(250));
        
        // Row 2: 2 preceding + current = 100+150+200 = 450
        Assert.That(rows[2]["Rolling3Sum"].AsDecimal(), Is.EqualTo(450));
        
        // Row 3: 2 preceding + current = 150+200+120 = 470
        Assert.That(rows[3]["Rolling3Sum"].AsDecimal(), Is.EqualTo(470));
        
        // Row 4: 2 preceding + current = 200+120+180 = 500
        Assert.That(rows[4]["Rolling3Sum"].AsDecimal(), Is.EqualTo(500));
    }

    #endregion

    #region MIN/MAX with Frame Tests

    [Test]
    public void MinOverFrameTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Amount,
                MIN(Amount) OVER (
                    ORDER BY Id 
                    ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING
                ) AS MinInWindow
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Row 0: min of [100, 150] = 100
        Assert.That(rows[0]["MinInWindow"].AsDecimal(), Is.EqualTo(100));
        
        // Row 1: min of [100, 150, 200] = 100
        Assert.That(rows[1]["MinInWindow"].AsDecimal(), Is.EqualTo(100));
        
        // Row 2: min of [150, 200, 120] = 120
        Assert.That(rows[2]["MinInWindow"].AsDecimal(), Is.EqualTo(120));
        
        // Row 3: min of [200, 120, 180] = 120
        Assert.That(rows[3]["MinInWindow"].AsDecimal(), Is.EqualTo(120));
        
        // Row 4: min of [120, 180] = 120
        Assert.That(rows[4]["MinInWindow"].AsDecimal(), Is.EqualTo(120));
    }

    [Test]
    public void MaxOverFrameTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Amount,
                MAX(Amount) OVER (
                    ORDER BY Id 
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                ) AS MaxSoFar
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Running max
        Assert.That(rows[0]["MaxSoFar"].AsDecimal(), Is.EqualTo(100));
        Assert.That(rows[1]["MaxSoFar"].AsDecimal(), Is.EqualTo(150));
        Assert.That(rows[2]["MaxSoFar"].AsDecimal(), Is.EqualTo(200));
        Assert.That(rows[3]["MaxSoFar"].AsDecimal(), Is.EqualTo(200)); // still 200
        Assert.That(rows[4]["MaxSoFar"].AsDecimal(), Is.EqualTo(200)); // still 200
    }

    #endregion

    #region FIRST_VALUE/LAST_VALUE with Frame Tests

    [Test]
    public void FirstValueWithFrameTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Amount,
                FIRST_VALUE(Amount) OVER (
                    ORDER BY Id 
                    ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING
                ) AS FirstInWindow
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Row 0: first of [100, 150] = 100
        Assert.That(rows[0]["FirstInWindow"].AsDecimal(), Is.EqualTo(100));
        
        // Row 1: first of [100, 150, 200] = 100
        Assert.That(rows[1]["FirstInWindow"].AsDecimal(), Is.EqualTo(100));
        
        // Row 2: first of [150, 200, 120] = 150
        Assert.That(rows[2]["FirstInWindow"].AsDecimal(), Is.EqualTo(150));
    }

    [Test]
    public void LastValueWithFrameTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Amount,
                LAST_VALUE(Amount) OVER (
                    ORDER BY Id 
                    ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING
                ) AS LastInPartition
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // All rows should have the same last value (180)
        foreach (var row in rows)
        {
            Assert.That(row["LastInPartition"].AsDecimal(), Is.EqualTo(180));
        }
    }

    [Test]
    public void LastValueWithCurrentRowFrameTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Amount,
                LAST_VALUE(Amount) OVER (
                    ORDER BY Id 
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                ) AS LastSoFar
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Last value should be the current row's amount
        Assert.That(rows[0]["LastSoFar"].AsDecimal(), Is.EqualTo(100));
        Assert.That(rows[1]["LastSoFar"].AsDecimal(), Is.EqualTo(150));
        Assert.That(rows[2]["LastSoFar"].AsDecimal(), Is.EqualTo(200));
        Assert.That(rows[3]["LastSoFar"].AsDecimal(), Is.EqualTo(120));
        Assert.That(rows[4]["LastSoFar"].AsDecimal(), Is.EqualTo(180));
    }

    #endregion

    #region NTH_VALUE with Frame Tests

    [Test]
    public void NthValueWithFrameTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Amount,
                NTH_VALUE(Amount, 2) OVER (
                    ORDER BY Id 
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                ) AS SecondValue
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Row 0: frame has only 1 row, so NTH_VALUE(2) is NULL
        Assert.That(rows[0]["SecondValue"].IsNull, Is.True);
        
        // Row 1+: frame has at least 2 rows, second value is 150
        Assert.That(rows[1]["SecondValue"].AsDecimal(), Is.EqualTo(150));
        Assert.That(rows[2]["SecondValue"].AsDecimal(), Is.EqualTo(150));
        Assert.That(rows[3]["SecondValue"].AsDecimal(), Is.EqualTo(150));
        Assert.That(rows[4]["SecondValue"].AsDecimal(), Is.EqualTo(150));
    }

    #endregion

    #region No Frame Clause Tests (Default Behavior)

    [Test]
    public void AggregateWithoutFrameUsesWholePartitionTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Product,
                Amount,
                SUM(Amount) OVER (PARTITION BY Product) AS TotalForProduct
            FROM Sales
            ORDER BY Product, Id");

        Assert.That(rows, Has.Count.EqualTo(8));
        
        // Product A total: 100+150+200+120+180 = 750
        for (int i = 0; i < 5; i++)
        {
            Assert.That(rows[i]["TotalForProduct"].AsDecimal(), Is.EqualTo(750));
        }
        
        // Product B total: 50+75+60 = 185
        for (int i = 5; i < 8; i++)
        {
            Assert.That(rows[i]["TotalForProduct"].AsDecimal(), Is.EqualTo(185));
        }
    }

    #endregion

    #region Complex Frame Tests

    [Test]
    public void MultipleWindowFunctionsWithDifferentFramesTest()
    {
        CreateSalesTable();
        InsertSalesData();

        var rows = m_engine.Query(@"
            SELECT 
                Id,
                Amount,
                SUM(Amount) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunningSum,
                AVG(Amount) OVER (ORDER BY Id ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) AS Moving3Avg,
                COUNT(*) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) AS WindowSize
            FROM Sales
            WHERE Product = 'A'
            ORDER BY Id");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // Verify all window functions work correctly
        Assert.That(rows[0]["RunningSum"].AsDecimal(), Is.EqualTo(100));
        Assert.That(rows[0]["Moving3Avg"].AsDouble(), Is.EqualTo(100.0).Within(0.01));
        Assert.That(rows[0]["WindowSize"].AsInt64(), Is.EqualTo(2)); // current + 1 following
        
        Assert.That(rows[2]["RunningSum"].AsDecimal(), Is.EqualTo(450));
        Assert.That(rows[2]["Moving3Avg"].AsDouble(), Is.EqualTo(150.0).Within(0.01)); // (100+150+200)/3
        Assert.That(rows[2]["WindowSize"].AsInt64(), Is.EqualTo(3)); // 1 preceding + current + 1 following
    }

    #endregion
}
