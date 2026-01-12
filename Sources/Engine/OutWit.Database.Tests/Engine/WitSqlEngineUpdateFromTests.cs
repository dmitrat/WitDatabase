using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for UPDATE ... FROM clause support.
/// </summary>
[TestFixture]
public class WitSqlEngineUpdateFromTests
{
    private WitSqlEngine m_engine = null!;

    [SetUp]
    public void SetUp()
    {
        var database = WitDatabase.CreateInMemory();
        m_engine = new WitSqlEngine(database, ownsStore: true);

        // Create test tables
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                CategoryId INT,
                Price DECIMAL(10,2)
            )");

        m_engine.Execute(@"
            CREATE TABLE Categories (
                Id INT PRIMARY KEY,
                Name VARCHAR(50) NOT NULL,
                Discount DECIMAL(5,2) DEFAULT 0
            )");

        m_engine.Execute(@"
            CREATE TABLE PriceUpdates (
                ProductId INT PRIMARY KEY,
                NewPrice DECIMAL(10,2) NOT NULL
            )");

        // Insert test data
        m_engine.Execute("INSERT INTO Categories (Id, Name, Discount) VALUES (1, 'Electronics', 0.10), (2, 'Clothing', 0.20)");
        m_engine.Execute("INSERT INTO Products (Id, Name, CategoryId, Price) VALUES (1, 'Phone', 1, 100.00), (2, 'Laptop', 1, 500.00), (3, 'Shirt', 2, 25.00)");
        m_engine.Execute("INSERT INTO PriceUpdates (ProductId, NewPrice) VALUES (1, 120.00), (3, 30.00)");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #region Basic UPDATE ... FROM

    [Test]
    public void UpdateFromSimpleJoinTest()
    {
        // Update products with prices from PriceUpdates table
        m_engine.Execute(@"
            UPDATE Products
            SET Price = pu.NewPrice
            FROM PriceUpdates AS pu
            WHERE Products.Id = pu.ProductId");

        var rows = m_engine.Query("SELECT Id, Price FROM Products ORDER BY Id");
        
        Assert.That(rows.Count, Is.EqualTo(3));
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(120.00m)); // Updated
        Assert.That(rows[1]["Price"].AsDecimal(), Is.EqualTo(500.00m)); // Not updated
        Assert.That(rows[2]["Price"].AsDecimal(), Is.EqualTo(30.00m));  // Updated
    }

    [Test]
    public void UpdateFromWithAliasTest()
    {
        // Update using alias for target table
        m_engine.Execute(@"
            UPDATE Products AS p
            SET Price = pu.NewPrice
            FROM PriceUpdates AS pu
            WHERE p.Id = pu.ProductId");

        var rows = m_engine.Query("SELECT Id, Price FROM Products WHERE Id = 1");
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(120.00m));
    }

    [Test]
    public void UpdateFromWithExpressionTest()
    {
        // Update with calculated value from joined table
        m_engine.Execute(@"
            UPDATE Products
            SET Price = Price * (1 - c.Discount)
            FROM Categories AS c
            WHERE Products.CategoryId = c.Id");

        var rows = m_engine.Query("SELECT Id, Price FROM Products ORDER BY Id");
        
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(90.00m));  // 100 * 0.9
        Assert.That(rows[1]["Price"].AsDecimal(), Is.EqualTo(450.00m)); // 500 * 0.9
        Assert.That(rows[2]["Price"].AsDecimal(), Is.EqualTo(20.00m));  // 25 * 0.8
    }

    #endregion

    #region UPDATE ... FROM with Multiple Tables

    [Test]
    public void UpdateFromMultipleTablesTest()
    {
        // Join with multiple tables in FROM
        m_engine.Execute(@"
            UPDATE Products
            SET Name = c.Name || ' - ' || Products.Name
            FROM Categories AS c
            WHERE Products.CategoryId = c.Id AND c.Id = 1");

        var rows = m_engine.Query("SELECT Id, Name FROM Products ORDER BY Id");
        
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Electronics - Phone"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Electronics - Laptop"));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("Shirt")); // Not updated
    }

    #endregion

    #region UPDATE ... FROM with RETURNING

    [Test]
    public void UpdateFromWithReturningTest()
    {
        var rows = m_engine.Query(@"
            UPDATE Products
            SET Price = pu.NewPrice
            FROM PriceUpdates AS pu
            WHERE Products.Id = pu.ProductId
            RETURNING Id, Name, Price");

        Assert.That(rows.Count, Is.EqualTo(2));
        
        var ids = rows.Select(r => r["Id"].AsInt64()).OrderBy(x => x).ToList();
        Assert.That(ids, Is.EqualTo(new[] { 1L, 3L }));
    }

    #endregion

    #region UPDATE ... FROM with Subquery

    [Test]
    public void UpdateFromSubqueryTest()
    {
        m_engine.Execute(@"
            UPDATE Products
            SET Price = sq.AvgPrice
            FROM (SELECT CategoryId, AVG(Price) AS AvgPrice FROM Products GROUP BY CategoryId) AS sq
            WHERE Products.CategoryId = sq.CategoryId");

        // All products in same category should have same price now
        var rows = m_engine.Query("SELECT Id, Price FROM Products WHERE CategoryId = 1 ORDER BY Id");
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(rows[1]["Price"].AsDecimal()));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void UpdateFromNoMatchTest()
    {
        // No rows should be updated when FROM doesn't match
        m_engine.Execute(@"
            UPDATE Products
            SET Price = 999
            FROM PriceUpdates AS pu
            WHERE Products.Id = pu.ProductId AND pu.ProductId = 999");

        var rows = m_engine.Query("SELECT Price FROM Products WHERE Id = 1");
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(100.00m)); // Unchanged
    }

    [Test]
    public void UpdateFromSelfJoinTest()
    {
        // Self-join scenario - update based on same table
        m_engine.Execute(@"
            UPDATE Products AS p1
            SET Price = p2.Price
            FROM Products AS p2
            WHERE p1.Id = 3 AND p2.Id = 1");

        var rows = m_engine.Query("SELECT Price FROM Products WHERE Id = 3");
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(100.00m)); // Copied from Id=1
    }

    #endregion
}
