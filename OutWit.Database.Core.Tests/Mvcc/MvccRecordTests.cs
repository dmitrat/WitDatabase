using OutWit.Database.Core.Mvcc;

namespace OutWit.Database.Core.Tests.Mvcc
{
    [TestFixture]
    public class MvccRecordTests
    {
        #region Serialization Tests

        [Test]
        public void SerializeDeserializeRoundTripTest()
        {
            var value = new byte[] { 1, 2, 3, 4, 5 };
            var record = new MvccRecord(value, createTimestamp: 100, transactionId: 5, deleteTimestamp: 200);

            var serialized = record.Serialize();
            var deserialized = MvccRecord.Deserialize(serialized);

            Assert.That(deserialized.Value, Is.EqualTo(value));
            Assert.That(deserialized.CreateTimestamp, Is.EqualTo(100));
            Assert.That(deserialized.TransactionId, Is.EqualTo(5));
            Assert.That(deserialized.DeleteTimestamp, Is.EqualTo(200));
        }

        [Test]
        public void SerializeDeserializeEmptyValueTest()
        {
            var record = new MvccRecord(Array.Empty<byte>(), createTimestamp: 100);

            var serialized = record.Serialize();
            var deserialized = MvccRecord.Deserialize(serialized);

            Assert.That(deserialized.Value, Is.Empty);
            Assert.That(deserialized.CreateTimestamp, Is.EqualTo(100));
        }

        [Test]
        public void SerializeProducesCorrectSizeTest()
        {
            var value = new byte[100];
            var record = new MvccRecord(value, createTimestamp: 1);

            var serialized = record.Serialize();

            Assert.That(serialized.Length, Is.EqualTo(MvccRecord.HEADER_SIZE + value.Length));
        }

        [Test]
        public void DeserializeThrowsOnTooShortDataTest()
        {
            var shortData = new byte[MvccRecord.HEADER_SIZE - 1];

            Assert.Throws<ArgumentException>(() => MvccRecord.Deserialize(shortData));
        }

        [Test]
        public void TryDeserializeReturnsFalseOnInvalidDataTest()
        {
            var shortData = new byte[MvccRecord.HEADER_SIZE - 1];

            var result = MvccRecord.TryDeserialize(shortData, out var record);

            Assert.That(result, Is.False);
            Assert.That(record.Value, Is.Null);
        }

        [Test]
        public void TryDeserializeReturnsTrueOnValidDataTest()
        {
            var value = new byte[] { 1, 2, 3 };
            var original = new MvccRecord(value, createTimestamp: 100);
            var serialized = original.Serialize();

            var result = MvccRecord.TryDeserialize(serialized, out var record);

            Assert.That(result, Is.True);
            Assert.That(record.Value, Is.EqualTo(value));
        }

        #endregion

        #region Default Values Tests

        [Test]
        public void DefaultDeleteTimestampIsNotDeletedTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100);

            Assert.That(record.DeleteTimestamp, Is.EqualTo(MvccRecord.NOT_DELETED));
            Assert.That(record.IsDeleted, Is.False);
        }

        [Test]
        public void DefaultTransactionIdIsNoTransactionTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100);

            Assert.That(record.TransactionId, Is.EqualTo(MvccRecord.NO_TRANSACTION));
            Assert.That(record.IsCommitted, Is.True);
        }

        #endregion

        #region Visibility Tests - Basic

        [Test]
        public void CommittedRecordIsVisibleBeforeDeleteTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100);

            var visible = record.IsVisibleAsOf(150);

            Assert.That(visible, Is.True);
        }

        [Test]
        public void CommittedRecordNotVisibleBeforeCreateTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100);

            var visible = record.IsVisibleAsOf(50);

            Assert.That(visible, Is.False);
        }

        [Test]
        public void DeletedRecordNotVisibleAfterDeleteTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, deleteTimestamp: 200);

            var visible = record.IsVisibleAsOf(250);

            Assert.That(visible, Is.False);
        }

        [Test]
        public void DeletedRecordVisibleBetweenCreateAndDeleteTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, deleteTimestamp: 200);

            var visible = record.IsVisibleAsOf(150);

            Assert.That(visible, Is.True);
        }

        [Test]
        public void UncommittedRecordNotVisibleToOthersTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, transactionId: 5);

            var visible = record.IsVisibleAsOf(150);

            Assert.That(visible, Is.False);
        }

        #endregion

        #region Visibility Tests - Transaction Context

        [Test]
        public void RecordCreatedByOwnTransactionIsVisibleTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, transactionId: 5);

            var visible = record.IsVisibleTo(
                snapshotTimestamp: 50,  // Before creation
                readingTransactionId: 5,  // Same transaction
                isCommittedFunc: _ => false,
                getCommitTimestampFunc: _ => null);

            Assert.That(visible, Is.True);
        }

        [Test]
        public void RecordCreatedByOtherUncommittedTransactionNotVisibleTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, transactionId: 5);

            var visible = record.IsVisibleTo(
                snapshotTimestamp: 150,
                readingTransactionId: 10,  // Different transaction
                isCommittedFunc: txId => false,  // Not committed
                getCommitTimestampFunc: _ => null);

            Assert.That(visible, Is.False);
        }

        [Test]
        public void RecordCreatedByOtherCommittedTransactionIsVisibleTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, transactionId: 5);

            var visible = record.IsVisibleTo(
                snapshotTimestamp: 150,
                readingTransactionId: 10,
                isCommittedFunc: txId => txId == 5,  // Transaction 5 is committed
                getCommitTimestampFunc: txId => txId == 5 ? 120L : null);

            Assert.That(visible, Is.True);
        }

        [Test]
        public void RecordCommittedAfterSnapshotNotVisibleTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, transactionId: 5);

            var visible = record.IsVisibleTo(
                snapshotTimestamp: 110,  // Snapshot before commit
                readingTransactionId: 10,
                isCommittedFunc: txId => txId == 5,
                getCommitTimestampFunc: txId => txId == 5 ? 120L : null);  // Committed at 120

            Assert.That(visible, Is.False);
        }

        [Test]
        public void DeletedRecordInOwnTransactionNotVisibleTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, transactionId: 5, deleteTimestamp: 150);

            var visible = record.IsVisibleTo(
                snapshotTimestamp: 50,
                readingTransactionId: 5,
                isCommittedFunc: _ => false,
                getCommitTimestampFunc: _ => null);

            Assert.That(visible, Is.False);
        }

        #endregion

        #region Modification Tests

        [Test]
        public void WithDeleteTimestampCreatesNewRecordTest()
        {
            var original = new MvccRecord(new byte[] { 1 }, createTimestamp: 100);
            var deleted = original.WithDeleteTimestamp(200);

            Assert.That(deleted.DeleteTimestamp, Is.EqualTo(200));
            Assert.That(deleted.IsDeleted, Is.True);
            
            // Original unchanged
            Assert.That(original.DeleteTimestamp, Is.EqualTo(MvccRecord.NOT_DELETED));
            Assert.That(original.IsDeleted, Is.False);
        }

        [Test]
        public void WithDeleteTimestampPreservesOtherFieldsTest()
        {
            var value = new byte[] { 1, 2, 3 };
            var original = new MvccRecord(value, createTimestamp: 100, transactionId: 5);
            var deleted = original.WithDeleteTimestamp(200);

            Assert.That(deleted.Value, Is.EqualTo(value));
            Assert.That(deleted.CreateTimestamp, Is.EqualTo(100));
            Assert.That(deleted.TransactionId, Is.EqualTo(5));
        }

        [Test]
        public void AsCommittedSetsTransactionIdToZeroTest()
        {
            var original = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, transactionId: 5);
            var committed = original.AsCommitted();

            Assert.That(committed.TransactionId, Is.EqualTo(MvccRecord.NO_TRANSACTION));
            Assert.That(committed.IsCommitted, Is.True);
        }

        [Test]
        public void AsCommittedPreservesOtherFieldsTest()
        {
            var value = new byte[] { 1, 2, 3 };
            var original = new MvccRecord(value, createTimestamp: 100, transactionId: 5, deleteTimestamp: 200);
            var committed = original.AsCommitted();

            Assert.That(committed.Value, Is.EqualTo(value));
            Assert.That(committed.CreateTimestamp, Is.EqualTo(100));
            Assert.That(committed.DeleteTimestamp, Is.EqualTo(200));
        }

        #endregion

        #region Edge Cases

        [Test]
        public void RecordVisibleAtExactCreateTimestampTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100);

            var visible = record.IsVisibleAsOf(100);

            Assert.That(visible, Is.True);
        }

        [Test]
        public void RecordNotVisibleAtExactDeleteTimestampTest()
        {
            var record = new MvccRecord(new byte[] { 1 }, createTimestamp: 100, deleteTimestamp: 200);

            var visible = record.IsVisibleAsOf(200);

            Assert.That(visible, Is.False);
        }

        [Test]
        public void ConstructorThrowsOnNullValueTest()
        {
            Assert.Throws<ArgumentNullException>(() => 
                new MvccRecord(null!, createTimestamp: 100));
        }

        #endregion
    }
}
