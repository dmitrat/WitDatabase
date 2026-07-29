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
        var context = new ContextExecution { Database = m_engine };
        var executor = new StatementExecutor(context);

        executor.Execute(WitSql.Parse("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE")[0]);
        Assert.That(context.PendingIsolationLevel, Is.EqualTo(WitIsolationLevel.Serializable),
            "SET must record the requested level");

        executor.Execute(WitSql.Parse("BEGIN TRANSACTION")[0]);

        Assert.That(context.PendingIsolationLevel, Is.Null,
            "BEGIN must consume the pending level, i.e. actually apply it to this transaction");

        executor.Execute(WitSql.Parse("ROLLBACK")[0]);
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: after BEGIN then SET - the order WitDbConnection.BeginDbTransaction "
            + "actually emits - the requested level is still sitting in PendingIsolationLevel, "
            + "unapplied. The transaction ran at ReadCommitted. "
            + "dropin-gaps / core-mvcc, AdoNet/WitDbConnection.cs:164")]
    public void BeginThenSetLeavesTheTransactionAtTheDefaultLevelTest()
    {
        // The order WitDbConnection.BeginDbTransaction actually emits: BEGIN TRANSACTION first, then
        // SET TRANSACTION ISOLATION LEVEL. The transaction has already started at ReadCommitted by
        // the time the level is recorded, so the requested level is left sitting in
        // PendingIsolationLevel, unapplied.
        var context = new ContextExecution { Database = m_engine };
        var executor = new StatementExecutor(context);

        executor.Execute(WitSql.Parse("BEGIN TRANSACTION")[0]);
        executor.Execute(WitSql.Parse("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE")[0]);

        Assert.That(context.PendingIsolationLevel, Is.Null,
            "the level requested for this transaction must have been applied to it, not left pending");

        executor.Execute(WitSql.Parse("ROLLBACK")[0]);
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
    /// The original finding, restated against measurement. <b>Two of its three shapes were wrong,
    /// and the third was right for a different reason than the one recorded.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The finding said the grammar must learn <c>CROSS APPLY</c>, <c>OUTER APPLY</c> and a
    /// <c>VALUES</c> table source because EF Core emits all three. Phase 3 measured each claim:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b><c>TOP</c> and <c>APPLY</c> are T-SQL, and SQLite rejects them too</b>, so by the drop-in
    /// bar neither is a WitDatabase defect. The two cases below were written in SQL Server syntax.
    /// </description></item>
    /// <item><description>
    /// <b><c>AS V(Id)</c> — a derived column list — is rejected by SQLite as well.</b> Requiring it
    /// would make WitDatabase stricter than nothing and looser than SQLite, for no consumer.
    /// </description></item>
    /// <item><description>
    /// <b><c>APPLY</c> was nevertheless a real defect — emitted, not missing.</b> The provider's own
    /// generator produced <c>OUTER APPLY</c> for a correlated <c>Take</c>, and its own parser
    /// rejected it. Fixed in <c>WitQuerySqlGenerator</c> by refusing at translation time, the way
    /// EF Core's SQLite provider does. See <c>GeneratedSqlIsParseableTests</c>.
    /// </description></item>
    /// </list>
    /// <para>
    /// So the parse-level assertions here are kept only as <b>parity pins</b>: these shapes must
    /// stay rejected, because SQLite rejects them.
    /// </para>
    /// </remarks>
    [TestCase("SELECT * FROM A CROSS APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x")]
    [TestCase("SELECT * FROM A OUTER APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x")]
    [TestCase("SELECT * FROM (VALUES (1), (2)) AS V(Id)")]
    public void EfShapedTableSourceIsRejectedJustAsSqliteRejectsItTest(string sql)
    {
        Assert.That(() => WitSql.Parse(sql), Throws.Exception,
            "SQLite rejects this shape too, so parity means rejecting it rather than learning it");
    }

    /// <summary>
    /// The one shape from that finding where SQLite and WitDatabase genuinely differ — deliberately
    /// left open.
    /// </summary>
    /// <remarks>
    /// A <b>bare</b> <c>VALUES</c> table source is accepted by SQLite and rejected here, so it is a
    /// real divergence. It is not, however, something the drop-in path needs: measured 2026-07-28,
    /// WitDatabase's own EF provider translates both an inlined and a parameterised collection to
    /// <c>IN (…)</c> — <c>IN (1, 2, 3)</c> and <c>IN (@ids1, @ids2, @ids3)</c> — and never emits
    /// <c>VALUES</c>. The audit's claim that EF Core emits it for inlined lists does not hold for
    /// this provider.
    ///
    /// Supporting it is more than grammar: the executor would have to materialise a row set, with
    /// SQLite's <c>column1..columnN</c> naming. Deferred on that basis, with the measurement
    /// recorded rather than the question left open.
    /// </remarks>
    [Test]
    [Ignore("DEFERRED 2026-07-28, not a defect on the drop-in path. SQLite accepts " +
            "`SELECT * FROM (VALUES (1), (2))` and WitDatabase rejects it, so it is a real " +
            "divergence - but the EF provider never emits it: collections translate to IN (...), " +
            "measured in GeneratedSqlIsParseableTests. Implementing it needs executor work to " +
            "materialise a row set with column1..columnN naming, which phase 3 does not cover.")]
    public void BareValuesTableSourceParsesTest()
    {
        Assert.That(() => WitSql.Parse("SELECT * FROM (VALUES (1), (2))"), Throws.Nothing,
            "SQLite accepts a bare VALUES table source");
    }

    #endregion

    #region User-defined functions and stored procedures

    [Test]
    [Ignore("CONFIRMED 2026-07-27: CREATE FUNCTION does not parse. WitSQL.md section 22 documents it with "
            + "full syntax. dropin-gaps, Parser/Grammars/WitSqlParser.g4:35")]
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
    [Ignore("CONFIRMED 2026-07-27: CREATE PROCEDURE does not parse, while WitSQL.md section 23 documents "
            + "it with full syntax.")]
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
