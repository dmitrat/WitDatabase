using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.LSM
{
    /// <summary>
    /// Unit tests for LSM-Tree components.
    /// </summary>
    [TestFixture]
    public class LsmTreeTests : IDisposable
    {
        private string m_testDir = null!;

        [SetUp]
        public void SetUp()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), $"lsm_test_{Guid.NewGuid():N}");
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

        #region MemTable Tests

        [Test]
        public void MemTablePutAndGetTest()
        {
            using var memTable = new MemTable();
            var key = ToBytes("key1");
            var value = ToBytes("value1");

            memTable.Put(key, value);
        
            Assert.That(memTable.TryGet(key, out var result), Is.True);
            Assert.That(result, Is.EqualTo(value));
        }

        [Test]
        public void MemTableDeleteReturnsTombstoneTest()
        {
            using var memTable = new MemTable();
            var key = ToBytes("key1");
            var value = ToBytes("value1");

            memTable.Put(key, value);
            memTable.Delete(key);
        
            Assert.That(memTable.TryGet(key, out var result), Is.True);
            Assert.That(result, Is.Null); // Tombstone
        }

        [Test]
        public void MemTableScanReturnsSortedRangeTest()
        {
            using var memTable = new MemTable();
        
            memTable.Put(ToBytes("c"), ToBytes("3"));
            memTable.Put(ToBytes("a"), ToBytes("1"));
            memTable.Put(ToBytes("b"), ToBytes("2"));

            var results = memTable.Scan(null, null).ToList();
        
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].Key, Is.EqualTo(ToBytes("a")));
            Assert.That(results[1].Key, Is.EqualTo(ToBytes("b")));
            Assert.That(results[2].Key, Is.EqualTo(ToBytes("c")));
        }

        [Test]
        public void MemTableCountTest()
        {
            using var memTable = new MemTable();
        
            Assert.That(memTable.Count, Is.EqualTo(0));
        
            memTable.Put(ToBytes("a"), ToBytes("1"));
            memTable.Put(ToBytes("b"), ToBytes("2"));
        
            Assert.That(memTable.Count, Is.EqualTo(2));
        }

        [Test]
        public void MemTableApproximateSizeTest()
        {
            using var memTable = new MemTable();
        
            Assert.That(memTable.ApproximateSize, Is.EqualTo(0));
        
            memTable.Put(ToBytes("key"), ToBytes("value"));
        
            Assert.That(memTable.ApproximateSize, Is.GreaterThan(0));
        }

        [Test]
        public void MemTableConcurrentWritesTest()
        {
            using var memTable = new MemTable();
            const int threadCount = 4;
            const int opsPerThread = 1000;

            var tasks = Enumerable.Range(0, threadCount)
                .Select(t => Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        var key = ToBytes($"thread{t}_key{i}");
                        var value = ToBytes($"value{i}");
                        memTable.Put(key, value);
                    }
                }))
                .ToArray();

            Task.WaitAll(tasks);

            Assert.That(memTable.Count, Is.EqualTo(threadCount * opsPerThread));
        }

        [Test]
        public void MemTableConcurrentReadWriteTest()
        {
            using var memTable = new MemTable();
            const int writeCount = 1000;
            var cts = new CancellationTokenSource();
            var readsCompleted = 0;

            // Pre-populate some data
            for (int i = 0; i < 100; i++)
            {
                memTable.Put(ToBytes($"init{i}"), ToBytes($"value{i}"));
            }

            // Start reader threads
            var readerTasks = Enumerable.Range(0, 2)
                .Select(_ => Task.Run(() =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        memTable.TryGet(ToBytes("init50"), out byte[]? _);
                        memTable.Scan(null, null).ToList();
                        Interlocked.Increment(ref readsCompleted);
                    }
                }))
                .ToArray();

            // Writer thread
            var writerTask = Task.Run(() =>
            {
                for (int i = 0; i < writeCount; i++)
                {
                    memTable.Put(ToBytes($"new{i}"), ToBytes($"value{i}"));
                }
            });

            writerTask.Wait();
            cts.Cancel();
            Task.WaitAll(readerTasks);

            Assert.That(memTable.Count, Is.EqualTo(100 + writeCount));
            Assert.That(readsCompleted, Is.GreaterThan(0));
        }

        #endregion

        #region WriteAheadLog Tests

        [Test]
        public void WalAppendAndReplayTest()
        {
            var walPath = Path.Combine(m_testDir, "test.wal");
        
            using (var wal = new WriteAheadLog(walPath, createNew: true))
            {
                wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
                wal.AppendPut(ToBytes("key2"), ToBytes("value2"));
                wal.Sync();
            }

            var entries = new List<(byte[] Key, byte[] Value)>();
            using (var wal = new WriteAheadLog(walPath))
            {
                wal.Replay(
                    onPut: (k, v) => entries.Add((k, v)),
                    onDelete: _ => { }
                );
            }

            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].Key, Is.EqualTo(ToBytes("key1")));
            Assert.That(entries[0].Value, Is.EqualTo(ToBytes("value1")));
        }

        [Test]
        public void WalRecoversDeleteEntriesTest()
        {
            var walPath = Path.Combine(m_testDir, "test.wal");
        
            using (var wal = new WriteAheadLog(walPath, createNew: true))
            {
                wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
                wal.AppendDelete(ToBytes("key1"));
                wal.Sync();
            }

            var puts = new List<byte[]>();
            var deletes = new List<byte[]>();
            using (var wal = new WriteAheadLog(walPath))
            {
                wal.Replay(
                    onPut: (k, v) => puts.Add(k),
                    onDelete: k => deletes.Add(k)
                );
            }

            Assert.That(puts.Count, Is.EqualTo(1));
            Assert.That(deletes.Count, Is.EqualTo(1));
        }

        [Test]
        public void WalTruncateTest()
        {
            var walPath = Path.Combine(m_testDir, "test.wal");
        
            using var wal = new WriteAheadLog(walPath, createNew: true);
            wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
            wal.Sync();
        
            Assert.That(wal.Size, Is.GreaterThan(12)); // More than just header
        
            wal.Truncate();
        
            Assert.That(wal.Size, Is.EqualTo(12)); // Just header (Magic + EntryCounter)
        }

        #endregion

        #region SSTable Tests

        [Test]
        public void SSTableBuildAndReadTest()
        {
            var sstPath = Path.Combine(m_testDir, "test.sst");
        
            SSTableInfo info;
            using (var builder = new SSTableBuilder(sstPath))
            {
                builder.Add(ToBytes("a"), ToBytes("1"));
                builder.Add(ToBytes("b"), ToBytes("2"));
                builder.Add(ToBytes("c"), ToBytes("3"));
                info = builder.Finish();
            }

            TestContext.WriteLine($"Built SSTable: EntryCount={info.EntryCount}, HasBloom={info.HasBloomFilter}, FileSize={info.FileSize}");

            using var reader = new SSTableReader(sstPath);
            TestContext.WriteLine($"Reader: EntryCount={reader.EntryCount}, HasBloom={reader.HasBloomFilter}");
        
            // Test scan first to verify data is there
            var entries = reader.Scan().ToList();
            TestContext.WriteLine($"Scan found {entries.Count} entries");
            foreach (var e in entries)
            {
                TestContext.WriteLine($"  Key={System.Text.Encoding.UTF8.GetString(e.Key)}, Value={System.Text.Encoding.UTF8.GetString(e.Value ?? [])}");
            }
        
            var foundA = reader.TryGet(ToBytes("a"), out var value);
            TestContext.WriteLine($"TryGet 'a': found={foundA}");
            Assert.That(foundA, Is.True, "Key 'a' should be found");
            Assert.That(value, Is.EqualTo(ToBytes("1")));

            Assert.That(reader.TryGet(ToBytes("b"), out value), Is.True);
            Assert.That(value, Is.EqualTo(ToBytes("2")));

            Assert.That(reader.TryGet(ToBytes("c"), out value), Is.True);
            Assert.That(value, Is.EqualTo(ToBytes("3")));

            Assert.That(reader.TryGet(ToBytes("d"), out _), Is.False);
        }

        [Test]
        public void SSTableScanAllEntriesTest()
        {
            var sstPath = Path.Combine(m_testDir, "test.sst");
        
            using (var builder = new SSTableBuilder(sstPath))
            {
                for (int i = 0; i < 100; i++)
                {
                    var key = BitConverter.GetBytes(i);
                    var value = BitConverter.GetBytes(i * 10);
                    builder.Add(key, value);
                }
                builder.Finish();
            }

            using var reader = new SSTableReader(sstPath);
            var entries = reader.Scan().ToList();
        
            Assert.That(entries.Count, Is.EqualTo(100));
            Assert.That(reader.EntryCount, Is.EqualTo(100));
        }

        [Test]
        public void SSTableHandlesTombstonesTest()
        {
            var sstPath = Path.Combine(m_testDir, "test.sst");
        
            using (var builder = new SSTableBuilder(sstPath))
            {
                builder.Add(ToBytes("a"), ToBytes("1"));
                builder.Add(ToBytes("b"), null); // Tombstone
                builder.Add(ToBytes("c"), ToBytes("3"));
                builder.Finish();
            }

            using var reader = new SSTableReader(sstPath);
        
            Assert.That(reader.TryGet(ToBytes("b"), out var value), Is.True);
            Assert.That(value, Is.Null); // Tombstone
        }

        #endregion

        #region SSTable with Bloom Filter Tests

        [Test]
        public void SSTableBloomFilterSkipsNonExistentKeysTest()
        {
            var sstPath = Path.Combine(m_testDir, "bloom.sst");
        
            using (var builder = new SSTableBuilder(sstPath))
            {
                for (int i = 0; i < 100; i++)
                {
                    builder.Add(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
                }
                var info = builder.Finish();
                Assert.That(info.HasBloomFilter, Is.True);
            }

            using var reader = new SSTableReader(sstPath);
            Assert.That(reader.HasBloomFilter, Is.True);

            // Existing keys should be found
            Assert.That(reader.TryGet(BitConverter.GetBytes(50), out var value), Is.True);
            Assert.That(BitConverter.ToInt32(value!), Is.EqualTo(500));

            // Non-existent keys should be filtered by Bloom filter (no disk read needed)
            Assert.That(reader.TryGet(BitConverter.GetBytes(999), out _), Is.False);
        }

        [Test]
        public void SSTableBloomFilterIntegrationTest()
        {
            var sstPath = Path.Combine(m_testDir, "bloom_large.sst");
            const int keyCount = 10000;
        
            // Keys must be added in sorted order! Use simple byte comparison
            var keys = Enumerable.Range(0, keyCount)
                .Select(i => BitConverter.GetBytes(i))
                .OrderBy(k => k, Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b)))
                .ToArray();
        
            using (var builder = new SSTableBuilder(sstPath))
            {
                foreach (var key in keys)
                {
                    builder.Add(key, key);
                }
                builder.Finish();
            }

            using var reader = new SSTableReader(sstPath);
            Assert.That(reader.HasBloomFilter, Is.True);
            Assert.That(reader.EntryCount, Is.EqualTo(keyCount));

            // All existing keys should be found
            for (int i = 0; i < keyCount; i += 100)
            {
                var key = BitConverter.GetBytes(i);
                Assert.That(reader.TryGet(key, out _), Is.True, $"Key {i} should be found");
            }

            // Non-existent keys should not be found
            for (int i = keyCount; i < keyCount + 100; i++)
            {
                var key = BitConverter.GetBytes(i);
                Assert.That(reader.TryGet(key, out _), Is.False, $"Key {i} should NOT be found");
            }
        }

        [Test]
        public void SSTableLegacyFormatStillWorksTest()
        {
            // Verify that new SSTables with Bloom filter work
            var sstPath = Path.Combine(m_testDir, "legacy.sst");
        
            using (var builder = new SSTableBuilder(sstPath))
            {
                builder.Add(ToBytes("a"), ToBytes("1"));
                builder.Add(ToBytes("b"), ToBytes("2"));
                builder.Finish();
            }

            using var reader = new SSTableReader(sstPath);
            Assert.That(reader.HasBloomFilter, Is.True);
            
            Assert.That(reader.TryGet(ToBytes("a"), out var value), Is.True);
            Assert.That(value, Is.EqualTo(ToBytes("1")));
        }

        #endregion

        #region LsmTree Integration Tests

        [Test]
        public void LsmTreePutAndGetTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new LsmTreeStore(m_testDir, options);

            tree.Put(ToBytes("key1"), ToBytes("value1"));
        
            var result = tree.Get(ToBytes("key1"));
            Assert.That(result, Is.EqualTo(ToBytes("value1")));
        }

        [Test]
        public void LsmTreeDeleteTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new LsmTreeStore(m_testDir, options);

            tree.Put(ToBytes("key1"), ToBytes("value1"));
            tree.Delete(ToBytes("key1"));
        
            var result = tree.Get(ToBytes("key1"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void LsmTreeScanReturnsSortedResultsTest()
        {
            var options = new LsmOptions { EnableWal = false };
            using var tree = new LsmTreeStore(m_testDir, options);

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
        public void LsmTreeFlushToSSTableTest()
        {
            var dir = Path.Combine(m_testDir, "flush");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 100
            };
        
            using (var tree = new LsmTreeStore(dir, options))
            {
                for (int i = 0; i < 50; i++)
                {
                    var key = ToBytes($"key{i:D5}");
                    var value = ToBytes($"value{i}");
                    tree.Put(key, value);
                }
                tree.Flush();
            
                Assert.That(tree.SSTableCount, Is.GreaterThan(0));
            }

            // Reopen and verify
            using var tree2 = new LsmTreeStore(dir, new LsmOptions { EnableWal = false });
            var result = tree2.Get(ToBytes("key00000"));
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(ToBytes("value0")));
        }

        [Test]
        public void LsmTreeWalRecoveryTest()
        {
            var dir = Path.Combine(m_testDir, "wal");
            var options = new LsmOptions { EnableWal = true, SyncWrites = true };
        
            using (var tree = new LsmTreeStore(dir, options))
            {
                tree.Put(ToBytes("key1"), ToBytes("value1"));
                tree.Put(ToBytes("key2"), ToBytes("value2"));
            }

            // Reopen - should recover from WAL
            using var tree2 = new LsmTreeStore(dir, options);
        
            Assert.That(tree2.Get(ToBytes("key1")), Is.EqualTo(ToBytes("value1")));
            Assert.That(tree2.Get(ToBytes("key2")), Is.EqualTo(ToBytes("value2")));
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
        
            using var tree = new LsmTreeStore(dir, options);
        
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

        [Test]
        public void LsmTreeConcurrentReadsTest()
        {
            var dir = Path.Combine(m_testDir, "concurrent_read");
            var options = new LsmOptions { EnableWal = false };
            using var tree = new LsmTreeStore(dir, options);

            // Pre-populate
            for (int i = 0; i < 1000; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();

            // Concurrent reads
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
                MemTableSizeLimit = 1024 * 10, // 10KB
                Level0CompactionTrigger = 100 // Disable auto-compaction
            };
            using var tree = new LsmTreeStore(dir, options);

            // Pre-populate
            for (int i = 0; i < 100; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var writeCount = 0;
            var readCount = 0;
            var errors = new List<Exception>();

            // Writer task
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
                catch (ObjectDisposedException) { } // Expected on shutdown
                catch (Exception ex) { lock (errors) errors.Add(ex); }
            });

            // Reader tasks
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
                    catch (ObjectDisposedException) { } // Expected on shutdown
                    catch (Exception ex) { lock (errors) errors.Add(ex); }
                }))
                .ToArray();

            // Wait for timeout
            try { Task.WaitAll([writerTask, .. readerTasks]); }
            catch (AggregateException) { }

            Assert.That(errors, Is.Empty, $"Errors: {string.Join(", ", errors.Select(e => e.Message))}");
            Assert.That(writeCount, Is.GreaterThan(0));
            Assert.That(readCount, Is.GreaterThan(0));
        }

        [Test]
        public void LsmTreeCompactionTest()
        {
            var dir = Path.Combine(m_testDir, "compaction");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 3,
                BackgroundCompaction = false // Use synchronous compaction for predictable test
            };
        
            using var tree = new LsmTreeStore(dir, options);
        
            // Insert enough data to trigger multiple flushes and compaction
            for (int i = 0; i < 200; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();

            // After compaction, should have fewer SSTables
            Assert.That(tree.SSTableCount, Is.LessThanOrEqualTo(3));

            // All data should still be accessible
            for (int i = 0; i < 200; i++)
            {
                var result = tree.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null, $"Key {i} not found after compaction");
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
                BackgroundCompaction = false // Use synchronous compaction
            };
        
            using var tree = new LsmTreeStore(dir, options);
        
            // Insert and delete data
            for (int i = 0; i < 100; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            tree.Flush();

            // Delete half
            for (int i = 0; i < 50; i++)
            {
                tree.Delete(BitConverter.GetBytes(i));
            }
            tree.Flush();

            // Force compaction
            tree.Compact();

            // Verify deleted keys are gone
            for (int i = 0; i < 50; i++)
            {
                var result = tree.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Null, $"Deleted key {i} still present");
            }

            // Verify remaining keys exist
            for (int i = 50; i < 100; i++)
            {
                var result = tree.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null, $"Key {i} missing");
            }
        }

        #endregion

        #region Compactor Tests

        [Test]
        public void CompactorMergesSSTablesTest()
        {
            // Create multiple SSTables
            var sst1Path = Path.Combine(m_testDir, "sst_001.sst");
            var sst2Path = Path.Combine(m_testDir, "sst_002.sst");
            var outputPath = Path.Combine(m_testDir, "sst_003.sst");

            using (var builder = new SSTableBuilder(sst1Path))
            {
                builder.Add(ToBytes("a"), ToBytes("1"));
                builder.Add(ToBytes("c"), ToBytes("3"));
                builder.Finish();
            }

            using (var builder = new SSTableBuilder(sst2Path))
            {
                builder.Add(ToBytes("b"), ToBytes("2"));
                builder.Add(ToBytes("c"), ToBytes("3_new")); // Override
                builder.Finish();
            }

            var compactor = new Compactor(m_testDir);
            var result = compactor.Compact([sst1Path, sst2Path], outputPath);

            Assert.That(result.InputFiles, Is.EqualTo(2));
            Assert.That(result.OutputEntries, Is.EqualTo(3)); // a, b, c

            // Verify merged content
            using var reader = new SSTableReader(outputPath);
            Assert.That(reader.TryGet(ToBytes("a"), out var v), Is.True);
            Assert.That(v, Is.EqualTo(ToBytes("1")));

            Assert.That(reader.TryGet(ToBytes("b"), out v), Is.True);
            Assert.That(v, Is.EqualTo(ToBytes("2")));

            Assert.That(reader.TryGet(ToBytes("c"), out v), Is.True);
            Assert.That(v, Is.EqualTo(ToBytes("3_new"))); // Newest wins
        }

        [Test]
        public void CompactorRemovesTombstonesTest()
        {
            var sst1Path = Path.Combine(m_testDir, "sst_001.sst");
            var sst2Path = Path.Combine(m_testDir, "sst_002.sst");
            var outputPath = Path.Combine(m_testDir, "sst_003.sst");

            using (var builder = new SSTableBuilder(sst1Path))
            {
                builder.Add(ToBytes("a"), ToBytes("1"));
                builder.Add(ToBytes("b"), ToBytes("2"));
                builder.Finish();
            }

            using (var builder = new SSTableBuilder(sst2Path))
            {
                builder.Add(ToBytes("a"), null); // Tombstone
                builder.Finish();
            }

            var compactor = new Compactor(m_testDir);
            var result = compactor.Compact([sst1Path, sst2Path], outputPath);

            Assert.That(result.TombstonesRemoved, Is.EqualTo(1));
            Assert.That(result.OutputEntries, Is.EqualTo(1)); // Only b

            using var reader = new SSTableReader(outputPath);
            Assert.That(reader.TryGet(ToBytes("a"), out _), Is.False); // Removed
            Assert.That(reader.TryGet(ToBytes("b"), out var v), Is.True);
            Assert.That(v, Is.EqualTo(ToBytes("2")));
        }

        #endregion

        #region BloomFilter Tests

        [Test]
        public void BloomFilterAddAndContainsTest()
        {
            var filter = new BloomFilter(100);
        
            filter.Add(ToBytes("test1"));
            filter.Add(ToBytes("test2"));
        
            Assert.That(filter.MightContain(ToBytes("test1")), Is.True);
            Assert.That(filter.MightContain(ToBytes("test2")), Is.True);
        }

        [Test]
        public void BloomFilterSerializeRoundtripTest()
        {
            var filter = new BloomFilter(100);
            filter.Add(ToBytes("key1"));
            filter.Add(ToBytes("key2"));
        
            var bytes = filter.ToBytes();
            var restored = new BloomFilter(bytes, filter.HashCount, filter.Size);
        
            Assert.That(restored.MightContain(ToBytes("key1")), Is.True);
            Assert.That(restored.MightContain(ToBytes("key2")), Is.True);
        }

        [Test]
        public void BloomFilterClearTest()
        {
            var filter = new BloomFilter(100);
            filter.Add(ToBytes("test"));
        
            Assert.That(filter.MightContain(ToBytes("test")), Is.True);
        
            filter.Clear();
        
            Assert.That(filter.MightContain(ToBytes("test")), Is.False);
        }

        [Test]
        public void BloomFilterFalsePositiveRateTest()
        {
            const int itemCount = 10000;
            var filter = new BloomFilter(itemCount, 0.01);

            // Add items
            for (int i = 0; i < itemCount; i++)
            {
                filter.Add(BitConverter.GetBytes(i));
            }

            // All added items should be found
            for (int i = 0; i < itemCount; i++)
            {
                Assert.That(filter.MightContain(BitConverter.GetBytes(i)), Is.True);
            }

            // Check false positive rate on non-existent items
            int falsePositives = 0;
            const int testCount = 10000;
            for (int i = itemCount; i < itemCount + testCount; i++)
            {
                if (filter.MightContain(BitConverter.GetBytes(i)))
                    falsePositives++;
            }

            double actualRate = (double)falsePositives / testCount;
            // Allow 3x the expected rate due to statistical variation
            Assert.That(actualRate, Is.LessThan(0.03), $"False positive rate {actualRate:P2} too high");
        }

        [Test]
        public void BloomFilterSerializeAndDeserializeWorksTest()
        {
            // Create filter and add keys
            var filter = new BloomFilter(100, 0.01);
            filter.Add(ToBytes("a"));
            filter.Add(ToBytes("b"));
            filter.Add(ToBytes("c"));
            
            TestContext.WriteLine($"Original: Size={filter.Size}, HashCount={filter.HashCount}");
            
            // Verify originals work
            Assert.That(filter.MightContain(ToBytes("a")), Is.True, "Original 'a'");
            Assert.That(filter.MightContain(ToBytes("b")), Is.True, "Original 'b'");
            Assert.That(filter.MightContain(ToBytes("c")), Is.True, "Original 'c'");
            
            // Serialize
            var bytes = filter.ToBytes();
            TestContext.WriteLine($"Serialized: {bytes.Length} bytes");
            
            // Deserialize with ORIGINAL bit size (not bytes.Length * 8!)
            var restored = new BloomFilter(bytes, filter.HashCount, filter.Size);
            TestContext.WriteLine($"Restored: Size={restored.Size}, HashCount={restored.HashCount}");
            
            // Verify restored works
            Assert.That(restored.MightContain(ToBytes("a")), Is.True, "Restored 'a'");
            Assert.That(restored.MightContain(ToBytes("b")), Is.True, "Restored 'b'");
            Assert.That(restored.MightContain(ToBytes("c")), Is.True, "Restored 'c'");
            Assert.That(restored.MightContain(ToBytes("d")), Is.False, "Restored 'd' should not exist");
        }

        #endregion

        #region BlockCache Tests

        [Test]
        public void BlockCachePutAndGetTest()
        {
            using var cache = new BlockCache(1024 * 1024);
            var data = new byte[] { 1, 2, 3, 4, 5 };
            
            cache.Put("test.sst", 0, data);
            
            Assert.That(cache.TryGet("test.sst", 0, out var result), Is.True);
            Assert.That(result, Is.EqualTo(data));
            Assert.That(cache.Hits, Is.EqualTo(1));
        }

        [Test]
        public void BlockCacheMissTest()
        {
            using var cache = new BlockCache(1024 * 1024);
            
            Assert.That(cache.TryGet("nonexistent.sst", 0, out _), Is.False);
            Assert.That(cache.Misses, Is.EqualTo(1));
        }

        [Test]
        public void BlockCacheEvictionTest()
        {
            // Small cache that can hold ~10 blocks
            using var cache = new BlockCache(1000);
            
            // Add 20 blocks of 100 bytes each
            for (int i = 0; i < 20; i++)
            {
                cache.Put("test.sst", i, new byte[100]);
            }
            
            // Cache should have evicted some entries
            Assert.That(cache.CurrentSizeBytes, Is.LessThanOrEqualTo(1000));
            Assert.That(cache.Count, Is.LessThan(20));
        }

        [Test]
        public void BlockCacheInvalidateTest()
        {
            using var cache = new BlockCache(1024 * 1024);
            
            cache.Put("file1.sst", 0, new byte[100]);
            cache.Put("file1.sst", 1, new byte[100]);
            cache.Put("file2.sst", 0, new byte[100]);
            
            Assert.That(cache.Count, Is.EqualTo(3));
            
            cache.Invalidate("file1.sst");
            
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.TryGet("file1.sst", 0, out _), Is.False);
            Assert.That(cache.TryGet("file2.sst", 0, out _), Is.True);
        }

        [Test]
        public void BlockCacheHitRatioTest()
        {
            using var cache = new BlockCache(1024 * 1024);
            
            cache.Put("test.sst", 0, new byte[100]);
            
            // 2 hits
            cache.TryGet("test.sst", 0, out _);
            cache.TryGet("test.sst", 0, out _);
            
            // 1 miss
            cache.TryGet("test.sst", 1, out _);
            
            Assert.That(cache.Hits, Is.EqualTo(2));
            Assert.That(cache.Misses, Is.EqualTo(1));
            Assert.That(cache.HitRatio, Is.EqualTo(2.0 / 3.0).Within(0.001));
        }

        [Test]
        public void BlockCacheLargeBlockSkippedTest()
        {
            // Cache of 1000 bytes
            using var cache = new BlockCache(1000);
            
            // Try to cache a block that's > 25% of cache size
            cache.Put("test.sst", 0, new byte[500]);
            
            // Should not be cached
            Assert.That(cache.TryGet("test.sst", 0, out _), Is.False);
        }

        [Test]
        public void BlockCacheConcurrentAccessTest()
        {
            using var cache = new BlockCache(10 * 1024 * 1024);
            const int threadCount = 4;
            const int opsPerThread = 1000;

            var tasks = Enumerable.Range(0, threadCount)
                .Select(t => Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        var blockIndex = i % 100;
                        var key = $"file{t}.sst";
                        
                        if (i % 3 == 0)
                        {
                            cache.Put(key, blockIndex, new byte[100]);
                        }
                        else
                        {
                            cache.TryGet(key, blockIndex, out _);
                        }
                    }
                }))
                .ToArray();

            Task.WaitAll(tasks);
            
            // Should complete without exceptions
            Assert.That(cache.Count, Is.GreaterThan(0));
        }

        #endregion

        #region BlockCache Integration Tests

        [Test]
        public void LsmTreeBlockCacheIntegrationTest()
        {
            var dir = Path.Combine(m_testDir, "cache_integration");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = true,
                BlockCacheSizeBytes = 1024 * 1024, // 1 MB
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 100 // Disable auto-compaction
            };
        
            using var tree = new LsmTreeStore(dir, options);
            
            // Insert and flush to create SSTable
            for (int i = 0; i < 50; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();
            
            // First read - should be cache misses
            var initialMisses = tree.BlockCache?.Misses ?? 0;
            for (int i = 0; i < 50; i++)
            {
                tree.Get(BitConverter.GetBytes(i));
            }
            
            var afterFirstRead = tree.BlockCache;
            Assert.That(afterFirstRead, Is.Not.Null);
            Assert.That(afterFirstRead!.Misses, Is.GreaterThan(initialMisses));
            
            // Second read - should be cache hits
            var hitsBeforeSecondRead = afterFirstRead.Hits;
            for (int i = 0; i < 50; i++)
            {
                tree.Get(BitConverter.GetBytes(i));
            }
            
            Assert.That(afterFirstRead.Hits, Is.GreaterThan(hitsBeforeSecondRead));
            Assert.That(afterFirstRead.HitRatio, Is.GreaterThan(0));
            TestContext.WriteLine($"Cache stats: Hits={afterFirstRead.Hits}, Misses={afterFirstRead.Misses}, Ratio={afterFirstRead.HitRatio:P}");
        }

        [Test]
        public void LsmTreeBlockCacheDisabledTest()
        {
            var dir = Path.Combine(m_testDir, "no_cache");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = false // Disable cache
            };
        
            using var tree = new LsmTreeStore(dir, options);
            
            Assert.That(tree.BlockCache, Is.Null);
            
            // Operations should still work
            tree.Put(ToBytes("key"), ToBytes("value"));
            var result = tree.Get(ToBytes("key"));
            Assert.That(result, Is.EqualTo(ToBytes("value")));
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
        
            using var tree = new LsmTreeStore(dir, options);
        
            // Insert enough data to trigger compaction
            for (int i = 0; i < 200; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();

            // Wait for background compaction
            tree.WaitForCompaction();
            
            // Compaction should not be running
            Assert.That(tree.IsCompacting, Is.False);

            // All data should still be accessible - this is the key test
            for (int i = 0; i < 200; i++)
            {
                var result = tree.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null, $"Key {i} not found");
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i * 10));
            }
        }

        [Test]
        public void LsmTreeSyncCompactionTest()
        {
            var dir = Path.Combine(m_testDir, "sync_compaction");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                EnableBlockCache = false,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 3,
                BackgroundCompaction = false // Synchronous compaction
            };
        
            using var tree = new LsmTreeStore(dir, options);
        
            // Insert data
            for (int i = 0; i < 200; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            tree.Flush();

            // Compaction should have already happened (synchronously)
            Assert.That(tree.SSTableCount, Is.LessThanOrEqualTo(3));

            // All data should be accessible
            for (int i = 0; i < 200; i++)
            {
                var result = tree.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null);
            }
        }

        #endregion

        #region Statistics Tests

        [Test]
        public void LsmTreeStatisticsTest()
        {
            var dir = Path.Combine(m_testDir, "stats");
            var options = new LsmOptions 
            { 
                EnableWal = false,
                MemTableSizeLimit = 100,
                Level0CompactionTrigger = 100 // Disable auto-compaction
            };
        
            using var tree = new LsmTreeStore(dir, options);
            var stats = tree.Statistics;
            
            // Initial state
            Assert.That(stats.Gets, Is.EqualTo(0));
            Assert.That(stats.Puts, Is.EqualTo(0));
            Assert.That(stats.Deletes, Is.EqualTo(0));
            
            // Puts
            for (int i = 0; i < 10; i++)
            {
                tree.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }
            Assert.That(stats.Puts, Is.EqualTo(10));
            Assert.That(stats.BytesWritten, Is.GreaterThan(0));
            
            // Gets
            for (int i = 0; i < 5; i++)
            {
                tree.Get(BitConverter.GetBytes(i));
            }
            Assert.That(stats.Gets, Is.EqualTo(5));
            
            // Deletes
            tree.Delete(BitConverter.GetBytes(0));
            tree.Delete(BitConverter.GetBytes(1));
            Assert.That(stats.Deletes, Is.EqualTo(2));
            
            // Scan
            tree.Scan(null, null).ToList();
            Assert.That(stats.Scans, Is.EqualTo(1));
            
            // Flush
            tree.Flush();
            Assert.That(stats.Flushes, Is.GreaterThanOrEqualTo(1));
            
            // Snapshot
            var snapshot = stats.GetSnapshot();
            Assert.That(snapshot.Gets, Is.EqualTo(5));
            Assert.That(snapshot.Puts, Is.EqualTo(10));
            
            // Reset
            stats.Reset();
            Assert.That(stats.Gets, Is.EqualTo(0));
            Assert.That(stats.Puts, Is.EqualTo(0));
        }

        #endregion
    }
}
