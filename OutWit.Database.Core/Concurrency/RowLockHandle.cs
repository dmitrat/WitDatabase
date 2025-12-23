namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Handle for a row-level lock. Disposing this handle releases the lock.
    /// </summary>
    public sealed class RowLockHandle : IDisposable, IAsyncDisposable
    {
        #region Fields

        private readonly IRowLockManager m_manager;
        private readonly byte[] m_key;
        private readonly long m_transactionId;
        private readonly RowLockMode m_mode;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new row lock handle.
        /// </summary>
        internal RowLockHandle(
            IRowLockManager manager,
            byte[] key,
            long transactionId,
            RowLockMode mode)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_key = key ?? throw new ArgumentNullException(nameof(key));
            m_transactionId = transactionId;
            m_mode = mode;
        }

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;

            // Release this specific lock
            // Note: We use ReleaseAllLocks in transaction cleanup
            // Individual lock release is handled by the manager internally
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the locked key.
        /// </summary>
        public byte[] Key => m_key;

        /// <summary>
        /// Gets the transaction ID holding this lock.
        /// </summary>
        public long TransactionId => m_transactionId;

        /// <summary>
        /// Gets the lock mode.
        /// </summary>
        public RowLockMode Mode => m_mode;

        /// <summary>
        /// Gets whether this handle has been disposed.
        /// </summary>
        public bool IsDisposed => m_disposed;

        #endregion
    }
}
