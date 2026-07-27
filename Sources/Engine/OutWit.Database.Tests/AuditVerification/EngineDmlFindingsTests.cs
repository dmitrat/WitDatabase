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

    // CONFIRMED 2026-07-27: none of these raise. WitSQL.md documents the exact range of each type
    // (TINYINT -128..127, SMALLINT -32,768..32,767), so the written value is outside the declared
    // contract and is silently altered rather than rejected. engine-dml, Types/WitTypeConverter.cs:576
    private const string NarrowingIgnore =
        "CONFIRMED 2026-07-27: no exception is raised and the out-of-range value is written " +
        "silently. engine-dml, Types/WitTypeConverter.cs:576";

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

    #region Declared VARCHAR length and DECIMAL precision are not enforced

    [Test]
    [Ignore("CONFIRMED 2026-07-27: a 12-character string is accepted into VARCHAR(5). The declared " +
            "length is recorded and never checked. engine-dml, Definitions/DefinitionColumn.cs:148")]
    public void VarcharLengthIsEnforcedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, S VARCHAR(5))");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, S) VALUES (1, 'far too long')"),
            Throws.Exception, "a 12-character string does not fit in VARCHAR(5)");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: 123456.78 is accepted into DECIMAL(5, 2). Declared precision is " +
            "recorded and never checked.")]
    public void DecimalPrecisionIsEnforcedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V DECIMAL(5, 2))");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 123456.78)"),
            Throws.Exception, "123456.78 exceeds DECIMAL(5, 2)");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: stored and read back as 1.23456 rather than rounded to the " +
            "declared scale of 1.23. Scale is recorded and never applied, so a DECIMAL column does " +
            "not round-trip at the precision the schema promises.")]
    public void DecimalScaleIsAppliedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V DECIMAL(10, 2))");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 1.23456)");

        Assert.That(m_engine.Query("SELECT V FROM T")[0][0].AsDecimal(), Is.EqualTo(1.23m),
            "the value must be stored at the declared scale");
    }

    #endregion

    #region Statement atomicity

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the statement throws on the third row and the first two remain " +
            "in the table, so a failed INSERT is half-applied. engine-dml, " +
            "Statements/StatementExecutor.Update.cs:1076")]
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
    [Ignore("CONFIRMED 2026-07-27: the UPDATE throws on a later row but row 1 is left at 30 " +
            "instead of its original 1. Same non-atomicity on the UPDATE path, and here it mutates " +
            "data that already existed rather than only adding rows.")]
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
