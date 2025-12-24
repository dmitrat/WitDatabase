using Microsoft.JSInterop;

namespace OutWit.Database.Core.IndexedDb;

/// <summary>
/// Provides .NET wrapper for IndexedDB JavaScript operations.
/// Thread-safe via async operations (single-threaded in WASM).
/// </summary>
public sealed class IndexedDbInterop : IDisposable, IAsyncDisposable
{
    #region Constants

    private const string JS_NAMESPACE = "witDb";

    #endregion

    #region Fields

    private readonly IJSRuntime m_jsRuntime;
    private readonly string m_databaseName;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new IndexedDB interop wrapper.
    /// </summary>
    /// <param name="jsRuntime">Blazor JS runtime.</param>
    /// <param name="databaseName">Name of the IndexedDB database.</param>
    public IndexedDbInterop(IJSRuntime jsRuntime, string databaseName)
    {
        m_jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
        
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name cannot be empty", nameof(databaseName));
        
        m_databaseName = databaseName;
        Log($"IndexedDbInterop created for database: {databaseName}");
    }

    #endregion

    #region Debug Logging

    private static void Log(string message)
    {
        StorageIndexedDb.DebugLog?.Invoke($"[JS Interop] {message}");
    }

    #endregion

    #region Open / Close

    /// <summary>
    /// Opens or creates the IndexedDB database.
    /// </summary>
    public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Log($"Opening database: {m_databaseName}");
        await m_jsRuntime.InvokeVoidAsync($"{JS_NAMESPACE}.open", cancellationToken, m_databaseName);
        Log($"Database opened: {m_databaseName}");
    }

    /// <summary>
    /// Closes the IndexedDB database connection.
    /// </summary>
    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        if (m_disposed) return;
        
        try
        {
            Log($"Closing database: {m_databaseName}");
            await m_jsRuntime.InvokeVoidAsync($"{JS_NAMESPACE}.close", cancellationToken, m_databaseName);
            Log($"Database closed: {m_databaseName}");
        }
        catch (JSDisconnectedException)
        {
            Log("JSDisconnectedException during close (Blazor circuit disconnected)");
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// Reads a page from IndexedDB.
    /// </summary>
    /// <param name="pageNumber">Page number (0-based).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Page data, or null if page doesn't exist.</returns>
    public async ValueTask<byte[]?> ReadPageAsync(long pageNumber, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log($"ReadPage: {pageNumber}");
        var result = await m_jsRuntime.InvokeAsync<byte[]?>(
            $"{JS_NAMESPACE}.readPage", 
            cancellationToken, 
            m_databaseName, 
            pageNumber);
        Log($"ReadPage: {pageNumber} returned {result?.Length ?? 0} bytes");
        
        return result;
    }

    #endregion

    #region Write

    /// <summary>
    /// Writes a page to IndexedDB.
    /// </summary>
    /// <param name="pageNumber">Page number (0-based).</param>
    /// <param name="data">Page data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask WritePageAsync(long pageNumber, byte[] data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log($"WritePage: {pageNumber}, size={data.Length}");
        await m_jsRuntime.InvokeVoidAsync(
            $"{JS_NAMESPACE}.writePage", 
            cancellationToken, 
            m_databaseName, 
            pageNumber, 
            data);
        Log($"WritePage: {pageNumber} complete");
    }

    /// <summary>
    /// Writes multiple pages to IndexedDB in a single transaction.
    /// </summary>
    /// <param name="pages">Pages to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask WritePagesAsync(IEnumerable<(long PageNumber, byte[] Data)> pages, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var pagesArray = pages.Select(p => new { pageNumber = p.PageNumber, data = p.Data }).ToArray();
        Log($"WritePages: {pagesArray.Length} pages");
        
        await m_jsRuntime.InvokeVoidAsync(
            $"{JS_NAMESPACE}.writePages", 
            cancellationToken, 
            m_databaseName, 
            pagesArray);
        Log($"WritePages: complete");
    }

    #endregion

    #region Metadata

    /// <summary>
    /// Gets the total page count from database metadata.
    /// </summary>
    public async ValueTask<long> GetPageCountAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log("GetPageCount...");
        var result = await m_jsRuntime.InvokeAsync<long>(
            $"{JS_NAMESPACE}.getPageCount", 
            cancellationToken, 
            m_databaseName);
        Log($"GetPageCount: {result}");
        
        return result;
    }

    /// <summary>
    /// Sets the total page count in database metadata.
    /// </summary>
    public async ValueTask SetPageCountAsync(long count, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log($"SetPageCount: {count}");
        await m_jsRuntime.InvokeVoidAsync(
            $"{JS_NAMESPACE}.setPageCount", 
            cancellationToken, 
            m_databaseName, 
            count);
        Log($"SetPageCount: complete");
    }

    /// <summary>
    /// Gets the page size from database metadata.
    /// </summary>
    /// <returns>Page size, or 0 if not set.</returns>
    public async ValueTask<int> GetPageSizeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log("GetPageSize...");
        var result = await m_jsRuntime.InvokeAsync<int>(
            $"{JS_NAMESPACE}.getPageSize", 
            cancellationToken, 
            m_databaseName);
        Log($"GetPageSize: {result}");
        
        return result;
    }

    /// <summary>
    /// Sets the page size in database metadata.
    /// </summary>
    public async ValueTask SetPageSizeAsync(int pageSize, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log($"SetPageSize: {pageSize}");
        await m_jsRuntime.InvokeVoidAsync(
            $"{JS_NAMESPACE}.setPageSize", 
            cancellationToken, 
            m_databaseName, 
            pageSize);
        Log($"SetPageSize: complete");
    }

    /// <summary>
    /// Truncates the database to the specified page count.
    /// </summary>
    public async ValueTask TruncatePagesAsync(long newPageCount, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log($"TruncatePages: {newPageCount}");
        await m_jsRuntime.InvokeVoidAsync(
            $"{JS_NAMESPACE}.truncatePages", 
            cancellationToken, 
            m_databaseName, 
            newPageCount);
        Log($"TruncatePages: complete");
    }

    #endregion

    #region Database Operations

    /// <summary>
    /// Checks if the database exists.
    /// </summary>
    public async ValueTask<bool> DatabaseExistsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log("DatabaseExists...");
        var result = await m_jsRuntime.InvokeAsync<bool>(
            $"{JS_NAMESPACE}.databaseExists", 
            cancellationToken, 
            m_databaseName);
        Log($"DatabaseExists: {result}");
        
        return result;
    }

    /// <summary>
    /// Deletes the entire database.
    /// </summary>
    public async ValueTask DeleteDatabaseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        Log("DeleteDatabase...");
        await m_jsRuntime.InvokeVoidAsync(
            $"{JS_NAMESPACE}.deleteDatabase", 
            cancellationToken, 
            m_databaseName);
        Log("DeleteDatabase: complete");
    }

    #endregion

    #region Tools

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        Log("Dispose (sync) - attempting async close...");
        try
        {
            CloseAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log($"Dispose error: {ex.Message}");
        }
    }

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (m_disposed) return;
        m_disposed = true;

        Log("DisposeAsync");
        await CloseAsync();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the database name.
    /// </summary>
    public string DatabaseName => m_databaseName;

    #endregion
}
