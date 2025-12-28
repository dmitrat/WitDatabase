namespace OutWit.Database.Tests;

/// <summary>
/// Tests for window function execution in the SQL engine.
/// Covers: ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK, CUME_DIST,
/// FIRST_VALUE, LAST_VALUE, NTH_VALUE, LAG, LEAD, and aggregate window functions.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineWindowFunctionTests : WitSqlEngineTestsBase
{
    #region Setup

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        CreateEmployeesTable();
    }

    private void CreateEmployeesTable()
    {
        m_engine.Execute(@"
            CREATE TABLE Employees (
                Id INT PRIMARY KEY,
                Name VARCHAR(100),
                Department VARCHAR(50),
                Salary DECIMAL,
                HireDate DATE
            )");

        // Insert test data
        m_engine.Execute(@"
            INSERT INTO Employees (Id, Name, Department, Salary, HireDate) VALUES
            (1, 'Alice', 'Engineering', 80000, '2020-01-15'),
            (2, 'Bob', 'Engineering', 75000, '2019-06-01'),
            (3, 'Carol', 'Engineering', 85000, '2021-03-20'),
            (4, 'David', 'Sales', 60000, '2018-11-10'),
            (5, 'Eve', 'Sales', 65000, '2020-05-25'),
            (6, 'Frank', 'Sales', 60000, '2022-02-14'),
            (7, 'Grace', 'HR', 55000, '2019-09-01'),
            (8, 'Henry', 'HR', 58000, '2021-07-18')");
    }

    #endregion

    #region ROW_NUMBER Tests

    [Test]
    public void RowNumber_SimpleOrderBy_ReturnsSequentialNumbers()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary, ROW_NUMBER() OVER (ORDER BY Salary DESC) AS RowNum
            FROM Employees
            ORDER BY RowNum");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Check that row numbers are sequential 1-8
        for (int i = 0; i < 8; i++)
        {
            Assert.That(rows[i]["RowNum"].AsInt64(), Is.EqualTo(i + 1));
        }

        // Verify that Carol (highest salary 85000) has RowNum=1
        var carol = rows.First(r => r["Name"].AsString() == "Carol");
        Assert.That(carol["RowNum"].AsInt64(), Is.EqualTo(1));
        
        // Verify that row with RowNum=1 has highest salary
        var row1 = rows.First(r => r["RowNum"].AsInt64() == 1);
        Assert.That(row1["Salary"].AsDecimal(), Is.EqualTo(85000m));
    }

    [Test]
    public void RowNumber_WithPartitionBy_ResetsPerPartition()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   ROW_NUMBER() OVER (PARTITION BY Department ORDER BY Salary DESC) AS DeptRank
            FROM Employees
            ORDER BY Department, DeptRank");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Check Engineering department ranks
        var engineering = rows.Where(r => r["Department"].AsString() == "Engineering").ToList();
        Assert.That(engineering[0]["DeptRank"].AsInt64(), Is.EqualTo(1));
        Assert.That(engineering[1]["DeptRank"].AsInt64(), Is.EqualTo(2));
        Assert.That(engineering[2]["DeptRank"].AsInt64(), Is.EqualTo(3));

        // Check Sales department ranks
        var sales = rows.Where(r => r["Department"].AsString() == "Sales").ToList();
        Assert.That(sales[0]["DeptRank"].AsInt64(), Is.EqualTo(1));
        Assert.That(sales[1]["DeptRank"].AsInt64(), Is.EqualTo(2));
        Assert.That(sales[2]["DeptRank"].AsInt64(), Is.EqualTo(3));

        // Check HR department ranks
        var hr = rows.Where(r => r["Department"].AsString() == "HR").ToList();
        Assert.That(hr[0]["DeptRank"].AsInt64(), Is.EqualTo(1));
        Assert.That(hr[1]["DeptRank"].AsInt64(), Is.EqualTo(2));
    }

    #endregion

    #region RANK Tests

    [Test]
    public void Rank_WithTies_SkipsRanks()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   RANK() OVER (ORDER BY Salary DESC) AS SalaryRank
            FROM Employees");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Carol (85000) should have rank 1
        var carol = rows.First(r => r["Name"].AsString() == "Carol");
        Assert.That(carol["SalaryRank"].AsInt64(), Is.EqualTo(1));

        // David and Frank both have 60000 - should have same rank
        var david = rows.First(r => r["Name"].AsString() == "David");
        var frank = rows.First(r => r["Name"].AsString() == "Frank");
        Assert.That(david["SalaryRank"].AsInt64(), Is.EqualTo(frank["SalaryRank"].AsInt64()));
    }

    [Test]
    public void Rank_WithPartition_RanksWithinPartition()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   RANK() OVER (PARTITION BY Department ORDER BY Salary DESC) AS DeptRank
            FROM Employees");

        // Within Sales: Eve (65000) should have rank 1
        var eve = rows.First(r => r["Name"].AsString() == "Eve");
        Assert.That(eve["DeptRank"].AsInt64(), Is.EqualTo(1));
        
        // David and Frank (60000) should both have rank 2
        var david = rows.First(r => r["Name"].AsString() == "David");
        var frank = rows.First(r => r["Name"].AsString() == "Frank");
        Assert.That(david["DeptRank"].AsInt64(), Is.EqualTo(2));
        Assert.That(frank["DeptRank"].AsInt64(), Is.EqualTo(2));
    }

    #endregion

    #region DENSE_RANK Tests

    [Test]
    public void DenseRank_WithTies_NoGaps()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary,
                   DENSE_RANK() OVER (ORDER BY Salary DESC) AS DenseRank
            FROM Employees");

        Assert.That(rows, Has.Count.EqualTo(8));

        // David and Frank (60000) have same rank but no gap after
        var david = rows.First(r => r["Name"].AsString() == "David");
        var frank = rows.First(r => r["Name"].AsString() == "Frank");
        Assert.That(david["DenseRank"].AsInt64(), Is.EqualTo(frank["DenseRank"].AsInt64()));

        // Henry (58000) should have the next consecutive rank after David/Frank
        var henry = rows.First(r => r["Name"].AsString() == "Henry");
        Assert.That(henry["DenseRank"].AsInt64(), Is.EqualTo(david["DenseRank"].AsInt64() + 1));
        
        // Grace (55000) should be after Henry
        var grace = rows.First(r => r["Name"].AsString() == "Grace");
        Assert.That(grace["DenseRank"].AsInt64(), Is.EqualTo(henry["DenseRank"].AsInt64() + 1));
    }

    #endregion

    #region NTILE Tests

    [Test]
    public void Ntile_DistributesRowsIntoBuckets()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary,
                   NTILE(4) OVER (ORDER BY Salary DESC) AS Quartile
            FROM Employees");

        Assert.That(rows, Has.Count.EqualTo(8));

        // With 8 rows and 4 buckets, each bucket should have 2 rows
        var quartile1 = rows.Where(r => r["Quartile"].AsInt64() == 1).ToList();
        var quartile2 = rows.Where(r => r["Quartile"].AsInt64() == 2).ToList();
        var quartile3 = rows.Where(r => r["Quartile"].AsInt64() == 3).ToList();
        var quartile4 = rows.Where(r => r["Quartile"].AsInt64() == 4).ToList();

        Assert.That(quartile1, Has.Count.EqualTo(2));
        Assert.That(quartile2, Has.Count.EqualTo(2));
        Assert.That(quartile3, Has.Count.EqualTo(2));
        Assert.That(quartile4, Has.Count.EqualTo(2));
    }

    #endregion

    #region LAG/LEAD Tests

    [Test]
    public void Lag_ReturnsPreviousRowValue()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary,
                   LAG(Salary, 1) OVER (ORDER BY Salary) AS PrevSalary
            FROM Employees
            ORDER BY Salary");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Grace has lowest salary (55000) so PrevSalary should be NULL for her
        var grace = rows.First(r => r["Name"].AsString() == "Grace");
        Assert.That(grace["PrevSalary"].IsNull, Is.True);

        // Henry (58000) should have Grace's salary (55000) as previous
        var henry = rows.First(r => r["Name"].AsString() == "Henry");
        Assert.That(henry["PrevSalary"].AsDecimal(), Is.EqualTo(55000m));
    }

    [Test]
    public void Lag_WithDefaultValue_ReturnsDefault()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary,
                   LAG(Salary, 1, 0) OVER (ORDER BY Salary) AS PrevSalary
            FROM Employees
            ORDER BY Salary");

        // Grace (lowest salary) should have 0 (the default) instead of NULL
        var grace = rows.First(r => r["Name"].AsString() == "Grace");
        Assert.That(grace["PrevSalary"].AsDecimal(), Is.EqualTo(0));
    }

    [Test]
    public void Lead_ReturnsNextRowValue()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary,
                   LEAD(Salary, 1) OVER (ORDER BY Salary) AS NextSalary
            FROM Employees
            ORDER BY Salary");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Carol has highest salary (85000) so NextSalary should be NULL for her
        var carol = rows.First(r => r["Name"].AsString() == "Carol");
        Assert.That(carol["NextSalary"].IsNull, Is.True);

        // Alice (80000) should have Carol's salary (85000) as next
        var alice = rows.First(r => r["Name"].AsString() == "Alice");
        Assert.That(alice["NextSalary"].AsDecimal(), Is.EqualTo(85000m));
    }

    [Test]
    public void Lead_WithPartition_WorksWithinPartition()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   LEAD(Salary, 1) OVER (PARTITION BY Department ORDER BY Salary) AS NextDeptSalary
            FROM Employees");

        // Carol has highest salary in Engineering, so her NextDeptSalary should be NULL
        var carol = rows.First(r => r["Name"].AsString() == "Carol");
        Assert.That(carol["NextDeptSalary"].IsNull, Is.True);
        
        // Bob (75000) in Engineering should have Alice (80000) as next
        var bob = rows.First(r => r["Name"].AsString() == "Bob");
        Assert.That(bob["NextDeptSalary"].AsDecimal(), Is.EqualTo(80000m));
    }

    #endregion

    #region FIRST_VALUE/LAST_VALUE Tests

    [Test]
    public void FirstValue_ReturnsFirstValueInPartition()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   FIRST_VALUE(Name) OVER (PARTITION BY Department ORDER BY Salary DESC) AS TopEarner
            FROM Employees");

        // In Engineering, Carol (85000) is the top earner
        var engineering = rows.Where(r => r["Department"].AsString() == "Engineering").ToList();
        foreach (var row in engineering)
        {
            Assert.That(row["TopEarner"].AsString(), Is.EqualTo("Carol"));
        }

        // In Sales, Eve (65000) is the top earner
        var sales = rows.Where(r => r["Department"].AsString() == "Sales").ToList();
        foreach (var row in sales)
        {
            Assert.That(row["TopEarner"].AsString(), Is.EqualTo("Eve"));
        }
    }

    [Test]
    public void LastValue_ReturnsLastValueInPartition()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   LAST_VALUE(Name) OVER (PARTITION BY Department ORDER BY Salary DESC) AS LowestEarner
            FROM Employees");

        // In Engineering, Bob (75000) is the lowest earner
        var engineering = rows.Where(r => r["Department"].AsString() == "Engineering").ToList();
        foreach (var row in engineering)
        {
            Assert.That(row["LowestEarner"].AsString(), Is.EqualTo("Bob"));
        }
    }

    #endregion

    #region NTH_VALUE Tests

    [Test]
    public void NthValue_ReturnsNthValue()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary,
                   NTH_VALUE(Name, 2) OVER (ORDER BY Salary DESC) AS SecondHighest
            FROM Employees");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Second highest salary is Alice (80000)
        foreach (var row in rows)
        {
            Assert.That(row["SecondHighest"].AsString(), Is.EqualTo("Alice"));
        }
    }

    #endregion

    #region Aggregate Window Functions Tests

    [Test]
    public void SumOver_ReturnsPartitionTotal()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   SUM(Salary) OVER (PARTITION BY Department) AS DeptTotal
            FROM Employees
            ORDER BY Department, Name");

        // Engineering total: 80000 + 75000 + 85000 = 240000
        var engineering = rows.Where(r => r["Department"].AsString() == "Engineering").ToList();
        foreach (var row in engineering)
        {
            Assert.That(row["DeptTotal"].AsDecimal(), Is.EqualTo(240000m));
        }

        // Sales total: 60000 + 65000 + 60000 = 185000
        var sales = rows.Where(r => r["Department"].AsString() == "Sales").ToList();
        foreach (var row in sales)
        {
            Assert.That(row["DeptTotal"].AsDecimal(), Is.EqualTo(185000m));
        }

        // HR total: 55000 + 58000 = 113000
        var hr = rows.Where(r => r["Department"].AsString() == "HR").ToList();
        foreach (var row in hr)
        {
            Assert.That(row["DeptTotal"].AsDecimal(), Is.EqualTo(113000m));
        }
    }

    [Test]
    public void AvgOver_ReturnsPartitionAverage()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   AVG(Salary) OVER (PARTITION BY Department) AS DeptAvg
            FROM Employees
            ORDER BY Department, Name");

        // Engineering avg: 240000 / 3 = 80000
        var engineering = rows.Where(r => r["Department"].AsString() == "Engineering").ToList();
        foreach (var row in engineering)
        {
            Assert.That(row["DeptAvg"].AsDouble(), Is.EqualTo(80000.0).Within(0.01));
        }
    }

    [Test]
    public void CountOver_ReturnsPartitionCount()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department,
                   COUNT(*) OVER (PARTITION BY Department) AS DeptCount
            FROM Employees
            ORDER BY Department, Name");

        // Engineering has 3 employees
        var engineering = rows.Where(r => r["Department"].AsString() == "Engineering").ToList();
        foreach (var row in engineering)
        {
            Assert.That(row["DeptCount"].AsInt64(), Is.EqualTo(3));
        }

        // Sales has 3 employees
        var sales = rows.Where(r => r["Department"].AsString() == "Sales").ToList();
        foreach (var row in sales)
        {
            Assert.That(row["DeptCount"].AsInt64(), Is.EqualTo(3));
        }

        // HR has 2 employees
        var hr = rows.Where(r => r["Department"].AsString() == "HR").ToList();
        foreach (var row in hr)
        {
            Assert.That(row["DeptCount"].AsInt64(), Is.EqualTo(2));
        }
    }

    [Test]
    public void MinMaxOver_ReturnsPartitionMinMax()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   MIN(Salary) OVER (PARTITION BY Department) AS MinSalary,
                   MAX(Salary) OVER (PARTITION BY Department) AS MaxSalary
            FROM Employees
            ORDER BY Department, Name");

        // Engineering: min 75000, max 85000
        var engineering = rows.Where(r => r["Department"].AsString() == "Engineering").ToList();
        foreach (var row in engineering)
        {
            Assert.That(row["MinSalary"].AsDecimal(), Is.EqualTo(75000m));
            Assert.That(row["MaxSalary"].AsDecimal(), Is.EqualTo(85000m));
        }
    }

    #endregion

    #region Multiple Window Functions Tests

    [Test]
    public void MultipleWindowFunctions_InSameQuery()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Department, Salary,
                   ROW_NUMBER() OVER (PARTITION BY Department ORDER BY Salary DESC) AS DeptRank,
                   ROW_NUMBER() OVER (ORDER BY Salary DESC) AS GlobalRank,
                   SUM(Salary) OVER (PARTITION BY Department) AS DeptTotal
            FROM Employees");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Check Carol has GlobalRank 1 (highest salary)
        var carol = rows.First(r => r["Name"].AsString() == "Carol");
        Assert.That(carol["GlobalRank"].AsInt64(), Is.EqualTo(1));
        Assert.That(carol["DeptRank"].AsInt64(), Is.EqualTo(1)); // Also top in Engineering
        Assert.That(carol["DeptTotal"].AsDecimal(), Is.EqualTo(240000m));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void WindowFunction_EmptyTable_HandlesGracefully()
    {
        m_engine.Execute("CREATE TABLE EmptyTest (Id INT PRIMARY KEY, Value INT)");

        var rows = m_engine.Query(@"
            SELECT Id, Value, ROW_NUMBER() OVER (ORDER BY Value) AS RowNum
            FROM EmptyTest");

        Assert.That(rows, Has.Count.EqualTo(0));
    }

    [Test]
    public void WindowFunction_SingleRow_WorksCorrectly()
    {
        m_engine.Execute("CREATE TABLE SingleRow (Id INT PRIMARY KEY, Value INT)");
        m_engine.Execute("INSERT INTO SingleRow (Id, Value) VALUES (1, 100)");

        var rows = m_engine.Query(@"
            SELECT Id, Value,
                   ROW_NUMBER() OVER (ORDER BY Value) AS RowNum,
                   RANK() OVER (ORDER BY Value) AS Rank,
                   LAG(Value, 1) OVER (ORDER BY Value) AS PrevValue,
                   LEAD(Value, 1) OVER (ORDER BY Value) AS NextValue
            FROM SingleRow");

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["RowNum"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["Rank"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["PrevValue"].IsNull, Is.True);
        Assert.That(rows[0]["NextValue"].IsNull, Is.True);
    }

    [Test]
    public void WindowFunction_WithNullValues_HandlesCorrectly()
    {
        m_engine.Execute("CREATE TABLE WithNulls (Id INT PRIMARY KEY, Value INT)");
        m_engine.Execute(@"
            INSERT INTO WithNulls (Id, Value) VALUES 
            (1, 10), (2, NULL), (3, 30), (4, NULL), (5, 50)");

        var rows = m_engine.Query(@"
            SELECT Id, Value,
                   ROW_NUMBER() OVER (ORDER BY Value) AS RowNum,
                   COUNT(Value) OVER () AS NonNullCount
            FROM WithNulls
            ORDER BY RowNum");

        Assert.That(rows, Has.Count.EqualTo(5));
        
        // COUNT(Value) should be 3 (excludes NULLs)
        foreach (var row in rows)
        {
            Assert.That(row["NonNullCount"].AsInt64(), Is.EqualTo(3));
        }
    }

    #endregion

    #region PERCENT_RANK and CUME_DIST Tests

    [Test]
    public void PercentRank_CalculatesCorrectly()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary,
                   PERCENT_RANK() OVER (ORDER BY Salary) AS PctRank
            FROM Employees");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Grace (lowest salary) should have percent_rank of 0
        var grace = rows.First(r => r["Name"].AsString() == "Grace");
        Assert.That(grace["PctRank"].AsDouble(), Is.EqualTo(0.0).Within(0.001));

        // Carol (highest salary) should have percent_rank of 1
        var carol = rows.First(r => r["Name"].AsString() == "Carol");
        Assert.That(carol["PctRank"].AsDouble(), Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void CumeDist_CalculatesCorrectly()
    {
        var rows = m_engine.Query(@"
            SELECT Name, Salary,
                   CUME_DIST() OVER (ORDER BY Salary) AS CumeDist
            FROM Employees");

        Assert.That(rows, Has.Count.EqualTo(8));

        // CUME_DIST for all rows should be > 0
        foreach (var row in rows)
        {
            Assert.That(row["CumeDist"].AsDouble(), Is.GreaterThan(0));
        }

        // Carol (highest salary) should have cume_dist of 1
        var carol = rows.First(r => r["Name"].AsString() == "Carol");
        Assert.That(carol["CumeDist"].AsDouble(), Is.EqualTo(1.0).Within(0.001));
    }

    #endregion

    #region Complex Expressions with Window Functions

    [Test]
    public void WindowFunction_InArithmeticExpression()
    {
        // Note: Expressions containing window functions (like Salary - LAG(...)) 
        // require the window function to be in a subquery or CTE.
        // For now, test that window function results can be used in outer queries.
        var rows = m_engine.Query(@"
            SELECT Name, Salary, PrevSalary, Salary - PrevSalary AS SalaryDiff
            FROM (
                SELECT Name, Salary,
                       LAG(Salary, 1, Salary) OVER (ORDER BY Salary) AS PrevSalary
                FROM Employees
            ) AS sub
            ORDER BY Salary");

        Assert.That(rows, Has.Count.EqualTo(8));

        // Grace (lowest salary) should have diff of 0 (Salary - Salary because default is same as Salary)
        var grace = rows.First(r => r["Name"].AsString() == "Grace");
        Assert.That(grace["SalaryDiff"].AsDecimal(), Is.EqualTo(0m));
    }

    #endregion
}
