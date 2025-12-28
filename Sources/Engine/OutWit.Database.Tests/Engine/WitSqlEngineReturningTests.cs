namespace OutWit.Database.Tests;

/// <summary>
/// Tests for RETURNING clause in INSERT, UPDATE, DELETE statements.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineReturningTests : WitSqlEngineTestsBase
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
        // Create Users table with auto-increment
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Email VARCHAR(255),
                CreatedAt DATETIME
            )");

        // Create Products table
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Price DECIMAL NOT NULL,
                Stock INT DEFAULT 0
            )");
    }

    #endregion

    #region INSERT RETURNING Tests

    [Test]
    public void InsertReturningIdTest()
    {
        var result = m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('John', 'john@test.com') RETURNING Id");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        Assert.That(result.HasRows, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(1));
        Assert.That(result.Columns[0].Name, Is.EqualTo("Id"));
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void InsertReturningAllColumnsTest()
    {
        var result = m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Jane', 'jane@test.com') RETURNING *");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        Assert.That(result.HasRows, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(4)); // Id, Name, Email, CreatedAt
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Jane"));
        Assert.That(rows[0]["Email"].AsString(), Is.EqualTo("jane@test.com"));
    }

    [Test]
    public void InsertReturningMultipleColumnsTest()
    {
        var result = m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com') RETURNING Id, Name, Email");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        Assert.That(result.Columns, Has.Count.EqualTo(3));
        
        var rows = result.ReadAll();
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[0]["Email"].AsString(), Is.EqualTo("bob@test.com"));
    }

    [Test]
    public void InsertReturningWithAliasTest()
    {
        var result = m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com') RETURNING Id AS UserId, Name AS UserName");
        
        Assert.That(result.Columns, Has.Count.EqualTo(2));
        Assert.That(result.Columns[0].Name, Is.EqualTo("UserId"));
        Assert.That(result.Columns[1].Name, Is.EqualTo("UserName"));
        
        var rows = result.ReadAll();
        Assert.That(rows[0]["UserId"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["UserName"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void InsertMultipleRowsReturningTest()
    {
        var result = m_engine.Execute(@"
            INSERT INTO Users (Name, Email) VALUES 
                ('User1', 'user1@test.com'),
                ('User2', 'user2@test.com'),
                ('User3', 'user3@test.com')
            RETURNING Id, Name");
        
        Assert.That(result.RowsAffected, Is.EqualTo(3));
        Assert.That(result.HasRows, Is.True);
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(3));
        
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("User1"));
        
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("User2"));
        
        Assert.That(rows[2]["Id"].AsInt64(), Is.EqualTo(3));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("User3"));
    }

    [Test]
    public void InsertReturningDefaultValueTest()
    {
        var result = m_engine.Execute("INSERT INTO Products (Name, Price) VALUES ('Widget', 29.99) RETURNING Id, Name, Stock");
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Widget"));
        Assert.That(rows[0]["Stock"].AsInt64(), Is.EqualTo(0)); // Default value
    }

    [Test]
    public void InsertWithoutReturningTest()
    {
        var result = m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Test', 'test@test.com')");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        Assert.That(result.HasRows, Is.False);
        Assert.That(result.Columns, Is.Empty);
    }

    #endregion

    #region UPDATE RETURNING Tests

    [Test]
    public void UpdateReturningTest()
    {
        // Insert test data
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('John', 'john@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Jane', 'jane@test.com')");
        
        var result = m_engine.Execute("UPDATE Users SET Email = 'john.updated@test.com' WHERE Name = 'John' RETURNING Id, Name, Email");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        Assert.That(result.HasRows, Is.True);
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("John"));
        Assert.That(rows[0]["Email"].AsString(), Is.EqualTo("john.updated@test.com"));
    }

    [Test]
    public void UpdateReturningAllColumnsTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Gadget', 49.99, 10)");
        
        var result = m_engine.Execute("UPDATE Products SET Price = 39.99, Stock = 15 WHERE Id = 1 RETURNING *");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Gadget"));
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(39.99m));
        Assert.That(rows[0]["Stock"].AsInt64(), Is.EqualTo(15));
    }

    [Test]
    public void UpdateMultipleRowsReturningTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Item1', 10, 5)");
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Item2', 20, 10)");
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Item3', 30, 15)");
        
        var result = m_engine.Execute("UPDATE Products SET Stock = Stock + 100 WHERE Price < 25 RETURNING Id, Name, Stock");
        
        Assert.That(result.RowsAffected, Is.EqualTo(2));
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
        
        // Item1 and Item2 should be updated
        Assert.That(rows.Any(r => r["Name"].AsString() == "Item1" && r["Stock"].AsInt64() == 105), Is.True);
        Assert.That(rows.Any(r => r["Name"].AsString() == "Item2" && r["Stock"].AsInt64() == 110), Is.True);
    }

    [Test]
    public void UpdateNoMatchReturningTest()
    {
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('John', 'john@test.com')");
        
        var result = m_engine.Execute("UPDATE Users SET Email = 'updated@test.com' WHERE Name = 'NotExist' RETURNING Id, Name");
        
        Assert.That(result.RowsAffected, Is.EqualTo(0));
        Assert.That(result.HasRows, Is.True); // Has RETURNING clause
        
        var rows = result.ReadAll();
        Assert.That(rows, Is.Empty);
    }

    #endregion

    #region DELETE RETURNING Tests

    [Test]
    public void DeleteReturningTest()
    {
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('ToDelete', 'delete@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('ToKeep', 'keep@test.com')");
        
        var result = m_engine.Execute("DELETE FROM Users WHERE Name = 'ToDelete' RETURNING Id, Name, Email");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        Assert.That(result.HasRows, Is.True);
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("ToDelete"));
        Assert.That(rows[0]["Email"].AsString(), Is.EqualTo("delete@test.com"));
        
        // Verify row is actually deleted
        var remaining = m_engine.Query("SELECT COUNT(*) AS Count FROM Users");
        Assert.That(remaining[0]["Count"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void DeleteReturningAllColumnsTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('ToRemove', 99.99, 50)");
        
        var result = m_engine.Execute("DELETE FROM Products WHERE Name = 'ToRemove' RETURNING *");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("ToRemove"));
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(99.99m));
        Assert.That(rows[0]["Stock"].AsInt64(), Is.EqualTo(50));
    }

    [Test]
    public void DeleteMultipleRowsReturningTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Cheap1', 5, 100)");
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Cheap2', 8, 200)");
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Expensive', 100, 10)");
        
        var result = m_engine.Execute("DELETE FROM Products WHERE Price < 10 RETURNING Id, Name, Price");
        
        Assert.That(result.RowsAffected, Is.EqualTo(2));
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
        
        Assert.That(rows.Any(r => r["Name"].AsString() == "Cheap1"), Is.True);
        Assert.That(rows.Any(r => r["Name"].AsString() == "Cheap2"), Is.True);
        
        // Verify only expensive item remains
        var remaining = m_engine.Query("SELECT Name FROM Products");
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0]["Name"].AsString(), Is.EqualTo("Expensive"));
    }

    [Test]
    public void DeleteNoMatchReturningTest()
    {
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('John', 'john@test.com')");
        
        var result = m_engine.Execute("DELETE FROM Users WHERE Name = 'NotExist' RETURNING Id, Name");
        
        Assert.That(result.RowsAffected, Is.EqualTo(0));
        Assert.That(result.HasRows, Is.True);
        
        var rows = result.ReadAll();
        Assert.That(rows, Is.Empty);
    }

    #endregion

    #region RETURNING with Expressions Tests

    [Test]
    public void InsertReturningWithExpressionTest()
    {
        var result = m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Widget', 25.00, 10) RETURNING Id, Name, Price * Stock AS TotalValue");
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["TotalValue"].AsDecimal(), Is.EqualTo(250.00m));
    }

    [Test]
    public void UpdateReturningWithExpressionTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Item', 10.00, 20)");
        
        var result = m_engine.Execute("UPDATE Products SET Price = 15.00 WHERE Id = 1 RETURNING Id, Price * Stock AS NewValue");
        
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["NewValue"].AsDecimal(), Is.EqualTo(300.00m)); // 15 * 20
    }

    #endregion

    #region RETURNING Schema Tests

    [Test]
    public void ReturningSchemaTypesTest()
    {
        var result = m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Test', 19.99, 5) RETURNING Id, Name, Price, Stock");
        
        Assert.That(result.Columns, Has.Count.EqualTo(4));
        
        // Verify column types from schema
        Assert.That(result.Columns[0].Name, Is.EqualTo("Id"));
        Assert.That(result.Columns[1].Name, Is.EqualTo("Name"));
        Assert.That(result.Columns[2].Name, Is.EqualTo("Price"));
        Assert.That(result.Columns[3].Name, Is.EqualTo("Stock"));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void InsertReturningUsedInSubsequentQueryTest()
    {
        // Get the generated ID from INSERT RETURNING
        var insertResult = m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('TestUser', 'test@example.com') RETURNING Id");
        var rows = insertResult.ReadAll();
        var userId = rows[0]["Id"].AsInt64();
        
        // Use the ID in a subsequent query
        var selectResult = m_engine.Query($"SELECT * FROM Users WHERE Id = {userId}");
        Assert.That(selectResult, Has.Count.EqualTo(1));
        Assert.That(selectResult[0]["Name"].AsString(), Is.EqualTo("TestUser"));
    }

    [Test]
    public void MultipleReturningOperationsTest()
    {
        // INSERT with RETURNING
        var insertResult = m_engine.Execute("INSERT INTO Products (Name, Price, Stock) VALUES ('Product', 50.00, 100) RETURNING Id");
        var productId = insertResult.ReadAll()[0]["Id"].AsInt64();
        
        // UPDATE with RETURNING
        var updateResult = m_engine.Execute($"UPDATE Products SET Price = 45.00 WHERE Id = {productId} RETURNING Price");
        Assert.That(updateResult.ReadAll()[0]["Price"].AsDecimal(), Is.EqualTo(45.00m));
        
        // DELETE with RETURNING
        var deleteResult = m_engine.Execute($"DELETE FROM Products WHERE Id = {productId} RETURNING Name, Price");
        var deletedRow = deleteResult.ReadAll()[0];
        Assert.That(deletedRow["Name"].AsString(), Is.EqualTo("Product"));
        Assert.That(deletedRow["Price"].AsDecimal(), Is.EqualTo(45.00m));
    }

    #endregion
}
