namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// Interface for page cache implementations.
/// Implement this interface to provide custom caching strategies.
/// </summary>
public interface IPageCache : IProvider, IDisposable
{
    #region Functions

    /// <summary>
    /// Gets a page from the cache, loading from storage if necessary.
    /// </summary>
    Cache.CachedPage GetPage(long pageNumber);

    /// <summary>
    /// Creates a new page in the cache (for newly allocated pages).
    /// </summary>
    Cache.CachedPage CreatePage(long pageNumber);

    /// <summary>
    /// Marks a page as dirty (modified).
    /// </summary>
    void MarkDirty(long pageNumber);

    /// <summary>
    /// Releases a reference to a page.
    /// </summary>
    void ReleasePage(long pageNumber);

    /// <summary>
    /// Evicts a specific page from the cache.
    /// </summary>
    void Evict(long pageNumber);

    /// <summary>
    /// Flushes all dirty pages to storage.
    /// </summary>
    void FlushAll();

    /// <summary>
    /// Flushes all dirty pages to storage asynchronously.
    /// </summary>
    ValueTask FlushAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all pages from the cache.
    /// </summary>
    void Clear();

    #endregion

    #region Properties

    /// <summary>
    /// Gets the current number of cached pages.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the number of dirty pages in the cache.
    /// </summary>
    int DirtyCount { get; }

    /// <summary>
    /// Gets the unique provider key identifying this cache implementation.
    /// Used for validation when opening existing databases.
    /// </summary>
    /// <example>
    /// "lru" for LRU cache, "clock" for sharded clock cache.
    /// </example>
    string ProviderKey { get; }

    #endregion
}
