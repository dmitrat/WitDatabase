namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// DDL inside an open transaction must do what it says, and must be undone by a rollback.
/// </summary>
/// <remarks>
/// <para>
/// Recorded in <c>AUDIT-2026-07.md</c> as finding 19 and re-measured at head <c>c23b983</c> on
/// 2026-08-01. <c>SchemaCatalog</c> was built over the <c>TransactionalStore</c> itself, so every
/// schema write was an auto-commit <c>Put</c>. A transaction holds the write lock for its lifetime
/// and <c>DatabaseLock</c> refuses same-thread re-entry, so:
/// </para>
/// <code>
/// BEGIN TRANSACTION; CREATE TABLE Z (Id INT); COMMIT
///   -> LockRecursionException: Cannot acquire write lock - current thread already holds write lock
/// </code>
/// <para>
/// <b>And table Z existed afterwards, and was usable.</b> That is the half the record did not state:
/// the caller is told the statement failed while the change is permanent. All five DDL kinds
/// measured did it - <c>CREATE TABLE</c>, <c>CREATE INDEX</c>, <c>CREATE VIEW</c>,
/// <c>DROP TABLE</c>, <c>CREATE SEQUENCE</c> - and <c>ALTER TABLE</c> threw on the <i>read</i> lock
/// instead, from the row scan its migration performs.
/// </para>
/// <para>
/// It is not a niche path. Every migration tool wraps DDL in a transaction, which is what made this
/// a drop-in defect rather than an inconvenience.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class DdlInsideTransactionFindingsTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 10)");
    }

    #endregion

    #region It runs at all

    /// <summary>
    /// Every DDL kind, inside a transaction. All five threw before the fix.
    /// </summary>
    [TestCase("CREATE TABLE Z (Id INT)", TestName = "CREATE TABLE inside a transaction")]
    [TestCase("CREATE INDEX IX ON T (V)", TestName = "CREATE INDEX inside a transaction")]
    [TestCase("CREATE VIEW VW AS SELECT Id FROM T", TestName = "CREATE VIEW inside a transaction")]
    [TestCase("CREATE SEQUENCE SQ", TestName = "CREATE SEQUENCE inside a transaction")]
    [TestCase("DROP TABLE T", TestName = "DROP TABLE inside a transaction")]
    [TestCase("ALTER TABLE T ADD COLUMN Q INT", TestName = "ALTER TABLE inside a transaction")]
    [TestCase("CREATE TRIGGER TR AFTER INSERT ON T FOR EACH ROW BEGIN SELECT 1; END",
        TestName = "CREATE TRIGGER inside a transaction")]
    public void DdlRunsInsideATransactionTest(string ddl)
    {
        m_engine.Execute("BEGIN TRANSACTION");

        Assert.That(() => m_engine.Execute(ddl), Throws.Nothing,
            "DDL inside a transaction took the write lock the transaction was already holding");

        Assert.That(() => m_engine.Execute("COMMIT"), Throws.Nothing);
    }

    /// <summary>
    /// And what it created is real after the commit, not merely un-thrown.
    /// </summary>
    [Test]
    public void WhatDdlCreatedInsideATransactionSurvivesTheCommitTest()
    {
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("CREATE TABLE Z (Id INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Z (Id) VALUES (1)");
        m_engine.Execute("COMMIT");

        Assert.That(m_engine.GetTable("Z"), Is.Not.Null);
        Assert.That(m_engine.Query("SELECT COUNT(*) FROM Z")[0][0].AsInt64(), Is.EqualTo(1));
    }

    #endregion

    #region It is undone by a rollback

    /// <summary>
    /// A rolled-back <c>CREATE</c> must leave nothing behind.
    /// </summary>
    /// <remarks>
    /// This is the half that made a replayed migration die on "already exists": the table survived
    /// the rollback that was supposed to remove it.
    /// </remarks>
    [Test]
    public void CreateTableIsUndoneByRollbackTest()
    {
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("CREATE TABLE Z (Id INT PRIMARY KEY)");
        m_engine.Execute("ROLLBACK");

        Assert.That(m_engine.GetTable("Z"), Is.Null,
            "a rolled-back CREATE TABLE left the table in the catalog");
    }

    /// <summary>
    /// And a rolled-back <c>DROP</c> must give the object back.
    /// </summary>
    /// <remarks>
    /// The worse direction of the same fault: <c>BEGIN; DROP TABLE Orders; ROLLBACK;</c> lost the
    /// table permanently while orphaning its rows.
    /// </remarks>
    [Test]
    public void DropTableIsUndoneByRollbackTest()
    {
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("DROP TABLE T");
        m_engine.Execute("ROLLBACK");

        Assert.That(m_engine.GetTable("T"), Is.Not.Null,
            "a rolled-back DROP TABLE lost the table for good");
        Assert.That(m_engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64(), Is.EqualTo(1),
            "and its rows must come back with it");
    }

    /// <summary>
    /// The same for the objects that live in their own catalog records.
    /// </summary>
    [TestCase("CREATE VIEW VW AS SELECT Id FROM T", "VW", TestName = "a rolled-back CREATE VIEW")]
    [TestCase("CREATE INDEX IX ON T (V)", "IX", TestName = "a rolled-back CREATE INDEX")]
    [TestCase("CREATE SEQUENCE SQ", "SQ", TestName = "a rolled-back CREATE SEQUENCE")]
    public void CreatedObjectIsUndoneByRollbackTest(string ddl, string name)
    {
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute(ddl);
        m_engine.Execute("ROLLBACK");

        Assert.That(SchemaKnows(name), Is.False,
            $"'{name}' survived the rollback of the statement that created it");
    }

    /// <summary>
    /// A column added inside a rolled-back transaction must not stay on the table.
    /// </summary>
    /// <remarks>
    /// `SchemaCatalog.AddColumn` has no duplicate-name check, so a migration replayed after a failed
    /// attempt appended a second identically-named column rather than failing.
    /// </remarks>
    [Test]
    public void AddColumnIsUndoneByRollbackTest()
    {
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("ALTER TABLE T ADD COLUMN Q INT");
        m_engine.Execute("ROLLBACK");

        var columns = m_engine.GetTable("T")!.Columns.Select(c => c.Name).ToArray();

        Assert.That(columns, Does.Not.Contain("Q"),
            "the column survived the rollback, so replaying the migration adds it twice");
    }

    #endregion

    #region The ALTER family sees the rows of its own transaction

    /// <summary>
    /// A migration that writes rows and then reshapes the table must not lose the rows it wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>AUDIT-2026-07.md</c> finding 35, a separate item from finding 19: the ALTER family
    /// migrates rows by scanning them, and eleven of those scans went to the store directly rather
    /// than through the transaction. Recorded consequence - <c>RenameTable</c>, <c>DropColumn</c> and
    /// <c>AlterColumnType</c> could not see rows written in the same transaction, so those rows ended
    /// up committed under the old prefix or the old encoding.
    /// </para>
    /// <para>
    /// It is closed here as a consequence of finding 19's fix rather than as a separate change: the
    /// scans now go through <c>ScanStore</c>, which is the helper that had been added for them and
    /// left unused. Kept as its own test because the two findings are separate, and because "it works
    /// now" and "it works for the reason we think" are different claims - <b>this one asserts the
    /// row, not the absence of an exception.</b>
    /// </para>
    /// </remarks>
    [TestCase("ALTER TABLE T RENAME TO T2", "T2", TestName = "RENAME TABLE")]
    [TestCase("ALTER TABLE T DROP COLUMN W", "T", TestName = "DROP COLUMN")]
    [TestCase("ALTER TABLE T ALTER COLUMN W TYPE VARCHAR(50)", "T", TestName = "ALTER COLUMN TYPE")]
    [TestCase("ALTER TABLE T ADD COLUMN Q INT DEFAULT 7", "T", TestName = "ADD COLUMN")]
    public void AlterKeepsRowsWrittenInTheSameTransactionTest(string ddl, string readFrom)
    {
        m_engine.Execute("ALTER TABLE T ADD COLUMN W INT");

        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("INSERT INTO T (Id, V, W) VALUES (2, 20, 200)");
        m_engine.Execute(ddl);
        m_engine.Execute("COMMIT");

        var rows = m_engine.Query($"SELECT Id, V FROM {readFrom} ORDER BY Id")
            .Select(row => (row[0].AsInt64(), row[1].AsInt64()))
            .ToArray();

        Assert.That(rows, Is.EqualTo(new[] { (1L, 10L), (2L, 20L) }),
            "the row written inside the transaction must survive the reshaping that followed it");
    }

    #endregion

    #region The failure must not leave half the work

    /// <summary>
    /// A DDL statement that fails must leave the catalog as it found it.
    /// </summary>
    /// <remarks>
    /// The general form of what made this finding worse than a refusal: the catalog was mutated in
    /// memory before the store write that threw, so a reported failure had already happened.
    /// </remarks>
    [Test]
    public void FailedDdlLeavesNothingBehindTest()
    {
        m_engine.Execute("CREATE TABLE Z (Id INT PRIMARY KEY)");

        m_engine.Execute("BEGIN TRANSACTION");

        Assert.That(() => m_engine.Execute("CREATE TABLE Z (Id INT PRIMARY KEY)"),
            Throws.Exception, "creating a table that exists must fail");

        m_engine.Execute("ROLLBACK");

        Assert.That(m_engine.GetTable("Z")!.Columns, Has.Count.EqualTo(1),
            "the failed second CREATE must not have touched the first table");
    }

    #endregion

    #region Helpers

    private bool SchemaKnows(string name)
    {
        return m_engine.Catalog.GetView(name) != null
               || m_engine.Catalog.GetIndex(name) != null
               || m_engine.Catalog.GetSequence(name) != null;
    }

    #endregion
}
