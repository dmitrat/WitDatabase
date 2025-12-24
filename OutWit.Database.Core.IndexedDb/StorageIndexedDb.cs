using Microsoft.JSInterop;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.IndexedDb;

/// <summary>
/// IStorage implementation backed by browser IndexedDB.
/// Designed for Blazor WebAssembly applications.
/// </summary>
/// <remarks>
/// This storage provider enables WitDatabase to run entirely in the browser
/// with data persisted to IndexedDB. It implements the standard IStorage interface,
/// allowing it to work with StoreBTree and all existing database functionality.
/// 
/// Limitations:
/// - Not compatible with LSM-Tree (use BTree instead)
/// - File locking not applicable (single-tab access recommended)
/// - Sync methods use async bridge (acceptable in WASM single-threaded environment)
/// </remarks>
public sealed class StorageIndexedDb : IStorage, IAsyncDisposable
{
    #region Constants

    /// <summary>
    /// Provider key for IndexedDB storage.
    /// </summary>
    public const string PROVIDER_KEY = "indexeddb";

    private const int DEFAULT_INITIAL_PAGES = 1;

    #endregion

    #region Fields

    private readonly IndexedDbInterop m_interop;
    private readonly int m_pageSize;
    private readonly bool m_ownsInterop;

    private long m_pageCount;
    private bool m_initialized;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new IndexedDB storage with the specified database name.
    /// </summary>
    /// <param name="databaseName">Name of the IndexedDB database.</param>
    /// <param name="jsRuntime">Blazor JS runtime.</param>
    /// <param name="pageSize">Page size in bytes (default: 4096).</param>
    public StorageIndexedDb(string databaseName, IJSRuntime jsRuntime, int pageSize = DatabaseConstants.DEFAULT_PAGE_SIZE)
        : this(new IndexedDbInterop(jsRuntime, databaseName), pageSize, ownsInterop: true)
    {
    }

    /// <summary>
    /// Creates a new IndexedDB storage with an existing interop instance.
    /// </summary>
    /// <param name="interop">IndexedDB interop wrapper.</param>
    /// <param name="pageSize">Page size in bytes.</param>
    /// <param name="ownsInterop">Whether to dispose the interop when this storage is disposed.</param>
    public StorageIndexedDb(IndexedDbInterop interop, int pageSize = DatabaseConstants.DEFAULT_PAGE_SIZE, bool ownsInterop = true)
    {
        if (pageSize < DatabaseConstants.MIN_PAGE_SIZE || pageSize > DatabaseConstants.MAX_PAGE_SIZE)
            throw new ArgumentOutOfRangeException(nameof(pageSize), 
                $"Page size must be between {DatabaseConstants.MIN_PAGE_SIZE} and {DatabaseConstants.MAX_PAGE_SIZE}");

        m_interop = interop ?? throw new ArgumentNullException(nameof(interop));
        m_pageSize = pageSize;
        m_ownsInterop = ownsInterop;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the storage by opening the IndexedDB database and loading metadata.
    /// This is called automatically on first operation, but can be called explicitly
    /// for early initialization.
    /// </summary>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (m_initialized) return;
        
        ThrowIfDisposed();

        await m_interop.OpenAsync(cancellationToken);
        
        // Check if database was previously created with a different page size
        var storedPageSize = await m_interop.GetPageSizeAsync(cancellationToken);
        if (storedPageSize > 0 && storedPageSize != m_pageSize)
        {
            throw new InvalidOperationException(
                $"Database was created with page size {storedPageSize}, but {m_pageSize} was requested. " +
                $"Page size cannot be changed after database creation.");
        }
        
        // Store page size for new databases
        if (storedPageSize == 0)
        {
            await m_interop.SetPageSizeAsync(m_pageSize, cancellationToken);
        }
        
        // Load current page count
        m_pageCount = await m_interop.GetPageCountAsync(cancellationToken);
        
        // Ensure at least one page exists (for header)
        if (m_pageCount == 0)
        {
            m_pageCount = DEFAULT_INITIAL_PAGES;
            await m_interop.SetPageCountAsync(m_pageCount, cancellationToken);
            
            // Initialize first page with zeros
            await m_interop.WritePageAsync(0, new byte[m_pageSize], cancellationToken);
        }
        
        m_initialized = true;
    }

    private void EnsureInitialized()
    {
        if (!m_initialized)
        {
            // Sync initialization - acceptable in WASM single-threaded environment
            InitializeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    #endregion

    #region Read

    /// <inheritdoc/>
    public void ReadPage(long pageNumber, Span<byte> buffer)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ValidatePageNumber(pageNumber);
        ValidateBuffer(buffer);

        // Sync bridge for async operation
        var data = m_interop.ReadPageAsync(pageNumber).AsTask().GetAwaiter().GetResult();
        
        if (data != null)
        {
            data.AsSpan(0, Math.Min(data.Length, m_pageSize)).CopyTo(buffer);
        }
        else
        {
            // Page doesn't exist yet - return zeros
            buffer[..m_pageSize].Clear();
        }
    }

    /// <inheritdoc/>
    public async ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await InitializeAsync(cancellationToken);
        ValidatePageNumber(pageNumber);
        ValidateBuffer(buffer.Span);

        var data = await m_interop.ReadPageAsync(pageNumber, cancellationToken);
        
        if (data != null)
        {
            data.AsMemory(0, Math.Min(data.Length, m_pageSize)).CopyTo(buffer);
        }
        else
        {
            // Page doesn't exist yet - return zeros
            buffer.Span[..m_pageSize].Clear();
        }
    }

    #endregion

    #region Write

    /// <inheritdoc/>
    public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ValidatePageNumber(pageNumber);
        ValidateBuffer(buffer);

        // Sync bridge for async operation
        m_interop.WritePageAsync(pageNumber, buffer[..m_pageSize].ToArray()).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await InitializeAsync(cancellationToken);
        ValidatePageNumber(pageNumber);
        ValidateBuffer(buffer.Span);

        await m_interop.WritePageAsync(pageNumber, buffer[..m_pageSize].ToArray(), cancellationToken);
    }

    #endregion

    #region Flush

    /// <inheritdoc/>
    public void Flush()
    {
        ThrowIfDisposed();
        // IndexedDB commits automatically after each transaction
        // No explicit flush needed
    }

    /// <inheritdoc/>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // IndexedDB commits automatically after each transaction
        return ValueTask.CompletedTask;
    }

    #endregion

    #region SetSize

    /// <inheritdoc/>
    public void SetSize(long pageCount)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        if (pageCount < 0)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        // Sync bridge
        SetSizeAsync(pageCount).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sets the size of the storage asynchronously.
    /// </summary>
    public async ValueTask SetSizeAsync(long pageCount, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await InitializeAsync(cancellationToken);

        if (pageCount < 0)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        if (pageCount < m_pageCount)
        {
            // Truncate
            await m_interop.TruncatePagesAsync(pageCount, cancellationToken);
        }
        else
        {
            // Extend - just update count, pages will be created on write
            await m_interop.SetPageCountAsync(pageCount, cancellationToken);
        }
        
        m_pageCount = pageCount;
    }

    #endregion

    #region Tools

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    private void ValidatePageNumber(long pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= m_pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), 
                $"Page number must be between 0 and {m_pageCount - 1}");
    }

    private void ValidateBuffer(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < m_pageSize)
            throw new ArgumentException($"Buffer must be at least {m_pageSize} bytes", nameof(buffer));
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        if (m_ownsInterop)
        {
            // Sync dispose - best effort
            try
            {
                m_interop.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore errors during sync dispose
            }
        }
    }

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        if (m_ownsInterop)
        {
            await m_interop.DisposeAsync();
        }
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public int PageSize => m_pageSize;

    /// <inheritdoc/>
    public long PageCount
    {
        get
        {
            EnsureInitialized();
            return m_pageCount;
        }
    }

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    /// <summary>
    /// Gets the database name.
    /// </summary>
    public string DatabaseName => m_interop.DatabaseName;

    /// <summary>
    /// Gets whether the storage has been initialized.
    /// </summary>
    public bool IsInitialized => m_initialized;

    #endregion
}
