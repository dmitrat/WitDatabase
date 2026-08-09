namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// PINS A DEFECT, NOT CORRECT BEHAVIOUR - known issue 13. Three statements update a row without firing
/// any UPDATE trigger: <c>MERGE … WHEN MATCHED THEN UPDATE</c>, <c>INSERT … ON CONFLICT DO UPDATE</c>,
/// and a foreign key's <c>ON UPDATE CASCADE</c>.
/// </summary>
/// <remarks>
/// <para>
/// Found 2026-08-09 while fixing <c>UPDATE OF</c> (issue 12), by grepping for the SHAPE rather than
/// the site: <c>StatementExecutor</c> calls <c>Database.UpdateRow</c> from six places and only the
/// four in <c>StatementExecutor.Update.cs</c> fire triggers. The other three are
/// <c>StatementExecutor.Merge.cs</c>, the <c>ON CONFLICT</c> branch of
/// <c>StatementExecutor.Insert.cs</c> and the referential cascade in
/// <c>StatementExecutor.Validation.cs</c>. Measured, not read: each case below updates the row and
/// leaves the log empty.
/// </para>
/// <para>
/// <b>What it costs.</b> An audit trigger is the reason people write triggers at all, and it silently
/// misses every row these three statements change. There is no error and nothing in the catalogue to
/// say so.
/// </para>
/// <para>
/// <b>Why it is not fixed here.</b> It is a different piece of work from issue 12 and it has decisions
/// of its own that need answering first: whether a BEFORE trigger may cancel a cascade (which would
/// leave the foreign key dangling), whether an INSTEAD OF trigger stands in for the matched half of a
/// MERGE, and - since 2026-08-09 - what the assigned-column set is for a cascade, which names no
/// columns in any SET clause the user wrote. Each case says what the fix must invert it to.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class TriggerlessWritePathsTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE Log (Id BIGINT PRIMARY KEY AUTOINCREMENT, Note VARCHAR(50))");
    }

    #endregion

    #region Tests

    /// <summary>
    /// PINS A DEFECT. The fix must make this <c>1</c>: the matched half of a MERGE is an update.
    /// </summary>
    [Test]
    public void MergeUpdatesARowAndFiresNoTriggerTest()
    {
        m_engine.Execute("CREATE TABLE Target (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute("CREATE TABLE Source (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute("INSERT INTO Target (Id, V) VALUES (1, 1)");
        m_engine.Execute("INSERT INTO Source (Id, V) VALUES (1, 2)");

        CreateUpdateTriggerOn("Target");

        m_engine.Execute(@"
            MERGE INTO Target AS t USING Source AS s ON t.Id = s.Id
            WHEN MATCHED THEN UPDATE SET V = s.V");

        Assert.That(ValueOf("Target"), Is.EqualTo(2), "the control: the row really was updated");

        Assert.That(LogCount(), Is.EqualTo(0),
            "PINS A DEFECT: MERGE updated the row and the AFTER UPDATE trigger did not fire");
    }

    /// <summary>
    /// PINS A DEFECT. The fix must make this <c>1</c>: <c>DO UPDATE</c> is an update.
    /// </summary>
    [Test]
    public void OnConflictDoUpdateFiresNoTriggerTest()
    {
        m_engine.Execute("CREATE TABLE Target (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute("INSERT INTO Target (Id, V) VALUES (1, 1)");

        CreateUpdateTriggerOn("Target");

        m_engine.Execute("INSERT INTO Target (Id, V) VALUES (1, 5) ON CONFLICT (Id) DO UPDATE SET V = 5");

        Assert.That(ValueOf("Target"), Is.EqualTo(5), "the control: the row really was updated");

        Assert.That(LogCount(), Is.EqualTo(0),
            "PINS A DEFECT: ON CONFLICT DO UPDATE updated the row and the trigger did not fire");
    }

    /// <summary>
    /// PINS A DEFECT. The fix must make this <c>1</c>: the child row changed, so a trigger on the CHILD
    /// table should see it. This is the one whose fix needs a decision first - a cascade names no
    /// columns in any SET clause a user wrote, so the assigned-column set for <c>UPDATE OF</c> has to
    /// be worked out (the foreign key's own columns are the obvious answer).
    /// </summary>
    [Test]
    public void ACascadedUpdateFiresNoTriggerOnTheChildTest()
    {
        m_engine.Execute("CREATE TABLE Parent (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute(
            "CREATE TABLE Child (Id BIGINT PRIMARY KEY, ParentId BIGINT REFERENCES Parent(Id) ON UPDATE CASCADE)");
        m_engine.Execute("INSERT INTO Parent (Id, V) VALUES (1, 1)");
        m_engine.Execute("INSERT INTO Child (Id, ParentId) VALUES (1, 1)");

        CreateUpdateTriggerOn("Child");

        m_engine.Execute("UPDATE Parent SET Id = 2 WHERE Id = 1");

        Assert.That(m_engine.Query("SELECT ParentId FROM Child WHERE Id = 1")[0][0].AsInt64(), Is.EqualTo(2),
            "the control: the cascade really did rewrite the child row");

        Assert.That(LogCount(), Is.EqualTo(0),
            "PINS A DEFECT: the child row was rewritten and its AFTER UPDATE trigger did not fire");
    }

    #endregion

    #region Helpers

    private void CreateUpdateTriggerOn(string table)
    {
        m_engine.Execute($@"
            CREATE TRIGGER T AFTER UPDATE ON {table} FOR EACH ROW
            BEGIN
                INSERT INTO Log (Note) VALUES ('fired');
            END");

        // The control that makes every "did not fire" above mean something: the same trigger DOES fire
        // for an ordinary UPDATE of the same table, so it is live and correctly declared.
        m_engine.Execute($"UPDATE {table} SET Id = Id");

        Assert.That(LogCount(), Is.EqualTo(1), "the trigger has to be live before anything is pinned");

        m_engine.Execute("DELETE FROM Log");
    }

    private int LogCount() => m_engine.Query("SELECT Id FROM Log").Count;

    private long ValueOf(string table) => m_engine.Query($"SELECT V FROM {table} WHERE Id = 1")[0][0].AsInt64();

    #endregion
}
