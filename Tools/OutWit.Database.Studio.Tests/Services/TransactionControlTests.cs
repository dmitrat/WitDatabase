using System.Data;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Manual transaction control (WS-26), measured against a real database in every case: the question
/// "did the rollback undo it" is only answerable by reading the rows back.
///
/// Autocommit stays the default. What is new is that a person can turn it off, and that Studio then
/// tells the truth about whose transaction it is - the CONNECTION's, shared by every tab of that
/// database, because a connection can hold exactly one.
/// </summary>
[TestFixture]
public class TransactionControlTests
{
    #region Autocommit

    [Test]
    public async Task AutocommitIsTheDefaultTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        Assert.That(fixture.Database.HasOpenTransaction, Is.False,
            "a session must start in autocommit - a transaction nobody opened is one nobody will close");
        Assert.That(fixture.Database.Isolation, Is.EqualTo(IsolationLevel.ReadCommitted),
            "ReadCommitted is the engine's own default, and the one the design names");
    }

    /// <summary>
    /// The positive control for every rollback case below: without a transaction there is nothing to
    /// undo, so a statement stays applied. Without this, "the row is gone after a rollback" could be
    /// reported by an implementation that never wrote the row at all.
    /// </summary>
    [Test]
    public async Task WithoutATransactionAStatementStaysAppliedTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('autocommit', 'INFO')");

        await fixture.Database.RollbackTransactionAsync();

        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(3),
            "a rollback with no transaction open must not be able to remove anything");
    }

    #endregion

    #region Begin, commit, rollback

    [Test]
    public async Task RollbackUndoesEverythingInTheTransactionTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Database.BeginTransactionAsync();

        Assert.That(fixture.Database.HasOpenTransaction, Is.True);

        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('first', 'INFO')");
        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('second', 'INFO')");

        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(4),
            "inside its own transaction a connection reads its own writes");

        await fixture.Database.RollbackTransactionAsync();

        Assert.That(fixture.Database.HasOpenTransaction, Is.False);
        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(2),
            "both rows had to leave, not one of them");
    }

    [Test]
    public async Task CommitKeepsEverythingInTheTransactionTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Database.BeginTransactionAsync();

        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('kept', 'INFO')");

        await fixture.Database.CommitTransactionAsync();

        Assert.That(fixture.Database.HasOpenTransaction, Is.False);
        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(3));
    }

    [Test]
    public async Task SecondBeginIsRefusedTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Database.BeginTransactionAsync();

        Assert.That(async () => await fixture.Database.BeginTransactionAsync(),
            Throws.InstanceOf<InvalidOperationException>(),
            "one connection, one transaction - the alternative is two owners of one commit");

        await fixture.Database.RollbackTransactionAsync();
    }

    [Test]
    public async Task CommitAndRollbackWithNothingOpenDoNothingTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        // The toolbar reaches both, and the second press of a pair must not throw at a person.
        await fixture.Database.CommitTransactionAsync();
        await fixture.Database.RollbackTransactionAsync();

        Assert.That(fixture.Database.HasOpenTransaction, Is.False);
    }

    [Test]
    public async Task StatementsInsideTheTransactionAreCountedTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('before', 'INFO')");

        Assert.That(fixture.Database.TransactionStatementCount, Is.Zero,
            "a statement outside a transaction is not in one");

        await fixture.Database.BeginTransactionAsync();

        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('inside', 'INFO')");
        await fixture.Database.ExecuteQueryAsync("SELECT * FROM Logs");

        Assert.That(fixture.Database.TransactionStatementCount, Is.EqualTo(2),
            "the indicator says what is at stake, so a read counts as much as a write");

        await fixture.Database.RollbackTransactionAsync();

        Assert.That(fixture.Database.TransactionStatementCount, Is.Zero);
    }

    [Test]
    public async Task OpeningAndClosingRaiseTheSessionsEventTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var raised = 0;
        fixture.Database.TransactionChanged += (_, _) => raised++;

        await fixture.Database.BeginTransactionAsync();
        var afterBegin = raised;

        await fixture.Database.RollbackTransactionAsync();

        Assert.That(afterBegin, Is.GreaterThan(0), "the status bar cannot know without being told");
        Assert.That(raised, Is.GreaterThan(afterBegin));
    }

    [TestCase(IsolationLevel.Serializable)]
    [TestCase(IsolationLevel.RepeatableRead)]
    [TestCase(IsolationLevel.ReadUncommitted)]
    [TestCase(IsolationLevel.Snapshot)]
    public async Task EveryIsolationLevelTheDesignOffersOpensTest(IsolationLevel isolation)
    {
        await using var fixture = await StudioFixture.CreateAsync();

        fixture.Database.Isolation = isolation;

        await fixture.Database.BeginTransactionAsync();

        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('isolated', 'INFO')");

        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(3));

        await fixture.Database.RollbackTransactionAsync();

        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(2),
            $"a transaction at {isolation} still has to be undoable");
    }

    #endregion

    #region The connection going away

    /// <summary>
    /// The artifact is the FILE: the database is reopened afterwards and read from a connection that
    /// knows nothing about the one that wrote it.
    ///
    /// Two claims, and only the second is Studio's. Removing the rollback from <c>CloseAsync</c> left
    /// the row count at 2 - the engine discards an uncommitted transaction when the connection goes,
    /// so the FIRST assertion pins the engine's behaviour and would pass without any of this code.
    /// What does not survive the sabotage is the session's own answer: without the rollback it goes on
    /// reporting a transaction that no longer exists, and the status bar with it.
    /// </summary>
    [Test]
    public async Task ClosingTheConnectionRollsAnOpenTransactionBackTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var session = fixture.Database;

        await session.BeginTransactionAsync();
        await session.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('never committed', 'INFO')");

        await fixture.Connections.CloseAsync(session);

        Assert.That(session.HasOpenTransaction, Is.False,
            "a closed connection holds no transaction, and must not say it does");
        Assert.That(session.TransactionStatementCount, Is.Zero);

        var reopened = await fixture.ConnectAsync();

        Assert.That(await fixture.CountRowsAsync("Logs", reopened), Is.EqualTo(2),
            "a transaction nobody committed must not be committed by the close (the engine's, pinned here)");
    }

    #endregion

    #region The table editor inside someone else's transaction

    /// <summary>
    /// The interaction that had to be designed rather than discovered: the table editor commits its
    /// buffer as one transaction, and a query tab of the same connection may already have one open.
    /// A savepoint is what lets both be true.
    /// </summary>
    [Test]
    public async Task AnAppliedBufferJoinsTheOpenTransactionAndLeavesWithItTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Database.BeginTransactionAsync();

        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('from the query tab', 'INFO')");

        var batch = await fixture.Database.ExecuteBatchAsync(
        [
            SqlStatement.Of("INSERT INTO Logs (Message, Level) VALUES ('from the editor', 'INFO')"),
            SqlStatement.Of("INSERT INTO Logs (Message, Level) VALUES ('from the editor too', 'INFO')")
        ]);

        Assert.That(batch.Committed, Is.True, batch.ErrorMessage);
        Assert.That(fixture.Database.HasOpenTransaction, Is.True,
            "the table editor must not end a transaction it did not open");
        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(5));

        await fixture.Database.RollbackTransactionAsync();

        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(2),
            "everything inside the transaction leaves together, the buffer included");
    }

    [Test]
    public async Task AFailedBufferLeavesTheOpenTransactionIntactTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Database.BeginTransactionAsync();

        await fixture.Database.ExecuteNonQueryAsync(
            "INSERT INTO Logs (Message, Level) VALUES ('written before the buffer', 'INFO')");

        var batch = await fixture.Database.ExecuteBatchAsync(
        [
            SqlStatement.Of("INSERT INTO Logs (Message, Level) VALUES ('first of the buffer', 'INFO')"),
            SqlStatement.Of("INSERT INTO Customers (Id, Name) VALUES (1, 'duplicate key')")
        ]);

        Assert.That(batch.Committed, Is.False);
        Assert.That(batch.FailedIndex, Is.EqualTo(1));
        Assert.That(fixture.Database.HasOpenTransaction, Is.True);

        // 2 seeded + the one written before the buffer. The buffer's own first row went back.
        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(3),
            "the savepoint must remove the buffer's own rows and nothing else");

        await fixture.Database.CommitTransactionAsync();

        Assert.That(await fixture.CountRowsAsync("Logs"), Is.EqualTo(3),
            "and what the user wrote before the buffer is still theirs to commit");
    }

    #endregion
}
