using OutWit.Database.Core.Utils;

namespace OutWit.Database.Core.Concurrency;

/// <summary>
/// Provides file-level locking for multi-process synchronization.
/// Uses FileStream exclusive access for reliable cross-process locking.
/// 
/// Note: This implementation provides exclusive locking only.
/// Shared (reader) locks are not supported at the file level.
/// Use DatabaseLock for in-process reader/writer synchronization.
/// </summary>
public sealed class FileLock : IDisposable
{
    #region Constants

    private const int INITIAL_RETRY_DELAY_MS = 10;
    private const int MAX_RETRY_DELAY_MS = 500;

    #endregion

    #region Fields

    private readonly string m_lockFilePath;
    private readonly TimeSpan m_timeout;
    private FileStream? m_lockFile;
    private bool m_disposed;
    private bool m_hasLock;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a file lock for the specified database path.
    /// </summary>
    /// <param name="databasePath">Path to the database file.</param>
    /// <param name="timeout">Lock acquisition timeout.</param>
    public FileLock(string databasePath, TimeSpan? timeout = null)
    {
        // Through DatabaseFiles rather than by string concatenation, so that whoever deletes a
        // database and whoever locks it cannot drift apart - which is the whole reason that class
        // exists, and the sidecar drifted from it on the day it was introduced.
        m_lockFilePath = DatabaseFiles.GetLockPath(databasePath)!;
        m_timeout = timeout ?? DatabaseLock.DEFAULT_TIMEOUT;
    }

    #endregion

    #region SharedLock

    /// <summary>
    /// Acquires a shared (read) lock on the database file.
    /// Note: This is implemented as an exclusive lock for simplicity.
    /// For true shared locking, use DatabaseLock.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for the lock (overrides constructor timeout).</param>
    /// <exception cref="TimeoutException">Thrown if the lock cannot be acquired.</exception>
    public void AcquireSharedLock(TimeSpan? timeout = null)
    {
        // Shared lock is same as exclusive for file-level locking
        AcquireExclusiveLock(timeout);
    }

    /// <summary>
    /// Acquires a shared lock asynchronously.
    /// </summary>
    public Task AcquireSharedLockAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return AcquireExclusiveLockAsync(timeout, cancellationToken);
    }

    #endregion

    #region ExclusiveLock

    /// <summary>
    /// Tries once to acquire the exclusive lock, without waiting or retrying.
    /// </summary>
    /// <returns>True if the lock was taken; false if someone else holds it.</returns>
    /// <remarks>
    /// Added in 5.0.0 for the database-exclusivity guard, which wants "is this database already open"
    /// answered now rather than after a wait: the other holder is a whole engine, so waiting would
    /// turn a design limit into a stall.
    ///
    /// <b>Not expressible as <c>AcquireExclusiveLock(TimeSpan.Zero)</c>.</b> That overload computes
    /// <c>deadline = UtcNow + timeout</c> and loops <c>while (UtcNow &lt; deadline)</c>, so a zero
    /// timeout skips the body entirely and reports a timeout without ever having tried - it would
    /// refuse the first caller as readily as the second.
    /// </remarks>
    public bool TryAcquireExclusiveLock()
    {
        ThrowIfDisposed();

        if (m_hasLock)
            return true;

        EnsureDirectoryExists();
        return TryTakeOnce();
    }

    /// <summary>
    /// Tries to acquire the exclusive lock, retrying with backoff until the timeout expires.
    /// </summary>
    /// <returns>True if the lock was taken; false if someone else held it for the whole wait.</returns>
    /// <remarks>
    /// The waiting counterpart of <see cref="TryAcquireExclusiveLock()"/>, and it reports rather than
    /// throws for the same reason: the caller wants to turn "someone else has it" into its own
    /// exception, naming the database and the limit, not a <see cref="TimeoutException"/> about a file.
    ///
    /// A zero timeout is one attempt, deliberately - it must not mean "give up without trying", which
    /// is the trap <see cref="AcquireExclusiveLock"/>'s deadline loop falls into.
    /// </remarks>
    public bool TryAcquireExclusiveLock(TimeSpan timeout)
    {
        ThrowIfDisposed();

        if (m_hasLock)
            return true;

        EnsureDirectoryExists();

        var deadline = DateTime.UtcNow + timeout;
        var delay = INITIAL_RETRY_DELAY_MS;

        while (true)
        {
            if (TryTakeOnce())
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;

            Thread.Sleep(delay);
            delay = Math.Min(delay * 2, MAX_RETRY_DELAY_MS);
        }
    }

    private bool TryTakeOnce()
    {

        try
        {
            m_lockFile = new FileStream(
                m_lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.None);

            m_hasLock = true;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Unix reports a denied advisory lock this way in some configurations, and a read-only
            // directory reports it on both platforms. Either way the lock was not taken.
            return false;
        }
    }

    /// <summary>
    /// Acquires an exclusive (write) lock on the database file.
    /// No other processes can access while this lock is held.
    /// Uses exponential backoff for retries.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for the lock (overrides constructor timeout).</param>
    /// <exception cref="TimeoutException">Thrown if the lock cannot be acquired.</exception>
    public void AcquireExclusiveLock(TimeSpan? timeout = null)
    {
        ThrowIfDisposed();
        
        if (m_hasLock)
            return; // Already have lock
        
        var effectiveTimeout = timeout ?? m_timeout;
        var deadline = DateTime.UtcNow + effectiveTimeout;
        var delay = INITIAL_RETRY_DELAY_MS;

        EnsureDirectoryExists();

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // Try to open file with exclusive access - this IS the lock
                m_lockFile = new FileStream(
                    m_lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None, // Exclusive - no sharing
                    1,
                    FileOptions.None);
                
                m_hasLock = true;
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // UnauthorizedAccessException as well as IOException: Unix reports a denied advisory
                // lock that way in some configurations, and the single-attempt path has always caught
                // both. This loop caught only IOException, so on those configurations it escaped the
                // retry instead of waiting - found while wiring the open guard to it.
                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, MAX_RETRY_DELAY_MS);
            }
        }

        throw new TimeoutException($"Could not acquire exclusive file lock within {effectiveTimeout.TotalSeconds:F1} seconds.");
    }

    /// <summary>
    /// Acquires an exclusive lock asynchronously with exponential backoff.
    /// </summary>
    public async Task AcquireExclusiveLockAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (m_hasLock)
            return; // Already have lock
        
        var effectiveTimeout = timeout ?? m_timeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);
        
        EnsureDirectoryExists();
        
        var delay = INITIAL_RETRY_DELAY_MS;

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                m_lockFile = new FileStream(
                    m_lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous);
                
                m_hasLock = true;
                return;
            }
            catch (IOException)
            {
                try
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Could not acquire exclusive file lock within {effectiveTimeout.TotalSeconds:F1} seconds.");
                }
                delay = Math.Min(delay * 2, MAX_RETRY_DELAY_MS);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"Could not acquire exclusive file lock within {effectiveTimeout.TotalSeconds:F1} seconds.");
    }

    #endregion

    #region ReleaseLock

    /// <summary>
    /// Releases the currently held lock.
    /// </summary>
    public void ReleaseLock()
    {
        if (m_lockFile != null)
        {
            m_lockFile.Dispose();
            m_lockFile = null;
        }
        
        m_hasLock = false;
    }

    #endregion

    #region Tools

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    private void EnsureDirectoryExists()
    {
        var dir = Path.GetDirectoryName(m_lockFilePath);

        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        ReleaseLock();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether an exclusive lock is currently held.
    /// </summary>
    public bool HasExclusiveLock => m_hasLock;

    /// <summary>
    /// Gets whether a shared lock is currently held.
    /// Note: Shared lock is same as exclusive at file level.
    /// </summary>
    public bool HasSharedLock => m_hasLock;
    
    /// <summary>
    /// Gets whether any lock is currently held.
    /// </summary>
    public bool IsLockHeld => m_hasLock;

    #endregion

}