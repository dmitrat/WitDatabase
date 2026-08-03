using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.LSM
{
    /// <summary>
    /// Stress tests for LSM-Tree with various parameter combinations.
    /// Tests durability, concurrency, and performance under load.
    /// </summary>
    [TestFixture]
    [Category("Stress")]
    public class LsmTreeStressTests : IDisposable
    {
        private string m_testDir = null!;

        [SetUp]
        public void SetUp()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), $"lsm_stress_{Guid.NewGuid():N}");
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

        #region Test Case Sources

        /// <summary>
        /// Combinations of WAL, BlockCache, and Compaction settings.
        /// </summary>
        private static readonly object[] LsmOptionCombinations =
        [
            // WAL enabled, cache enabled, sync compaction
            new object[] { true, true, false, "WAL+Cache+SyncCompact" },
            // WAL enabled, cache enabled, background compaction
            new object[] { true, true, true, "WAL+Cache+BgCompact" },
            // WAL disabled, cache enabled
            new object[] { false, true, false, "NoWAL+Cache" },
            // WAL enabled, cache disabled
            new object[] { true, false, false, "WAL+NoCache" },
            // Minimal config
            new object[] { false, false, false, "Minimal" },
        ];

        /// <summary>
        /// MemTable size variations.
        /// </summary>
        private static readonly object[] MemTableSizes =
        [
            new object[] { 1024, "1KB" },          // Very small - frequent flushes
            new object[] { 64 * 1024, "64KB" },    // Small
            new object[] { 1024 * 1024, "1MB" },   // Medium
            new object[] { 4 * 1024 * 1024, "4MB" } // Large
        ];

        #endregion

        #region Basic Stress Tests with Parameter Combinations

        [Test]
        [TestCaseSource(nameof(LsmOptionCombinations))]
        public void SequentialWriteReadTest(bool enableWal, bool enableCache, bool bgCompaction, string configName)
        {
            const int count = 10_000;
            var dir = Path.Combine(m_testDir, configName);

            var options = new LsmOptions
            {
                EnableWal = enableWal,
                SyncWrites = false,
                EnableBlockCache = enableCache,
                BlockCacheSizeBytes = 10 * 1024 * 1024,
                MemTableSizeLimit = 256 * 1024,
                Level0CompactionTrigger = 4,
                BackgroundCompaction = bgCompaction
            };

            using var store = new StoreLsm(dir, options);

            // Write
            for (int i = 0; i < count; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            store.Checkpoint();
            store.WaitForCompaction();

            // Read and verify
            for (int i = 0; i < count; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null, $"[{configName}] Key {i} not found");
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i * 10), $"[{configName}] Value mismatch for key {i}");
            }

            TestContext.WriteLine($"[{configName}] Completed: {count} entries, {store.SSTableCount} SSTables");
        }

        [Test]
        [TestCaseSource(nameof(MemTableSizes))]
        public void MemTableSizeImpactTest(int memTableSize, string sizeName)
        {
            const int count = 5_000;
            var dir = Path.Combine(m_testDir, $"memtable_{sizeName}");

            var options = new LsmOptions
            {
                EnableWal = false,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 10 * 1024 * 1024,
                MemTableSizeLimit = memTableSize,
                Level0CompactionTrigger = 10,
                BackgroundCompaction = false
            };

            using var store = new StoreLsm(dir, options);
            var stats = store.Statistics;

            // Write
            for (int i = 0; i < count; i++)
            {
                store.Put(BitConverter.GetBytes(i), new byte[100]);
            }
            store.Checkpoint();

            // Verify
            for (int i = 0; i < count; i += 100) // Sample verification
            {
                Assert.That(store.Get(BitConverter.GetBytes(i)), Is.Not.Null);
            }

            TestContext.WriteLine($"[{sizeName}] Flushes: {stats.Flushes}, SSTables: {store.SSTableCount}");
        }

        #endregion

        #region Concurrent Access Tests

        [Test]
        [TestCaseSource(nameof(LsmOptionCombinations))]
        public void ConcurrentWritersTest(bool enableWal, bool enableCache, bool bgCompaction, string configName)
        {
            const int writerCount = 4;
            const int keysPerWriter = 2_500;
            var dir = Path.Combine(m_testDir, $"concurrent_{configName}");

            var options = new LsmOptions
            {
                EnableWal = enableWal,
                SyncWrites = false,
                EnableBlockCache = enableCache,
                BlockCacheSizeBytes = 10 * 1024 * 1024,
                MemTableSizeLimit = 512 * 1024,
                Level0CompactionTrigger = 8,
                BackgroundCompaction = bgCompaction
            };

            using var store = new StoreLsm(dir, options);
            var exceptions = new List<Exception>();

            var tasks = Enumerable.Range(0, writerCount)
                .Select(writerId => Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < keysPerWriter; i++)
                        {
                            int key = writerId * keysPerWriter + i;
                            store.Put(BitConverter.GetBytes(key), BitConverter.GetBytes(writerId));
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions) exceptions.Add(ex);
                    }
                }))
                .ToArray();

            Task.WaitAll(tasks);
            store.Checkpoint();
            store.WaitForCompaction();

            Assert.That(exceptions, Is.Empty, 
                $"[{configName}] Exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");

            // Verify all keys
            int totalKeys = writerCount * keysPerWriter;
            for (int i = 0; i < totalKeys; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null, $"[{configName}] Key {i} missing");
            }

            TestContext.WriteLine($"[{configName}] {totalKeys} keys written by {writerCount} threads");
        }

        [Test]
        [TestCaseSource(nameof(LsmOptionCombinations))]
        public void ConcurrentReadWriteTest(bool enableWal, bool enableCache, bool bgCompaction, string configName)
        {
            const int initialKeys = 1_000;
            const int writerOps = 2_000;
            const int readerOps = 5_000;
            var dir = Path.Combine(m_testDir, $"rw_{configName}");

            var options = new LsmOptions
            {
                EnableWal = enableWal,
                SyncWrites = false,
                EnableBlockCache = enableCache,
                BlockCacheSizeBytes = 10 * 1024 * 1024,
                MemTableSizeLimit = 256 * 1024,
                Level0CompactionTrigger = 6,
                BackgroundCompaction = bgCompaction
            };

            using var store = new StoreLsm(dir, options);
            var exceptions = new List<Exception>();

            // Pre-populate
            for (int i = 0; i < initialKeys; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Writers
            var writerTask = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < writerOps && !cts.Token.IsCancellationRequested; i++)
                    {
                        int key = initialKeys + i;
                        store.Put(BitConverter.GetBytes(key), BitConverter.GetBytes(key));
                    }
                }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            });

            // Readers
            var readerTasks = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() =>
                {
                    try
                    {
                        var random = new Random();
                        for (int i = 0; i < readerOps && !cts.Token.IsCancellationRequested; i++)
                        {
                            int key = random.Next(initialKeys);
                            store.Get(BitConverter.GetBytes(key));
                        }
                    }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
                }))
                .ToArray();

            Task.WaitAll([writerTask, .. readerTasks]);

            Assert.That(exceptions, Is.Empty,
                $"[{configName}] Exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");

            TestContext.WriteLine($"[{configName}] Writer: {writerOps} ops, Readers: 4x{readerOps} ops");
        }

        #endregion

        #region Durability Tests

        [Test]
        [TestCaseSource(nameof(LsmOptionCombinations))]
        public void RecoveryAfterReopenTest(bool enableWal, bool enableCache, bool bgCompaction, string configName)
        {
            const int count = 5_000;
            var dir = Path.Combine(m_testDir, $"recovery_{configName}");

            var options = new LsmOptions
            {
                EnableWal = enableWal,
                SyncWrites = false,
                EnableBlockCache = enableCache,
                BlockCacheSizeBytes = 5 * 1024 * 1024,
                MemTableSizeLimit = 256 * 1024,
                Level0CompactionTrigger = 4,
                BackgroundCompaction = false // Sync for deterministic state
            };

            // Write phase
            using (var store = new StoreLsm(dir, options))
            {
                for (int i = 0; i < count; i++)
                {
                    store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
                }
                // Don't flush - let some data be in MemTable/WAL
            }

            // Reopen and verify
            using (var store = new StoreLsm(dir, options))
            {
                int recovered = 0;
                for (int i = 0; i < count; i++)
                {
                    var result = store.Get(BitConverter.GetBytes(i));
                    if (result != null && BitConverter.ToInt32(result) == i * 10)
                        recovered++;
                }

                // With WAL enabled, all should be recovered
                // Without WAL, only flushed data survives
                if (enableWal)
                {
                    Assert.That(recovered, Is.EqualTo(count), $"[{configName}] Not all keys recovered");
                }
                else
                {
                    TestContext.WriteLine($"[{configName}] Recovered {recovered}/{count} keys (no WAL)");
                }
            }
        }

        [Test]
        public void MultipleReopenCyclesTest()
        {
            const int cycles = 5;
            const int keysPerCycle = 1_000;
            var dir = Path.Combine(m_testDir, "multi_reopen");

            var options = new LsmOptions
            {
                EnableWal = true,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 5 * 1024 * 1024,
                MemTableSizeLimit = 128 * 1024,
                Level0CompactionTrigger = 4,
                BackgroundCompaction = false
            };

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                using var store = new StoreLsm(dir, options);

                // Verify previous cycles' data
                for (int prevCycle = 0; prevCycle < cycle; prevCycle++)
                {
                    for (int i = 0; i < keysPerCycle; i++)
                    {
                        int key = prevCycle * keysPerCycle + i;
                        var result = store.Get(BitConverter.GetBytes(key));
                        Assert.That(result, Is.Not.Null, $"Cycle {cycle}: Key from cycle {prevCycle} missing");
                    }
                }

                // Write this cycle's data
                for (int i = 0; i < keysPerCycle; i++)
                {
                    int key = cycle * keysPerCycle + i;
                    store.Put(BitConverter.GetBytes(key), BitConverter.GetBytes(cycle));
                }
            }

            // Final verification
            using (var store = new StoreLsm(dir, options))
            {
                int totalKeys = cycles * keysPerCycle;
                var scanCount = store.Scan(null, null).Count();
                Assert.That(scanCount, Is.EqualTo(totalKeys));
            }
        }

        #endregion

        #region Heavy Load Tests

        [Test]
        public void LargeDatasetWithMixedOperationsTest()
        {
            const int initialKeys = 50_000;
            const int operations = 100_000;
            var dir = Path.Combine(m_testDir, "large_mixed");

            var options = new LsmOptions
            {
                EnableWal = false,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 50 * 1024 * 1024,
                MemTableSizeLimit = 4 * 1024 * 1024,
                Level0CompactionTrigger = 4,
                BackgroundCompaction = true
            };

            using var store = new StoreLsm(dir, options);
            var expected = new Dictionary<int, int>();
            var random = new Random(42);

            // Initial load
            for (int i = 0; i < initialKeys; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
                expected[i] = i;
            }

            // Mixed operations: 50% read, 35% write, 15% delete
            for (int op = 0; op < operations; op++)
            {
                int key = random.Next(initialKeys + op / 10);
                int action = random.Next(100);

                if (action < 50) // Read
                {
                    var result = store.Get(BitConverter.GetBytes(key));
                    if (expected.TryGetValue(key, out var expectedVal))
                    {
                        Assert.That(result, Is.Not.Null);
                        Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(expectedVal));
                    }
                }
                else if (action < 85) // Write
                {
                    int value = op;
                    store.Put(BitConverter.GetBytes(key), BitConverter.GetBytes(value));
                    expected[key] = value;
                }
                else // Delete
                {
                    store.Delete(BitConverter.GetBytes(key));
                    expected.Remove(key);
                }
            }

            store.Checkpoint();
            store.WaitForCompaction();

            // Sample verification
            foreach (var (key, value) in expected.Take(1000))
            {
                var result = store.Get(BitConverter.GetBytes(key));
                Assert.That(result, Is.Not.Null);
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(value));
            }

            TestContext.WriteLine($"Final state: {expected.Count} keys, {store.SSTableCount} SSTables");
            TestContext.WriteLine($"Stats: {store.Statistics.GetSnapshot()}");
        }

        [Test]
        public void CompactionUnderLoadTest()
        {
            var dir = Path.Combine(m_testDir, "compaction_load");

            var options = new LsmOptions
            {
                EnableWal = false,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 20 * 1024 * 1024,
                MemTableSizeLimit = 64 * 1024, // Small - frequent flushes
                Level0CompactionTrigger = 3,   // Aggressive compaction
                BackgroundCompaction = true
            };

            using var store = new StoreLsm(dir, options);

            const int waves = 20;
            const int keysPerWave = 1_000;

            for (int wave = 0; wave < waves; wave++)
            {
                // Write same keys with new values
                for (int i = 0; i < keysPerWave; i++)
                {
                    store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(wave));
                }

                // Verify random sample during load
                var random = new Random(wave);
                for (int check = 0; check < 100; check++)
                {
                    int key = random.Next(keysPerWave);
                    var result = store.Get(BitConverter.GetBytes(key));
                    Assert.That(result, Is.Not.Null);
                }
            }

            store.Checkpoint();
            store.WaitForCompaction();

            // Final verification - should have latest values
            for (int i = 0; i < keysPerWave; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null);
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(waves - 1));
            }

            TestContext.WriteLine($"After {waves} waves: {store.SSTableCount} SSTables, {store.Statistics.Compactions} compactions");
        }

        #endregion

        #region Edge Cases

        [Test]
        [TestCaseSource(nameof(LsmOptionCombinations))]
        public void EmptyKeyValueTest(bool enableWal, bool enableCache, bool bgCompaction, string configName)
        {
            var dir = Path.Combine(m_testDir, $"empty_{configName}");

            var options = new LsmOptions
            {
                EnableWal = enableWal,
                EnableBlockCache = enableCache,
                BackgroundCompaction = bgCompaction
            };

            using var store = new StoreLsm(dir, options);

            // Empty value
            store.Put(BitConverter.GetBytes(1), []);
            var result = store.Get(BitConverter.GetBytes(1));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Length, Is.EqualTo(0));

            // Large key
            var largeKey = new byte[1000];
            new Random(42).NextBytes(largeKey);
            store.Put(largeKey, BitConverter.GetBytes(42));
            
            result = store.Get(largeKey);
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(42));
        }

        [Test]
        [TestCaseSource(nameof(LsmOptionCombinations))]
        public void LargeValuesTest(bool enableWal, bool enableCache, bool bgCompaction, string configName)
        {
            var dir = Path.Combine(m_testDir, $"large_val_{configName}");

            var options = new LsmOptions
            {
                EnableWal = enableWal,
                EnableBlockCache = enableCache,
                MemTableSizeLimit = 1024 * 1024,
                BackgroundCompaction = bgCompaction
            };

            using var store = new StoreLsm(dir, options);
            var random = new Random(42);

            // Values up to 100KB
            int[] sizes = [1000, 10_000, 50_000, 100_000];
            foreach (var size in sizes)
            {
                var value = new byte[size];
                random.NextBytes(value);
                
                store.Put(BitConverter.GetBytes(size), value);
                store.Checkpoint();

                var result = store.Get(BitConverter.GetBytes(size));
                Assert.That(result, Is.Not.Null, $"[{configName}] Value size {size} not found");
                Assert.That(result, Is.EqualTo(value), $"[{configName}] Value size {size} mismatch");
            }
        }

        [Test]
        public void RapidOpenCloseTest()
        {
            var dir = Path.Combine(m_testDir, "rapid_open_close");

            var options = new LsmOptions
            {
                EnableWal = true,
                EnableBlockCache = true,
                MemTableSizeLimit = 64 * 1024
            };

            for (int i = 0; i < 20; i++)
            {
                using var store = new StoreLsm(dir, options);
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }

            // Verify all data survived
            using (var store = new StoreLsm(dir, options))
            {
                for (int i = 0; i < 20; i++)
                {
                    var result = store.Get(BitConverter.GetBytes(i));
                    Assert.That(result, Is.Not.Null, $"Key {i} missing after rapid open/close");
                }
            }
        }

        #endregion
    }
}
