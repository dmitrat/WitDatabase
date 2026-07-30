using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Tree;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Tree;

/// <summary>
/// Tests for BTreeConcurrentStore - thread-safe BTree wrapper.
/// </summary>
[TestFixture]
public class BTreeConcurrentStoreTests : IDisposable
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"btree_concurrent_test_{Guid.NewGuid():N}");
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

    #region Basic Operations Tests

    [Test]
    public void PutAndGetWorksTest()
    {
        var filePath = Path.Combine(m_testDir, "basic.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        store.Put(ToBytes("key1"), ToBytes("value1"));
        var result = store.Get(ToBytes("key1"));

        Assert.That(result, Is.Not.Null);
        Assert.That(FromBytes(result), Is.EqualTo("value1"));
    }

    [Test]
    public void DeleteWorksTest()
    {
        var filePath = Path.Combine(m_testDir, "delete.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        store.Put(ToBytes("key1"), ToBytes("value1"));
        var deleted = store.Delete(ToBytes("key1"));
        var result = store.Get(ToBytes("key1"));

        Assert.That(deleted, Is.True);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ScanWorksTest()
    {
        var filePath = Path.Combine(m_testDir, "scan.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        for (int i = 0; i < 10; i++)
        {
            store.Put(ToBytes($"key_{i:D3}"), ToBytes($"value_{i}"));
        }

        var results = store.Scan(ToBytes("key_002"), ToBytes("key_007")).ToList();

        Assert.That(results.Count, Is.EqualTo(5)); // 002, 003, 004, 005, 006
    }

    #endregion

    #region Scan Chunking Tests

    /// <summary>
    /// A scan is taken in chunks, so the re-seek between them must land exactly after the last key
    /// returned: one key too far loses an entry, one key short returns it twice. This crosses several
    /// chunk boundaries and compares against the entries the store was given.
    /// </summary>
    [Test]
    [TestCase(null, null)]
    [TestCase("key_00100", null)]
    [TestCase("key_00100", "key_01700")]
    public void ScanCrossesChunkBoundariesExactlyTest(string? start, string? end)
    {
        const int ENTRIES = 2000;   // several times the 512-entry chunk

        var filePath = Path.Combine(m_testDir, "chunked.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        var written = new List<string>();
        for (int i = 0; i < ENTRIES; i++)
        {
            var key = $"key_{i:D5}";
            store.Put(ToBytes(key), ToBytes($"value_{i}"));
            written.Add(key);
        }

        var expected = written
            .Where(key => (start == null || string.CompareOrdinal(key, start) >= 0)
                       && (end == null || string.CompareOrdinal(key, end) < 0))
            .ToList();

        var scanned = store.Scan(start == null ? null : ToBytes(start), end == null ? null : ToBytes(end))
            .Select(entry => FromBytes(entry.Key))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(scanned, Is.EqualTo(expected), "the chunked scan is not the range it was asked for");
            Assert.That(scanned.Distinct().Count(), Is.EqualTo(scanned.Count), "a key was returned twice");
        });
    }

    /// <summary>
    /// The same for the async scan, which re-seeks the same way.
    /// </summary>
    [Test]
    public async Task ScanAsyncCrossesChunkBoundariesExactlyTest()
    {
        const int ENTRIES = 2000;

        var filePath = Path.Combine(m_testDir, "chunked_async.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        for (int i = 0; i < ENTRIES; i++)
            store.Put(ToBytes($"key_{i:D5}"), ToBytes($"value_{i}"));

        var scanned = new List<string>();
        await foreach (var entry in store.ScanAsync(null, null))
            scanned.Add(FromBytes(entry.Key));

        Assert.Multiple(() =>
        {
            Assert.That(scanned.Count, Is.EqualTo(ENTRIES));
            Assert.That(scanned, Is.Ordered.Using<string>(StringComparer.Ordinal));
            Assert.That(scanned.Distinct().Count(), Is.EqualTo(scanned.Count), "a key was returned twice");
        });
    }

    /// <summary>
    /// The property the chunking exists for: no lock is held while the consumer has the chunk, so a
    /// consumer may write to the store from inside its own scan. The engine does exactly this -
    /// deleting rows found through an index range scan - and holding a read lock across the
    /// enumeration would throw on the read-to-write upgrade instead.
    /// </summary>
    [Test]
    public void ScanAllowsTheConsumerToWriteWhileEnumeratingTest()
    {
        const int ENTRIES = 1500;

        var filePath = Path.Combine(m_testDir, "scan_then_write.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        for (int i = 0; i < ENTRIES; i++)
            store.Put(ToBytes($"key_{i:D5}"), ToBytes($"value_{i}"));

        var seen = 0;

        Assert.DoesNotThrow(() =>
        {
            foreach (var entry in store.Scan(null, null))
            {
                seen++;

                // Deleting the entry the scan just handed over, on the scan's own thread.
                store.Delete(entry.Key);
            }
        });

        Assert.That(seen, Is.EqualTo(ENTRIES), "the scan did not survive being written to from inside");
        Assert.That(store.Count(), Is.EqualTo(0));
    }

    /// <summary>
    /// The cost the chunking exists to avoid: an open-ended range whose consumer takes a handful of
    /// entries must not pull the whole tail into memory first. Materialising the range under one lock
    /// - which is what this store used to do - allocated 25.5 MB and took 108 ms on a 200,000-entry
    /// index where the unwrapped store took 3.1 ms and allocated nothing.
    /// </summary>
    [Test]
    public void ScanDoesNotMaterialiseTheWholeRangeTest()
    {
        const int ENTRIES = 50_000;

        var filePath = Path.Combine(m_testDir, "scan_streaming.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        for (int i = 0; i < ENTRIES; i++)
            store.Put(ToBytes($"key_{i:D6}"), ToBytes($"value_{i}"));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var taken = store.Scan(ToBytes("key_000010"), null).Take(5).Count();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        TestContext.Out.WriteLine($"took {taken} of {ENTRIES}, allocated {allocated / 1024.0:F0} KB");

        Assert.That(taken, Is.EqualTo(5));

        // One chunk is 512 entries or 1 MB, whichever comes first; the whole range would be about
        // 6 MB here. The budget is generous in both directions so that this is not a machine
        // measurement - it fails only if the scan goes back to materialising the range.
        Assert.That(allocated, Is.LessThan(2 * 1024 * 1024),
            "the scan materialised far more than one chunk - is it pulling the whole range again?");
    }

    #endregion

    #region Concurrent Access Tests

    [Test]
    public void ConcurrentWritesAreThreadSafeTest()
    {
        var filePath = Path.Combine(m_testDir, "concurrent.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        const int threads = 4;
        const int entriesPerThread = 100;

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < entriesPerThread; i++)
            {
                store.Put(ToBytes($"t{threadId}_key_{i:D5}"), ToBytes($"value_{i}"));
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.That(store.Count(), Is.EqualTo(threads * entriesPerThread));
    }

    [Test]
    public void ConcurrentReadsAndWritesTest()
    {
        var filePath = Path.Combine(m_testDir, "mixed.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        // Prepopulate
        for (int i = 0; i < 100; i++)
        {
            store.Put(ToBytes($"key_{i:D5}"), ToBytes($"value_{i}"));
        }

        const int readers = 4;
        const int writers = 2;
        const int operations = 50;

        var readCount = 0;
        var writeCount = 0;

        var readerTasks = Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < operations; i++)
            {
                var key = $"key_{i % 100:D5}";
                var result = store.Get(ToBytes(key));
                if (result != null)
                {
                    Interlocked.Increment(ref readCount);
                }
            }
        })).ToArray();

        var writerTasks = Enumerable.Range(0, writers).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < operations; i++)
            {
                store.Put(ToBytes($"new_t{threadId}_key_{i:D5}"), ToBytes($"new_value_{i}"));
                Interlocked.Increment(ref writeCount);
            }
        })).ToArray();

        Task.WaitAll(readerTasks.Concat(writerTasks).ToArray());

        Assert.That(readCount, Is.EqualTo(readers * operations));
        Assert.That(writeCount, Is.EqualTo(writers * operations));
    }

    [Test]
    public void HighContentionNoExceptionsTest()
    {
        var filePath = Path.Combine(m_testDir, "contention.witdb");
        var options = BTreeConcurrencyOptions.Debug; // Track statistics
        using var store = new BTreeConcurrentStore(filePath, options);

        const int threads = 8;
        const int operations = 100;
        var errors = 0;

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < operations; i++)
                {
                    // Mix of operations on same keys
                    var key = $"shared_key_{i % 10:D3}";
                    
                    if (i % 3 == 0)
                    {
                        store.Put(ToBytes(key), ToBytes($"value_{threadId}_{i}"));
                    }
                    else
                    {
                        store.Get(ToBytes(key));
                    }
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref errors);
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.That(errors, Is.EqualTo(0), "No exceptions should occur under contention");
        
        TestContext.WriteLine($"Read count: {store.ReadCount}");
        TestContext.WriteLine($"Write count: {store.WriteCount}");
    }

    #endregion

    #region Statistics Tests

    [Test]
    public void StatisticsTrackCorrectlyTest()
    {
        var filePath = Path.Combine(m_testDir, "stats.witdb");
        // Use Debug mode which has TrackStatistics = true
        var options = BTreeConcurrencyOptions.Debug;
        using var store = new BTreeConcurrentStore(filePath, options);

        Assert.That(store.ReadCount, Is.EqualTo(0));
        Assert.That(store.WriteCount, Is.EqualTo(0));

        store.Put(ToBytes("key1"), ToBytes("value1"));
        Assert.That(store.WriteCount, Is.EqualTo(1));

        store.Get(ToBytes("key1"));
        Assert.That(store.ReadCount, Is.EqualTo(1));

        store.Delete(ToBytes("key1"));
        Assert.That(store.WriteCount, Is.EqualTo(2));
    }

    [Test]
    public void StatisticsDisabledByDefaultTest()
    {
        var filePath = Path.Combine(m_testDir, "no_stats.witdb");
        using var store = new BTreeConcurrentStore(filePath);

        store.Put(ToBytes("key1"), ToBytes("value1"));
        store.Get(ToBytes("key1"));

        // With default options (TrackStatistics = false), counts stay 0
        Assert.That(store.ReadCount, Is.EqualTo(0));
        Assert.That(store.WriteCount, Is.EqualTo(0));
    }

    #endregion

    #region Async Entry Point Tests

    /// <summary>
    /// No method on this store may hold its <see cref="ReaderWriterLockSlim"/> across an await: the
    /// lock records the THREAD that took it, so a continuation resuming on another one throws out of
    /// the release and leaves the lock held for ever.
    /// </summary>
    /// <remarks>
    /// The interleaving is not left to chance. The storage underneath completes its asynchronous
    /// flush from a thread of its own with continuations forced asynchronous, and the caller is a
    /// dedicated thread rather than a pool thread - so whatever resumes the continuation, it cannot
    /// be the thread that entered the lock.
    ///
    /// Found in the wild on 2026-07-30, when secondary indexes started using this store:
    /// <c>WitDatabase.FlushAsync</c> -> <c>IndexManager.FlushAsync</c> threw
    /// <c>SynchronizationLockException: The write lock is being released without being held</c>.
    /// </remarks>
    [Test]
    public void AsyncFlushDoesNotStrandTheWriteLockTest()
    {
        var storage = new ThreadHoppingStorage(new StorageMemory(4096));
        using var store = new BTreeConcurrentStore(
            new StoreBTree(storage, cacheSize: 100, ownsStorage: true), options: null, ownsStore: true);

        for (int i = 0; i < 200; i++)
            store.Put(ToBytes($"key_{i:D4}"), ToBytes($"value_{i}"));

        Exception? flushError = null;

        // A dedicated thread, so that a pool-scheduled continuation is a different thread by
        // construction rather than by luck.
        var caller = new Thread(() =>
        {
            try
            {
                store.FlushAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                flushError = e;
            }
        })
        {
            IsBackground = true
        };

        caller.Start();
        Assert.That(caller.Join(TimeSpan.FromSeconds(30)), Is.True, "the flush never returned");
        Assert.That(flushError, Is.Null, $"the asynchronous flush threw: {flushError}");

        // And the lock has to be free afterwards. If the release was skipped, this write waits for a
        // lock held by a thread that has already gone.
        var writer = Task.Run(() => store.Put(ToBytes("after_flush"), ToBytes("value")));

        Assert.That(writer.Wait(TimeSpan.FromSeconds(10)), Is.True,
            "the write lock was never released - the store is deadlocked");
        Assert.That(FromBytes(store.Get(ToBytes("after_flush"))!), Is.EqualTo("value"));
    }

    #endregion

    #region Helpers

    private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);
    private static string FromBytes(byte[] bytes) => TextEncoding.UTF8.GetString(bytes);

    /// <summary>
    /// Storage whose asynchronous calls complete from a thread of their own, so a caller that awaits
    /// them resumes somewhere else.
    /// </summary>
    private sealed class ThreadHoppingStorage : IStorage
    {
        private readonly IStorage m_inner;

        public ThreadHoppingStorage(IStorage inner) => m_inner = inner;

        private static Task HopAsync(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            })
            {
                IsBackground = true
            };

            thread.Start();
            return completion.Task;
        }

        public void ReadPage(long pageNumber, Span<byte> buffer) => m_inner.ReadPage(pageNumber, buffer);

        public async ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var target = buffer;
            await HopAsync(() => m_inner.ReadPage(pageNumber, target.Span));
        }

        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer) => m_inner.WritePage(pageNumber, buffer);

        public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var source = buffer;
            await HopAsync(() => m_inner.WritePage(pageNumber, source.Span));
        }

        public void Flush() => m_inner.Flush();

        public async ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            await HopAsync(() => m_inner.Flush());

        public void SetSize(long pageCount) => m_inner.SetSize(pageCount);

        public int PageSize => m_inner.PageSize;

        public long PageCount => m_inner.PageCount;

        public bool IsReadOnly => m_inner.IsReadOnly;

        public string ProviderKey => m_inner.ProviderKey;

        public void Dispose() => m_inner.Dispose();
    }

    #endregion
}
