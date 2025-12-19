namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Provides thread-safe database locking with configurable timeout.
    /// Supports shared (read) and exclusive (write) locks.
    /// Includes both synchronous and asynchronous APIs.
    /// 
    /// Implementation uses a fair reader-writer lock pattern:
    /// - Multiple readers can hold locks simultaneously
    /// - Writers have priority over new readers (prevents writer starvation)
    /// - Uses SemaphoreSlim for both sync and async support
    /// - Detects reentrant write lock acquisition on same thread (sync only)
    /// </summary>
    public sealed class DatabaseLock : IDisposable
    {
        #region Constants

        /// <summary>
        /// Gets the default lock timeout.
        /// </summary>
        internal static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromSeconds(5);
        
        /// <summary>
        /// Timeout for release operations (should never actually timeout).
        /// </summary>
        private static readonly TimeSpan RELEASE_TIMEOUT = TimeSpan.FromSeconds(30);

        #endregion

        #region Fields

        // Main lock for coordinating readers and writers
        private readonly SemaphoreSlim m_writeSemaphore = new(1, 1);
        // Protects reader count modifications
        private readonly SemaphoreSlim m_readerCountLock = new(1, 1);
        // Gate for new readers when writer is waiting (writer priority)
        private readonly SemaphoreSlim m_readerGate = new(1, 1);
        
        private readonly TimeSpan m_lockTimeout;
        private int m_readerCount;
        private int m_waitingWriters;
        private int m_waitingReaders;
        private bool m_disposed;
        
        // Track write lock owner thread for reentrancy detection (sync only)
        private int m_writeOwnerThreadId = -1;

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
        /// Writers have priority - new readers wait if a writer is waiting.
        /// </summary>
        /// <returns>A disposable handle that releases the lock when disposed.</returns>
        /// <exception cref="TimeoutException">Thrown if the lock cannot be acquired within the timeout.</exception>
        /// <exception cref="LockRecursionException">Thrown if current thread already holds write lock.</exception>
        public IDisposable AcquireReadLock()
        {
            ThrowIfDisposed();
            
            // Check for write lock held by current thread
            if (m_writeOwnerThreadId == Environment.CurrentManagedThreadId)
            {
                throw new LockRecursionException("Cannot acquire read lock - current thread already holds write lock.");
            }
            
            Interlocked.Increment(ref m_waitingReaders);
            
            try
            {
                // Wait for reader gate (blocks if writer is waiting - writer priority)
                if (!m_readerGate.Wait(m_lockTimeout))
                    throw new TimeoutException($"Could not acquire read lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
                
                try
                {
                    // Now acquire reader count lock
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
                }
                finally
                {
                    m_readerGate.Release();
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
                // Wait for reader gate (blocks if writer is waiting)
                await m_readerGate.WaitAsync(cts.Token).ConfigureAwait(false);
                
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
                }
                finally
                {
                    m_readerGate.Release();
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
            // Use timeout to prevent deadlock, but it should never actually timeout
            if (!m_readerCountLock.Wait(RELEASE_TIMEOUT))
            {
                // This should never happen, but if it does, we have a serious problem
                throw new InvalidOperationException("Failed to release read lock - potential deadlock detected");
            }
            
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
            // Use timeout to prevent deadlock
            using var cts = new CancellationTokenSource(RELEASE_TIMEOUT);
            
            await m_readerCountLock.WaitAsync(cts.Token).ConfigureAwait(false);
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
        /// Writers have priority over new readers.
        /// </summary>
        /// <returns>A disposable handle that releases the lock when disposed.</returns>
        /// <exception cref="TimeoutException">Thrown if the lock cannot be acquired within the timeout.</exception>
        /// <exception cref="LockRecursionException">Thrown if current thread already holds write lock.</exception>
        public IDisposable AcquireWriteLock()
        {
            ThrowIfDisposed();
            
            // Check for write lock held by current thread
            if (m_writeOwnerThreadId == Environment.CurrentManagedThreadId)
            {
                throw new LockRecursionException("Cannot acquire write lock - current thread already holds write lock.");
            }
            
            Interlocked.Increment(ref m_waitingWriters);
            
            bool readerGateAcquired = false;
            bool writeSemaphoreAcquired = false;
            
            try
            {
                // Block new readers while writer is waiting
                if (!m_readerGate.Wait(m_lockTimeout))
                    throw new TimeoutException($"Could not acquire write lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
                readerGateAcquired = true;
                
                // Now wait for write semaphore (existing readers will release it)
                if (!m_writeSemaphore.Wait(m_lockTimeout))
                    throw new TimeoutException($"Could not acquire write lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
                writeSemaphoreAcquired = true;
                
                // Mark current thread as owner
                m_writeOwnerThreadId = Environment.CurrentManagedThreadId;
                
                Interlocked.Decrement(ref m_waitingWriters);
                return new LockHandleSync(() =>
                {
                    m_writeOwnerThreadId = -1;
                    m_writeSemaphore.Release();
                    m_readerGate.Release();
                });
            }
            catch
            {
                // Clean up on failure
                if (writeSemaphoreAcquired)
                    m_writeSemaphore.Release();
                if (readerGateAcquired)
                    m_readerGate.Release();
                    
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

            bool readerGateAcquired = false;
            bool writeSemaphoreAcquired = false;

            try
            {
                // Block new readers while writer is waiting
                await m_readerGate.WaitAsync(cts.Token).ConfigureAwait(false);
                readerGateAcquired = true;
                
                // Now wait for write semaphore
                await m_writeSemaphore.WaitAsync(cts.Token).ConfigureAwait(false);
                writeSemaphoreAcquired = true;
                
                Interlocked.Decrement(ref m_waitingWriters);
                return new LockHandleAsync(() =>
                {
                    m_writeSemaphore.Release();
                    m_readerGate.Release();
                    return ValueTask.CompletedTask;
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Clean up on failure
                if (writeSemaphoreAcquired)
                    m_writeSemaphore.Release();
                if (readerGateAcquired)
                    m_readerGate.Release();
                    
                Interlocked.Decrement(ref m_waitingWriters);
                throw new TimeoutException($"Could not acquire write lock within {m_lockTimeout.TotalSeconds:F1} seconds. Database is locked.");
            }
            catch
            {
                // Clean up on failure
                if (writeSemaphoreAcquired)
                    m_writeSemaphore.Release();
                if (readerGateAcquired)
                    m_readerGate.Release();
                    
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
            m_readerGate.Dispose();
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

        /// <summary>
        /// Gets the current number of active readers.
        /// </summary>
        public int CurrentReaderCount => Volatile.Read(ref m_readerCount);
        
        /// <summary>
        /// Gets whether the current thread holds a write lock (sync operations only).
        /// </summary>
        public bool IsWriteLockHeldByCurrentThread => m_writeOwnerThreadId == Environment.CurrentManagedThreadId;

        #endregion

    }
}
