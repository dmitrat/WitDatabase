using System.Buffers.Binary;

namespace OutWit.Database.Core.Mvcc
{
    /// <summary>
    /// Represents a versioned record for MVCC (Multi-Version Concurrency Control).
    /// Each record has a creation timestamp, optional deletion timestamp, 
    /// transaction ID, and the actual value.
    /// </summary>
    public readonly struct MvccRecord
    {
        #region Constants

        /// <summary>
        /// Size of the MVCC metadata header in bytes.
        /// Layout: CreateTimestamp(8) + DeleteTimestamp(8) + TransactionId(8) = 24 bytes
        /// </summary>
        public const int HEADER_SIZE = 24;

        /// <summary>
        /// Value indicating the record has not been deleted.
        /// </summary>
        public const long NOT_DELETED = long.MaxValue;

        /// <summary>
        /// Value indicating no transaction (committed data).
        /// </summary>
        public const long NO_TRANSACTION = 0;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new MVCC record.
        /// </summary>
        /// <param name="value">The record value.</param>
        /// <param name="createTimestamp">The timestamp when the record was created.</param>
        /// <param name="transactionId">The ID of the transaction that created this version (0 if committed).</param>
        /// <param name="deleteTimestamp">The timestamp when the record was deleted (MaxValue if not deleted).</param>
        public MvccRecord(
            byte[] value, 
            long createTimestamp, 
            long transactionId = NO_TRANSACTION,
            long deleteTimestamp = NOT_DELETED)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            CreateTimestamp = createTimestamp;
            DeleteTimestamp = deleteTimestamp;
            TransactionId = transactionId;
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes the record to a byte array.
        /// Format: [CreateTimestamp:8][DeleteTimestamp:8][TransactionId:8][Value:N]
        /// </summary>
        public byte[] Serialize()
        {
            var result = new byte[HEADER_SIZE + Value.Length];
            
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(0, 8), CreateTimestamp);
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(8, 8), DeleteTimestamp);
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(16, 8), TransactionId);
            Value.CopyTo(result.AsSpan(HEADER_SIZE));
            
            return result;
        }

        /// <summary>
        /// Deserializes a record from a byte array.
        /// </summary>
        /// <param name="data">The serialized data.</param>
        /// <returns>The deserialized record.</returns>
        /// <exception cref="ArgumentException">If data is too short.</exception>
        public static MvccRecord Deserialize(ReadOnlySpan<byte> data)
        {
            if (data.Length < HEADER_SIZE)
                throw new ArgumentException($"Data must be at least {HEADER_SIZE} bytes.", nameof(data));

            var createTs = BinaryPrimitives.ReadInt64LittleEndian(data[..8]);
            var deleteTs = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(8, 8));
            var txId = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(16, 8));
            var value = data[HEADER_SIZE..].ToArray();

            return new MvccRecord(value, createTs, txId, deleteTs);
        }

        /// <summary>
        /// Attempts to deserialize a record from a byte array.
        /// </summary>
        /// <param name="data">The serialized data.</param>
        /// <param name="record">The deserialized record if successful.</param>
        /// <returns>True if deserialization succeeded, false otherwise.</returns>
        public static bool TryDeserialize(ReadOnlySpan<byte> data, out MvccRecord record)
        {
            if (data.Length < HEADER_SIZE)
            {
                record = default;
                return false;
            }

            try
            {
                record = Deserialize(data);
                return true;
            }
            catch
            {
                record = default;
                return false;
            }
        }

        #endregion

        #region Visibility

        /// <summary>
        /// Checks if this record is visible to a transaction with the given snapshot timestamp.
        /// </summary>
        /// <param name="snapshotTimestamp">The snapshot timestamp of the reading transaction.</param>
        /// <param name="readingTransactionId">The ID of the reading transaction (0 for non-transactional reads).</param>
        /// <param name="isCommittedFunc">Function to check if a transaction is committed.</param>
        /// <param name="getCommitTimestampFunc">Function to get the commit timestamp of a transaction.</param>
        /// <returns>True if the record is visible, false otherwise.</returns>
        public bool IsVisibleTo(
            long snapshotTimestamp, 
            long readingTransactionId,
            Func<long, bool> isCommittedFunc,
            Func<long, long?> getCommitTimestampFunc)
        {
            // Rule 1: If created by our own transaction, it's visible
            if (TransactionId == readingTransactionId && readingTransactionId != NO_TRANSACTION)
            {
                return !IsDeleted;
            }

            // Rule 2: If created by an uncommitted transaction (not ours), not visible
            if (TransactionId != NO_TRANSACTION && !isCommittedFunc(TransactionId))
            {
                return false;
            }

            // Rule 3: If created after our snapshot, not visible
            var effectiveCreateTs = TransactionId == NO_TRANSACTION 
                ? CreateTimestamp 
                : getCommitTimestampFunc(TransactionId) ?? CreateTimestamp;
            
            if (effectiveCreateTs > snapshotTimestamp)
            {
                return false;
            }

            // Rule 4: If deleted before our snapshot by a committed transaction, not visible
            if (IsDeleted && DeleteTimestamp <= snapshotTimestamp)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Simplified visibility check for committed data only (no active transaction context).
        /// Used for non-transactional reads.
        /// </summary>
        /// <param name="asOfTimestamp">The timestamp to check visibility as of.</param>
        /// <returns>True if the record is visible, false otherwise.</returns>
        public bool IsVisibleAsOf(long asOfTimestamp)
        {
            // Only committed records (TransactionId == 0) are considered
            if (TransactionId != NO_TRANSACTION)
                return false;

            // Created before the check timestamp
            if (CreateTimestamp > asOfTimestamp)
                return false;

            // Not deleted, or deleted after the check timestamp
            if (IsDeleted && DeleteTimestamp <= asOfTimestamp)
                return false;

            return true;
        }

        #endregion

        #region Modification

        /// <summary>
        /// Creates a new record marking this one as deleted at the specified timestamp.
        /// </summary>
        /// <param name="deleteTimestamp">The deletion timestamp.</param>
        /// <returns>A new record with the delete timestamp set.</returns>
        public MvccRecord WithDeleteTimestamp(long deleteTimestamp)
        {
            return new MvccRecord(Value, CreateTimestamp, TransactionId, deleteTimestamp);
        }

        /// <summary>
        /// Creates a new record with the transaction marked as committed.
        /// Sets TransactionId to 0 (committed).
        /// </summary>
        /// <returns>A new record marked as committed.</returns>
        public MvccRecord AsCommitted()
        {
            return new MvccRecord(Value, CreateTimestamp, NO_TRANSACTION, DeleteTimestamp);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the record value.
        /// </summary>
        public byte[] Value { get; }

        /// <summary>
        /// Gets the timestamp when this version was created.
        /// </summary>
        public long CreateTimestamp { get; }

        /// <summary>
        /// Gets the timestamp when this version was deleted.
        /// MaxValue indicates the record has not been deleted.
        /// </summary>
        public long DeleteTimestamp { get; }

        /// <summary>
        /// Gets the ID of the transaction that created this version.
        /// 0 indicates the record has been committed.
        /// </summary>
        public long TransactionId { get; }

        /// <summary>
        /// Gets whether this record has been deleted.
        /// </summary>
        public bool IsDeleted => DeleteTimestamp != NOT_DELETED;

        /// <summary>
        /// Gets whether this record has been committed (not part of an active transaction).
        /// </summary>
        public bool IsCommitted => TransactionId == NO_TRANSACTION;

        #endregion
    }
}
