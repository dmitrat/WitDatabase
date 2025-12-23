using System.Text;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Stores
{
    [TestFixture]
    public class MvccKeyValueStoreTests
    {
        #region Fields

        private StoreInMemory m_innerStore = null!;
        private TransactionTimestampManager m_timestampManager = null!;
        private MvccKeyValueStore m_store = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_innerStore = new StoreInMemory();
            m_timestampManager = new TransactionTimestampManager();
            m_store = new MvccKeyValueStore(m_innerStore, m_timestampManager, ownsStore: false);
        }

        [TearDown]
        public void TearDown()
        {
            m_store.Dispose();
            m_innerStore.Dispose();
        }

        #endregion

        #region Helper Methods

        private static byte[] ToBytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);
        private static string FromBytes(byte[] b) => System.Text.Encoding.UTF8.GetString(b);

        #endregion

        #region Basic Operations Tests

        [Test]
        public void PutAndGetReturnsValueTest()
        {
            var key = ToBytes("key1");
            var value = ToBytes("value1");

            m_store.Put(key, value);
            var result = m_store.Get(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(FromBytes(result!), Is.EqualTo("value1"));
        }

        [Test]
        public void GetNonExistentKeyReturnsNullTest()
        {
            var result = m_store.Get(ToBytes("nonexistent"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void DeleteRemovesKeyTest()
        {
            var key = ToBytes("key1");
            m_store.Put(key, ToBytes("value1"));

            var deleted = m_store.Delete(key);
            var result = m_store.Get(key);

            Assert.That(deleted, Is.True);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void DeleteNonExistentKeyReturnsFalseTest()
        {
            var deleted = m_store.Delete(ToBytes("nonexistent"));
            Assert.That(deleted, Is.False);
        }

        [Test]
        public void UpdateValueReturnsNewValueTest()
        {
            var key = ToBytes("key1");
            m_store.Put(key, ToBytes("value1"));
            m_store.Put(key, ToBytes("value2"));

            var result = m_store.Get(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(FromBytes(result!), Is.EqualTo("value2"));
        }

        #endregion

        #region Version Tests

        [Test]
        public void PutCreatesNewVersionTest()
        {
            var key = ToBytes("key1");

            m_store.Put(key, ToBytes("v1"));
            m_store.Put(key, ToBytes("v2"));
            m_store.Put(key, ToBytes("v3"));

            var versionCount = m_store.GetVersionCount(key);
            Assert.That(versionCount, Is.EqualTo(3));
        }

        [Test]
        public void GetAllVersionsReturnsAllVersionsTest()
        {
            var key = ToBytes("key1");

            m_store.Put(key, ToBytes("v1"));
            m_store.Put(key, ToBytes("v2"));
            m_store.Put(key, ToBytes("v3"));

            var versions = m_store.GetAllVersions(key);

            Assert.That(versions.Count, Is.EqualTo(3));
            // Should be in newest-first order
            Assert.That(FromBytes(versions[0].Value), Is.EqualTo("v3"));
            Assert.That(FromBytes(versions[1].Value), Is.EqualTo("v2"));
            Assert.That(FromBytes(versions[2].Value), Is.EqualTo("v1"));
        }

        [Test]
        public void DeleteMarksVersionAsDeletedTest()
        {
            var key = ToBytes("key1");
            m_store.Put(key, ToBytes("value1"));
            m_store.Delete(key);

            var versions = m_store.GetAllVersions(key);

            Assert.That(versions.Count, Is.EqualTo(1));
            Assert.That(versions[0].IsDeleted, Is.True);
        }

        #endregion

        #region Snapshot Isolation Tests

        [Test]
        public void GetAsOfReturnsValueAtSnapshotTest()
        {
            var key = ToBytes("key1");

            // Create version at timestamp 1
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 1);
            // Create version at timestamp 5
            m_store.PutVersion(key, ToBytes("v2"), timestamp: 5);
            // Create version at timestamp 10
            m_store.PutVersion(key, ToBytes("v3"), timestamp: 10);

            // Read at different snapshots
            var atTs3 = m_store.GetAsOf(key, snapshotTimestamp: 3);
            var atTs7 = m_store.GetAsOf(key, snapshotTimestamp: 7);
            var atTs15 = m_store.GetAsOf(key, snapshotTimestamp: 15);

            Assert.That(atTs3, Is.Not.Null);
            Assert.That(FromBytes(atTs3!), Is.EqualTo("v1"));

            Assert.That(atTs7, Is.Not.Null);
            Assert.That(FromBytes(atTs7!), Is.EqualTo("v2"));

            Assert.That(atTs15, Is.Not.Null);
            Assert.That(FromBytes(atTs15!), Is.EqualTo("v3"));
        }

        [Test]
        public void GetAsOfReturnsNullBeforeCreationTest()
        {
            var key = ToBytes("key1");
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 10);

            var result = m_store.GetAsOf(key, snapshotTimestamp: 5);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetAsOfReturnsNullAfterDeleteTest()
        {
            var key = ToBytes("key1");
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 1);
            m_store.DeleteVersion(key, timestamp: 10);

            var atTs5 = m_store.GetAsOf(key, snapshotTimestamp: 5);
            var atTs15 = m_store.GetAsOf(key, snapshotTimestamp: 15);

            Assert.That(atTs5, Is.Not.Null);
            Assert.That(FromBytes(atTs5!), Is.EqualTo("v1"));

            Assert.That(atTs15, Is.Null);
        }

        #endregion

        #region Transaction Tests

        [Test]
        public void UncommittedVersionNotVisibleToOthersTest()
        {
            var key = ToBytes("key1");
            
            // Transaction 1 creates a version
            m_timestampManager.RegisterTransaction(1, snapshotTimestamp: 100);
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 101, transactionId: 1);

            // Non-transactional read should not see it
            var result = m_store.GetAsOf(key, snapshotTimestamp: 150, transactionId: 0);
            Assert.That(result, Is.Null);

            // Transaction 2 should not see it
            m_timestampManager.RegisterTransaction(2, snapshotTimestamp: 100);
            var result2 = m_store.GetAsOf(key, snapshotTimestamp: 150, transactionId: 2);
            Assert.That(result2, Is.Null);
        }

        [Test]
        public void UncommittedVersionVisibleToOwnTransactionTest()
        {
            var key = ToBytes("key1");
            
            m_timestampManager.RegisterTransaction(1, snapshotTimestamp: 100);
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 101, transactionId: 1);

            // Same transaction should see it
            var result = m_store.GetAsOf(key, snapshotTimestamp: 50, transactionId: 1);

            Assert.That(result, Is.Not.Null);
            Assert.That(FromBytes(result!), Is.EqualTo("v1"));
        }

        [Test]
        public void CommitTransactionMakesVersionVisibleTest()
        {
            var key = ToBytes("key1");
            
            m_timestampManager.RegisterTransaction(1, snapshotTimestamp: 100);
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 101, transactionId: 1);

            // Not visible before commit
            var beforeCommit = m_store.GetAsOf(key, snapshotTimestamp: 150, transactionId: 0);
            Assert.That(beforeCommit, Is.Null);

            // Commit
            m_store.CommitTransaction(1, commitTimestamp: 110);

            // Visible after commit
            var afterCommit = m_store.GetAsOf(key, snapshotTimestamp: 150, transactionId: 0);
            Assert.That(afterCommit, Is.Not.Null);
            Assert.That(FromBytes(afterCommit!), Is.EqualTo("v1"));
        }

        [Test]
        public void RollbackTransactionRemovesVersionTest()
        {
            var key = ToBytes("key1");
            
            m_timestampManager.RegisterTransaction(1, snapshotTimestamp: 100);
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 101, transactionId: 1);

            // Version exists
            Assert.That(m_store.GetVersionCount(key), Is.EqualTo(1));

            // Rollback
            m_store.RollbackTransaction(1);

            // Version removed
            Assert.That(m_store.GetVersionCount(key), Is.EqualTo(0));
        }

        [Test]
        public void CommittedAfterSnapshotNotVisibleTest()
        {
            var key = ToBytes("key1");
            
            // Transaction starts with snapshot at 100
            m_timestampManager.RegisterTransaction(1, snapshotTimestamp: 100);
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 101, transactionId: 1);
            m_store.CommitTransaction(1, commitTimestamp: 110);

            // Transaction 2 with snapshot at 105 should NOT see the commit at 110
            m_timestampManager.RegisterTransaction(2, snapshotTimestamp: 105);
            var result = m_store.GetAsOf(key, snapshotTimestamp: 105, transactionId: 2);

            Assert.That(result, Is.Null);
        }

        #endregion

        #region Scan Tests

        [Test]
        public void ScanReturnsLatestVersionsTest()
        {
            m_store.Put(ToBytes("a"), ToBytes("a1"));
            m_store.Put(ToBytes("a"), ToBytes("a2"));
            m_store.Put(ToBytes("b"), ToBytes("b1"));
            m_store.Put(ToBytes("c"), ToBytes("c1"));

            var results = m_store.Scan(null, null).ToList();

            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results.Any(r => FromBytes(r.Key) == "a" && FromBytes(r.Value) == "a2"), Is.True);
            Assert.That(results.Any(r => FromBytes(r.Key) == "b" && FromBytes(r.Value) == "b1"), Is.True);
            Assert.That(results.Any(r => FromBytes(r.Key) == "c" && FromBytes(r.Value) == "c1"), Is.True);
        }

        [Test]
        public void ScanAsOfReturnsSnapshotVersionsTest()
        {
            m_store.PutVersion(ToBytes("a"), ToBytes("a1"), timestamp: 1);
            m_store.PutVersion(ToBytes("a"), ToBytes("a2"), timestamp: 10);
            m_store.PutVersion(ToBytes("b"), ToBytes("b1"), timestamp: 5);

            var atTs3 = m_store.ScanAsOf(null, null, snapshotTimestamp: 3, transactionId: 0).ToList();
            var atTs15 = m_store.ScanAsOf(null, null, snapshotTimestamp: 15, transactionId: 0).ToList();

            Assert.That(atTs3.Count, Is.EqualTo(1));
            Assert.That(FromBytes(atTs3[0].Value), Is.EqualTo("a1"));

            Assert.That(atTs15.Count, Is.EqualTo(2));
        }

        [Test]
        public void ScanExcludesDeletedKeysTest()
        {
            m_store.Put(ToBytes("a"), ToBytes("a1"));
            m_store.Put(ToBytes("b"), ToBytes("b1"));
            m_store.Delete(ToBytes("a"));

            var results = m_store.Scan(null, null).ToList();

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(FromBytes(results[0].Key), Is.EqualTo("b"));
        }

        [Test]
        public void ScanWithRangeTest()
        {
            m_store.Put(ToBytes("a"), ToBytes("a1"));
            m_store.Put(ToBytes("b"), ToBytes("b1"));
            m_store.Put(ToBytes("c"), ToBytes("c1"));
            m_store.Put(ToBytes("d"), ToBytes("d1"));

            var results = m_store.Scan(ToBytes("b"), ToBytes("d")).ToList();

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.Any(r => FromBytes(r.Key) == "b"), Is.True);
            Assert.That(results.Any(r => FromBytes(r.Key) == "c"), Is.True);
        }

        #endregion

        #region Garbage Collection Tests

        [Test]
        public void GarbageCollectRemovesOldVersionsTest()
        {
            var key = ToBytes("key1");

            // Use Put (which auto-advances timestamps) to create multiple versions
            m_store.Put(key, ToBytes("v1"));
            m_store.Put(key, ToBytes("v2"));
            m_store.Put(key, ToBytes("v3"));

            Assert.That(m_store.GetVersionCount(key), Is.EqualTo(3));

            // Get current timestamp for GC
            var currentTs = m_timestampManager.CurrentTimestamp;

            // GC with min snapshot at current + 1 should remove old versions
            var removed = m_store.GarbageCollect(minActiveSnapshotTimestamp: currentTs + 1);

            Assert.That(removed, Is.GreaterThan(0));
            Assert.That(m_store.GetVersionCount(key), Is.LessThan(3));
            
            // Latest version should still be accessible
            var result = m_store.Get(key);
            Assert.That(result, Is.Not.Null);
            Assert.That(FromBytes(result!), Is.EqualTo("v3"));
        }

        [Test]
        public void GarbageCollectKeepsVisibleVersionTest()
        {
            var key = ToBytes("key1");

            // Use m_store.Put which auto-assigns timestamps
            m_store.Put(key, ToBytes("v1"));
            m_store.Put(key, ToBytes("v2"));
            
            var versionsBefore = m_store.GetVersionCount(key);
            Assert.That(versionsBefore, Is.EqualTo(2));

            // Get current timestamp for GC
            var currentTs = m_timestampManager.CurrentTimestamp;

            // GC with min snapshot at current timestamp + 1
            m_store.GarbageCollect(minActiveSnapshotTimestamp: currentTs + 1);

            // Latest version should still be visible
            var result = m_store.Get(key);
            Assert.That(result, Is.Not.Null);
            Assert.That(FromBytes(result!), Is.EqualTo("v2"));
        }

        [Test]
        public void GarbageCollectDoesNotRemoveUncommittedTest()
        {
            var key = ToBytes("key1");

            m_timestampManager.RegisterTransaction(1, snapshotTimestamp: 100);
            m_store.PutVersion(key, ToBytes("v1"), timestamp: 1, transactionId: 1);

            var removed = m_store.GarbageCollect(minActiveSnapshotTimestamp: 1000);

            Assert.That(removed, Is.EqualTo(0));
            Assert.That(m_store.GetVersionCount(key), Is.EqualTo(1));
        }

        #endregion

        #region ProviderKey Tests

        [Test]
        public void ProviderKeyIncludesInnerStoreTest()
        {
            Assert.That(m_store.ProviderKey, Does.StartWith("mvcc:"));
            Assert.That(m_store.ProviderKey, Does.Contain(m_innerStore.ProviderKey));
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void DisposeOwnsStoreDisposesInnerTest()
        {
            var inner = new StoreInMemory();
            var store = new MvccKeyValueStore(inner, m_timestampManager, ownsStore: true);

            store.Dispose();

            Assert.Throws<ObjectDisposedException>(() => inner.Get(ToBytes("test")));
        }

        [Test]
        public void DisposeNotOwnsStoreKeepsInnerTest()
        {
            var inner = new StoreInMemory();
            var store = new MvccKeyValueStore(inner, m_timestampManager, ownsStore: false);

            store.Dispose();

            // Inner store should still work
            Assert.DoesNotThrow(() => inner.Get(ToBytes("test")));

            inner.Dispose();
        }

        #endregion
    }
}
