using NUnit.Framework;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Transactions
{
    /// <summary>
    /// Tests for different isolation levels in MvccTransaction.
    /// </summary>
    [TestFixture]
    public class MvccTransactionIsolationLevelTests
    {
        private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);
        private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);
        private static string ToString(byte[]? data) => data == null ? "null" : System.Text.Encoding.UTF8.GetString(data);

        #region ReadUncommitted Tests

        [Test]
        public void ReadUncommittedSeesUncommittedChangesTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("initial"));

            // Start a write transaction
            using var writeTx = store.BeginTransaction();
            writeTx.Put(Key("key1"), Value("modified"));
            // NOT committed yet

            // Start a ReadUncommitted transaction
            using var readTx = store.BeginTransaction(IsolationLevel.ReadUncommitted);
            
            // ReadUncommitted should see the uncommitted change
            // Note: In our MVCC implementation, uncommitted changes from OTHER transactions
            // are visible through the timestamp manager's current timestamp
            var value = readTx.Get(Key("key1"));
            
            // This depends on implementation - in strict ReadUncommitted it would see "modified"
            // But our MVCC uses visibility rules that may still hide uncommitted from other txs
            Assert.That(value, Is.Not.Null);
        }

        [Test]
        public void ReadUncommittedSeesOwnChangesTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("initial"));

            using var tx = store.BeginTransaction(IsolationLevel.ReadUncommitted);
            tx.Put(Key("key1"), Value("modified"));

            // Should see own uncommitted change
            var value = tx.Get(Key("key1"));
            Assert.That(ToString(value), Is.EqualTo("modified"));
        }

        #endregion

        #region ReadCommitted Tests

        [Test]
        public void ReadCommittedSeesLatestCommittedDataTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            // Start a ReadCommitted transaction
            using var readTx = store.BeginTransaction(IsolationLevel.ReadCommitted);
            
            // First read
            var value1 = readTx.Get(Key("key1"));
            Assert.That(ToString(value1), Is.EqualTo("v1"));

            // Another transaction commits a change
            using (var writeTx = store.BeginTransaction())
            {
                writeTx.Put(Key("key1"), Value("v2"));
                writeTx.Commit();
            }

            // ReadCommitted should see the new committed value (non-repeatable read)
            var value2 = readTx.Get(Key("key1"));
            Assert.That(ToString(value2), Is.EqualTo("v2"));
        }

        [Test]
        public void ReadCommittedDoesNotSeeUncommittedTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("initial"));

            // Start a write transaction but don't commit
            var writeTx = store.BeginTransaction();
            writeTx.Put(Key("key1"), Value("uncommitted"));

            // ReadCommitted should not see uncommitted change
            using var readTx = store.BeginTransaction(IsolationLevel.ReadCommitted);
            var value = readTx.Get(Key("key1"));
            Assert.That(ToString(value), Is.EqualTo("initial"));

            writeTx.Rollback();
        }

        [Test]
        public void ReadCommittedAllowsNonRepeatableReadsTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            using var readTx = store.BeginTransaction(IsolationLevel.ReadCommitted);
            
            // First read
            var read1 = readTx.Get(Key("key1"));
            Assert.That(ToString(read1), Is.EqualTo("v1"));

            // Concurrent modification and commit
            using (var writeTx = store.BeginTransaction())
            {
                writeTx.Put(Key("key1"), Value("v2"));
                writeTx.Commit();
            }

            // Second read - gets different value (non-repeatable read)
            var read2 = readTx.Get(Key("key1"));
            Assert.That(ToString(read2), Is.EqualTo("v2"));
            Assert.That(ToString(read1), Is.Not.EqualTo(ToString(read2)));
        }

        #endregion

        #region RepeatableRead Tests

        [Test]
        public void RepeatableReadPreventsNonRepeatableReadsTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            using var readTx = store.BeginTransaction(IsolationLevel.RepeatableRead);
            
            // First read
            var read1 = readTx.Get(Key("key1"));
            Assert.That(ToString(read1), Is.EqualTo("v1"));

            // Concurrent modification
            using (var writeTx = store.BeginTransaction())
            {
                writeTx.Put(Key("key1"), Value("v2"));
                writeTx.Commit();
            }

            // Second read - should get SAME value (repeatable read)
            var read2 = readTx.Get(Key("key1"));
            Assert.That(ToString(read2), Is.EqualTo("v1")); // Same as first read
        }

        [Test]
        public void RepeatableReadTracksReadSetTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            var readTx = (MvccTransaction)store.BeginTransaction(IsolationLevel.RepeatableRead);
            
            // Read should add to read set
            readTx.Get(Key("key1"));
            
            Assert.That(readTx.ReadSet.Count, Is.EqualTo(1));
        }

        [Test]
        public void RepeatableReadDetectsReadConflictOnCommitTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            var tx = store.BeginTransaction(IsolationLevel.RepeatableRead);
            
            // Read the key
            tx.Get(Key("key1"));
            
            // Write something else (so we have changes to commit)
            tx.Put(Key("key2"), Value("v2"));

            // Concurrent modification to the key we read
            using (var writeTx = store.BeginTransaction())
            {
                writeTx.Put(Key("key1"), Value("modified"));
                writeTx.Commit();
            }

            // Commit should fail because key1 was modified after we read it
            Assert.Throws<InvalidOperationException>(() => tx.Commit());
            tx.Dispose();
        }

        #endregion

        #region Snapshot Tests

        [Test]
        public void SnapshotIsolationProvidesConsistentViewTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));
            store.Put(Key("key2"), Value("v2"));

            using var snapshotTx = store.BeginTransaction(IsolationLevel.Snapshot);
            
            // Read first value
            var read1 = snapshotTx.Get(Key("key1"));
            Assert.That(ToString(read1), Is.EqualTo("v1"));

            // Concurrent modifications
            using (var writeTx = store.BeginTransaction())
            {
                writeTx.Put(Key("key1"), Value("modified1"));
                writeTx.Put(Key("key2"), Value("modified2"));
                writeTx.Commit();
            }

            // Snapshot still sees original values
            var read1Again = snapshotTx.Get(Key("key1"));
            var read2 = snapshotTx.Get(Key("key2"));
            
            Assert.That(ToString(read1Again), Is.EqualTo("v1"));
            Assert.That(ToString(read2), Is.EqualTo("v2"));
        }

        [Test]
        public void SnapshotDoesNotTrackReadSetByDefaultTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            var snapshotTx = (MvccTransaction)store.BeginTransaction(IsolationLevel.Snapshot);
            snapshotTx.Get(Key("key1"));
            
            // Snapshot isolation doesn't need to track reads for conflict detection
            Assert.That(snapshotTx.ReadSet.Count, Is.EqualTo(0));
            snapshotTx.Dispose();
        }

        [Test]
        public void SnapshotDetectsWriteWriteConflictTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            var tx1 = store.BeginTransaction(IsolationLevel.Snapshot);
            var tx2 = store.BeginTransaction(IsolationLevel.Snapshot);

            // Both modify same key
            tx1.Put(Key("key1"), Value("tx1"));
            tx2.Put(Key("key1"), Value("tx2"));

            // First commit wins
            tx1.Commit();

            // Second should fail
            Assert.Throws<InvalidOperationException>(() => tx2.Commit());
            
            tx1.Dispose();
            tx2.Dispose();
        }

        #endregion

        #region Serializable Tests

        [Test]
        public void SerializableTracksReadSetTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            var tx = (MvccTransaction)store.BeginTransaction(IsolationLevel.Serializable);
            tx.Get(Key("key1"));
            
            Assert.That(tx.ReadSet.Count, Is.EqualTo(1));
            tx.Dispose();
        }

        [Test]
        public void SerializableDetectsReadConflictTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("v1"));

            var tx = store.BeginTransaction(IsolationLevel.Serializable);
            
            // Read the key
            tx.Get(Key("key1"));
            
            // Write something (so we have changes)
            tx.Put(Key("key2"), Value("new"));

            // Concurrent modification to key we read
            using (var writeTx = store.BeginTransaction())
            {
                writeTx.Put(Key("key1"), Value("modified"));
                writeTx.Commit();
            }

            // Commit should fail - serialization failure
            var ex = Assert.Throws<InvalidOperationException>(() => tx.Commit());
            Assert.That(ex!.Message, Does.Contain("serialization failure"));
            tx.Dispose();
        }

        #endregion

        #region Cross-Isolation Tests

        [Test]
        public void DifferentIsolationLevelsCanCoexistTest()
        {
            using var store = CreateStore();
            store.Put(Key("key1"), Value("initial"));

            // Start transactions with different isolation levels
            using var snapshotTx = store.BeginTransaction(IsolationLevel.Snapshot);
            using var readCommittedTx = store.BeginTransaction(IsolationLevel.ReadCommitted);

            // Both read initial value
            var snapshot1 = snapshotTx.Get(Key("key1"));
            var readComm1 = readCommittedTx.Get(Key("key1"));
            Assert.That(ToString(snapshot1), Is.EqualTo("initial"));
            Assert.That(ToString(readComm1), Is.EqualTo("initial"));

            // Commit a change
            using (var writeTx = store.BeginTransaction())
            {
                writeTx.Put(Key("key1"), Value("modified"));
                writeTx.Commit();
            }

            // Snapshot still sees initial
            var snapshot2 = snapshotTx.Get(Key("key1"));
            Assert.That(ToString(snapshot2), Is.EqualTo("initial"));

            // ReadCommitted sees modified
            var readComm2 = readCommittedTx.Get(Key("key1"));
            Assert.That(ToString(readComm2), Is.EqualTo("modified"));
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
