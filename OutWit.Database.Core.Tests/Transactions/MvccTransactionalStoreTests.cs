using System.Text;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Transactions
{
    [TestFixture]
    public class MvccTransactionalStoreTests
    {
        #region Fields

        private StoreInMemory m_innerStore = null!;
        private MvccTransactionalStore m_store = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_innerStore = new StoreInMemory();
            m_store = new MvccTransactionalStore(m_innerStore, ownsStore: false);
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

        #region Basic Transaction Tests

        [Test]
        public void BeginTransactionReturnsActiveTransactionTest()
        {
            using var tx = m_store.BeginTransaction();

            Assert.That(tx, Is.Not.Null);
            Assert.That(tx.State, Is.EqualTo(TransactionState.Active));
        }

        [Test]
        public void TransactionHasSnapshotIsolationByDefaultTest()
        {
            using var tx = m_store.BeginTransaction();

            Assert.That(tx.IsolationLevel, Is.EqualTo(IsolationLevel.Snapshot));
        }

        [Test]
        public void TransactionPutAndGetWorksTest()
        {
            using var tx = m_store.BeginTransaction();

            tx.Put(ToBytes("key1"), ToBytes("value1"));
            var result = tx.Get(ToBytes("key1"));

            Assert.That(result, Is.Not.Null);
            Assert.That(FromBytes(result!), Is.EqualTo("value1"));
        }

        [Test]
        public void TransactionCommitMakesDataVisibleTest()
        {
            using (var tx = m_store.BeginTransaction())
            {
                tx.Put(ToBytes("key1"), ToBytes("value1"));
                tx.Commit();
            }

            var result = m_store.Get(ToBytes("key1"));
            Assert.That(result, Is.Not.Null);
            Assert.That(FromBytes(result!), Is.EqualTo("value1"));
        }

        [Test]
        public void TransactionRollbackDiscardsDataTest()
        {
            using (var tx = m_store.BeginTransaction())
            {
                tx.Put(ToBytes("key1"), ToBytes("value1"));
                tx.Rollback();
            }

            var result = m_store.Get(ToBytes("key1"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TransactionDisposeRollsBackIfActiveTest()
        {
            using (var tx = m_store.BeginTransaction())
            {
                tx.Put(ToBytes("key1"), ToBytes("value1"));
                // No commit - dispose should rollback
            }

            var result = m_store.Get(ToBytes("key1"));
            Assert.That(result, Is.Null);
        }

        #endregion

        #region Snapshot Isolation Tests

        [Test]
        public void SnapshotIsolationSeesConsistentDataTest()
        {
            // Setup initial data
            m_store.Put(ToBytes("key1"), ToBytes("v1"));

            // Start transaction 1 with snapshot
            using var tx1 = m_store.BeginTransaction();
            var initialValue = tx1.Get(ToBytes("key1"));
            Assert.That(FromBytes(initialValue!), Is.EqualTo("v1"));

            // Commit new value outside transaction 1
            m_store.Put(ToBytes("key1"), ToBytes("v2"));

            // Transaction 1 should still see old value (snapshot isolation)
            var snapshotValue = tx1.Get(ToBytes("key1"));
            Assert.That(FromBytes(snapshotValue!), Is.EqualTo("v1"));
        }

        [Test]
        public void ConcurrentTransactionsSeeOwnWritesTest()
        {
            using var tx1 = m_store.BeginTransaction();
            using var tx2 = m_store.BeginTransaction();

            tx1.Put(ToBytes("key1"), ToBytes("tx1-value"));
            tx2.Put(ToBytes("key1"), ToBytes("tx2-value"));

            Assert.That(FromBytes(tx1.Get(ToBytes("key1"))!), Is.EqualTo("tx1-value"));
            Assert.That(FromBytes(tx2.Get(ToBytes("key1"))!), Is.EqualTo("tx2-value"));
        }

        [Test]
        public void UncommittedChangesNotVisibleToOtherTransactionsTest()
        {
            using var tx1 = m_store.BeginTransaction();
            tx1.Put(ToBytes("key1"), ToBytes("value1"));

            // Start tx2 after tx1's write (but before commit)
            using var tx2 = m_store.BeginTransaction();

            // tx2 should not see tx1's uncommitted changes
            var result = tx2.Get(ToBytes("key1"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void CommittedChangesVisibleToNewTransactionsTest()
        {
            using (var tx1 = m_store.BeginTransaction())
            {
                tx1.Put(ToBytes("key1"), ToBytes("value1"));
                tx1.Commit();
            }

            using var tx2 = m_store.BeginTransaction();
            var result = tx2.Get(ToBytes("key1"));

            Assert.That(result, Is.Not.Null);
            Assert.That(FromBytes(result!), Is.EqualTo("value1"));
        }

        #endregion

        #region Write-Write Conflict Tests

        [Test]
        public void WriteWriteConflictDetectedTest()
        {
            m_store.Put(ToBytes("key1"), ToBytes("initial"));

            using var tx1 = m_store.BeginTransaction();
            using var tx2 = m_store.BeginTransaction();

            tx1.Put(ToBytes("key1"), ToBytes("tx1-value"));
            tx2.Put(ToBytes("key1"), ToBytes("tx2-value"));

            // First committer wins
            tx1.Commit();

            // Second transaction should detect conflict
            Assert.Throws<InvalidOperationException>(() => tx2.Commit());
        }

        [Test]
        public void NoConflictWhenWritingDifferentKeysTest()
        {
            using var tx1 = m_store.BeginTransaction();
            using var tx2 = m_store.BeginTransaction();

            tx1.Put(ToBytes("key1"), ToBytes("value1"));
            tx2.Put(ToBytes("key2"), ToBytes("value2"));

            Assert.DoesNotThrow(() => tx1.Commit());
            Assert.DoesNotThrow(() => tx2.Commit());

            Assert.That(FromBytes(m_store.Get(ToBytes("key1"))!), Is.EqualTo("value1"));
            Assert.That(FromBytes(m_store.Get(ToBytes("key2"))!), Is.EqualTo("value2"));
        }

        [Test]
        public void HasWriteConflictReturnsTrueWhenConflictExistsTest()
        {
            m_store.Put(ToBytes("key1"), ToBytes("initial"));

            using var tx1 = m_store.BeginTransaction();
            using var tx2 = m_store.BeginTransaction();

            tx1.Put(ToBytes("key1"), ToBytes("tx1-value"));
            tx1.Commit();

            tx2.Put(ToBytes("key1"), ToBytes("tx2-value"));

            var mvccTx2 = tx2 as IMvccTransaction;
            Assert.That(mvccTx2, Is.Not.Null);
            Assert.That(mvccTx2!.HasWriteConflict(), Is.True);
        }

        #endregion

        #region Read-Only Transaction Tests

        [Test]
        public void ReadOnlyTransactionCanReadTest()
        {
            m_store.Put(ToBytes("key1"), ToBytes("value1"));

            var tx = m_store.BeginReadOnlyTransaction();

            var result = tx.Get(ToBytes("key1"));
            Assert.That(FromBytes(result!), Is.EqualTo("value1"));

            tx.Dispose();
        }

        [Test]
        public void ReadOnlyTransactionCannotWriteTest()
        {
            var tx = m_store.BeginReadOnlyTransaction();

            Assert.Throws<InvalidOperationException>(() =>
                tx.Put(ToBytes("key1"), ToBytes("value1")));

            tx.Dispose();
        }

        [Test]
        public void SetReadOnlyAfterWriteThrowsTest()
        {
            using var tx = m_store.BeginTransaction();
            tx.Put(ToBytes("key1"), ToBytes("value1"));

            var mvccTx = tx as IMvccTransaction;
            Assert.That(mvccTx, Is.Not.Null);

            Assert.Throws<InvalidOperationException>(() => mvccTx!.SetReadOnly());
        }

        #endregion

        #region Concurrent Read Tests

        [Test]
        public void MultipleConcurrentReadsWorkTest()
        {
            m_store.Put(ToBytes("key1"), ToBytes("value1"));
            m_store.Put(ToBytes("key2"), ToBytes("value2"));

            var transactions = new List<ITransaction>();
            var results = new List<(string Key, string Value)>();

            // Start multiple read transactions
            for (int i = 0; i < 5; i++)
            {
                transactions.Add(m_store.BeginReadOnlyTransaction());
            }

            // All should see the same data
            foreach (var tx in transactions)
            {
                var v1 = FromBytes(tx.Get(ToBytes("key1"))!);
                var v2 = FromBytes(tx.Get(ToBytes("key2"))!);
                results.Add(("key1", v1));
                results.Add(("key2", v2));
            }

            foreach (var tx in transactions)
            {
                tx.Dispose();
            }

            Assert.That(results.All(r => r.Key == "key1" && r.Value == "value1" ||
                                         r.Key == "key2" && r.Value == "value2"), Is.True);
        }

        [Test]
        public void ReadDuringWriteTransactionWorksTest()
        {
            m_store.Put(ToBytes("key1"), ToBytes("initial"));

            // Start write transaction
            using var writeTx = m_store.BeginTransaction();
            writeTx.Put(ToBytes("key1"), ToBytes("new-value"));

            // Start read transaction during write
            var readTx = m_store.BeginReadOnlyTransaction();

            // Read should see old value (snapshot isolation)
            var result = readTx.Get(ToBytes("key1"));
            Assert.That(FromBytes(result!), Is.EqualTo("initial"));

            // Commit write
            writeTx.Commit();

            // Read transaction still sees old value (its snapshot)
            var resultAfterCommit = readTx.Get(ToBytes("key1"));
            Assert.That(FromBytes(resultAfterCommit!), Is.EqualTo("initial"));

            readTx.Dispose();
        }

        #endregion

        #region Savepoint Tests

        [Test]
        public void SavepointWorksInMvccTransactionTest()
        {
            using var tx = m_store.BeginTransaction();

            tx.Put(ToBytes("key1"), ToBytes("v1"));
            
            if (tx is ITransactionWithSavepoints txSp)
            {
                txSp.CreateSavepoint("sp1");
                tx.Put(ToBytes("key1"), ToBytes("v2"));
                
                Assert.That(FromBytes(tx.Get(ToBytes("key1"))!), Is.EqualTo("v2"));
                
                txSp.RollbackToSavepoint("sp1");
                
                Assert.That(FromBytes(tx.Get(ToBytes("key1"))!), Is.EqualTo("v1"));
            }
            else
            {
                Assert.Fail("Transaction should support savepoints");
            }
        }

        #endregion

        #region Garbage Collection Tests

        [Test]
        public void GarbageCollectionRemovesOldVersionsTest()
        {
            var key = ToBytes("key1");

            // Create multiple versions
            for (int i = 0; i < 5; i++)
            {
                m_store.Put(key, ToBytes($"v{i}"));
            }

            var versionsBefore = m_store.GetVersionCount(key);
            Assert.That(versionsBefore, Is.EqualTo(5));

            // Run GC
            var removed = m_store.RunGarbageCollection();

            var versionsAfter = m_store.GetVersionCount(key);
            Assert.That(versionsAfter, Is.LessThan(versionsBefore));
        }

        #endregion

        #region Active Transaction Count Tests

        [Test]
        public void ActiveTransactionCountTrackedCorrectlyTest()
        {
            Assert.That(m_store.ActiveTransactionCount, Is.EqualTo(0));

            var tx1 = m_store.BeginTransaction();
            Assert.That(m_store.ActiveTransactionCount, Is.EqualTo(1));

            var tx2 = m_store.BeginTransaction();
            Assert.That(m_store.ActiveTransactionCount, Is.EqualTo(2));

            tx1.Commit();
            Assert.That(m_store.ActiveTransactionCount, Is.EqualTo(1));

            tx2.Rollback();
            Assert.That(m_store.ActiveTransactionCount, Is.EqualTo(0));
        }

        #endregion

        #region Async Tests

        [Test]
        public async Task BeginTransactionAsyncWorksTest()
        {
            await using var tx = await m_store.BeginTransactionAsync();

            Assert.That(tx, Is.Not.Null);
            Assert.That(tx.State, Is.EqualTo(TransactionState.Active));
        }

        [Test]
        public async Task CommitAsyncWorksTest()
        {
            await using (var tx = await m_store.BeginTransactionAsync())
            {
                await tx.PutAsync(ToBytes("key1"), ToBytes("value1"));
                await tx.CommitAsync();
            }

            var result = await m_store.GetAsync(ToBytes("key1"));
            Assert.That(FromBytes(result!), Is.EqualTo("value1"));
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void DisposeRollsBackActiveTransactionsTest()
        {
            var innerStore = new StoreInMemory();
            var store = new MvccTransactionalStore(innerStore, ownsStore: false);

            var tx = store.BeginTransaction();
            tx.Put(ToBytes("key1"), ToBytes("value1"));

            store.Dispose();

            // Transaction should be rolled back
            Assert.That(tx.State, Is.EqualTo(TransactionState.RolledBack));

            innerStore.Dispose();
        }

        #endregion
    }
}
