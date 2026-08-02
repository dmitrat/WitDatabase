namespace OutWit.Database.Tests.Schema;

/// <summary>
/// Stored procedures: a body of statements, invoked by <c>CALL</c>.
/// </summary>
/// <remarks>
/// <para>
/// A procedure body may contain DML and DDL, and a trigger body may not contain <c>CALL</c>. That
/// rule was decided against measurement rather than taste - see
/// <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 3. DDL run from inside a loop over rows is what
/// is dangerous: <c>DROP TABLE</c> against the table that loop is walking reports success and
/// destroys it. A <c>CALL</c> at the top level is a statement, not a loop, so the restriction sits
/// on the trigger rather than on the procedure and needs no analysis of the call graph.
/// </para>
/// <para>
/// Transaction control stays refused in any body, for a stronger reason than DDL ever had: it is
/// stopped by nothing at runtime.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ProcedureTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE Log (Id INT PRIMARY KEY AUTOINCREMENT, Note VARCHAR(100))");
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 10)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, 20)");
    }

    #endregion

    #region It runs

    [Test]
    public void ABodyRunsEveryStatementTest()
    {
        m_engine.Execute(@"
            CREATE PROCEDURE Seed AS BEGIN
                INSERT INTO Log (Note) VALUES ('first');
                INSERT INTO Log (Note) VALUES ('second');
            END");

        m_engine.Execute("CALL Seed()");

        Assert.That(m_engine.Query("SELECT COUNT(*) FROM Log")[0][0].AsInt64(), Is.EqualTo(2));
    }

    [Test]
    public void AnArgumentReachesTheBodyTest()
    {
        m_engine.Execute(@"
            CREATE PROCEDURE Write2(Note VARCHAR(100)) AS BEGIN
                INSERT INTO Log (Note) VALUES (Note);
            END");

        m_engine.Execute("CALL Write2('hello')");

        Assert.That(m_engine.Query("SELECT Note FROM Log")[0][0].AsString(), Is.EqualTo("hello"));
    }

    /// <summary>
    /// The call's result is its last statement's result, so a body ending in a SELECT returns rows.
    /// </summary>
    /// <remarks>
    /// Without this the subsystem would exist and be unreachable the way consumers actually reach
    /// one: <c>CommandType.StoredProcedure</c> plus <c>ExecuteReader</c> is the common shape, and it
    /// needs something to read.
    /// </remarks>
    [Test]
    public void TheLastStatementsResultIsTheCallsResultTest()
    {
        m_engine.Execute("CREATE PROCEDURE GetAll AS BEGIN SELECT Id FROM T ORDER BY Id; END");

        var ids = m_engine.Query("CALL GetAll()")
            .Select(row => row[0].AsInt64())
            .ToArray();

        Assert.That(ids, Is.EqualTo(new[] { 1L, 2L }));
    }

    [Test]
    public void AProcedureMayCallAnotherTest()
    {
        m_engine.Execute("CREATE PROCEDURE Inner2 AS BEGIN INSERT INTO Log (Note) VALUES ('inner'); END");
        m_engine.Execute("CREATE PROCEDURE Outer2 AS BEGIN CALL Inner2(); END");

        m_engine.Execute("CALL Outer2()");

        Assert.That(m_engine.Query("SELECT Note FROM Log")[0][0].AsString(), Is.EqualTo("inner"));
    }

    /// <summary>
    /// A procedure body may contain DDL, which a trigger body may not.
    /// </summary>
    [Test]
    public void AProcedureBodyMayContainDdlTest()
    {
        m_engine.Execute("CREATE PROCEDURE MakeTable AS BEGIN CREATE TABLE Z (Id INT PRIMARY KEY); END");

        Assert.That(() => m_engine.Execute("CALL MakeTable()"), Throws.Nothing);
        Assert.That(m_engine.GetTable("Z"), Is.Not.Null);
        Assert.That(() => m_engine.Execute("INSERT INTO Z (Id) VALUES (1)"), Throws.Nothing,
            "and what it created must be usable");
    }

    #endregion

    #region Recursion is bounded rather than refused

    /// <summary>
    /// A procedure may call itself, because every body statement passes the nesting counter.
    /// </summary>
    /// <remarks>
    /// The contrast with a function is the point. A function is evaluated inside an expression and
    /// never passes through <c>StatementExecutor.Execute</c>, so nothing counts its depth and a
    /// self-call is refused at declaration. A procedure's every statement goes through that door,
    /// so recursion is bounded at 32 and refused with an error a caller can catch - which is the
    /// failure the whole limit exists to replace.
    /// </remarks>
    [Test]
    public void ASelfCallingProcedureIsBoundedNotFatalTest()
    {
        m_engine.Execute(@"
            CREATE PROCEDURE Forever AS BEGIN
                INSERT INTO Log (Note) VALUES ('again');
                CALL Forever();
            END");

        Assert.That(() => m_engine.Execute("CALL Forever()"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("32"),
            "unbounded recursion must surface as a catchable error, not as a dead process");
    }

    #endregion

    #region Refused at declaration

    /// <summary>
    /// Transaction control, in any body.
    /// </summary>
    /// <remarks>
    /// Measured: a nested <c>COMMIT</c> is stopped by nothing at runtime. It commits the calling
    /// statement's transaction, and the rest of that statement then runs outside one - a three-row
    /// <c>INSERT</c> left two rows behind after its third failed, raising only the key violation.
    /// </remarks>
    [TestCase("COMMIT", TestName = "COMMIT")]
    [TestCase("ROLLBACK", TestName = "ROLLBACK")]
    [TestCase("BEGIN TRANSACTION", TestName = "BEGIN TRANSACTION")]
    [TestCase("SAVEPOINT S", TestName = "SAVEPOINT")]
    public void TransactionControlInABodyIsRefusedTest(string statement)
    {
        Assert.That(() => m_engine.Execute($"CREATE PROCEDURE P AS BEGIN {statement}; END"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("transaction"),
            "the refusal must say why, because this one fails silently rather than loudly");
    }

    [TestCase("CREATE FUNCTION F() RETURNS INT AS BEGIN RETURN 1; END", TestName = "CREATE FUNCTION")]
    [TestCase("DROP FUNCTION F", TestName = "DROP FUNCTION")]
    [TestCase("CREATE PROCEDURE Q AS BEGIN SELECT 1; END", TestName = "CREATE PROCEDURE")]
    [TestCase("DROP PROCEDURE Q", TestName = "DROP PROCEDURE")]
    public void DeclaringARoutineInsideABodyIsRefusedTest(string statement)
    {
        Assert.That(() => m_engine.Execute($"CREATE PROCEDURE P AS BEGIN {statement}; END"),
            Throws.InstanceOf<NotSupportedException>());
    }

    /// <summary>
    /// And the rule that keeps DDL out of a row loop: a trigger body may not <c>CALL</c>.
    /// </summary>
    /// <remarks>
    /// This is the one line that lets a procedure have DDL at all. Without it a trigger could reach
    /// a procedure that drops a table, which is the measured case where the statement reports
    /// success and the table is gone.
    /// </remarks>
    [Test]
    public void ATriggerBodyMayNotCallAProcedureTest()
    {
        m_engine.Execute("CREATE PROCEDURE P AS BEGIN INSERT INTO Log (Note) VALUES ('x'); END");

        Assert.That(() => m_engine.Execute(
                "CREATE TRIGGER TR AFTER INSERT ON T FOR EACH ROW BEGIN CALL P(); END"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("CALL"),
            "a CALL from a trigger would put a row loop underneath a body allowed to run DDL");
    }

    [Test]
    public void AForeignLanguageIsRefusedTest()
    {
        Assert.That(() => m_engine.Execute(
                "CREATE PROCEDURE P LANGUAGE plpgsql AS BEGIN SELECT 1; END"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("plpgsql"));
    }

    #endregion

    #region Refused at call time

    [Test]
    public void CallingSomethingThatIsNotThereIsRefusedTest()
    {
        Assert.That(() => m_engine.Execute("CALL NeverExisted()"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("NeverExisted"));
    }

    [Test]
    public void TheWrongNumberOfArgumentsIsRefusedTest()
    {
        m_engine.Execute("CREATE PROCEDURE P(A INT, B INT) AS BEGIN SELECT A + B; END");

        Assert.That(() => m_engine.Execute("CALL P(1)"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("2"));
    }

    #endregion

    #region Dropping

    [Test]
    public void AProcedureCanBeDroppedTest()
    {
        m_engine.Execute("CREATE PROCEDURE P AS BEGIN SELECT 1; END");
        m_engine.Execute("DROP PROCEDURE P");

        Assert.That(() => m_engine.Execute("CALL P()"), Throws.Exception);
        Assert.That(() => m_engine.Execute("DROP PROCEDURE IF EXISTS P"), Throws.Nothing);
    }

    [Test]
    public void AProcedureAnotherOneCallsCannotBeDroppedTest()
    {
        m_engine.Execute("CREATE PROCEDURE Inner2 AS BEGIN SELECT 1; END");
        m_engine.Execute("CREATE PROCEDURE Outer2 AS BEGIN CALL Inner2(); END");

        Assert.That(() => m_engine.Execute("DROP PROCEDURE Inner2"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Outer2"));
    }

    #endregion

    #region Atomicity

    /// <summary>
    /// A body that fails part-way leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// The <c>CALL</c> is a statement, and a statement is a unit of work - the property
    /// <c>ExecuteAtomically</c> gives every data-modifying statement. It is asserted here rather than
    /// assumed because a routine body is exactly the shape that used to break it: a nested
    /// <c>COMMIT</c> tore the calling statement in two, which is why one is refused above.
    /// </remarks>
    [Test]
    public void AFailingBodyLeavesNothingBehindTest()
    {
        m_engine.Execute("INSERT INTO Log (Id, Note) VALUES (1, 'taken')");

        m_engine.Execute(@"
            CREATE PROCEDURE Partial2 AS BEGIN
                INSERT INTO T (Id, V) VALUES (3, 30);
                INSERT INTO Log (Id, Note) VALUES (1, 'duplicate');
            END");

        Assert.That(() => m_engine.Execute("CALL Partial2()"), Throws.Exception);

        Assert.That(m_engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64(), Is.EqualTo(2),
            "the row the body wrote before it failed must be gone");
    }

    #endregion
}
