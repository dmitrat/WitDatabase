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
    [Ignore("Transaction support not fully implemented - lock recursion issue")]
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
    [Ignore("Transaction support not fully implemented - lock recursion issue")]
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
    [Ignore("Transaction support not fully implemented - lock recursion issue")]
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
    [Ignore("Transaction support not fully implemented - lock recursion issue")]
    public void ChangesVisibleWithinTransactionTest()
    {
        CreateUsersTable();
        
        using var handle = m_engine.BeginTransaction();
        
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Test', 'test@test.com')");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        
        m_engine.Commit();
    }

    #endregion
}
