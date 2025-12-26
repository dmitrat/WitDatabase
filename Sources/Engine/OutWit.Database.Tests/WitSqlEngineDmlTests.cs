namespace OutWit.Database.Tests;

/// <summary>
/// Tests for WitSqlEngine DML operations (INSERT, UPDATE, DELETE).
/// </summary>
[TestFixture]
public sealed class WitSqlEngineDmlTests : WitSqlEngineTestsBase
{
    #region Insert Tests

    [Test]
    public void InsertWithAllColumnsInsertsRowTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[0]["Email"].AsString(), Is.EqualTo("alice@test.com"));
    }

    [Test]
    public void InsertWithPartialColumnsInsertsRowTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Bob')");
        
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM Users");
        
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(row.Value["Email"].IsNull, Is.True);
    }

    [Test]
    public void InsertMultipleRowsInsertsAllRowsTest()
    {
        CreateUsersTable();
        
        m_engine.Execute(@"
            INSERT INTO Users (Name, Email) VALUES 
                ('Alice', 'alice@test.com'),
                ('Bob', 'bob@test.com'),
                ('Charlie', 'charlie@test.com')");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void InsertWithAutoIncrementGeneratesIdTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Test')");
        
        var row = m_engine.QueryFirstOrDefault("SELECT Id FROM Users");
        
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Id"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void InsertFromSelectInsertsRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE UsersCopy (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Email VARCHAR(255)
            )");
        
        m_engine.Execute("INSERT INTO UsersCopy (Name, Email) SELECT Name, Email FROM Users");
        
        var rows = m_engine.Query("SELECT * FROM UsersCopy");
        
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    #endregion

    #region Update Tests

    [Test]
    public void UpdateWithWhereUpdatesMatchingRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("UPDATE Users SET Email = 'newemail@test.com' WHERE Name = 'Alice'");
        
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM Users WHERE Name = 'Alice'");
        
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Email"].AsString(), Is.EqualTo("newemail@test.com"));
    }

    [Test]
    public void UpdateWithoutWhereUpdatesAllRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("UPDATE Users SET Email = 'all@test.com'");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows.All(r => r["Email"].AsString() == "all@test.com"), Is.True);
    }

    [Test]
    public void UpdateMultipleColumnsUpdatesAllColumnsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("UPDATE Users SET Name = 'Updated', Email = 'updated@test.com' WHERE Name = 'Alice'");
        
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM Users WHERE Name = 'Updated'");
        
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Email"].AsString(), Is.EqualTo("updated@test.com"));
    }

    [Test]
    public void UpdateWithExpressionUpdatesCorrectlyTest()
    {
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('Test', 10)");
        
        m_engine.Execute("UPDATE TestTable SET Value = Value * 2");
        
        var row = m_engine.QueryFirstOrDefault("SELECT Value FROM TestTable");
        
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Value"].AsInt64(), Is.EqualTo(20));
    }

    #endregion

    #region Delete Tests

    [Test]
    public void DeleteWithWhereDeletesMatchingRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("DELETE FROM Users WHERE Name = 'Bob'");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.Any(r => r["Name"].AsString() == "Bob"), Is.False);
    }

    [Test]
    public void DeleteWithoutWhereDeletesAllRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("DELETE FROM Users");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void DeleteNonMatchingDoesNotDeleteRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("DELETE FROM Users WHERE Name = 'NonExistent'");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    #endregion
}
