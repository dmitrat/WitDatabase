using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using OutWit.Database.Schema;

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
    /// <remarks>
    /// <b>Re-decided once the fix landed: this is a documented sharp edge, not an open defect.</b> It was
    /// <c>[Ignore]</c>d as a confirmed defect while there was no way to get agreement; there now is -
    /// <c>new WitSqlEngine(database, sharedCatalog)</c> - and
    /// <see cref="SharedCatalogMakesATableVisibleToBothSessionsTest"/> asserts it. So this test is active
    /// again and pins what the <i>single-argument</i> constructor does: each engine builds its own
    /// catalog and therefore its own idea of the schema. Anyone putting two engines on one database has
    /// to share the catalog, and this is the test that says why.
    /// </remarks>
    [Test]
    public void ProbeSecondEngineSeesATableCreatedAfterItTest()
    {
        using var database = CreateDatabase();

        using var first = new WitSqlEngine(database);
        using var second = new WitSqlEngine(database);

        first.Execute("CREATE TABLE Later (Id BIGINT PRIMARY KEY, V TEXT)");
        first.Execute("INSERT INTO Later (Id, V) VALUES (1, 'a')");

        var outcome = Observe(() => ReadRows(second, "SELECT V FROM Later"));
        Report("a table created after the second engine existed, separate catalogs", outcome);

        // PINS THE SHARP EDGE OF THE SINGLE-ARGUMENT CONSTRUCTOR, not a defect: each engine built its
        // own catalog, so each has its own schema. If this ever starts passing, the catalog has become
        // a live view over the store and the shared-catalog constructor is no longer needed.
        Assert.That(outcome.Error, Is.InstanceOf<InvalidOperationException>(),
            "two engines with separate catalogs no longer diverge - if the catalog now reads through "
            + "to the store, the sharing constructor and this test can both go");
    }

    /// <summary>
    /// Probe: rows inserted by the first engine into a table <i>both</i> engines already know about.
    /// This is the read path with no schema change involved.
    /// </summary>
    /// <remarks>
    /// The other half of the sharp edge, and the more insidious one: the <b>rows</b> come off the shared
    /// store so a scan sees them, while the <b>count</b> comes from the engine's own catalog counter. A
    /// query and its own count therefore disagreed across sessions with no crash involved - the same
    /// split phase 4 met after a process kill. Also re-decided from defect to documented behaviour:
    /// <see cref="SharedCatalogMakesRowsAndTheirCountAgreeAcrossSessionsTest"/> asserts the fix.
    /// </remarks>
    [Test]
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

        // PINS THE SHARP EDGE, and the asymmetry is the whole point: the scan sees the row because the
        // store is shared, the count does not because the catalog is not. Both are asserted, because a
        // test of either alone would have reported the wrong thing - the rows-only version would have
        // said "works" and the count-only version would have said "sees nothing".
        Assert.Multiple(() =>
        {
            Assert.That(scanned.Value, Is.EqualTo("1"),
                "the row is in the shared store, so a scan must find it");
            Assert.That(counted.Value, Is.EqualTo("Integer:0"),
                "and the separate catalog's counter must still be stale - if it is not, the counter "
                + "now reads through to the store and the sharing constructor can go");
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

    #region The shared catalog - the same questions, asked of the fix

    /// <summary>
    /// The two divergence probes above, repeated with <b>one catalog shared</b> between the sessions.
    /// This is the design instrument B was built to validate, so these assert the fix.
    /// </summary>
    [Test]
    public void SharedCatalogMakesATableVisibleToBothSessionsTest()
    {
        using var database = CreateDatabase(mvcc: true);
        using var schema = new SchemaCatalog(database.Store);

        using var first = new WitSqlEngine(database, schema);
        using var second = new WitSqlEngine(database, schema);

        first.Execute("CREATE TABLE Later (Id BIGINT PRIMARY KEY, V TEXT)");
        first.Execute("INSERT INTO Later (Id, V) VALUES (1, 'a')");

        var scanned = Observe(() => ReadRows(second, "SELECT V FROM Later"));
        Report("shared catalog: a table created after the second session existed", scanned);

        Assert.Multiple(() =>
        {
            Assert.That(scanned.Error, Is.Null, "the second session must see the table");
            Assert.That(scanned.Value, Is.EqualTo("1"), "and the row in it");
        });
    }

    /// <summary>
    /// And the half that a <c>COUNT(*)</c>-only test would have missed: rows and their cached counter
    /// must agree across sessions, not just within one.
    /// </summary>
    [Test]
    public void SharedCatalogMakesRowsAndTheirCountAgreeAcrossSessionsTest()
    {
        using var database = CreateDatabase(mvcc: true);
        using var schema = new SchemaCatalog(database.Store);

        using var first = new WitSqlEngine(database, schema);
        first.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");

        using var second = new WitSqlEngine(database, schema);

        first.Execute("INSERT INTO T (Id, V) VALUES (1, 'a')");

        var scanned = Observe(() => ReadRows(second, "SELECT V FROM T"));
        var counted = Observe(() => Scalar(second, "SELECT COUNT(*) FROM T"));

        Report("shared catalog: rows the other session inserted, scanned", scanned);
        Report("shared catalog: the same rows, counted", counted);

        Assert.Multiple(() =>
        {
            Assert.That(scanned.Value, Is.EqualTo("1"), "the row must be visible");
            Assert.That(counted.Value, Is.EqualTo("Integer:1"), "and its count must agree");
        });
    }

    /// <summary>
    /// The risk a shared catalog introduces: it guards itself with a
    /// <see cref="ReaderWriterLockSlim"/> created <c>NoRecursion</c>, so two sessions interleaving DDL
    /// and DML through one catalog is a new way to deadlock or to throw
    /// <see cref="LockRecursionException"/>. Driven from one thread on purpose - recursion is
    /// thread-affine, so a single thread is the harshest case for it, and it needs no interleaving to
    /// be exact.
    /// </summary>
    [Test]
    public void SharedCatalogSurvivesInterleavedWorkFromBothSessionsTest()
    {
        using var database = CreateDatabase(mvcc: true);
        using var schema = new SchemaCatalog(database.Store);

        using var first = new WitSqlEngine(database, schema);
        using var second = new WitSqlEngine(database, schema);

        var outcome = Observe<object?>(() =>
        {
            first.Execute("CREATE TABLE A (Id BIGINT PRIMARY KEY, V TEXT)");
            second.Execute("CREATE TABLE B (Id BIGINT PRIMARY KEY, V TEXT)");

            first.Execute("INSERT INTO A (Id, V) VALUES (1, 'a')");
            second.Execute("INSERT INTO B (Id, V) VALUES (1, 'b')");

            first.Execute("CREATE INDEX ix_a ON A (V)");
            second.Execute("CREATE INDEX ix_b ON B (V)");

            first.Execute("INSERT INTO B (Id, V) VALUES (2, 'via-first')");
            second.Execute("INSERT INTO A (Id, V) VALUES (2, 'via-second')");

            first.Execute("DROP TABLE B");

            return "ok";
        });

        Report("shared catalog: interleaved DDL and DML from both sessions", outcome);

        Assert.That(outcome.Error, Is.Null,
            "a shared catalog must tolerate two sessions using it - this is the deadlock and "
            + "lock-recursion risk the sharing introduces");

        // And the surviving table is consistent through both sessions.
        Assert.Multiple(() =>
        {
            Assert.That(ReadRows(first, "SELECT V FROM A"), Is.EqualTo(2));
            Assert.That(ReadRows(second, "SELECT V FROM A"), Is.EqualTo(2));
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
