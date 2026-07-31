using System.Data;
using System.Data.Common;
using System.Transactions;
using NUnit.Framework;
using IsolationLevel = System.Data.IsolationLevel;

namespace OutWit.Database.AdoNet.Tests.AuditVerification;

/// <summary>
/// Verification of the ADO.NET half of the <c>dropin-gaps</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// "Drop-in" is a contract claim, so these tests are deliberately written against the <b>base
/// types</b> - <see cref="DbTransaction"/>, <see cref="DbConnection"/> - rather than against
/// WitDatabase's own classes. That is how EF Core reaches the provider, and it is the difference
/// between a method existing and a method being wired to the contract.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class DropInGapsAdoNetTests
{
    #region Savepoints are not wired to the ADO.NET contract

    [Test]
    // FIXED 2026-07-31 (phase 6). The six savepoint members carry `override` now and
    // SupportsSavepoints answers True. The census that found them - ContractCensusProbeTests - showed
    // the audit had named three of the six: the async trio was shadowed too.
    public void SavepointsAreAdvertisedThroughTheContractTest()
    {
        using var connection = OpenConnection();
        using DbTransaction transaction = connection.BeginTransaction();

        Assert.That(transaction.SupportsSavepoints, Is.True,
            "the provider implements savepoints, so it must advertise them on the contract");
    }

    [Test]
    // FIXED 2026-07-31 (phase 6). Save through the base type reached the base class's virtual, which
    // throws NotSupportedException; the identical call on the concrete type worked, which is exactly
    // why the provider's own tests missed it and EF Core would not have.
    public void RollbackToSavepointThroughTheContractUndoesLaterWorkTest()
    {
        using var connection = OpenConnection();
        Execute(connection, "CREATE TABLE T (Id INT PRIMARY KEY)");

        using DbTransaction transaction = connection.BeginTransaction();
        Execute(connection, "INSERT INTO T (Id) VALUES (1)", transaction);
        transaction.Save("sp");
        Execute(connection, "INSERT INTO T (Id) VALUES (2)", transaction);
        transaction.Rollback("sp");
        transaction.Commit();

        Assert.That(Count(connection, "T"), Is.EqualTo(1),
            "rolling back to the savepoint must undo only the second insert");
    }

    #endregion

    #region Ambient transactions / TransactionScope

    [Test]
    [Ignore("CONFIRMED 2026-07-27: EnlistTransaction throws NotSupportedException - DbConnection's virtual "
            + "is not overridden. dropin-gaps, AdoNet/WitDbConnection.cs:154")]
    public void ConnectionEnlistsInAnAmbientTransactionTest()
    {
        using var scope = new TransactionScope();
        using var connection = OpenConnection();

        Assert.That(() => connection.EnlistTransaction(System.Transactions.Transaction.Current), Throws.Nothing,
            "a drop-in ADO.NET provider must support enlistment in an ambient transaction");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and this is the half that loses data silently: the row survives a "
            + "TransactionScope that was never completed. Because the connection never enlists, the "
            + "write commits independently of the ambient transaction, so any code relying on "
            + "TransactionScope for atomicity across components is silently wrong.")]
    public void AbandonedTransactionScopeRollsBackTheWriteTest()
    {
        using var connection = OpenConnection();
        Execute(connection, "CREATE TABLE T (Id INT PRIMARY KEY)");

        using (var scope = new TransactionScope())
        {
            Execute(connection, "INSERT INTO T (Id) VALUES (1)");
            // Scope is disposed without Complete(), so the write must not survive.
        }

        Assert.That(Count(connection, "T"), Is.EqualTo(0),
            "an incomplete TransactionScope must roll the enlisted work back");
    }

    #endregion

    #region Isolation level requested through BeginTransaction

    [Test]
    public void RequestedIsolationLevelIsReportedByTheTransactionTest()
    {
        // Passes: WitDbTransaction reports back whatever level was requested. Kept as a pin, and as
        // the reason the deeper gap is invisible from the outside - the contract surface looks
        // correct while the engine below runs at ReadCommitted. The engine-side proof is in
        // OutWit.Database.Tests/AuditVerification/DropInGapsEngineTests.
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        Assert.That(transaction.IsolationLevel, Is.EqualTo(IsolationLevel.Serializable));
    }

    #endregion

    #region Helpers

    private static WitDbConnection OpenConnection()
    {
        var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static void Execute(WitDbConnection connection, string sql, DbTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction != null)
            command.Transaction = (WitDbTransaction)transaction;
        command.ExecuteNonQuery();
    }

    private static int Count(WitDbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    #endregion
}
