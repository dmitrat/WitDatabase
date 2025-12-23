namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Represents a request to acquire a row-level lock.
    /// </summary>
    public sealed class RowLockRequest
    {
        #region Constructors

        /// <summary>
        /// Creates a new row lock request.
        /// </summary>
        /// <param name="key">The key to lock.</param>
        /// <param name="transactionId">The ID of the requesting transaction.</param>
        /// <param name="mode">The lock mode.</param>
        /// <param name="waitMode">The wait behavior if lock cannot be acquired immediately.</param>
        /// <param name="timeout">Optional timeout for waiting.</param>
        public RowLockRequest(
            byte[] key,
            long transactionId,
            RowLockMode mode = RowLockMode.Exclusive,
            RowLockWaitMode waitMode = RowLockWaitMode.Wait,
            TimeSpan? timeout = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            TransactionId = transactionId;
            Mode = mode;
            WaitMode = waitMode;
            Timeout = timeout;
            RequestedAt = DateTime.UtcNow;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the key to lock.
        /// </summary>
        public byte[] Key { get; }

        /// <summary>
        /// Gets the ID of the requesting transaction.
        /// </summary>
        public long TransactionId { get; }

        /// <summary>
        /// Gets the lock mode.
        /// </summary>
        public RowLockMode Mode { get; }

        /// <summary>
        /// Gets the wait behavior.
        /// </summary>
        public RowLockWaitMode WaitMode { get; }

        /// <summary>
        /// Gets the optional timeout for waiting.
        /// </summary>
        public TimeSpan? Timeout { get; }

        /// <summary>
        /// Gets when this request was created.
        /// </summary>
        public DateTime RequestedAt { get; }

        #endregion
    }
}
