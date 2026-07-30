using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Cache;

/// <summary>
/// LRU (Least Recently Used) page cache for buffering frequently accessed pages.
/// Reduces disk I/O by keeping hot pages in memory.
/// </summary>
/// <remarks>
/// Simple LRU implementation - good for general workloads with low concurrency.
/// For high-concurrency scenarios, consider using <see cref="PageCacheShardedClock"/>.
/// </remarks>
public sealed class PageCacheLru : IPageCache
{
    #region Constants

    /// <summary>
    /// Provider key for LRU cache.
    /// </summary>
    public const string PROVIDER_KEY = "lru";

    /// <summary>
    /// How long <see cref="Dispose"/> waits for in-flight writes to finish before giving up on
    /// recycling their buffers.
    /// </summary>
    private static readonly TimeSpan DISPOSE_DRAIN_TIMEOUT = TimeSpan.FromSeconds(30);

    #endregion

    #region Fields

    private readonly IStorage m_storage;

    private readonly int m_maxPages;

    private readonly Dictionary<long, LinkedListNode<CachedPage>> m_cache;

    private readonly LinkedList<CachedPage> m_lruList;

    private readonly Lock m_lock = new();

    private readonly SemaphoreSlim m_asyncLock = new(1, 1);

    /// <summary>
    /// Number of <see cref="FlushAllAsync"/> writes currently handed to the storage.
    /// </summary>
    /// <remarks>
    /// A write in flight holds the page's <b>pooled</b> buffer, so nothing may return that array to the
    /// pool until it completes. Deliberately separate from the reference count, which also covers a page
    /// merely checked out by a caller.
    /// </remarks>
    private int m_writesInFlight;

    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new LRU page cache with the specified maximum size.
    /// </summary>
    /// <param name="storage">Underlying storage</param>
    /// <param name="maxPages">Maximum number of pages to cache</param>
    public PageCacheLru(IStorage storage, int maxPages = DatabaseConstants.DEFAULT_CACHE_SIZE)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (maxPages < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPages), "Cache must hold at least 1 page");

        m_storage = storage;
        m_maxPages = maxPages;
        m_cache = new Dictionary<long, LinkedListNode<CachedPage>>(maxPages);
        m_lruList = new LinkedList<CachedPage>();
    }

    #endregion

    #region Sync Operations

    /// <inheritdoc/>
    public CachedPage GetPage(long pageNumber)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();

            if (m_cache.TryGetValue(pageNumber, out var node))
            {
                // Move to front (most recently used)
                m_lruList.Remove(node);
                m_lruList.AddFirst(node);
                node.Value.IncrementReferenceCount();
                return node.Value;
            }

            // Need to load from storage
            return LoadPage(pageNumber);
        }
    }

    /// <inheritdoc/>
    public CachedPage CreatePage(long pageNumber)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();

            if (m_cache.ContainsKey(pageNumber))
                throw new InvalidOperationException($"Page {pageNumber} already exists in cache");

            EnsureCapacity();

            var page = new CachedPage(pageNumber, m_storage.PageSize);
            page.Data.Clear();
            page.MarkDirty();
            page.ReferenceCount = 1;

            var node = m_lruList.AddFirst(page);
            m_cache[pageNumber] = node;

            return page;
        }
    }

    /// <inheritdoc/>
    public void MarkDirty(long pageNumber)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();

            if (m_cache.TryGetValue(pageNumber, out var node))
            {
                node.Value.MarkDirty();
            }
        }
    }

    /// <inheritdoc/>
    public void ReleasePage(long pageNumber)
    {
        lock (m_lock)
        {
            if (m_cache.TryGetValue(pageNumber, out var node))
            {
                node.Value.DecrementReferenceCount();
            }
        }
    }

    /// <inheritdoc/>
    public void FlushAll()
    {
        lock (m_lock)
        {
            ThrowIfDisposed();
            FlushAllInternal();
        }
    }

    /// <summary>
    /// Flushes a specific dirty page to storage.
    /// </summary>
    public void FlushPage(long pageNumber)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();

            if (m_cache.TryGetValue(pageNumber, out var node) && node.Value.IsDirty)
            {
                m_storage.WritePage(node.Value.PageNumber, node.Value.ReadOnlyData);
                node.Value.ClearDirty();
            }
        }
    }

    /// <inheritdoc/>
    public void Evict(long pageNumber)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();

            if (m_cache.TryGetValue(pageNumber, out var node))
            {
                if (node.Value.ReferenceCount > 0)
                    throw new InvalidOperationException($"Cannot evict page {pageNumber}: page is pinned (ReferenceCount = {node.Value.ReferenceCount})");
                    
                EvictNode(node);
            }
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (m_lock)
        {
            ThrowIfDisposed();

            // Refuse on exactly the condition Evict refuses on, and before anything is flushed or
            // disposed, so a rejected Clear leaves the cache untouched. Disposing a CachedPage returns
            // its rented array to the pool, so doing it to a page someone else still holds - a caller,
            // or a write in flight - hands live memory to the next borrower.
            foreach (var page in m_lruList)
            {
                if (page.ReferenceCount > 0)
                    throw new InvalidOperationException(
                        $"Cannot clear page {page.PageNumber}: page is pinned (ReferenceCount = {page.ReferenceCount})");
            }

            FlushAllInternal();
            DiscardAllPages(keepPinnedBuffers: false);
        }
    }

    /// <summary>
    /// Empties the cache. With <paramref name="keepPinnedBuffers"/> the pooled array of a page that is
    /// still pinned is <b>not</b> returned to the pool - the page is dropped and its buffer left to the
    /// garbage collector. Leaking a rented array costs a reuse; returning one a write is still reading
    /// from corrupts the next borrower's page.
    /// </summary>
    private void DiscardAllPages(bool keepPinnedBuffers)
    {
        foreach (var page in m_lruList)
        {
            if (!(keepPinnedBuffers && page.ReferenceCount > 0))
                page.Dispose();
        }

        m_cache.Clear();
        m_lruList.Clear();
    }

    #endregion

    #region Async Operations

    /// <inheritdoc/>
    public async ValueTask<CachedPage> GetPageAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (m_cache.TryGetValue(pageNumber, out var node))
            {
                // Move to front (most recently used)
                m_lruList.Remove(node);
                m_lruList.AddFirst(node);
                node.Value.IncrementReferenceCount();
                return node.Value;
            }

            // Need to load from storage asynchronously
            return await LoadPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_asyncLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<CachedPage> CreatePageAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (m_cache.ContainsKey(pageNumber))
                throw new InvalidOperationException($"Page {pageNumber} already exists in cache");

            await EnsureCapacityAsync(cancellationToken).ConfigureAwait(false);

            var page = new CachedPage(pageNumber, m_storage.PageSize);
            page.Data.Clear();
            page.MarkDirty();
            page.ReferenceCount = 1;

            var node = m_lruList.AddFirst(page);
            m_cache[pageNumber] = node;

            return page;
        }
        finally
        {
            m_asyncLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask EvictAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (m_cache.TryGetValue(pageNumber, out var node))
            {
                if (node.Value.ReferenceCount > 0)
                    throw new InvalidOperationException($"Cannot evict page {pageNumber}: page is pinned (ReferenceCount = {node.Value.ReferenceCount})");
                    
                await EvictNodeAsync(node, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            m_asyncLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask FlushAllAsync(CancellationToken cancellationToken = default)
    {
        List<CachedPage> dirtyPages;
        
        await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            dirtyPages = m_lruList.Where(p => p.IsDirty).ToList();
            
            foreach (var page in dirtyPages)
            {
                page.IncrementReferenceCount();
            }
        }
        finally
        {
            m_asyncLock.Release();
        }

        // Mark the whole write batch as in flight: every page in it holds a pooled buffer the storage is
        // reading from, and Dispose has to wait for that rather than recycle it.
        Interlocked.Increment(ref m_writesInFlight);
        try
        {
            foreach (var page in dirtyPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!page.IsDisposed)
                {
                    await m_storage.WritePageAsync(page.PageNumber, page.Memory, cancellationToken).ConfigureAwait(false);
                    page.ClearDirty();
                }
            }

            await m_storage.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref m_writesInFlight);

            await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var page in dirtyPages)
                {
                    if (m_cache.ContainsKey(page.PageNumber))
                    {
                        page.DecrementReferenceCount();
                    }
                }
            }
            finally
            {
                m_asyncLock.Release();
            }
        }
    }

    /// <summary>
    /// Flushes a specific dirty page to storage asynchronously.
    /// </summary>
    public async ValueTask FlushPageAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        CachedPage? pageToFlush = null;
        
        await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (m_cache.TryGetValue(pageNumber, out var node) && node.Value.IsDirty)
            {
                pageToFlush = node.Value;
                pageToFlush.IncrementReferenceCount();
            }
        }
        finally
        {
            m_asyncLock.Release();
        }

        if (pageToFlush == null)
            return;

        try
        {
            if (!pageToFlush.IsDisposed)
            {
                await m_storage.WritePageAsync(pageToFlush.PageNumber, pageToFlush.Memory, cancellationToken).ConfigureAwait(false);
                pageToFlush.ClearDirty();
            }
        }
        finally
        {
            await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_cache.ContainsKey(pageNumber))
                {
                    pageToFlush.DecrementReferenceCount();
                }
            }
            finally
            {
                m_asyncLock.Release();
            }
        }
    }

    #endregion

    #region Private Sync Helpers

    private void FlushAllInternal()
    {
        foreach (var page in m_lruList.Where(p => p.IsDirty))
        {
            m_storage.WritePage(page.PageNumber, page.ReadOnlyData);
            page.ClearDirty();
        }

        m_storage.Flush();
    }

    private CachedPage LoadPage(long pageNumber)
    {
        EnsureCapacity();

        var page = new CachedPage(pageNumber, m_storage.PageSize);
        m_storage.ReadPage(pageNumber, page.Data);
        page.ReferenceCount = 1;

        var node = m_lruList.AddFirst(page);
        m_cache[pageNumber] = node;

        return page;
    }

    private void EnsureCapacity()
    {
        while (m_cache.Count >= m_maxPages)
        {
            var nodeToEvict = m_lruList.Last;

            while (nodeToEvict != null && nodeToEvict.Value.ReferenceCount > 0)
            {
                nodeToEvict = nodeToEvict.Previous;
            }

            if (nodeToEvict == null)
                throw new InvalidOperationException("Cache is full and all pages are pinned");

            EvictNode(nodeToEvict);
        }
    }

    private void EvictNode(LinkedListNode<CachedPage> node)
    {
        var page = node.Value;

        if (page.IsDirty)
        {
            m_storage.WritePage(page.PageNumber, page.ReadOnlyData);
        }

        m_cache.Remove(page.PageNumber);
        m_lruList.Remove(node);
        page.Dispose();
    }

    #endregion

    #region Private Async Helpers

    private async ValueTask<CachedPage> LoadPageAsync(long pageNumber, CancellationToken cancellationToken)
    {
        await EnsureCapacityAsync(cancellationToken).ConfigureAwait(false);

        var page = new CachedPage(pageNumber, m_storage.PageSize);
        await m_storage.ReadPageAsync(pageNumber, page.Memory, cancellationToken).ConfigureAwait(false);
        page.ReferenceCount = 1;

        var node = m_lruList.AddFirst(page);
        m_cache[pageNumber] = node;

        return page;
    }

    private async ValueTask EnsureCapacityAsync(CancellationToken cancellationToken)
    {
        while (m_cache.Count >= m_maxPages)
        {
            var nodeToEvict = m_lruList.Last;

            while (nodeToEvict != null && nodeToEvict.Value.ReferenceCount > 0)
            {
                nodeToEvict = nodeToEvict.Previous;
            }

            if (nodeToEvict == null)
                throw new InvalidOperationException("Cache is full and all pages are pinned");

            await EvictNodeAsync(nodeToEvict, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask EvictNodeAsync(LinkedListNode<CachedPage> node, CancellationToken cancellationToken)
    {
        var page = node.Value;

        if (page.IsDirty)
        {
            await m_storage.WritePageAsync(page.PageNumber, page.Memory, cancellationToken).ConfigureAwait(false);
        }

        m_cache.Remove(page.PageNumber);
        m_lruList.Remove(node);
        page.Dispose();
    }

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

        // Dispose is the only production caller of Clear, so it cannot inherit Clear's refusal -
        // shutting down has to succeed. What it must not do is throw away a write that is still in
        // flight, so wait for the storage to finish with the buffers first. Outside m_lock, because the
        // flush's own bookkeeping runs under m_asyncLock; and bounded, because a Dispose that can hang
        // for ever is its own defect.
        SpinWait.SpinUntil(() => Volatile.Read(ref m_writesInFlight) == 0, DISPOSE_DRAIN_TIMEOUT);

        lock (m_lock)
        {
            if (m_disposed)
                return;

            FlushAllInternal();

            // Pinned pages keep their pooled buffer rather than returning it. In the ordinary case
            // nothing is pinned by now; if the drain above timed out, a leak is the safe answer.
            DiscardAllPages(keepPinnedBuffers: true);

            m_asyncLock.Dispose();
            m_disposed = true;
        }
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            lock (m_lock)
            {
                return m_cache.Count;
            }
        }
    }

    /// <inheritdoc/>
    public int DirtyCount
    {
        get
        {
            lock (m_lock)
            {
                return m_lruList.Count(p => p.IsDirty);
            }
        }
    }

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    #endregion
}
