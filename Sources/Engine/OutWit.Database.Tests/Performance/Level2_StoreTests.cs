using NUnit.Framework;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using System.Diagnostics;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Tests for Level 2: Store Engine (B-Tree, LSM-Tree) performance.
/// These tests bypass the SQL layer to measure raw key-value performance.
/// </summary>
[TestFixture]
public class Level2_StoreTests
{
    #region Constants

    private const int SMALL_COUNT = 1000;
    private const int MEDIUM_COUNT = 10000;

    #endregion

    #region B-Tree Tests

    [Test]
    public void BTreeSequentialInsertTest()
    {
        var counts = new[] { 1000, 5000, 10000 };
        var times = new List<(int Count, double Ms)>();

        foreach (var count in counts)
        {
            using var storage = new StorageMemory();
            using var store = new StoreBTree(storage, cacheSize: 1000, ownsStorage: false);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                var key = BitConverter.GetBytes(i);
                var value = new byte[100]; // 100 bytes payload
                store.Put(key, value);
            }
            sw.Stop();

            times.Add((count, sw.Elapsed.TotalMilliseconds));
        }

        TestContext.Out.WriteLine("=== B-Tree Sequential Insert ===");
        foreach (var (count, ms) in times)
        {
            TestContext.Out.WriteLine($"  {count,6} keys: {ms,8:F2} ms ({ms / count:F4} ms/key)");
        }

        var ratio = times[2].Ms / times[0].Ms;
        TestContext.Out.WriteLine($"  Scaling ratio (10K/1K): {ratio:F2}x (expected ~10x for O(n log n))");
    }

    [Test]
    public void BTreeRandomInsertTest()
    {
        using var storage = new StorageMemory();
        using var store = new StoreBTree(storage, cacheSize: 1000, ownsStorage: false);

        var rnd = new Random(42);
        var keys = Enumerable.Range(0, MEDIUM_COUNT).OrderBy(_ => rnd.Next()).ToArray();

        var sw = Stopwatch.StartNew();
        foreach (var key in keys)
        {
            var keyBytes = BitConverter.GetBytes(key);
            var value = new byte[100];
            store.Put(keyBytes, value);
        }
        sw.Stop();

        TestContext.Out.WriteLine($"=== B-Tree Random Insert ({MEDIUM_COUNT} keys) ===");
        TestContext.Out.WriteLine($"  Time: {sw.Elapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"  Per key: {sw.Elapsed.TotalMilliseconds / MEDIUM_COUNT:F4} ms");
    }

    [Test]
    public void BTreePointLookupTest()
    {
        using var storage = new StorageMemory();
        using var store = new StoreBTree(storage, cacheSize: 1000, ownsStorage: false);

        // Populate
        for (int i = 0; i < MEDIUM_COUNT; i++)
        {
            var key = BitConverter.GetBytes(i);
            var value = new byte[100];
            store.Put(key, value);
        }

        // Random lookups
        var rnd = new Random(42);
        var lookupKeys = Enumerable.Range(0, SMALL_COUNT).Select(_ => rnd.Next(MEDIUM_COUNT)).ToArray();

        var sw = Stopwatch.StartNew();
        int found = 0;
        foreach (var keyInt in lookupKeys)
        {
            var key = BitConverter.GetBytes(keyInt);
            var value = store.Get(key);
            if (value != null) found++;
        }
        sw.Stop();

        TestContext.Out.WriteLine($"=== B-Tree Point Lookup ({SMALL_COUNT} lookups in {MEDIUM_COUNT} keys) ===");
        TestContext.Out.WriteLine($"  Time: {sw.Elapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"  Per lookup: {sw.Elapsed.TotalMilliseconds / SMALL_COUNT:F4} ms");
        TestContext.Out.WriteLine($"  Found: {found}");
    }

    [Test]
    public void BTreeMemoryUsageTest()
    {
        using var storage = new StorageMemory();
        using var store = new StoreBTree(storage, cacheSize: 1000, ownsStorage: false);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);

        for (int i = 0; i < SMALL_COUNT; i++)
        {
            var key = BitConverter.GetBytes(i);
            var value = new byte[100];
            store.Put(key, value);
        }

        var after = GC.GetTotalAllocatedBytes(precise: true);
        var allocated = after - before;

        TestContext.Out.WriteLine($"=== B-Tree Memory Usage ({SMALL_COUNT} keys, 100 bytes each) ===");
        TestContext.Out.WriteLine($"  Allocated: {allocated / 1024.0:F2} KB");
        TestContext.Out.WriteLine($"  Per key: {allocated / SMALL_COUNT:F0} bytes");
        TestContext.Out.WriteLine($"  Overhead: {(allocated - SMALL_COUNT * 100) / 1024.0:F2} KB");
    }

    #endregion

    #region LSM-Tree Tests

    [Test]
    public void LsmSequentialInsertTest()
    {
        var counts = new[] { 1000, 5000, 10000 };
        var times = new List<(int Count, double Ms)>();

        foreach (var count in counts)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"lsm_test_{Guid.NewGuid():N}");
            try
            {
                using var store = new StoreLsm(dir, new LsmOptions { EnableWal = false, SyncWrites = false });

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                {
                    var key = BitConverter.GetBytes(i);
                    var value = new byte[100];
                    store.Put(key, value);
                }
                sw.Stop();

                times.Add((count, sw.Elapsed.TotalMilliseconds));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        TestContext.Out.WriteLine("=== LSM-Tree Sequential Insert ===");
        foreach (var (count, ms) in times)
        {
            TestContext.Out.WriteLine($"  {count,6} keys: {ms,8:F2} ms ({ms / count:F4} ms/key)");
        }

        var ratio = times[2].Ms / times[0].Ms;
        TestContext.Out.WriteLine($"  Scaling ratio (10K/1K): {ratio:F2}x");
    }

    [Test]
    public void LsmRandomInsertTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lsm_test_{Guid.NewGuid():N}");
        try
        {
            using var store = new StoreLsm(dir, new LsmOptions { EnableWal = false, SyncWrites = false });

            var rnd = new Random(42);
            var keys = Enumerable.Range(0, MEDIUM_COUNT).OrderBy(_ => rnd.Next()).ToArray();

            var sw = Stopwatch.StartNew();
            foreach (var key in keys)
            {
                var keyBytes = BitConverter.GetBytes(key);
                var value = new byte[100];
                store.Put(keyBytes, value);
            }
            sw.Stop();

            TestContext.Out.WriteLine($"=== LSM-Tree Random Insert ({MEDIUM_COUNT} keys) ===");
            TestContext.Out.WriteLine($"  Time: {sw.Elapsed.TotalMilliseconds:F2} ms");
            TestContext.Out.WriteLine($"  Per key: {sw.Elapsed.TotalMilliseconds / MEDIUM_COUNT:F4} ms");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    #endregion

    #region Comparison Tests

    [Test]
    [Category("Performance")]
    public void CompareBTreeVsLsmTest()
    {
        const int count = 5000;

        // B-Tree
        double btreeTime;
        {
            using var storage = new StorageMemory();
            using var store = new StoreBTree(storage, cacheSize: 1000, ownsStorage: false);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                var key = BitConverter.GetBytes(i);
                var value = new byte[100];
                store.Put(key, value);
            }
            sw.Stop();
            btreeTime = sw.Elapsed.TotalMilliseconds;
        }

        // LSM-Tree
        double lsmTime;
        var lsmDir = Path.Combine(Path.GetTempPath(), $"lsm_test_{Guid.NewGuid():N}");
        try
        {
            using var store = new StoreLsm(lsmDir, new LsmOptions { EnableWal = false, SyncWrites = false });

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                var key = BitConverter.GetBytes(i);
                var value = new byte[100];
                store.Put(key, value);
            }
            sw.Stop();
            lsmTime = sw.Elapsed.TotalMilliseconds;
        }
        finally
        {
            try { Directory.Delete(lsmDir, true); } catch { }
        }

        TestContext.Out.WriteLine($"=== B-Tree vs LSM-Tree ({count} keys) ===");
        TestContext.Out.WriteLine($"  B-Tree: {btreeTime:F2} ms ({btreeTime / count:F4} ms/key)");
        TestContext.Out.WriteLine($"  LSM-Tree: {lsmTime:F2} ms ({lsmTime / count:F4} ms/key)");
        TestContext.Out.WriteLine($"  LSM/B-Tree ratio: {lsmTime / btreeTime:F2}x");
    }

    #endregion

    #region Scaling Tests

    /// <summary>
    /// Test to verify B-Tree insert scales appropriately (not O(n²)).
    /// </summary>
    [Test]
    [Category("Performance")]
    public void BTreeInsertScalingTest()
    {
        var counts = new[] { 500, 1000, 2000, 4000 };
        var times = new List<(int Count, double Ms)>();

        foreach (var count in counts)
        {
            using var storage = new StorageMemory();
            using var store = new StoreBTree(storage, cacheSize: 1000, ownsStorage: false);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                var key = BitConverter.GetBytes(i);
                var value = new byte[100];
                store.Put(key, value);
            }
            sw.Stop();

            times.Add((count, sw.Elapsed.TotalMilliseconds));
        }

        TestContext.Out.WriteLine("=== B-Tree Insert Scaling ===");
        foreach (var (count, ms) in times)
        {
            TestContext.Out.WriteLine($"  {count,5} keys: {ms,8:F2} ms ({ms / count:F4} ms/key)");
        }

        // Check scaling: for O(n log n), 4000/500 = 8x data should take ~8-10x time
        // For O(n²), it would take ~64x time
        var ratio = times[3].Ms / times[0].Ms;
        TestContext.Out.WriteLine($"  Scaling ratio (4000/500): {ratio:F2}x");
        TestContext.Out.WriteLine($"    Expected for O(n log n): ~8-12x");
        TestContext.Out.WriteLine($"    Expected for O(n²): ~64x");

        Assert.That(ratio, Is.LessThan(30), "B-Tree insert should not show O(n²) scaling");
    }

    #endregion
}
