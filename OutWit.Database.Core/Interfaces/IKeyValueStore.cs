namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Common interface for key-value storage engines.
    /// Implemented by both B+Tree and LSM-Tree.
    /// </summary>
    public interface IKeyValueStore : IProvider, IDisposable
    {
        #region Get

        /// <summary>
        /// Gets the value for the specified key.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <returns>The value, or null if not found or deleted.</returns>
        byte[]? Get(ReadOnlySpan<byte> key);

        /// <summary>
        /// Gets the value for the specified key asynchronously.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The value, or null if not found or deleted.</returns>
        ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default);

        #endregion

        #region Put

        /// <summary>
        /// Inserts or updates a key-value pair.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);

        /// <summary>
        /// Inserts or updates a key-value pair asynchronously.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default);

        #endregion

        #region Delete

        /// <summary>
        /// Deletes a key from the store.
        /// </summary>
        /// <param name="key">The key to delete.</param>
        /// <returns>True if deleted, false if not found.</returns>
        bool Delete(ReadOnlySpan<byte> key);

        /// <summary>
        /// Deletes a key from the store asynchronously.
        /// </summary>
        /// <param name="key">The key to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if deleted, false if not found.</returns>
        ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default);

        #endregion

        #region Scan

        /// <summary>
        /// Scans key-value pairs in the specified range.
        /// </summary>
        /// <param name="startKey">Start of range (inclusive), or null for beginning.</param>
        /// <param name="endKey">End of range (exclusive), or null for end.</param>
        /// <returns>Enumerable of key-value pairs in order.</returns>
        IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey);

        /// <summary>
        /// Scans key-value pairs asynchronously.
        /// </summary>
        /// <param name="startKey">Start of range (inclusive), or null for beginning.</param>
        /// <param name="endKey">End of range (exclusive), or null for end.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Async enumerable of key-value pairs in order.</returns>
        IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(byte[]? startKey, byte[]? endKey, CancellationToken cancellationToken = default);

        #endregion

        #region Flush

        /// <summary>
        /// Flushes any pending writes to durable storage.
        /// </summary>
        void Flush();

        /// <summary>
        /// Flushes any pending writes to durable storage asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask FlushAsync(CancellationToken cancellationToken = default);

        #endregion

        #region Properties

        /// <summary>
        /// Gets the unique provider key identifying this store implementation.
        /// Used for validation when opening existing databases.
        /// </summary>
        /// <example>
        /// "btree" for B-Tree store, "lsm" for LSM-Tree store.
        /// </example>
        string ProviderKey { get; }

        #endregion
    }
}
