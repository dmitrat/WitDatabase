using System.Buffers;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Pages;

namespace OutWit.Database.Core.Managers;

/// <summary>
/// Manages page allocation, deallocation, and access for the database.
/// Coordinates between storage and cache layers.
/// </summary>
public sealed class PageManager : IDisposable
{
    #region Fields

    private readonly IStorage m_storage;

    private readonly IPageCache m_cache;

    private readonly ProviderMetadata? m_initialMetadata;

    private readonly Lock m_lock = new();

    private readonly byte[] m_headerBuffer;

    private DatabaseHeader m_header;

    private bool m_headerDirty;

    private bool m_disposed;

    private bool m_initialized;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new PageManager with a custom page cache implementation.
    /// For async initialization (e.g., WASM), use <see cref="CreateAsync"/> instead.
    /// </summary>
    /// <param name="storage">Underlying storage</param>
    /// <param name="cache">Page cache implementation</param>
    /// <param name="providerMetadata">Optional metadata for new databases</param>
    public PageManager(IStorage storage, IPageCache cache, ProviderMetadata? providerMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(cache);

        m_storage = storage;
        m_cache = cache;
        m_initialMetadata = providerMetadata;
        m_headerBuffer = new byte[storage.PageSize];

        if (m_storage.PageCount == 0 || IsNewDatabase())
        {
            InitializeNewDatabase();
        }
        else
        {
            LoadHeader();
        }
        
        m_initialized = true;
    }

    /// <summary>
    /// Creates a new PageManager with the default ShardedClockCache.
    /// For async initialization (e.g., WASM), use <see cref="CreateAsync"/> instead.
    /// </summary>
    /// <param name="storage">Underlying storage</param>
    /// <param name="cacheSize">Number of pages to cache</param>
    public PageManager(IStorage storage, int cacheSize = DatabaseConstants.DEFAULT_CACHE_SIZE)
        : this(storage, new PageCacheShardedClock(storage, cacheSize), providerMetadata: null)
    {
    }

    /// <summary>
    /// Private constructor for async factory pattern.
    /// </summary>
    private PageManager(IStorage storage, IPageCache cache, ProviderMetadata? providerMetadata, bool deferInitialization)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(cache);

        m_storage = storage;
        m_cache = cache;
        m_initialMetadata = providerMetadata;
        m_headerBuffer = new byte[storage.PageSize];
        m_initialized = false;
    }

    #endregion

    #region Static Factory Methods

    /// <summary>
    /// Creates a new PageManager with asynchronous initialization.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="storage">Underlying storage</param>
    /// <param name="cache">Page cache implementation</param>
    /// <param name="providerMetadata">Optional metadata for new databases</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An initialized PageManager</returns>
    public static async ValueTask<PageManager> CreateAsync(
        IStorage storage, 
        IPageCache cache, 
        ProviderMetadata? providerMetadata = null,
        CancellationToken cancellationToken = default)
    {
        var manager = new PageManager(storage, cache, providerMetadata, deferInitialization: true);
        await manager.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return manager;
    }

    /// <summary>
    /// Creates a new PageManager with the default cache and asynchronous initialization.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="storage">Underlying storage</param>
    /// <param name="cacheSize">Number of pages to cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An initialized PageManager</returns>
    public static ValueTask<PageManager> CreateAsync(
        IStorage storage, 
        int cacheSize = DatabaseConstants.DEFAULT_CACHE_SIZE,
        CancellationToken cancellationToken = default)
    {
        var cache = new PageCacheShardedClock(storage, cacheSize);
        return CreateAsync(storage, cache, providerMetadata: null, cancellationToken);
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the PageManager asynchronously.
    /// </summary>
    private async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (m_initialized) return;

        cancellationToken.ThrowIfCancellationRequested();

        if (m_storage.PageCount == 0 || await IsNewDatabaseAsync(cancellationToken).ConfigureAwait(false))
        {
            await InitializeNewDatabaseAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await LoadHeaderAsync(cancellationToken).ConfigureAwait(false);
        }
        
        m_initialized = true;
    }

    private void InitializeNewDatabase()
    {
        m_header = DatabaseHeader.CreateNew((ushort)m_storage.PageSize);
        
        // Apply provider metadata if provided
        if (m_initialMetadata.HasValue)
        {
            m_header.Providers = m_initialMetadata.Value;
        }

        // Ensure file is large enough for at least the header page
        if (m_storage.PageCount < 1)
        {
            m_storage.SetSize(1);
        }

        SaveHeaderImmediate();
    }

    private async ValueTask InitializeNewDatabaseAsync(CancellationToken cancellationToken)
    {
        m_header = DatabaseHeader.CreateNew((ushort)m_storage.PageSize);
        
        // Apply provider metadata if provided
        if (m_initialMetadata.HasValue)
        {
            m_header.Providers = m_initialMetadata.Value;
        }

        // Ensure file is large enough for at least the header page
        if (m_storage.PageCount < 1)
        {
            await m_storage.SetSizeAsync(1, cancellationToken).ConfigureAwait(false);
        }

        await SaveHeaderImmediateAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Allocates a new page from the free list or by extending the file.
    /// </summary>
    /// <param name="pageType">Type of the new page</param>
    /// <returns>Page number and cached page</returns>
    public (uint PageNumber, CachedPage Page) AllocatePage(PageType pageType)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            uint pageNumber;

            if (m_header.FirstFreePage != DatabaseConstants.NULL_PAGE_NUMBER)
            {
                // Reuse a free page
                pageNumber = m_header.FirstFreePage;

                // Get the free page to read its next pointer
                var freePage = m_cache.GetPage(pageNumber);
                var freeHeader = PageHeader.ReadFrom(freePage.ReadOnlyData);

                // Update the free list head
                m_header.FirstFreePage = freeHeader.RightChild;
                m_header.FreePageCount--;

                m_cache.ReleasePage(pageNumber);
                
                // Evict from cache so we can create fresh
                m_cache.Evict(pageNumber);
            }
            else
            {
                // Extend the file
                pageNumber = m_header.TotalPageCount;
                m_header.TotalPageCount++;
                m_storage.SetSize(m_header.TotalPageCount);
            }

            // Create and initialize the new page
            var page = m_cache.CreatePage(pageNumber);
            var header = PageHeader.CreateEmpty(pageType, m_storage.PageSize);
            header.WriteTo(page.Data);
            page.MarkDirty();

            // Mark header as dirty (will be saved on Flush)
            m_headerDirty = true;

            return (pageNumber, page);
        }
    }

    /// <summary>
    /// Allocates a new page asynchronously.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="pageType">Type of the new page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Page number and cached page</returns>
    public async ValueTask<(uint PageNumber, CachedPage Page)> AllocatePageAsync(PageType pageType, CancellationToken cancellationToken = default)
    {
        uint pageNumber;
        bool needsExtend = false;
        uint newTotalPageCount = 0;
        bool needsFreePageRead = false;
        uint freePageToRead = 0;

        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            if (m_header.FirstFreePage != DatabaseConstants.NULL_PAGE_NUMBER)
            {
                // Need to read free page asynchronously
                freePageToRead = m_header.FirstFreePage;
                needsFreePageRead = true;
                pageNumber = freePageToRead;
            }
            else
            {
                // Need to extend the file
                pageNumber = m_header.TotalPageCount;
                m_header.TotalPageCount++;
                newTotalPageCount = m_header.TotalPageCount;
                needsExtend = true;
            }
        }

        // Handle free page reuse outside of lock (async)
        if (needsFreePageRead)
        {
            // Read the free page to get its next pointer
            var freePage = await m_cache.GetPageAsync(freePageToRead, cancellationToken).ConfigureAwait(false);
            var freeHeader = PageHeader.ReadFrom(freePage.ReadOnlyData);
            
            lock (m_lock)
            {
                // Update the free list head
                m_header.FirstFreePage = freeHeader.RightChild;
                m_header.FreePageCount--;
                m_headerDirty = true;
            }
            
            m_cache.ReleasePage(freePageToRead);
            
            // Evict from cache so we can create fresh
            await m_cache.EvictAsync(freePageToRead, cancellationToken).ConfigureAwait(false);
        }

        // Extend storage outside of lock (async)
        if (needsExtend)
        {
            await m_storage.SetSizeAsync(newTotalPageCount, cancellationToken).ConfigureAwait(false);
            
            lock (m_lock)
            {
                m_headerDirty = true;
            }
        }

        // Create and initialize the new page (async)
        var page = await m_cache.CreatePageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
        var header = PageHeader.CreateEmpty(pageType, m_storage.PageSize);
        header.WriteTo(page.Data);
        page.MarkDirty();

        return (pageNumber, page);
    }

    /// <summary>
    /// Allocates multiple pages at once (more efficient than multiple AllocatePage calls).
    /// </summary>
    /// <param name="pageType">Type of the new pages</param>
    /// <param name="count">Number of pages to allocate</param>
    /// <returns>Array of page numbers</returns>
    public uint[] AllocatePages(PageType pageType, int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive");

        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            var pageNumbers = new uint[count];
            
            for (int i = 0; i < count; i++)
            {
                uint pageNumber;

                if (m_header.FirstFreePage != DatabaseConstants.NULL_PAGE_NUMBER)
                {
                    pageNumber = m_header.FirstFreePage;
                    var freePage = m_cache.GetPage(pageNumber);
                    var freeHeader = PageHeader.ReadFrom(freePage.ReadOnlyData);
                    m_header.FirstFreePage = freeHeader.RightChild;
                    m_header.FreePageCount--;
                    m_cache.ReleasePage(pageNumber);
                    m_cache.Evict(pageNumber);
                }
                else
                {
                    pageNumber = m_header.TotalPageCount;
                    m_header.TotalPageCount++;
                }

                pageNumbers[i] = pageNumber;
            }

            // Extend file once for all new pages
            if (m_storage.PageCount < m_header.TotalPageCount)
            {
                m_storage.SetSize(m_header.TotalPageCount);
            }

            // Initialize all pages
            for (int i = 0; i < count; i++)
            {
                var page = m_cache.CreatePage(pageNumbers[i]);
                var header = PageHeader.CreateEmpty(pageType, m_storage.PageSize);
                header.WriteTo(page.Data);
                page.MarkDirty();
                m_cache.ReleasePage(pageNumbers[i]);
            }

            m_headerDirty = true;
            return pageNumbers;
        }
    }

    /// <summary>
    /// Frees a page, adding it to the free list for later reuse.
    /// </summary>
    /// <param name="pageNumber">Page number to free</param>
    public void FreePage(uint pageNumber)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (pageNumber == 0)
                throw new ArgumentException("Cannot free page 0 (header page)", nameof(pageNumber));

            if (pageNumber >= m_header.TotalPageCount)
                throw new ArgumentOutOfRangeException(nameof(pageNumber),
                    $"Page number {pageNumber} is out of range (total: {m_header.TotalPageCount})");

            var page = m_cache.GetPage(pageNumber);

            // Mark as free and point to current head of free list
            var header = new PageHeader
            {
                PageType = PageType.Free,
                Flags = 0,
                CellCount = 0,
                FreeSpaceStart = (ushort)m_storage.PageSize,
                FragmentedFreeSpace = 0,
                RightChild = m_header.FirstFreePage,
                Reserved = 0
            };

            header.WriteTo(page.Data);
            page.MarkDirty();

            m_header.FirstFreePage = pageNumber;
            m_header.FreePageCount++;

            m_cache.ReleasePage(pageNumber);
            m_headerDirty = true;
        }
    }

    /// <summary>
    /// Frees a page asynchronously, adding it to the free list for later reuse.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="pageNumber">Page number to free</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async ValueTask FreePageAsync(uint pageNumber, CancellationToken cancellationToken = default)
    {
        uint totalPages;
        uint firstFreePage;
        
        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            if (pageNumber == 0)
                throw new ArgumentException("Cannot free page 0 (header page)", nameof(pageNumber));

            if (pageNumber >= m_header.TotalPageCount)
                throw new ArgumentOutOfRangeException(nameof(pageNumber),
                    $"Page number {pageNumber} is out of range (total: {m_header.TotalPageCount})");

            totalPages = m_header.TotalPageCount;
            firstFreePage = m_header.FirstFreePage;
        }

        // Get the page asynchronously
        var page = await m_cache.GetPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);

        lock (m_lock)
        {
            // Mark as free and point to current head of free list
            var header = new PageHeader
            {
                PageType = PageType.Free,
                Flags = 0,
                CellCount = 0,
                FreeSpaceStart = (ushort)m_storage.PageSize,
                FragmentedFreeSpace = 0,
                RightChild = m_header.FirstFreePage,
                Reserved = 0
            };

            header.WriteTo(page.Data);
            page.MarkDirty();

            m_header.FirstFreePage = pageNumber;
            m_header.FreePageCount++;

            m_cache.ReleasePage(pageNumber);
            m_headerDirty = true;
        }
    }

    /// <summary>
    /// Frees multiple pages at once (more efficient than multiple FreePage calls).
    /// </summary>
    /// <param name="pageNumbers">Page numbers to free</param>
    public void FreePages(ReadOnlySpan<uint> pageNumbers)
    {
        if (pageNumbers.IsEmpty)
            return;

        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            foreach (uint pageNumber in pageNumbers)
            {
                if (pageNumber == 0)
                    throw new ArgumentException("Cannot free page 0 (header page)");

                if (pageNumber >= m_header.TotalPageCount)
                    throw new ArgumentOutOfRangeException(nameof(pageNumbers),
                        $"Page number {pageNumber} is out of range (total: {m_header.TotalPageCount})");

                var page = m_cache.GetPage(pageNumber);

                var header = new PageHeader
                {
                    PageType = PageType.Free,
                    Flags = 0,
                    CellCount = 0,
                    FreeSpaceStart = (ushort)m_storage.PageSize,
                    FragmentedFreeSpace = 0,
                    RightChild = m_header.FirstFreePage,
                    Reserved = 0
                };

                header.WriteTo(page.Data);
                page.MarkDirty();

                m_header.FirstFreePage = pageNumber;
                m_header.FreePageCount++;

                m_cache.ReleasePage(pageNumber);
            }

            m_headerDirty = true;
        }
    }

    /// <summary>
    /// Gets a page from cache or storage.
    /// </summary>
    /// <param name="pageNumber">Page number to retrieve</param>
    /// <returns>The cached page</returns>
    public CachedPage GetPage(uint pageNumber)
    {
        // Fast path: read TotalPageCount without full lock for validation
        // (TotalPageCount only increases, so this is safe)
        uint totalPages;
        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            totalPages = m_header.TotalPageCount;
        }

        if (pageNumber >= totalPages)
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Page number {pageNumber} is out of range (total: {totalPages})");

        return m_cache.GetPage(pageNumber);
    }

    /// <summary>
    /// Gets a page from cache or storage asynchronously.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="pageNumber">Page number to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The cached page</returns>
    public async ValueTask<CachedPage> GetPageAsync(uint pageNumber, CancellationToken cancellationToken = default)
    {
        uint totalPages;
        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            totalPages = m_header.TotalPageCount;
        }

        if (pageNumber >= totalPages)
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Page number {pageNumber} is out of range (total: {totalPages})");

        return await m_cache.GetPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases a reference to a page acquired via GetPage.
    /// </summary>
    public void ReleasePage(uint pageNumber)
    {
        m_cache.ReleasePage(pageNumber);
    }

    /// <summary>
    /// Marks a page as dirty (modified).
    /// </summary>
    public void MarkDirty(uint pageNumber)
    {
        m_cache.MarkDirty(pageNumber);
    }

    /// <summary>
    /// Flushes all dirty pages and the header to storage.
    /// </summary>
    public void Flush()
    {
        lock (m_lock)
        {
            ThrowIfDisposed();
            
            if (!m_initialized) return;
            
            if (m_headerDirty)
            {
                SaveHeaderImmediate();
                m_headerDirty = false;
            }
            
            m_cache.FlushAll();
        }
    }

    /// <summary>
    /// Flushes all dirty pages and the header to storage asynchronously.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        bool needsSaveHeader;
        
        lock (m_lock)
        {
            ThrowIfDisposed();
            
            if (!m_initialized) return;
            
            needsSaveHeader = m_headerDirty;
        }
        
        if (needsSaveHeader)
        {
            await SaveHeaderImmediateAsync(cancellationToken).ConfigureAwait(false);
            
            lock (m_lock)
            {
                m_headerDirty = false;
            }
        }
        
        await m_cache.FlushAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the current database header (read-only).
    /// </summary>
    public DatabaseHeader GetHeader()
    {
        lock (m_lock)
        {
            ThrowIfNotInitialized();
            return m_header;
        }
    }

    /// <summary>
    /// Gets the provider metadata from the header.
    /// </summary>
    public ProviderMetadata GetProviderMetadata()
    {
        lock (m_lock)
        {
            ThrowIfNotInitialized();
            return m_header.Providers;
        }
    }

    /// <summary>
    /// Updates the provider metadata in the header.
    /// </summary>
    public void SetProviderMetadata(ProviderMetadata metadata)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            m_header.Providers = metadata;
            m_headerDirty = true;
        }
    }

    /// <summary>
    /// Updates the schema root page number.
    /// </summary>
    public void SetSchemaRootPage(uint pageNumber)
    {
        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            m_header.SchemaRootPage = pageNumber;
            m_headerDirty = true;
        }
    }

    /// <summary>
    /// Increments the transaction counter.
    /// </summary>
    public uint IncrementTransactionCounter()
    {
        lock (m_lock)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            m_header.TransactionCounter++;
            m_headerDirty = true;
            return m_header.TransactionCounter;
        }
    }

    private bool IsNewDatabase()
    {
        if (m_storage.PageCount == 0)
            return true;

        m_storage.ReadPage(0, m_headerBuffer.AsSpan(0, m_storage.PageSize));
        
        return CheckHeaderIsNew();
    }

    private async ValueTask<bool> IsNewDatabaseAsync(CancellationToken cancellationToken)
    {
        if (m_storage.PageCount == 0)
            return true;

        await m_storage.ReadPageAsync(0, m_headerBuffer.AsMemory(0, m_storage.PageSize), cancellationToken)
            .ConfigureAwait(false);
        
        return CheckHeaderIsNew();
    }

    private bool CheckHeaderIsNew()
    {
        // Check for valid magic bytes
        if (!m_headerBuffer.AsSpan()[..16].SequenceEqual(DatabaseConstants.MAGIC_BYTES))
        {
            // File exists but has invalid header - this could be:
            // 1. Corrupted database
            // 2. Encrypted database opened without encryption
            // 3. Not a WitDatabase file
            // 
            // Only treat as "new" if file is completely empty (all zeros in first 16 bytes)
            bool allZeros = true;
            for (int i = 0; i < 16; i++)
            {
                if (m_headerBuffer[i] != 0)
                {
                    allZeros = false;
                    break;
                }
            }
            
            if (allZeros)
            {
                return true; // Empty file, can be initialized
            }
            
            throw new InvalidDataException(
                "Invalid database header. The file may be corrupted, encrypted (try opening with password), " +
                "or not a valid WitDatabase file.");
        }
        
        return false;
    }

    private void LoadHeader()
    {
        m_storage.ReadPage(0, m_headerBuffer.AsSpan(0, m_storage.PageSize));
        m_header = DatabaseHeader.ReadFrom(m_headerBuffer);

        if (m_header.PageSize != m_storage.PageSize)
        {
            throw new InvalidDataException(
                $"Page size mismatch: file has {m_header.PageSize} bytes, storage expects {m_storage.PageSize}");
        }
    }

    private async ValueTask LoadHeaderAsync(CancellationToken cancellationToken)
    {
        await m_storage.ReadPageAsync(0, m_headerBuffer.AsMemory(0, m_storage.PageSize), cancellationToken)
            .ConfigureAwait(false);
        
        m_header = DatabaseHeader.ReadFrom(m_headerBuffer);

        if (m_header.PageSize != m_storage.PageSize)
        {
            throw new InvalidDataException(
                $"Page size mismatch: file has {m_header.PageSize} bytes, storage expects {m_storage.PageSize}");
        }
    }

    private void SaveHeaderImmediate()
    {
        m_headerBuffer.AsSpan(0, m_storage.PageSize).Clear();
        m_header.WriteTo(m_headerBuffer);
        m_storage.WritePage(0, m_headerBuffer.AsSpan(0, m_storage.PageSize));
    }

    private async ValueTask SaveHeaderImmediateAsync(CancellationToken cancellationToken)
    {
        m_headerBuffer.AsSpan(0, m_storage.PageSize).Clear();
        m_header.WriteTo(m_headerBuffer);
        await m_storage.WritePageAsync(0, m_headerBuffer.AsMemory(0, m_storage.PageSize), cancellationToken)
            .ConfigureAwait(false);
    }

    #endregion

    #region Tools

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    private void ThrowIfNotInitialized()
    {
        if (!m_initialized)
        {
            throw new InvalidOperationException(
                "PageManager is not initialized. Use PageManager.CreateAsync() for async initialization, " +
                "or ensure InitializeAsync() has completed before using this instance.");
        }
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
                    if (m_initialized)
                    {
                        Flush();
                    }
                    m_cache.Dispose();
                    m_disposed = true;
                }
            }
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the page size in bytes.
    /// </summary>
    public int PageSize => m_storage.PageSize;

    /// <summary>
    /// Gets the total number of pages in the database.
    /// </summary>
    public uint TotalPageCount
    {
        get
        {
            ThrowIfNotInitialized();
            return m_header.TotalPageCount;
        }
    }

    /// <summary>
    /// Gets the number of free pages available for reuse.
    /// </summary>
    public uint FreePageCount
    {
        get
        {
            ThrowIfNotInitialized();
            return m_header.FreePageCount;
        }
    }

    /// <summary>
    /// Gets whether the PageManager has been initialized.
    /// </summary>
    public bool IsInitialized => m_initialized;

    #endregion
}
