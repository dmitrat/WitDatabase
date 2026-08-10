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

    // DELETED 2026-08-10 by the ledger census: UnappliedIsolationLevelIsPickedUpByTheNextTransactionTest.
    //
    // It was suppressed on 2026-07-27 and it had become unfailable AND wrong, in two independent ways.
    //
    // 1. It read `context.PendingIsolationLevel` - the field on ContextExecution. Since KnownIssues 21
    //    (PR #177) the level lives on the DATABASE, and StatementExecutor reads and writes
    //    `m_context.Database.PendingIsolationLevel`. Nothing in the product touches the ContextExecution
    //    field any more, so it is permanently null and the test failed identically whether isolation
    //    worked perfectly or not at all. A test asking a question of a field the product abandoned.
    //
    // 2. Its assertion - "a level requested for an earlier transaction must not be applied to a later
    //    one" - is the OPPOSITE of the rule this engine deliberately adopted, which
    //    SetAfterBeginAppliesToTheNextTransactionNotTheRunningOneTest asserts green two methods above.
    //    SET is session-scoped here, as in SQL Server. Keeping it would have meant one fixture holding
    //    a suppressed certificate against its own documented decision.
    //
    // HANDED FORWARD, not fixed here because it is a public-surface change: ContextExecution.PendingIsolationLevel
    // is now dead, and its own doc comment still says "Set by SET TRANSACTION ISOLATION LEVEL and
    // consumed by BEGIN TRANSACTION", which is false. Removing a public member is a breaking change and
    // is Dmitry's call.

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
    // BUILT by phase 9 (10.0.0): APPLY/LATERAL, TOP n, VALUES as a query term and the derived column
    // list all landed together. THREE of the four suppressed shapes were lifted by the 2026-08-10
    // ledger census and passed on the first run; the FOURTH still fails, and the census is what
    // separated them - as one marker they read as one gap, and they are not one gap.
    private const string UnaliasedValuesIgnore =
        "LIVE, re-measured 2026-08-10 by the ledger census. `SELECT * FROM (VALUES (1), (2))` - a VALUES "
        + "table source with NO alias - is refused with \"Line 1:31 - mismatched input '<EOF>' expecting "
        + "AS\", while the same shape WITH a derived column list parses. So the alias is mandatory here "
        + "and optional on both target engines. Much narrower than the marker this was split out of, "
        + "which claimed all four shapes were unbuilt; the other three have worked since 10.0.0.";

    [TestCase("SELECT * FROM A CROSS APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x")]
    [TestCase("SELECT * FROM A OUTER APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x")]
    [TestCase("SELECT * FROM (VALUES (1), (2)) AS V(Id)")]
    [TestCase("SELECT * FROM (VALUES (1), (2))", Ignore = UnaliasedValuesIgnore)]
    public void LargeEngineTableSourceParsesTest(string sql)
    {
        Assert.That(() => WitSql.Parse(sql), Throws.Nothing,
            "PostgreSQL and SQL Server accept this shape, and WitSQL targets those rather than SQLite");
    }

    #endregion

    #region User-defined functions and stored procedures

    // BUILT by phase 9d, released as 11.0.0 - a function catalog, evaluator integration and
    // persistence, exactly the subsystem the old marker said was missing.
    //
    // BOTH MARKERS WERE PINNING A SYNTAX MISTAKE, NOT A MISSING CAPABILITY, and the 2026-08-10 ledger
    // census is what established it: the suppressed SQL omits the `AS` the grammar requires, so the
    // tests failed with `mismatched input 'BEGIN' expecting {AS, LANGUAGE}` - a parse error naming the
    // token it wanted. The procedure's `IN x INT` is the second half: this grammar takes a bare
    // parameter list, with no mode keyword. A marker that reads "does not parse" is a description of an
    // OUTCOME, and the outcome had two causes, neither of them the one the text names.

    [Test]
    public void CreateFunctionIsSupportedTest()
    {
        Assert.That(
            () => m_engine.Execute(
                "CREATE FUNCTION Doubled(x INT) RETURNS INT AS BEGIN RETURN x * 2; END"),
            Throws.Nothing,
            "WitSQL.md section 22 documents CREATE FUNCTION as a feature");
    }

    [Test]
    public void CreateProcedureIsSupportedTest()
    {
        Assert.That(
            () => m_engine.Execute(
                "CREATE PROCEDURE AddOne(x INT) AS BEGIN SELECT x + 1; END"),
            Throws.Nothing,
            "WitSQL.md section 23 documents CREATE PROCEDURE as a feature");
    }

    #endregion
}
