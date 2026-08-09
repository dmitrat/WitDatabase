namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// <c>UPDATE OF</c> names the columns a trigger watches, and an update of any other column must not
/// reach it. Known issue 12 until 2026-08-09: the parser read the list, the catalogue stored it, and
/// nothing on the firing path ever consulted it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision this fixture pins.</b> A trigger fires when the statement <b>names</b> a watched
/// column in its <c>SET</c> clause - not when the value changes. That is what SQLite and PostgreSQL
/// both do, and it keeps the answer a property of the statement rather than of the data:
/// <c>SET Watched = Watched</c> fires, and one row of a multi-row update cannot fire while its
/// neighbour does not. <see cref="AssigningTheSameValueStillFiresTest"/> is that decision, and it is
/// the case that tells the two readings apart.
/// </para>
/// <para>
/// <b>Why there is a case per statement shape.</b> <c>UPDATE</c> has four execution paths and each
/// fires triggers itself: the PK-equality fast path, the <c>PK IN (…)</c> batch fast path, the
/// streaming path and the standard iterator path. A single case exercises exactly one of them, which
/// is how a fix reaches one path and misses three - phase 7's rule, and the reason this is not one
/// test with four assertions.
/// </para>
/// <para>
/// <b>The routing was measured, not read off the code</b> (2026-08-09): the four paths were
/// instrumented to print their name, and each shape below took the path its comment claims. The two
/// timing cases took <i>two</i> paths, which is the finding worth keeping - the statement naming an
/// unwatched column goes through the FAST path even though a BEFORE trigger exists, because the guard
/// now asks whether the trigger is reached rather than whether it exists.
/// </para>
/// <para>
/// <b>Power measured the other way on the same day.</b> With <c>WatchesAnyOf</c> returned to
/// <c>true</c> - the defect restored - <b>8 cases went red</b>: all four paths, both timings, the
/// several-columns case and the replacement of the old pin in
/// <c>TriggerBodyFidelityTests</c>. Narrowing the comparison to <c>Ordinal</c> reddened
/// <see cref="TheColumnNameIsMatchedCaseInsensitivelyTest"/> and nothing else. The cases that stayed
/// green under both are the controls, and they are named as such below.
/// </para>
/// <para>
/// <b>Every case carries its control.</b> A trigger that never fires at all would satisfy "did not
/// fire for the wrong column" for free, so each case also updates the watched column and asserts that
/// the trigger is alive.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class UpdateOfColumnsTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        // An AUTOINCREMENT single-column key is what the two fast paths require: they work by _rowid,
        // and only for a generated key is the key value the rowid.
        m_engine.Execute(@"
            CREATE TABLE Source (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Watched INT,
                Second INT,
                Ignored INT
            )");

        m_engine.Execute("CREATE TABLE Log (Id BIGINT PRIMARY KEY AUTOINCREMENT, Note VARCHAR(100))");

        m_engine.Execute("INSERT INTO Source (Watched, Second, Ignored) VALUES (1, 1, 1)");
        m_engine.Execute("INSERT INTO Source (Watched, Second, Ignored) VALUES (2, 2, 2)");
    }

    #endregion

    #region The four UPDATE paths

    /// <summary>
    /// The PK-equality fast path: no FROM, <c>WHERE Id = 1</c>, no BEFORE or INSTEAD OF trigger the
    /// statement's columns reach.
    /// </summary>
    [Test]
    public void FastPathHonoursTheColumnListTest()
    {
        CreateAfterTrigger("UPDATE OF Watched");

        m_engine.Execute("UPDATE Source SET Ignored = 20 WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(0), "an update of Ignored must not reach a trigger on Watched");

        m_engine.Execute("UPDATE Source SET Watched = 10 WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(1), "and the trigger is live for the column it names");
    }

    /// <summary>
    /// The batch fast path: <c>WHERE Id IN (…)</c>, which is a different method from the one above and
    /// fires the trigger once per row.
    /// </summary>
    [Test]
    public void BatchFastPathHonoursTheColumnListTest()
    {
        CreateAfterTrigger("UPDATE OF Watched");

        m_engine.Execute("UPDATE Source SET Ignored = 20 WHERE Id IN (1, 2)");
        Assert.That(LogCount(), Is.EqualTo(0), "an update of Ignored must not reach a trigger on Watched");

        m_engine.Execute("UPDATE Source SET Watched = 10 WHERE Id IN (1, 2)");
        Assert.That(LogCount(), Is.EqualTo(2), "and it fires once per row for the column it names");
    }

    /// <summary>
    /// The streaming path: a WHERE that is not a key lookup, no RETURNING, no BEFORE or INSTEAD OF
    /// trigger the statement's columns reach.
    /// </summary>
    [Test]
    public void StreamingPathHonoursTheColumnListTest()
    {
        CreateAfterTrigger("UPDATE OF Watched");

        m_engine.Execute("UPDATE Source SET Ignored = 20 WHERE Ignored > 0");
        Assert.That(LogCount(), Is.EqualTo(0), "an update of Ignored must not reach a trigger on Watched");

        m_engine.Execute("UPDATE Source SET Watched = 10 WHERE Ignored > 0");
        Assert.That(LogCount(), Is.EqualTo(2), "and the trigger is live for the column it names");
    }

    /// <summary>
    /// The standard iterator path: RETURNING keeps the statement out of streaming and the WHERE keeps
    /// it out of both fast paths, so this is the only shape here that goes through it.
    /// </summary>
    [Test]
    public void StandardPathHonoursTheColumnListTest()
    {
        CreateAfterTrigger("UPDATE OF Watched");

        m_engine.Query("UPDATE Source SET Ignored = 20 WHERE Ignored > 0 RETURNING Id");
        Assert.That(LogCount(), Is.EqualTo(0), "an update of Ignored must not reach a trigger on Watched");

        m_engine.Query("UPDATE Source SET Watched = 10 WHERE Ignored > 0 RETURNING Id");
        Assert.That(LogCount(), Is.EqualTo(2), "and the trigger is live for the column it names");
    }

    #endregion

    #region The other two timings

    /// <summary>
    /// A BEFORE trigger fires only from the standard path - the other three refuse to run while one
    /// that the statement reaches exists. Measured: the first statement here takes the FAST path and
    /// the second the standard one, so this case also covers the guard asking whether the trigger is
    /// REACHED rather than whether it exists.
    /// </summary>
    [Test]
    public void BeforeTriggerHonoursTheColumnListTest()
    {
        m_engine.Execute(@"
            CREATE TRIGGER T BEFORE UPDATE OF Watched ON Source FOR EACH ROW
            BEGIN
                INSERT INTO Log (Note) VALUES ('fired');
            END");

        m_engine.Execute("UPDATE Source SET Ignored = 20 WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(0), "an update of Ignored must not reach a BEFORE trigger on Watched");

        m_engine.Execute("UPDATE Source SET Watched = 10 WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(1), "and the trigger is live for the column it names");
    }

    /// <summary>
    /// The one where getting it wrong loses data rather than a log line: an INSTEAD OF trigger stands
    /// in for the write. One that watches a column the statement does not name must not stand in for
    /// it, or the update disappears and the statement still reports success.
    /// </summary>
    [Test]
    public void InsteadOfTriggerHonoursTheColumnListTest()
    {
        m_engine.Execute(@"
            CREATE TRIGGER T INSTEAD OF UPDATE OF Watched ON Source FOR EACH ROW
            BEGIN
                INSERT INTO Log (Note) VALUES ('instead');
            END");

        m_engine.Execute("UPDATE Source SET Ignored = 20 WHERE Id = 1");

        Assert.That(LogCount(), Is.EqualTo(0), "an update of Ignored must not reach it");
        Assert.That(ValueOf("Ignored", 1), Is.EqualTo(20),
            "and the write it does not watch must actually happen - this is the half that loses data");

        m_engine.Execute("UPDATE Source SET Watched = 10 WHERE Id = 1");

        Assert.That(LogCount(), Is.EqualTo(1), "the trigger is live for the column it names");
        Assert.That(ValueOf("Watched", 1), Is.EqualTo(1),
            "and there it does stand in for the write, so the value is unchanged");
    }

    #endregion

    #region What the clause means

    /// <summary>
    /// THE DECISION, and the case that tells the two readings apart: the clause is about the columns
    /// the statement NAMES, not the ones whose value changes. Under the other reading this update
    /// assigns nothing new and the trigger would stay silent.
    ///
    /// It stays green with the filter removed, and that is correct - it has power over the READING,
    /// not over the fix. What it would catch is an implementation wired to <c>modifiedColumns</c>,
    /// which is sitting right there in three of the four paths and is the easy wrong answer.
    /// </summary>
    [Test]
    public void AssigningTheSameValueStillFiresTest()
    {
        CreateAfterTrigger("UPDATE OF Watched");

        m_engine.Execute("UPDATE Source SET Watched = Watched WHERE Id = 1");

        Assert.That(LogCount(), Is.EqualTo(1),
            "SET Watched = Watched names the column, so it fires - as it does on SQLite and PostgreSQL");
    }

    /// <summary>
    /// Naming any one of several watched columns is enough, and a column outside the list is not.
    /// </summary>
    [Test]
    public void AnyOfSeveralWatchedColumnsFiresTest()
    {
        CreateAfterTrigger("UPDATE OF Watched, Second");

        m_engine.Execute("UPDATE Source SET Ignored = 20 WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(0));

        m_engine.Execute("UPDATE Source SET Second = 20 WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(1), "the second name in the list counts");

        m_engine.Execute("UPDATE Source SET Watched = 10 WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(2), "and so does the first");
    }

    /// <summary>
    /// A statement that assigns a watched column among others fires once, not once per matching name.
    /// Green with the filter removed too: its power is over a filter written as a loop that fires
    /// inside it.
    /// </summary>
    [Test]
    public void OneStatementFiresOnceTest()
    {
        CreateAfterTrigger("UPDATE OF Watched, Second");

        m_engine.Execute("UPDATE Source SET Watched = 10, Second = 20, Ignored = 30 WHERE Id = 1");

        Assert.That(LogCount(), Is.EqualTo(1));
    }

    /// <summary>
    /// Identifiers are matched the way the rest of the engine matches them.
    /// </summary>
    [Test]
    public void TheColumnNameIsMatchedCaseInsensitivelyTest()
    {
        CreateAfterTrigger("UPDATE OF Watched");

        m_engine.Execute("UPDATE Source SET WATCHED = 10 WHERE Id = 1");

        Assert.That(LogCount(), Is.EqualTo(1));
    }

    /// <summary>
    /// The control for the whole fixture: a trigger with no <c>OF</c> clause watches every column, and
    /// the filter must not have narrowed the ordinary case. Without it, "the trigger did not fire" is
    /// an assertion a broken filter satisfies everywhere.
    /// </summary>
    [Test]
    public void ATriggerWithoutTheClauseStillWatchesEveryColumnTest()
    {
        CreateAfterTrigger(string.Empty);

        m_engine.Execute("UPDATE Source SET Ignored = 20 WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(1), "plain UPDATE means every column");

        m_engine.Execute("UPDATE Source SET Watched = 10 WHERE Ignored > 0");
        Assert.That(LogCount(), Is.EqualTo(3), "on every path, and once per row");
    }

    /// <summary>
    /// INSERT and DELETE carry no column list at all, and a trigger on one of them must be unaffected
    /// by the filter that was added for UPDATE.
    /// </summary>
    [Test]
    public void InsertAndDeleteTriggersAreUnaffectedTest()
    {
        m_engine.Execute(@"
            CREATE TRIGGER TI AFTER INSERT ON Source FOR EACH ROW
            BEGIN
                INSERT INTO Log (Note) VALUES ('inserted');
            END");

        m_engine.Execute(@"
            CREATE TRIGGER TD AFTER DELETE ON Source FOR EACH ROW
            BEGIN
                INSERT INTO Log (Note) VALUES ('deleted');
            END");

        m_engine.Execute("INSERT INTO Source (Watched, Second, Ignored) VALUES (3, 3, 3)");
        Assert.That(LogCount(), Is.EqualTo(1));

        m_engine.Execute("DELETE FROM Source WHERE Id = 1");
        Assert.That(LogCount(), Is.EqualTo(2));
    }

    #endregion

    #region The catalogue

    /// <summary>
    /// The list has to be publishable, or a <c>CREATE TRIGGER</c> rebuilt from the catalogue - a dump,
    /// a table rebuild - comes back watching every column. That was harmless while the engine ignored
    /// the clause; the moment it stops ignoring it, it is a silent loss of fidelity in every dump.
    /// </summary>
    [Test]
    public void TheCatalogueMustPublishTheColumnListTest()
    {
        CreateAfterTrigger("UPDATE OF Watched, Second");

        var rows = m_engine.Query(@"
            SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE, EVENT_OBJECT_COLUMN
            FROM INFORMATION_SCHEMA.TRIGGERED_UPDATE_COLUMNS
            ORDER BY EVENT_OBJECT_COLUMN");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["TRIGGER_NAME"].AsString(), Is.EqualTo("T"));
        Assert.That(rows[0]["EVENT_OBJECT_TABLE"].AsString(), Is.EqualTo("Source"));
        Assert.That(rows.Select(row => row["EVENT_OBJECT_COLUMN"].AsString()),
            Is.EqualTo(new[] { "Second", "Watched" }));
    }

    /// <summary>
    /// A trigger that watches every column has no rows here - which is how the standard says "all of
    /// them", and it is what stops the view above from passing on a catalogue that publishes every
    /// column of every trigger.
    /// </summary>
    [Test]
    public void ATriggerWithoutTheClauseHasNoRowsInTheViewTest()
    {
        CreateAfterTrigger(string.Empty);

        var rows = m_engine.Query("SELECT * FROM INFORMATION_SCHEMA.TRIGGERED_UPDATE_COLUMNS");

        Assert.That(rows, Is.Empty);
    }

    /// <summary>
    /// TRIGGERS keeps the shape every other database has: the list went into its own standard view
    /// rather than into a column of this one.
    /// </summary>
    [Test]
    public void TheTriggersViewIsUnchangedTest()
    {
        CreateAfterTrigger("UPDATE OF Watched");

        var rows = m_engine.Query("SELECT * FROM INFORMATION_SCHEMA.TRIGGERS");

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ColumnNames, Has.Count.EqualTo(14));
        Assert.That(rows[0]["EVENT_MANIPULATION"].AsString(), Is.EqualTo("UPDATE"),
            "a trigger with a column list is still an UPDATE trigger there");
    }

    #endregion

    #region Helpers

    private void CreateAfterTrigger(string ofClause)
    {
        m_engine.Execute($@"
            CREATE TRIGGER T AFTER {(ofClause.Length == 0 ? "UPDATE" : ofClause)} ON Source FOR EACH ROW
            BEGIN
                INSERT INTO Log (Note) VALUES ('fired');
            END");
    }

    private int LogCount() => m_engine.Query("SELECT Id FROM Log").Count;

    private long ValueOf(string column, long id) =>
        m_engine.Query($"SELECT {column} FROM Source WHERE Id = {id}")[0][0].AsInt64();

    #endregion
}
