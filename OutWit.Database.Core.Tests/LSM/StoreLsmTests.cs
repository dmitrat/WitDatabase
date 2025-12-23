using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.LSM
{
    /// <summary>
    /// Integration tests for LsmTreeStore - the main LSM-Tree implementation.
    /// </summary>
    [TestFixture]
    public class StoreLsmTests : IDisposable
    {
        private string m_testDir = null!;

        [SetUp]
        public void SetUp()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), $"lsm_store_test_{Guid.NewGuid():N}");
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

        private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);

        #region Basic Operations

        [Test]
        public void LsmTreePutAndGetTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            tree.Put(ToBytes("key1"), ToBytes("value1"));
        
            var result = tree.Get(ToBytes("key1"));
            Assert.That(result, Is.EqualTo(ToBytes("value1")));
        }

        [Test]
        public void LsmTreeDeleteTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            tree.Put(ToBytes("key1"), ToBytes("value1"));
            tree.Delete(ToBytes("key1"));
        
            var result = tree.Get(ToBytes("key1"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void LsmTreeScanReturnsSortedResultsTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            tree.Put(ToBytes("c"), ToBytes("3"));
            tree.Put(ToBytes("a"), ToBytes("1"));
            tree.Put(ToBytes("b"), ToBytes("2"));

            var results = tree.Scan(null, null).ToList();
        
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].Key, Is.EqualTo(ToBytes("a")));
            Assert.That(results[1].Key, Is.EqualTo(ToBytes("b")));
            Assert.That(results[2].Key, Is.EqualTo(ToBytes("c")));
        }

        [Test]
        public void LsmTreeGetNonExistentKeyTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            var result = tree.Get(ToBytes("nonexistent"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void LsmTreeUpdateValueTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            tree.Put(ToBytes("key"), ToBytes("value1"));
            tree.Put(ToBytes("key"), ToBytes("value2"));
        
            var result = tree.Get(ToBytes("key"));
            Assert.That(result, Is.EqualTo(ToBytes("value2")));
        }

        #endregion

        #region Flush and SSTable

        [Test]
        public void LsmTreeFlushToSSTableTest()
        {
            var dir = Path.Combine(m_testDir, "flush");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 100
            };
        
            using (var tree = new StoreLsm(dir, options))
            {
                for (int i = 0; i < 50; i++)
                {
                    tree.Put(ToBytes($"key{i:D5}"), ToBytes($"value{i}"));
                }
                tree.Flush();
            
                Assert.That(tree.SSTableCount, Is.GreaterThan(0));
            }

            // Reopen and verify
            using var tree2 = new StoreLsm(dir, new LsmOptions { EnableWal = false });
            var result = tree2.Get(ToBytes("key00000"));
            Assert.That(result, Is.EqualTo(ToBytes("value0")));
        }

        [Test]
        public void LsmTreeMultipleSSTablesTest()
        {
            var dir = Path.Combine(m_testDir, "multi");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 50,
                Level0CompactionTrigger = 100 // Disable auto-compaction
            };
        
            using var tree = new StoreLsm(dir, options);
        
            for (int i = 0; i < 100; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();
        
            // Verify all keys are accessible
            for (int i = 0; i < 100; i++)
            {
                var result = tree.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null);
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i * 10));
            }
        }

        #endregion

        #region WAL Recovery

        [Test]
        public void LsmTreeWalRecoveryTest()
        {
            var dir = Path.Combine(m_testDir, "wal");
            var options = new LsmOptions { EnableWal = true, SyncWrites = true };
        
            using (var tree = new StoreLsm(dir, options))
            {
                tree.Put(ToBytes("key1"), ToBytes("value1"));
                tree.Put(ToBytes("key2"), ToBytes("value2"));
            }

            // Reopen - should recover from WAL
            using var tree2 = new StoreLsm(dir, options);
        
            Assert.That(tree2.Get(ToBytes("key1")), Is.EqualTo(ToBytes("value1")));
            Assert.That(tree2.Get(ToBytes("key2")), Is.EqualTo(ToBytes("value2")));
        }

        [Test]
        public void LsmTreeWalRecoveryWithDeletesTest()
        {
            var dir = Path.Combine(m_testDir, "wal_delete");
            var options = new LsmOptions { EnableWal = true, SyncWrites = true };
        
            using (var tree = new StoreLsm(dir, options))
            {
                tree.Put(ToBytes("key1"), ToBytes("value1"));
                tree.Put(ToBytes("key2"), ToBytes("value2"));
                tree.Delete(ToBytes("key1"));
            }

            using var tree2 = new StoreLsm(dir, options);
        
            Assert.That(tree2.Get(ToBytes("key1")), Is.Null);
            Assert.That(tree2.Get(ToBytes("key2")), Is.EqualTo(ToBytes("value2")));
        }

        #endregion

        #region Compaction

        [Test]
        public void LsmTreeCompactionTest()
        {
            var dir = Path.Combine(m_testDir, "compaction");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 3,
                BackgroundCompaction = false
            };
        
            using var tree = new StoreLsm(dir, options);
        
            for (int i = 0; i < 200; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();

            Assert.That(tree.SSTableCount, Is.LessThanOrEqualTo(3));

            for (int i = 0; i < 200; i++)
            {
                var result = tree.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null, $"Key {i} not found");
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i * 10));
            }
        }

        [Test]
        public void LsmTreeCompactionRemovesTombstonesTest()
        {
            var dir = Path.Combine(m_testDir, "compaction_tombstone");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 3,
                BackgroundCompaction = false
            };
        
            using var tree = new StoreLsm(dir, options);
        
            for (int i = 0; i < 100; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            tree.Flush();

            for (int i = 0; i < 50; i++)
            {
                tree.Delete(BitConverter.GetBytes(i));
            }
            tree.Flush();

            tree.Compact();

            for (int i = 0; i < 50; i++)
            {
                Assert.That(tree.Get(BitConverter.GetBytes(i)), Is.Null);
            }

            for (int i = 50; i < 100; i++)
            {
                Assert.That(tree.Get(BitConverter.GetBytes(i)), Is.Not.Null);
            }
        }

        [Test]
        public void LsmTreeBackgroundCompactionTest()
        {
            var dir = Path.Combine(m_testDir, "bg_compaction");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = false,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 3,
                BackgroundCompaction = true
            };
        
            using var tree = new StoreLsm(dir, options);
        
            for (int i = 0; i < 200; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();

            tree.WaitForCompaction();
            Assert.That(tree.IsCompacting, Is.False);

            for (int i = 0; i < 200; i++)
            {
                var result = tree.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null, $"Key {i} not found");
            }
        }

        #endregion

        #region Concurrent Access

        [Test]
        public void LsmTreeConcurrentReadsTest()
        {
            var dir = Path.Combine(m_testDir, "concurrent_read");
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(dir, options);

            for (int i = 0; i < 1000; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();

            const int threadCount = 8;
            const int readsPerThread = 1000;
            var errors = 0;

            var tasks = Enumerable.Range(0, threadCount)
                .Select(t => Task.Run(() =>
                {
                    var random = new Random();
                    for (int i = 0; i < readsPerThread; i++)
                    {
                        int key = random.Next(1000);
                        var result = tree.Get(BitConverter.GetBytes(key));
                        if (result == null || BitConverter.ToInt32(result) != key * 10)
                        {
                            Interlocked.Increment(ref errors);
                        }
                    }
                }))
                .ToArray();

            Task.WaitAll(tasks);
            Assert.That(errors, Is.EqualTo(0));
        }

        [Test]
        public void LsmTreeConcurrentReadWriteTest()
        {
            var dir = Path.Combine(m_testDir, "concurrent_rw");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 1024 * 10,
                Level0CompactionTrigger = 100
            };
            using var tree = new StoreLsm(dir, options);

            for (int i = 0; i < 100; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var writeCount = 0;
            var readCount = 0;
            var errors = new List<Exception>();

            var writerTask = Task.Run(() =>
            {
                try
                {
                    int i = 100;
                    while (!cts.Token.IsCancellationRequested)
                    {
                        tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
                        Interlocked.Increment(ref writeCount);
                        i++;
                    }
                }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { lock (errors) errors.Add(ex); }
            });

            var readerTasks = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() =>
                {
                    try
                    {
                        var random = new Random();
                        while (!cts.Token.IsCancellationRequested)
                        {
                            int key = random.Next(100);
                            tree.Get(BitConverter.GetBytes(key));
                            Interlocked.Increment(ref readCount);
                        }
                    }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex) { lock (errors) errors.Add(ex); }
                }))
                .ToArray();

            try { Task.WaitAll([writerTask, .. readerTasks]); }
            catch (AggregateException) { }

            Assert.That(errors, Is.Empty);
            Assert.That(writeCount, Is.GreaterThan(0));
            Assert.That(readCount, Is.GreaterThan(0));
        }

        #endregion

        #region Block Cache Integration

        [Test]
        public void LsmTreeBlockCacheIntegrationTest()
        {
            var dir = Path.Combine(m_testDir, "cache_integration");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 1024 * 1024,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 100
            };
        
            using var tree = new StoreLsm(dir, options);
            
            for (int i = 0; i < 50; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();
            
            var initialMisses = tree.BlockCache?.Misses ?? 0;
            for (int i = 0; i < 50; i++)
            {
                tree.Get(BitConverter.GetBytes(i));
            }
            
            Assert.That(tree.BlockCache, Is.Not.Null);
            Assert.That(tree.BlockCache!.Misses, Is.GreaterThan(initialMisses));
            
            var hitsBeforeSecondRead = tree.BlockCache.Hits;
            for (int i = 0; i < 50; i++)
            {
                tree.Get(BitConverter.GetBytes(i));
            }
            
            Assert.That(tree.BlockCache.Hits, Is.GreaterThan(hitsBeforeSecondRead));
        }

        [Test]
        public void LsmTreeBlockCacheDisabledTest()
        {
            var dir = Path.Combine(m_testDir, "no_cache");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = false
            };
        
            using var tree = new StoreLsm(dir, options);
            
            Assert.That(tree.BlockCache, Is.Null);
            
            tree.Put(ToBytes("key"), ToBytes("value"));
            Assert.That(tree.Get(ToBytes("key")), Is.EqualTo(ToBytes("value")));
        }

        #endregion

        #region Statistics

        [Test]
        public void LsmTreeStatisticsTest()
        {
            var dir = Path.Combine(m_testDir, "stats");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 100
            };
        
            using var tree = new StoreLsm(dir, options);
            var stats = tree.Statistics;
            
            Assert.That(stats.Gets, Is.EqualTo(0));
            Assert.That(stats.Puts, Is.EqualTo(0));
            
            for (int i = 0; i < 10; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            Assert.That(stats.Puts, Is.EqualTo(10));
            Assert.That(stats.BytesWritten, Is.GreaterThan(0));
            
            for (int i = 0; i < 5; i++)
            {
                tree.Get(BitConverter.GetBytes(i));
            }
            Assert.That(stats.Gets, Is.EqualTo(5));
            
            tree.Delete(BitConverter.GetBytes(0));
            Assert.That(stats.Deletes, Is.EqualTo(1));
            
            tree.Scan(null, null).ToList();
            Assert.That(stats.Scans, Is.EqualTo(1));
            
            var snapshot = stats.GetSnapshot();
            Assert.That(snapshot.Gets, Is.EqualTo(5));
            
            stats.Reset();
            Assert.That(stats.Gets, Is.EqualTo(0));
        }

        #endregion

        #region IKeyValueStoreStatistics Tests

        [Test]
        public void LsmTreeImplementsIKeyValueStoreStatisticsTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            // StoreLsm should implement IKeyValueStoreStatistics
            Assert.That(tree, Is.InstanceOf<IKeyValueStoreStatistics>());
        }

        [Test]
        public void LsmTreeCountReturnsCorrectValueTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            var stats = (IKeyValueStoreStatistics)tree;
            
            Assert.That(stats.Count(), Is.EqualTo(0));
            
            for (int i = 0; i < 50; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            
            Assert.That(stats.Count(), Is.EqualTo(50));
        }

        [Test]
        public async Task LsmTreeCountAsyncWorksTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            var stats = (IKeyValueStoreStatistics)tree;
            
            for (int i = 0; i < 25; i++)
            {
                await tree.PutAsync(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            
            var count = await stats.CountAsync();
            Assert.That(count, Is.EqualTo(25));
        }

        [Test]
        public void LsmTreeApproximateSizeInBytesTest()
        {
            var dir = Path.Combine(m_testDir, "size_test");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 1000
            };
            using var tree = new StoreLsm(dir, options);

            var stats = (IKeyValueStoreStatistics)tree;
            var initialSize = stats.ApproximateSizeInBytes;
            
            // Add data
            for (int i = 0; i < 100; i++)
            {
                tree.Put(BitConverter.GetBytes(i), new byte[100]);
            }
            
            var afterPutSize = stats.ApproximateSizeInBytes;
            Assert.That(afterPutSize, Is.GreaterThan(initialSize));
            
            // Flush to SSTable
            tree.Flush();
            
            var afterFlushSize = stats.ApproximateSizeInBytes;
            Assert.That(afterFlushSize, Is.GreaterThan(0));
        }

        [Test]
        public void LsmTreeAreStatisticsExactIsFalseTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            var stats = (IKeyValueStoreStatistics)tree;
            
            // LSM statistics are approximate (may include tombstones, duplicates)
            Assert.That(stats.AreStatisticsExact, Is.False);
        }

        [Test]
        public void LsmTreeEstimatedKeyCountEqualsCountTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            var stats = (IKeyValueStoreStatistics)tree;
            
            for (int i = 0; i < 30; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            
            Assert.That(stats.EstimatedKeyCount, Is.EqualTo(stats.Count()));
        }

        [Test]
        public void LsmTreeCountIncludesMemTableAndSSTablesTest()
        {
            var dir = Path.Combine(m_testDir, "count_all");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 500,
                Level0CompactionTrigger = 100
            };
            using var tree = new StoreLsm(dir, options);

            var stats = (IKeyValueStoreStatistics)tree;
            
            // Add some data (stays in MemTable)
            for (int i = 0; i < 10; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            
            // Flush to SSTable
            tree.Flush();
            
            // Add more data to MemTable
            for (int i = 10; i < 20; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            
            // Count should include both
            Assert.That(stats.Count(), Is.EqualTo(20));
        }

        [Test]
        public void LsmTreeExtensionMethodsUseNativeStatsTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            for (int i = 0; i < 10; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            
            // Extension methods should use native stats
            var wrapper = tree.GetStatistics();
            Assert.That(wrapper.HasNativeStatistics, Is.True);
            Assert.That(wrapper.AreStatisticsExact, Is.False); // LSM is approximate
            Assert.That(wrapper.Count(), Is.EqualTo(10));
        }

        #endregion

        #region Async Operations

        [Test]
        public async Task LsmTreeAsyncOperationsTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            await tree.PutAsync(ToBytes("key"), ToBytes("value"));
            
            var result = await tree.GetAsync(ToBytes("key"));
            Assert.That(result, Is.EqualTo(ToBytes("value")));
            
            var deleted = await tree.DeleteAsync(ToBytes("key"));
            Assert.That(deleted, Is.True);
            
            result = await tree.GetAsync(ToBytes("key"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task LsmTreeAsyncScanTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new StoreLsm(m_testDir, options);

            for (int i = 0; i < 10; i++)
            {
                await tree.PutAsync(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }

            var results = new List<int>();
            await foreach (var (key, _) in tree.ScanAsync(null, null))
            {
                results.Add(BitConverter.ToInt32(key));
            }

            Assert.That(results.Count, Is.EqualTo(10));
        }

        #endregion
    }
}
