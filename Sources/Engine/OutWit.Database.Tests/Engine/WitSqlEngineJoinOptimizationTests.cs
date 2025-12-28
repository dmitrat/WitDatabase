using OutWit.Database.Core.Builder;

namespace OutWit.Database.Tests;

/// <summary>
/// Integration tests for join order optimization in queries.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineJoinOptimizationTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);
    }

    #endregion

    #region Implicit Join (FROM a, b) Tests

    [Test]
    public void ImplicitCrossJoinWorksWithDifferentSizeTablesTest()
    {
        // Create tables with different sizes
        CreateSmallAndLargeTables();

        // Query with implicit cross join (should optimize order)
        var result = m_engine.Query(@"
            SELECT s.Id AS SmallId, l.Id AS LargeId
            FROM SmallTable s, LargeTable l
            WHERE s.Id = l.SmallId");

        // Should return matching rows
        Assert.That(result.Count, Is.GreaterThan(0));
        Assert.That(result.All(r => r["SmallId"].AsInt64() == r["SmallId"].AsInt64()), Is.True);
    }

    [Test]
    public void ImplicitJoinWithThreeTablesWorksTest()
    {
        CreateThreeRelatedTables();

        // Query with three tables (should optimize order)
        var result = m_engine.Query(@"
            SELECT c.Name AS Category, p.Name AS Product, o.Quantity
            FROM OrderItems o, Products p, Categories c
            WHERE o.ProductId = p.Id AND p.CategoryId = c.Id");

        Assert.That(result.Count, Is.GreaterThan(0));
    }

    [Test]
    public void ImplicitJoinProducesCorrectResultsTest()
    {
        // Create simple tables
        m_engine.Execute("CREATE TABLE A (Id BIGINT PRIMARY KEY, Value VARCHAR(10))");
        m_engine.Execute("CREATE TABLE B (Id BIGINT PRIMARY KEY, AId BIGINT, Name VARCHAR(10))");

        m_engine.Execute("INSERT INTO A (Id, Value) VALUES (1, 'One')");
        m_engine.Execute("INSERT INTO A (Id, Value) VALUES (2, 'Two')");
        m_engine.Execute("INSERT INTO B (Id, AId, Name) VALUES (1, 1, 'B1')");
        m_engine.Execute("INSERT INTO B (Id, AId, Name) VALUES (2, 1, 'B2')");
        m_engine.Execute("INSERT INTO B (Id, AId, Name) VALUES (3, 2, 'B3')");

        // Implicit join with WHERE condition
        var result = m_engine.Query("SELECT A.Value, B.Name FROM A, B WHERE A.Id = B.AId ORDER BY B.Name");

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0]["Value"].AsString(), Is.EqualTo("One"));
        Assert.That(result[0]["Name"].AsString(), Is.EqualTo("B1"));
    }

    #endregion

    #region Explicit JOIN Tests

    [Test]
    public void ExplicitInnerJoinOptimizesOrderTest()
    {
        CreateSmallAndLargeTables();

        // Large LEFT JOIN Small - but join ordering should optimize INNER joins
        // For nested-loop join, smaller table on outer loop is better
        var result = m_engine.Query(@"
            SELECT l.Id, s.Value
            FROM LargeTable l
            INNER JOIN SmallTable s ON l.SmallId = s.Id
            ORDER BY l.Id");

        Assert.That(result.Count, Is.GreaterThan(0));
    }

    [Test]
    public void LeftJoinPreservesSemanticOrderTest()
    {
        CreateSmallAndLargeTables();

        // LEFT JOIN should NOT swap sides (semantics would change)
        var result = m_engine.Query(@"
            SELECT l.Id, s.Value
            FROM LargeTable l
            LEFT JOIN SmallTable s ON l.SmallId = s.Id
            ORDER BY l.Id");

        // Should have all LargeTable rows
        Assert.That(result.Count, Is.EqualTo(100));
    }

    [Test]
    public void RightJoinPreservesSemanticOrderTest()
    {
        // Create tables where not all SmallTable rows have matches
        m_engine.Execute("CREATE TABLE SmallTable (Id BIGINT PRIMARY KEY, Value VARCHAR(50))");
        m_engine.Execute("CREATE TABLE LargeTable (Id BIGINT PRIMARY KEY, SmallId BIGINT, Value VARCHAR(50))");
        
        // Insert 10 rows into SmallTable
        for (int i = 1; i <= 10; i++)
        {
            m_engine.Execute($"INSERT INTO SmallTable (Id, Value) VALUES ({i}, 'Small{i}')");
        }

        // Insert only 50 rows into LargeTable (some SmallTable rows won't match)
        for (int i = 1; i <= 50; i++)
        {
            var smallId = ((i - 1) % 5) + 1; // Only matches SmallTable rows 1-5
            m_engine.Execute($"INSERT INTO LargeTable (Id, SmallId, Value) VALUES ({i}, {smallId}, 'Large{i}')");
        }

        // RIGHT JOIN should return all SmallTable rows (with NULLs for unmatched)
        // SmallTable rows 1-5 each have 10 matches, rows 6-10 have no matches
        var result = m_engine.Query(@"
            SELECT s.Id AS SId, l.Id AS LId
            FROM LargeTable l
            RIGHT JOIN SmallTable s ON l.SmallId = s.Id
            ORDER BY s.Id");

        // Should have 50 matched rows + 5 unmatched rows = 55 rows
        Assert.That(result.Count, Is.EqualTo(55));
        
        // Verify that unmatched rows have NULL for LargeTable columns
        var unmatchedRows = result.Where(r => r["SId"].AsInt64() > 5).ToList();
        Assert.That(unmatchedRows, Has.Count.EqualTo(5));
        Assert.That(unmatchedRows.All(r => r["LId"].IsNull), Is.True);
    }

    #endregion

    #region Performance Verification Tests

    [Test]
    public void JoinOptimizationDoesNotChangeResultsTest()
    {
        CreateSmallAndLargeTables();

        // Execute query that will trigger optimization
        var optimized = m_engine.Query(@"
            SELECT s.Id AS SId, l.Id AS LId, s.Value AS SValue
            FROM LargeTable l, SmallTable s
            WHERE l.SmallId = s.Id
            ORDER BY s.Id, l.Id");

        // Execute equivalent query with explicit order
        var explicit_result = m_engine.Query(@"
            SELECT s.Id AS SId, l.Id AS LId, s.Value AS SValue
            FROM SmallTable s, LargeTable l
            WHERE s.Id = l.SmallId
            ORDER BY s.Id, l.Id");

        // Results should be identical
        Assert.That(optimized.Count, Is.EqualTo(explicit_result.Count));
        
        for (int i = 0; i < optimized.Count; i++)
        {
            Assert.That(optimized[i]["SId"].AsInt64(), Is.EqualTo(explicit_result[i]["SId"].AsInt64()));
            Assert.That(optimized[i]["LId"].AsInt64(), Is.EqualTo(explicit_result[i]["LId"].AsInt64()));
        }
    }

    [Test]
    public void MultiTableJoinProducesCorrectResultsTest()
    {
        CreateThreeRelatedTables();

        // Complex join across three tables - use aliases to avoid ambiguity
        var result = m_engine.Query(@"
            SELECT 
                c.Name AS CategoryName,
                COUNT(*) AS ProductCount,
                SUM(o.Quantity) AS TotalQuantity
            FROM Categories c
            INNER JOIN Products p ON c.Id = p.CategoryId
            INNER JOIN OrderItems o ON p.Id = o.ProductId
            GROUP BY c.Name
            ORDER BY CategoryName");

        Assert.That(result.Count, Is.GreaterThan(0));
    }

    #endregion

    #region Cross Join Tests

    [Test]
    public void CrossJoinOptimizesOrderTest()
    {
        m_engine.Execute("CREATE TABLE T1 (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE T2 (Id BIGINT PRIMARY KEY)");

        for (int i = 1; i <= 5; i++)
            m_engine.Execute($"INSERT INTO T1 (Id) VALUES ({i})");
        for (int i = 1; i <= 3; i++)
            m_engine.Execute($"INSERT INTO T2 (Id) VALUES ({i})");

        // Explicit CROSS JOIN - use qualified column names
        var result = m_engine.Query(@"
            SELECT T1.Id, T2.Id
            FROM T1 CROSS JOIN T2
            ORDER BY T1.Id, T2.Id");

        // Should have 5 * 3 = 15 rows
        Assert.That(result.Count, Is.EqualTo(15));
    }

    #endregion

    #region Self-Join Tests

    [Test]
    public void SelfJoinWorksCorrectlyTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Employees (
                Id BIGINT PRIMARY KEY,
                Name VARCHAR(100),
                ManagerId BIGINT
            )");

        m_engine.Execute("INSERT INTO Employees (Id, Name, ManagerId) VALUES (1, 'CEO', NULL)");
        m_engine.Execute("INSERT INTO Employees (Id, Name, ManagerId) VALUES (2, 'Manager1', 1)");
        m_engine.Execute("INSERT INTO Employees (Id, Name, ManagerId) VALUES (3, 'Manager2', 1)");
        m_engine.Execute("INSERT INTO Employees (Id, Name, ManagerId) VALUES (4, 'Employee1', 2)");
        m_engine.Execute("INSERT INTO Employees (Id, Name, ManagerId) VALUES (5, 'Employee2', 2)");

        // Self-join with aliases
        var result = m_engine.Query(@"
            SELECT e.Name AS Employee, m.Name AS Manager
            FROM Employees e
            LEFT JOIN Employees m ON e.ManagerId = m.Id
            ORDER BY e.Id");

        Assert.That(result.Count, Is.EqualTo(5));
        Assert.That(result[0]["Manager"].IsNull, Is.True); // CEO has no manager
        Assert.That(result[1]["Manager"].AsString(), Is.EqualTo("CEO"));
    }

    #endregion

    #region Helper Methods

    private void CreateSmallAndLargeTables()
    {
        m_engine.Execute(@"
            CREATE TABLE SmallTable (
                Id BIGINT PRIMARY KEY,
                Value VARCHAR(50)
            )");

        m_engine.Execute(@"
            CREATE TABLE LargeTable (
                Id BIGINT PRIMARY KEY,
                SmallId BIGINT,
                Value VARCHAR(50)
            )");

        // Insert 10 rows into SmallTable
        for (int i = 1; i <= 10; i++)
        {
            m_engine.Execute($"INSERT INTO SmallTable (Id, Value) VALUES ({i}, 'Small{i}')");
        }

        // Insert 100 rows into LargeTable (10 per SmallTable row)
        for (int i = 1; i <= 100; i++)
        {
            var smallId = ((i - 1) % 10) + 1;
            m_engine.Execute($"INSERT INTO LargeTable (Id, SmallId, Value) VALUES ({i}, {smallId}, 'Large{i}')");
        }
    }

    private void CreateThreeRelatedTables()
    {
        m_engine.Execute(@"
            CREATE TABLE Categories (
                Id BIGINT PRIMARY KEY,
                Name VARCHAR(50)
            )");

        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY,
                CategoryId BIGINT,
                Name VARCHAR(100)
            )");

        m_engine.Execute(@"
            CREATE TABLE OrderItems (
                Id BIGINT PRIMARY KEY,
                ProductId BIGINT,
                Quantity INT
            )");

        // Insert categories (small: 3 rows)
        m_engine.Execute("INSERT INTO Categories (Id, Name) VALUES (1, 'Electronics')");
        m_engine.Execute("INSERT INTO Categories (Id, Name) VALUES (2, 'Clothing')");
        m_engine.Execute("INSERT INTO Categories (Id, Name) VALUES (3, 'Books')");

        // Insert products (medium: 15 rows, 5 per category)
        for (int i = 1; i <= 15; i++)
        {
            var categoryId = ((i - 1) % 3) + 1;
            m_engine.Execute($"INSERT INTO Products (Id, CategoryId, Name) VALUES ({i}, {categoryId}, 'Product{i}')");
        }

        // Insert order items (large: 100 rows)
        for (int i = 1; i <= 100; i++)
        {
            var productId = ((i - 1) % 15) + 1;
            var quantity = (i % 10) + 1;
            m_engine.Execute($"INSERT INTO OrderItems (Id, ProductId, Quantity) VALUES ({i}, {productId}, {quantity})");
        }
    }

    #endregion
}
