using System.Data;
using System.Data.Common;
using System.Text;
using OutWit.Database.AdoNet.Pool;
using OutWit.Database.Core.Exceptions;

namespace OutWit.Database.AdoNet.Tests.AuditVerification;

/// <summary>
/// Phase 5 instrument A - the concurrency-model probe.
/// </summary>
/// <remarks>
/// Nothing in this repository states what the concurrency model is, and the phase-5 plan makes
/// establishing it the first thing the audit must do: single writer and many readers? one engine per
/// file per process? more than one process? The answer decides whether "a second connection cannot
/// open the database" is a defect or a documented limit.
///
/// This fixture does not assert desirable behaviour. It <b>records the model by execution</b>, one
/// question per test, so that the model is a measurement rather than a reading of the code. Two
/// controls guard it: <see cref="ControlTwoConnectionsToDifferentFilesBothOpenTest"/> and
/// <see cref="ControlOneConnectionReopensAfterCloseTest"/>. If either goes red the harness is wrong,
/// not the engine, and the phase stops until it is fixed.
///
/// The probes print their observation with <see cref="TestContext.Out"/> and assert only what has
/// already been observed, so a change to the model shows up as a failure here.
///
/// <para>
/// <b>Extended 2026-07-30, in the second half of the phase.</b>
/// <see cref="ProbeLsmParallelModeSeesItsOwnCommittedRowsTest"/> asks the parallel-mode question again
/// over <c>Store=lsm</c>, and it found the heaviest defect of the phase: ten acknowledged INSERTs leave
/// 0 or 1 rows, and a clean close and reopen recovers nothing, so they were <b>lost</b>. Every earlier
/// parallel-mode probe here ran over the default btree store, which the builder wraps in
/// <c>BTreeConcurrentStore</c> - a wrapper that does not buffer. So the phase's own "parallel mode is
/// supported" verdict was measured on the component that cannot exhibit this, which is the same lesson
/// as phase 4's: a refutation is only as wide as what was actually run.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class ConcurrencyModelProbeTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_model_probe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        ConnectionPool.ClearAllPools();

        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch
        {
            // A probe that leaves a handle open must not fail the run on cleanup.
        }
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: two connections to two <i>different</i> files both open. If this fails, the probes
    /// below are measuring the harness rather than the sharing model.
    /// </summary>
    [Test]
    public void ControlTwoConnectionsToDifferentFilesBothOpenTest()
    {
        var first = FileConnectionString("control_a.witdb");
        var second = FileConnectionString("control_b.witdb");

        using var connA = new WitDbConnection(first);
        connA.Open();

        using var connB = new WitDbConnection(second);
        connB.Open();

        Assert.Multiple(() =>
        {
            Assert.That(connA.State, Is.EqualTo(ConnectionState.Open));
            Assert.That(connB.State, Is.EqualTo(ConnectionState.Open));
        });
    }

    /// <summary>
    /// Control: one connection, closed and reopened, succeeds. This separates "the second opener is
    /// refused because the file is shared exclusively" from "the first opener leaked its handle".
    /// </summary>
    [Test]
    public void ControlOneConnectionReopensAfterCloseTest()
    {
        var cs = FileConnectionString("control_reopen.witdb");

        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            Execute(conn, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
            Execute(conn, "INSERT INTO T (Id, V) VALUES (1, 'a')");
        }

        using var reopened = new WitDbConnection(cs);
        reopened.Open();

        Assert.That(Scalar(reopened, "SELECT V FROM T WHERE Id = 1"), Is.EqualTo("a"));
    }

    #endregion

    #region Q1 - a second connection to the same database, same process

    /// <summary>
    /// Probe: the default store (btree) over a file. <see cref="Core.Storage.StorageFile"/> opens
    /// read-write with <c>FileShare.None</c>.
    /// </summary>
    [Test]
    public void ProbeSecondConnectionToSameBTreeFileTest()
    {
        var cs = FileConnectionString("btree_shared.witdb");

        using var first = new WitDbConnection(cs);
        first.Open();
        Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");

        using var second = new WitDbConnection(cs);
        var outcome = Observe(() => second.Open());

        Report("Q1 btree, second connection in the same process", outcome);

        // INVERTED BY THE SHARED-DATABASE WORK, and this is the phase's headline. This probe asserted
        // `Threw` for two releases' worth of behaviour: each connection built its own engine, and a
        // database admits one engine, so the second connection failed - which meant a host with scoped
        // DbContexts did not work at all. Connections now share one engine per database, so a second
        // one opens. A second *engine* is still refused; that distinction is tested by
        // SharedDatabaseConnectionTests.SecondEngineOverTheSameFileIsStillRefusedTest.
        Assert.That(outcome.Threw, Is.False,
            "a second connection in the same process is the supported shape - see plan doc section 8");
    }

    /// <summary>
    /// Probe: the same question for the LSM store, which is a directory of files rather than one
    /// file, and whose readers open with <c>FileShare.Read</c>.
    /// </summary>
    /// <remarks>
    /// <b>This is the one row of the model that differs by platform, and CI is what found it.</b>
    /// The first version of this probe asserted <c>outcome.Threw</c> unconditionally, having measured
    /// it on Windows - and went red on the Linux runner, which is the instrument over-claiming rather
    /// than the engine changing.
    ///
    /// The mechanism: .NET emulates <c>FileShare</c> on Unix with advisory <c>flock</c>, where
    /// <c>FileShare.None</c> becomes an exclusive lock and anything else becomes a shared one. btree
    /// passes <c>FileShare.None</c> and is refused on both platforms; the LSM write-ahead log opens
    /// <c>ReadWrite</c>/<c>FileShare.Read</c>, which Windows treats as "no second writer" and Unix
    /// treats as "shared - come in".
    /// </remarks>
    [Test]
    public void ProbeSecondConnectionToSameLsmDirectoryTest()
    {
        var dir = Path.Combine(m_testDir, "lsm_shared");
        var cs = $"Data Source={dir};Store=lsm";

        using var first = new WitDbConnection(cs);
        first.Open();
        Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        Execute(first, "INSERT INTO T (Id, V) VALUES (1, 'a')");

        using var second = new WitDbConnection(cs);
        var outcome = Observe(() => second.Open());

        Report($"Q1 lsm, second connection in the same process [{Platform}]", outcome);

        var read = Observe(() => Scalar(second, "SELECT V FROM T WHERE Id = 1"));
        Report($"Q1 lsm, the second connection reads a row the first wrote [{Platform}]", read);

        // This probe has now recorded three different models, which is worth keeping visible:
        //   1. before 5.0.0 - refused on Windows via wal.log, ADMITTED on Linux, where .NET maps
        //      FileShare.Read to a shared advisory lock, and the two engines then diverged (section 3a);
        //   2. with the exclusivity guard - refused with DatabaseAlreadyOpenException on both platforms;
        //   3. now - a second CONNECTION shares the one engine, so it opens, identically on both.
        // The guard still refuses a second engine; connections are handles onto one.
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Threw, Is.False,
                $"a second LSM connection was refused on {Platform} - connections share an engine");
            Assert.That(read.Value, Is.EqualTo("String:a"),
                "and it must see the row the first connection committed");
        });
    }

    /// <summary>
    /// Probe: the consequence of the platform split above. Where a second LSM connection opens, two
    /// engines write the same directory - so what does each see, and what survives on disk?
    /// </summary>
    /// <remarks>
    /// "It opens" is a possibility; this asks for the cost. Deterministic and single-threaded: the
    /// writes are ordered by the test, so nothing here depends on an interleaving. On Windows the
    /// second connection cannot open at all and the probe reports that and stops.
    /// </remarks>
    [Test]
    public void ProbeTwoLsmConnectionsBothWriteTest()
    {
        var dir = Path.Combine(m_testDir, "lsm_two_writers");
        var cs = $"Data Source={dir};Store=lsm";

        int reopenedRows;

        using (var first = new WitDbConnection(cs))
        {
            first.Open();
            Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
            Execute(first, "INSERT INTO T (Id, V) VALUES (1, 'first')");

            using var second = new WitDbConnection(cs);
            var opened = Observe(() => second.Open());
            Report($"Q1 lsm two writers, second Open [{Platform}]", opened);

            Assert.That(opened.Threw, Is.False,
                "the second connection must open - it shares the first one's engine");

            var secondWrite = Observe(() => Execute(second, "INSERT INTO T (Id, V) VALUES (2, 'second')"));
            Report($"Q1 lsm two writers, the second engine's INSERT [{Platform}]", secondWrite);

            // Counted by reading rows, never by COUNT(*): this engine keeps a cached per-table
            // counter, and phase 4 published a false catastrophe by trusting it.
            var seenByFirst = CountRows(first, "SELECT Id FROM T");
            var seenBySecond = CountRows(second, "SELECT Id FROM T");

            Report($"Q1 lsm two writers, rows visible to the FIRST engine [{Platform}]",
                new Outcome(false, null, null, $"Int32:{seenByFirst}"));
            Report($"Q1 lsm two writers, rows visible to the SECOND engine [{Platform}]",
                new Outcome(false, null, null, $"Int32:{seenBySecond}"));

            // THE DEFECT THIS PROBE WAS BUILT FOR IS GONE, and its numbers are the record of that.
            // Measured on Linux before the fix: 1 and 2 - two engines over one LSM directory, the
            // second seeing both rows because it replayed wal.log at open, the first unable to see the
            // second's row at all because it lived in another engine's memtable with nothing to
            // invalidate or notify. There is now one engine, so both connections see both rows, and
            // this asserts the agreement rather than the divergence.
            Assert.Multiple(() =>
            {
                Assert.That(seenByFirst, Is.EqualTo(2),
                    "both connections share one engine, so both must see both rows");
                Assert.That(seenBySecond, Is.EqualTo(2),
                    "both connections share one engine, so both must see both rows");
            });
        }

        // Both engines are now closed. Whatever a fresh reader finds is what actually survived.
        using (var reopened = new WitDbConnection(cs))
        {
            reopened.Open();
            reopenedRows = CountRows(reopened, "SELECT Id FROM T");
        }

        Report($"Q1 lsm two writers, rows on disk after both closed [{Platform}]",
            new Outcome(false, null, null, $"Int32:{reopenedRows}"));

        // Two rows were written and both were acknowledged. Anything less is data loss, and this
        // assertion is the one that says so - it is NOT pinning current behaviour. Measured
        // 2026-07-30 on Linux: 2, so this ordered, uncontended case does NOT lose a write, and the
        // finding is stated as divergent views rather than as corruption. See the plan document
        // section 3a for the contended experiment that is NOT yet run.
        Assert.That(reopenedRows, Is.EqualTo(2),
            $"two engines each acknowledged an INSERT, but {reopenedRows} row(s) survived");
    }

    /// <summary>
    /// Probe: <c>Data Source=:memory:</c>. Two connections with the same connection string - do they
    /// address the same database at all? Every <see cref="ConnectionPool"/> test uses this form, so
    /// the answer decides what those tests are evidence of.
    /// </summary>
    [Test]
    public void ProbeTwoMemoryConnectionsShareOneDatabaseTest()
    {
        const string cs = "Data Source=:memory:";

        using var first = new WitDbConnection(cs);
        first.Open();
        Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        Execute(first, "INSERT INTO T (Id, V) VALUES (1, 'a')");

        using var second = new WitDbConnection(cs);
        second.Open();

        var outcome = Observe(() => Scalar(second, "SELECT V FROM T WHERE Id = 1"));
        Report("Q1 :memory:, the second connection reads the first connection's row", outcome);

        // PINS AN OBSERVATION. Measured 2026-07-30: "Table 'T' not found". Two connections with the
        // same :memory: connection string are two separate databases, because ConfigureStorage calls
        // WithMemoryStorage() per connection and nothing is keyed by the connection string. SQLite
        // behaves the same way until asked for Cache=Shared, so this is a candidate documented
        // limit rather than a defect - but it is what makes the ConnectionPool tests below vacuous.
        Assert.That(outcome.Threw, Is.True,
            "two :memory: connections now share a database - the pool tests stop being vacuous "
            + "and Docs/PHASE5-CONCURRENCY-PLAN.md needs updating");
    }

    /// <summary>
    /// Probe: <c>Read Only=true</c>. A read-only opener is the one shape that could share a file -
    /// <see cref="Core.Storage.StorageFile"/> grants <c>FileShare.Read</c> when read-only. So: is
    /// the setting honoured at all?
    /// </summary>
    [Test]
    public void ProbeReadOnlyConnectionIsHonouredTest()
    {
        var path = Path.Combine(m_testDir, "readonly.witdb");

        using (var seed = new WitDbConnection($"Data Source={path}"))
        {
            seed.Open();
            Execute(seed, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
            Execute(seed, "INSERT INTO T (Id, V) VALUES (1, 'a')");
        }

        using var reader = new WitDbConnection($"Data Source={path};Read Only=true");
        reader.Open();

        var write = Observe(() => Execute(reader, "INSERT INTO T (Id, V) VALUES (2, 'b')"));
        Report("Q1 Read Only=true, a write through a read-only connection", write);

        var rows = Observe(() => Scalar(reader, "SELECT V FROM T WHERE Id = 2"));
        Report("Q1 Read Only=true, the row that write would have added", rows);

        // INVERTED BY THE FIX. This asserted the defect: the INSERT used to succeed and the row read
        // back as 'b', because WitDbConnection never read options.ReadOnly - ConfigureStorage only asked
        // whether the mode was Memory, so the setting was parsed and dropped. Read-only is now enforced
        // per session, and the message names the statement kind and how to get a writable connection.
        Assert.Multiple(() =>
        {
            Assert.That(write.Threw, Is.True, "a read-only connection must refuse writes");
            Assert.That(write.Message, Does.Contain("read-only"),
                "and must say why, rather than failing obscurely");
            Assert.That(rows.Value, Is.EqualTo("<null>"), "the write must not have landed");
        });
    }

    /// <summary>
    /// Probe: two read-only connections to one file. If read-only were honoured this is the shape
    /// that supports many readers over one file.
    /// </summary>
    [Test]
    public void ProbeTwoReadOnlyConnectionsToSameFileTest()
    {
        var path = Path.Combine(m_testDir, "readonly_pair.witdb");

        using (var seed = new WitDbConnection($"Data Source={path}"))
        {
            seed.Open();
            Execute(seed, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        }

        var cs = $"Data Source={path};Read Only=true";

        using var first = new WitDbConnection(cs);
        first.Open();

        using var second = new WitDbConnection(cs);
        var outcome = Observe(() => second.Open());

        Report("Q1 two Read Only=true connections to one file", outcome);

        // Two read-only connections now open - but NOT because read-only works. They share one engine
        // like any two connections, and that engine is read-write, because the provider still drops the
        // setting (see ProbeReadOnlyConnectionIsHonouredTest, still pinning that defect). So this test
        // no longer says anything about read-only; it is kept because it is the shape a consumer reaches
        // for when they want many readers, and it should keep working once read-only is honoured.
        Assert.That(outcome.Threw, Is.False,
            "two connections to one file must open, read-only or not");
    }

    /// <summary>
    /// Probe: an LSM database with the write-ahead log turned off. § 3a established that LSM
    /// exclusivity comes from <c>wal.log</c> - so what protects a database that has no log?
    /// </summary>
    /// <remarks>
    /// <c>EnableWal</c> is reachable from a connection string, and <c>StoreLsm.m_wal</c> is nullable
    /// and gated on it. If nothing else provides exclusivity then this hole is open on <b>both</b>
    /// platforms, which decides whether fixing the log's share mode is sufficient or whether the
    /// limit needs enforcing explicitly.
    /// </remarks>
    [Test]
    [TestCase(true, true, TestName = "ProbeLsmExclusivity_WalOn_TransactionsOn")]
    [TestCase(false, true, TestName = "ProbeLsmExclusivity_WalOff_TransactionsOn")]
    [TestCase(true, false, TestName = "ProbeLsmExclusivity_WalOn_TransactionsOff")]
    [TestCase(false, false, TestName = "ProbeLsmExclusivity_WalOff_TransactionsOff")]
    public void ProbeLsmExclusivityAcrossWalAndTransactionsTest(bool wal, bool transactions)
    {
        var label = $"EnableWal={wal}, Transactions={transactions}";
        var dir = Path.Combine(m_testDir, $"lsm_{wal}_{transactions}");
        var cs = $"Data Source={dir};Store=lsm;EnableWal={wal};Transactions={transactions}";

        using var first = new WitDbConnection(cs);
        first.Open();
        Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        Execute(first, "INSERT INTO T (Id, V) VALUES (1, 'a')");

        var files = Directory.Exists(dir)
            ? string.Join(", ", Directory.GetFiles(dir).Select(Path.GetFileName))
            : "<none>";
        TestContext.Out.WriteLine($"PROBE  Q1 lsm <{label}> files on disk [{Platform}]  ->  {files}");

        using var second = new WitDbConnection(cs);
        var outcome = Observe(() => second.Open());

        Report($"Q1 lsm <{label}>, second connection [{Platform}]", outcome);

        if (!outcome.Threw)
        {
            Report($"Q1 lsm <{label}>, the second engine writes too [{Platform}]",
                Observe(() => Execute(second, "INSERT INTO T (Id, V) VALUES (2, 'b')")));
            Report($"Q1 lsm <{label}>, rows visible to the FIRST engine [{Platform}]",
                Observe(() => new Outcome(false, null, null, $"Int32:{CountRows(first, "SELECT Id FROM T")}").Value!));
            Report($"Q1 lsm <{label}>, rows visible to the SECOND engine [{Platform}]",
                Observe(() => new Outcome(false, null, null, $"Int32:{CountRows(second, "SELECT Id FROM T")}").Value!));
        }

        // One expectation for all four configurations and both platforms, which is the point. Before
        // 5.0.0 the answer varied on both axes, because the write-ahead log's share mode was the only
        // thing refusing anyone. Now a second connection shares the engine whatever the configuration,
        // and the files on disk are still reported because which files exist used to change the answer.
        Assert.That(outcome.Threw, Is.False,
            $"a second connection was refused with {label} on {Platform} - connections share an engine");
    }

    #endregion

    #region Q1 - the connection pool against a real database

    /// <summary>
    /// Probe: the pool exists to hand out several live connections to one connection string. Over a
    /// file-backed database, <c>Min Pool Size=2</c> asks it to open two at construction time.
    /// </summary>
    [Test]
    public void ProbePoolPreCreatesTwoFileConnectionsTest()
    {
        var cs = FileConnectionString("pool_min2.witdb") + ";Pooling=true;Min Pool Size=2;Max Pool Size=4";

        var outcome = Observe(() => ConnectionPool.GetPool(cs));
        Report("Q1 pool over a file, Min Pool Size=2", outcome);

        // CLOSED AS A SIDE EFFECT, which is worth recording because it was not the goal. The pool used
        // to fail in its own constructor over a file-backed database: it pre-opens MinPoolSize
        // connections, and the second hit the first one's exclusive handle. Nothing in the pool changed
        // - pooled connections are ordinary connections, and connections now share an engine, so
        // pre-creating several works. The pool is still unreferenced by the provider (§ 6), so no
        // consumer reaches this unless they construct it themselves.
        Assert.That(outcome.Threw, Is.False,
            "the pool must be constructible over a file-backed database");
    }

    /// <summary>
    /// Probe: two simultaneous borrows from a pool over a file-backed database - the per-request
    /// <c>DbContext</c> shape the phase-5 plan names as the demo deployment.
    /// </summary>
    [Test]
    public void ProbeTwoSimultaneousPoolBorrowsOverAFileTest()
    {
        var cs = FileConnectionString("pool_borrow.witdb") + ";Pooling=true;Min Pool Size=0;Max Pool Size=4";

        var pool = ConnectionPool.GetPool(cs);

        var first = Observe(() => pool.GetConnection());
        Report("Q1 pool over a file, first borrow", first);

        var second = Observe(() => pool.GetConnection());
        Report("Q1 pool over a file, second simultaneous borrow", second);

        // THE PHASE-5 PLAN CALLED THIS THE MOST CONSEQUENTIAL OPEN QUESTION IN THE PROJECT, and it is
        // now closed. Before: the first borrow succeeded and the second threw, so a web application
        // holding a per-request DbContext could not be served at all. Both borrows now work, because
        // the connections share one engine. Note what did NOT fix it: the pool. Sharing the engine is
        // what the shape needed, and the pool was only ever pooling the cheap half.
        Assert.Multiple(() =>
        {
            Assert.That(first.Threw, Is.False, "the first borrow should succeed");
            Assert.That(second.Threw, Is.False,
                "two simultaneous borrows over one file must work - this is the demo-deployment shape");
        });
    }

    /// <summary>
    /// Probe: what the existing pool tests actually establish. They all use <c>:memory:</c>; if two
    /// borrowed connections do not share a database then the pool's ~30 green tests say nothing
    /// about the property the pool exists for.
    /// </summary>
    [Test]
    public void ProbeTwoPoolBorrowsOverMemoryShareOneDatabaseTest()
    {
        const string cs = "Data Source=:memory:;Pooling=true;Min Pool Size=0;Max Pool Size=4";

        var pool = ConnectionPool.GetPool(cs);

        var first = pool.GetConnection();
        Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        Execute(first, "INSERT INTO T (Id, V) VALUES (1, 'a')");

        var second = pool.GetConnection();
        var outcome = Observe(() => Scalar(second, "SELECT V FROM T WHERE Id = 1"));

        Report("Q1 pool over :memory:, second borrow reads the first borrow's row", outcome);

        // PINS THE VACUITY OF THE EXISTING POOL SUITE. Measured 2026-07-30: "Table 'T' not found".
        // Every test in ConnectionPoolTests uses Data Source=:memory:, and two :memory: connections
        // are two separate databases - so a green pool suite says nothing about whether the pool can
        // hand out usable connections to one database. The property the pool exists for is untested,
        // and over a file it fails outright (see the two probes above).
        Assert.Multiple(() =>
        {
            Assert.That(ReferenceEquals(first, second), Is.False,
                "the pool handed out the same connection twice");
            Assert.That(outcome.Threw, Is.True,
                "pooled :memory: connections now share a database - the pool suite gains meaning "
                + "and Docs/PHASE5-CONCURRENCY-PLAN.md needs updating");
        });
    }

    #endregion

    #region Q3 - parallel mode

    /// <summary>
    /// Probe: the marker on <c>ConnectionStringWithMaxWritersTest</c> reads "Parallel Mode=Buffered
    /// causes SQL parsing issues - requires investigation". That is a note, not a verdict. This runs
    /// the shape and records what actually happens, statement by statement.
    /// </summary>
    [Test]
    [TestCase("Auto")]
    [TestCase("Buffered")]
    [TestCase("Latched")]
    [TestCase("Optimistic")]
    public void ProbeParallelModeOverAFileTest(string mode)
    {
        var path = Path.Combine(m_testDir, $"parallel_{mode}.witdb");
        var cs = $"Data Source={path};Parallel Mode={mode};Max Writers=4;Transactions=false";

        using var conn = new WitDbConnection(cs);

        var open = Observe(() => conn.Open());
        Report($"Q3 Parallel Mode={mode}, Open", open);
        if (open.Threw)
            return;

        var create = Observe(() => Execute(conn, "CREATE TABLE Data (K TEXT PRIMARY KEY, V TEXT)"));
        Report($"Q3 Parallel Mode={mode}, CREATE TABLE", create);
        if (create.Threw)
            return;

        var insert = Observe(() =>
        {
            for (var i = 0; i < 10; i++)
                Execute(conn, $"INSERT INTO Data (K, V) VALUES ('key{i}', 'value{i}')");
        });
        Report($"Q3 Parallel Mode={mode}, 10 INSERTs", insert);
        if (insert.Threw)
            return;

        var read = Observe(() => CountRows(conn, "SELECT K FROM Data"));
        Report($"Q3 Parallel Mode={mode}, rows actually returned by a scan", read);

        // THE VERDICT ON Q3, and it is "supported", not "unfinished experiment". Measured
        // 2026-07-30: all four modes open, take DDL, take ten INSERTs, and a scan returns all ten
        // rows - counted by reading them, not by COUNT(*). The one marker that suggested otherwise
        // named the wrong cause; see ControlTheMarkersCreateTableWithoutParallelModeTest.
        Assert.That(read.Value, Is.EqualTo("Int32:10"),
            $"Parallel Mode={mode} no longer round-trips ten rows");
    }

    /// <summary>
    /// Probe: the ignored test's own statements, verbatim - including the closing
    /// <c>SELECT COUNT(*)</c> that <see cref="ProbeParallelModeOverAFileTest"/> deliberately avoids.
    /// A marker is a claim about a specific shape, so the shape has to be the same one.
    /// </summary>
    [Test]
    public void ProbeBufferedParallelModeExactlyAsTheMarkerDescribesTest()
    {
        var path = Path.Combine(m_testDir, "max_writers.witdb");
        var cs = $"Data Source={path};Parallel Mode=Buffered;Max Writers=4;Transactions=false";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        var create = Observe(() => Execute(conn, "CREATE TABLE Data (Key TEXT PRIMARY KEY, Value TEXT)"));
        Report("Q3 marker replica, CREATE TABLE Data (Key TEXT PRIMARY KEY, Value TEXT)", create);
        if (create.Threw)
            return;

        var insert = Observe(() =>
        {
            for (var i = 0; i < 10; i++)
                Execute(conn, $"INSERT INTO Data (Key, Value) VALUES ('key{i}', 'value{i}')");
        });
        Report("Q3 marker replica, 10 INSERTs", insert);
        if (insert.Threw)
            return;

        var counted = Observe(() => Scalar(conn, "SELECT COUNT(*) FROM Data"));
        Report("Q3 marker replica, SELECT COUNT(*) FROM Data", counted);

        var scanned = Observe(() => CountRows(conn, "SELECT Key FROM Data"));
        Report("Q3 marker replica, rows a scan actually returns", scanned);

        // The marker's whole shape, end to end, after the PR 2 grammar fix. Both numbers, because on
        // this engine COUNT(*) is a cached counter and a scan is the rows - phase 4 learned that the
        // hard way, and here they agree.
        Assert.Multiple(() =>
        {
            Assert.That(create.Threw, Is.False, "the marker's CREATE TABLE must parse");
            Assert.That(scanned.Value, Is.EqualTo("Int32:10"), "ten rows were inserted");
            Assert.That(counted.Value, Is.EqualTo("Int64:10"), "and the cached count must agree");
        });
    }

    /// <summary>
    /// Attribution control for the marker replica above: the same <c>CREATE TABLE</c> with no
    /// parallel mode at all.
    /// </summary>
    /// <remarks>
    /// <b>This control is what settled the misattribution, and it has now flipped.</b> When first
    /// written it asserted <c>create.Threw</c> and passed: the statement failed with no parallel mode
    /// set, which is what proved the marker's reason string wrong. The grammar fix in PR 2 - <c>KEY</c>
    /// added to <c>nonReservedKeyword</c> - turned it red, and that red was the confirmation that the
    /// control depended on the defect rather than on something else. It now asserts the fixed
    /// behaviour, and the historical verdict stays here in prose so the record is not lost.
    /// </remarks>
    [Test]
    public void ControlTheMarkersCreateTableWithoutParallelModeTest()
    {
        var path = Path.Combine(m_testDir, "no_parallel.witdb");

        using var conn = new WitDbConnection($"Data Source={path};Transactions=false");
        conn.Open();

        var create = Observe(() => Execute(conn, "CREATE TABLE Data (Key TEXT PRIMARY KEY, Value TEXT)"));
        Report("Q3 attribution control, the same CREATE TABLE with no parallel mode", create);

        var renamed = Observe(() => Execute(conn, "CREATE TABLE Data2 (K TEXT PRIMARY KEY, Value TEXT)"));
        Report("Q3 attribution control, the same shape with the column renamed off 'Key'", renamed);

        Assert.Multiple(() =>
        {
            Assert.That(create.Threw, Is.False,
                "a column named Key must parse - it was the marker's actual cause, fixed in PR 2");
            Assert.That(renamed.Threw, Is.False,
                "the same shape with an ordinary column name must keep working");
        });
    }

    /// <summary>
    /// Probe: parallel mode over an <b>LSM</b> store, which is a different wrapper from every probe
    /// above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-07-30, and it exposes how narrow the earlier Q3 verdict was.</b> Every probe in
    /// this region runs with the default store, so <c>WitDatabaseBuilder</c> wraps <c>StoreBTree</c> in
    /// <c>BTreeConcurrentStore</c>. Only <c>Store=lsm</c> reaches <c>LsmParallelStore</c>, which buffers
    /// writes per thread and answers reads from the underlying store. So "parallel mode is supported"
    /// was measured on the wrapper that does not buffer, and said nothing about the one that does - a
    /// refutation is only as wide as what was actually run.
    /// </para>
    /// <para>
    /// Both spellings of the commit setting are run, because they are not equally protected:
    /// <c>MvccTransaction.Commit</c> flushes the store only <c>if (SynchronousCommit)</c>, and
    /// <c>LsmParallelStore.Flush</c> is what drains the write buffers. With the default
    /// <c>true</c> the flush hides the read path entirely; with <c>Synchronous Commit=false</c> - a
    /// connection-string setting, so a supported configuration - nothing drains them and the read path
    /// is on its own.
    /// </para>
    /// </remarks>
    [Test]
    [TestCase(true, TestName = "LsmParallel_SynchronousCommit")]
    [TestCase(false, TestName = "LsmParallel_AsynchronousCommit")]
    public void ProbeLsmParallelModeSeesItsOwnCommittedRowsTest(bool synchronousCommit)
    {
        var directory = Path.Combine(m_testDir, $"lsm_parallel_{synchronousCommit}");
        var cs = $"Data Source={directory};Store=lsm;Parallel Mode=Buffered;Max Writers=4;" +
                 $"Synchronous Commit={synchronousCommit}";

        using var conn = new WitDbConnection(cs);

        var open = Observe(() => conn.Open());
        Report($"Q3 LSM, Synchronous Commit={synchronousCommit}, Open", open);
        if (open.Threw)
            return;

        var create = Observe(() => Execute(conn, "CREATE TABLE Data (K TEXT PRIMARY KEY, V TEXT)"));
        Report($"Q3 LSM, Synchronous Commit={synchronousCommit}, CREATE TABLE", create);
        if (create.Threw)
            return;

        var insert = Observe(() =>
        {
            for (var i = 0; i < 10; i++)
                Execute(conn, $"INSERT INTO Data (K, V) VALUES ('key{i}', 'value{i}')");
        });
        Report($"Q3 LSM, Synchronous Commit={synchronousCommit}, 10 INSERTs", insert);
        if (insert.Threw)
            return;

        // Counted by reading the rows, never by COUNT(*) - on this engine that is a cached counter,
        // and phase 4 published a false catastrophe by trusting it.
        var scannedCount = CountRows(conn, "SELECT K FROM Data");
        TestContext.Out.WriteLine(
            $"PROBE  Q3 LSM, Synchronous Commit={synchronousCommit}, rows a scan returns  ->  {scannedCount}");

        var single = Observe(() => Scalar(conn, "SELECT V FROM Data WHERE K = 'key7'"));
        Report($"Q3 LSM, Synchronous Commit={synchronousCommit}, single-key lookup of key7", single);

        // LOSS OR INVISIBILITY? Closing the connection disposes the engine, which drains every write
        // buffer on the way out. If the rows appear after a reopen they were written and merely
        // unreadable; if they are still missing they never reached the store at all. The distinction
        // decides whether this is a visibility defect or a data-loss one, and it is not guessable.
        conn.Close();

        using var reopened = new WitDbConnection(cs);
        reopened.Open();
        var afterReopenCount = CountRows(reopened, "SELECT K FROM Data");
        TestContext.Out.WriteLine(
            $"PROBE  Q3 LSM, Synchronous Commit={synchronousCommit}, rows after close and reopen  ->  " +
            $"{afterReopenCount}");

        // WHICH rows survived is the informative part: the first, the last, or an arbitrary subset says
        // different things about where the writes go.
        var surviving = new List<string>();
        using (var cmd = reopened.CreateCommand())
        {
            cmd.CommandText = "SELECT K FROM Data";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                surviving.Add(reader.GetString(0));
        }
        TestContext.Out.WriteLine(
            $"PROBE  Q3 LSM, Synchronous Commit={synchronousCommit}, surviving keys  ->  " +
            $"[{string.Join(",", surviving.OrderBy(k => k))}]");

        // ===================================================================================
        // PINS A DEFECT, NOT CORRECT BEHAVIOUR.
        //
        // Measured 2026-07-30, ten INSERTs every one of which reported success:
        //
        //   Synchronous Commit=True (the DEFAULT)  scan 1, after reopen 1, surviving [key0]
        //   ...and on a second run of the same code, 0 / 0 / []
        //   Synchronous Commit=False               scan 0, after reopen 0, surviving []
        //
        // The reopen is what makes the verdict, and it is the harsher one: closing the connection
        // disposes the engine and drains every buffer, so rows still absent afterwards were never
        // written. This is LOST DATA in a supported configuration reachable from a connection string -
        // not the "Get returns null" visibility problem the marker describes.
        //
        // HOW MANY survive is deliberately NOT pinned. It came out 1 on one run and 0 on the next, so
        // an exact figure would be a timing-dependent gate, and this project has already had CI inherit
        // one of those. What is pinned is the part that was stable across runs: rows are lost, both
        // views agree, and the survivors are a prefix of what was written.
        //
        // INVERT TO: scanned == 10, afterReopen == 10, single == "String:value7", surviving == all ten.
        // That inversion is the proof the fix landed.
        // ===================================================================================
        Assert.Multiple(() =>
        {
            Assert.That(scannedCount, Is.LessThan(10),
                "PINNED OBSERVATION: acknowledged INSERTs are missing; correct is 10");
            Assert.That(single.Value, Is.EqualTo("<null>"),
                "PINNED OBSERVATION: the single-key lookup finds nothing; correct is String:value7");
            Assert.That(afterReopenCount, Is.EqualTo(scannedCount),
                "PINNED OBSERVATION: a clean close and reopen recovers nothing, so the rows were lost " +
                "rather than hidden - this is the assertion that makes it a data-loss verdict");
            Assert.That(surviving, Is.EqualTo(
                    Enumerable.Range(0, scannedCount).Select(i => $"key{i}").ToArray()),
                "PINNED OBSERVATION: the survivors are the FIRST rows written, not the last - the clue " +
                "to where the writes go; correct is all ten keys");
        });
    }

    /// <summary>
    /// Attribution control for the probe above: the same LSM store with <b>no</b> parallel mode.
    /// </summary>
    /// <remarks>
    /// Without this, "Store=lsm loses rows" and "the parallel wrapper loses rows" are indistinguishable,
    /// and the first would be a far larger claim. Together with
    /// <see cref="ProbeParallelModeOverAFileTest"/>, which runs <c>Parallel Mode=Buffered</c> over the
    /// default btree store and returns all ten rows, this brackets the defect to exactly one component:
    /// <c>LsmParallelStore</c>, the wrapper only <c>Store=lsm</c> reaches.
    /// </remarks>
    [Test]
    public void ControlLsmWithoutParallelModeKeepsEveryRowTest()
    {
        var directory = Path.Combine(m_testDir, "lsm_no_parallel");
        var cs = $"Data Source={directory};Store=lsm";

        using var conn = new WitDbConnection(cs);
        conn.Open();
        Execute(conn, "CREATE TABLE Data (K TEXT PRIMARY KEY, V TEXT)");

        for (var i = 0; i < 10; i++)
            Execute(conn, $"INSERT INTO Data (K, V) VALUES ('key{i}', 'value{i}')");

        var scanned = Observe(() => CountRows(conn, "SELECT K FROM Data"));
        Report("Q3 LSM attribution control, no parallel mode, rows a scan returns", scanned);

        var single = Observe(() => Scalar(conn, "SELECT V FROM Data WHERE K = 'key7'"));
        Report("Q3 LSM attribution control, no parallel mode, single-key lookup of key7", single);

        Assert.Multiple(() =>
        {
            Assert.That(scanned.Value, Is.EqualTo("Int32:10"),
                "an LSM store without the parallel wrapper must keep every row");
            Assert.That(single.Value, Is.EqualTo("String:value7"),
                "and must find one by key");
        });
    }

    #endregion

    #region Q1 - read-only, the other spelling

    /// <summary>
    /// Probe: <c>Mode=ReadOnly</c> rather than <c>Read Only=true</c>. Both are accepted by
    /// <see cref="WitDbConnectionStringBuilder"/>, and a consumer may reach for either.
    /// </summary>
    [Test]
    public void ProbeModeReadOnlyIsHonouredTest()
    {
        var path = Path.Combine(m_testDir, "mode_readonly.witdb");

        using (var seed = new WitDbConnection($"Data Source={path}"))
        {
            seed.Open();
            Execute(seed, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        }

        using var reader = new WitDbConnection($"Data Source={path};Mode=ReadOnly");
        var open = Observe(() => reader.Open());
        Report("Q1 Mode=ReadOnly, Open", open);
        if (open.Threw)
            return;

        var write = Observe(() => Execute(reader, "INSERT INTO T (Id, V) VALUES (1, 'a')"));
        Report("Q1 Mode=ReadOnly, a write through it", write);

        // INVERTED BY THE FIX. Both spellings used to be dropped; both are honoured now, and the second
        // spelling mattered - a consumer reaches for either, and fixing only `Read Only=true` would have
        // left a silent hole behind the one that looks more like SQLite's.
        Assert.That(write.Threw, Is.True, "Mode=ReadOnly must refuse writes too");
    }

    /// <summary>
    /// Probe: after a second opener has been refused, is the first connection still usable, and is
    /// the refused one left holding anything? A failed <c>Open</c> that leaks a handle would show up
    /// as the file still being locked once the first connection closes.
    /// </summary>
    [Test]
    public void ProbeRefusedOpenLeavesNothingBehindTest()
    {
        var cs = FileConnectionString("refused_open.witdb");

        object? firstStillWorks;

        using (var first = new WitDbConnection(cs))
        {
            first.Open();
            Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
            Execute(first, "INSERT INTO T (Id, V) VALUES (1, 'a')");

            using var second = new WitDbConnection(cs);
            var refused = Observe(() => second.Open());
            Report("Q1 second open, formerly refused", refused);

            // Was "a refused Open leaves nothing behind" - the second connection is no longer refused,
            // so what this now checks is the other half it always checked: that opening a second
            // connection does not disturb the first, and that the file is fully released afterwards.
            Assert.Multiple(() =>
            {
                Assert.That(refused.Threw, Is.False, "a second connection shares the engine");
                Assert.That(second.State, Is.EqualTo(ConnectionState.Open));
            });

            firstStillWorks = Scalar(first, "SELECT V FROM T WHERE Id = 1");
        }

        Report("Q1 refused open, the first connection afterwards",
            new Outcome(false, null, null, Describe(firstStillWorks)));

        using var reopened = new WitDbConnection(cs);
        var reopen = Observe(() => reopened.Open());
        Report("Q1 refused open, reopening once the first connection is disposed", reopen);
        Assert.That(reopen.Threw, Is.False, "the refused opener leaked a handle on the file");
    }

    #endregion

    #region Tools

    /// <summary>
    /// Named in every report line, because one row of the model differs by platform and a verdict
    /// without the platform on it is what made the first version of the LSM probe wrong.
    /// </summary>
    private static string Platform => OperatingSystem.IsWindows() ? "windows" : "unix";

    private string FileConnectionString(string fileName) =>
        $"Data Source={Path.Combine(m_testDir, fileName)}";

    private static void Execute(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    /// <summary>
    /// Counts rows by reading them, never by <c>COUNT(*)</c>: this engine answers <c>COUNT(*)</c>
    /// from a cached per-table counter, which is separate state. Phase 4 published a false
    /// catastrophe by trusting it.
    /// </summary>
    private static int CountRows(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        var count = 0;
        while (reader.Read())
            count++;

        return count;
    }

    private static Outcome Observe(Action action)
    {
        return Observe<object?>(() =>
        {
            action();
            return null;
        });
    }

    private static Outcome Observe<T>(Func<T> func)
    {
        try
        {
            var value = func();
            return new Outcome(false, null, null, Describe(value));
        }
        catch (Exception e)
        {
            return new Outcome(true, e.GetType().Name, e.Message, null);
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        byte[] bytes => $"byte[{bytes.Length}]",
        _ => $"{value.GetType().Name}:{value}"
    };

    private static void Report(string question, Outcome outcome)
    {
        var text = new StringBuilder();
        text.Append("PROBE  ").Append(question).Append("  ->  ");

        if (outcome.Threw)
            text.Append("THREW ").Append(outcome.ExceptionType).Append(": ").Append(Flatten(outcome.Message));
        else
            text.Append("OK, value ").Append(outcome.Value);

        TestContext.Out.WriteLine(text.ToString());
    }

    private static string Flatten(string? message) =>
        message?.Replace("\r", " ").Replace("\n", " ") ?? "";

    private sealed record Outcome(bool Threw, string? ExceptionType, string? Message, string? Value);

    #endregion
}
