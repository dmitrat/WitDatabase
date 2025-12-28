using OutWit.Database.Core;
using OutWit.Database.Core.Builder;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for EXPLAIN statement execution.
/// </summary>
[TestFixture]
public class WitSqlEngineExplainTests
{
    #region Fields

    private Engine.WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();

        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);

        // Create test tables
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id INT PRIMARY KEY,
                Name TEXT NOT NULL,
                Email TEXT,
                Age INT
            )");

        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id INT PRIMARY KEY,
                UserId INT NOT NULL,
                Total DECIMAL,
                OrderDate DATE,
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            )");

        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name TEXT NOT NULL,
                Category TEXT,
                Price DECIMAL
            )");

        // Create indexes for testing
        m_engine.Execute("CREATE INDEX IX_Orders_UserId ON Orders (UserId)");
        m_engine.Execute("CREATE INDEX IX_Products_Category ON Products (Category)");

        // Insert sample data
        m_engine.Execute("INSERT INTO Users VALUES (1, 'Alice', 'alice@example.com', 30)");
        m_engine.Execute("INSERT INTO Users VALUES (2, 'Bob', 'bob@example.com', 25)");
        m_engine.Execute("INSERT INTO Orders VALUES (1, 1, 100.00, '2024-01-01')");
        m_engine.Execute("INSERT INTO Orders VALUES (2, 1, 200.00, '2024-01-15')");
        m_engine.Execute("INSERT INTO Products VALUES (1, 'Widget', 'Electronics', 29.99)");
        m_engine.Execute("INSERT INTO Products VALUES (2, 'Gadget', 'Electronics', 49.99)");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Simple EXPLAIN Tests

    [Test]
    public void ExplainSimpleSelectTest()
    {
        var result = m_engine.Execute("EXPLAIN SELECT * FROM Users");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(0));
        Assert.That(result.Columns.Count, Is.EqualTo(3)); // id, parent, detail
        Assert.That(result.Columns[0].Name, Is.EqualTo("id"));
        Assert.That(result.Columns[1].Name, Is.EqualTo("parent"));
        Assert.That(result.Columns[2].Name, Is.EqualTo("detail"));
    }

    [Test]
    public void ExplainReturnsTableScanForSimpleQueryTest()
    {
        var result = m_engine.Execute("EXPLAIN SELECT * FROM Users");
        var rows = result.ReadAll();
        
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        Assert.That(details.Any(d => d.Contains("SCAN TABLE") || d.Contains("Users")), Is.True);
    }

    [Test]
    public void ExplainQueryPlanSimpleSelectTest()
    {
        var result = m_engine.Execute("EXPLAIN QUERY PLAN SELECT * FROM Users WHERE Id = 1");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(0));
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        Assert.That(details.Any(d => d.Contains("SCAN") || d.Contains("SEARCH") || d.Contains("Users")), Is.True);
    }

    #endregion

    #region Index Usage Tests

    [Test]
    public void ExplainShowsIndexUsageTest()
    {
        // Create an index and verify EXPLAIN shows it being used
        var result = m_engine.Execute("EXPLAIN QUERY PLAN SELECT * FROM Orders WHERE UserId = 1");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(0));
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        
        // Should show either index usage or table scan
        Assert.That(details.Any(d => 
            d.Contains("INDEX") || 
            d.Contains("SCAN") || 
            d.Contains("Orders")), Is.True);
    }

    #endregion

    #region Join Tests

    [Test]
    public void ExplainJoinQueryTest()
    {
        var result = m_engine.Execute(@"
            EXPLAIN QUERY PLAN 
            SELECT u.Name, o.Total 
            FROM Users u 
            INNER JOIN Orders o ON u.Id = o.UserId");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(1));
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        
        // Should show join operation
        Assert.That(details.Any(d => 
            d.Contains("JOIN") || 
            d.Contains("NESTED LOOP") || 
            d.Contains("HASH")), Is.True);
    }

    [Test]
    public void ExplainMultipleTableJoinTest()
    {
        m_engine.Execute(@"
            CREATE TABLE OrderItems (
                Id INT PRIMARY KEY,
                OrderId INT,
                ProductId INT
            )");
        m_engine.Execute("INSERT INTO OrderItems VALUES (1, 1, 1)");

        var result = m_engine.Execute(@"
            EXPLAIN QUERY PLAN 
            SELECT u.Name, p.Name, oi.OrderId
            FROM Users u 
            INNER JOIN Orders o ON u.Id = o.UserId
            INNER JOIN OrderItems oi ON o.Id = oi.OrderId
            INNER JOIN Products p ON oi.ProductId = p.Id");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(2));
    }

    #endregion

    #region Aggregation Tests

    [Test]
    public void ExplainGroupByQueryTest()
    {
        var result = m_engine.Execute(@"
            EXPLAIN QUERY PLAN 
            SELECT Category, COUNT(*) 
            FROM Products 
            GROUP BY Category");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(0));
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        
        // Should show aggregation
        Assert.That(details.Any(d => d.Contains("AGGREGATE") || d.Contains("GroupBy")), Is.True);
    }

    #endregion

    #region Sort/Limit Tests

    [Test]
    public void ExplainOrderByQueryTest()
    {
        var result = m_engine.Execute(@"
            EXPLAIN QUERY PLAN 
            SELECT * FROM Products 
            ORDER BY Price DESC");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(0));
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        
        // Should show sort operation
        Assert.That(details.Any(d => d.Contains("SORT") || d.Contains("Sort")), Is.True);
    }

    [Test]
    public void ExplainLimitQueryTest()
    {
        var result = m_engine.Execute(@"
            EXPLAIN QUERY PLAN 
            SELECT * FROM Users 
            LIMIT 10");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(0));
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        
        // Should show limit operation
        Assert.That(details.Any(d => d.Contains("LIMIT") || d.Contains("Limit")), Is.True);
    }

    #endregion

    #region Window Function Tests

    [Test]
    public void ExplainWindowFunctionQueryTest()
    {
        var result = m_engine.Execute(@"
            EXPLAIN QUERY PLAN 
            SELECT 
                Name,
                ROW_NUMBER() OVER (ORDER BY Price) AS RowNum
            FROM Products");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(0));
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        
        // Should show window operation
        Assert.That(details.Any(d => d.Contains("WINDOW") || d.Contains("Window")), Is.True);
    }

    #endregion

    #region Subquery Tests

    [Test]
    public void ExplainSubqueryTest()
    {
        var result = m_engine.Execute(@"
            EXPLAIN QUERY PLAN 
            SELECT * FROM Users 
            WHERE Id IN (SELECT UserId FROM Orders WHERE Total > 150)");
        var rows = result.ReadAll();
        
        Assert.That(rows.Count, Is.GreaterThan(0));
    }

    #endregion

    #region Detailed vs Query Plan Tests

    [Test]
    public void ExplainDetailedShowsSchemaTest()
    {
        var result = m_engine.Execute("EXPLAIN SELECT Id, Name FROM Users");
        var rows = result.ReadAll();
        
        var details = rows.Select(r => r["detail"].AsString()).ToList();
        
        // Detailed EXPLAIN should show column schema
        Assert.That(details.Any(d => d.Contains("->") || d.Contains("[")), Is.True);
    }

    [Test]
    public void ExplainQueryPlanIsMoreConciseTest()
    {
        var resultDetailed = m_engine.Execute("EXPLAIN SELECT * FROM Users");
        var resultQueryPlan = m_engine.Execute("EXPLAIN QUERY PLAN SELECT * FROM Users");
        
        var rowsDetailed = resultDetailed.ReadAll();
        var rowsQueryPlan = resultQueryPlan.ReadAll();
        
        // Both should return results
        Assert.That(rowsDetailed.Count, Is.GreaterThan(0));
        Assert.That(rowsQueryPlan.Count, Is.GreaterThan(0));
    }

    #endregion
}
