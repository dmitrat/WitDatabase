using Microsoft.JSInterop;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.IndexedDb;

/// <summary>
/// IStorage implementation backed by browser IndexedDB.
/// Designed for Blazor WebAssembly applications.
/// </summary>
public sealed class StorageIndexedDb : IStorage, IAsyncOnlyStorage, IAsyncInitializable, IAsyncDisposable
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
    
    /// <summary>
    /// Debug logging action. Set externally for debugging purposes.
    /// When null (default), no logging occurs.
    /// </summary>
    public static Action<string>? DebugLog { get; set; }

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new IndexedDB storage with the specified database name.
    /// Call <see cref="InitializeAsync"/> before use, or use <see cref="WitDatabaseBuilder.BuildAsync"/>.
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
    /// Call <see cref="InitializeAsync"/> before use, or use <see cref="WitDatabaseBuilder.BuildAsync"/>.
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

    #region Debug Logging

    private static void Log(string message)
    {
        DebugLog?.Invoke($"[IndexedDb] {message}");
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the storage by opening the IndexedDB database and loading metadata.
    /// Must be called before any read/write operations in WASM environment.
    /// </summary>
    /// <remarks>
    /// This method is idempotent - calling it multiple times is safe.
    /// Use <see cref="WitDatabaseBuilder.BuildAsync"/> for automatic initialization.
    /// </remarks>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (m_initialized)
            return;
        
        ThrowIfDisposed();
        
        // Critical: yield to browser event loop before any JS interop
        await Task.Yield();
        
        await m_interop.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await Task.Yield();
        
        var storedPageSize = await m_interop.GetPageSizeAsync(cancellationToken).ConfigureAwait(false);
        
        if (storedPageSize > 0 && storedPageSize != m_pageSize)
        {
            throw new InvalidOperationException(
                $"Database was created with page size {storedPageSize}, but {m_pageSize} was requested.");
        }
        
        if (storedPageSize == 0)
        {
            await Task.Yield();
            await m_interop.SetPageSizeAsync(m_pageSize, cancellationToken).ConfigureAwait(false);
        }
        
        await Task.Yield();
        m_pageCount = await m_interop.GetPageCountAsync(cancellationToken).ConfigureAwait(false);
        
        if (m_pageCount == 0)
        {
            m_pageCount = DEFAULT_INITIAL_PAGES;
            await Task.Yield();
            await m_interop.SetPageCountAsync(m_pageCount, cancellationToken).ConfigureAwait(false);
            
            await Task.Yield();
            await m_interop.WritePageAsync(0, new byte[m_pageSize], cancellationToken).ConfigureAwait(false);
        }
        
        m_initialized = true;
        Log($"Initialized: db={m_interop.DatabaseName}, pageSize={m_pageSize}, pageCount={m_pageCount}");
    }

    private void ThrowIfNotInitialized()
    {
        if (!m_initialized)
        {
            throw new InvalidOperationException(
                "StorageIndexedDb is not initialized. Call InitializeAsync() first.");
        }
    }

    #endregion

    #region Read

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown if storage is not initialized. Call <see cref="InitializeAsync"/> first.
    /// </exception>
    public void ReadPage(long pageNumber, Span<byte> buffer)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();
        ValidatePageNumber(pageNumber);
        ValidateBuffer(buffer);

        // Warning: This will deadlock in WASM - use ReadPageAsync instead
        var data = m_interop.ReadPageAsync(pageNumber).AsTask().GetAwaiter().GetResult();
        
        if (data != null)
        {
            data.AsSpan(0, Math.Min(data.Length, m_pageSize)).CopyTo(buffer);
        }
        else
        {
            buffer[..m_pageSize].Clear();
        }
    }

    /// <inheritdoc/>
    public async ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!m_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        
        ValidatePageNumber(pageNumber);
        ValidateBuffer(buffer.Span);

        // Yield to allow browser event loop to process
        await Task.Yield();

        var data = await m_interop.ReadPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
        
        if (data != null)
        {
            data.AsMemory(0, Math.Min(data.Length, m_pageSize)).CopyTo(buffer);
        }
        else
        {
            buffer.Span[..m_pageSize].Clear();
        }
    }

    #endregion

    #region Write

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown if storage is not initialized. Call <see cref="InitializeAsync"/> first.
    /// </exception>
    public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();
        ValidatePageNumberForWrite(pageNumber);
        ValidateBuffer(buffer);

        // Warning: This will deadlock in WASM - use WritePageAsync instead
        if (pageNumber >= m_pageCount)
        {
            SetSize(pageNumber + 1);
        }

        m_interop.WritePageAsync(pageNumber, buffer[..m_pageSize].ToArray()).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!m_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        
        ValidatePageNumberForWrite(pageNumber);
        ValidateBuffer(buffer.Span);

        // Yield to allow browser event loop to process
        await Task.Yield();

        if (pageNumber >= m_pageCount)
        {
            await SetSizeAsync(pageNumber + 1, cancellationToken).ConfigureAwait(false);
        }

        await m_interop.WritePageAsync(pageNumber, buffer[..m_pageSize].ToArray(), cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Flush

    /// <inheritdoc/>
    public void Flush()
    {
        ThrowIfDisposed();
        // IndexedDB transactions are auto-committed, no explicit flush needed
    }

    /// <inheritdoc/>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // IndexedDB transactions are auto-committed, no explicit flush needed
        return ValueTask.CompletedTask;
    }

    #endregion

    #region SetSize

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown if storage is not initialized. Call <see cref="InitializeAsync"/> first.
    /// </exception>
    public void SetSize(long pageCount)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        if (pageCount < 0)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        // Warning: This will deadlock in WASM - use SetSizeAsync instead
        SetSizeAsync(pageCount).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sets the size of the storage asynchronously.
    /// </summary>
    public async ValueTask SetSizeAsync(long pageCount, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!m_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (pageCount < 0)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        // Yield to allow browser event loop to process
        await Task.Yield();

        if (pageCount < m_pageCount)
        {
            await m_interop.TruncatePagesAsync(pageCount, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await m_interop.SetPageCountAsync(pageCount, cancellationToken).ConfigureAwait(false);
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
                $"Page number must be between 0 and {m_pageCount - 1}, got {pageNumber}");
    }

    private void ValidatePageNumberForWrite(long pageNumber)
    {
        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), 
                "Page number cannot be negative");
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
            try
            {
                m_interop.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Best effort disposal
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
            await m_interop.DisposeAsync().ConfigureAwait(false);
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
            ThrowIfNotInitialized();
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

    /// <inheritdoc/>
    public bool IsInitialized => m_initialized;

    /// <inheritdoc/>
    /// <remarks>
    /// Always returns true for IndexedDB storage. In Blazor WASM,
    /// synchronous I/O operations will deadlock, so async methods must be used.
    /// Use <see cref="WitDatabaseBuilder.BuildAsync"/> for proper initialization.
    /// </remarks>
    public bool RequiresAsyncOperations => true;

    #endregion
}
