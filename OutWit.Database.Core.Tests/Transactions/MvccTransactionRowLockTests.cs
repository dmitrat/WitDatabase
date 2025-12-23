using NUnit.Framework;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Transactions
{
    /// <summary>
    /// Tests for row-level locking in MvccTransaction (FOR UPDATE / FOR SHARE).
    /// </summary>
    [TestFixture]
    public class MvccTransactionRowLockTests
    {
        private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);
        private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        #region GetForUpdate Tests

        [Test]
        public void GetForUpdateAcquiresExclusiveLockTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx = (MvccTransaction)store.BeginTransaction();

            var value = tx.GetForUpdate(Key("key1"));

            Assert.That(value, Is.Not.Null);
            Assert.That(store.RowLockManager.IsLockedByTransaction(Key("key1"), tx.TransactionId), Is.True);
        }

        [Test]
        public void GetForUpdateBlocksOtherExclusiveLockNoWaitTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx1 = (MvccTransaction)store.BeginTransaction();
            tx1.GetForUpdate(Key("key1"));

            using var tx2 = (MvccTransaction)store.BeginTransaction();

            Assert.Throws<RowLockException>(() =>
                tx2.GetForUpdate(Key("key1"), RowLockWaitMode.NoWait));
        }

        [Test]
        public void GetForUpdateSkipLockedReturnsNullTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx1 = (MvccTransaction)store.BeginTransaction();
            tx1.GetForUpdate(Key("key1"));

            using var tx2 = (MvccTransaction)store.BeginTransaction();

            var value = tx2.GetForUpdate(Key("key1"), RowLockWaitMode.SkipLocked);

            Assert.That(value, Is.Null);
        }

        [Test]
        public void GetForUpdateReleasedOnCommitTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx1 = (MvccTransaction)store.BeginTransaction();
            tx1.GetForUpdate(Key("key1"));
            tx1.Commit();

            // Lock should be released
            Assert.That(store.RowLockManager.IsLocked(Key("key1")), Is.False);

            using var tx2 = (MvccTransaction)store.BeginTransaction();
            var value = tx2.GetForUpdate(Key("key1"), RowLockWaitMode.NoWait);
            Assert.That(value, Is.Not.Null);
        }

        [Test]
        public void GetForUpdateReleasedOnRollbackTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx1 = (MvccTransaction)store.BeginTransaction();
            tx1.GetForUpdate(Key("key1"));
            tx1.Rollback();

            // Lock should be released
            Assert.That(store.RowLockManager.IsLocked(Key("key1")), Is.False);
        }

        [Test]
        public void GetForUpdateSameKeyTwiceSucceedsTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx = (MvccTransaction)store.BeginTransaction();

            var value1 = tx.GetForUpdate(Key("key1"));
            var value2 = tx.GetForUpdate(Key("key1")); // Same key, should succeed

            Assert.That(value1, Is.EqualTo(value2));
        }

        #endregion

        #region GetForShare Tests

        [Test]
        public void GetForShareAcquiresSharedLockTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx = (MvccTransaction)store.BeginTransaction();

            var value = tx.GetForShare(Key("key1"));

            Assert.That(value, Is.Not.Null);
            Assert.That(store.RowLockManager.IsLockedByTransaction(Key("key1"), tx.TransactionId), Is.True);
        }

        [Test]
        public void MultipleSharedLocksAllowedTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx1 = (MvccTransaction)store.BeginTransaction();
            using var tx2 = (MvccTransaction)store.BeginTransaction();

            var value1 = tx1.GetForShare(Key("key1"));
            var value2 = tx2.GetForShare(Key("key1"));

            Assert.That(value1, Is.Not.Null);
            Assert.That(value2, Is.Not.Null);
        }

        [Test]
        public void SharedLockBlocksExclusiveLockTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx1 = (MvccTransaction)store.BeginTransaction();
            tx1.GetForShare(Key("key1"));

            using var tx2 = (MvccTransaction)store.BeginTransaction();

            Assert.Throws<RowLockException>(() =>
                tx2.GetForUpdate(Key("key1"), RowLockWaitMode.NoWait));
        }

        [Test]
        public void ExclusiveLockBlocksSharedLockTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            using var tx1 = (MvccTransaction)store.BeginTransaction();
            tx1.GetForUpdate(Key("key1"));

            using var tx2 = (MvccTransaction)store.BeginTransaction();

            Assert.Throws<RowLockException>(() =>
                tx2.GetForShare(Key("key1"), RowLockWaitMode.NoWait));
        }

        #endregion

        #region Async Tests

        [Test]
        public async Task GetForUpdateAsyncAcquiresLockTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            await using var tx = (MvccTransaction)store.BeginTransaction();

            var value = await tx.GetForUpdateAsync(Key("key1"));

            Assert.That(value, Is.Not.Null);
            Assert.That(store.RowLockManager.IsLockedByTransaction(Key("key1"), tx.TransactionId), Is.True);
        }

        [Test]
        public async Task GetForShareAsyncAcquiresLockTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            await using var tx = (MvccTransaction)store.BeginTransaction();

            var value = await tx.GetForShareAsync(Key("key1"));

            Assert.That(value, Is.Not.Null);
            Assert.That(store.RowLockManager.IsLockedByTransaction(Key("key1"), tx.TransactionId), Is.True);
        }

        [Test]
        public async Task GetForUpdateAsyncNoWaitThrowsTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            await using var tx1 = (MvccTransaction)store.BeginTransaction();
            await tx1.GetForUpdateAsync(Key("key1"));

            await using var tx2 = (MvccTransaction)store.BeginTransaction();

            Assert.ThrowsAsync<RowLockException>(async () =>
                await tx2.GetForUpdateAsync(Key("key1"), RowLockWaitMode.NoWait));
        }

        [Test]
        public async Task GetForUpdateAsyncSkipLockedReturnsNullTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            await using var tx1 = (MvccTransaction)store.BeginTransaction();
            await tx1.GetForUpdateAsync(Key("key1"));

            await using var tx2 = (MvccTransaction)store.BeginTransaction();

            var value = await tx2.GetForUpdateAsync(Key("key1"), RowLockWaitMode.SkipLocked);

            Assert.That(value, Is.Null);
        }

        #endregion

        #region Dispose Releases Locks

        [Test]
        public void DisposeReleasesRowLocksTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            var tx = (MvccTransaction)store.BeginTransaction();
            tx.GetForUpdate(Key("key1"));
            
            Assert.That(store.RowLockManager.IsLocked(Key("key1")), Is.True);

            tx.Dispose();

            Assert.That(store.RowLockManager.IsLocked(Key("key1")), Is.False);
        }

        [Test]
        public async Task DisposeAsyncReleasesRowLocksTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));

            var tx = (MvccTransaction)store.BeginTransaction();
            await tx.GetForUpdateAsync(Key("key1"));

            Assert.That(store.RowLockManager.IsLocked(Key("key1")), Is.True);

            await tx.DisposeAsync();

            Assert.That(store.RowLockManager.IsLocked(Key("key1")), Is.False);
        }

        #endregion

        #region Multiple Keys

        [Test]
        public void LockMultipleKeysTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("value1"));
            store.Put(Key("key2"), Value("value2"));
            store.Put(Key("key3"), Value("value3"));

            using var tx = (MvccTransaction)store.BeginTransaction();

            tx.GetForUpdate(Key("key1"));
            tx.GetForShare(Key("key2"));
            tx.GetForUpdate(Key("key3"));

            Assert.That(store.RowLockManager.IsLockedByTransaction(Key("key1"), tx.TransactionId), Is.True);
            Assert.That(store.RowLockManager.IsLockedByTransaction(Key("key2"), tx.TransactionId), Is.True);
            Assert.That(store.RowLockManager.IsLockedByTransaction(Key("key3"), tx.TransactionId), Is.True);

            tx.Commit();

            Assert.That(store.RowLockManager.LockCount, Is.EqualTo(0));
        }

        #endregion

        #region Helper Methods

        private static MvccTransactionalStore CreateStore()
        {
            var innerStore = new StoreInMemory();
            return new MvccTransactionalStore(innerStore);
        }

        #endregion
    }
}
