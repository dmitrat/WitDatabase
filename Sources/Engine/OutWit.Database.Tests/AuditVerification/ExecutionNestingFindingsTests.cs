namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Nested execution must have a bound, and crossing it must be an error a caller can catch.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-01, at head <c>c23b983</c>: a trigger that inserts into its own table recurses
/// with nothing counting the levels. 200 levels pass, 400 pass, and <b>600 kills the host
/// process</b> - <c>StackOverflowException</c> cannot be caught in .NET, so the process embedding
/// the database dies with it. No exception is raised, no transaction is rolled back, and nothing in
/// the suite noticed, because no test had ever gone deep.
/// </para>
/// <para>
/// This is a defect on the trigger path today, and it is also the prerequisite for the routine
/// subsystem of phase 9d: a procedure that calls itself is the ordinary way to write one, and a
/// function reachable from a computed column would carry the same exposure per row. The design note
/// <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 2 records the measurement and the decision.
/// </para>
/// <para>
/// The bound is <b>32</b>, which is what SQL Server allows and what PostgreSQL's
/// <c>max_stack_depth</c> approximates. The number matters far less than the class of failure: a
/// catchable exception naming the depth, rather than a dead process.
/// </para>
/// <para>
/// <b>Why the synchronous path is the whole surface.</b> The count is kept in
/// <c>StatementExecutor.Execute</c>, which every statement passes through, and it was checked rather
/// than assumed that nothing goes round it: <c>WitSqlEngine.Async.cs</c> is an empty file, and every
/// <c>…Async</c> member of <c>WitDbCommand</c> is <c>Task.Run</c> over the synchronous call. There is
/// no second execution path for a limit to miss. This is worth recording because the neighbouring
/// guard <i>does</i> have that hole: <c>AcquireWriteLockAsync</c> never records the owning thread, so
/// the <b>lock's</b> recursion check is off for async writers - a separate, pre-existing item in
/// <c>AUDIT-2026-07.md</c>, and not something this limit depends on.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class ExecutionNestingFindingsTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE R (Id INT PRIMARY KEY)");
    }

    #endregion

    #region Tests

    /// <summary>
    /// The bound exists and is reported as an error rather than as a dead process.
    /// </summary>
    /// <remarks>
    /// Red before the fix by succeeding: 100 levels was comfortably within what the engine used to
    /// run, so this asserted an exception that never came. It does not need to reach the depth that
    /// kills the process to prove the bound is missing - which is the point of choosing 100.
    /// </remarks>
    [Test]
    public void RunawayNestingIsRefusedRatherThanFatalTest()
    {
        SelfRecursingTrigger(depth: 100);

        Assert.That(() => m_engine.Execute("INSERT INTO R (Id) VALUES (1)"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("nest"),
            "unbounded nesting must surface as a catchable error, not as a stack overflow");
    }

    /// <summary>
    /// The error must say what was exceeded, so a caller can act on it.
    /// </summary>
    [Test]
    public void TheRefusalNamesTheDepthTest()
    {
        SelfRecursingTrigger(depth: 100);

        // Catch, not Throws: the latter demands the exact type, and the limit is raised as a private
        // subclass so the levels it unwinds through do not each re-wrap it. What a caller is
        // promised is InvalidOperationException, which is what this asks for.
        var message = Assert.Catch<InvalidOperationException>(
            () => m_engine.Execute("INSERT INTO R (Id) VALUES (1)"))!.ToString();

        Assert.That(message, Does.Contain("32"),
            "the refusal must name the limit that was crossed");
    }

    /// <summary>
    /// The control: a depth that killed the host process before the bound existed.
    /// </summary>
    /// <remarks>
    /// This test is the instrument's own proof. With the bound removed it does not fail - <b>it
    /// takes the whole test run down with it</b>, which is exactly the failure being closed and is
    /// worth more than an assertion. <b>Verified by reverting the limit on 2026-08-01</b>, not
    /// asserted: the run reported <i>"The active test run was aborted. Reason: Test host process
    /// crashed : Stack overflow."</i> and took the other five tests in this fixture with it.
    /// </remarks>
    [Test]
    public void ADepthThatUsedToKillTheProcessIsNowAnErrorTest()
    {
        SelfRecursingTrigger(depth: 5000);

        Assert.That(() => m_engine.Execute("INSERT INTO R (Id) VALUES (1)"),
            Throws.InstanceOf<InvalidOperationException>(),
            "5000 levels crashed the host process before the bound was added");
    }

    /// <summary>
    /// And the bound must be wide enough not to refuse ordinary nesting.
    /// </summary>
    /// <remarks>
    /// The counter is worthless if it breaks a three-deep trigger chain, which is a shape consumers
    /// really write. Measured before the fix as working, and it must keep working.
    /// </remarks>
    [Test]
    public void OrdinaryNestingStillRunsTest()
    {
        m_engine.Execute("CREATE TABLE A (Id INT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE B (Id INT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE C (Id INT PRIMARY KEY)");
        m_engine.Execute("CREATE TRIGGER TA AFTER INSERT ON A FOR EACH ROW BEGIN INSERT INTO B (Id) VALUES (1); END");
        m_engine.Execute("CREATE TRIGGER TB AFTER INSERT ON B FOR EACH ROW BEGIN INSERT INTO C (Id) VALUES (1); END");

        Assert.That(() => m_engine.Execute("INSERT INTO A (Id) VALUES (1)"), Throws.Nothing);
        Assert.That(m_engine.Query("SELECT COUNT(*) FROM C")[0][0].AsInt64(), Is.EqualTo(1));
    }

    /// <summary>
    /// A bounded recursion that finishes below the limit must not be touched by the counter.
    /// </summary>
    /// <remarks>
    /// The counter has to be restored on the way out as well as raised on the way in. Without the
    /// decrement, the twentieth sibling statement of a flat body would be refused as if it were
    /// twenty levels deep - a bound that counts total work rather than depth.
    /// </remarks>
    [Test]
    public void RecursionThatTerminatesBelowTheLimitStillRunsTest()
    {
        SelfRecursingTrigger(depth: 20);

        Assert.That(() => m_engine.Execute("INSERT INTO R (Id) VALUES (1)"), Throws.Nothing);
        Assert.That(m_engine.Query("SELECT COUNT(*) FROM R")[0][0].AsInt64(), Is.EqualTo(20));
    }

    /// <summary>
    /// The depth is a property of one statement, not of the connection.
    /// </summary>
    /// <remarks>
    /// A counter that is never reset would let a long-lived engine refuse the thousandth ordinary
    /// insert. This runs the refusal and then asserts the very next statement is unaffected.
    /// </remarks>
    [Test]
    public void TheCounterIsResetForTheNextStatementTest()
    {
        SelfRecursingTrigger(depth: 100);

        Assert.That(() => m_engine.Execute("INSERT INTO R (Id) VALUES (1)"),
            Throws.InstanceOf<InvalidOperationException>());

        m_engine.Execute("DROP TRIGGER TR");

        Assert.That(() => m_engine.Execute("INSERT INTO R (Id) VALUES (500)"), Throws.Nothing,
            "the nesting counter belongs to a statement, not to the engine");
    }

    #endregion

    #region Helpers

    private void SelfRecursingTrigger(int depth)
    {
        m_engine.Execute($@"
            CREATE TRIGGER TR AFTER INSERT ON R FOR EACH ROW
            WHEN (NEW.Id < {depth})
            BEGIN
                INSERT INTO R (Id) VALUES (NEW.Id + 1);
            END");
    }

    #endregion
}
