namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Abstraction for database file storage. Allows different implementations
    /// for file system, in-memory, and platform-specific storage (e.g., IndexedDB for WASM).
    /// </summary>
    public interface IStorage : IProvider, IDisposable
    {
        #region Read

        /// <summary>
        /// Reads a page from storage into the buffer.
        /// </summary>
        /// <param name="pageNumber">Zero-based page number</param>
        /// <param name="buffer">Buffer to read into (must be at least PageSize bytes)</param>
        void ReadPage(long pageNumber, Span<byte> buffer);

        /// <summary>
        /// Reads a page from storage asynchronously.
        /// </summary>
        /// <param name="pageNumber">Zero-based page number</param>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="cancellationToken">Cancellation token</param>
        ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default);

        #endregion

        #region Write

        /// <summary>
        /// Writes a page to storage from the buffer.
        /// </summary>
        /// <param name="pageNumber">Zero-based page number</param>
        /// <param name="buffer">Buffer containing page data</param>
        void WritePage(long pageNumber, ReadOnlySpan<byte> buffer);

        /// <summary>
        /// Writes a page to storage asynchronously.
        /// </summary>
        /// <param name="pageNumber">Zero-based page number</param>
        /// <param name="buffer">Buffer containing page data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

        #endregion

        #region Flush

        /// <summary>
        /// Flushes all pending writes to durable storage.
        /// </summary>
        void Flush();

        /// <summary>
        /// Flushes all pending writes to durable storage asynchronously.
        /// </summary>
        ValueTask FlushAsync(CancellationToken cancellationToken = default);

        #endregion

        #region SetSize

        /// <summary>
        /// Extends the storage to accommodate the specified number of pages.
        /// </summary>
        /// <param name="pageCount">New total page count</param>
        void SetSize(long pageCount);

        /// <summary>
        /// Extends the storage to accommodate the specified number of pages asynchronously.
        /// </summary>
        /// <param name="pageCount">New total page count</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// Default implementation calls sync <see cref="SetSize"/>.
        /// Override for async-only environments like Blazor WASM with IndexedDB.
        /// </remarks>
        ValueTask SetSizeAsync(long pageCount, CancellationToken cancellationToken = default)
        {
            SetSize(pageCount);
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the page size in bytes
        /// </summary>
        int PageSize { get; }

        /// <summary>
        /// Gets the total number of pages in the storage
        /// </summary>
        long PageCount { get; }

        /// <summary>
        /// Gets whether the storage is read-only
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// Gets the unique provider key identifying this storage implementation.
        /// Used for validation when opening existing databases.
        /// </summary>
        /// <example>
        /// "file" for file storage, "memory" for in-memory storage, "encrypted" for encrypted storage.
        /// </example>
        string ProviderKey { get; }

        #endregion
    }
}
