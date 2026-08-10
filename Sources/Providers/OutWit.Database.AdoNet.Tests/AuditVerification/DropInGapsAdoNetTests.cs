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

    // FIXED 2026-07-31 (phase 6). EnlistTransaction is overridden and the connection enlists as the
    // single resource manager of the ambient transaction.
    [Test]
    public void ConnectionEnlistsInAnAmbientTransactionTest()
    {
        using var scope = new TransactionScope();
        using var connection = OpenConnection();

        Assert.That(() => connection.EnlistTransaction(System.Transactions.Transaction.Current), Throws.Nothing,
            "a drop-in ADO.NET provider must support enlistment in an ambient transaction");
    }

    /// <summary>
    /// FIXED 2026-07-31 (phase 6), and the test had to be corrected before it could be believed.
    /// </summary>
    /// <remarks>
    /// As recorded, this opened the connection BEFORE the scope and then asserted that the write must
    /// roll back. **No provider behaves that way**: enlistment happens at <c>Open</c>, so a connection
    /// opened before the scope began is not part of it - SqlClient included, and its documentation says
    /// so. The recorded finding therefore over-stated the defect and would have failed against SQL
    /// Server too. The write not rolling back in that shape is correct.
    ///
    /// The real defect is the one underneath: the connection never enlisted at all, in any shape, so an
    /// abandoned scope committed regardless. This is the canonical shape - the connection is opened
    /// inside the scope - and it is what a consumer relying on <c>TransactionScope</c> for atomicity
    /// actually writes.
    /// </remarks>
    [Test]
    public void AbandonedTransactionScopeRollsBackTheWriteTest()
    {
        using var setup = OpenSharedConnection();
        Execute(setup, "CREATE TABLE T (Id INT PRIMARY KEY)");

        using (var scope = new TransactionScope())
        {
            using var connection = OpenSharedConnection();
            Execute(connection, "INSERT INTO T (Id) VALUES (1)");
            // Scope is disposed without Complete(), so the write must not survive.
        }

        Assert.That(Count(setup, "T"), Is.EqualTo(0),
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

    /// <summary>
    /// Control, and the half that stops the test above passing vacuously: a scope that IS completed
    /// must commit. A rollback assertion is satisfied just as well by a write that never happened.
    /// </summary>
    [Test]
    public void CompletedTransactionScopeCommitsTheWriteTest()
    {
        using var setup = OpenSharedConnection();
        Execute(setup, "CREATE TABLE T (Id INT PRIMARY KEY)");

        using (var scope = new TransactionScope())
        {
            using var connection = OpenSharedConnection();
            Execute(connection, "INSERT INTO T (Id) VALUES (1)");

            scope.Complete();
        }

        Assert.That(Count(setup, "T"), Is.EqualTo(1),
            "a completed TransactionScope must commit the enlisted work");
    }

    /// <summary>
    /// The limit, refused by name rather than faked. This engine enlists as the single resource manager
    /// of a transaction; a second database in the same scope would need it to promote to a distributed
    /// transaction, and it has no two-phase prepare with which to keep that promise.
    /// </summary>
    /// <remarks>
    /// Refusing here is the point. The alternative - joining anyway and committing independently - is
    /// the defect this whole section is about, one scope wider.
    /// </remarks>
    [Test]
    public void TwoDatabasesInOneScopeAreRefusedRatherThanFakedTest()
    {
        using var scope = new TransactionScope();

        using var first = OpenSharedConnection();

        var second = new WitDbConnection($"Data Source={Path.Combine(m_testDir, "other.witdb")}");

        var refused = Assert.Throws<NotSupportedException>(() => second.Open());

        TestContext.Out.WriteLine($"PROBE  a second database in one scope  ->  {refused!.Message}");

        Assert.That(refused.Message, Does.Contain("one database per TransactionScope"),
            "the refusal must say what the limit is");

        second.Dispose();
    }

    /// <summary>
    /// Probe: the requested isolation level has to be APPLIED, not merely reported. The recorded finding
    /// says the level never reaches the transaction; the test that covers it only asks what
    /// <c>DbTransaction.IsolationLevel</c> answers, which a field would satisfy.
    /// </summary>
    /// <remarks>
    /// The discriminating question is a repeated read. Under <c>Serializable</c> or
    /// <c>RepeatableRead</c>, a read taken twice inside one transaction must return the same rows even
    /// though another connection committed in between; under <c>ReadCommitted</c> it may see the new
    /// row. So the two levels have to DIFFER, and the strict one has to be the stable one - either
    /// outcome on its own could be an accident of the storage engine.
    /// </remarks>
    // FIXED 2026-08-10 by KnownIssues 21 (PR #177): the level lives on the database per connection
    // instead of on the per-Execute context that BEGIN TRANSACTION discarded, and the provider sends it
    // before BEGIN rather than after. Both markers lifted by the 2026-08-10 ledger census.
    // ReadCommitted stays the control: it is ALLOWED to see the row, and does.
    //
    // What this pair does NOT certify, and the census measured it deliberately: a repeated read is
    // stable, but SERIALIZABLE still permits WRITE SKEW - see SerializableRejectsWriteSkewTest in
    // Core.Tests, still suppressed. What the engine provides is snapshot isolation under three names.

    [Test]
    [TestCase(IsolationLevel.ReadCommitted)]
    [TestCase(IsolationLevel.RepeatableRead)]
    [TestCase(IsolationLevel.Serializable)]
    public void RequestedIsolationLevelIsAppliedNotJustReportedTest(IsolationLevel level)
    {
        using var setup = OpenSharedConnection();
        Execute(setup, "CREATE TABLE T (Id INT PRIMARY KEY)");
        Execute(setup, "INSERT INTO T (Id) VALUES (1)");

        using var reader = OpenSharedConnection();
        using DbTransaction transaction = ((DbConnection)reader).BeginTransaction(level);

        // Counted by READING the rows: this engine answers COUNT(*) from a cached per-table counter,
        // and phase 4 published a false catastrophe by trusting it.
        var before = CountByReading(reader, "T");

        // A different connection commits while the transaction above is open.
        using (var writer = OpenSharedConnection())
            Execute(writer, "INSERT INTO T (Id) VALUES (2)");

        var after = CountByReading(reader, "T");

        transaction.Rollback();

        TestContext.Out.WriteLine(
            $"PROBE  {level}: rows seen inside the transaction  ->  before={before}, after={after}");

        Assert.That(before, Is.EqualTo(1), "the transaction did not see the row that was there when it began");

        if (level == IsolationLevel.ReadCommitted)
            return;

        Assert.That(after, Is.EqualTo(before),
            $"{level} must not see a row another connection committed after this transaction began");
    }

    #endregion

    #region Setup/TearDown

    private string m_testDir = null!;

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_dropin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// A connection to a shared, file-backed database. An in-memory one is private to its connection,
    /// so two of them cannot see each other's work - which is exactly what an ambient transaction test
    /// needs them to do.
    /// </summary>
    private WitDbConnection OpenSharedConnection()
    {
        var connection = new WitDbConnection($"Data Source={Path.Combine(m_testDir, "scope.witdb")}");
        connection.Open();
        return connection;
    }

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

    /// <summary>
    /// Counts rows by pulling them, never through <c>COUNT(*)</c>, which this engine answers from a
    /// cached counter.
    /// </summary>
    private static int CountByReading(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id FROM {table}";

        using var reader = command.ExecuteReader();

        var rows = 0;
        while (reader.Read())
            rows++;

        return rows;
    }

    private static int Count(WitDbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    #endregion
}
