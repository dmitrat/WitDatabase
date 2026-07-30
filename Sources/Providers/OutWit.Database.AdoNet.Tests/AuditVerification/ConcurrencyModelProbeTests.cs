using System.Data;
using System.Data.Common;
using System.Text;
using OutWit.Database.AdoNet.Pool;

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
        Assert.That(outcome.Threw, Is.True, "expected the second opener to be refused");
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

        if (!outcome.Threw)
        {
            var read = Observe(() => Scalar(second, "SELECT V FROM T WHERE Id = 1"));
            Report($"Q1 lsm, the second connection reads a row the first wrote [{Platform}]", read);
        }

        if (OperatingSystem.IsWindows())
        {
            // PINS AN OBSERVATION. Measured 2026-07-30 on Windows: IOException on <dir>/wal.log, not
            // on the store. The LSM store's own files are shareable - SSTableReader opens
            // FileShare.Read - so exclusivity comes from the write-ahead log, which matters because
            // the failure lands part-way through opening the engine.
            Assert.Multiple(() =>
            {
                Assert.That(outcome.Threw, Is.True,
                    "a second LSM connection now opens on Windows too - update the plan doc");
                Assert.That(outcome.Message, Does.Contain("wal.log"),
                    "LSM exclusivity no longer comes from the WAL; re-establish where it does");
            });

            return;
        }

        // PINS A DEFECT, and a data-corruption one. Measured 2026-07-30 on the Linux CI runner: the
        // second connection OPENED and read the first connection's row. Two independent engines are
        // then live over one LSM directory, each with its own memtable and its own handle on the same
        // write-ahead log, and nothing coordinates them - see
        // ProbeTwoLsmConnectionsBothWriteTest for what that costs. The fix inverts this assertion.
        Assert.That(outcome.Threw, Is.False,
            "a second LSM connection is now refused on Unix - if that is the fix, invert this "
            + "assertion and close the marker");
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

            if (opened.Threw)
            {
                Assert.That(OperatingSystem.IsWindows(), Is.True,
                    "the second connection was refused on a platform where it had opened before");
                return;
            }

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

            // PINS THE DEFECT'S ACTUAL SHAPE. Measured 2026-07-30 on Linux: 1 and 2. The second
            // engine sees both rows because it replayed wal.log at open, which already held the
            // first engine's row; the first engine cannot see the second's row at all, because it
            // lives in a memtable belonging to a different engine and nothing invalidates or
            // notifies. So the cost of the missing exclusivity is not (in this case) a lost write -
            // it is two engines holding divergent views of one database, each answering confidently.
            Assert.Multiple(() =>
            {
                Assert.That(seenByFirst, Is.EqualTo(1),
                    "the first engine's view changed - re-establish what the two engines see");
                Assert.That(seenBySecond, Is.EqualTo(2),
                    "the second engine's view changed - re-establish what the two engines see");
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

        // PINS A DEFECT, NOT CORRECT BEHAVIOUR. Measured 2026-07-30: the INSERT succeeded and the
        // row reads back as 'b'. WitDbConnection never reads options.ReadOnly - ConfigureStorage
        // only asks whether the mode is Memory - so the setting is parsed and dropped. When it is
        // honoured, the write must throw and these two assertions invert.
        Assert.Multiple(() =>
        {
            Assert.That(write.Threw, Is.False,
                "a read-only connection now refuses writes - invert this and close the marker");
            Assert.That(rows.Value, Is.EqualTo("String:b"),
                "the write no longer lands - invert this and close the marker");
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

        // PINS A DEFECT, downstream of the one above. StorageFile does grant FileShare.Read when
        // opened read-only, so many readers over one file is a shape the storage layer already
        // supports; it is unreachable only because the provider drops the setting. Measured
        // 2026-07-30: IOException on the second opener.
        Assert.That(outcome.Threw, Is.True,
            "two read-only connections now share a file - invert this and close the marker");
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

        // Through the provider a wal.log appears in ALL FOUR combinations - measured 2026-07-30,
        // including with EnableWal=false and Transactions=false - so the provider cannot produce a
        // log-less LSM database and the share mode of the log governs every case. Which platform is
        // being measured therefore decides the expectation, exactly as in
        // ProbeSecondConnectionToSameLsmDirectoryTest.
        Assert.That(files, Does.Contain("wal.log"),
            $"no wal.log for {label}: the provider can now produce a log-less LSM database, which "
            + "has no exclusivity on any platform - see ProbeLsmWithoutWalIsStillExclusiveTest");

        if (OperatingSystem.IsWindows())
        {
            Assert.That(outcome.Threw, Is.True,
                $"a second engine opened an LSM database with {label} on Windows");
            return;
        }

        // PINS THE SECTION 3a DEFECT across all four configurations: on Unix FileShare.Read is a
        // shared advisory lock, so the log refuses nobody. The fix inverts this.
        Assert.That(outcome.Threw, Is.False,
            $"a second engine is now refused with {label} on Unix - if that is the fix, invert this "
            + "assertion and close the marker");
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

        // PINS A DEFECT. Measured 2026-07-30: IOException, "the process cannot access the file
        // ... because it is being used by another process." The pool's constructor pre-opens
        // MinPoolSize connections; the second one hits the first one's FileShare.None handle. So the
        // pool cannot be constructed at all over a file-backed database with Min Pool Size >= 2.
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Threw, Is.True,
                "the pool now pre-creates two file connections - invert this and close the marker");
            Assert.That(outcome.ExceptionType, Is.EqualTo(nameof(IOException)),
                "the failure changed shape; re-read what it is now before trusting this test");
        });
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

        // PINS A DEFECT, and it is the one the phase-5 plan calls the most consequential open
        // question in the project. Measured 2026-07-30: the first borrow succeeds, the second throws
        // IOException. A web application holding a per-request DbContext borrows more than one
        // connection at a time, so the pool cannot serve the demo-deployment shape at all. Nothing
        // in the provider references ConnectionPool either, so no consumer reaches this today.
        Assert.Multiple(() =>
        {
            Assert.That(first.Threw, Is.False, "the first borrow should succeed");
            Assert.That(second.Threw, Is.True,
                "two simultaneous borrows over a file now work - invert this and close the marker");
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

        // PINS A DEFECT. Measured 2026-07-30: the write succeeded. Both spellings of read-only are
        // dropped, so neither is a way to open a second reader over one file.
        Assert.That(write.Threw, Is.False,
            "Mode=ReadOnly now refuses writes - invert this and close the marker");
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
            Report("Q1 refused open, the exception", refused);

            Assert.That(second.State, Is.EqualTo(ConnectionState.Closed),
                "a connection whose Open failed should report Closed");

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
