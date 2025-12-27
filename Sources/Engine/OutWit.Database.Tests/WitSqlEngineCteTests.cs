namespace OutWit.Database.Tests;

/// <summary>
/// Tests for CTE (Common Table Expression) execution.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineCteTests : WitSqlEngineTestsBase
{
    #region Setup

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        CreateTestTables();
    }

    private void CreateTestTables()
    {
        // Create Orders table
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT,
                Amount DECIMAL,
                Status VARCHAR(20)
            )");

        // Create Products table
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Category VARCHAR(50),
                Price DECIMAL
            )");

        // Create Categories table for hierarchical tests
        m_engine.Execute(@"
            CREATE TABLE Categories (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                ParentId BIGINT
            )");
    }

    private void InsertTestData()
    {
        // Insert orders
        m_engine.Execute("INSERT INTO Orders (UserId, Amount, Status) VALUES (1, 100, 'active')");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount, Status) VALUES (1, 200, 'active')");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount, Status) VALUES (2, 150, 'pending')");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount, Status) VALUES (2, 300, 'active')");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount, Status) VALUES (3, 50, 'cancelled')");

        // Insert products
        m_engine.Execute("INSERT INTO Products (Name, Category, Price) VALUES ('Widget A', 'Electronics', 29.99)");
        m_engine.Execute("INSERT INTO Products (Name, Category, Price) VALUES ('Widget B', 'Electronics', 49.99)");
        m_engine.Execute("INSERT INTO Products (Name, Category, Price) VALUES ('Gadget X', 'Accessories', 19.99)");
        m_engine.Execute("INSERT INTO Products (Name, Category, Price) VALUES ('Gadget Y', 'Electronics', 99.99)");
    }

    private void InsertCategoryHierarchy()
    {
        // Insert hierarchical categories
        // Root categories (no parent)
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Electronics', NULL)");     // Id = 1
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Clothing', NULL)");        // Id = 2
        
        // Level 1
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Computers', 1)");          // Id = 3
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Phones', 1)");             // Id = 4
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Men', 2)");                // Id = 5
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Women', 2)");              // Id = 6
        
        // Level 2
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Laptops', 3)");            // Id = 7
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Desktops', 3)");           // Id = 8
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Smartphones', 4)");        // Id = 9
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Shirts', 5)");             // Id = 10
    }

    #endregion

    #region Simple CTE Tests

    [Test]
    public void SimpleCteSelectAllFromCteTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH ActiveOrders AS (
                SELECT * FROM Orders WHERE Status = 'active'
            )
            SELECT * FROM ActiveOrders");

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.All(r => r["Status"].AsString() == "active"), Is.True);
    }

    [Test]
    public void SimpleCteWithFilterOnCteTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH ActiveOrders AS (
                SELECT * FROM Orders WHERE Status = 'active'
            )
            SELECT * FROM ActiveOrders WHERE Amount > 100");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.All(r => r["Amount"].AsDecimal() > 100), Is.True);
    }

    [Test]
    public void SimpleCteWithAggregationTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH ActiveOrders AS (
                SELECT * FROM Orders WHERE Status = 'active'
            )
            SELECT COUNT(*) AS OrderCount, SUM(Amount) AS TotalAmount FROM ActiveOrders");

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["OrderCount"].AsInt64(), Is.EqualTo(3));
        Assert.That(rows[0]["TotalAmount"].AsDecimal(), Is.EqualTo(600m)); // 100 + 200 + 300
    }

    [Test]
    public void CteWithAliasTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH ActiveOrders AS (
                SELECT * FROM Orders WHERE Status = 'active'
            )
            SELECT ao.UserId, ao.Amount FROM ActiveOrders ao WHERE ao.Amount > 150");

        Assert.That(rows, Has.Count.EqualTo(2));
    }

    #endregion

    #region CTE with Column Names Tests

    [Test]
    public void CteWithExplicitColumnNamesTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH UserTotals (UserId, TotalAmount) AS (
                SELECT UserId, SUM(Amount) FROM Orders GROUP BY UserId
            )
            SELECT * FROM UserTotals ORDER BY TotalAmount DESC");

        Assert.That(rows, Has.Count.EqualTo(3));
        // User 2 has highest total (150 + 300 = 450)
        Assert.That(rows[0]["UserId"].AsInt64(), Is.EqualTo(2));
    }

    #endregion

    #region Multiple CTE Tests

    [Test]
    public void MultipleCteDefinitionsTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH 
                ActiveOrders AS (
                    SELECT * FROM Orders WHERE Status = 'active'
                ),
                ExpensiveProducts AS (
                    SELECT * FROM Products WHERE Price > 30
                )
            SELECT COUNT(*) AS ActiveCount FROM ActiveOrders");

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["ActiveCount"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void MultipleCteWithJoinTest()
    {
        // Create Users table
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100)
            )");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Alice')");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Bob')");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Charlie')");

        InsertTestData();

        var rows = m_engine.Query(@"
            WITH 
                ActiveOrders AS (
                    SELECT * FROM Orders WHERE Status = 'active'
                ),
                UserNames AS (
                    SELECT Id, Name FROM Users
                )
            SELECT u.Name, ao.Amount 
            FROM ActiveOrders ao
            INNER JOIN UserNames u ON ao.UserId = u.Id
            ORDER BY ao.Amount DESC");

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Amount"].AsDecimal(), Is.EqualTo(300m));
    }

    [Test]
    public void CteReferencingAnotherCteTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH 
                AllOrders AS (
                    SELECT * FROM Orders
                ),
                ActiveOrders AS (
                    SELECT * FROM AllOrders WHERE Status = 'active'
                )
            SELECT COUNT(*) AS Count FROM ActiveOrders");

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Count"].AsInt64(), Is.EqualTo(3));
    }

    #endregion

    #region CTE with ORDER BY and LIMIT

    [Test]
    public void CteWithOrderByOnMainQueryTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH LargeOrders AS (
                SELECT * FROM Orders WHERE Amount > 100
            )
            SELECT * FROM LargeOrders ORDER BY Amount DESC");

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Amount"].AsDecimal(), Is.EqualTo(300m));
        Assert.That(rows[1]["Amount"].AsDecimal(), Is.EqualTo(200m));
        Assert.That(rows[2]["Amount"].AsDecimal(), Is.EqualTo(150m));
    }

    [Test]
    public void CteWithLimitOnMainQueryTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH LargeOrders AS (
                SELECT * FROM Orders WHERE Amount > 100
            )
            SELECT * FROM LargeOrders ORDER BY Amount DESC LIMIT 2");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Amount"].AsDecimal(), Is.EqualTo(300m));
        Assert.That(rows[1]["Amount"].AsDecimal(), Is.EqualTo(200m));
    }

    #endregion

    #region CTE with GROUP BY

    [Test]
    public void CteWithGroupByInCteQueryTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH UserOrderTotals AS (
                SELECT UserId, SUM(Amount) AS Total
                FROM Orders
                GROUP BY UserId
            )
            SELECT * FROM UserOrderTotals WHERE Total > 200 ORDER BY Total DESC");

        Assert.That(rows, Has.Count.EqualTo(2));
        // User 2: 150 + 300 = 450
        // User 1: 100 + 200 = 300
        Assert.That(rows[0]["Total"].AsDecimal(), Is.EqualTo(450m));
        Assert.That(rows[1]["Total"].AsDecimal(), Is.EqualTo(300m));
    }

    [Test]
    public void CteWithGroupByOnMainQueryTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH ActiveOrders AS (
                SELECT * FROM Orders WHERE Status = 'active'
            )
            SELECT UserId, COUNT(*) AS OrderCount, SUM(Amount) AS Total
            FROM ActiveOrders
            GROUP BY UserId
            ORDER BY Total DESC");

        Assert.That(rows, Has.Count.EqualTo(2));
        // User 2: 1 active order (300)
        // User 1: 2 active orders (100 + 200 = 300)
        // Both have same total, so order might vary
    }

    #endregion

    #region CTE with DISTINCT

    [Test]
    public void CteWithDistinctTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH UniqueStatuses AS (
                SELECT DISTINCT Status FROM Orders
            )
            SELECT * FROM UniqueStatuses ORDER BY Status");

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.Select(r => r["Status"].AsString()), 
            Is.EquivalentTo(new[] { "active", "cancelled", "pending" }));
    }

    #endregion

    #region CTE Scoping Tests

    [Test]
    public void CteIsNotVisibleAfterQueryExecutionTest()
    {
        InsertTestData();

        // First query with CTE
        var rows1 = m_engine.Query(@"
            WITH ActiveOrders AS (
                SELECT * FROM Orders WHERE Status = 'active'
            )
            SELECT COUNT(*) AS Count FROM ActiveOrders");

        Assert.That(rows1[0]["Count"].AsInt64(), Is.EqualTo(3));

        // Second query should not see the CTE from first query
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Query("SELECT * FROM ActiveOrders"));
        
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void DuplicateCteNameThrowsExceptionTest()
    {
        InsertTestData();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Query(@"
                WITH 
                    Orders AS (SELECT * FROM Orders),
                    Orders AS (SELECT * FROM Products)
                SELECT * FROM Orders"));

        Assert.That(ex!.Message, Does.Contain("Duplicate CTE name"));
    }

    #endregion

    #region CTE with Subqueries

    [Test]
    public void CteContainingSubqueryTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH HighValueOrders AS (
                SELECT * FROM Orders 
                WHERE Amount > (SELECT AVG(Amount) FROM Orders)
            )
            SELECT * FROM HighValueOrders ORDER BY Amount");

        // Average is (100+200+150+300+50)/5 = 160
        // Orders > 160: 200, 300
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    #endregion

    #region CTE with Expressions

    [Test]
    public void CteWithExpressionColumnsTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH OrdersWithTax AS (
                SELECT Id, Amount, Amount * 1.1 AS AmountWithTax 
                FROM Orders
            )
            SELECT * FROM OrdersWithTax WHERE AmountWithTax > 200 ORDER BY AmountWithTax DESC");

        Assert.That(rows, Has.Count.EqualTo(2));
        // 300 * 1.1 = 330, 200 * 1.1 = 220
        Assert.That(rows[0]["AmountWithTax"].AsDouble(), Is.EqualTo(330.0).Within(0.01));
    }

    [Test]
    public void CteWithCaseExpressionTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH OrderPriority AS (
                SELECT Id, Amount,
                    CASE 
                        WHEN Amount >= 300 THEN 'High'
                        WHEN Amount >= 150 THEN 'Medium'
                        ELSE 'Low'
                    END AS Priority
                FROM Orders
            )
            SELECT Priority, COUNT(*) AS Count FROM OrderPriority GROUP BY Priority ORDER BY Priority");

        Assert.That(rows, Has.Count.EqualTo(3));
    }

    #endregion

    #region CTE with UNION

    [Test]
    public void CteWithUnionInCteQueryTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH CombinedData AS (
                SELECT Id, Amount AS Value FROM Orders WHERE Status = 'active'
                UNION ALL
                SELECT Id, Price AS Value FROM Products WHERE Category = 'Electronics'
            )
            SELECT COUNT(*) AS Count FROM CombinedData");

        Assert.That(rows, Has.Count.EqualTo(1));
        // 3 active orders + 3 electronics products = 6
        Assert.That(rows[0]["Count"].AsInt64(), Is.EqualTo(6));
    }

    [Test]
    public void CteWithUnionOnMainQueryTest()
    {
        InsertTestData();

        var rows = m_engine.Query(@"
            WITH ActiveOrders AS (
                SELECT Id, Amount FROM Orders WHERE Status = 'active'
            ),
            PendingOrders AS (
                SELECT Id, Amount FROM Orders WHERE Status = 'pending'
            )
            SELECT * FROM ActiveOrders
            UNION ALL
            SELECT * FROM PendingOrders
            ORDER BY Amount DESC");

        Assert.That(rows, Has.Count.EqualTo(4)); // 3 active + 1 pending
    }

    #endregion

    #region Recursive CTE Tests

    [Test]
    public void RecursiveCteSimpleHierarchyTest()
    {
        InsertCategoryHierarchy();

        var rows = m_engine.Query(@"
            WITH RECURSIVE CategoryTree (Id, Name, ParentId, Level) AS (
                SELECT Id, Name, ParentId, 0 AS Level
                FROM Categories
                WHERE ParentId IS NULL
                UNION ALL
                SELECT c.Id, c.Name, c.ParentId, ct.Level + 1
                FROM Categories c
                INNER JOIN CategoryTree ct ON c.ParentId = ct.Id
            )
            SELECT * FROM CategoryTree ORDER BY Level, Name");

        // We have 10 categories total
        Assert.That(rows, Has.Count.EqualTo(10));
        
        // Level 0: Electronics, Clothing (2 root categories)
        var level0 = rows.Where(r => r["Level"].AsInt64() == 0).ToList();
        Assert.That(level0, Has.Count.EqualTo(2));
        
        // Level 1: Computers, Phones, Men, Women (4 categories)
        var level1 = rows.Where(r => r["Level"].AsInt64() == 1).ToList();
        Assert.That(level1, Has.Count.EqualTo(4));
        
        // Level 2: Laptops, Desktops, Smartphones, Shirts (4 categories)
        var level2 = rows.Where(r => r["Level"].AsInt64() == 2).ToList();
        Assert.That(level2, Has.Count.EqualTo(4));
    }

    [Test]
    public void RecursiveCteWithFilterTest()
    {
        InsertCategoryHierarchy();

        var rows = m_engine.Query(@"
            WITH RECURSIVE CategoryTree (Id, Name, ParentId, Level) AS (
                SELECT Id, Name, ParentId, 0 AS Level
                FROM Categories
                WHERE ParentId IS NULL
                UNION ALL
                SELECT c.Id, c.Name, c.ParentId, ct.Level + 1
                FROM Categories c
                INNER JOIN CategoryTree ct ON c.ParentId = ct.Id
            )
            SELECT * FROM CategoryTree WHERE Level <= 1 ORDER BY Level, Name");

        // Level 0 + Level 1 = 2 + 4 = 6 categories
        Assert.That(rows, Has.Count.EqualTo(6));
    }

    [Test]
    public void RecursiveCteCountByLevelTest()
    {
        InsertCategoryHierarchy();

        var rows = m_engine.Query(@"
            WITH RECURSIVE CategoryTree (Id, Name, ParentId, Level) AS (
                SELECT Id, Name, ParentId, 0 AS Level
                FROM Categories
                WHERE ParentId IS NULL
                UNION ALL
                SELECT c.Id, c.Name, c.ParentId, ct.Level + 1
                FROM Categories c
                INNER JOIN CategoryTree ct ON c.ParentId = ct.Id
            )
            SELECT Level, COUNT(*) AS Count FROM CategoryTree GROUP BY Level ORDER BY Level");

        Assert.That(rows, Has.Count.EqualTo(3)); // 3 levels (0, 1, 2)
        Assert.That(rows[0]["Level"].AsInt64(), Is.EqualTo(0));
        Assert.That(rows[0]["Count"].AsInt64(), Is.EqualTo(2)); // 2 root categories
        Assert.That(rows[1]["Level"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["Count"].AsInt64(), Is.EqualTo(4)); // 4 level-1 categories
        Assert.That(rows[2]["Level"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[2]["Count"].AsInt64(), Is.EqualTo(4)); // 4 level-2 categories
    }

    [Test]
    public void RecursiveCteSpecificBranchTest()
    {
        InsertCategoryHierarchy();

        // Get only Electronics subtree
        var rows = m_engine.Query(@"
            WITH RECURSIVE ElectronicsTree (Id, Name, ParentId, Level) AS (
                SELECT Id, Name, ParentId, 0 AS Level
                FROM Categories
                WHERE Name = 'Electronics'
                UNION ALL
                SELECT c.Id, c.Name, c.ParentId, et.Level + 1
                FROM Categories c
                INNER JOIN ElectronicsTree et ON c.ParentId = et.Id
            )
            SELECT * FROM ElectronicsTree ORDER BY Level, Name");

        // Electronics (1) + Computers, Phones (2) + Laptops, Desktops, Smartphones (3) = 6
        Assert.That(rows, Has.Count.EqualTo(6));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Electronics"));
    }

    [Test]
    public void RecursiveCteWithPathTest()
    {
        InsertCategoryHierarchy();

        var rows = m_engine.Query(@"
            WITH RECURSIVE CategoryPath (Id, Name, Path, Level) AS (
                SELECT Id, Name, Name AS Path, 0 AS Level
                FROM Categories
                WHERE ParentId IS NULL
                UNION ALL
                SELECT c.Id, c.Name, cp.Path || ' > ' || c.Name, cp.Level + 1
                FROM Categories c
                INNER JOIN CategoryPath cp ON c.ParentId = cp.Id
            )
            SELECT * FROM CategoryPath WHERE Level = 2 ORDER BY Path");

        Assert.That(rows, Has.Count.EqualTo(4)); // 4 level-2 categories
        
        // Check path format
        var laptopsRow = rows.First(r => r["Name"].AsString() == "Laptops");
        Assert.That(laptopsRow["Path"].AsString(), Is.EqualTo("Electronics > Computers > Laptops"));
    }

    [Test]
    public void RecursiveCteNumberSequenceTest()
    {
        // Use a seed table for the anchor query
        m_engine.Execute("CREATE TABLE Seed (N BIGINT)");
        m_engine.Execute("INSERT INTO Seed (N) VALUES (1)");

        var rows = m_engine.Query(@"
            WITH RECURSIVE NumberSeq (N) AS (
                SELECT N FROM Seed
                UNION ALL
                SELECT N + 1 FROM NumberSeq WHERE N < 10
            )
            SELECT * FROM NumberSeq ORDER BY N");

        Assert.That(rows, Has.Count.EqualTo(10));
        for (int i = 0; i < 10; i++)
        {
            Assert.That(rows[i]["N"].AsInt64(), Is.EqualTo(i + 1));
        }
    }

    [Test]
    public void RecursiveCteMaxDepthExceededThrowsTest()
    {
        // Use a seed table for the anchor query
        m_engine.Execute("CREATE TABLE SeedMax (N BIGINT)");
        m_engine.Execute("INSERT INTO SeedMax (N) VALUES (1)");

        // This will try to recurse more than 1000 times
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Query(@"
                WITH RECURSIVE InfiniteLoop (N) AS (
                    SELECT N FROM SeedMax
                    UNION ALL
                    SELECT N + 1 FROM InfiniteLoop WHERE N < 2000
                )
                SELECT COUNT(*) FROM InfiniteLoop"));

        Assert.That(ex!.Message, Does.Contain("exceeded maximum recursion depth"));
    }

    [Test]
    public void RecursiveCteRequiresUnionAllTest()
    {
        InsertCategoryHierarchy();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Query(@"
                WITH RECURSIVE CategoryTree (Id, Name, ParentId, Level) AS (
                    SELECT Id, Name, ParentId, 0 AS Level
                    FROM Categories
                    WHERE ParentId IS NULL
                    UNION
                    SELECT c.Id, c.Name, c.ParentId, ct.Level + 1
                    FROM Categories c
                    INNER JOIN CategoryTree ct ON c.ParentId = ct.Id
                )
                SELECT * FROM CategoryTree"));

        Assert.That(ex!.Message, Does.Contain("UNION ALL"));
    }

    [Test]
    public void RecursiveCteFibonacciTest()
    {
        // Use a seed table for the anchor query with initial Fibonacci values
        m_engine.Execute("CREATE TABLE FibSeed (N BIGINT, Fib BIGINT, NextFib BIGINT)");
        m_engine.Execute("INSERT INTO FibSeed (N, Fib, NextFib) VALUES (1, 0, 1)");

        var rows = m_engine.Query(@"
            WITH RECURSIVE Fibonacci (N, Fib, NextFib) AS (
                SELECT N, Fib, NextFib FROM FibSeed
                UNION ALL
                SELECT N + 1, NextFib, Fib + NextFib
                FROM Fibonacci
                WHERE N < 10
            )
            SELECT N, Fib FROM Fibonacci ORDER BY N");

        Assert.That(rows, Has.Count.EqualTo(10));
        
        // Check Fibonacci sequence: 0, 1, 1, 2, 3, 5, 8, 13, 21, 34
        var expected = new long[] { 0, 1, 1, 2, 3, 5, 8, 13, 21, 34 };
        for (int i = 0; i < 10; i++)
        {
            Assert.That(rows[i]["Fib"].AsInt64(), Is.EqualTo(expected[i]), 
                $"Fibonacci({i + 1}) should be {expected[i]}");
        }
    }

    [Test]
    public void RecursiveCteMultipleLevelsDeepTest()
    {
        // Create deeper hierarchy
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Gaming Laptops', 7)");  // Level 3
        m_engine.Execute("INSERT INTO Categories (Name, ParentId) VALUES ('Budget Gaming', 11)"); // Level 4

        InsertCategoryHierarchy(); // This will add more categories with IDs continuing from 11

        var rows = m_engine.Query(@"
            WITH RECURSIVE FullTree (Id, Name, ParentId, Level) AS (
                SELECT Id, Name, ParentId, 0 AS Level
                FROM Categories
                WHERE ParentId IS NULL
                UNION ALL
                SELECT c.Id, c.Name, c.ParentId, ft.Level + 1
                FROM Categories c
                INNER JOIN FullTree ft ON c.ParentId = ft.Id
            )
            SELECT MAX(Level) AS MaxLevel FROM FullTree");

        // Should have deep hierarchy
        Assert.That(rows[0]["MaxLevel"].AsInt64(), Is.GreaterThanOrEqualTo(2));
    }

    #endregion
}
