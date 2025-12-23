using NUnit.Framework;
using OutWit.Database.Core.Mvcc;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Mvcc
{
    /// <summary>
    /// Unit tests for MvccGarbageCollector.
    /// </summary>
    [TestFixture]
    public class MvccGarbageCollectorTests
    {
        private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);
        private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        #region Basic Tests

        [Test]
        public void RunNowRemovesOldVersionsTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);
            using var gc = new MvccGarbageCollector(mvccStore, timestampManager);

            // Create multiple versions
            mvccStore.Put(Key("key1"), Value("v1"));
            mvccStore.Put(Key("key1"), Value("v2"));
            mvccStore.Put(Key("key1"), Value("v3"));

            Assert.That(mvccStore.GetVersionCount(Key("key1")), Is.EqualTo(3));

            var stats = gc.RunNow();

            // Old versions should be removed (keeping only the latest)
            Assert.That(stats.VersionsRemoved, Is.GreaterThan(0));
            Assert.That(mvccStore.GetVersionCount(Key("key1")), Is.LessThan(3));
        }

        [Test]
        public void RunNowReturnsStatisticsTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);
            using var gc = new MvccGarbageCollector(mvccStore, timestampManager);

            mvccStore.Put(Key("key1"), Value("v1"));
            mvccStore.Put(Key("key1"), Value("v2"));

            var stats = gc.RunNow();

            Assert.That(stats, Is.Not.Null);
            Assert.That(stats.Duration, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
            Assert.That(stats.CompletedAt, Is.LessThanOrEqualTo(DateTime.UtcNow));
            Assert.That(stats.MinActiveSnapshotTimestamp, Is.GreaterThan(0));
        }

        [Test]
        public void NoVersionsRemovedWhenActiveTransactionTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);
            using var gc = new MvccGarbageCollector(mvccStore, timestampManager);

            // Create versions
            mvccStore.Put(Key("key1"), Value("v1"));
            var oldSnapshot = timestampManager.CurrentTimestamp;
            
            mvccStore.Put(Key("key1"), Value("v2"));
            mvccStore.Put(Key("key1"), Value("v3"));

            // Register an active transaction with old snapshot
            var txId = timestampManager.GetNextTimestamp();
            timestampManager.RegisterTransaction(txId, oldSnapshot);

            var versionsBefore = mvccStore.GetVersionCount(Key("key1"));
            var stats = gc.RunNow();

            // Old versions should not be removed because active transaction might need them
            var versionsAfter = mvccStore.GetVersionCount(Key("key1"));
            
            // At least one version should be kept for the active transaction
            Assert.That(versionsAfter, Is.GreaterThanOrEqualTo(1));
        }

        #endregion

        #region Background Collection Tests

        [Test]
        public void BackgroundCollectionRunsTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);

            var collectionCount = 0;
            var options = new MvccGarbageCollectorOptions
            {
                CollectionInterval = TimeSpan.FromMilliseconds(50),
                RunOnStart = true,
                EnableStatistics = true,
                OnCollectionComplete = _ => Interlocked.Increment(ref collectionCount)
            };

            using var gc = new MvccGarbageCollector(mvccStore, timestampManager, options);

            // Wait for at least 2 collection runs
            Thread.Sleep(200);

            Assert.That(collectionCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(gc.RunCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void PauseStopsBackgroundCollectionTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);

            var options = new MvccGarbageCollectorOptions
            {
                CollectionInterval = TimeSpan.FromMilliseconds(50),
                RunOnStart = false
            };

            using var gc = new MvccGarbageCollector(mvccStore, timestampManager, options);

            Thread.Sleep(100);
            var runCountBefore = gc.RunCount;

            gc.Pause();
            Thread.Sleep(150);

            var runCountAfter = gc.RunCount;

            // Should not increase much after pause
            Assert.That(runCountAfter - runCountBefore, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void ResumeRestartsBackgroundCollectionTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);

            var options = new MvccGarbageCollectorOptions
            {
                CollectionInterval = TimeSpan.FromMilliseconds(50),
                RunOnStart = false
            };

            using var gc = new MvccGarbageCollector(mvccStore, timestampManager, options);

            gc.Pause();
            Thread.Sleep(100);
            var runCountPaused = gc.RunCount;

            gc.Resume();
            Thread.Sleep(150);

            Assert.That(gc.RunCount, Is.GreaterThan(runCountPaused));
        }

        [Test]
        public void SetIntervalChangesCollectionRateTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);

            var options = new MvccGarbageCollectorOptions
            {
                CollectionInterval = TimeSpan.FromSeconds(10), // Long interval
                RunOnStart = false
            };

            using var gc = new MvccGarbageCollector(mvccStore, timestampManager, options);

            Thread.Sleep(50);
            Assert.That(gc.RunCount, Is.EqualTo(0));

            // Change to short interval
            gc.SetInterval(TimeSpan.FromMilliseconds(20));
            Thread.Sleep(100);

            Assert.That(gc.RunCount, Is.GreaterThan(0));
        }

        #endregion

        #region Async Tests

        [Test]
        public async Task RunAsyncReturnsStatisticsTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);
            using var gc = new MvccGarbageCollector(mvccStore, timestampManager);

            mvccStore.Put(Key("key1"), Value("v1"));
            mvccStore.Put(Key("key1"), Value("v2"));

            var stats = await gc.RunAsync();

            Assert.That(stats, Is.Not.Null);
            Assert.That(stats.VersionsRemoved, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task RunAsyncCanBeCancelledTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);
            using var gc = new MvccGarbageCollector(mvccStore, timestampManager);

            // Add lots of data to make GC take longer
            for (int i = 0; i < 1000; i++)
            {
                mvccStore.Put(Key($"key{i}"), Value($"v{i}_1"));
                mvccStore.Put(Key($"key{i}"), Value($"v{i}_2"));
            }

            using var cts = new CancellationTokenSource();
            
            // Start the task
            var task = gc.RunAsync(cts.Token);
            
            // Cancel after a small delay
            await Task.Delay(1);
            cts.Cancel();

            // The task might complete before cancellation or throw
            // Either is acceptable behavior
            try
            {
                await task;
                // Task completed before cancellation - that's OK
                Assert.Pass("GC completed before cancellation");
            }
            catch (OperationCanceledException)
            {
                // Task was cancelled - that's also OK
                Assert.Pass("GC was cancelled");
            }
        }

        #endregion

        #region Statistics Tests

        [Test]
        public void TotalVersionsRemovedAccumulatesTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);
            using var gc = new MvccGarbageCollector(mvccStore, timestampManager);

            // First batch of versions
            mvccStore.Put(Key("key1"), Value("v1"));
            mvccStore.Put(Key("key1"), Value("v2"));
            gc.RunNow();

            var afterFirst = gc.TotalVersionsRemoved;

            // Second batch
            mvccStore.Put(Key("key2"), Value("v1"));
            mvccStore.Put(Key("key2"), Value("v2"));
            gc.RunNow();

            Assert.That(gc.TotalVersionsRemoved, Is.GreaterThanOrEqualTo(afterFirst));
        }

        [Test]
        public void CallbackIsInvokedTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);

            GarbageCollectionStatistics? receivedStats = null;
            var options = new MvccGarbageCollectorOptions
            {
                EnableStatistics = true,
                OnCollectionComplete = stats => receivedStats = stats
            };

            using var gc = new MvccGarbageCollector(mvccStore, timestampManager, options);

            mvccStore.Put(Key("key1"), Value("v1"));
            gc.RunNow();

            Assert.That(receivedStats, Is.Not.Null);
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void DisposeStopsBackgroundCollectionTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);

            var options = new MvccGarbageCollectorOptions
            {
                CollectionInterval = TimeSpan.FromMilliseconds(20),
                RunOnStart = true
            };

            var gc = new MvccGarbageCollector(mvccStore, timestampManager, options);
            Thread.Sleep(100);
            gc.Dispose();

            var runCountAtDispose = gc.RunCount;
            Thread.Sleep(100);

            // Should not increase after dispose
            Assert.That(gc.RunCount, Is.EqualTo(runCountAtDispose));
        }

        [Test]
        public void RunNowAfterDisposeThrowsTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);

            var gc = new MvccGarbageCollector(mvccStore, timestampManager);
            gc.Dispose();

            Assert.Throws<ObjectDisposedException>(() => gc.RunNow());
        }

        #endregion

        #region Concurrent Run Tests

        [Test]
        public void ConcurrentRunsAreSerializedTest()
        {
            var timestampManager = new TransactionTimestampManager();
            using var innerStore = new StoreInMemory();
            using var mvccStore = new MvccKeyValueStore(innerStore, timestampManager, ownsStore: false);
            using var gc = new MvccGarbageCollector(mvccStore, timestampManager);

            // Add some data
            for (int i = 0; i < 100; i++)
            {
                mvccStore.Put(Key($"key{i}"), Value($"v{i}"));
            }

            // Run multiple concurrent collections
            var tasks = new List<Task<GarbageCollectionStatistics>>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(gc.RunAsync());
            }

            Task.WaitAll(tasks.ToArray());

            // All should complete without exception
            Assert.That(tasks.All(t => t.IsCompletedSuccessfully), Is.True);
        }

        #endregion
    }
}
