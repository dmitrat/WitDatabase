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
public sealed class StoreBTree : IKeyValueStore, IAsyncDisposable
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

    #endregion

    #region Get

    /// <inheritdoc/>
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return m_tree.Search(key);
    }

    /// <inheritdoc/>
    public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Get(key));
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
    public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Put(key, value);
        return ValueTask.CompletedTask;
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
    public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Delete(key));
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
        foreach (var item in Scan(startKey, endKey))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
        await ValueTask.CompletedTask;
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
            m_pageManager.Dispose();
        }
        
        if (m_ownsStorage)
        {
            m_storage?.Dispose();
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

    #endregion
}
