using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Phase 5 instrument A, core half - what the builder actually wires for concurrency control.
/// </summary>
/// <remarks>
/// <see cref="FileLock"/> exists, is documented as the cross-process mechanism, and
/// <see cref="LockManager"/> has a constructor that takes a database path in order to use it. The
/// question this fixture settles by execution is whether any configuration a consumer can ask for
/// ever reaches that constructor, and what the option named <c>FileLocking</c> actually turns off.
///
/// The write-serialisation probes use the parked-collaborator technique rather than a stress loop: a
/// store decorator blocks inside <c>Put</c>, so "a second writer entered while the first was still
/// inside" is an exact observation instead of a lucky interleaving. The background body is wrapped,
/// because an unhandled exception on a background thread takes the whole test process down.
///
/// Controls: <see cref="ControlFileLockCreatesItsSidecarWhenUsedDirectlyTest"/> proves the probe can
/// see a <c>.lock</c> file when one is made, and
/// <see cref="ControlParkedStoreLetsOneWriterThroughTest"/> proves the parked store does not simply
/// block everything.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class ConcurrencyModelWiringProbeTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_wiring_probe_{Guid.NewGuid():N}");
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
    /// Control: used directly, <see cref="FileLock"/> creates a <c>.lock</c> sidecar next to the
    /// database. Without this, "no sidecar exists" would not be evidence of anything.
    /// </summary>
    [Test]
    public void ControlFileLockCreatesItsSidecarWhenUsedDirectlyTest()
    {
        var path = Path.Combine(m_testDir, "control_lock.witdb");

        using (var fileLock = new FileLock(path))
        {
            fileLock.AcquireExclusiveLock(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(fileLock.HasExclusiveLock, Is.True);
                Assert.That(File.Exists(path + ".lock"), Is.True,
                    "FileLock is supposed to create its sidecar; the probe below relies on it");
            });
        }

        Report("the sidecar FileLock creates when used directly", path + ".lock", File.Exists(path + ".lock"));
    }

    /// <summary>
    /// Control: the parked store admits the first writer and blocks it there. If this fails, the
    /// serialisation probes are measuring the decorator rather than the lock manager.
    /// </summary>
    [Test]
    public void ControlParkedStoreLetsOneWriterThroughTest()
    {
        using var parked = new ParkedStore();
        using var database = new WitDatabaseBuilder().WithStore(parked).WithTransactions().Build();

        var writer = RunOnBackgroundThread(() => database.Put("a", Bytes("1")));

        Assert.That(parked.WaitUntilInside(1, TimeSpan.FromSeconds(5)), Is.True,
            "the first writer never reached the store");

        parked.Release();
        Assert.That(writer.Wait(TimeSpan.FromSeconds(5)), Is.True, "the first writer never returned");
        Assert.That(writer.Error, Is.Null, $"the writer threw: {writer.Error}");
    }

    #endregion

    #region Q1/Q4 - is the cross-process FileLock reachable at all?

    /// <summary>
    /// Probe: open a file-backed database the ordinary way, write through it, and look for the
    /// <c>.lock</c> sidecar that <see cref="FileLock"/> would have created. File locking is on by
    /// default, so if the sidecar is absent the cross-process path was never taken.
    /// </summary>
    [Test]
    public void ProbeFileBackedDatabaseCreatesTheLockSidecarTest()
    {
        var path = Path.Combine(m_testDir, "wired.witdb");

        using (var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithTransactions().Build())
        {
            database.Put("a", Bytes("1"));
            database.Flush();
        }

        var sidecar = path + ".lock";
        Report("the sidecar after an ordinary write to a file-backed database", sidecar, File.Exists(sidecar));

        var siblings = Directory.GetFiles(m_testDir).Select(Path.GetFileName).ToArray();
        TestContext.Out.WriteLine($"PROBE  files left in the database directory  ->  {string.Join(", ", siblings)}");

        // INVERTED BY THE 5.0.0 FIX, and the inversion is the proof it landed. This assertion used to
        // read Is.False and pass: FileLock was unreachable from every configuration, because the
        // builder called the LockManager constructor whose own summary reads "for in-memory databases
        // (no file locking)". The exclusivity guard now takes the sidecar before any database file is
        // opened, so it exists for the lifetime of the engine.
        Assert.That(File.Exists(sidecar), Is.True,
            "the exclusivity guard did not take its lock - a second engine could open this database");
    }

    /// <summary>
    /// Probe: the same question asked of the whole reachable configuration surface. Every one of
    /// these is a configuration a consumer can ask for; the probe records which of them produce a
    /// lock sidecar.
    /// </summary>
    [Test]
    [TestCase("btree, transactions", false, false)]
    [TestCase("btree, transactions, mvcc", true, false)]
    [TestCase("btree, transactions, file locking off", false, true)]
    public void ProbeLockSidecarAcrossConfigurationsTest(string label, bool mvcc, bool withoutFileLocking)
    {
        var path = Path.Combine(m_testDir, $"cfg_{label.GetHashCode():x8}.witdb");

        var builder = new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithTransactions();

        if (mvcc)
            builder = builder.WithMvcc();

        if (withoutFileLocking)
            builder = builder.WithoutFileLocking();

        using (var database = builder.Build())
        {
            database.Put("a", Bytes("1"));
            database.Flush();
        }

        Report($"the sidecar for <{label}>", path + ".lock", File.Exists(path + ".lock"));

        // The guard is on for every configuration except the one that explicitly turns it off, which
        // is now the ONLY job EnableFileLocking has. Note the sidecar outlives the engine - the lock is
        // released on Dispose, the file is left behind, and its presence says nothing about whether
        // anyone holds it.
        Assert.That(File.Exists(path + ".lock"), Is.EqualTo(!withoutFileLocking),
            $"unexpected sidecar state for <{label}>");
    }

    /// <summary>
    /// Probe: through the Core builder, can an LSM database exist with no write-ahead log - and if so,
    /// does anything then stop a second engine opening it?
    /// </summary>
    /// <remarks>
    /// § 3a established that LSM exclusivity comes from <c>wal.log</c> alone. Through the ADO.NET
    /// provider a <c>wal.log</c> appears in all four <c>EnableWal</c>/<c>Transactions</c> combinations,
    /// so the provider cannot produce a log-less database. <see cref="WitDatabaseBuilder"/> is public
    /// API too, and <c>LsmOptions.EnableWal</c> is a settable property - so the question has to be
    /// asked here as well, because the answer decides whether fixing the log's share mode is a
    /// sufficient fix or only the common case.
    /// </remarks>
    /// <remarks>
    /// The <c>EnableWal=false</c> case was <c>[Ignore]</c>d as a confirmed defect and is now closed by
    /// the 5.0.0 exclusivity guard. Before the fix that directory held only <c>sst_000000.sst</c> and a
    /// second engine opened it - on Windows, so unlike § 3a it was never a Unix-only problem.
    /// </remarks>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void ProbeLsmWithoutWalIsStillExclusiveTest(bool enableWal)
    {
        var dir = Path.Combine(m_testDir, $"lsm_core_{enableWal}");

        using var first = new WitDatabaseBuilder()
            .WithLsmTree(dir, o => o.EnableWal = enableWal)
            .WithoutTransactions()
            .Build();

        first.Put("a", Bytes("1"));
        first.Flush();

        var files = Directory.Exists(dir)
            ? string.Join(", ", Directory.GetFiles(dir).Select(Path.GetFileName))
            : "<none>";
        Report($"files for an LSM store with EnableWal={enableWal}", "directory", files);

        Exception? refused = null;
        WitDatabase? second = null;

        try
        {
            second = new WitDatabaseBuilder()
                .WithLsmTree(dir, o => o.EnableWal = enableWal)
                .WithoutTransactions()
                .Build();
        }
        catch (Exception e)
        {
            refused = e;
        }
        finally
        {
            second?.Dispose();
        }

        Report($"second LSM engine with EnableWal={enableWal}", "outcome",
            refused is null ? "OPENED" : $"refused with {refused.GetType().Name}");

        // ASSERTS CORRECT BEHAVIOUR. A single-process design must refuse the second engine whatever
        // files the database happens to contain - which is precisely why the guard cannot be a share
        // mode on one of those files.
        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.Not.Null,
                $"a second LSM engine opened the same directory with EnableWal={enableWal}, so "
                + "exclusivity depends on which files exist rather than on the model");
            Assert.That(refused, Is.TypeOf<DatabaseAlreadyOpenException>(),
                "the refusal must name the engine's own limit, not surface an OS sharing violation");
        });
    }

    #endregion

    #region Q1 - what FileLocking=false actually turns off

    /// <summary>
    /// Probe: with file locking on - the default - is a second writer serialised behind the first?
    /// </summary>
    [Test]
    public void ProbeSecondWriterIsSerialisedWithFileLockingOnTest()
    {
        var entered = ProbeConcurrentWriters(withoutFileLocking: false);
        Report("writers inside the store at once, file locking ON (the default)", "count", entered);

        Assert.That(entered, Is.EqualTo(1),
            "the default configuration must serialise writers");
    }

    /// <summary>
    /// Probe: and with <c>FileLocking=false</c>, which reads as "do not coordinate across
    /// processes". The builder uses the flag to decide whether a <see cref="LockManager"/> exists at
    /// all, and both transactional stores document <c>null</c> as "no locking".
    /// </summary>
    [Test]
    public void ProbeSecondWriterIsSerialisedWithFileLockingOffTest()
    {
        var entered = ProbeConcurrentWriters(withoutFileLocking: true);
        Report("writers inside the store at once, FileLocking=false", "count", entered);

        // INVERTED BY THE 5.0.0 FIX. This used to read Is.EqualTo(2) and pass: the flag decided
        // whether a LockManager existed at all, and both transactional stores treat null as "no
        // locking", so a setting that reads "do not coordinate across processes" removed the mutual
        // exclusion between two threads writing the same store. The two jobs are now separate - a
        // lock manager is built unconditionally, and EnableFileLocking controls only the exclusive
        // database lock.
        Assert.That(entered, Is.EqualTo(1),
            "FileLocking=false admits two concurrent writers again - the flag has gone back to "
            + "deciding whether a LockManager exists");
    }

    /// <summary>
    /// Drives two writers at a store that parks the first one inside <c>Put</c>, and reports how
    /// many got in. One means the writes are serialised; two means they are not.
    /// </summary>
    private static int ProbeConcurrentWriters(bool withoutFileLocking)
    {
        using var parked = new ParkedStore();

        var builder = new WitDatabaseBuilder().WithStore(parked).WithTransactions();
        if (withoutFileLocking)
            builder = builder.WithoutFileLocking();

        using var database = builder.Build();

        var first = RunOnBackgroundThread(() => database.Put("a", Bytes("1")));

        if (!parked.WaitUntilInside(1, TimeSpan.FromSeconds(5)))
        {
            parked.Release();
            Assert.Fail("the first writer never reached the store, so nothing was measured");
        }

        var second = RunOnBackgroundThread(() => database.Put("b", Bytes("2")));

        // Thread-start latency must not be part of the measurement: on a loaded runner it would make
        // an unserialised second writer look serialised, which is a timing-dependent gate of exactly
        // the kind phase 3 removed. So wait for the second thread to be running first, and only then
        // give it a budget to get inside the store.
        Assert.That(second.Started.Wait(TimeSpan.FromSeconds(30)), Is.True,
            "the second writer's thread never started, so nothing was measured");

        // If writes are serialised the second writer is blocked outside the store and this wait
        // times out; if they are not, it walks in while the first is still parked. The budget is
        // deliberately generous - a serialised writer stays out however long it is given, so a long
        // wait costs time in the serialised case and buys robustness in the other.
        var bothInside = parked.WaitUntilInside(2, TimeSpan.FromSeconds(10));
        var entered = parked.MaxConcurrentlyInside;
        var threads = parked.DistinctThreads;

        parked.Release();
        first.Wait(TimeSpan.FromSeconds(5));
        second.Wait(TimeSpan.FromSeconds(5));

        TestContext.Out.WriteLine(
            $"PROBE    both inside = {bothInside}, distinct threads seen inside = {threads}, " +
            $"first error = {first.Error?.GetType().Name ?? "none"}, " +
            $"second error = {second.Error?.GetType().Name ?? "none"}");

        Assert.That(threads, Is.GreaterThan(0), "no thread reached the store");

        return entered;
    }

    #endregion

    #region Tools

    private static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private static void Report(string question, string subject, object observed) =>
        TestContext.Out.WriteLine($"PROBE  {question}  ->  {subject} = {observed}");

    private static BackgroundRun RunOnBackgroundThread(Action action)
    {
        var run = new BackgroundRun();

        var thread = new Thread(() =>
        {
            run.Started.Set();

            try
            {
                action();
            }
            catch (Exception e)
            {
                // An unhandled exception on a background thread ends the process and takes every
                // other test with it, so it is captured and asserted on instead.
                run.Error = e;
            }
            finally
            {
                run.Finished.Set();
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        return run;
    }

    private sealed class BackgroundRun
    {
        /// <summary>
        /// Set as the first act of the thread body, so a probe can take thread-start latency out of
        /// whatever it is timing.
        /// </summary>
        public ManualResetEventSlim Started { get; } = new(false);

        public ManualResetEventSlim Finished { get; } = new(false);

        public Exception? Error { get; set; }

        public bool Wait(TimeSpan timeout) => Finished.Wait(timeout);
    }

    /// <summary>
    /// A store decorator that parks every writer inside <c>Put</c> until released, and records how
    /// many were inside at once and on how many distinct threads.
    /// </summary>
    private sealed class ParkedStore : IKeyValueStore
    {
        private readonly Dictionary<byte[], byte[]> m_values = new();
        private readonly ManualResetEventSlim m_release = new(false);
        private readonly ManualResetEventSlim m_someoneInside = new(false);
        private readonly Lock m_lock = new();
        private readonly HashSet<int> m_threads = new();

        private int m_inside;
        private int m_maxInside;

        public int MaxConcurrentlyInside
        {
            get
            {
                lock (m_lock)
                    return m_maxInside;
            }
        }

        public int DistinctThreads
        {
            get
            {
                lock (m_lock)
                    return m_threads.Count;
            }
        }

        public void Release() => m_release.Set();

        /// <summary>
        /// Waits until at least <paramref name="count"/> writers are inside <c>Put</c> at the same
        /// time.
        /// </summary>
        public bool WaitUntilInside(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                lock (m_lock)
                {
                    if (m_inside >= count)
                        return true;
                }

                m_someoneInside.Wait(TimeSpan.FromMilliseconds(20));
            }

            lock (m_lock)
                return m_inside >= count;
        }

        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            var keyCopy = key.ToArray();
            var valueCopy = value.ToArray();

            lock (m_lock)
            {
                m_inside++;
                m_maxInside = Math.Max(m_maxInside, m_inside);
                m_threads.Add(Environment.CurrentManagedThreadId);
            }

            m_someoneInside.Set();

            m_release.Wait(TimeSpan.FromSeconds(20));

            lock (m_lock)
            {
                m_values[keyCopy] = valueCopy;
                m_inside--;
            }
        }

        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            var keyCopy = key.ToArray();

            lock (m_lock)
            {
                foreach (var pair in m_values)
                {
                    if (pair.Key.AsSpan().SequenceEqual(keyCopy))
                        return pair.Value;
                }
            }

            return null;
        }

        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Get(key));

        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            Put(key, value);
            return ValueTask.CompletedTask;
        }

        public bool Delete(ReadOnlySpan<byte> key)
        {
            var keyCopy = key.ToArray();

            lock (m_lock)
            {
                foreach (var pair in m_values)
                {
                    if (pair.Key.AsSpan().SequenceEqual(keyCopy))
                        return m_values.Remove(pair.Key);
                }
            }

            return false;
        }

        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Delete(key));

        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
        {
            List<(byte[], byte[])> snapshot;

            lock (m_lock)
                snapshot = m_values.Select(p => (p.Key, p.Value)).ToList();

            return snapshot;
        }

        public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
            byte[]? startKey,
            byte[]? endKey,
            CancellationToken cancellationToken = default)
        {
            foreach (var pair in Scan(startKey, endKey))
            {
                yield return pair;
                await Task.Yield();
            }
        }

        public void Flush() { }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public string ProviderKey => "parked";

        public void Dispose()
        {
            m_release.Set();
            m_release.Dispose();
            m_someoneInside.Dispose();
        }
    }

    #endregion
}
