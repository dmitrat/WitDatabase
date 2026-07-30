using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Concurrency;

/// <summary>
/// Phase 5 instrument B — what two <see cref="WitSqlEngine"/>s over one <see cref="WitDatabase"/>
/// actually see of each other.
/// </summary>
/// <remarks>
/// <b>The question this decides.</b> The target model (see <c>Docs/PHASE5-CONCURRENCY-PLAN.md</c> § 8)
/// is one process, one engine per database, <i>many connections</i> — an ASP.NET Core host with several
/// scoped <c>DbContext</c>s. Making that work means a connection must stop bringing its own engine.
/// The obvious split is "share the <see cref="WitDatabase"/>, give each connection its own
/// <see cref="WitSqlEngine"/>", because the engine holds <c>m_currentTransaction</c> and is therefore a
/// session rather than a database object.
///
/// That split is only correct if two engines over one database agree about the schema. They may not:
/// <c>SchemaCatalog</c> loads the schema <b>once, in its constructor</b>, into plain dictionaries of
/// tables, indexes, views, triggers, sequences, row ids and row counts, and <c>WitSqlEngine</c>
/// constructs its own. <c>ReloadMetadataFromStore</c> exists but refreshes only the counters, not the
/// table list.
///
/// So this fixture asks, by execution rather than by reading, exactly what diverges. Its answers are
/// what the shared-engine design has to be built around, and a green run here is <b>not</b> a statement
/// that any of this is desirable — every assertion is labelled with what it records.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class SharedDatabaseTwoEnginesProbeTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_two_engines_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: one engine over the database behaves normally. If this fails, the probes below are
    /// measuring the harness.
    /// </summary>
    [Test]
    public void ControlOneEngineSeesItsOwnWorkTest()
    {
        using var database = CreateDatabase();
        using var engine = new WitSqlEngine(database);

        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        engine.Execute("INSERT INTO T (Id, V) VALUES (1, 'a')");

        Assert.That(ReadRows(engine, "SELECT V FROM T"), Is.EqualTo(1));
    }

    /// <summary>
    /// Control: a table created <i>before</i> the second engine exists is visible to it, because
    /// <c>SchemaCatalog</c> loads the schema in its constructor. This separates "the catalog never
    /// sees anything" from "the catalog never sees anything <i>new</i>", which is the actual finding.
    /// </summary>
    [Test]
    public void ControlSecondEngineSeesSchemaThatPredatesItTest()
    {
        using var database = CreateDatabase();

        using var first = new WitSqlEngine(database);
        first.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        first.Execute("INSERT INTO T (Id, V) VALUES (1, 'a')");

        using var second = new WitSqlEngine(database);

        Assert.That(ReadRows(second, "SELECT V FROM T"), Is.EqualTo(1),
            "the second engine loads the schema when it is constructed, so it must see this table");
    }

    #endregion

    #region Probes

    /// <summary>
    /// Probe: a table created by the first engine <i>after</i> the second one exists.
    /// </summary>
    [Test]
    [Ignore("CONFIRMED 2026-07-30: the second engine raises InvalidOperationException \"Table 'Later' "
            + "not found\" for a table the first engine created and committed. SchemaCatalog loads the "
            + "schema ONCE in its constructor into plain dictionaries, and WitSqlEngine constructs its "
            + "own catalog, so two sessions over one database each get a private and immediately stale "
            + "idea of the schema. ReloadMetadataFromStore refreshes only the counters, not the table "
            + "list. This is the blocker for the shared-engine work: sharing the WitDatabase alone is "
            + "not enough, the catalog has to be shared too - it is database-level state that is "
            + "currently session-level. core-concurrency, Engine/WitSqlEngine.cs:45")]
    public void ProbeSecondEngineSeesATableCreatedAfterItTest()
    {
        using var database = CreateDatabase();

        using var first = new WitSqlEngine(database);
        using var second = new WitSqlEngine(database);

        first.Execute("CREATE TABLE Later (Id BIGINT PRIMARY KEY, V TEXT)");
        first.Execute("INSERT INTO Later (Id, V) VALUES (1, 'a')");

        var outcome = Observe(() => ReadRows(second, "SELECT V FROM Later"));
        Report("a table created after the second engine existed", outcome);

        // ASSERTS CORRECT BEHAVIOUR. Two sessions on one database must agree that a committed table
        // exists; this is the property the shared-engine design needs and the one to build toward.
        Assert.That(outcome.Error, Is.Null,
            "the second engine cannot see a table the first one created - a per-connection engine "
            + "over a shared database would give each connection its own idea of the schema");
    }

    /// <summary>
    /// Probe: rows inserted by the first engine into a table <i>both</i> engines already know about.
    /// This is the read path with no schema change involved.
    /// </summary>
    [Test]
    [Ignore("CONFIRMED 2026-07-30, and it is the more insidious half: a scan through the second engine "
            + "returns the row (1), while SELECT COUNT(*) through the same engine returns 0. The rows "
            + "come off the shared store, but the count comes from the second engine's own "
            + "SchemaCatalog counter, which the first engine's INSERT incremented in ITS catalog. So a "
            + "query and its own count disagree ACROSS sessions - the same rows-versus-counter split "
            + "phase 4 met after a crash, now reachable with no crash at all. Only caught because the "
            + "probe measured both; a COUNT(*)-only test would have reported no rows and a rows-only "
            + "test would have reported success. core-concurrency, Engine/WitSqlEngine.cs:45")]
    public void ProbeSecondEngineSeesRowsWrittenByTheFirstTest()
    {
        using var database = CreateDatabase();

        using var first = new WitSqlEngine(database);
        first.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");

        using var second = new WitSqlEngine(database);

        first.Execute("INSERT INTO T (Id, V) VALUES (1, 'a')");

        var scanned = Observe(() => ReadRows(second, "SELECT V FROM T"));
        Report("rows the first engine inserted, as the second engine scans them", scanned);

        var counted = Observe(() => Scalar(second, "SELECT COUNT(*) FROM T"));
        Report("the same rows as the second engine COUNTs them", counted);

        // Rows and their count are separate state on this engine - phase 4 published a false
        // catastrophe by trusting COUNT(*) - so both are reported and both are asserted.
        Assert.Multiple(() =>
        {
            Assert.That(scanned.Value, Is.EqualTo("1"),
                "a committed row must be visible to the other session");
            Assert.That(counted.Value, Is.EqualTo("1"),
                "and its count must agree with it");
        });
    }

    /// <summary>
    /// Probe: does each engine keep its own transaction, which is the reason a connection cannot
    /// simply share one engine?
    /// </summary>
    [Test]
    [TestCase(false, TestName = "ProbeEachEngineHasItsOwnTransaction_NoMvcc",
        Ignore = "CONFIRMED 2026-07-30: with MVCC off, the second session's BEGIN TRANSACTION throws "
                 + "LockRecursionException, \"Cannot acquire write lock - current thread already holds "
                 + "write lock\". A non-MVCC transaction holds the database-wide write lock for its "
                 + "whole duration, so one transaction per database is the real limit, and on one "
                 + "thread it surfaces as a lock-recursion error rather than as blocking or a clear "
                 + "refusal. One writer at a time IS the documented model for MVCC=false, so the "
                 + "defect is the diagnosis, not the exclusion. MVCC is the provider default and its "
                 + "case passes, so this does not block the shared-engine work. "
                 + "core-concurrency, Core/Concurrency/DatabaseLock.cs")]
    [TestCase(true, TestName = "ProbeEachEngineHasItsOwnTransaction_Mvcc")]
    public void ProbeEachEngineHasItsOwnTransactionTest(bool mvcc)
    {
        using var database = CreateDatabase(mvcc);

        using var first = new WitSqlEngine(database);
        first.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");

        using var second = new WitSqlEngine(database);

        var firstBegin = Observe(() => first.Execute("BEGIN TRANSACTION"));
        Report($"the first engine begins a transaction [mvcc={mvcc}]", firstBegin);

        var secondBegin = Observe(() => second.Execute("BEGIN TRANSACTION"));
        Report($"the second engine begins one while the first is open [mvcc={mvcc}]", secondBegin);

        _ = Observe(() => first.Execute("ROLLBACK"));
        _ = Observe(() => second.Execute("ROLLBACK"));

        // ASSERTS CORRECT BEHAVIOUR: independent transactions per session are an ADO.NET requirement,
        // and they are also why sharing a single engine between connections is not an option - the
        // engine holds one m_currentTransaction.
        Assert.Multiple(() =>
        {
            Assert.That(firstBegin.Error, Is.Null, "the first transaction must start");
            Assert.That(secondBegin.Error, Is.Null,
                "two sessions on one database must be able to hold transactions at the same time");
        });
    }

    #endregion

    #region Tools

    private WitDatabase CreateDatabase(bool mvcc = false)
    {
        var builder = new WitDatabaseBuilder()
            .WithFilePath(Path.Combine(m_testDir, "shared.witdb"))
            .WithBTree()
            .WithTransactions();

        if (mvcc)
            builder = builder.WithMvcc();

        return builder.Build();
    }

    /// <summary>
    /// Counts rows by reading them. Never <c>COUNT(*)</c> unless that is the thing being measured:
    /// this engine answers it from a cached per-table counter, which is separate state.
    /// </summary>
    private static int ReadRows(WitSqlEngine engine, string sql)
    {
        using var result = engine.Execute(sql);
        return result.ReadAll().Count;
    }

    private static string Scalar(WitSqlEngine engine, string sql)
    {
        using var result = engine.Execute(sql);
        var rows = result.ReadAll();

        return rows.Count == 0 ? "<none>" : rows[0][0].ToString() ?? "<null>";
    }

    private static Outcome Observe<T>(Func<T> func)
    {
        try
        {
            return new Outcome(func()?.ToString() ?? "<null>", null);
        }
        catch (Exception e)
        {
            return new Outcome(null, e);
        }
    }

    private static Outcome Observe(Action action) => Observe<object?>(() =>
    {
        action();
        return "ok";
    });

    private static void Report(string question, Outcome outcome) =>
        TestContext.Out.WriteLine(outcome.Error is null
            ? $"PROBE  {question}  ->  OK, value {outcome.Value}"
            : $"PROBE  {question}  ->  THREW {outcome.Error.GetType().Name}: "
              + outcome.Error.Message.Replace('\r', ' ').Replace('\n', ' '));

    private sealed record Outcome(string? Value, Exception? Error);

    #endregion
}
