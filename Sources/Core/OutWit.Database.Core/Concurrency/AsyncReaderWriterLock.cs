namespace OutWit.Database.Core.Concurrency;

/// <summary>
/// A reader/writer lock that may be held across an <c>await</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> <see cref="ReaderWriterLockSlim"/> is <i>thread-affine</i>: it records
/// which THREAD holds the lock, so a continuation resuming on another one throws
/// <c>SynchronizationLockException</c> out of the release and - far worse - leaves the lock held by a
/// thread that has moved on, so every later reader and writer waits for ever. That defect shipped in
/// 5.0.0 and was fixed in 6.0.0 by making every asynchronous entry point of
/// <see cref="Tree.BTreeConcurrentStore"/> do its work SYNCHRONOUSLY.
/// </para>
/// <para>
/// That fix was correct and it had a cost nobody had measured until 2026-08-10: since 12.0.0 the
/// concurrent wrapper wraps <b>every</b> B+Tree store, so the genuinely asynchronous path underneath -
/// <c>BTree.UpsertAsync</c> through <c>PageManager.AllocatePageAsync</c> to
/// <c>IStorage.WritePageAsync</c>, measured to work over a storage with no synchronous operations at
/// all - was unreachable in every supported configuration. A database in a browser could be built and
/// closed but never written to.
/// </para>
/// <para>
/// <see cref="SemaphoreSlim"/> is not thread-affine, so a lock built out of semaphores can be released
/// by whichever thread the continuation happens to land on. This one keeps the two properties the
/// wrapper depends on: <b>many concurrent readers</b>, and <b>one writer excluding everybody</b>.
/// </para>
/// <para>
/// <b>Writers are preferred</b>, via the turnstile: a waiting writer takes
/// <see cref="m_turnstile"/> and every subsequent reader queues behind it, so a steady stream of
/// readers cannot starve a writer indefinitely. Readers that are already inside are not disturbed -
/// the writer still waits for them through <see cref="m_writeGate"/>.
/// </para>
/// <para>
/// <b>Not reentrant</b>, exactly like the <c>LockRecursionPolicy.NoRecursion</c> it replaces. Taking
/// the same mode twice on one call path deadlocks rather than throwing, which is the one behaviour
/// this type has that its predecessor did not, and is why <see cref="Tree.BTreeConcurrentStore"/>'s
/// scan still hands its results out in chunks rather than holding a read lock across a consumer's
/// code.
/// </para>
/// </remarks>
public sealed class AsyncReaderWriterLock : IDisposable
{
    #region Fields

    /// <summary>
    /// Held by a writer for the whole of its turn, and by the FIRST reader on behalf of all readers.
    /// This is what makes writers exclusive with readers.
    /// </summary>
    private readonly SemaphoreSlim m_writeGate = new(1, 1);

    /// <summary>
    /// Guards <see cref="m_readers"/>. Held only for the few instructions that change the count, never
    /// across the wait on <see cref="m_writeGate"/>... except by the first reader, which is exactly
    /// where a reader has to wait for a writer to finish.
    /// </summary>
    private readonly SemaphoreSlim m_readerGate = new(1, 1);

    /// <summary>
    /// The writer-preference gate. A writer holds it for its whole turn, so readers arriving after the
    /// writer queue here rather than joining the reader group and extending it.
    /// </summary>
    private readonly SemaphoreSlim m_turnstile = new(1, 1);

    private int m_readers;
    private bool m_disposed;

    #endregion

    #region Read

    /// <summary>
    /// Takes the lock in shared mode. Several callers may hold it at once.
    /// </summary>
    public void EnterRead()
    {
        ThrowIfDisposed();

        m_turnstile.Wait();
        m_turnstile.Release();

        m_readerGate.Wait();
        try
        {
            if (m_readers == 0)
                m_writeGate.Wait();

            m_readers++;
        }
        finally
        {
            m_readerGate.Release();
        }
    }

    /// <summary>
    /// Takes the lock in shared mode without blocking a thread while it waits.
    /// </summary>
    public async ValueTask EnterReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await m_turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        m_turnstile.Release();

        await m_readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (m_readers == 0)
                await m_writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            m_readers++;
        }
        finally
        {
            m_readerGate.Release();
        }
    }

    /// <summary>
    /// Releases a shared hold. May be called from a different thread than the one that took it, which
    /// is the whole point of this type.
    /// </summary>
    public void ExitRead()
    {
        m_readerGate.Wait();
        try
        {
            m_readers--;

            if (m_readers == 0)
                m_writeGate.Release();
        }
        finally
        {
            m_readerGate.Release();
        }
    }

    #endregion

    #region Write

    /// <summary>
    /// Takes the lock in exclusive mode.
    /// </summary>
    public void EnterWrite()
    {
        ThrowIfDisposed();

        m_turnstile.Wait();

        try
        {
            m_writeGate.Wait();
        }
        catch
        {
            m_turnstile.Release();
            throw;
        }
    }

    /// <summary>
    /// Takes the lock in exclusive mode without blocking a thread while it waits.
    /// </summary>
    public async ValueTask EnterWriteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await m_turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await m_writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            m_turnstile.Release();
            throw;
        }
    }

    /// <summary>
    /// Releases an exclusive hold. May be called from a different thread than the one that took it.
    /// </summary>
    public void ExitWrite()
    {
        m_writeGate.Release();
        m_turnstile.Release();
    }

    #endregion

    #region Tools

    /// <summary>
    /// The number of callers currently holding the lock in shared mode. For diagnostics and for tests
    /// that have to observe the lock's state rather than infer it from timing.
    /// </summary>
    public int CurrentReaderCount => Volatile.Read(ref m_readers);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;

        m_writeGate.Dispose();
        m_readerGate.Dispose();
        m_turnstile.Dispose();
    }

    #endregion
}
