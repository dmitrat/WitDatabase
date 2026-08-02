using OutWit.Database.Definitions;
using OutWit.Database.Parser;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A trigger must run the body it was created with.
/// </summary>
/// <remarks>
/// <para>
/// Until 9.0.0 a trigger body was stored as one string of statements joined with <c>;</c> and taken
/// apart again with <c>string.Split(';')</c> at execution time. That has two independent faults, and
/// both are silent:
/// </para>
/// <list type="number">
/// <item><description>
/// The join went through the statement serializer, which dropped clauses it could not write - a body
/// of <c>INSERT … ON CONFLICT DO NOTHING</c> was stored as a plain <c>INSERT</c>, so the trigger's
/// conflict handling disappeared and the trigger began throwing on a duplicate instead of ignoring
/// it. Measured 2026-07-31.
/// </description></item>
/// <item><description>
/// Splitting SQL on <c>;</c> is not parsing it. A semicolon inside a string literal cut the
/// statement in half.
/// </description></item>
/// </list>
/// <para>
/// Storing the statements as statements removes both: there is nothing to render and nothing to
/// split.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class TriggerBodyFidelityTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE Source (Id INT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE Log (Id INT PRIMARY KEY, Note VARCHAR(100))");
    }

    #endregion

    #region Tests

    [Test]
    public void TriggerBodyKeepsItsOnConflictClauseTest()
    {
        m_engine.Execute(@"
            CREATE TRIGGER T AFTER INSERT ON Source FOR EACH ROW
            BEGIN
                INSERT INTO Log (Id, Note) VALUES (1, 'once') ON CONFLICT DO NOTHING;
            END");

        m_engine.Execute("INSERT INTO Source (Id) VALUES (1)");

        // The second insert fires the trigger again with the same Log key. ON CONFLICT DO NOTHING
        // says to ignore that; with the clause dropped the trigger threw instead.
        Assert.That(() => m_engine.Execute("INSERT INTO Source (Id) VALUES (2)"),
            Throws.Nothing,
            "the trigger's ON CONFLICT DO NOTHING must survive being stored");

        Assert.That(Rows("SELECT Id FROM Log"), Has.Length.EqualTo(1));
    }

    [Test]
    public void TriggerBodyKeepsItsConflictResolutionTest()
    {
        m_engine.Execute(@"
            CREATE TRIGGER T AFTER INSERT ON Source FOR EACH ROW
            BEGIN
                INSERT OR IGNORE INTO Log (Id, Note) VALUES (1, 'once');
            END");

        m_engine.Execute("INSERT INTO Source (Id) VALUES (1)");

        Assert.That(() => m_engine.Execute("INSERT INTO Source (Id) VALUES (2)"),
            Throws.Nothing,
            "INSERT OR IGNORE must not be stored as a plain INSERT");
    }

    [Test]
    public void TriggerBodyKeepsASemicolonInsideAStringLiteralTest()
    {
        m_engine.Execute(@"
            CREATE TRIGGER T AFTER INSERT ON Source FOR EACH ROW
            BEGIN
                INSERT INTO Log (Id, Note) VALUES (1, 'a;b');
            END");

        m_engine.Execute("INSERT INTO Source (Id) VALUES (1)");

        var note = m_engine.Query("SELECT Note FROM Log WHERE Id = 1")
            .Select(row => row[0].ToString())
            .FirstOrDefault();

        Assert.That(note, Does.Contain("a;b"),
            "the body was split on ';' rather than parsed, which cut this statement in half");
    }

    /// <remarks>
    /// Coverage, not a regression guard. Measured: with the resolver forced down the legacy path
    /// this one still passes, because a two-statement body with no semicolon inside a literal and
    /// no clause the renderer drops splits back apart correctly. The other three in this fixture go
    /// red. Recorded so nobody reads it as evidence it is not.
    /// </remarks>
    [Test]
    public void TriggerWithSeveralStatementsRunsAllOfThemTest()
    {
        m_engine.Execute(@"
            CREATE TRIGGER T AFTER INSERT ON Source FOR EACH ROW
            BEGIN
                INSERT INTO Log (Id, Note) VALUES (1, 'first');
                INSERT INTO Log (Id, Note) VALUES (2, 'second');
            END");

        m_engine.Execute("INSERT INTO Source (Id) VALUES (1)");

        Assert.That(Rows("SELECT Id FROM Log"), Has.Length.EqualTo(2),
            "a multi-statement body must keep every statement");
    }

    #endregion

    #region Refused at declaration

    /// <summary>
    /// A body the engine cannot run is refused when the trigger is declared, not when it fires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is phase 7's rule applied one area over: accepted, enforced, or refused at declaration
    /// time - never accepted and ignored.
    /// </para>
    /// <para>
    /// <b>Why, as of 2026-08-01.</b> The original answer - DDL deadlocks against the write lock the
    /// firing statement holds, and fails part-way - <b>no longer applies</b>: schema writes go
    /// through the caller's transaction now and <c>AUDIT-2026-07.md</c> finding 19 is closed. The
    /// reason the refusal is kept is measured and worse:
    /// <see cref="DdlInsideATriggerDestroysWhatTheStatementIsWritingTest"/> shows a trigger body
    /// dropping the table its own statement is writing, <b>reporting success</b>, and leaving
    /// nothing. Read that test before considering this list obsolete.
    /// </para>
    /// <para>
    /// <c>COMMIT</c> is in this list for a different and stronger reason: it is stopped by nothing at
    /// runtime. It commits the firing statement's transaction, so the rest of that statement runs
    /// outside one - measured as a three-row <c>INSERT</c> leaving two rows behind after its third
    /// failed. DDL fails loudly; this does not fail.
    /// </para>
    /// </remarks>
    [TestCase("CREATE TABLE Z (Id INT)", "CREATE TABLE")]
    [TestCase("DROP TABLE Log", "DROP TABLE")]
    [TestCase("CREATE INDEX IX ON Log (Id)", "CREATE INDEX")]
    [TestCase("COMMIT", "COMMIT")]
    public void TriggerBodyThatCannotRunIsRefusedWhenDeclaredTest(string body, string named)
    {
        Assert.That(() => m_engine.Execute(
                $"CREATE TRIGGER T AFTER INSERT ON Source FOR EACH ROW BEGIN {body}; END"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains(named),
            "the refusal must name what it refused, so the caller can act on it");
    }

    /// <summary>
    /// What the refusal above is actually protecting against, demonstrated by going round it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written because the refusal's original justification stopped being true.</b> It rested on
    /// DDL inside a trigger deadlocking against the write lock and failing part-way; that was fixed
    /// on 2026-08-01 when schema writes were routed through the caller's transaction
    /// (<c>AUDIT-2026-07.md</c> finding 19). A guard whose stated reason has been repaired is a guard
    /// the next reader removes - correctly, on the evidence in front of them - so the real reason
    /// needs to be a test rather than a sentence.
    /// </para>
    /// <para>
    /// The real reason, measured: a trigger body runs <b>inside a loop over rows</b>, and
    /// <c>DROP TABLE</c> against the table that loop is walking <b>reports success and destroys
    /// it</b>. The statement says it worked; the table and the row it just wrote are gone; nothing
    /// is raised. Compare the neighbouring cases, also measured: DDL against an unrelated object
    /// works, and a DDL that fails rolls back cleanly. Only the object underneath the statement is
    /// the problem.
    /// </para>
    /// <para>
    /// The declaration refusal is bypassed here on purpose - through the catalog API, which takes a
    /// definition rather than SQL. That is the only way to reach this state, which is the point:
    /// <b>the refusal is the whole defence.</b>
    /// </para>
    /// </remarks>
    [Test]
    public void DdlInsideATriggerDestroysWhatTheStatementIsWritingTest()
    {
        m_engine.CreateTrigger(new DefinitionTrigger
        {
            Name = "Destroyer",
            TableName = "Source",
            Time = TriggerTime.After,
            Event = TriggerEvent.Insert,
            ForEachRow = true,
            Statements = WitSql.Parse("DROP TABLE Source").ToList()
        });

        // No exception: the statement reports success.
        m_engine.Execute("INSERT INTO Source (Id) VALUES (1)");

        Assert.That(m_engine.GetTable("Source"), Is.Null,
            "this is what CREATE TRIGGER refuses at declaration - a statement that reports success "
            + "and destroys the table it was writing to. If this ever stops being true, the refusal "
            + "in RefuseNonDmlBody can be revisited; until then it is the only thing standing here");
    }

    /// <summary>
    /// And the refusal is narrow: it must not catch the bodies that do work.
    /// </summary>
    [TestCase("INSERT INTO Log (Id) VALUES (1)")]
    [TestCase("UPDATE Log SET Note = 'x' WHERE Id = 1")]
    [TestCase("DELETE FROM Log WHERE Id = 1")]
    [TestCase("SELECT 1")]
    public void DmlBodyIsStillAcceptedTest(string body)
    {
        Assert.That(() => m_engine.Execute(
                $"CREATE TRIGGER T AFTER INSERT ON Source FOR EACH ROW BEGIN {body}; END"),
            Throws.Nothing);
    }

    #endregion

    #region Helpers

    private long[] Rows(string sql) =>
        m_engine.Query(sql).Select(row => row[0].AsInt64()).OrderBy(id => id).ToArray();

    #endregion
}
