using NUnit.Framework;
using OutWit.Database.Core.IndexedDb.Indexes;
using OutWit.Database.Core.IndexedDb.Tests.Mocks;
using System.Diagnostics;

namespace OutWit.Database.Core.IndexedDb.Tests.Stress;

/// <summary>
/// Stress tests for IndexedDB storage and secondary indexes.
/// Tests large datasets and concurrent operations.
/// </summary>
[TestFixture]
[Category("Stress")]
public class StressTests
{
    #region Fields

    private MockJSRuntime m_jsRuntime = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_jsRuntime = new MockJSRuntime();
    }

    #endregion

    #region Large Dataset Tests

    [Test]
    public async Task StorageLargeDataset10KPagesTest()
    {
        // Arrange
        const int pageCount = 10_000;
        const int pageSize = 4096;
        await using var storage = new StorageIndexedDb("LargeDb", m_jsRuntime, pageSize);
        await storage.InitializeAsync();

        var sw = Stopwatch.StartNew();

        // Act - Write 10K pages
        for (int i = 0; i < pageCount; i++)
        {
            var data = CreateTestPage(pageSize, i);
            await storage.WritePageAsync(i, data);
        }

        var writeTime = sw.ElapsedMilliseconds;
        sw.Restart();

        // Verify - Read all pages back
        for (int i = 0; i < pageCount; i++)
        {
            var buffer = new byte[pageSize];
            await storage.ReadPageAsync(i, buffer);
            Assert.That(buffer[0], Is.EqualTo((byte)(i & 0xFF)));
        }

        var readTime = sw.ElapsedMilliseconds;

        // Assert
        Assert.That(storage.PageCount, Is.EqualTo(pageCount));
        
        // Log performance (not strictly required, informational)
        TestContext.WriteLine($"Write 10K pages: {writeTime}ms ({pageCount * 1000.0 / writeTime:F0} pages/sec)");
        TestContext.WriteLine($"Read 10K pages: {readTime}ms ({pageCount * 1000.0 / readTime:F0} pages/sec)");
    }

    [Test]
    public async Task IndexLargeDataset10KEntriesUniqueTest()
    {
        // Arrange
        const int entryCount = 10_000;
        var interop = new IndexedDbIndexInterop(m_jsRuntime, "IndexDb", "large_unique_idx");
        await interop.OpenAsync();

        using var index = new SecondaryIndexIndexedDb("large_unique_idx", interop, isUnique: true, ownsInterop: true);

        var sw = Stopwatch.StartNew();

        // Act - Add 10K entries
        for (int i = 0; i < entryCount; i++)
        {
            var indexKey = GetBytes($"key_{i:D5}");
            var primaryKey = GetBytes($"pk_{i:D5}");
            index.Add(indexKey, primaryKey);
        }

        var addTime = sw.ElapsedMilliseconds;
        sw.Restart();

        // Verify - Find all entries
        for (int i = 0; i < entryCount; i++)
        {
            var indexKey = GetBytes($"key_{i:D5}");
            var results = index.Find(indexKey).ToList();
            Assert.That(results, Has.Count.EqualTo(1));
        }

        var findTime = sw.ElapsedMilliseconds;

        // Assert
        Assert.That(index.Count, Is.EqualTo(entryCount));
        
        TestContext.WriteLine($"Add 10K unique entries: {addTime}ms ({entryCount * 1000.0 / addTime:F0} entries/sec)");
        TestContext.WriteLine($"Find 10K entries: {findTime}ms ({entryCount * 1000.0 / findTime:F0} lookups/sec)");
    }

    [Test]
    public async Task IndexLargeDataset10KEntriesNonUniqueTest()
    {
        // Arrange
        const int categoryCount = 100;
        const int itemsPerCategory = 100;
        var interop = new IndexedDbIndexInterop(m_jsRuntime, "IndexDb", "large_nonunique_idx");
        await interop.OpenAsync();

        using var index = new SecondaryIndexIndexedDb("large_nonunique_idx", interop, isUnique: false, ownsInterop: true);

        var sw = Stopwatch.StartNew();

        // Act - Add 10K entries (100 categories * 100 items)
        for (int cat = 0; cat < categoryCount; cat++)
        {
            var categoryKey = GetBytes($"category_{cat:D3}");
            for (int item = 0; item < itemsPerCategory; item++)
            {
                var primaryKey = GetBytes($"pk_{cat:D3}_{item:D3}");
                index.Add(categoryKey, primaryKey);
            }
        }

        var addTime = sw.ElapsedMilliseconds;
        sw.Restart();

        // Verify - Find all entries for each category
        for (int cat = 0; cat < categoryCount; cat++)
        {
            var categoryKey = GetBytes($"category_{cat:D3}");
            var results = index.Find(categoryKey).ToList();
            Assert.That(results, Has.Count.EqualTo(itemsPerCategory));
        }

        var findTime = sw.ElapsedMilliseconds;

        // Assert
        Assert.That(index.Count, Is.EqualTo(categoryCount * itemsPerCategory));
        
        TestContext.WriteLine($"Add 10K non-unique entries: {addTime}ms");
        TestContext.WriteLine($"Find 100 categories: {findTime}ms ({categoryCount * 1000.0 / findTime:F0} category lookups/sec)");
    }

    #endregion

    #region Concurrent Operations Tests

    [Test]
    public async Task StorageConcurrentReadsTest()
    {
        // Arrange
        const int pageCount = 100;
        const int pageSize = 4096;
        const int concurrentReads = 50;
        
        await using var storage = new StorageIndexedDb("ConcurrentDb", m_jsRuntime, pageSize);
        await storage.InitializeAsync();

        // Write test data
        for (int i = 0; i < pageCount; i++)
        {
            await storage.WritePageAsync(i, CreateTestPage(pageSize, i));
        }

        var sw = Stopwatch.StartNew();

        // Act - Concurrent reads
        var tasks = new List<Task>();
        for (int i = 0; i < concurrentReads; i++)
        {
            var pageNum = i % pageCount;
            tasks.Add(Task.Run(async () =>
            {
                var buffer = new byte[pageSize];
                await storage.ReadPageAsync(pageNum, buffer);
            }));
        }

        await Task.WhenAll(tasks);

        var elapsed = sw.ElapsedMilliseconds;

        // Assert
        TestContext.WriteLine($"Concurrent {concurrentReads} reads: {elapsed}ms");
    }

    [Test]
    public async Task StorageConcurrentWritesTest()
    {
        // Arrange
        const int pageSize = 4096;
        const int concurrentWrites = 50;
        
        await using var storage = new StorageIndexedDb("ConcurrentWriteDb", m_jsRuntime, pageSize);
        await storage.InitializeAsync();

        // Pre-allocate pages to avoid race condition on SetSize
        await storage.SetSizeAsync(concurrentWrites);

        var sw = Stopwatch.StartNew();

        // Act - Concurrent writes to different pre-allocated pages
        var tasks = new List<Task>();
        for (int i = 0; i < concurrentWrites; i++)
        {
            var pageNum = i;
            tasks.Add(Task.Run(async () =>
            {
                await storage.WritePageAsync(pageNum, CreateTestPage(pageSize, pageNum));
            }));
        }

        await Task.WhenAll(tasks);

        var elapsed = sw.ElapsedMilliseconds;

        // Assert - verify all pages were written
        Assert.That(storage.PageCount, Is.EqualTo(concurrentWrites));
        
        // Verify content
        for (int i = 0; i < concurrentWrites; i++)
        {
            var buffer = new byte[pageSize];
            await storage.ReadPageAsync(i, buffer);
            Assert.That(buffer[0], Is.EqualTo((byte)(i & 0xFF)));
        }
        
        TestContext.WriteLine($"Concurrent {concurrentWrites} writes: {elapsed}ms");
    }

    [Test]
    public async Task IndexConcurrentAddsTest()
    {
        // Arrange
        const int concurrentAdds = 100;
        var interop = new IndexedDbIndexInterop(m_jsRuntime, "ConcurrentIndexDb", "concurrent_idx");
        await interop.OpenAsync();

        using var index = new SecondaryIndexIndexedDb("concurrent_idx", interop, isUnique: true, ownsInterop: true);

        var sw = Stopwatch.StartNew();

        // Act - Concurrent adds
        var tasks = new List<Task>();
        for (int i = 0; i < concurrentAdds; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                var indexKey = GetBytes($"concurrent_key_{idx:D5}");
                var primaryKey = GetBytes($"pk_{idx:D5}");
                index.Add(indexKey, primaryKey);
            }));
        }

        await Task.WhenAll(tasks);

        var elapsed = sw.ElapsedMilliseconds;

        // Assert
        Assert.That(index.Count, Is.EqualTo(concurrentAdds));
        TestContext.WriteLine($"Concurrent {concurrentAdds} adds: {elapsed}ms");
    }

    #endregion

    #region Memory Pressure Tests

    [Test]
    public async Task StorageLargePageSizeTest()
    {
        // Arrange
        const int pageSize = 64 * 1024; // 64KB pages
        const int pageCount = 100;
        
        await using var storage = new StorageIndexedDb("LargePageDb", m_jsRuntime, pageSize);
        await storage.InitializeAsync();

        var sw = Stopwatch.StartNew();

        // Act - Write 100 large pages (6.4MB total)
        for (int i = 0; i < pageCount; i++)
        {
            var data = CreateTestPage(pageSize, i);
            await storage.WritePageAsync(i, data);
        }

        var writeTime = sw.ElapsedMilliseconds;
        sw.Restart();

        // Verify
        for (int i = 0; i < pageCount; i++)
        {
            var buffer = new byte[pageSize];
            await storage.ReadPageAsync(i, buffer);
            Assert.That(buffer[0], Is.EqualTo((byte)(i & 0xFF)));
        }

        var readTime = sw.ElapsedMilliseconds;

        // Assert
        var totalMB = (pageSize * pageCount) / (1024.0 * 1024.0);
        TestContext.WriteLine($"Write {totalMB:F1}MB ({pageCount} x {pageSize / 1024}KB pages): {writeTime}ms");
        TestContext.WriteLine($"Read {totalMB:F1}MB: {readTime}ms");
    }

    [Test]
    public async Task IndexLargeKeysTest()
    {
        // Arrange
        const int keySize = 1024; // 1KB keys
        const int entryCount = 100;
        
        var interop = new IndexedDbIndexInterop(m_jsRuntime, "LargeKeyDb", "large_key_idx");
        await interop.OpenAsync();

        using var index = new SecondaryIndexIndexedDb("large_key_idx", interop, isUnique: true, ownsInterop: true);

        // Act - Add entries with large keys
        for (int i = 0; i < entryCount; i++)
        {
            var indexKey = CreateLargeKey(keySize, i);
            var primaryKey = GetBytes($"pk_{i:D5}");
            index.Add(indexKey, primaryKey);
        }

        // Verify
        for (int i = 0; i < entryCount; i++)
        {
            var indexKey = CreateLargeKey(keySize, i);
            var results = index.Find(indexKey).ToList();
            Assert.That(results, Has.Count.EqualTo(1));
        }

        // Assert
        Assert.That(index.Count, Is.EqualTo(entryCount));
    }

    [Test]
    public async Task IndexRangeScanPerformanceTest()
    {
        // Arrange
        const int entryCount = 1000;
        var interop = new IndexedDbIndexInterop(m_jsRuntime, "RangeScanDb", "range_idx");
        await interop.OpenAsync();

        using var index = new SecondaryIndexIndexedDb("range_idx", interop, isUnique: true, ownsInterop: true);

        // Add sorted entries
        for (int i = 0; i < entryCount; i++)
        {
            var indexKey = GetBytes($"key_{i:D5}");
            var primaryKey = GetBytes($"pk_{i:D5}");
            index.Add(indexKey, primaryKey);
        }

        var sw = Stopwatch.StartNew();

        // Act - Range scan 10% of entries
        var startKey = GetBytes("key_00100");
        var endKey = GetBytes("key_00200");
        var results = index.FindRange(startKey, endKey).ToList();

        var elapsed = sw.ElapsedMilliseconds;

        // Assert
        Assert.That(results, Has.Count.EqualTo(100));
        TestContext.WriteLine($"Range scan 100 entries from 1000: {elapsed}ms");
    }

    #endregion

    #region Bulk Operations Tests

    [Test]
    public async Task StorageBulkWriteTest()
    {
        // Arrange
        const int batchSize = 100;
        const int pageSize = 4096;
        
        await using var storage = new StorageIndexedDb("BulkDb", m_jsRuntime, pageSize);
        await storage.InitializeAsync();

        var sw = Stopwatch.StartNew();

        // Act - Write batch
        for (int i = 0; i < batchSize; i++)
        {
            await storage.WritePageAsync(i, CreateTestPage(pageSize, i));
        }
        await storage.FlushAsync();

        var elapsed = sw.ElapsedMilliseconds;

        // Assert
        Assert.That(storage.PageCount, Is.EqualTo(batchSize));
        TestContext.WriteLine($"Bulk write {batchSize} pages with flush: {elapsed}ms");
    }

    [Test]
    public async Task IndexBulkRemoveTest()
    {
        // Arrange
        const int entryCount = 1000;
        const int removeCount = 500;
        
        var interop = new IndexedDbIndexInterop(m_jsRuntime, "BulkRemoveDb", "bulk_remove_idx");
        await interop.OpenAsync();

        using var index = new SecondaryIndexIndexedDb("bulk_remove_idx", interop, isUnique: true, ownsInterop: true);

        // Add entries
        for (int i = 0; i < entryCount; i++)
        {
            index.Add(GetBytes($"key_{i:D5}"), GetBytes($"pk_{i:D5}"));
        }

        var sw = Stopwatch.StartNew();

        // Act - Remove half
        for (int i = 0; i < removeCount; i++)
        {
            index.Remove(GetBytes($"key_{i:D5}"), GetBytes($"pk_{i:D5}"));
        }

        var elapsed = sw.ElapsedMilliseconds;

        // Assert
        Assert.That(index.Count, Is.EqualTo(entryCount - removeCount));
        TestContext.WriteLine($"Remove {removeCount} entries from {entryCount}: {elapsed}ms");
    }

    [Test]
    public void IndexClearLargeDatasetTest()
    {
        // Arrange
        const int entryCount = 5000;
        
        var interop = new IndexedDbIndexInterop(m_jsRuntime, "ClearDb", "clear_idx");
        interop.OpenAsync().AsTask().GetAwaiter().GetResult();

        using var index = new SecondaryIndexIndexedDb("clear_idx", interop, isUnique: true, ownsInterop: true);

        // Add entries
        for (int i = 0; i < entryCount; i++)
        {
            index.Add(GetBytes($"key_{i:D5}"), GetBytes($"pk_{i:D5}"));
        }

        Assert.That(index.Count, Is.EqualTo(entryCount));

        var sw = Stopwatch.StartNew();

        // Act
        index.Clear();

        var elapsed = sw.ElapsedMilliseconds;

        // Assert
        Assert.That(index.Count, Is.EqualTo(0));
        TestContext.WriteLine($"Clear {entryCount} entries: {elapsed}ms");
    }

    #endregion

    #region Helpers

    private static byte[] CreateTestPage(int size, int seed)
    {
        var data = new byte[size];
        data[0] = (byte)(seed & 0xFF);
        data[1] = (byte)((seed >> 8) & 0xFF);
        data[2] = (byte)((seed >> 16) & 0xFF);
        data[3] = (byte)((seed >> 24) & 0xFF);
        
        // Fill rest with pattern
        for (int i = 4; i < size; i++)
        {
            data[i] = (byte)((seed + i) & 0xFF);
        }
        
        return data;
    }

    private static byte[] CreateLargeKey(int size, int seed)
    {
        var key = new byte[size];
        var prefix = GetBytes($"largekey_{seed:D5}_");
        prefix.CopyTo(key, 0);
        
        // Fill rest with pattern
        for (int i = prefix.Length; i < size; i++)
        {
            key[i] = (byte)((seed + i) & 0xFF);
        }
        
        return key;
    }

    private static byte[] GetBytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    #endregion
}
