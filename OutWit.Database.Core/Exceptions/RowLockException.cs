namespace OutWit.Database.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when a row lock cannot be acquired.
    /// </summary>
    public class RowLockException : Exception
    {
        #region Constructors

        /// <summary>
        /// Creates a new row lock exception.
        /// </summary>
        public RowLockException()
            : base("Cannot acquire row lock.")
        {
        }

        /// <summary>
        /// Creates a new row lock exception with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public RowLockException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a new row lock exception with a message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public RowLockException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a new row lock exception for a specific key.
        /// </summary>
        /// <param name="key">The key that could not be locked.</param>
        /// <param name="holdingTransactionId">The transaction holding the lock.</param>
        /// <param name="requestingTransactionId">The transaction requesting the lock.</param>
        public RowLockException(byte[] key, long holdingTransactionId, long requestingTransactionId)
            : base($"Cannot acquire lock on key. Lock held by transaction {holdingTransactionId}, requested by transaction {requestingTransactionId}.")
        {
            Key = key;
            HoldingTransactionId = holdingTransactionId;
            RequestingTransactionId = requestingTransactionId;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the key that could not be locked, if available.
        /// </summary>
        public byte[]? Key { get; }

        /// <summary>
        /// Gets the ID of the transaction holding the lock, if available.
        /// </summary>
        public long? HoldingTransactionId { get; }

        /// <summary>
        /// Gets the ID of the transaction requesting the lock, if available.
        /// </summary>
        public long? RequestingTransactionId { get; }

        #endregion
    }
}
