using NUnit.Framework;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Exceptions;

namespace OutWit.Database.Core.Tests.Concurrency
{
    /// <summary>
    /// Unit tests for RowLockManager.
    /// </summary>
    [TestFixture]
    public class RowLockManagerTests
    {
        private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        #region Basic Lock Operations

        [Test]
        public void AcquireExclusiveLockSucceedsTest()
        {
            using var manager = new RowLockManager();
            var request = new RowLockRequest(Key("key1"), transactionId: 1, RowLockMode.Exclusive);

            var handle = manager.AcquireLock(request);

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.TransactionId, Is.EqualTo(1));
            Assert.That(handle.Mode, Is.EqualTo(RowLockMode.Exclusive));
            Assert.That(manager.LockCount, Is.EqualTo(1));
        }

        [Test]
        public void AcquireSharedLockSucceedsTest()
        {
            using var manager = new RowLockManager();
            var request = new RowLockRequest(Key("key1"), transactionId: 1, RowLockMode.Shared);

            var handle = manager.AcquireLock(request);

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.Mode, Is.EqualTo(RowLockMode.Shared));
            Assert.That(manager.LockCount, Is.EqualTo(1));
        }

        [Test]
        public void MultipleSharedLocksAllowedTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            var handle1 = manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Shared));
            var handle2 = manager.AcquireLock(new RowLockRequest(key, 2, RowLockMode.Shared));
            var handle3 = manager.AcquireLock(new RowLockRequest(key, 3, RowLockMode.Shared));

            Assert.That(handle1, Is.Not.Null);
            Assert.That(handle2, Is.Not.Null);
            Assert.That(handle3, Is.Not.Null);
            Assert.That(manager.LockCount, Is.EqualTo(1)); // Same key
            Assert.That(manager.HoldingTransactionCount, Is.EqualTo(3));
        }

        [Test]
        public void SameTransactionCanReacquireLockTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            var handle1 = manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));
            var handle2 = manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            Assert.That(handle1, Is.Not.Null);
            Assert.That(handle2, Is.Not.Null);
        }

        #endregion

        #region Lock Release

        [Test]
        public void ReleaseAllLocksReleasesLockTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));
            Assert.That(manager.IsLocked(key), Is.True);

            manager.ReleaseAllLocks(1);

            Assert.That(manager.IsLocked(key), Is.False);
            Assert.That(manager.LockCount, Is.EqualTo(0));
        }

        [Test]
        public void ReleaseAllLocksReleasesMultipleKeysTest()
        {
            using var manager = new RowLockManager();
            var key1 = Key("key1");
            var key2 = Key("key2");
            var key3 = Key("key3");

            manager.AcquireLock(new RowLockRequest(key1, 1, RowLockMode.Exclusive));
            manager.AcquireLock(new RowLockRequest(key2, 1, RowLockMode.Exclusive));
            manager.AcquireLock(new RowLockRequest(key3, 1, RowLockMode.Exclusive));

            Assert.That(manager.LockCount, Is.EqualTo(3));

            manager.ReleaseAllLocks(1);

            Assert.That(manager.LockCount, Is.EqualTo(0));
        }

        #endregion

        #region NoWait Mode

        [Test]
        public void ExclusiveLockBlocksAnotherExclusiveNoWaitTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, RowLockWaitMode.NoWait);

            var ex = Assert.Throws<RowLockException>(() => manager.AcquireLock(request));
            Assert.That(ex!.HoldingTransactionId, Is.EqualTo(1));
            Assert.That(ex.RequestingTransactionId, Is.EqualTo(2));
        }

        [Test]
        public void ExclusiveLockBlocksSharedNoWaitTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var request = new RowLockRequest(key, 2, RowLockMode.Shared, RowLockWaitMode.NoWait);

            Assert.Throws<RowLockException>(() => manager.AcquireLock(request));
        }

        [Test]
        public void SharedLockBlocksExclusiveNoWaitTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Shared));

            var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, RowLockWaitMode.NoWait);

            Assert.Throws<RowLockException>(() => manager.AcquireLock(request));
        }

        #endregion

        #region SkipLocked Mode

        [Test]
        public void SkipLockedReturnsNullWhenLockedTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, RowLockWaitMode.SkipLocked);
            var handle = manager.AcquireLock(request);

            Assert.That(handle, Is.Null);
        }

        [Test]
        public void SkipLockedSucceedsWhenNotLockedTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            var request = new RowLockRequest(key, 1, RowLockMode.Exclusive, RowLockWaitMode.SkipLocked);
            var handle = manager.AcquireLock(request);

            Assert.That(handle, Is.Not.Null);
        }

        #endregion

        #region Wait Mode

        [Test]
        public void WaitModeGrantsLockAfterReleaseTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");
            var lockGranted = false;

            // Transaction 1 holds exclusive lock
            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            // Transaction 2 waits for lock
            var waitTask = Task.Run(() =>
            {
                var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, 
                    RowLockWaitMode.Wait, TimeSpan.FromSeconds(5));
                var handle = manager.AcquireLock(request);
                lockGranted = handle != null;
            });

            // Give time for wait to start
            Thread.Sleep(100);

            // Release transaction 1's lock
            manager.ReleaseAllLocks(1);

            // Wait for transaction 2 to acquire
            waitTask.Wait(TimeSpan.FromSeconds(2));

            Assert.That(lockGranted, Is.True);
        }

        [Test]
        public void WaitModeTimesOutTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, 
                RowLockWaitMode.Wait, TimeSpan.FromMilliseconds(100));

            Assert.Throws<TimeoutException>(() => manager.AcquireLock(request));
        }

        #endregion

        #region Lock Upgrade

        [Test]
        public void UpgradeFromSharedToExclusiveSucceedsWhenOnlyHolderTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Shared));

            var upgradeRequest = new RowLockRequest(key, 1, RowLockMode.Exclusive, RowLockWaitMode.NoWait);
            var handle = manager.AcquireLock(upgradeRequest);

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.Mode, Is.EqualTo(RowLockMode.Exclusive));
        }

        [Test]
        public void UpgradeFromSharedToExclusiveFailsWithOtherHoldersTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Shared));
            manager.AcquireLock(new RowLockRequest(key, 2, RowLockMode.Shared));

            var upgradeRequest = new RowLockRequest(key, 1, RowLockMode.Exclusive, RowLockWaitMode.NoWait);

            Assert.Throws<RowLockException>(() => manager.AcquireLock(upgradeRequest));
        }

        #endregion

        #region Query Methods

        [Test]
        public void IsLockedReturnsTrueWhenLockedTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            Assert.That(manager.IsLocked(key), Is.False);

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            Assert.That(manager.IsLocked(key), Is.True);
        }

        [Test]
        public void IsLockedByTransactionWorksTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            Assert.That(manager.IsLockedByTransaction(key, 1), Is.True);
            Assert.That(manager.IsLockedByTransaction(key, 2), Is.False);
        }

        [Test]
        public void GetLockedKeysReturnsCorrectKeysTest()
        {
            using var manager = new RowLockManager();
            var key1 = Key("key1");
            var key2 = Key("key2");

            manager.AcquireLock(new RowLockRequest(key1, 1, RowLockMode.Exclusive));
            manager.AcquireLock(new RowLockRequest(key2, 1, RowLockMode.Exclusive));

            var lockedKeys = manager.GetLockedKeys(1);

            Assert.That(lockedKeys.Count, Is.EqualTo(2));
        }

        #endregion

        #region Async Operations

        [Test]
        public async Task AcquireLockAsyncSucceedsTest()
        {
            using var manager = new RowLockManager();
            var request = new RowLockRequest(Key("key1"), 1, RowLockMode.Exclusive);

            var handle = await manager.AcquireLockAsync(request);

            Assert.That(handle, Is.Not.Null);
            Assert.That(manager.LockCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AcquireLockAsyncNoWaitThrowsTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            await manager.AcquireLockAsync(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, RowLockWaitMode.NoWait);

            Assert.ThrowsAsync<RowLockException>(async () => await manager.AcquireLockAsync(request));
        }

        [Test]
        public async Task AcquireLockAsyncSkipLockedReturnsNullTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            await manager.AcquireLockAsync(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, RowLockWaitMode.SkipLocked);
            var handle = await manager.AcquireLockAsync(request);

            Assert.That(handle, Is.Null);
        }

        [Test]
        public async Task AcquireLockAsyncWaitGrantsAfterReleaseTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            await manager.AcquireLockAsync(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var waitTask = Task.Run(async () =>
            {
                var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, 
                    RowLockWaitMode.Wait, TimeSpan.FromSeconds(5));
                return await manager.AcquireLockAsync(request);
            });

            await Task.Delay(100);

            manager.ReleaseAllLocks(1);

            var handle = await waitTask;

            Assert.That(handle, Is.Not.Null);
        }

        [Test]
        public async Task AcquireLockAsyncTimeoutThrowsTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            await manager.AcquireLockAsync(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, 
                RowLockWaitMode.Wait, TimeSpan.FromMilliseconds(100));

            Assert.ThrowsAsync<TimeoutException>(async () => await manager.AcquireLockAsync(request));
        }

        [Test]
        public async Task AcquireLockAsyncCancellationThrowsTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");

            await manager.AcquireLockAsync(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            using var cts = new CancellationTokenSource();
            var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, 
                RowLockWaitMode.Wait, TimeSpan.FromSeconds(10));

            // Start waiting for lock
            var waitTask = manager.AcquireLockAsync(request, cts.Token);
            
            // Wait a bit for the request to be queued
            await Task.Delay(50);
            
            // Cancel
            cts.Cancel();

            // Should throw OperationCanceledException
            try
            {
                await waitTask;
                Assert.Fail("Expected OperationCanceledException");
            }
            catch (OperationCanceledException)
            {
                // Expected
                Assert.Pass();
            }
        }

        #endregion

        #region Concurrent Access

        [Test]
        public void ConcurrentSharedLocksTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");
            const int numReaders = 10;
            var handles = new RowLockHandle?[numReaders];

            Parallel.For(0, numReaders, i =>
            {
                var request = new RowLockRequest(key, i + 1, RowLockMode.Shared);
                handles[i] = manager.AcquireLock(request);
            });

            Assert.That(handles.All(h => h != null), Is.True);
            Assert.That(manager.HoldingTransactionCount, Is.EqualTo(numReaders));
        }

        [Test]
        public void ConcurrentExclusiveLocksAreSerializedTest()
        {
            using var manager = new RowLockManager();
            var key = Key("key1");
            const int numWriters = 5;
            var successCount = 0;
            var order = new List<int>();

            Parallel.For(0, numWriters, i =>
            {
                var request = new RowLockRequest(key, i + 1, RowLockMode.Exclusive, 
                    RowLockWaitMode.Wait, TimeSpan.FromSeconds(10));
                var handle = manager.AcquireLock(request);
                if (handle != null)
                {
                    lock (order) { order.Add(i); }
                    Interlocked.Increment(ref successCount);
                    Thread.Sleep(10); // Simulate work
                    manager.ReleaseAllLocks(i + 1);
                }
            });

            Assert.That(successCount, Is.EqualTo(numWriters));
        }

        #endregion

        #region Dispose

        [Test]
        public void DisposeReleasesAllWaitersTest()
        {
            var manager = new RowLockManager();
            var key = Key("key1");

            manager.AcquireLock(new RowLockRequest(key, 1, RowLockMode.Exclusive));

            var waitTask = Task.Run(() =>
            {
                var request = new RowLockRequest(key, 2, RowLockMode.Exclusive, 
                    RowLockWaitMode.Wait, TimeSpan.FromSeconds(10));
                return manager.AcquireLock(request);
            });

            Thread.Sleep(100);

            manager.Dispose();

            // Waiter should complete (lock not granted)
            var handle = waitTask.Result;
            Assert.That(handle, Is.Null);
        }

        [Test]
        public void OperationsAfterDisposeThrowTest()
        {
            var manager = new RowLockManager();
            manager.Dispose();

            var request = new RowLockRequest(Key("key1"), 1, RowLockMode.Exclusive);

            Assert.Throws<ObjectDisposedException>(() => manager.AcquireLock(request));
            Assert.Throws<ObjectDisposedException>(() => manager.IsLocked(Key("key1")));
        }

        #endregion
    }
}
