namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Verification of the seven unverified <c>engine-dml</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// As in <see cref="EngineQueryFindingsTests"/>, every test asserts the <b>correct</b> behaviour:
/// a failure confirms the finding, a pass refutes it. Run 2026-07-27 against <c>main</c> at
/// a668f73, where all seven reproduced. Confirmed tests carry <c>[Ignore]</c> with the observed
/// behaviour; remove it when the defect is fixed.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class EngineDmlFindingsTests : WitSqlEngineTestsBase
{
    #region Self-referencing foreign keys are excluded from cascade handling

    [Test]
    public void SelfReferencingForeignKeyCascadesOnDeleteTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Node (
                Id INT PRIMARY KEY,
                ParentId INT,
                FOREIGN KEY (ParentId) REFERENCES Node(Id) ON DELETE CASCADE)");
        m_engine.Execute("INSERT INTO Node (Id, ParentId) VALUES (1, NULL)");
        m_engine.Execute("INSERT INTO Node (Id, ParentId) VALUES (2, 1)");

        m_engine.Execute("DELETE FROM Node WHERE Id = 1");

        Assert.That(Count("Node"), Is.EqualTo(0), "the child row must cascade away with its parent");
    }

    [Test]
    public void SelfReferencingForeignKeyRestrictsOnDeleteTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Node (
                Id INT PRIMARY KEY,
                ParentId INT,
                FOREIGN KEY (ParentId) REFERENCES Node(Id) ON DELETE RESTRICT)");
        m_engine.Execute("INSERT INTO Node (Id, ParentId) VALUES (1, NULL)");
        m_engine.Execute("INSERT INTO Node (Id, ParentId) VALUES (2, 1)");

        Assert.That(() => m_engine.Execute("DELETE FROM Node WHERE Id = 1"), Throws.Exception,
            "row 2 still references row 1, so RESTRICT must reject the delete");
    }

    [Test]
    public void SelfReferencingCycleDoesNotRecurseForeverTest()
    {
        // Enabling self-referencing cascades made the recursion reachable, so this pins the guard
        // that bounds it. Two rows pointing at each other is a cycle; without the in-flight row
        // set the cascade would recurse until the stack ended, and a StackOverflowException cannot
        // be caught - it takes the host process down, as the recursive-trigger finding showed.
        m_engine.Execute(@"
            CREATE TABLE Node (
                Id INT PRIMARY KEY,
                PeerId INT,
                FOREIGN KEY (PeerId) REFERENCES Node(Id) ON DELETE CASCADE)");
        m_engine.Execute("INSERT INTO Node (Id, PeerId) VALUES (1, NULL)");
        m_engine.Execute("INSERT INTO Node (Id, PeerId) VALUES (2, 1)");
        m_engine.Execute("UPDATE Node SET PeerId = 2 WHERE Id = 1");

        Assert.That(() => m_engine.Execute("DELETE FROM Node WHERE Id = 1"), Throws.Nothing,
            "a reference cycle must terminate rather than exhaust the stack");

        Assert.That(Count("Node"), Is.EqualTo(0),
            "both rows are reachable from the deleted one, so the cycle cascades away entirely");
    }

    [Test]
    public void SelfReferencingRowPointingAtItselfCanBeDeletedTest()
    {
        // The exclusion that stops RESTRICT firing on the row being deleted. A row referencing
        // itself is its own child; treating it as one would make it undeletable.
        m_engine.Execute(@"
            CREATE TABLE Node (
                Id INT PRIMARY KEY,
                PeerId INT,
                FOREIGN KEY (PeerId) REFERENCES Node(Id) ON DELETE RESTRICT)");
        m_engine.Execute("INSERT INTO Node (Id, PeerId) VALUES (1, NULL)");
        m_engine.Execute("UPDATE Node SET PeerId = 1 WHERE Id = 1");

        Assert.That(() => m_engine.Execute("DELETE FROM Node WHERE Id = 1"), Throws.Nothing,
            "a row referencing only itself has no other referent to protect");
        Assert.That(Count("Node"), Is.EqualTo(0));
    }

    #endregion

    #region ON UPDATE actions are never applied

    [Test]
    public void OnUpdateCascadeRewritesTheChildKeyTest()
    {
        CreateParentChild("ON UPDATE CASCADE");

        m_engine.Execute("UPDATE P SET Id = 2 WHERE Id = 1");

        var child = m_engine.Query("SELECT PId FROM C WHERE Id = 1")[0][0];
        Assert.That(child.AsInt64(), Is.EqualTo(2), "the child key must follow the parent");
    }

    [Test]
    public void OnUpdateSetNullClearsTheChildKeyTest()
    {
        CreateParentChild("ON UPDATE SET NULL");

        m_engine.Execute("UPDATE P SET Id = 2 WHERE Id = 1");

        var child = m_engine.Query("SELECT PId FROM C WHERE Id = 1")[0][0];
        Assert.That(child.IsNull, Is.True, "the child key must be cleared");
    }

    [Test]
    public void UpdatingAReferencedKeyNeverLeavesAnOrphanTest()
    {
        CreateParentChild("ON UPDATE CASCADE");

        m_engine.Execute("UPDATE P SET Id = 2 WHERE Id = 1");

        var orphans = m_engine.Query(
            "SELECT C.Id FROM C WHERE C.PId IS NOT NULL AND C.PId NOT IN (SELECT Id FROM P)");
        Assert.That(orphans, Is.Empty, "referential integrity must hold after the parent key changes");
    }

    #endregion

    #region UPDATE of an autoincrement primary key

    [Test]
    [Ignore("CONFIRMED 2026-07-27, though the audit states it imprecisely. The row is not " +
            "'unreachable by PK' - it is reachable by the WRONG key. After UPDATE T SET Id = 100 " +
            "WHERE Id = 1: SELECT Id returns 100, WHERE Id = 100 returns nothing, and WHERE Id = 1 " +
            "returns one row that projects Id = 100. The stored column is updated while the key " +
            "index keeps the old rowid, so a query on a value the table no longer contains yields " +
            "a row that contradicts its own predicate. engine-dml, " +
            "Statements/StatementExecutor.Update.cs:891")]
    public void RowStaysReachableAfterItsAutoincrementKeyIsUpdatedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(20))");
        m_engine.Execute("INSERT INTO T (Name) VALUES ('a')");

        m_engine.Execute("UPDATE T SET Id = 100 WHERE Id = 1");

        Assert.Multiple(() =>
        {
            Assert.That(Count("T"), Is.EqualTo(1), "the update must not duplicate or lose the row");
            Assert.That(m_engine.Query("SELECT Name FROM T WHERE Id = 100"), Has.Count.EqualTo(1),
                "the row must be reachable by its new primary key");
            Assert.That(m_engine.Query("SELECT Name FROM T WHERE Id = 1"), Is.Empty,
                "the row must no longer be reachable by its old primary key");
        });
    }

    [Test]
    public void UpdatedAutoincrementKeyStillRejectsADuplicateTest()
    {
        // Passes, and that bounds the damage above: the uniqueness check reads the stored column
        // rather than the stale index, so the desynchronised key does NOT open a hole through which
        // two rows could claim the same primary key. The defect is a lookup failure, not
        // corruption of uniqueness. Kept active as the pin for that boundary.
        m_engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(20))");
        m_engine.Execute("INSERT INTO T (Name) VALUES ('a')");
        m_engine.Execute("UPDATE T SET Id = 100 WHERE Id = 1");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, Name) VALUES (100, 'b')"),
            Throws.Exception, "a second row must not be able to claim the same primary key");
    }

    #endregion

    #region Narrowing numeric writes and unparseable text

    // FIXED by phase 7 (8.0.0): an out-of-range write is refused rather than silently narrowed. The
    // suppression reason that used to live here was a FOSSIL - the [TestCase]s below stopped naming it
    // when they were un-ignored, so the constant sat unreferenced while its text still read "CONFIRMED
    // 2026-07-27: none of these raise". Found by the 2026-08-10 ledger census; anyone grepping the
    // ledger for a diagnosis would have read a sentence that had been false for months.

    [TestCase("SMALLINT", "100000")]
    [TestCase("TINYINT", "999")]
    [TestCase("INT", "9999999999999")]
    public void OutOfRangeNumericWriteIsRejectedTest(string type, string literal)
    {
        m_engine.Execute($"CREATE TABLE T (Id INT PRIMARY KEY, V {type})");

        Assert.That(() => m_engine.Execute($"INSERT INTO T (Id, V) VALUES (1, {literal})"),
            Throws.Exception, $"{literal} does not fit in {type} and must not be silently altered");
    }

    [Test]
    public void UnparseableTextIsNotWrittenAsZeroTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT)");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 'not a number')"),
            Throws.Exception, "unparseable text must be rejected, not stored as 0");
    }

    #endregion

    #region Declared VARCHAR length and DECIMAL precision are enforced

    // FIXED by phase 7, released as 8.0.0 - the DDL path captures MaxLength, Precision and Scale, and
    // the write path enforces them on INSERT and on UPDATE alike. The three markers here were lifted by
    // the 2026-08-10 ledger census, which ran them rather than reading them: all three passed on the
    // first run. They are regression guards now, and the reason the census was worth doing is that the
    // marker text below them had been describing a fixed defect since 8.0.0.

    [Test]
    public void VarcharLengthIsEnforcedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, S VARCHAR(5))");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, S) VALUES (1, 'far too long')"),
            Throws.Exception, "a 12-character string does not fit in VARCHAR(5)");
    }

    [Test]
    public void DecimalPrecisionIsEnforcedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V DECIMAL(5, 2))");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 123456.78)"),
            Throws.Exception, "123456.78 exceeds DECIMAL(5, 2)");
    }

    [Test]
    public void DecimalScaleIsAppliedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V DECIMAL(10, 2))");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 1.23456)");

        Assert.That(m_engine.Query("SELECT V FROM T")[0][0].AsDecimal(), Is.EqualTo(1.23m),
            "the value must be stored at the declared scale");
    }

    #endregion

    #region Bulk UPDATE collapsing many rows onto one value

    /// <summary>
    /// The question <c>StatementExecutorUpdateTests.BulkUpdateDetectsDuplicateInBatchTest</c> deferred
    /// to "an integration test" on 2026-01 and nobody wrote. Asked against a real engine by the
    /// 2026-08-10 ledger census.
    /// </summary>
    /// <remarks>
    /// The suppressed original could not have answered it: its fixture is a mock whose
    /// <c>CreateTableScan</c> always returns the ORIGINAL rows, so the UNIQUE check never sees the
    /// conflict whatever the engine does. The marker described that limitation accurately and then
    /// left the subject unmeasured - which is the shape the census exists to find.
    ///
    /// Three arms plus a control, because the constraint reaches the row by three different routes and
    /// a single arm cannot tell "the engine checks" from "this one route checks". The control is the
    /// same UPDATE on a column with no constraint: it must be ACCEPTED, otherwise a probe reporting
    /// refusal everywhere would prove nothing.
    /// </remarks>
    [TestCase("Email TEXT UNIQUE", null, TestName = "BulkUpdateOntoOneValue_UniqueColumn")]
    [TestCase("Email TEXT", "CREATE UNIQUE INDEX IX_U_Email ON U (Email)", TestName = "BulkUpdateOntoOneValue_UniqueIndex")]
    public void BulkUpdateOntoOneValueIsRefusedTest(string column, string? index)
    {
        m_engine.Execute($"CREATE TABLE U (Id INT PRIMARY KEY, {column})");
        if (index is not null)
            m_engine.Execute(index);

        m_engine.Execute("INSERT INTO U (Id, Email) VALUES (1, 'a@t.com')");
        m_engine.Execute("INSERT INTO U (Id, Email) VALUES (2, 'b@t.com')");
        m_engine.Execute("INSERT INTO U (Id, Email) VALUES (3, 'c@t.com')");

        Assert.That(() => m_engine.Execute("UPDATE U SET Email = 'same@t.com'"),
            Throws.Exception, "collapsing three rows onto one value breaks UNIQUE");

        // And the statement must leave nothing behind - a partial bulk UPDATE would be the worse
        // half of this defect, and only the VALUES can tell the two apart.
        var emails = m_engine.Query("SELECT Email FROM U ORDER BY Id").Select(r => r[0].AsString());
        Assert.That(emails, Is.EqualTo(new[] { "a@t.com", "b@t.com", "c@t.com" }),
            "the refused statement must leave every row as it was");
    }

    [Test]
    public void BulkUpdateOntoOneValueIsRefusedOnThePrimaryKeyTest()
    {
        m_engine.Execute("CREATE TABLE U (Id INT PRIMARY KEY, Email TEXT)");
        m_engine.Execute("INSERT INTO U (Id, Email) VALUES (1, 'a@t.com')");
        m_engine.Execute("INSERT INTO U (Id, Email) VALUES (2, 'b@t.com')");

        Assert.That(() => m_engine.Execute("UPDATE U SET Id = 9"),
            Throws.Exception, "collapsing two rows onto one primary key breaks the key");

        Assert.That(m_engine.Query("SELECT Id FROM U ORDER BY Id").Count, Is.EqualTo(2),
            "the refused statement must leave both rows");
    }

    /// <summary>
    /// The control on the three arms above: without a constraint the very same statement is ACCEPTED.
    /// Without it, "refused" is an outcome no run could fail to produce.
    /// </summary>
    [Test]
    public void BulkUpdateOntoOneValueIsAcceptedWithoutAConstraintTest()
    {
        m_engine.Execute("CREATE TABLE U (Id INT PRIMARY KEY, Email TEXT)");
        m_engine.Execute("INSERT INTO U (Id, Email) VALUES (1, 'a@t.com')");
        m_engine.Execute("INSERT INTO U (Id, Email) VALUES (2, 'b@t.com')");

        m_engine.Execute("UPDATE U SET Email = 'same@t.com'");

        var emails = m_engine.Query("SELECT Email FROM U ORDER BY Id").Select(r => r[0].AsString());
        Assert.That(emails, Is.EqualTo(new[] { "same@t.com", "same@t.com" }),
            "nothing forbids this, so it must go through");
    }

    #endregion

    #region Statement atomicity

    [Test]
    public void FailedMultiRowInsertLeavesNoRowsBehindTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT CHECK (V < 10))");

        Assert.That(
            () => m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 1), (2, 2), (3, 99)"),
            Throws.Exception);

        Assert.That(Count("T"), Is.EqualTo(0),
            "a statement is atomic: the two rows written before the failure must be rolled back");
    }

    [Test]
    public void FailedMultiRowUpdateLeavesNoRowChangedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT CHECK (V < 100))");
        for (int i = 1; i <= 5; i++)
            m_engine.Execute($"INSERT INTO T (Id, V) VALUES ({i}, {i})");

        // Multiplying by 30 pushes the later rows past the CHECK while the first rows still pass.
        Assert.That(() => m_engine.Execute("UPDATE T SET V = V * 30"), Throws.Exception);

        var values = m_engine.Query("SELECT V FROM T ORDER BY Id")
            .Select(r => r[0].AsInt64()).ToArray();
        Assert.That(values, Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }),
            "the whole UPDATE must roll back, leaving every row at its original value");
    }

    #endregion

    #region Recursive triggers

    [Test]
    [Explicit("CONFIRMED 2026-07-27 by running it alone: the test host dies with " +
              "'Test host process crashed : Stack overflow.' A StackOverflowException cannot be " +
              "caught in .NET, so this test cannot run inside the suite without taking every other " +
              "test down with it - hence [Explicit] rather than [Ignore]. There is no depth " +
              "counter anywhere in StatementExecutor.Triggers.cs. Reproduce with: " +
              "dotnet test --filter RecursiveTriggerIsBoundedByADepthLimitTest. " +
              "engine-dml, Statements/StatementExecutor.Triggers.cs:121")]
    public void RecursiveTriggerIsBoundedByADepthLimitTest()
    {
        m_engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, V INT)");
        m_engine.Execute(@"
            CREATE TRIGGER Rec
            AFTER INSERT ON T
            FOR EACH ROW
            BEGIN
                INSERT INTO T (V) VALUES (1);
            END");

        Assert.That(() => m_engine.Execute("INSERT INTO T (V) VALUES (1)"), Throws.Exception,
            "unbounded trigger recursion must surface as a catchable error");
    }

    #endregion

    #region Helpers

    private void CreateParentChild(string onUpdateAction)
    {
        m_engine.Execute("CREATE TABLE P (Id INT PRIMARY KEY, Name VARCHAR(20))");
        m_engine.Execute($@"
            CREATE TABLE C (
                Id INT PRIMARY KEY,
                PId INT,
                FOREIGN KEY (PId) REFERENCES P(Id) {onUpdateAction})");
        m_engine.Execute("INSERT INTO P (Id, Name) VALUES (1, 'p')");
        m_engine.Execute("INSERT INTO C (Id, PId) VALUES (1, 1)");
    }

    private int Count(string table) =>
        (int)m_engine.Query($"SELECT COUNT(*) FROM {table}")[0][0].AsInt64();

    #endregion
}
