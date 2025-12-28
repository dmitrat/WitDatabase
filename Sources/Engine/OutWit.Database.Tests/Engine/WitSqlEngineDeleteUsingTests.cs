using OutWit.Database.Core.Builder;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for DELETE ... USING clause support.
/// </summary>
[TestFixture]
public class WitSqlEngineDeleteUsingTests
{
    private Engine.WitSqlEngine m_engine = null!;

    [SetUp]
    public void SetUp()
    {
        var database = WitDatabase.CreateInMemory();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);

        // Create test tables
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id INT PRIMARY KEY,
                CustomerId INT NOT NULL,
                Status VARCHAR(20) NOT NULL,
                Amount DECIMAL(10,2)
            )");

        m_engine.Execute(@"
            CREATE TABLE Customers (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                IsActive BOOLEAN DEFAULT TRUE
            )");

        m_engine.Execute(@"
            CREATE TABLE DeleteList (
                OrderId INT PRIMARY KEY
            )");

        // Insert test data
        m_engine.Execute("INSERT INTO Customers (Id, Name, IsActive) VALUES (1, 'Alice', TRUE), (2, 'Bob', FALSE), (3, 'Charlie', TRUE)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId, Status, Amount) VALUES (1, 1, 'shipped', 100.00), (2, 2, 'pending', 200.00), (3, 2, 'shipped', 150.00), (4, 3, 'pending', 50.00)");
        m_engine.Execute("INSERT INTO DeleteList (OrderId) VALUES (2), (4)");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #region Basic DELETE ... USING

    [Test]
    public void DeleteUsingSimpleJoinTest()
    {
        // Delete orders that are in the DeleteList
        m_engine.Execute(@"
            DELETE FROM Orders
            USING DeleteList AS dl
            WHERE Orders.Id = dl.OrderId");

        var rows = m_engine.Query("SELECT Id FROM Orders ORDER BY Id");
        
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void DeleteUsingWithAliasTest()
    {
        // Delete using alias for target table
        m_engine.Execute(@"
            DELETE FROM Orders AS o
            USING DeleteList AS dl
            WHERE o.Id = dl.OrderId");

        var rows = m_engine.Query("SELECT COUNT(*) AS cnt FROM Orders");
        Assert.That(rows[0]["cnt"].AsInt64(), Is.EqualTo(2));
    }

    [Test]
    public void DeleteUsingJoinConditionTest()
    {
        // Delete orders for inactive customers
        m_engine.Execute(@"
            DELETE FROM Orders
            USING Customers AS c
            WHERE Orders.CustomerId = c.Id AND c.IsActive = FALSE");

        var rows = m_engine.Query("SELECT Id FROM Orders ORDER BY Id");
        
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1)); // Alice's order
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(4)); // Charlie's order
    }

    #endregion

    #region DELETE ... USING with Multiple Tables

    [Test]
    public void DeleteUsingMultipleTablesTest()
    {
        // Delete orders that match both conditions
        m_engine.Execute(@"
            DELETE FROM Orders
            USING Customers AS c, DeleteList AS dl
            WHERE Orders.CustomerId = c.Id AND Orders.Id = dl.OrderId AND c.IsActive = FALSE");

        // Only order 2 matches all conditions (inactive customer Bob AND in DeleteList)
        var rows = m_engine.Query("SELECT Id FROM Orders ORDER BY Id");
        
        Assert.That(rows.Count, Is.EqualTo(3));
        var ids = rows.Select(r => r["Id"].AsInt64()).ToList();
        Assert.That(ids, Does.Not.Contain(2L));
        Assert.That(ids, Does.Contain(4L)); // Not deleted because Charlie is active
    }

    #endregion

    #region DELETE ... USING with RETURNING

    [Test]
    public void DeleteUsingWithReturningTest()
    {
        var rows = m_engine.Query(@"
            DELETE FROM Orders
            USING DeleteList AS dl
            WHERE Orders.Id = dl.OrderId
            RETURNING Id, Status, Amount");

        Assert.That(rows.Count, Is.EqualTo(2));
        
        var ids = rows.Select(r => r["Id"].AsInt64()).OrderBy(x => x).ToList();
        Assert.That(ids, Is.EqualTo(new[] { 2L, 4L }));
    }

    #endregion

    #region DELETE ... USING with Subquery

    [Test]
    public void DeleteUsingSubqueryTest()
    {
        m_engine.Execute(@"
            DELETE FROM Orders
            USING (SELECT Id FROM Customers WHERE IsActive = FALSE) AS inactive
            WHERE Orders.CustomerId = inactive.Id");

        var rows = m_engine.Query("SELECT COUNT(*) AS cnt FROM Orders");
        Assert.That(rows[0]["cnt"].AsInt64(), Is.EqualTo(2)); // Only active customers' orders remain
    }

    #endregion

    #region Edge Cases

    [Test]
    public void DeleteUsingNoMatchTest()
    {
        // No rows should be deleted when USING doesn't match
        m_engine.Execute(@"
            DELETE FROM Orders
            USING DeleteList AS dl
            WHERE Orders.Id = dl.OrderId AND dl.OrderId = 999");

        var rows = m_engine.Query("SELECT COUNT(*) AS cnt FROM Orders");
        Assert.That(rows[0]["cnt"].AsInt64(), Is.EqualTo(4)); // All rows remain
    }

    [Test]
    public void DeleteUsingSelfJoinTest()
    {
        // Self-join scenario - delete based on same table
        m_engine.Execute(@"
            DELETE FROM Orders AS o1
            USING Orders AS o2
            WHERE o1.CustomerId = o2.CustomerId AND o1.Id <> o2.Id AND o1.Amount < o2.Amount");

        // Should delete orders with lower amounts when same customer has higher amount order
        var rows = m_engine.Query("SELECT Id, CustomerId, Amount FROM Orders ORDER BY CustomerId, Amount DESC");
        
        // Bob (CustomerId=2) had orders of 200 and 150, so 150 should be deleted
        var bobOrders = rows.Where(r => r["CustomerId"].AsInt64() == 2).ToList();
        Assert.That(bobOrders.Count, Is.EqualTo(1));
        Assert.That(bobOrders[0]["Amount"].AsDecimal(), Is.EqualTo(200.00m));
    }

    [Test]
    public void DeleteUsingDuplicateMatchesTest()
    {
        // When USING produces multiple matches for same row, row should be deleted only once
        m_engine.Execute(@"
            CREATE TABLE MultiMatch (
                OrderId INT
            )");
        m_engine.Execute("INSERT INTO MultiMatch (OrderId) VALUES (1), (1), (1)"); // Same OrderId 3 times

        m_engine.Execute(@"
            DELETE FROM Orders
            USING MultiMatch AS mm
            WHERE Orders.Id = mm.OrderId");

        var rows = m_engine.Query("SELECT COUNT(*) AS cnt FROM Orders");
        Assert.That(rows[0]["cnt"].AsInt64(), Is.EqualTo(3)); // Only 1 order deleted
    }

    #endregion
}
