using System.Runtime.CompilerServices;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Cache;

/// <summary>
/// High-performance page cache using Clock (Second Chance) algorithm with sharding.
/// Provides better concurrency than simple LRU and resistance to scan patterns.
/// </summary>
/// <remarks>
/// Recommended for high-concurrency and scan-heavy workloads.
/// For simpler workloads with low concurrency, <see cref="PageCacheLru"/> may be sufficient.
/// </remarks>
public sealed class PageCacheShardedClock : IPageCache, IAsyncDisposable
{
    #region Constants

    private const int DEFAULT_SHARD_COUNT = 16;
    private const int MIN_PAGES_PER_SHARD = 4;

    /// <summary>
    /// How long <see cref="Dispose"/> waits for in-flight writes to finish before giving up on
    /// recycling their buffers.
    /// </summary>
    private static readonly TimeSpan DISPOSE_DRAIN_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Provider key for sharded clock cache.
    /// </summary>
    public const string PROVIDER_KEY = "clock";

    #endregion

    #region Nested Types

    private sealed class CacheShard : IDisposable, IAsyncDisposable
    {
        #region Fields

        private readonly IStorage m_storage;
        private readonly int m_capacity;
        private readonly Lock m_lock = new();
        private readonly SemaphoreSlim m_asyncLock = new(1, 1);
        
        // Clock data structures
        private readonly CachedPage?[] m_pages;
        private readonly Dictionary<long, int> m_pageIndex;
        private int m_clockHand;
        private int m_count;
        private int m_firstFreeSlot;

        /// <summary>
        /// Number of <see cref="FlushAllAsync"/> writes currently handed to the storage.
        /// </summary>
        /// <remarks>
        /// A write in flight holds the page's <b>pooled</b> buffer, so nothing may return that array to
        /// the pool until it completes. This is deliberately separate from the reference count: the
        /// reference count also covers a page simply checked out by a caller, which is a different
        /// question and never a reason to make <see cref="Dispose"/> wait.
        /// </remarks>
        private int m_writesInFlight;
        private bool m_disposed;

        #endregion

        #region Constructors

        public CacheShard(IStorage storage, int capacity)
        {
            m_storage = storage;
            m_capacity = capacity;
            m_pages = new CachedPage?[capacity];
            m_pageIndex = new Dictionary<long, int>(capacity);
            m_clockHand = 0;
            m_count = 0;
            m_firstFreeSlot = 0;
        }

        #endregion

        #region Sync Operations

        public CachedPage GetPage(long pageNumber)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();

                if (m_pageIndex.TryGetValue(pageNumber, out int index))
                {
                    var page = m_pages[index]!;
                    page.Referenced = true;
                    page.IncrementReferenceCount();
                    return page;
                }

                return LoadPage(pageNumber);
            }
        }

        public CachedPage CreatePage(long pageNumber)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();

                if (m_pageIndex.ContainsKey(pageNumber))
                    throw new InvalidOperationException($"Page {pageNumber} already exists in cache");

                int slot = FindSlotForNewPage();

                var page = new CachedPage(pageNumber, m_storage.PageSize);
                page.Data.Clear();
                page.MarkDirty();
                page.ReferenceCount = 1;
                page.Referenced = true;

                m_pages[slot] = page;
                m_pageIndex[pageNumber] = slot;
                m_count++;

                return page;
            }
        }

        public void MarkDirty(long pageNumber)
        {
            lock (m_lock)
            {
                if (m_pageIndex.TryGetValue(pageNumber, out int index))
                {
                    m_pages[index]?.MarkDirty();
                }
            }
        }

        public void ReleasePage(long pageNumber)
        {
            lock (m_lock)
            {
                if (m_pageIndex.TryGetValue(pageNumber, out int index))
                {
                    m_pages[index]?.DecrementReferenceCount();
                }
            }
        }

        public void Evict(long pageNumber)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();

                if (m_pageIndex.TryGetValue(pageNumber, out int index))
                {
                    var page = m_pages[index];
                    if (page != null)
                    {
                        if (page.ReferenceCount > 0)
                            throw new InvalidOperationException($"Cannot evict pinned page {pageNumber}");

                        EvictSlot(index);
                    }
                }
            }
        }

        public void FlushAll()
        {
            lock (m_lock)
            {
                ThrowIfDisposed();
                FlushAllInternal();
            }
        }

        public void Clear()
        {
            lock (m_lock)
            {
                // Refuse on exactly the condition Evict refuses on, and refuse BEFORE anything is
                // flushed or disposed, so a rejected Clear leaves the shard untouched rather than
                // half emptied. Disposing a CachedPage returns its rented array to the pool, so
                // doing it to a page someone else still holds - a caller, or a write in flight -
                // hands live memory to the next borrower.
                for (int i = 0; i < m_capacity; i++)
                {
                    var page = m_pages[i];
                    if (page != null && page.ReferenceCount > 0)
                        throw new InvalidOperationException($"Cannot clear pinned page {page.PageNumber}");
                }

                FlushAllInternal();
                DiscardAllPages(keepPinnedBuffers: false);
            }
        }

        /// <summary>
        /// Empties the shard. With <paramref name="keepPinnedBuffers"/> the pooled array of a page that
        /// is still pinned is <b>not</b> returned to the pool - the page is dropped and its buffer left
        /// to the garbage collector. Leaking a rented array costs a reuse; returning one that a write is
        /// still reading from corrupts the next borrower's page.
        /// </summary>
        private void DiscardAllPages(bool keepPinnedBuffers)
        {
            for (int i = 0; i < m_capacity; i++)
            {
                var page = m_pages[i];
                if (page != null && !(keepPinnedBuffers && page.ReferenceCount > 0))
                    page.Dispose();

                m_pages[i] = null;
            }

            m_pageIndex.Clear();
            m_count = 0;
            m_clockHand = 0;
            m_firstFreeSlot = 0;
        }

        #endregion

        #region Async Operations

        public async ValueTask<CachedPage> GetPageAsync(long pageNumber, CancellationToken cancellationToken)
        {
            await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();

                if (m_pageIndex.TryGetValue(pageNumber, out int index))
                {
                    var page = m_pages[index]!;
                    page.Referenced = true;
                    page.IncrementReferenceCount();
                    return page;
                }

                return await LoadPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_asyncLock.Release();
            }
        }

        public async ValueTask<CachedPage> CreatePageAsync(long pageNumber, CancellationToken cancellationToken)
        {
            await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();

                if (m_pageIndex.ContainsKey(pageNumber))
                    throw new InvalidOperationException($"Page {pageNumber} already exists in cache");

                int slot = await FindSlotForNewPageAsync(cancellationToken).ConfigureAwait(false);

                var page = new CachedPage(pageNumber, m_storage.PageSize);
                page.Data.Clear();
                page.MarkDirty();
                page.ReferenceCount = 1;
                page.Referenced = true;

                m_pages[slot] = page;
                m_pageIndex[pageNumber] = slot;
                m_count++;

                return page;
            }
            finally
            {
                m_asyncLock.Release();
            }
        }

        public async ValueTask EvictAsync(long pageNumber, CancellationToken cancellationToken)
        {
            await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();

                if (m_pageIndex.TryGetValue(pageNumber, out int index))
                {
                    var page = m_pages[index];
                    if (page != null)
                    {
                        if (page.ReferenceCount > 0)
                            throw new InvalidOperationException($"Cannot evict pinned page {pageNumber}");

                        await EvictSlotAsync(index, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_asyncLock.Release();
            }
        }

        public async ValueTask FlushAllAsync(CancellationToken cancellationToken)
        {
            List<CachedPage>? dirtyPages = null;

            await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                
                for (int i = 0; i < m_capacity; i++)
                {
                    var page = m_pages[i];
                    if (page != null && page.IsDirty)
                    {
                        dirtyPages ??= new List<CachedPage>();
                        page.IncrementReferenceCount();
                        dirtyPages.Add(page);
                    }
                }
            }
            finally
            {
                m_asyncLock.Release();
            }

            if (dirtyPages == null)
                return;

            // Mark the whole write batch as in flight: every page in it holds a pooled buffer the
            // storage is reading from, and Dispose has to wait for that rather than recycle it.
            Interlocked.Increment(ref m_writesInFlight);
            try
            {
                foreach (var page in dirtyPages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!page.IsDisposed)
                    {
                        await m_storage.WritePageAsync(page.PageNumber, page.Memory, cancellationToken)
                            .ConfigureAwait(false);
                        page.ClearDirty();
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref m_writesInFlight);

                await m_asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    foreach (var page in dirtyPages)
                    {
                        page.DecrementReferenceCount();
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

        private CachedPage LoadPage(long pageNumber)
        {
            int slot = FindSlotForNewPage();

            var page = new CachedPage(pageNumber, m_storage.PageSize);
            m_storage.ReadPage(pageNumber, page.Data);
            page.ReferenceCount = 1;
            page.Referenced = true;

            m_pages[slot] = page;
            m_pageIndex[pageNumber] = slot;
            m_count++;

            return page;
        }

        private int FindSlotForNewPage()
        {
            if (m_count < m_capacity)
            {
                for (int i = m_firstFreeSlot; i < m_capacity; i++)
                {
                    if (m_pages[i] == null)
                    {
                        m_firstFreeSlot = i + 1;
                        return i;
                    }
                }
                for (int i = 0; i < m_firstFreeSlot; i++)
                {
                    if (m_pages[i] == null)
                    {
                        m_firstFreeSlot = i + 1;
                        return i;
                    }
                }
            }

            int maxIterations = m_capacity * 2;

            for (int iterations = 0; iterations < maxIterations; iterations++)
            {
                var page = m_pages[m_clockHand];

                if (page != null && page.ReferenceCount == 0)
                {
                    if (page.Referenced)
                    {
                        page.Referenced = false;
                    }
                    else
                    {
                        int slot = m_clockHand;
                        EvictSlot(slot);
                        m_clockHand = (m_clockHand + 1) % m_capacity;
                        return slot;
                    }
                }

                m_clockHand = (m_clockHand + 1) % m_capacity;
            }

            throw new InvalidOperationException("Cache is full and all pages are pinned");
        }

        private void EvictSlot(int slot)
        {
            var page = m_pages[slot];
            if (page != null)
            {
                if (page.IsDirty)
                {
                    m_storage.WritePage(page.PageNumber, page.ReadOnlyData);
                }

                m_pageIndex.Remove(page.PageNumber);
                page.Dispose();
                m_pages[slot] = null;
                m_count--;
                
                if (slot < m_firstFreeSlot)
                    m_firstFreeSlot = slot;
            }
        }

        private void FlushAllInternal()
        {
            for (int i = 0; i < m_capacity; i++)
            {
                var page = m_pages[i];
                if (page != null && page.IsDirty)
                {
                    m_storage.WritePage(page.PageNumber, page.ReadOnlyData);
                    page.ClearDirty();
                }
            }
        }

        #endregion

        #region Private Async Helpers

        private async ValueTask<CachedPage> LoadPageAsync(long pageNumber, CancellationToken cancellationToken)
        {
            int slot = await FindSlotForNewPageAsync(cancellationToken).ConfigureAwait(false);

            var page = new CachedPage(pageNumber, m_storage.PageSize);
            
            // Yield before JS interop to allow browser event loop to process
            await Task.Yield();
            
            await m_storage.ReadPageAsync(pageNumber, page.Memory, cancellationToken).ConfigureAwait(false);
            page.ReferenceCount = 1;
            page.Referenced = true;

            m_pages[slot] = page;
            m_pageIndex[pageNumber] = slot;
            m_count++;

            return page;
        }

        private async ValueTask<int> FindSlotForNewPageAsync(CancellationToken cancellationToken)
        {
            if (m_count < m_capacity)
            {
                for (int i = m_firstFreeSlot; i < m_capacity; i++)
                {
                    if (m_pages[i] == null)
                    {
                        m_firstFreeSlot = i + 1;
                        return i;
                    }
                }
                for (int i = 0; i < m_firstFreeSlot; i++)
                {
                    if (m_pages[i] == null)
                    {
                        m_firstFreeSlot = i + 1;
                        return i;
                    }
                }
            }

            int maxIterations = m_capacity * 2;

            for (int iterations = 0; iterations < maxIterations; iterations++)
            {
                var page = m_pages[m_clockHand];

                if (page != null && page.ReferenceCount == 0)
                {
                    if (page.Referenced)
                    {
                        page.Referenced = false;
                    }
                    else
                    {
                        int slot = m_clockHand;
                        await EvictSlotAsync(slot, cancellationToken).ConfigureAwait(false);
                        m_clockHand = (m_clockHand + 1) % m_capacity;
                        return slot;
                    }
                }

                m_clockHand = (m_clockHand + 1) % m_capacity;
            }

            throw new InvalidOperationException("Cache is full and all pages are pinned");
        }

        private async ValueTask EvictSlotAsync(int slot, CancellationToken cancellationToken)
        {
            var page = m_pages[slot];
            if (page != null)
            {
                if (page.IsDirty)
                {
                    // Yield before JS interop to allow browser event loop to process
                    await Task.Yield();
                    
                    await m_storage.WritePageAsync(page.PageNumber, page.Memory, cancellationToken)
                        .ConfigureAwait(false);
                }

                m_pageIndex.Remove(page.PageNumber);
                page.Dispose();
                m_pages[slot] = null;
                m_count--;
                
                if (slot < m_firstFreeSlot)
                    m_firstFreeSlot = slot;
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (m_disposed)
                return;

            // Dispose is the only production caller of Clear, so it cannot simply inherit Clear's
            // refusal - shutting down has to succeed. What it must not do is throw away a write that is
            // still in flight, so wait for the storage to finish with the buffers first. The wait is
            // outside m_lock, because the flush's own bookkeeping runs under m_asyncLock and would
            // otherwise have to queue behind us; and it is bounded, because a Dispose that can hang for
            // ever is its own defect.
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

        /// <summary>
        /// The same shutdown without a single synchronous storage call.
        /// </summary>
        /// <remarks>
        /// <see cref="Dispose"/> ends in <c>FlushAllInternal</c>, which writes every dirty page through
        /// <c>IStorage.WritePage</c> - so a storage that has no synchronous write (a browser's IndexedDb)
        /// could never be closed. The flush happens through <see cref="FlushAllAsync"/> here, and it runs
        /// <b>before</b> the lock is taken rather than inside it, because nothing may be awaited under
        /// <c>m_lock</c>.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
                return;

            await FlushAllAsync(CancellationToken.None).ConfigureAwait(false);

            // The drain is still needed: FlushAllAsync hands buffers to the storage and returns before
            // the storage is necessarily finished with them.
            SpinWait.SpinUntil(() => Volatile.Read(ref m_writesInFlight) == 0, DISPOSE_DRAIN_TIMEOUT);

            lock (m_lock)
            {
                if (m_disposed)
                    return;

                DiscardAllPages(keepPinnedBuffers: true);

                m_asyncLock.Dispose();
                m_disposed = true;
            }
        }

        #endregion

        #region Properties

        public int Count => Volatile.Read(ref m_count);

        public int DirtyCount
        {
            get
            {
                lock (m_lock)
                {
                    int count = 0;
                    for (int i = 0; i < m_capacity; i++)
                    {
                        if (m_pages[i]?.IsDirty == true)
                            count++;
                    }
                    return count;
                }
            }
        }

        #endregion
    }

    #endregion

    #region Fields

    private readonly IStorage m_storage;
    private readonly CacheShard[] m_shards;
    private readonly int m_shardMask;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new sharded clock cache.
    /// </summary>
    /// <param name="storage">Underlying storage</param>
    /// <param name="maxPages">Maximum number of pages to cache</param>
    /// <param name="shardCount">Number of shards (default: 16, must be power of 2)</param>
    public PageCacheShardedClock(IStorage storage, int maxPages = DatabaseConstants.DEFAULT_CACHE_SIZE, int? shardCount = null)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (maxPages < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPages), "Cache must hold at least 1 page");

        m_storage = storage;
        
        int actualShardCount = shardCount ?? Math.Min(DEFAULT_SHARD_COUNT, Math.Max(1, maxPages / MIN_PAGES_PER_SHARD));
        
        actualShardCount = RoundUpToPowerOf2(actualShardCount);
        
        m_shardMask = actualShardCount - 1;
        m_shards = new CacheShard[actualShardCount];

        int pagesPerShard = Math.Max(1, maxPages / actualShardCount);
        
        for (int i = 0; i < actualShardCount; i++)
        {
            m_shards[i] = new CacheShard(storage, pagesPerShard);
        }
    }

    #endregion

    #region Sync Operations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CacheShard GetShard(long pageNumber)
    {
        return m_shards[pageNumber & m_shardMask];
    }

    /// <inheritdoc/>
    public CachedPage GetPage(long pageNumber)
    {
        ThrowIfDisposed();
        return GetShard(pageNumber).GetPage(pageNumber);
    }

    /// <inheritdoc/>
    public CachedPage CreatePage(long pageNumber)
    {
        ThrowIfDisposed();
        return GetShard(pageNumber).CreatePage(pageNumber);
    }

    /// <inheritdoc/>
    public void MarkDirty(long pageNumber)
    {
        ThrowIfDisposed();
        GetShard(pageNumber).MarkDirty(pageNumber);
    }

    /// <inheritdoc/>
    public void ReleasePage(long pageNumber)
    {
        GetShard(pageNumber).ReleasePage(pageNumber);
    }

    /// <inheritdoc/>
    public void Evict(long pageNumber)
    {
        ThrowIfDisposed();
        GetShard(pageNumber).Evict(pageNumber);
    }

    /// <inheritdoc/>
    public void FlushAll()
    {
        ThrowIfDisposed();

        foreach (var shard in m_shards)
        {
            shard.FlushAll();
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        ThrowIfDisposed();

        foreach (var shard in m_shards)
        {
            shard.Clear();
        }
    }

    #endregion

    #region Async Operations

    /// <inheritdoc/>
    public async ValueTask<CachedPage> GetPageAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await GetShard(pageNumber).GetPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<CachedPage> CreatePageAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await GetShard(pageNumber).CreatePageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask EvictAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await GetShard(pageNumber).EvictAsync(pageNumber, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask FlushAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var tasks = new ValueTask[m_shards.Length];
        for (int i = 0; i < m_shards.Length; i++)
        {
            tasks[i] = m_shards[i].FlushAllAsync(cancellationToken);
        }

        foreach (var task in tasks)
        {
            await task.ConfigureAwait(false);
        }
    }

    #endregion

    #region Tools

    private static int RoundUpToPowerOf2(int value)
    {
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
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
            m_disposed = true;

            foreach (var shard in m_shards)
            {
                shard.Dispose();
            }
        }
    }

    /// <summary>
    /// Closes every shard without a synchronous storage call, so a database on a storage that has none
    /// can be closed at all.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (m_disposed)
            return;

        m_disposed = true;

        foreach (var shard in m_shards)
        {
            await shard.DisposeAsync().ConfigureAwait(false);
        }
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            int total = 0;
            foreach (var shard in m_shards)
            {
                total += shard.Count;
            }
            return total;
        }
    }

    /// <inheritdoc/>
    public int DirtyCount
    {
        get
        {
            int total = 0;
            foreach (var shard in m_shards)
            {
                total += shard.DirtyCount;
            }
            return total;
        }
    }

    /// <summary>
    /// Gets the number of shards.
    /// </summary>
    public int ShardCount => m_shards.Length;

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    #endregion
}
