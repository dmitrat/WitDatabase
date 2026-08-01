namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Renaming or dropping a column leaves expressions that still name it, and the table stops working.
/// </summary>
/// <remarks>
/// <para>
/// Found 2026-07-31 while auditing phase 8, and <b>measured against <c>main</c> at 8.0.0 to be
/// pre-existing</b> - both behave identically there. Recorded rather than fixed, because the fix is
/// a schema-rewrite question rather than a storage one: every stored expression that names the
/// column has to be rewritten or the operation refused, which is the same decision <c>ADD COLUMN …
/// PRIMARY KEY</c> faced in phase 7 and answered by refusing.
/// </para>
/// <para>
/// Storing expressions as trees rather than as text neither caused this nor fixes it - the tree
/// names the old column exactly as the text did. It does make the fix cheaper, since a rename can
/// walk the tree and rewrite the reference instead of doing surgery on SQL text, which is what
/// SQLite has to do.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class ColumnRenameAndDropFindingsTests : WitSqlEngineTestsBase
{
    #region RENAME COLUMN

    [Test]
    [Ignore("CONFIRMED 2026-07-31 by execution, and pre-existing - main at 8.0.0 behaves identically. " +
            "ALTER TABLE T RENAME COLUMN Age TO Years renames the column and leaves the column's CHECK " +
            "condition naming Age. Every subsequent INSERT throws KeyNotFoundException 'Column Age not " +
            "found', so the table cannot be written to at all. INFORMATION_SCHEMA still reports the " +
            "check as '(Age >= 0)'. The rename must rewrite every stored expression that names the " +
            "column, or be refused. engine, Schema/SchemaCatalog.Columns.cs RenameColumn")]
    public void RenamedColumnKeepsItsCheckWorkingTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Age INT CHECK (Age >= 0))");
        m_engine.Execute("ALTER TABLE T RENAME COLUMN Age TO Years");

        m_engine.Execute("INSERT INTO T (Id, Years) VALUES (1, 5)");

        Assert.That(m_engine.Query("SELECT Years FROM T WHERE Id = 1")[0][0].AsInt64(), Is.EqualTo(5),
            "a renamed column must still be writable");
    }

    #endregion

    #region DROP COLUMN

    [Test]
    [Ignore("CONFIRMED 2026-07-31 by execution, and pre-existing - main at 8.0.0 behaves identically. " +
            "ALTER TABLE U DROP COLUMN A succeeds while a table-level CHECK still names A, and every " +
            "subsequent INSERT throws KeyNotFoundException 'Column A not found'. Dropping a column must " +
            "drop the constraints that depend on it, or be refused while they exist - PostgreSQL " +
            "refuses without CASCADE. engine, Schema/SchemaCatalog.Columns.cs DropColumn")]
    public void DroppedColumnTakesTheChecksThatNeedItTest()
    {
        m_engine.Execute("CREATE TABLE U (Id INT PRIMARY KEY, A INT, B INT, CHECK (A > 0))");
        m_engine.Execute("ALTER TABLE U DROP COLUMN A");

        m_engine.Execute("INSERT INTO U (Id, B) VALUES (1, 1)");

        Assert.That(m_engine.Query("SELECT B FROM U WHERE Id = 1")[0][0].AsInt64(), Is.EqualTo(1),
            "a table must still be writable after a column it no longer has is dropped");
    }

    #endregion
}
