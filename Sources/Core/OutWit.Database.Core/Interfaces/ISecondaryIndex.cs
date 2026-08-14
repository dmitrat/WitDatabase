namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Represents a secondary index that maps index keys to primary keys.
    /// Used by the SQL engine for efficient lookups on non-primary key columns.
    /// </summary>
    public interface ISecondaryIndex : IDisposable
    {
        #region Lookup

        /// <summary>
        /// Finds all primary keys that match the specified index key.
        /// </summary>
        /// <param name="indexKey">The index key to search for.</param>
        /// <returns>Enumerable of primary keys matching the index key.</returns>
        IEnumerable<byte[]> Find(ReadOnlySpan<byte> indexKey);

        /// <summary>
        /// Finds all primary keys within the specified range of index keys.
        /// </summary>
        /// <param name="startKey">Start of range (inclusive), or null for beginning.</param>
        /// <param name="endKey">End of range (exclusive), or null for end.</param>
        /// <returns>Enumerable of (indexKey, primaryKey) pairs in order.</returns>
        IEnumerable<(byte[] IndexKey, byte[] PrimaryKey)> FindRange(byte[]? startKey, byte[]? endKey);

        /// <summary>
        /// Gets the first (minimum) entry in the index.
        /// Used for MIN() optimization when index exists on the column.
        /// </summary>
        /// <returns>The first (indexKey, primaryKey) pair, or null if index is empty.</returns>
        (byte[] IndexKey, byte[] PrimaryKey)? GetFirstEntry();

        /// <summary>
        /// Gets the last (maximum) entry in the index.
        /// Used for MAX() optimization when index exists on the column.
        /// </summary>
        /// <returns>The last (indexKey, primaryKey) pair, or null if index is empty.</returns>
        (byte[] IndexKey, byte[] PrimaryKey)? GetLastEntry();

        /// <summary>
        /// Checks if the specified index key exists in the index.
        /// </summary>
        /// <param name="indexKey">The index key to check.</param>
        /// <returns>True if the index key exists; otherwise false.</returns>
        bool Contains(ReadOnlySpan<byte> indexKey);

        /// <summary>
        /// Checks if a specific (indexKey, primaryKey) pair exists in the index.
        /// </summary>
        /// <param name="indexKey">The index key.</param>
        /// <param name="primaryKey">The primary key.</param>
        /// <returns>True if the pair exists; otherwise false.</returns>
        bool ContainsEntry(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey);

        #endregion

        #region Modification

        /// <summary>
        /// Adds an entry to the index.
        /// </summary>
        /// <param name="indexKey">The index key.</param>
        /// <param name="primaryKey">The primary key that this index key maps to.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the index is unique and an entry with the same index key already exists.
        /// </exception>
        void Add(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey);

        /// <summary>
        /// Removes an entry from the index.
        /// </summary>
        /// <param name="indexKey">The index key.</param>
        /// <param name="primaryKey">The primary key.</param>
        /// <returns>True if the entry was removed; false if it didn't exist.</returns>
        bool Remove(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey);

        /// <summary>
        /// Removes all entries with the specified index key.
        /// </summary>
        /// <param name="indexKey">The index key.</param>
        /// <returns>The number of entries removed.</returns>
        int RemoveAll(ReadOnlySpan<byte> indexKey);

        /// <summary>
        /// Clears all entries from the index.
        /// </summary>
        void Clear();

        #endregion

        #region Flush

        /// <summary>
        /// Flushes any pending changes to durable storage.
        /// </summary>
        void Flush();

        /// <summary>
        /// Flushes any pending changes asynchronously.
        /// </summary>
        ValueTask FlushAsync(CancellationToken cancellationToken = default);

        #endregion

        #region Properties

        /// <summary>
        /// Gets the name of this index.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets whether this is a unique index.
        /// Unique indexes allow only one primary key per index key.
        /// </summary>
        bool IsUnique { get; }

        /// <summary>
        /// Gets the number of entries in the index.
        /// </summary>
        long Count { get; }

        /// <summary>
        /// Whether this index's content was found where it was expected, rather than the index
        /// having been created from nothing at this open.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><see cref="Count"/> cannot answer this and that is the point.</b> An index over a
        /// column that is NULL in every row holds nothing and is perfectly healthy; an index whose
        /// file was never copied holds nothing and answers every query wrongly, with no error. Only
        /// the layer that opened the storage knows which happened, and it knows it for one moment.
        /// </para>
        /// <para>
        /// Defaulted to <c>true</c> so that an implementation which cannot tell keeps today's
        /// behaviour - nothing is rebuilt on a guess.
        /// </para>
        /// </remarks>
        bool ContentWasFound => true;

        #endregion
    }
}
