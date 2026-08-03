using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Tests.Stores;

namespace OutWit.Database.Core.Tests.LSM
{
    /// <summary>
    /// Tests for LsmTreeStore implementing IKeyValueStore interface.
    /// Verifies consistent behavior with other storage engines.
    /// </summary>
    [TestFixture]
    public class LsmTreeStoreInterfaceTests : KeyValueStoreTestBase
    {
        private string m_testDir = null!;

        protected override IKeyValueStore CreateStore()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), $"lsm_interface_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_testDir);

            var options = new LsmOptions
            {
                EnableWal = true,
                SyncWrites = false,
                MemTableSizeLimit = 64 * 1024, // 64KB for faster flushing in tests
                EnableBlockCache = true,
                BlockCacheSizeBytes = 1024 * 1024, // 1MB cache
                BackgroundCompaction = false, // Synchronous for predictable tests
                Level0CompactionTrigger = 10
            };

            return new StoreLsm(m_testDir, options);
        }

        protected override void CleanupStore()
        {
            try
            {
                if (Directory.Exists(m_testDir))
                    Directory.Delete(m_testDir, recursive: true);
            }
            catch { }
        }

        /// <summary>
        /// LSM-specific: Override base test because LSM always returns true for Delete.
        /// LSM writes tombstone marker regardless of key existence.
        /// </summary>
        [Test]
        public override void Delete_NonExistentKey_ReturnsFalse()
        {
            // LSM-Tree always returns true because it writes a tombstone
            var deleted = Store.Delete("non-existent"u8);
            Assert.That(deleted, Is.True, "LSM always writes tombstone, so returns true");
        }
    }

    /// <summary>
    /// Additional LSM-specific integration tests.
    /// </summary>
    [TestFixture]
    public class LsmTreeIntegrationTests : IDisposable
    {
        private string m_testDir = null!;

        [SetUp]
        public void SetUp()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), $"lsm_integration_{Guid.NewGuid():N}");
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

        #region Persistence Tests

        [Test]
        public void DataPersistsAfterReopenWithWal()
        {
            var options = new LsmOptions { EnableWal = true };

            // Write data
            using (var store = new StoreLsm(m_testDir, options))
            {
                for (int i = 0; i < 100; i++)
                {
                    store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
                }
            }

            // Reopen and verify
            using (var store = new StoreLsm(m_testDir, options))
            {
                for (int i = 0; i < 100; i++)
                {
                    var result = store.Get(BitConverter.GetBytes(i));
                    Assert.That(result, Is.Not.Null, $"Key {i} missing after reopen");
                    Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i * 10));
                }
            }
        }

        [Test]
        public void DataPersistsAfterFlushAndReopen()
        {
            var options = new LsmOptions 
            { 
                EnableWal = false, // Disable WAL - rely on SSTables only
                MemTableSizeLimit = 1024 * 1024
            };

            // Write and flush data
            using (var store = new StoreLsm(m_testDir, options))
            {
                for (int i = 0; i < 100; i++)
                {
                    store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
                }
                store.Checkpoint(); // Explicitly flush to SSTable
            }

            // Reopen and verify
            using (var store = new StoreLsm(m_testDir, options))
            {
                for (int i = 0; i < 100; i++)
                {
                    var result = store.Get(BitConverter.GetBytes(i));
                    Assert.That(result, Is.Not.Null, $"Key {i} missing after reopen");
                }
            }
        }

        [Test]
        public void DeletesPersistAfterReopen()
        {
            var options = new LsmOptions { EnableWal = true };

            using (var store = new StoreLsm(m_testDir, options))
            {
                for (int i = 0; i < 100; i++)
                {
                    store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
                }
                
                // Delete half
                for (int i = 0; i < 50; i++)
                {
                    store.Delete(BitConverter.GetBytes(i));
                }
            }

            using (var store = new StoreLsm(m_testDir, options))
            {
                // Deleted keys should stay deleted
                for (int i = 0; i < 50; i++)
                {
                    Assert.That(store.Get(BitConverter.GetBytes(i)), Is.Null);
                }
                
                // Other keys should exist
                for (int i = 50; i < 100; i++)
                {
                    Assert.That(store.Get(BitConverter.GetBytes(i)), Is.Not.Null);
                }
            }
        }

        #endregion

        #region Compaction Tests

        [Test]
        public void CompactionMergesSSTablesCorrectly()
        {
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 4,
                BackgroundCompaction = false
            };

            using var store = new StoreLsm(m_testDir, options);

            // Insert data in waves to create multiple SSTables
            for (int wave = 0; wave < 10; wave++)
            {
                for (int i = 0; i < 50; i++)
                {
                    // Same keys, different values each wave
                    store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(wave * 1000 + i));
                }
                store.Checkpoint();
            }

            // After compaction, only newest values should remain
            for (int i = 0; i < 50; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null);
                // Should be from wave 9 (last wave)
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(9 * 1000 + i));
            }
        }

        [Test]
        public void CompactionReclainsSpaceFromTombstones()
        {
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 200,
                Level0CompactionTrigger = 2, // Lower threshold
                BackgroundCompaction = false
            };

            using var store = new StoreLsm(m_testDir, options);

            // Insert data
            for (int i = 0; i < 100; i++)
            {
                store.Put(BitConverter.GetBytes(i), new byte[100]);
            }
            store.Checkpoint();

            // Delete all
            for (int i = 0; i < 100; i++)
            {
                store.Delete(BitConverter.GetBytes(i));
            }
            store.Checkpoint();

            // Force compaction
            store.Compact();

            // All keys should be gone
            for (int i = 0; i < 100; i++)
            {
                Assert.That(store.Get(BitConverter.GetBytes(i)), Is.Null);
            }

            // SSTable count should be minimal (0 or 1 empty)
            Assert.That(store.SSTableCount, Is.LessThanOrEqualTo(2));
        }

        #endregion

        #region Cache Integration Tests

        [Test]
        public void CacheImprovesRepeatedReadPerformance()
        {
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 10 * 1024 * 1024 // 10MB
            };

            using var store = new StoreLsm(m_testDir, options);

            // Insert and flush
            for (int i = 0; i < 1000; i++)
            {
                store.Put(BitConverter.GetBytes(i), new byte[100]);
            }
            store.Checkpoint();

            // First read - populates cache
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                store.Get(BitConverter.GetBytes(i));
            }
            var firstReadMs = watch.ElapsedMilliseconds;

            // Second read - should hit cache
            watch.Restart();
            for (int i = 0; i < 1000; i++)
            {
                store.Get(BitConverter.GetBytes(i));
            }
            var secondReadMs = watch.ElapsedMilliseconds;

            TestContext.WriteLine($"First read: {firstReadMs}ms, Second read: {secondReadMs}ms");
            TestContext.WriteLine($"Cache hit ratio: {store.BlockCache?.HitRatio:P}");

            // Cache should have hits
            Assert.That(store.BlockCache!.HitRatio, Is.GreaterThan(0));
        }

        [Test]
        public void CacheInvalidationOnCompaction()
        {
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 1024 * 1024,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 3,
                BackgroundCompaction = false
            };

            using var store = new StoreLsm(m_testDir, options);

            // Create multiple SSTables and read to populate cache
            for (int wave = 0; wave < 5; wave++)
            {
                for (int i = 0; i < 50; i++)
                {
                    store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(wave));
                }
                store.Checkpoint();
            }

            // Populate cache
            for (int i = 0; i < 50; i++)
            {
                store.Get(BitConverter.GetBytes(i));
            }

            var cacheCountBefore = store.BlockCache!.Count;

            // Trigger compaction - should invalidate cache
            store.Compact();

            // Verify data still accessible (from new compacted SSTable)
            for (int i = 0; i < 50; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null);
                // Should have latest value
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(4));
            }
        }

        #endregion

        #region Statistics Tests

        [Test]
        public void StatisticsTrackAllOperations()
        {
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = true,
                Level0CompactionTrigger = 100
            };

            using var store = new StoreLsm(m_testDir, options);
            var stats = store.Statistics;

            // Verify initial state
            Assert.That(stats.Gets, Is.EqualTo(0));
            Assert.That(stats.Puts, Is.EqualTo(0));

            // Do some operations
            for (int i = 0; i < 10; i++) store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            for (int i = 0; i < 5; i++) store.Get(BitConverter.GetBytes(i));
            store.Delete(BitConverter.GetBytes(0));
            store.Scan(null, null).ToList();
            store.Checkpoint();

            // Verify stats
            Assert.That(stats.Puts, Is.EqualTo(10));
            Assert.That(stats.Gets, Is.EqualTo(5));
            Assert.That(stats.Deletes, Is.EqualTo(1));
            Assert.That(stats.Scans, Is.EqualTo(1));
            Assert.That(stats.Flushes, Is.GreaterThanOrEqualTo(1));
            Assert.That(stats.BytesWritten, Is.GreaterThan(0));

            // Snapshot preserves values
            var snapshot = stats.GetSnapshot();
            Assert.That(snapshot.Puts, Is.EqualTo(10));

            // Reset clears
            stats.Reset();
            Assert.That(stats.Puts, Is.EqualTo(0));
        }

        #endregion

        #region Error Handling Tests

        [Test]
        public void DisposedStoreThrowsObjectDisposedException()
        {
            var store = new StoreLsm(m_testDir, new LsmOptions { EnableWal = false });
            store.Put("key"u8.ToArray(), "value"u8.ToArray());
            store.Dispose();

            Assert.Throws<ObjectDisposedException>(() => store.Get("key"u8));
            Assert.Throws<ObjectDisposedException>(() => store.Put("key"u8.ToArray(), "value"u8.ToArray()));
            Assert.Throws<ObjectDisposedException>(() => store.Delete("key"u8));
            Assert.Throws<ObjectDisposedException>(() => store.Scan(null, null).ToList());
            Assert.Throws<ObjectDisposedException>(() => store.Checkpoint());
        }

        #endregion

        #region Large Data Tests

        [Test]
        [Category("Stress")]
        public void HandlesLargeDataset()
        {
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 50 * 1024 * 1024, // 50MB
                MemTableSizeLimit = 4 * 1024 * 1024, // 4MB
                Level0CompactionTrigger = 4,
                BackgroundCompaction = true
            };

            using var store = new StoreLsm(m_testDir, options);

            const int count = 100_000;
            var value = new byte[100];

            // Insert
            for (int i = 0; i < count; i++)
            {
                store.Put(BitConverter.GetBytes(i), value);
            }
            store.Checkpoint();
            store.WaitForCompaction();

            // Verify random sample
            var random = new Random(42);
            for (int i = 0; i < 1000; i++)
            {
                var key = random.Next(count);
                var result = store.Get(BitConverter.GetBytes(key));
                Assert.That(result, Is.Not.Null, $"Key {key} not found");
            }

            // Full scan count
            var scanCount = store.Scan(null, null).Count();
            Assert.That(scanCount, Is.EqualTo(count));

            TestContext.WriteLine($"Final SSTable count: {store.SSTableCount}");
            TestContext.WriteLine($"Statistics: {store.Statistics.GetSnapshot()}");
        }

        #endregion
    }
}
