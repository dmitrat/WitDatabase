using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Transactions;

/// <summary>
/// Tests for transaction statement parsing (SS9, SS14.1).
/// Covers: BEGIN, COMMIT, ROLLBACK, SAVEPOINT, RELEASE, SET TRANSACTION ISOLATION LEVEL.
/// </summary>
[TestFixture]
public class TransactionParserTests
{
    #region BEGIN (SS9)

    [Test]
    public void ParseBeginTransactionTest()
    {
        var stmt = WitSql.ParseStatement("BEGIN TRANSACTION");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementBeginTransaction>());
    }

    [Test]
    public void ParseBeginWithoutTransactionKeywordTest()
    {
        var stmt = WitSql.ParseStatement("BEGIN");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementBeginTransaction>());
    }

    [Test]
    public void ParseBeginCaseInsensitiveTest()
    {
        var stmt = WitSql.ParseStatement("begin transaction");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementBeginTransaction>());
    }

    #endregion

    #region COMMIT (SS9)

    [Test]
    public void ParseCommitTest()
    {
        var stmt = WitSql.ParseStatement("COMMIT");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCommit>());
    }

    [Test]
    public void ParseCommitCaseInsensitiveTest()
    {
        var stmt = WitSql.ParseStatement("commit");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCommit>());
    }

    #endregion

    #region ROLLBACK (SS9)

    [Test]
    public void ParseRollbackTest()
    {
        var stmt = WitSql.ParseStatement("ROLLBACK");
        var rollback = (WitSqlStatementRollback)stmt;
        Assert.That(rollback.SavepointName, Is.Null);
    }

    [Test]
    public void ParseRollbackToSavepointTest()
    {
        var stmt = WitSql.ParseStatement("ROLLBACK TO SAVEPOINT sp1");
        var rollback = (WitSqlStatementRollback)stmt;
        Assert.That(rollback.SavepointName, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseRollbackToWithoutSavepointKeywordTest()
    {
        var stmt = WitSql.ParseStatement("ROLLBACK TO sp1");
        var rollback = (WitSqlStatementRollback)stmt;
        Assert.That(rollback.SavepointName, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseRollbackCaseInsensitiveTest()
    {
        var stmt = WitSql.ParseStatement("rollback to savepoint MyPoint");
        var rollback = (WitSqlStatementRollback)stmt;
        Assert.That(rollback.SavepointName, Is.EqualTo("MyPoint"));
    }

    #endregion

    #region SAVEPOINT (SS9)

    [Test]
    public void ParseSavepointTest()
    {
        var stmt = WitSql.ParseStatement("SAVEPOINT sp1");
        var savepoint = (WitSqlStatementSavepoint)stmt;
        Assert.That(savepoint.Name, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseSavepointWithUnderscoreTest()
    {
        var stmt = WitSql.ParseStatement("SAVEPOINT my_savepoint_1");
        var savepoint = (WitSqlStatementSavepoint)stmt;
        Assert.That(savepoint.Name, Is.EqualTo("my_savepoint_1"));
    }

    [Test]
    public void ParseSavepointCaseInsensitiveTest()
    {
        var stmt = WitSql.ParseStatement("savepoint SP1");
        var savepoint = (WitSqlStatementSavepoint)stmt;
        Assert.That(savepoint.Name, Is.EqualTo("SP1"));
    }

    #endregion

    #region RELEASE SAVEPOINT (SS9)

    [Test]
    public void ParseReleaseSavepointTest()
    {
        var stmt = WitSql.ParseStatement("RELEASE SAVEPOINT sp1");
        var release = (WitSqlStatementReleaseSavepoint)stmt;
        Assert.That(release.Name, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseReleaseWithoutSavepointKeywordTest()
    {
        var stmt = WitSql.ParseStatement("RELEASE sp1");
        var release = (WitSqlStatementReleaseSavepoint)stmt;
        Assert.That(release.Name, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseReleaseCaseInsensitiveTest()
    {
        var stmt = WitSql.ParseStatement("release savepoint MyPoint");
        var release = (WitSqlStatementReleaseSavepoint)stmt;
        Assert.That(release.Name, Is.EqualTo("MyPoint"));
    }

    #endregion

    #region SET TRANSACTION ISOLATION LEVEL (SS14.1)

    [Test]
    public void ParseSetTransactionReadUncommittedTest()
    {
        var stmt = WitSql.ParseStatement("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSetTransaction>());
        var setTxn = (WitSqlStatementSetTransaction)stmt;
        Assert.That(setTxn.IsolationLevel, Is.EqualTo(IsolationLevelType.ReadUncommitted));
    }

    [Test]
    public void ParseSetTransactionReadCommittedTest()
    {
        var stmt = WitSql.ParseStatement("SET TRANSACTION ISOLATION LEVEL READ COMMITTED");
        var setTxn = (WitSqlStatementSetTransaction)stmt;
        Assert.That(setTxn.IsolationLevel, Is.EqualTo(IsolationLevelType.ReadCommitted));
    }

    [Test]
    public void ParseSetTransactionRepeatableReadTest()
    {
        var stmt = WitSql.ParseStatement("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ");
        var setTxn = (WitSqlStatementSetTransaction)stmt;
        Assert.That(setTxn.IsolationLevel, Is.EqualTo(IsolationLevelType.RepeatableRead));
    }

    [Test]
    public void ParseSetTransactionSerializableTest()
    {
        var stmt = WitSql.ParseStatement("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE");
        var setTxn = (WitSqlStatementSetTransaction)stmt;
        Assert.That(setTxn.IsolationLevel, Is.EqualTo(IsolationLevelType.Serializable));
    }

    [Test]
    public void ParseSetTransactionSnapshotTest()
    {
        var stmt = WitSql.ParseStatement("SET TRANSACTION ISOLATION LEVEL SNAPSHOT");
        var setTxn = (WitSqlStatementSetTransaction)stmt;
        Assert.That(setTxn.IsolationLevel, Is.EqualTo(IsolationLevelType.Snapshot));
    }

    [Test]
    public void ParseSetTransactionCaseInsensitiveTest()
    {
        var stmt = WitSql.ParseStatement("set transaction isolation level serializable");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSetTransaction>());
    }

    #endregion

    #region Transaction Scenarios

    [Test]
    public void ParseBasicTransactionSequenceTest()
    {
        var statements = WitSql.Parse(@"
            BEGIN TRANSACTION;
            INSERT INTO Users (Name) VALUES ('John');
            COMMIT");
        
        Assert.That(statements, Has.Count.EqualTo(3));
        Assert.That(statements[0], Is.InstanceOf<WitSqlStatementBeginTransaction>());
        Assert.That(statements[1], Is.InstanceOf<WitSqlStatementInsert>());
        Assert.That(statements[2], Is.InstanceOf<WitSqlStatementCommit>());
    }

    [Test]
    public void ParseTransactionWithRollbackTest()
    {
        var statements = WitSql.Parse(@"
            BEGIN;
            UPDATE Accounts SET Balance = Balance - 100 WHERE Id = 1;
            UPDATE Accounts SET Balance = Balance + 100 WHERE Id = 2;
            ROLLBACK");
        
        Assert.That(statements, Has.Count.EqualTo(4));
        Assert.That(statements[3], Is.InstanceOf<WitSqlStatementRollback>());
    }

    [Test]
    public void ParseTransactionWithSavepointsTest()
    {
        var statements = WitSql.Parse(@"
            BEGIN TRANSACTION;
            INSERT INTO Messages (Content) VALUES ('Step 1');
            SAVEPOINT step1;
            INSERT INTO Messages (Content) VALUES ('Step 2');
            SAVEPOINT step2;
            INSERT INTO Messages (Content) VALUES ('Step 3');
            ROLLBACK TO step1;
            COMMIT");
        
        Assert.That(statements, Has.Count.EqualTo(8));
        Assert.That(statements[2], Is.InstanceOf<WitSqlStatementSavepoint>());
        Assert.That(statements[4], Is.InstanceOf<WitSqlStatementSavepoint>());
        Assert.That(statements[6], Is.InstanceOf<WitSqlStatementRollback>());
    }

    [Test]
    public void ParseTransactionWithIsolationLevelTest()
    {
        var statements = WitSql.Parse(@"
            BEGIN TRANSACTION;
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            SELECT * FROM Inventory WHERE ProductId = 1 FOR UPDATE;
            UPDATE Inventory SET Quantity = Quantity - 1 WHERE ProductId = 1;
            COMMIT");
        
        Assert.That(statements, Has.Count.EqualTo(5));
        Assert.That(statements[1], Is.InstanceOf<WitSqlStatementSetTransaction>());
    }

    [Test]
    public void ParseNestedSavepointsTest()
    {
        var statements = WitSql.Parse(@"
            BEGIN;
            SAVEPOINT sp_outer;
            INSERT INTO Items VALUES (1);
            SAVEPOINT sp_inner;
            INSERT INTO Items VALUES (2);
            ROLLBACK TO sp_inner;
            RELEASE SAVEPOINT sp_outer;
            COMMIT");
        
        Assert.That(statements, Has.Count.EqualTo(8));
        
        var outerSp = (WitSqlStatementSavepoint)statements[1];
        var innerSp = (WitSqlStatementSavepoint)statements[3];
        var rollback = (WitSqlStatementRollback)statements[5];
        var release = (WitSqlStatementReleaseSavepoint)statements[6];
        
        Assert.That(outerSp.Name, Is.EqualTo("sp_outer"));
        Assert.That(innerSp.Name, Is.EqualTo("sp_inner"));
        Assert.That(rollback.SavepointName, Is.EqualTo("sp_inner"));
        Assert.That(release.Name, Is.EqualTo("sp_outer"));
    }

    [Test]
    public void ParseTransactionWithStatementsTest()
    {
        var statements = WitSql.Parse(@"
            BEGIN TRANSACTION;
            INSERT INTO Users (Name) VALUES ('John');
            COMMIT");
        
        Assert.That(statements, Has.Count.EqualTo(3));
        Assert.That(statements[0], Is.InstanceOf<WitSqlStatementBeginTransaction>());
        Assert.That(statements[1], Is.InstanceOf<WitSqlStatementInsert>());
        Assert.That(statements[2], Is.InstanceOf<WitSqlStatementCommit>());
    }

    #endregion
}
