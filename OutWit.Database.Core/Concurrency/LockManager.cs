namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Coordinates both in-process and file-level locking for database access.
    /// Provides a unified API for acquiring and releasing locks.
    /// Supports both synchronous and asynchronous operations.
    /// 
    /// Note: FileLock is only used for write operations to coordinate between processes.
    /// Read operations only use in-process DatabaseLock since FileLock doesn't support
    /// true shared locks.
    /// </summary>
    public sealed class LockManager : IDisposable
    {
        #region Fields

        private readonly DatabaseLock m_processLock;
        private readonly FileLock? m_fileLock;
        private readonly bool m_useFileLocking;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a lock manager for in-memory databases (no file locking).
        /// </summary>
        /// <param name="lockTimeout">Maximum time to wait for lock acquisition.</param>
        public LockManager(TimeSpan? lockTimeout = null)
        {
            m_processLock = new DatabaseLock(lockTimeout);
            m_fileLock = null;
            m_useFileLocking = false;
        }

        /// <summary>
        /// Creates a lock manager for file-based databases.
        /// </summary>
        /// <param name="databasePath">Path to the database file.</param>
        /// <param name="lockTimeout">Maximum time to wait for lock acquisition.</param>
        public LockManager(string databasePath, TimeSpan? lockTimeout = null)
        {
            m_processLock = new DatabaseLock(lockTimeout);
            m_fileLock = new FileLock(databasePath, lockTimeout);
            m_useFileLocking = true;
        }

        #endregion

        #region ReadLock

        /// <summary>
        /// Acquires a read lock for the database.
        /// Multiple readers can hold locks simultaneously within the same process.
        /// Note: File locking is not used for read operations.
        /// </summary>
        /// <returns>A disposable handle that releases all locks when disposed.</returns>
        public IDisposable AcquireReadLock()
        {
            ThrowIfDisposed();
        
            // Only use in-process lock for reads - FileLock doesn't support shared locks
            return m_processLock.AcquireReadLock();
        }

        /// <summary>
        /// Acquires a read lock asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async disposable handle that releases all locks when disposed.</returns>
        public async Task<IAsyncDisposable> AcquireReadLockAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Only use in-process lock for reads
            return await m_processLock.AcquireReadLockAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region WriteLock

        /// <summary>
        /// Acquires a write lock for the database.
        /// Only one writer can hold the lock, blocking all readers.
        /// Uses both in-process and file locking for cross-process coordination.
        /// </summary>
        /// <returns>A disposable handle that releases all locks when disposed.</returns>
        public IDisposable AcquireWriteLock()
        {
            ThrowIfDisposed();

            var processHandle = m_processLock.AcquireWriteLock();

            try
            {
                if (m_useFileLocking)
                {
                    m_fileLock!.AcquireExclusiveLock();
                }

                return new LockHandleSyncCombined(processHandle, m_fileLock);
            }
            catch
            {
                processHandle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Acquires a write lock asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async disposable handle that releases all locks when disposed.</returns>
        public async Task<IAsyncDisposable> AcquireWriteLockAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
        
            var processHandle = await m_processLock.AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
        
            try
            {
                if (m_useFileLocking)
                {
                    await m_fileLock!.AcquireExclusiveLockAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            
                return new LockHandleAsyncCombined(processHandle, m_fileLock);
            }
            catch
            {
                await processHandle.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;

            m_fileLock?.Dispose();
            m_processLock.Dispose();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether file-level locking is enabled.
        /// </summary>
        public bool UseFileLocking => m_useFileLocking;

        /// <summary>
        /// Gets the current number of threads waiting for read access.
        /// </summary>
        public int WaitingReadCount => m_processLock.WaitingReadCount;

        /// <summary>
        /// Gets the current number of threads waiting for write access.
        /// </summary>
        public int WaitingWriteCount => m_processLock.WaitingWriteCount;

        #endregion
    }
}
