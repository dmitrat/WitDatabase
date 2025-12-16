using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Cache;

/// <summary>
/// LRU (Least Recently Used) page cache for buffering frequently accessed pages.
/// Reduces disk I/O by keeping hot pages in memory.
/// </summary>
/// <remarks>
/// Simple LRU implementation - good for general workloads with low concurrency.
/// For high-concurrency scenarios, consider using <see cref="ShardedClockCache"/>.
/// </remarks>
public sealed class LruPageCache : IPageCache
{
    #region Fields

    private readonly IStorage m_storage;

    private readonly int m_maxPages;

    private readonly Dictionary<long, LinkedListNode<CachedPage>> m_cache;

    private readonly LinkedList<CachedPage> m_lruList;

    private readonly Lock m_lock = new();

    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new LRU page cache with the specified maximum size.
    /// </summary>
    /// <param name="storage">Underlying storage</param>
    /// <param name="maxPages">Maximum number of pages to cache</param>
    public LruPageCache(IStorage storage, int maxPages = DatabaseConstants.DEFAULT_CACHE_SIZE)
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

    #region Functions

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

    /// <inheritdoc/>
    public async ValueTask FlushAllAsync(CancellationToken cancellationToken = default)
    {
        List<CachedPage> dirtyPages;
        
        lock (m_lock)
        {
            ThrowIfDisposed();
            dirtyPages = m_lruList.Where(p => p.IsDirty).ToList();
            
            foreach (var page in dirtyPages)
            {
                page.IncrementReferenceCount();
            }
        }

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
            lock (m_lock)
            {
                foreach (var page in dirtyPages)
                {
                    if (m_cache.ContainsKey(page.PageNumber))
                    {
                        page.DecrementReferenceCount();
                    }
                }
            }
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

    /// <summary>
    /// Flushes a specific dirty page to storage asynchronously.
    /// </summary>
    public async ValueTask FlushPageAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        CachedPage? pageToFlush = null;
        
        lock (m_lock)
        {
            ThrowIfDisposed();

            if (m_cache.TryGetValue(pageNumber, out var node) && node.Value.IsDirty)
            {
                pageToFlush = node.Value;
                pageToFlush.IncrementReferenceCount();
            }
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
            lock (m_lock)
            {
                if (m_cache.ContainsKey(pageNumber))
                {
                    pageToFlush.DecrementReferenceCount();
                }
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
            FlushAllInternal();

            foreach (var page in m_lruList)
            {
                page.Dispose();
            }

            m_cache.Clear();
            m_lruList.Clear();
        }
    }

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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!m_disposed)
        {
            lock (m_lock)
            {
                if (!m_disposed)
                {
                    Clear();
                    m_disposed = true;
                }
            }
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

    #endregion
}
