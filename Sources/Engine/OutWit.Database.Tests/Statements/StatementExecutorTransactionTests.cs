using NSubstitute;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Statements;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Tests for StatementExecutor transaction control operations.
/// </summary>
[TestFixture]
public sealed class StatementExecutorTransactionTests : StatementExecutorTestsBase
{
    #region Fields

    private StatementExecutor m_executor = null!;

    #endregion

    #region Setup

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        m_executor = new StatementExecutor(m_context);
    }

    #endregion

    #region BEGIN TRANSACTION Tests

    [Test]
    public void BeginTransactionCallsDatabaseBeginTransactionTest()
    {
        // Arrange
        var statement = WitSql.ParseStatement("BEGIN TRANSACTION");

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).BeginTransaction(WitIsolationLevel.ReadCommitted);
    }

    [Test]
    public void BeginTransactionReturnsEmptyResultTest()
    {
        // Arrange
        var statement = WitSql.ParseStatement("BEGIN TRANSACTION");

        // Act
        var result = m_executor.Execute(statement);

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(0));
    }

    [Test]
    public void BeginTransactionUsesPendingIsolationLevelTest()
    {
        // Arrange - set pending isolation level via SET TRANSACTION
        m_database.PendingIsolationLevel = WitIsolationLevel.Serializable;
        var statement = WitSql.ParseStatement("BEGIN TRANSACTION");

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).BeginTransaction(WitIsolationLevel.Serializable);
        Assert.That(m_database.PendingIsolationLevel, Is.Null, "Pending isolation level should be consumed");
    }

    [Test]
    public void BeginTransactionWithoutPendingLevelUsesDefaultTest()
    {
        // Arrange
        m_database.PendingIsolationLevel = null;
        var statement = WitSql.ParseStatement("BEGIN TRANSACTION");

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).BeginTransaction(WitIsolationLevel.ReadCommitted);
    }

    #endregion

    #region COMMIT Tests

    [Test]
    public void CommitCallsDatabaseCommitTest()
    {
        // Arrange
        var statement = WitSql.ParseStatement("COMMIT");

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).Commit();
    }

    [Test]
    public void CommitReturnsEmptyResultTest()
    {
        // Arrange
        var statement = WitSql.ParseStatement("COMMIT");

        // Act
        var result = m_executor.Execute(statement);

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(0));
    }

    #endregion

    #region ROLLBACK Tests

    [Test]
    public void RollbackCallsDatabaseRollbackTest()
    {
        // Arrange
        var statement = WitSql.ParseStatement("ROLLBACK");

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).Rollback();
    }

    [Test]
    public void RollbackReturnsEmptyResultTest()
    {
        // Arrange
        var statement = WitSql.ParseStatement("ROLLBACK");

        // Act
        var result = m_executor.Execute(statement);

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(0));
    }

    [Test]
    public void RollbackToSavepointCallsCorrectMethodTest()
    {
        // Arrange
        var statement = new WitSqlStatementRollback
        {
            SavepointName = "sp1"
        };

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).RollbackToSavepoint("sp1");
        m_database.DidNotReceive().Rollback();
    }

    [Test]
    public void RollbackWithNullSavepointRollsBackEntireTransactionTest()
    {
        // Arrange
        var statement = new WitSqlStatementRollback
        {
            SavepointName = null
        };

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).Rollback();
        m_database.DidNotReceive().RollbackToSavepoint(Arg.Any<string>());
    }

    [Test]
    public void RollbackWithEmptySavepointRollsBackEntireTransactionTest()
    {
        // Arrange
        var statement = new WitSqlStatementRollback
        {
            SavepointName = ""
        };

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).Rollback();
        m_database.DidNotReceive().RollbackToSavepoint(Arg.Any<string>());
    }

    #endregion

    #region SAVEPOINT Tests

    [Test]
    public void SavepointCallsDatabaseCreateSavepointTest()
    {
        // Arrange
        var statement = new WitSqlStatementSavepoint { Name = "sp1" };

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).CreateSavepoint("sp1");
    }

    [Test]
    public void SavepointReturnsEmptyResultTest()
    {
        // Arrange
        var statement = new WitSqlStatementSavepoint { Name = "my_savepoint" };

        // Act
        var result = m_executor.Execute(statement);

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(0));
    }

    [Test]
    public void SavepointWithNullNameThrowsTest()
    {
        // Arrange
        var statement = new WitSqlStatementSavepoint { Name = null! };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => m_executor.Execute(statement));
    }

    [Test]
    public void SavepointWithEmptyNameThrowsTest()
    {
        // Arrange
        var statement = new WitSqlStatementSavepoint { Name = "" };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => m_executor.Execute(statement));
    }

    #endregion

    #region RELEASE SAVEPOINT Tests

    [Test]
    public void ReleaseSavepointCallsDatabaseReleaseSavepointTest()
    {
        // Arrange
        var statement = new WitSqlStatementReleaseSavepoint { Name = "sp1" };

        // Act
        m_executor.Execute(statement);

        // Assert
        m_database.Received(1).ReleaseSavepoint("sp1");
    }

    [Test]
    public void ReleaseSavepointReturnsEmptyResultTest()
    {
        // Arrange
        var statement = new WitSqlStatementReleaseSavepoint { Name = "sp1" };

        // Act
        var result = m_executor.Execute(statement);

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(0));
    }

    [Test]
    public void ReleaseSavepointWithNullNameThrowsTest()
    {
        // Arrange
        var statement = new WitSqlStatementReleaseSavepoint { Name = null! };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => m_executor.Execute(statement));
    }

    [Test]
    public void ReleaseSavepointWithEmptyNameThrowsTest()
    {
        // Arrange
        var statement = new WitSqlStatementReleaseSavepoint { Name = "" };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => m_executor.Execute(statement));
    }

    #endregion

    #region SET TRANSACTION Tests

    [Test]
    public void SetTransactionSetsPendingIsolationLevelSerializableTest()
    {
        // Arrange
        var statement = new WitSqlStatementSetTransaction
        {
            IsolationLevel = IsolationLevelType.Serializable
        };

        // Act
        m_executor.Execute(statement);

        // Assert
        Assert.That(m_database.PendingIsolationLevel, Is.EqualTo(WitIsolationLevel.Serializable));
    }

    [Test]
    public void SetTransactionReturnsEmptyResultTest()
    {
        // Arrange
        var statement = new WitSqlStatementSetTransaction
        {
            IsolationLevel = IsolationLevelType.Serializable
        };

        // Act
        var result = m_executor.Execute(statement);

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(0));
    }

    [Test]
    public void SetTransactionAllIsolationLevelsTest()
    {
        var levels = new[]
        {
            (IsolationLevelType.ReadUncommitted, WitIsolationLevel.ReadUncommitted),
            (IsolationLevelType.ReadCommitted, WitIsolationLevel.ReadCommitted),
            (IsolationLevelType.RepeatableRead, WitIsolationLevel.RepeatableRead),
            (IsolationLevelType.Serializable, WitIsolationLevel.Serializable),
            (IsolationLevelType.Snapshot, WitIsolationLevel.Snapshot)
        };

        foreach (var (parserLevel, expectedCoreLevel) in levels)
        {
            // Arrange
            var statement = new WitSqlStatementSetTransaction
            {
                IsolationLevel = parserLevel
            };
            m_database.PendingIsolationLevel = null;

            // Act
            m_executor.Execute(statement);

            // Assert
            Assert.That(m_database.PendingIsolationLevel, Is.EqualTo(expectedCoreLevel),
                $"Failed for isolation level: {parserLevel}");
        }
    }

    [Test]
    public void SetTransactionFollowedByBeginUsesCorrectLevelTest()
    {
        // Arrange
        var setTransaction = new WitSqlStatementSetTransaction
        {
            IsolationLevel = IsolationLevelType.Snapshot
        };
        var beginTransaction = WitSql.ParseStatement("BEGIN TRANSACTION");

        // Act
        m_executor.Execute(setTransaction);
        m_executor.Execute(beginTransaction);

        // Assert
        m_database.Received(1).BeginTransaction(WitIsolationLevel.Snapshot);
        Assert.That(m_database.PendingIsolationLevel, Is.Null, "Should be consumed");
    }

    #endregion

    #region Transaction Workflow Tests

    [Test]
    public void CompleteTransactionWorkflowTest()
    {
        // Arrange
        var begin = WitSql.ParseStatement("BEGIN TRANSACTION");
        var commit = WitSql.ParseStatement("COMMIT");

        // Act - simulate typical transaction workflow
        m_executor.Execute(begin);
        m_executor.Execute(commit);

        // Assert
        m_database.Received(1).BeginTransaction(WitIsolationLevel.ReadCommitted);
        m_database.Received(1).Commit();
    }

    [Test]
    public void TransactionWithRollbackWorkflowTest()
    {
        // Arrange
        var begin = WitSql.ParseStatement("BEGIN TRANSACTION");
        var rollback = WitSql.ParseStatement("ROLLBACK");

        // Act
        m_executor.Execute(begin);
        m_executor.Execute(rollback);

        // Assert
        m_database.Received(1).BeginTransaction(WitIsolationLevel.ReadCommitted);
        m_database.Received(1).Rollback();
    }

    [Test]
    public void TransactionWithSavepointsWorkflowTest()
    {
        // Arrange
        var begin = WitSql.ParseStatement("BEGIN TRANSACTION");
        var savepoint = new WitSqlStatementSavepoint { Name = "sp1" };
        var rollbackToSavepoint = new WitSqlStatementRollback { SavepointName = "sp1" };
        var commit = WitSql.ParseStatement("COMMIT");

        // Act
        m_executor.Execute(begin);
        m_executor.Execute(savepoint);
        m_executor.Execute(rollbackToSavepoint);
        m_executor.Execute(commit);

        // Assert
        m_database.Received(1).BeginTransaction(WitIsolationLevel.ReadCommitted);
        m_database.Received(1).CreateSavepoint("sp1");
        m_database.Received(1).RollbackToSavepoint("sp1");
        m_database.Received(1).Commit();
    }

    [Test]
    public void MultipleSavepointsWorkflowTest()
    {
        // Arrange
        var begin = WitSql.ParseStatement("BEGIN TRANSACTION");
        var sp1 = new WitSqlStatementSavepoint { Name = "sp1" };
        var sp2 = new WitSqlStatementSavepoint { Name = "sp2" };
        var release = new WitSqlStatementReleaseSavepoint { Name = "sp1" };
        var commit = WitSql.ParseStatement("COMMIT");

        // Act
        m_executor.Execute(begin);
        m_executor.Execute(sp1);
        m_executor.Execute(sp2);
        m_executor.Execute(release);
        m_executor.Execute(commit);

        // Assert
        m_database.Received(1).CreateSavepoint("sp1");
        m_database.Received(1).CreateSavepoint("sp2");
        m_database.Received(1).ReleaseSavepoint("sp1");
        m_database.Received(1).Commit();
    }

    [Test]
    public void SetTransactionThenBeginWithDifferentLevelTest()
    {
        // Arrange - set one level, then set another
        var setFirst = new WitSqlStatementSetTransaction
        {
            IsolationLevel = IsolationLevelType.Serializable
        };
        var setSecond = new WitSqlStatementSetTransaction
        {
            IsolationLevel = IsolationLevelType.ReadUncommitted
        };
        var begin = WitSql.ParseStatement("BEGIN TRANSACTION");

        // Act
        m_executor.Execute(setFirst);
        m_executor.Execute(setSecond); // Override
        m_executor.Execute(begin);

        // Assert - should use the last SET TRANSACTION level
        m_database.Received(1).BeginTransaction(WitIsolationLevel.ReadUncommitted);
    }

    [Test]
    public void MultipleBeginTransactionsConsumesPendingLevelOnlyOnceTest()
    {
        // Arrange
        var setTransaction = new WitSqlStatementSetTransaction
        {
            IsolationLevel = IsolationLevelType.Serializable
        };
        var begin1 = WitSql.ParseStatement("BEGIN TRANSACTION");
        var commit1 = WitSql.ParseStatement("COMMIT");
        var begin2 = WitSql.ParseStatement("BEGIN TRANSACTION");

        // Act
        m_executor.Execute(setTransaction);
        m_executor.Execute(begin1);  // Uses Serializable
        m_executor.Execute(commit1);
        m_executor.Execute(begin2);  // Should use default ReadCommitted

        // Assert
        m_database.Received(1).BeginTransaction(WitIsolationLevel.Serializable);
        m_database.Received(1).BeginTransaction(WitIsolationLevel.ReadCommitted);
    }

    #endregion
}
