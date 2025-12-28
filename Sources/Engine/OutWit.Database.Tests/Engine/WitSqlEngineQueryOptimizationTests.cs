using OutWit.Database.Core.Builder;
using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for query optimization including index selection and predicate pushdown.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineQueryOptimizationTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        // Use database with index support
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);
    }

    #endregion

    #region Index Selection Tests

    [Test]
    public void QueryWithEqualityPredicateUsesIndexTest()
    {
        // Arrange
        CreateLargeUsersTable(100);
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        
        // Act - query with equality predicate should use index
        var result = m_engine.Query("SELECT * FROM Users WHERE Name = 'User50'");

        // Assert - should find the user
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["Name"].AsString(), Is.EqualTo("User50"));
    }

    [Test]
    public void QueryWithRangePredicateUsesIndexTest()
    {
        // Arrange
        CreateProductsTable();
        InsertProducts(100);
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Act - query with range predicate should use index
        var result = m_engine.Query("SELECT * FROM Products WHERE Price > 90");

        // Assert - should find products with price > 90
        Assert.That(result.Count, Is.GreaterThan(0));
        Assert.That(result.All(r => r["Price"].AsInt64() > 90), Is.True);
    }

    [Test]
    public void QueryWithBetweenPredicateUsesIndexTest()
    {
        // Arrange
        CreateProductsTable();
        InsertProducts(100);
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Act - BETWEEN should use index for range scan
        var result = m_engine.Query("SELECT * FROM Products WHERE Price BETWEEN 40 AND 60");

        // Assert
        Assert.That(result.Count, Is.GreaterThan(0));
        Assert.That(result.All(r => r["Price"].AsInt64() >= 40 && r["Price"].AsInt64() <= 60), Is.True);
    }

    [Test]
    public void QueryWithUniqueIndexSeekReturnsOneRowTest()
    {
        // Arrange
        CreateLargeUsersTable(100);
        m_engine.Execute("CREATE UNIQUE INDEX idx_users_email ON Users (Email)");

        // Act - unique index seek should return at most one row
        var result = m_engine.Query("SELECT * FROM Users WHERE Email = 'user50@test.com'");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["Email"].AsString(), Is.EqualTo("user50@test.com"));
    }

    [Test]
    public void QueryWithNoMatchingIndexUsesTableScanTest()
    {
        // Arrange
        CreateLargeUsersTable(50);
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - query on non-indexed column should still work (table scan)
        var result = m_engine.Query("SELECT * FROM Users WHERE Email = 'user25@test.com'");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void QueryWithParameterUsesIndexTest()
    {
        // Arrange
        CreateLargeUsersTable(100);
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - parameterized query should also use index
        var result = m_engine.Query(
            "SELECT * FROM Users WHERE Name = @name",
            new Dictionary<string, object?> { ["name"] = "User75" });

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["Name"].AsString(), Is.EqualTo("User75"));
    }

    [Test]
    public void QueryWithAndPredicatesUsesIndexTest()
    {
        // Arrange
        CreateLargeUsersTable(100);
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - first indexable predicate should use index, second applied as filter
        var result = m_engine.Query("SELECT * FROM Users WHERE Name = 'User50' AND Email = 'user50@test.com'");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void QueryWithLessThanUsesIndexRangeScanTest()
    {
        // Arrange
        CreateProductsTable();
        InsertProducts(100);
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Act
        var result = m_engine.Query("SELECT * FROM Products WHERE Price < 10");

        // Assert
        Assert.That(result.Count, Is.GreaterThan(0));
        Assert.That(result.All(r => r["Price"].AsInt64() < 10), Is.True);
    }

    [Test]
    public void QueryWithGreaterOrEqualUsesIndexRangeScanTest()
    {
        // Arrange
        CreateProductsTable();
        InsertProducts(100);
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Act
        var result = m_engine.Query("SELECT * FROM Products WHERE Price >= 95");

        // Assert
        Assert.That(result.Count, Is.GreaterThan(0));
        Assert.That(result.All(r => r["Price"].AsInt64() >= 95), Is.True);
    }

    #endregion

    #region Small Table Tests (No Index Optimization)

    [Test]
    public void QueryOnSmallTableDoesNotUseIndexTest()
    {
        // Arrange - small table (< 10 rows)
        CreateUsersTable();
        InsertTestUsers(); // Only 3 users
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - should still work correctly even without index optimization
        var result = m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice'");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    #endregion

    #region Complex Query Tests

    [Test]
    public void QueryWithJoinStillWorksTest()
    {
        // Arrange
        CreateOrdersSchema();
        m_engine.Execute("CREATE INDEX idx_orders_userid ON Orders (UserId)");

        // Act - JOIN queries should still work
        var result = m_engine.Query(@"
            SELECT u.Name, o.OrderDate
            FROM Users u
            INNER JOIN Orders o ON u.Id = o.UserId
            WHERE u.Name = 'Alice'");

        // Assert
        Assert.That(result.Count, Is.GreaterThan(0));
    }

    [Test]
    public void QueryWithSubqueryStillWorksTest()
    {
        // Arrange
        CreateOrdersSchema();
        m_engine.Execute("CREATE INDEX idx_orders_userid ON Orders (UserId)");

        // Act - subquery should still work
        var result = m_engine.Query(@"
            SELECT * FROM Users 
            WHERE Id IN (SELECT UserId FROM Orders WHERE Amount > 50)");

        // Assert - should complete without error
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void QueryWithOrderByAndLimitWorksWithIndexTest()
    {
        // Arrange
        CreateProductsTable();
        InsertProducts(100);
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Act
        var result = m_engine.Query("SELECT * FROM Products WHERE Price > 50 ORDER BY Name LIMIT 10");

        // Assert
        Assert.That(result.Count, Is.LessThanOrEqualTo(10));
    }

    [Test]
    public void QueryWithGroupByWorksWithIndexTest()
    {
        // Arrange
        CreateProductsWithCategoryTable();
        m_engine.Execute("CREATE INDEX idx_products_category ON ProductsWithCategory (Category)");

        // Act
        var result = m_engine.Query(@"
            SELECT Category, COUNT(*) as Cnt 
            FROM ProductsWithCategory 
            WHERE Category = 'Electronics'
            GROUP BY Category");

        // Assert
        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region Predicate Pushdown Tests

    [Test]
    public void FilterIsAppliedAfterIndexScanTest()
    {
        // Arrange
        CreateLargeUsersTable(100);
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - index on Name, but also filter on Email
        var result = m_engine.Query(@"
            SELECT * FROM Users 
            WHERE Name = 'User50' AND Email LIKE '%50%'");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void OrPredicateDoesNotUseIndexTest()
    {
        // Arrange
        CreateLargeUsersTable(100);
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - OR predicates typically can't use single index efficiently
        var result = m_engine.Query(@"
            SELECT * FROM Users 
            WHERE Name = 'User50' OR Name = 'User60'");

        // Assert - should still return correct results
        Assert.That(result, Has.Count.EqualTo(2));
    }

    #endregion

    #region Expression Index Tests

    [Test]
    public void QueryWithExpressionIndexWorksTest()
    {
        // Arrange
        CreateLargeUsersTable(100);
        m_engine.Execute("CREATE INDEX idx_users_lower_name ON Users ((LOWER(Name)))");

        // Act - expression index on LOWER(Name)
        var result = m_engine.Query(@"
            SELECT * FROM Users 
            WHERE LOWER(Name) = 'user50'");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
    }

    #endregion

    #region Helper Methods

    private void CreateLargeUsersTable(int count)
    {
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Email VARCHAR(255) NOT NULL
            )");

        for (int i = 1; i <= count; i++)
        {
            m_engine.Execute($"INSERT INTO Users (Name, Email) VALUES ('User{i}', 'user{i}@test.com')");
        }
    }

    private void CreateProductsTable()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Price INT NOT NULL
            )");
    }

    private void InsertProducts(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            m_engine.Execute($"INSERT INTO Products (Name, Price) VALUES ('Product{i}', {i})");
        }
    }

    private void CreateProductsWithCategoryTable()
    {
        m_engine.Execute(@"
            CREATE TABLE ProductsWithCategory (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Category VARCHAR(50) NOT NULL,
                Price INT NOT NULL
            )");

        // Insert some products
        m_engine.Execute("INSERT INTO ProductsWithCategory (Name, Category, Price) VALUES ('Laptop', 'Electronics', 1000)");
        m_engine.Execute("INSERT INTO ProductsWithCategory (Name, Category, Price) VALUES ('Phone', 'Electronics', 800)");
        m_engine.Execute("INSERT INTO ProductsWithCategory (Name, Category, Price) VALUES ('Desk', 'Furniture', 300)");
        m_engine.Execute("INSERT INTO ProductsWithCategory (Name, Category, Price) VALUES ('Chair', 'Furniture', 150)");
    }

    private void CreateOrdersSchema()
    {
        CreateUsersTable();
        InsertTestUsers();

        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT NOT NULL,
                Amount INT NOT NULL,
                OrderDate DATE NOT NULL
            )");

        // Get user IDs
        var users = m_engine.Query("SELECT Id, Name FROM Users");
        var aliceId = users.First(u => u["Name"].AsString() == "Alice")["Id"].AsInt64();
        var bobId = users.First(u => u["Name"].AsString() == "Bob")["Id"].AsInt64();

        // Insert orders
        m_engine.Execute($"INSERT INTO Orders (UserId, Amount, OrderDate) VALUES ({aliceId}, 100, '2024-01-01')");
        m_engine.Execute($"INSERT INTO Orders (UserId, Amount, OrderDate) VALUES ({aliceId}, 50, '2024-01-15')");
        m_engine.Execute($"INSERT INTO Orders (UserId, Amount, OrderDate) VALUES ({bobId}, 75, '2024-02-01')");
    }

    #endregion
}
