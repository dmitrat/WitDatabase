using System.Runtime.CompilerServices;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Tree;

namespace OutWit.Database.Core.Stores;

/// <summary>
/// Key-value store implementation backed by B+Tree.
/// Implements IKeyValueStore for unified storage engine interface.
/// </summary>
public sealed class StoreBTree : IKeyValueStore, IKeyValueStoreStatistics, IAsyncDisposable
{
    #region Constants

    /// <summary>
    /// Provider key for B-Tree store.
    /// </summary>
    public const string PROVIDER_KEY = "btree";

    #endregion

    #region Fields

    private readonly IStorage? m_storage;
    private readonly PageManager m_pageManager;
    private readonly BTree m_tree;
    private readonly bool m_ownsStorage;
    private readonly bool m_ownsPageManager;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new BTreeStore with file storage.
    /// For async initialization (e.g., WASM), use <see cref="CreateAsync(string, int, int, CancellationToken)"/> instead.
    /// </summary>
    /// <param name="filePath">Path to the database file.</param>
    /// <param name="pageSize">Page size in bytes (default 4096).</param>
    /// <param name="cacheSize">Number of pages to cache (default 1000).</param>
    public StoreBTree(string filePath, int pageSize = 4096, int cacheSize = 1000)
        : this(new StorageFile(filePath, pageSize), cacheSize, ownsStorage: true)
    {
    }

    /// <summary>
    /// Creates a new BTreeStore with custom storage.
    /// For async initialization (e.g., WASM), use <see cref="CreateAsync(IStorage, int, bool, CancellationToken)"/> instead.
    /// </summary>
    /// <param name="storage">Storage implementation.</param>
    /// <param name="cacheSize">Number of pages to cache.</param>
    /// <param name="ownsStorage">If true, disposes the storage when this store is disposed.</param>
    public StoreBTree(IStorage storage, int cacheSize = 1000, bool ownsStorage = true)
        : this(storage, cacheSize, ownsStorage, providerMetadata: null)
    {
    }

    /// <summary>
    /// Creates a new BTreeStore with custom storage and provider metadata.
    /// For async initialization (e.g., WASM), use <see cref="CreateAsync(IStorage, int, bool, ProviderMetadata?, CancellationToken)"/> instead.
    /// </summary>
    /// <param name="storage">Storage implementation.</param>
    /// <param name="cacheSize">Number of pages to cache.</param>
    /// <param name="ownsStorage">If true, disposes the storage when this store is disposed.</param>
    /// <param name="providerMetadata">Provider metadata for new databases.</param>
    public StoreBTree(IStorage storage, int cacheSize, bool ownsStorage, ProviderMetadata? providerMetadata)
    {
        m_storage = storage ?? throw new ArgumentNullException(nameof(storage));
        m_ownsStorage = ownsStorage;
        m_ownsPageManager = true;
        
        var cache = new PageCacheShardedClock(storage, cacheSize);
        m_pageManager = new PageManager(m_storage, cache, providerMetadata);
        
        // Use schema root page as B+Tree root, or create new tree
        var header = m_pageManager.GetHeader();
        uint rootPage = header.SchemaRootPage;
        
        m_tree = new BTree(m_pageManager, rootPage);
    }

    /// <summary>
    /// Creates a BTreeStore with an existing PageManager.
    /// </summary>
    /// <param name="pageManager">The page manager to use.</param>
    /// <param name="rootPageNumber">Root page number (0 to create new tree).</param>
    public StoreBTree(PageManager pageManager, uint rootPageNumber = 0)
    {
        m_pageManager = pageManager ?? throw new ArgumentNullException(nameof(pageManager));
        m_storage = null;
        m_ownsStorage = false;
        m_ownsPageManager = false;
        m_tree = new BTree(m_pageManager, rootPageNumber);
    }

    /// <summary>
    /// Private constructor for async factory pattern.
    /// </summary>
    private StoreBTree(
        IStorage? storage, 
        PageManager pageManager, 
        BTree tree, 
        bool ownsStorage, 
        bool ownsPageManager)
    {
        m_storage = storage;
        m_pageManager = pageManager;
        m_tree = tree;
        m_ownsStorage = ownsStorage;
        m_ownsPageManager = ownsPageManager;
    }

    #endregion

    #region Static Factory Methods

    /// <summary>
    /// Creates a new BTreeStore with file storage asynchronously.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="filePath">Path to the database file.</param>
    /// <param name="pageSize">Page size in bytes (default 4096).</param>
    /// <param name="cacheSize">Number of pages to cache (default 1000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An initialized BTreeStore.</returns>
    public static async ValueTask<StoreBTree> CreateAsync(
        string filePath, 
        int pageSize = 4096, 
        int cacheSize = 1000,
        CancellationToken cancellationToken = default)
    {
        var storage = new StorageFile(filePath, pageSize);
        return await CreateAsync(storage, cacheSize, ownsStorage: true, providerMetadata: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new BTreeStore with custom storage asynchronously.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="storage">Storage implementation.</param>
    /// <param name="cacheSize">Number of pages to cache.</param>
    /// <param name="ownsStorage">If true, disposes the storage when this store is disposed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An initialized BTreeStore.</returns>
    public static ValueTask<StoreBTree> CreateAsync(
        IStorage storage, 
        int cacheSize = 1000, 
        bool ownsStorage = true,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(storage, cacheSize, ownsStorage, providerMetadata: null, cancellationToken);
    }

    /// <summary>
    /// Creates a new BTreeStore with custom storage and provider metadata asynchronously.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="storage">Storage implementation.</param>
    /// <param name="cacheSize">Number of pages to cache.</param>
    /// <param name="ownsStorage">If true, disposes the storage when this store is disposed.</param>
    /// <param name="providerMetadata">Provider metadata for new databases.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An initialized BTreeStore.</returns>
    public static async ValueTask<StoreBTree> CreateAsync(
        IStorage storage, 
        int cacheSize, 
        bool ownsStorage, 
        ProviderMetadata? providerMetadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        
        var cache = new PageCacheShardedClock(storage, cacheSize);
        var pageManager = await PageManager.CreateAsync(storage, cache, providerMetadata, cancellationToken)
            .ConfigureAwait(false);
        
        // Use schema root page as B+Tree root, or create new tree
        var header = pageManager.GetHeader();
        uint rootPage = header.SchemaRootPage;
        
        // Validate root page - if it's beyond total pages or 0, create new tree
        // This handles case where database was partially created but not flushed
        if (rootPage == 0 || rootPage >= header.TotalPageCount)
        {
            rootPage = 0; // Force creation of new tree
        }
        
        // Use async BTree creation
        var tree = await BTree.CreateAsync(pageManager, rootPage, cancellationToken)
            .ConfigureAwait(false);
        
        return new StoreBTree(storage, pageManager, tree, ownsStorage, ownsPageManager: true);
    }

    /// <summary>
    /// Creates a BTreeStore with an existing PageManager asynchronously.
    /// </summary>
    /// <param name="pageManager">The page manager to use.</param>
    /// <param name="rootPageNumber">Root page number (0 to create new tree).</param>
    /// <returns>An initialized BTreeStore.</returns>
    public static StoreBTree CreateFromPageManager(PageManager pageManager, uint rootPageNumber = 0)
    {
        ArgumentNullException.ThrowIfNull(pageManager);
        
        var tree = new BTree(pageManager, rootPageNumber);
        return new StoreBTree(storage: null, pageManager, tree, ownsStorage: false, ownsPageManager: false);
    }

    #endregion

    #region Get

    /// <inheritdoc/>
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return m_tree.Search(key);
    }

    /// <inheritdoc/>
    public async ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await m_tree.SearchAsync(key, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Put

    /// <inheritdoc/>
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();
        m_tree.Upsert(key, value);
    }

    /// <inheritdoc/>
    public async ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await m_tree.UpsertAsync(key, value, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Delete

    /// <inheritdoc/>
    public bool Delete(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return m_tree.Delete(key);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await m_tree.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Scan

    /// <inheritdoc/>
    public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
    {
        ThrowIfDisposed();
        return m_tree.GetRange(startKey, endKey);
    }

    /// <summary>
    /// Scans with inclusive end key (for backwards compatibility with some tests).
    /// </summary>
    public IEnumerable<(byte[] Key, byte[] Value)> ScanInclusive(byte[]? startKey, byte[]? endKey)
    {
        ThrowIfDisposed();
        return m_tree.GetRangeInclusive(startKey, endKey);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? startKey,
        byte[]? endKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var item in m_tree.GetRangeAsync(startKey, endKey, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Scans with inclusive end key asynchronously.
    /// </summary>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanInclusiveAsync(
        byte[]? startKey,
        byte[]? endKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var item in m_tree.GetRangeInclusiveAsync(startKey, endKey, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    #endregion

    #region Flush

    /// <inheritdoc/>
    public void Flush()
    {
        ThrowIfDisposed();
        m_pageManager.Flush();
    }

    /// <inheritdoc/>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await m_pageManager.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Count

    /// <summary>
    /// Gets the total number of entries in the store.
    /// </summary>
    public long Count()
    {
        ThrowIfDisposed();
        return m_tree.Count();
    }

    /// <summary>
    /// Gets the total number of entries asynchronously.
    /// </summary>
    public ValueTask<long> CountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Count());
    }

    #endregion

    #region ContainsKey

    /// <summary>
    /// Checks if a key exists in the store.
    /// </summary>
    public bool ContainsKey(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return m_tree.ContainsKey(key);
    }

    /// <summary>
    /// Checks if a key exists in the store asynchronously.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key exists.</returns>
    public async ValueTask<bool> ContainsKeyAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await m_tree.ContainsKeyAsync(key, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Tools

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable / IAsyncDisposable

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_tree.Dispose();
        
        if (m_ownsPageManager)
        {
            m_pageManager.Dispose();
        }
        
        if (m_ownsStorage)
        {
            m_storage?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        await m_tree.DisposeAsync().ConfigureAwait(false);
        
        if (m_ownsPageManager)
        {
            // PageManager doesn't have IAsyncDisposable, use sync dispose
            // This is OK because it just flushes cache which uses async internally
            m_pageManager.Dispose();
        }
        
        if (m_ownsStorage)
        {
            if (m_storage is IAsyncDisposable asyncStorage)
            {
                await asyncStorage.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                m_storage?.Dispose();
            }
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the root page number of the underlying B+Tree.
    /// </summary>
    public uint RootPageNumber => m_tree.RootPageNumber;

    /// <summary>
    /// Gets the maximum inline value size.
    /// </summary>
    public int MaxInlineValueSize => m_tree.MaxInlineValueSize;

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    /// <summary>
    /// Gets the approximate size of the store in bytes.
    /// </summary>
    public long ApproximateSizeInBytes
    {
        get
        {
            ThrowIfDisposed();
            var header = m_pageManager.GetHeader();
            return (long)header.TotalPageCount * header.PageSize;
        }
    }

    /// <summary>
    /// Gets the estimated number of distinct keys.
    /// </summary>
    public long EstimatedKeyCount => Count();

    /// <summary>
    /// Gets whether the statistics are exact or approximate.
    /// </summary>
    public bool AreStatisticsExact => true;

    #endregion
}
