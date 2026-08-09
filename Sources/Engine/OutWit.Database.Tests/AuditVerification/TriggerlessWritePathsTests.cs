namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Three statements that update a row fire the table's UPDATE triggers: <c>MERGE … WHEN MATCHED THEN
/// UPDATE</c>, <c>INSERT … ON CONFLICT DO UPDATE</c>, and a foreign key's <c>ON UPDATE CASCADE</c>.
/// Known issue 13 until 2026-08-09, when none of them fired at all - these were pins and they
/// inverted.
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
/// <b>The three decisions it needed, settled with Dmitry on 2026-08-09.</b> A cascade fires BEFORE and
/// AFTER on the child, and a cancellation - or an INSTEAD OF standing in for the write - is an ERROR
/// rather than a skip, because skipping leaves the child pointing at a key that no longer exists and
/// hands a trigger the power to break referential integrity silently. An INSTEAD OF trigger DOES stand
/// in for the matched half of a MERGE and for <c>DO UPDATE</c>, because both are updates and one rule
/// beats two exceptions. And the columns a cascade "names", for <c>UPDATE OF</c>, are the foreign
/// key's own - exactly the ones it rewrites.
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
    /// The matched half of a MERGE is an update, and fires the table's UPDATE triggers.
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

        Assert.That(LogCount(), Is.EqualTo(1),
            "MERGE updated the row, so the AFTER UPDATE trigger fires");
    }

    /// <summary>
    /// <c>DO UPDATE</c> is an update, and fires the table's UPDATE triggers.
    /// </summary>
    [Test]
    public void OnConflictDoUpdateFiresNoTriggerTest()
    {
        m_engine.Execute("CREATE TABLE Target (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute("INSERT INTO Target (Id, V) VALUES (1, 1)");

        CreateUpdateTriggerOn("Target");

        m_engine.Execute("INSERT INTO Target (Id, V) VALUES (1, 5) ON CONFLICT (Id) DO UPDATE SET V = 5");

        Assert.That(ValueOf("Target"), Is.EqualTo(5), "the control: the row really was updated");

        Assert.That(LogCount(), Is.EqualTo(1),
            "ON CONFLICT DO UPDATE updated the row, so the trigger fires");
    }

    /// <summary>
    /// The child row changed, so a trigger on the CHILD table sees it. The columns the cascade
    /// "names" are the foreign key's own - see <see cref="ACascadeOnlyReachesATriggerOnItsOwnColumnsTest"/>.
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

        Assert.That(LogCount(), Is.EqualTo(1),
            "the child row was rewritten, so its AFTER UPDATE trigger fires");
    }

    #endregion

    #region The three decisions

    /// <summary>
    /// DECISION: the columns a cascade "names" are the foreign key's own, so <c>UPDATE OF</c> works
    /// there as it does everywhere else.
    /// </summary>
    [Test]
    public void ACascadeOnlyReachesATriggerOnItsOwnColumnsTest()
    {
        BuildParentAndChild();

        m_engine.Execute(@"
            CREATE TRIGGER TWatched AFTER UPDATE OF ParentId ON Child FOR EACH ROW
            BEGIN INSERT INTO Log (Note) VALUES ('fk'); END");

        m_engine.Execute(@"
            CREATE TRIGGER TOther AFTER UPDATE OF Note ON Child FOR EACH ROW
            BEGIN INSERT INTO Log (Note) VALUES ('other'); END");

        m_engine.Execute("UPDATE Parent SET Id = 2 WHERE Id = 1");

        Assert.That(LogCount(), Is.EqualTo(1),
            "the cascade rewrites ParentId, so only the trigger watching ParentId fires");
    }

    /// <summary>
    /// A cascade whose child BEFORE trigger does not complete fails the whole statement and leaves the
    /// child row alone.
    ///
    /// <para>
    /// <b>This is NOT the decision it was written for, and it is named after what it measures.</b> The
    /// decision is that a CANCELLATION is an error rather than a skip - and a trigger body cannot
    /// cancel from SQL on this engine: <c>ContextTrigger.Cancel</c> is set by the executor, not by
    /// anything a body can write. So the refusal in <c>WriteCascadedChildRow</c> is reachable only
    /// from a host that drives the executor directly, and this case exercises the neighbouring path -
    /// a body that fails - which lands in the same place for the user.
    /// </para>
    /// </summary>
    [Test]
    public void ACascadeWhoseChildTriggerFailsIsRefusedTest()
    {
        BuildParentAndChild();

        // The only way to cancel from a trigger body on this engine is to make it fail, and a BEFORE
        // trigger's failure is what stops the operation.
        m_engine.Execute(@"
            CREATE TRIGGER TStop BEFORE UPDATE ON Child FOR EACH ROW
            BEGIN INSERT INTO Log (Id, Note) VALUES (1, 'first'); INSERT INTO Log (Id, Note) VALUES (1, 'again'); END");

        Assert.That(() => m_engine.Execute("UPDATE Parent SET Id = 2 WHERE Id = 1"),
            Throws.Exception,
            "a cascade that cannot run its child's BEFORE trigger fails the statement");

        Assert.That(m_engine.Query("SELECT ParentId FROM Child WHERE Id = 1")[0][0].AsInt64(), Is.EqualTo(1),
            "and the child row is left alone rather than half-written");
    }

    /// <summary>
    /// DECISION: an INSTEAD OF trigger stands in for the matched half of a MERGE, because that half is
    /// an update and INSTEAD OF exists to replace the write.
    /// </summary>
    [Test]
    public void AnInsteadOfTriggerStandsInForTheMatchedHalfOfAMergeTest()
    {
        m_engine.Execute("CREATE TABLE Target (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute("CREATE TABLE Source (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute("INSERT INTO Target (Id, V) VALUES (1, 1)");
        m_engine.Execute("INSERT INTO Source (Id, V) VALUES (1, 2)");

        m_engine.Execute(@"
            CREATE TRIGGER TInstead INSTEAD OF UPDATE ON Target FOR EACH ROW
            BEGIN INSERT INTO Log (Note) VALUES ('instead'); END");

        m_engine.Execute(@"
            MERGE INTO Target AS t USING Source AS s ON t.Id = s.Id
            WHEN MATCHED THEN UPDATE SET V = s.V");

        Assert.Multiple(() =>
        {
            Assert.That(LogCount(), Is.EqualTo(1), "the trigger ran");
            Assert.That(m_engine.Query("SELECT V FROM Target WHERE Id = 1")[0][0].AsInt64(), Is.EqualTo(1),
                "and it stood IN FOR the write, so the value is unchanged");
        });
    }

    /// <summary>
    /// The control for the whole fixture: an INSERT that does NOT conflict is an insert, and must not
    /// fire an UPDATE trigger. A change that fired UPDATE triggers from the insert path would satisfy
    /// the DO UPDATE case above.
    /// </summary>
    [Test]
    public void AnInsertThatDoesNotConflictFiresNoUpdateTriggerTest()
    {
        m_engine.Execute("CREATE TABLE Target (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute("INSERT INTO Target (Id, V) VALUES (1, 1)");

        CreateUpdateTriggerOn("Target");

        m_engine.Execute("INSERT INTO Target (Id, V) VALUES (2, 2) ON CONFLICT (Id) DO UPDATE SET V = 9");

        Assert.That(LogCount(), Is.EqualTo(0));
    }

    #endregion

    #region Helpers

    private void BuildParentAndChild()
    {
        m_engine.Execute("CREATE TABLE Parent (Id BIGINT PRIMARY KEY, V INT)");
        m_engine.Execute(
            "CREATE TABLE Child (Id BIGINT PRIMARY KEY, ParentId BIGINT REFERENCES Parent(Id) ON UPDATE CASCADE, Note VARCHAR(20))");
        m_engine.Execute("INSERT INTO Parent (Id, V) VALUES (1, 1)");
        m_engine.Execute("INSERT INTO Child (Id, ParentId) VALUES (1, 1)");
    }

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
