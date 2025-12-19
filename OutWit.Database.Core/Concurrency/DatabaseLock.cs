namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Provides thread-safe database locking with configurable timeout.
    /// Supports shared (read) and exclusive (write) locks.
    /// Includes both synchronous and asynchronous APIs.
    /// 
    /// Uses a unified SemaphoreSlim-based implementation for both sync and async paths
    /// to ensure consistent behavior and avoid potential race conditions.
    /// </summary>
    public sealed class DatabaseLock : IDisposable
    {
        #region Constants

        /// <summary>
        /// Gets the default lock timeout.
        /// </summary>
        internal static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromSeconds(5);

        #endregion

        #region Fields

        // Unified locking using SemaphoreSlim (supports both sync and async)
        private readonly SemaphoreSlim m_writeSemaphore = new(1, 1);
        private readonly SemaphoreSlim m_readerCountLock = new(1, 1);
        private readonly TimeSpan m_lockTimeout;
        private int m_readerCount;
        private int m_waitingWriters;
        private int m_waitingReaders;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new database lock with the specified timeout.
        /// </summary>
        /// <param name="lockTimeout">Maximum time to wait for lock acquisition.</param>
        public DatabaseLock(TimeSpan? lockTimeout = null)
        {
            m_lockTimeout = lockTimeout ?? DEFAULT_TIMEOUT;
        }

        #endregion

        #region ReadLock

        /// <summary>
        /// Acquires a shared (read) lock.
        /// Multiple readers can hold shared locks simultaneously.
        /// </summary>
        /// <returns>A disposable handle that releases the lock when disposed.</returns>
        /// <exception cref="TimeoutException">Thrown if the lock cannot be acquired within the timeout.</exception>
        public IDisposable AcquireReadLock()
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref m_waitingReaders);
            
            try
            {
                if (!m_readerCountLock.Wait(m_lockTimeout))
                    throw new TimeoutException($"Could not acquire read lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
                
                try
                {
                    m_readerCount++;
                    if (m_readerCount == 1)
                    {
                        // First reader blocks writers
                        if (!m_writeSemaphore.Wait(m_lockTimeout))
                        {
                            m_readerCount--;
                            throw new TimeoutException($"Could not acquire read lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
                        }
                    }
                }
                finally
                {
                    m_readerCountLock.Release();
                }
                
                Interlocked.Decrement(ref m_waitingReaders);
                return new LockHandleSync(ReleaseReadLock);
            }
            catch
            {
                Interlocked.Decrement(ref m_waitingReaders);
                throw;
            }
        }

        /// <summary>
        /// Acquires a shared (read) lock asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async disposable handle that releases the lock when disposed.</returns>
        public async Task<IAsyncDisposable> AcquireReadLockAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref m_waitingReaders);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(m_lockTimeout);

            try
            {
                await m_readerCountLock.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    m_readerCount++;
                    if (m_readerCount == 1)
                    {
                        // First reader blocks writers
                        await m_writeSemaphore.WaitAsync(cts.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_readerCountLock.Release();
                }

                Interlocked.Decrement(ref m_waitingReaders);
                return new LockHandleAsync(ReleaseReadLockAsync);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Interlocked.Decrement(ref m_waitingReaders);
                throw new TimeoutException($"Could not acquire read lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
            }
            catch
            {
                Interlocked.Decrement(ref m_waitingReaders);
                throw;
            }
        }

        private void ReleaseReadLock()
        {
            m_readerCountLock.Wait();
            try
            {
                m_readerCount--;
                if (m_readerCount == 0)
                {
                    m_writeSemaphore.Release();
                }
            }
            finally
            {
                m_readerCountLock.Release();
            }
        }

        private async ValueTask ReleaseReadLockAsync()
        {
            await m_readerCountLock.WaitAsync().ConfigureAwait(false);
            try
            {
                m_readerCount--;
                if (m_readerCount == 0)
                {
                    m_writeSemaphore.Release();
                }
            }
            finally
            {
                m_readerCountLock.Release();
            }
        }

        #endregion

        #region WriteLock

        /// <summary>
        /// Acquires an exclusive (write) lock.
        /// Only one writer can hold the lock, and no readers are allowed.
        /// </summary>
        /// <returns>A disposable handle that releases the lock when disposed.</returns>
        /// <exception cref="TimeoutException">Thrown if the lock cannot be acquired within the timeout.</exception>
        public IDisposable AcquireWriteLock()
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref m_waitingWriters);
            
            try
            {
                if (!m_writeSemaphore.Wait(m_lockTimeout))
                    throw new TimeoutException($"Could not acquire write lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
                
                Interlocked.Decrement(ref m_waitingWriters);
                return new LockHandleSync(() => m_writeSemaphore.Release());
            }
            catch
            {
                Interlocked.Decrement(ref m_waitingWriters);
                throw;
            }
        }

        /// <summary>
        /// Acquires an exclusive (write) lock asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async disposable handle that releases the lock when disposed.</returns>
        public async Task<IAsyncDisposable> AcquireWriteLockAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref m_waitingWriters);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(m_lockTimeout);

            try
            {
                await m_writeSemaphore.WaitAsync(cts.Token).ConfigureAwait(false);
                Interlocked.Decrement(ref m_waitingWriters);
                return new LockHandleAsync(() =>
                {
                    m_writeSemaphore.Release();
                    return ValueTask.CompletedTask;
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Interlocked.Decrement(ref m_waitingWriters);
                throw new TimeoutException($"Could not acquire write lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
            }
            catch
            {
                Interlocked.Decrement(ref m_waitingWriters);
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
            m_writeSemaphore.Dispose();
            m_readerCountLock.Dispose();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the current number of threads waiting to acquire a read lock.
        /// </summary>
        public int WaitingReadCount => Volatile.Read(ref m_waitingReaders);

        /// <summary>
        /// Gets the current number of threads waiting to acquire a write lock.
        /// </summary>
        public int WaitingWriteCount => Volatile.Read(ref m_waitingWriters);

        /// <summary>
        /// Gets whether any thread holds a read lock.
        /// </summary>
        public bool IsReadLockHeld => Volatile.Read(ref m_readerCount) > 0;

        /// <summary>
        /// Gets whether any thread holds a write lock.
        /// </summary>
        public bool IsWriteLockHeld => m_writeSemaphore.CurrentCount == 0 && Volatile.Read(ref m_readerCount) == 0;

        #endregion

    }
}
