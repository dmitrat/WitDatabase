using OutWit.Database.Context;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Parser;
using OutWit.Database.Statements;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Verification of the engine-side <c>dropin-gaps</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// Covers the grammar-level gaps and the isolation-level handling that the ADO.NET provider sits on
/// top of. The ADO.NET contract half is in
/// <c>OutWit.Database.AdoNet.Tests/AuditVerification/DropInGapsAdoNetTests</c>.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class DropInGapsEngineTests : WitSqlEngineTestsBase
{
    #region Isolation level handling

    [Test]
    public void SettingIsolationLevelBeforeBeginAppliesItToThatTransactionTest()
    {
        // The documented usage, and it works. StatementExecutor.Transactions.cs says so in as many
        // words: "Use SET TRANSACTION ISOLATION LEVEL before BEGIN TRANSACTION if needed." BEGIN
        // then consumes the pending level and clears it.
        // Two SEPARATE Execute calls, which is what a driver does and what the executor-level
        // version of this case could not see: the level is recorded on the DATABASE, so it survives
        // between them. While it lived on the per-call context this passed with one shared context
        // and the behaviour was broken for every real consumer.
        m_engine.Execute("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE");

        Assert.That(m_engine.PendingIsolationLevel, Is.EqualTo(WitIsolationLevel.Serializable),
            "SET must record the requested level");

        m_engine.Execute("BEGIN TRANSACTION");

        Assert.That(m_engine.PendingIsolationLevel, Is.Null,
            "BEGIN must consume the pending level, i.e. actually apply it to this transaction");

        Assert.That(m_engine.CurrentTransaction!.IsolationLevel, Is.EqualTo(WitIsolationLevel.Serializable),
            "and the transaction must CARRY it - the assertion the plumbing check could not make");

        m_engine.Execute("ROLLBACK");
    }

    /// <summary>
    /// SET after BEGIN affects the NEXT transaction, not the running one - which is what every
    /// reference database does, and is why the order matters.
    /// </summary>
    /// <remarks>
    /// This was <c>[Ignore]</c>d on 2026-07-27 with the text "the requested level is still sitting in
    /// PendingIsolationLevel, unapplied. The transaction ran at ReadCommitted", citing
    /// <c>WitDbConnection.cs:164</c> - and that was the diagnosis of `Docs/KnownIssues.md` 21, sitting
    /// under a marker for six months. The ADO layer emits the two the right way round now; what this
    /// asserts is the SQL rule underneath, so that a driver written against it cannot be surprised.
    /// </remarks>
    [Test]
    public void SetAfterBeginAppliesToTheNextTransactionNotTheRunningOneTest()
    {
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE");

        Assert.That(m_engine.CurrentTransaction!.IsolationLevel, Is.EqualTo(WitIsolationLevel.ReadCommitted),
            "the running transaction keeps the level it began with");

        m_engine.Execute("ROLLBACK");

        m_engine.Execute("BEGIN TRANSACTION");

        Assert.That(m_engine.CurrentTransaction!.IsolationLevel, Is.EqualTo(WitIsolationLevel.Serializable),
            "and the NEXT one gets what was set - which is why the level must be sent first");

        m_engine.Execute("ROLLBACK");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27 at the executor level: within one ContextExecution the level left "
            + "pending by the first transaction IS consumed by the second. But it does not reach a "
            + "provider consumer - WitSqlEngine.Execute builds a fresh ContextExecution per call, so "
            + "through ADO.NET the level is silently DROPPED rather than leaked. The finding's "
            + "\"leaks onto the next transaction\" wording holds only for a shared context.")]
    public void UnappliedIsolationLevelIsPickedUpByTheNextTransactionTest()
    {
        // The "leaks onto the next transaction" half of the finding, isolated. Within a single
        // execution context it is exactly true: the level left pending by the first transaction is
        // consumed by the second, which therefore runs at a level nobody asked it to run at.
        //
        // It does not reach a provider consumer, though, and this test is what pins that down. See
        // the fixture note and the plan: WitSqlEngine.Execute builds a *fresh* ContextExecution per
        // call, so a level left pending by one Execute is discarded rather than carried into the
        // next. Through ADO.NET the requested level is silently dropped, not leaked.
        var context = new ContextExecution { Database = m_engine };
        var executor = new StatementExecutor(context);

        executor.Execute(WitSql.Parse("BEGIN TRANSACTION")[0]);
        executor.Execute(WitSql.Parse("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE")[0]);
        executor.Execute(WitSql.Parse("ROLLBACK")[0]);

        executor.Execute(WitSql.Parse("BEGIN TRANSACTION")[0]);
        var leaked = context.PendingIsolationLevel is null;
        executor.Execute(WitSql.Parse("ROLLBACK")[0]);

        Assert.That(leaked, Is.False,
            "a level requested for an earlier transaction must not be applied to a later one");
    }

    #endregion

    #region CROSS APPLY / OUTER APPLY / VALUES table sources

    /// <summary>
    /// Table-source shapes WitSQL does not support yet. <b>Unbuilt capability, not correct
    /// rejection.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original finding justified these by "EF Core emits them". Phase 3 measured that and it is
    /// largely false for this provider: collections translate to <c>IN (…)</c>, and the one shape the
    /// generator really did emit — <c>OUTER APPLY</c>, for a correlated <c>Take</c> — is now refused
    /// at translation time rather than emitted unparseably (see <c>GeneratedSqlIsParseableTests</c>).
    /// </para>
    /// <para>
    /// <b>That does not make these shapes unwanted.</b> The target is a drop-in replacement for the
    /// large engines, so the yardstick is PostgreSQL and SQL Server, not SQLite:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>CROSS APPLY</c> / <c>OUTER APPLY</c> is T-SQL; PostgreSQL spells the same thing
    /// <c>LATERAL</c>. Both support it. Supporting it here needs <b>lateral execution</b> — the right
    /// side re-evaluated per left row — which is engine work, not grammar.
    /// </description></item>
    /// <item><description>
    /// A <c>VALUES</c> table source with a derived column list <c>AS V(Id)</c> is <b>standard SQL</b>
    /// and works on both PostgreSQL and SQL Server. SQLite happens to reject it; that is SQLite's
    /// limitation, and no reason for WitSQL to inherit it.
    /// </description></item>
    /// <item><description>
    /// <c>TOP n</c> is T-SQL's row limiter. <c>LIMIT</c> already covers the capability, so this one
    /// is dialect surface rather than a missing feature — worth having for SQL Server source
    /// compatibility, not urgent.
    /// </description></item>
    /// </list>
    /// <para>
    /// Kept as executable specifications so each turns green the day it is built.
    /// </para>
    /// </remarks>
    private const string TableSourceIgnore =
        "UNBUILT CAPABILITY, re-scoped 2026-07-28. Not justified by 'EF Core emits it' - measured, " +
        "and it mostly does not - but by the actual target: PostgreSQL and SQL Server both support " +
        "these, and WitSQL is not meant to be a SQLite clone. APPLY needs lateral execution (engine " +
        "work, not grammar); the derived column list AS V(Id) is standard SQL that only SQLite " +
        "lacks; TOP duplicates LIMIT and is source-compatibility surface. None is phase-3 scope.";

    [TestCase("SELECT * FROM A CROSS APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x", Ignore = TableSourceIgnore)]
    [TestCase("SELECT * FROM A OUTER APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x", Ignore = TableSourceIgnore)]
    [TestCase("SELECT * FROM (VALUES (1), (2)) AS V(Id)", Ignore = TableSourceIgnore)]
    [TestCase("SELECT * FROM (VALUES (1), (2))", Ignore = TableSourceIgnore)]
    public void LargeEngineTableSourceParsesTest(string sql)
    {
        Assert.That(() => WitSql.Parse(sql), Throws.Nothing,
            "PostgreSQL and SQL Server accept this shape, and WitSQL targets those rather than SQLite");
    }

    #endregion

    #region User-defined functions and stored procedures

    [Test]
    [Ignore("UNBUILT CAPABILITY, restated 2026-07-29. CREATE FUNCTION does not parse, and WitSQL.md "
            + "section 22 documents it in full - now annotated there as not implemented. It is WANTED: "
            + "PostgreSQL and SQL Server both have user-defined functions, and application code "
            + "written against them would notice the absence, which is the bar. It is out of the "
            + "grammar phase because it is a subsystem - a function catalog, evaluator integration and "
            + "persistence - not a grammar rule. dropin-gaps")]
    public void CreateFunctionIsSupportedTest()
    {
        // Finding: WitSqlParser.g4:35 - WitSQL.md documents user-defined functions in section 22
        // and stored procedures in section 23, complete with syntax, while neither exists anywhere
        // in the stack. Dmitry names both as the remaining gaps to true drop-in status.
        Assert.That(
            () => m_engine.Execute(
                "CREATE FUNCTION Doubled(x INT) RETURNS INT BEGIN RETURN x * 2; END"),
            Throws.Nothing,
            "WitSQL.md section 22 documents CREATE FUNCTION as a feature");
    }

    [Test]
    [Ignore("UNBUILT CAPABILITY, restated 2026-07-29. CREATE PROCEDURE does not parse, and WitSQL.md "
            + "section 23 documents it in full - now annotated there as not implemented. Wanted for the "
            + "same reason as CreateFunctionIsSupportedTest, and out of the grammar phase for a bigger "
            + "one: it needs a procedural interpreter with variables, control flow and CALL. "
            + "dropin-gaps")]
    public void CreateProcedureIsSupportedTest()
    {
        Assert.That(
            () => m_engine.Execute(
                "CREATE PROCEDURE AddOne(IN x INT) BEGIN SELECT x + 1; END"),
            Throws.Nothing,
            "WitSQL.md section 23 documents CREATE PROCEDURE as a feature");
    }

    #endregion
}
