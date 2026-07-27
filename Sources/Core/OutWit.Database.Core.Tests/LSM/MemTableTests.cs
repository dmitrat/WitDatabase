using OutWit.Database.Core.LSM;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.LSM
{
    /// <summary>
    /// Unit tests for MemTable component.
    /// </summary>
    [TestFixture]
    public class MemTableTests
    {
        private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);

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
            using var cts = new CancellationTokenSource();
            var readsCompleted = 0;

            // The readers used to run until cancelled and the test then asserted that at least one
            // read had happened. That asserts what the scheduler managed, not what the memtable
            // does: on a loaded runner the reader tasks could fail to start before the writer
            // finished and cancellation arrived, and readsCompleted was 0 through no fault of the
            // code. Signalling the first read and waiting for it makes the concurrency real rather
            // than hoped for - the readers are provably running while the writer works.
            using var firstReadDone = new ManualResetEventSlim(false);

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
                        firstReadDone.Set();
                    }
                }))
                .ToArray();

            Assert.That(firstReadDone.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "the readers must be running before the writer is timed against them");

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

        [Test]
        public void MemTableUpdateExistingKeyTest()
        {
            using var memTable = new MemTable();
            var key = ToBytes("key");
            
            memTable.Put(key, ToBytes("value1"));
            var size1 = memTable.ApproximateSize;
            
            memTable.Put(key, ToBytes("value2_longer"));
            var size2 = memTable.ApproximateSize;
            
            Assert.That(memTable.Count, Is.EqualTo(1));
            Assert.That(size2, Is.GreaterThan(size1));
            Assert.That(memTable.TryGet(key, out var result), Is.True);
            Assert.That(result, Is.EqualTo(ToBytes("value2_longer")));
        }

        [Test]
        public void MemTableScanWithRangeTest()
        {
            using var memTable = new MemTable();
            
            memTable.Put(ToBytes("a"), ToBytes("1"));
            memTable.Put(ToBytes("b"), ToBytes("2"));
            memTable.Put(ToBytes("c"), ToBytes("3"));
            memTable.Put(ToBytes("d"), ToBytes("4"));
            memTable.Put(ToBytes("e"), ToBytes("5"));

            var results = memTable.Scan(ToBytes("b"), ToBytes("d")).ToList();
        
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Key, Is.EqualTo(ToBytes("b")));
            Assert.That(results[1].Key, Is.EqualTo(ToBytes("c")));
        }

        [Test]
        public void MemTableClearTest()
        {
            using var memTable = new MemTable();
            
            memTable.Put(ToBytes("a"), ToBytes("1"));
            memTable.Put(ToBytes("b"), ToBytes("2"));
            
            Assert.That(memTable.Count, Is.EqualTo(2));
            
            memTable.Clear();
            
            Assert.That(memTable.Count, Is.EqualTo(0));
            Assert.That(memTable.ApproximateSize, Is.EqualTo(0));
        }

        [Test]
        public void MemTableGetAllEntriesTest()
        {
            using var memTable = new MemTable();
            
            memTable.Put(ToBytes("c"), ToBytes("3"));
            memTable.Put(ToBytes("a"), ToBytes("1"));
            memTable.Delete(ToBytes("b")); // Delete creates tombstone
            
            var entries = memTable.GetAllEntries().ToList();
            
            Assert.That(entries.Count, Is.EqualTo(3));
            Assert.That(entries[0].Key, Is.EqualTo(ToBytes("a")));
            Assert.That(entries[1].Key, Is.EqualTo(ToBytes("b")));
            Assert.That(entries[1].Value, Is.Null); // Tombstone from Delete
            Assert.That(entries[2].Key, Is.EqualTo(ToBytes("c")));
        }
    }
}
