namespace OutWit.Database.Tests;

/// <summary>
/// Tests for WitSqlEngine transaction management.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineTransactionTests : WitSqlEngineTestsBase
{
    #region Begin/Commit Tests

    [Test]
    public void BeginTransactionReturnsHandleTest()
    {
        using var handle = m_engine.BeginTransaction();
        
        Assert.That(handle, Is.Not.Null);
    }

    [Test]
    public void CommitPersistsChangesTest()
    {
        CreateUsersTable();
        
        using (var handle = m_engine.BeginTransaction())
        {
            m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Test', 'test@test.com')");
            m_engine.Commit();
        }
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void CommitWithoutTransactionDoesNotThrowTest()
    {
        Assert.DoesNotThrow(() => m_engine.Commit());
    }

    #endregion

    #region Rollback Tests

    [Test]
    public void RollbackDiscardsChangesTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        using (var handle = m_engine.BeginTransaction())
        {
            m_engine.Execute("DELETE FROM Users");
            m_engine.Rollback();
        }
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void RollbackWithoutTransactionDoesNotThrowTest()
    {
        Assert.DoesNotThrow(() => m_engine.Rollback());
    }

    [Test]
    public void DisposeWithoutCommitAutoRollbacksTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        using (var handle = m_engine.BeginTransaction())
        {
            m_engine.Execute("DELETE FROM Users");
            // No Commit() - should auto-rollback on dispose
        }
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    #endregion

    #region Transaction Isolation Tests

    [Test]
    public void ChangesVisibleWithinTransactionTest()
    {
        CreateUsersTable();
        
        using var handle = m_engine.BeginTransaction();
        
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Test', 'test@test.com')");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        
        m_engine.Commit();
    }

    [Test]
    public void MultipleInsertsWithinTransactionVisibleTest()
    {
        CreateUsersTable();
        
        using (var handle = m_engine.BeginTransaction())
        {
            m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
            m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
            
            var rows = m_engine.Query("SELECT * FROM Users");
            Assert.That(rows, Has.Count.EqualTo(2));
            
            m_engine.Commit();
        }
        
        var finalRows = m_engine.Query("SELECT * FROM Users");
        Assert.That(finalRows, Has.Count.EqualTo(2));
    }

    [Test]
    public void UpdateWithinTransactionVisibleTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        using (var handle = m_engine.BeginTransaction())
        {
            m_engine.Execute("UPDATE Users SET Email = 'updated@test.com' WHERE Name = 'Alice'");
            
            var row = m_engine.QueryFirstOrDefault("SELECT Email FROM Users WHERE Name = 'Alice'");
            Assert.That(row, Is.Not.Null);
            Assert.That(row.Value["Email"].AsString(), Is.EqualTo("updated@test.com"));
            
            m_engine.Commit();
        }
    }

    [Test]
    public void DeleteWithinTransactionVisibleTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        using (var handle = m_engine.BeginTransaction())
        {
            m_engine.Execute("DELETE FROM Users WHERE Name = 'Alice'");
            
            var rows = m_engine.Query("SELECT * FROM Users");
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows.Any(r => r["Name"].AsString() == "Alice"), Is.False);
            
            m_engine.Commit();
        }
        
        var finalRows = m_engine.Query("SELECT * FROM Users");
        Assert.That(finalRows, Has.Count.EqualTo(2));
    }

    #endregion

    #region SQL Statement Tests

    [Test]
    public void BeginTransactionSqlStartsTransactionTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Test', 'test@test.com')");
        m_engine.Execute("COMMIT");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void RollbackSqlDiscardsChangesTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("DELETE FROM Users");
        m_engine.Execute("ROLLBACK");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    #endregion

    #region Savepoint Tests

    [Test]
    public void SavepointRollbackPartialChangesTest()
    {
        CreateUsersTable();
        
        using (var handle = m_engine.BeginTransaction())
        {
            m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
            
            m_engine.CreateSavepoint("sp1");
            
            m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
            
            // Verify both visible
            var rowsBeforeRollback = m_engine.Query("SELECT * FROM Users");
            Assert.That(rowsBeforeRollback, Has.Count.EqualTo(2));
            
            // Rollback to savepoint
            m_engine.RollbackToSavepoint("sp1");
            
            // Only Alice should be visible now
            var rowsAfterRollback = m_engine.Query("SELECT * FROM Users");
            Assert.That(rowsAfterRollback, Has.Count.EqualTo(1));
            Assert.That(rowsAfterRollback[0]["Name"].AsString(), Is.EqualTo("Alice"));
            
            m_engine.Commit();
        }
        
        var finalRows = m_engine.Query("SELECT * FROM Users");
        Assert.That(finalRows, Has.Count.EqualTo(1));
    }

    [Test]
    public void SavepointSqlWorksTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("SAVEPOINT sp1");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
        m_engine.Execute("ROLLBACK TO SAVEPOINT sp1");
        m_engine.Execute("COMMIT");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public void BeginTransactionWhileActiveThrowsTest()
    {
        using (var handle = m_engine.BeginTransaction())
        {
            Assert.Throws<InvalidOperationException>(() => m_engine.BeginTransaction());
            m_engine.Rollback();
        }
    }

    [Test]
    public void SavepointWithoutTransactionThrowsTest()
    {
        Assert.Throws<InvalidOperationException>(() => m_engine.CreateSavepoint("sp1"));
    }

    [Test]
    public void RollbackToNonExistentSavepointThrowsTest()
    {
        using (var handle = m_engine.BeginTransaction())
        {
            Assert.Throws<ArgumentException>(() => m_engine.RollbackToSavepoint("nonexistent"));
            m_engine.Rollback();
        }
    }

    #endregion
}
