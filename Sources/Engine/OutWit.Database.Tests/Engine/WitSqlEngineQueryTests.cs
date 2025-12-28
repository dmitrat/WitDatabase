namespace OutWit.Database.Tests;

/// <summary>
/// Tests for WitSqlEngine SELECT query operations.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineQueryTests : WitSqlEngineTestsBase
{
    #region Basic Select Tests

    [Test]
    public void SelectAllColumnsReturnsAllColumnsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0].ColumnNames, Does.Contain("Id"));
        Assert.That(rows[0].ColumnNames, Does.Contain("Name"));
        Assert.That(rows[0].ColumnNames, Does.Contain("Email"));
    }

    [Test]
    public void SelectSpecificColumnsReturnsOnlyThoseColumnsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT Name, Email FROM Users");
        
        Assert.That(rows[0].ColumnNames, Does.Contain("Name"));
        Assert.That(rows[0].ColumnNames, Does.Contain("Email"));
    }

    [Test]
    public void SelectWithAliasReturnsAliasedColumnTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT Name AS UserName FROM Users");
        
        Assert.That(rows[0].ColumnNames, Does.Contain("UserName"));
    }

    #endregion

    #region Where Tests

    [Test]
    public void SelectWithWhereFiltersRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice'");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void SelectWithWhereAndFiltersRowsTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 1)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 2)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 1)");
        
        var rows = m_engine.Query("SELECT * FROM TestTable WHERE Name = 'A' AND Value = 1");
        
        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void SelectWithWhereOrFiltersRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice' OR Name = 'Bob'");
        
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectWithWhereInFiltersRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users WHERE Name IN ('Alice', 'Charlie')");
        
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectWithWhereLikeFiltersRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users WHERE Name LIKE 'A%'");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void SelectWithWhereBetweenFiltersRowsTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 1)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 5)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('C', 10)");
        
        var rows = m_engine.Query("SELECT * FROM TestTable WHERE Value BETWEEN 2 AND 8");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("B"));
    }

    #endregion

    #region Order By Tests

    [Test]
    public void SelectWithOrderByAscSortsAscendingTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Name ASC");
        
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("Charlie"));
    }

    [Test]
    public void SelectWithOrderByDescSortsDescendingTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Name DESC");
        
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Charlie"));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void SelectWithOrderByMultipleColumnsSortsCorrectlyTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 2)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 1)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 1)");
        
        var rows = m_engine.Query("SELECT * FROM TestTable ORDER BY Name ASC, Value ASC");
        
        Assert.That(rows[0]["Value"].AsInt64(), Is.EqualTo(1)); // A, 1
        Assert.That(rows[1]["Value"].AsInt64(), Is.EqualTo(2)); // A, 2
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("B")); // B, 1
    }

    #endregion

    #region Limit / Offset Tests

    [Test]
    public void SelectWithLimitReturnsLimitedRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users LIMIT 2");
        
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectWithLimitAndOffsetReturnsCorrectRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Name LIMIT 1 OFFSET 1");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Bob"));
    }

    #endregion

    #region Distinct Tests

    [Test]
    public void SelectDistinctReturnsUniqueRowsTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 1)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 2)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 1)");
        
        var rows = m_engine.Query("SELECT DISTINCT Name FROM TestTable");
        
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    #endregion

    #region Group By Tests

    [Test]
    public void SelectWithGroupByGroupsRowsTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 1)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 2)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 3)");
        
        var rows = m_engine.Query("SELECT Name, SUM(Value) AS Total FROM TestTable GROUP BY Name ORDER BY Name");
        
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Total"].AsInt64(), Is.EqualTo(3)); // A: 1+2
        Assert.That(rows[1]["Total"].AsInt64(), Is.EqualTo(3)); // B: 3
    }

    [Test]
    public void SelectWithGroupByAndHavingFiltersGroupsTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 1)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 2)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 3)");
        
        var rows = m_engine.Query("SELECT Name, COUNT(*) AS Cnt FROM TestTable GROUP BY Name HAVING COUNT(*) > 1");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("A"));
    }

    #endregion

    #region Aggregate Function Tests

    [Test]
    public void SelectCountReturnsCountTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM Users");
        
        Assert.That(count.AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void SelectSumReturnsSumTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 10)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 20)");
        
        var sum = m_engine.ExecuteScalar("SELECT SUM(Value) FROM TestTable");
        
        Assert.That(sum.AsInt64(), Is.EqualTo(30));
    }

    [Test]
    public void SelectAvgReturnsAverageTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 10)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 20)");
        
        var avg = m_engine.ExecuteScalar("SELECT AVG(Value) FROM TestTable");
        
        Assert.That(avg.AsDouble(), Is.EqualTo(15.0));
    }

    [Test]
    public void SelectMinReturnsMinimumTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 10)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 5)");
        
        var min = m_engine.ExecuteScalar("SELECT MIN(Value) FROM TestTable");
        
        Assert.That(min.AsInt64(), Is.EqualTo(5));
    }

    [Test]
    public void SelectMaxReturnsMaximumTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 10)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 20)");
        
        var max = m_engine.ExecuteScalar("SELECT MAX(Value) FROM TestTable");
        
        Assert.That(max.AsInt64(), Is.EqualTo(20));
    }

    #endregion

    #region Join Tests

    [Test]
    public void SelectWithInnerJoinReturnsMatchingRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT,
                Total DECIMAL
            )");
        m_engine.Execute("INSERT INTO Orders (UserId, Total) VALUES (1, 100)");
        m_engine.Execute("INSERT INTO Orders (UserId, Total) VALUES (2, 200)");
        
        var rows = m_engine.Query(@"
            SELECT u.Name, o.Total 
            FROM Users u 
            INNER JOIN Orders o ON u.Id = o.UserId
            ORDER BY u.Name");
        
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectWithLeftJoinReturnsAllLeftRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT,
                Total DECIMAL
            )");
        m_engine.Execute("INSERT INTO Orders (UserId, Total) VALUES (1, 100)");
        
        var rows = m_engine.Query(@"
            SELECT u.Name, o.Total 
            FROM Users u 
            LEFT JOIN Orders o ON u.Id = o.UserId
            ORDER BY u.Name");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Total"].IsNull, Is.False); // Alice has order
        Assert.That(rows[1]["Total"].IsNull, Is.True);  // Bob has no order
    }

    #endregion

    #region Expression Tests

    [Test]
    public void SelectWithExpressionReturnsComputedValueTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('Test', 10)");
        
        var rows = m_engine.Query("SELECT Name, Value * 2 AS Doubled FROM TestTable");
        
        Assert.That(rows[0]["Doubled"].AsInt64(), Is.EqualTo(20));
    }

    [Test]
    public void SelectWithCaseExpressionReturnsCorrectValueTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('A', 1)");
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('B', 2)");
        
        var rows = m_engine.Query(@"
            SELECT Name, 
                CASE WHEN Value = 1 THEN 'One' ELSE 'Other' END AS ValueText 
            FROM TestTable
            ORDER BY Name");
        
        Assert.That(rows[0]["ValueText"].AsString(), Is.EqualTo("One"));
        Assert.That(rows[1]["ValueText"].AsString(), Is.EqualTo("Other"));
    }

    #endregion
}
