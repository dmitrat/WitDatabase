using OutWit.Database.Parser.Exceptions;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for WitSqlEngine Execute and Query methods.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineExecuteTests : WitSqlEngineTestsBase
{
    #region Execute Tests

    [Test]
    public void ExecuteWithEmptySqlThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() => m_engine.Execute(""));
    }

    [Test]
    public void ExecuteWithWhitespaceSqlThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() => m_engine.Execute("   "));
    }

    [Test]
    public void ExecuteCreateTableReturnsResultTest()
    {
        using var result = m_engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY)");
        
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void ExecuteMultipleStatementsReturnsLastResultTest()
    {
        CreateTestTable();
        
        using var result = m_engine.Execute(@"
            INSERT INTO TestTable (Name, Value) VALUES ('Test', 1);
            SELECT * FROM TestTable");
        
        Assert.That(result.Read(), Is.True);
        Assert.That(result.CurrentRow["Name"].AsString(), Is.EqualTo("Test"));
    }

    [Test]
    public void ExecuteWithParametersBindsValuesTest()
    {
        CreateTestTable();
        
        var parameters = new Dictionary<string, object?>
        {
            { "@name", "TestName" },
            { "@value", 42 }
        };
        
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES (@name, @value)", parameters);
        
        using var result = m_engine.Execute("SELECT * FROM TestTable WHERE Name = @name", parameters);
        Assert.That(result.Read(), Is.True);
        Assert.That(result.CurrentRow["Value"].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void ExecuteWithParametersWithoutAtPrefixBindsValuesTest()
    {
        CreateTestTable();
        
        var parameters = new Dictionary<string, object?>
        {
            { "name", "TestName" }
        };
        
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES (@name, 1)", parameters);
        
        using var result = m_engine.Execute("SELECT * FROM TestTable");
        Assert.That(result.Read(), Is.True);
        Assert.That(result.CurrentRow["Name"].AsString(), Is.EqualTo("TestName"));
    }

    [Test]
    public void ExecuteWithCancellationTokenTest()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        CreateTestTable();
        m_engine.Execute("INSERT INTO TestTable (Name, Value) VALUES ('Test', 1)");
        
        Assert.Throws<OperationCanceledException>(() => 
            m_engine.Execute("SELECT * FROM TestTable; SELECT * FROM TestTable", cancellationToken: cts.Token));
    }

    #endregion

    #region Query Tests

    [Test]
    public void QueryReturnsAllRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void QueryWithParametersReturnsFilteredRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var parameters = new Dictionary<string, object?> { { "@name", "Alice" } };
        var rows = m_engine.Query("SELECT * FROM Users WHERE Name = @name", parameters);
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void QueryReturnsEmptyListWhenNoRowsTest()
    {
        CreateUsersTable();
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Is.Empty);
    }

    #endregion

    #region QueryFirstOrDefault Tests

    [Test]
    public void QueryFirstOrDefaultReturnsFirstRowTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM Users ORDER BY Name");
        
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Name"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void QueryFirstOrDefaultReturnsNullWhenNoRowsTest()
    {
        CreateUsersTable();
        
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM Users");
        
        Assert.That(row, Is.Null);
    }

    #endregion

    #region ExecuteScalar Tests

    [Test]
    public void ExecuteScalarReturnsValueTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM Users");
        
        Assert.That(count.AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void ExecuteScalarReturnsNullWhenNoRowsTest()
    {
        CreateUsersTable();
        
        var result = m_engine.ExecuteScalar("SELECT Name FROM Users");
        
        Assert.That(result.IsNull, Is.True);
    }

    #endregion

    #region ExecuteNonQuery Tests

    [Test]
    public void ExecuteNonQueryReturnsRowsAffectedTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var affected = m_engine.ExecuteNonQuery("UPDATE Users SET Email = 'updated@test.com' WHERE Name = 'Alice'");
        
        Assert.That(affected, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteNonQueryReturnsZeroWhenNoRowsAffectedTest()
    {
        CreateUsersTable();
        
        var affected = m_engine.ExecuteNonQuery("DELETE FROM Users WHERE Name = 'NonExistent'");
        
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion

    #region LastInsertRowId / LastChangesCount Tests

    [Test]
    public void LastInsertRowIdUpdatesAfterInsertTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Test', 'test@test.com')");
        
        Assert.That(m_engine.LastInsertRowId, Is.EqualTo(1));
        
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Test2', 'test2@test.com')");
        
        Assert.That(m_engine.LastInsertRowId, Is.EqualTo(2));
    }

    [Test]
    public void LastChangesCountUpdatesAfterDmlTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("UPDATE Users SET Email = 'updated@test.com'");
        
        Assert.That(m_engine.LastChangesCount, Is.EqualTo(3));
    }

    #endregion
}
