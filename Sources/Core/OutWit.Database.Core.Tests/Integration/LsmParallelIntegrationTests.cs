using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Integration;

/// <summary>
/// Integration tests comparing regular vs parallel LSM store performance.
/// </summary>
[TestFixture]
public class LsmParallelIntegrationTests : IDisposable
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"lsm_integration_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch { }
    }

    #endregion

    #region Factory Tests

    [Test]
    public void FactoryCreatesRegularStoreWithNoneModeTest()
    {
        var dir = Path.Combine(m_testDir, "regular");
        var options = new ParallelModeOptions { Mode = ParallelMode.None };

        using var store = KeyValueStoreFactory.CreateLsmStore(dir, options);

        Assert.That(store, Is.TypeOf<StoreLsm>());
        Assert.That(store.ProviderKey, Is.EqualTo("lsm"));
    }

    [Test]
    public void FactoryCreatesParallelStoreWithBufferedModeTest()
    {
        var dir = Path.Combine(m_testDir, "buffered");
        var options = new ParallelModeOptions { Mode = ParallelMode.Buffered };

        using var store = KeyValueStoreFactory.CreateLsmStore(dir, options);

        Assert.That(store, Is.TypeOf<LsmParallelStore>());
        Assert.That(store.ProviderKey, Is.EqualTo("lsm-parallel"));
    }

    [Test]
    public void FactoryCreatesParallelStoreWithAutoModeTest()
    {
        var dir = Path.Combine(m_testDir, "auto");
        var options = new ParallelModeOptions { Mode = ParallelMode.Auto };

        using var store = KeyValueStoreFactory.CreateLsmStore(dir, options);

        Assert.That(store, Is.TypeOf<LsmParallelStore>());
    }

    #endregion

    #region Functional Equivalence Tests

    [Test]
    public void RegularAndParallelStoreHaveSameBehaviorTest()
    {
        var regularDir = Path.Combine(m_testDir, "eq_regular");
        var parallelDir = Path.Combine(m_testDir, "eq_parallel");

        using var regularStore = KeyValueStoreFactory.CreateLsmStore(regularDir, 
            new ParallelModeOptions { Mode = ParallelMode.None });
        using var parallelStore = KeyValueStoreFactory.CreateLsmStore(parallelDir, 
            new ParallelModeOptions { Mode = ParallelMode.Buffered });

        // Insert same data into both stores
        for (int i = 0; i < 100; i++)
        {
            var key = ToBytes($"key_{i:D5}");
            var value = ToBytes($"value_{i}");

            regularStore.Put(key, value);
            parallelStore.Put(key, value);
        }

        // Flush to ensure all data is written
        regularStore.Checkpoint();
        parallelStore.Checkpoint();

        // Verify both stores have same data
        for (int i = 0; i < 100; i++)
        {
            var key = ToBytes($"key_{i:D5}");

            var regularValue = regularStore.Get(key);
            var parallelValue = parallelStore.Get(key);

            Assert.That(parallelValue, Is.EqualTo(regularValue), $"Mismatch at key_{i:D5}");
        }
    }

    [Test]
    public void ParallelStoreHandlesDeleteCorrectlyTest()
    {
        var dir = Path.Combine(m_testDir, "delete_test");
        var options = new ParallelModeOptions { Mode = ParallelMode.Buffered };

        using var store = KeyValueStoreFactory.CreateLsmStore(dir, options);

        // Insert
        store.Put(ToBytes("key1"), ToBytes("value1"));
        store.Put(ToBytes("key2"), ToBytes("value2"));
        store.Checkpoint();

        // Verify
        Assert.That(store.Get(ToBytes("key1")), Is.Not.Null);
        Assert.That(store.Get(ToBytes("key2")), Is.Not.Null);

        // Delete
        store.Delete(ToBytes("key1"));
        store.Checkpoint();

        // Verify delete - LSM uses tombstones, so Get should return null
        Assert.That(store.Get(ToBytes("key1")), Is.Null);
        Assert.That(store.Get(ToBytes("key2")), Is.Not.Null);
    }

    [Test]
    public void ParallelStoreScanWorksCorrectlyTest()
    {
        var dir = Path.Combine(m_testDir, "scan_test");
        var options = new ParallelModeOptions { Mode = ParallelMode.Buffered };

        using var store = KeyValueStoreFactory.CreateLsmStore(dir, options);

        // Insert ordered keys
        for (int i = 0; i < 50; i++)
        {
            store.Put(ToBytes($"key_{i:D3}"), ToBytes($"value_{i}"));
        }
        store.Checkpoint();

        // Scan range
        var results = store.Scan(ToBytes("key_010"), ToBytes("key_020")).ToList();

        // Should include key_010 to key_019 (exclusive end)
        Assert.That(results.Count, Is.GreaterThanOrEqualTo(10));
        Assert.That(FromBytes(results[0].Key), Is.EqualTo("key_010"));
    }

    #endregion

    #region Concurrent Access Tests

    [Test]
    public void ParallelStoreSupportsConcurrentWritesTest()
    {
        var dir = Path.Combine(m_testDir, "concurrent_write");
        var options = new ParallelModeOptions { Mode = ParallelMode.Buffered };

        using var store = KeyValueStoreFactory.CreateLsmStore(dir, options);

        const int threads = 4;
        const int entriesPerThread = 200;
        var errors = new List<Exception>();

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < entriesPerThread; i++)
                {
                    var key = ToBytes($"t{threadId}_key_{i:D5}");
                    var value = ToBytes($"value_{threadId}_{i}");
                    store.Put(key, value);
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        Task.WaitAll(tasks);
        store.Checkpoint();

        Assert.That(errors, Is.Empty, $"Errors: {string.Join(", ", errors.Select(e => e.Message))}");

        // Verify some entries exist
        Assert.That(store.Get(ToBytes("t0_key_00000")), Is.Not.Null);
        Assert.That(store.Get(ToBytes("t1_key_00000")), Is.Not.Null);
    }

    [Test]
    public void ParallelStoreSupportsConcurrentReadsTest()
    {
        var dir = Path.Combine(m_testDir, "concurrent_read");
        var options = new ParallelModeOptions { Mode = ParallelMode.Buffered };

        using var store = KeyValueStoreFactory.CreateLsmStore(dir, options);

        // Prepopulate
        const int totalEntries = 500;
        for (int i = 0; i < totalEntries; i++)
        {
            store.Put(ToBytes($"key_{i:D5}"), ToBytes($"value_{i}"));
        }
        store.Checkpoint();

        const int threads = 4;
        const int readsPerThread = 200;
        var errors = new List<Exception>();
        var successfulReads = 0;

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            try
            {
                var random = new Random(threadId);
                for (int i = 0; i < readsPerThread; i++)
                {
                    var keyIndex = random.Next(totalEntries);
                    var result = store.Get(ToBytes($"key_{keyIndex:D5}"));
                    if (result != null)
                    {
                        Interlocked.Increment(ref successfulReads);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.That(errors, Is.Empty);
        Assert.That(successfulReads, Is.EqualTo(threads * readsPerThread));
    }

    [Test]
    public void ParallelStoreSupportsMixedReadWriteTest()
    {
        var dir = Path.Combine(m_testDir, "mixed_rw");
        var options = new ParallelModeOptions { Mode = ParallelMode.Buffered };

        using var store = KeyValueStoreFactory.CreateLsmStore(dir, options);

        // Prepopulate
        for (int i = 0; i < 100; i++)
        {
            store.Put(ToBytes($"key_{i:D5}"), ToBytes($"value_{i}"));
        }
        store.Checkpoint();

        const int readers = 2;
        const int writers = 2;
        const int operations = 100;
        var errors = new List<Exception>();

        var readerTasks = Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
        {
            try
            {
                var random = new Random();
                for (int i = 0; i < operations; i++)
                {
                    var keyIndex = random.Next(100);
                    store.Get(ToBytes($"key_{keyIndex:D5}"));
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        var writerTasks = Enumerable.Range(0, writers).Select(writerId => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < operations; i++)
                {
                    var key = ToBytes($"new_w{writerId}_k{i:D5}");
                    store.Put(key, ToBytes($"v{i}"));
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        Task.WaitAll(readerTasks.Concat(writerTasks).ToArray());

        Assert.That(errors, Is.Empty);
    }

    #endregion

    #region Performance Comparison Tests

    [Test]
    [Category("Performance")]
    public void CompareRegularVsParallelSingleThreadedTest()
    {
        const int entries = 2000;

        // Regular store
        var regularDir = Path.Combine(m_testDir, "perf_regular");
        long regularMs;
        using (var store = KeyValueStoreFactory.CreateLsmStore(regularDir, 
            new ParallelModeOptions { Mode = ParallelMode.None }))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < entries; i++)
            {
                store.Put(ToBytes($"key_{i:D6}"), ToBytes($"value_{i}"));
            }
            store.Checkpoint();
            sw.Stop();
            regularMs = sw.ElapsedMilliseconds;
        }

        // Parallel store (single-threaded usage)
        var parallelDir = Path.Combine(m_testDir, "perf_parallel");
        long parallelMs;
        using (var store = KeyValueStoreFactory.CreateLsmStore(parallelDir, 
            new ParallelModeOptions { Mode = ParallelMode.Buffered }))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < entries; i++)
            {
                store.Put(ToBytes($"key_{i:D6}"), ToBytes($"value_{i}"));
            }
            store.Checkpoint();
            sw.Stop();
            parallelMs = sw.ElapsedMilliseconds;
        }

        TestContext.WriteLine("=== LSM Single-Threaded Performance ===");
        TestContext.WriteLine($"Entries: {entries}");
        TestContext.WriteLine($"Regular store: {regularMs}ms ({entries * 1000.0 / Math.Max(regularMs, 1):F0} ops/sec)");
        TestContext.WriteLine($"Parallel store: {parallelMs}ms ({entries * 1000.0 / Math.Max(parallelMs, 1):F0} ops/sec)");

        // Parallel store may have some overhead in single-threaded
        Assert.Pass($"Single-threaded comparison completed");
    }

    [Test]
    [Category("Performance")]
    public void CompareRegularVsParallelMultiThreadedTest()
    {
        const int threads = 4;
        const int entriesPerThread = 500;
        const int totalEntries = threads * entriesPerThread;

        // Regular store (with global lock contention)
        var regularDir = Path.Combine(m_testDir, "perf_mt_regular");
        long regularMs;
        using (var store = new StoreLsm(regularDir))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
            {
                for (int i = 0; i < entriesPerThread; i++)
                {
                    // StoreLsm is already thread-safe internally
                    store.Put(ToBytes($"t{threadId}_key_{i:D5}"), ToBytes($"v{i}"));
                }
            })).ToArray();
            Task.WaitAll(tasks);
            store.Checkpoint();
            sw.Stop();
            regularMs = sw.ElapsedMilliseconds;
        }

        // Parallel store (with thread-local buffers)
        var parallelDir = Path.Combine(m_testDir, "perf_mt_parallel");
        long parallelMs;
        long entriesMerged;
        using (var store = (LsmParallelStore)KeyValueStoreFactory.CreateLsmStore(parallelDir, 
            new ParallelModeOptions { Mode = ParallelMode.Buffered }))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
            {
                for (int i = 0; i < entriesPerThread; i++)
                {
                    store.Put(ToBytes($"t{threadId}_key_{i:D5}"), ToBytes($"v{i}"));
                }
            })).ToArray();
            Task.WaitAll(tasks);
            store.Checkpoint();
            sw.Stop();
            parallelMs = sw.ElapsedMilliseconds;
            entriesMerged = store.EntriesMerged;
        }

        TestContext.WriteLine("=== LSM Multi-Threaded Performance ===");
        TestContext.WriteLine($"Threads: {threads}, Entries/thread: {entriesPerThread}, Total: {totalEntries}");
        TestContext.WriteLine($"Regular store: {regularMs}ms ({totalEntries * 1000.0 / Math.Max(regularMs, 1):F0} ops/sec)");
        TestContext.WriteLine($"Parallel store: {parallelMs}ms ({totalEntries * 1000.0 / Math.Max(parallelMs, 1):F0} ops/sec)");
        TestContext.WriteLine($"Entries merged: {entriesMerged}");
        
        var speedup = (double)regularMs / Math.Max(parallelMs, 1);
        TestContext.WriteLine($"Speedup: {speedup:F2}x");

        Assert.Pass($"Multi-threaded comparison completed. Speedup: {speedup:F2}x");
    }

    #endregion

    #region Statistics Tests

    [Test]
    public void ParallelStoreTracksStatisticsTest()
    {
        var dir = Path.Combine(m_testDir, "stats");
        var options = new ParallelModeOptions 
        { 
            Mode = ParallelMode.Buffered,
            TrackStatistics = true 
        };

        using var store = (LsmParallelStore)KeyValueStoreFactory.CreateLsmStore(dir, options);

        Assert.That(store.BuffersSubmitted, Is.EqualTo(0));
        Assert.That(store.EntriesMerged, Is.EqualTo(0));

        // Write enough to trigger buffer flush
        for (int i = 0; i < 1000; i++)
        {
            store.Put(ToBytes($"key_{i:D5}"), ToBytes($"value_{i}_{new string('x', 100)}"));
        }
        store.Checkpoint();

        TestContext.WriteLine($"Buffers submitted: {store.BuffersSubmitted}");
        TestContext.WriteLine($"Entries merged: {store.EntriesMerged}");
        TestContext.WriteLine($"Merge operations: {store.MergeOperations}");
        TestContext.WriteLine($"Avg entries/merge: {store.AverageEntriesPerMerge:F1}");

        Assert.That(store.EntriesMerged, Is.EqualTo(1000));
    }

    #endregion

    #region Helpers

    private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);
    private static string FromBytes(byte[] bytes) => TextEncoding.UTF8.GetString(bytes);

    #endregion
}
