using System.Buffers.Binary;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Indexes
{
    /// <summary>
    /// Generic secondary index implementation that uses any IKeyValueStore.
    /// Allows using the same storage engine for both primary data and indexes.
    /// </summary>
    /// <remarks>
    /// The index stores composite keys: indexKey + 0x00 + primaryKey + indexKeyLength.
    /// For unique indexes, the primaryKey is the value; for non-unique indexes, 
    /// it's part of the key to allow multiple primary keys per index key.
    /// </remarks>
    public sealed class SecondaryIndexKeyValueStore : ISecondaryIndex
    {
        #region Constants

        /// <summary>Separator byte between index key and primary key in composite key.</summary>
        private const byte KEY_SEPARATOR = 0x00;

        #endregion

        #region Fields

        private readonly IKeyValueStore m_store;
        private readonly bool m_ownsStore;
        private readonly ByteArrayComparer m_comparer;
        private long m_count;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new secondary index using the specified key-value store.
        /// </summary>
        /// <param name="name">The name of the index.</param>
        /// <param name="store">The underlying key-value store for storage.</param>
        /// <param name="isUnique">Whether this is a unique index.</param>
        /// <param name="ownsStore">If true, disposes the store when this index is disposed.</param>
        public SecondaryIndexKeyValueStore(string name, IKeyValueStore store, bool isUnique, bool ownsStore = true)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            Name = name;
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            IsUnique = isUnique;
            m_ownsStore = ownsStore;
            m_comparer = ByteArrayComparer.Default;
            m_count = CountEntries();
        }

        #endregion

        #region Lookup

        /// <inheritdoc/>
        public IEnumerable<byte[]> Find(ReadOnlySpan<byte> indexKey)
        {
            ThrowIfDisposed();

            var indexKeyArray = indexKey.ToArray();
            return FindInternal(indexKeyArray);
        }

        private IEnumerable<byte[]> FindInternal(byte[] indexKeyArray)
        {
            if (IsUnique)
            {
                var value = m_store.Get(indexKeyArray);
                if (value != null)
                    yield return value;
            }
            else
            {
                var startKey = CreatePrefixStartKey(indexKeyArray);
                var endKey = CreatePrefixEndKey(indexKeyArray);

                foreach (var (key, _) in m_store.Scan(startKey, endKey))
                {
                    var (_, primaryKey) = SplitCompositeKey(key);
                    if (primaryKey.Length > 0)
                        yield return primaryKey;
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerable<(byte[] IndexKey, byte[] PrimaryKey)> FindRange(byte[]? startKey, byte[]? endKey)
        {
            ThrowIfDisposed();

            if (IsUnique)
            {
                foreach (var (indexKey, primaryKey) in m_store.Scan(startKey, endKey))
                {
                    yield return (indexKey, primaryKey);
                }
            }
            else
            {
                byte[]? scanStart = startKey != null 
                    ? CreatePrefixStartKey(startKey) 
                    : null;
                byte[]? scanEnd = endKey != null 
                    ? CreatePrefixEndKey(endKey) 
                    : null;

                foreach (var (compositeKey, _) in m_store.Scan(scanStart, scanEnd))
                {
                    var (indexKey, primaryKey) = SplitCompositeKey(compositeKey);
                    if (primaryKey.Length > 0)
                        yield return (indexKey, primaryKey);
                }
            }
        }

        /// <inheritdoc/>
        public bool Contains(ReadOnlySpan<byte> indexKey)
        {
            ThrowIfDisposed();

            var indexKeyArray = indexKey.ToArray();

            if (IsUnique)
            {
                return m_store.Get(indexKeyArray) != null;
            }
            else
            {
                var startKey = CreatePrefixStartKey(indexKeyArray);
                var endKey = CreatePrefixEndKey(indexKeyArray);
                return m_store.Scan(startKey, endKey).Any();
            }
        }

        /// <inheritdoc/>
        public bool ContainsEntry(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey)
        {
            ThrowIfDisposed();

            var indexKeyArray = indexKey.ToArray();
            var primaryKeyArray = primaryKey.ToArray();

            if (IsUnique)
            {
                var value = m_store.Get(indexKeyArray);
                return value != null && m_comparer.Equals(value, primaryKeyArray);
            }
            else
            {
                var compositeKey = CreateCompositeKey(indexKeyArray, primaryKeyArray);
                return m_store.Get(compositeKey) != null;
            }
        }

        #endregion

        #region Modification

        /// <inheritdoc/>
        public void Add(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey)
        {
            ThrowIfDisposed();

            var indexKeyArray = indexKey.ToArray();
            var primaryKeyArray = primaryKey.ToArray();

            if (IsUnique)
            {
                var existing = m_store.Get(indexKeyArray);
                if (existing != null)
                {
                    throw new InvalidOperationException(
                        $"Unique index '{Name}' already contains an entry for this key.");
                }
                
                m_store.Put(indexKeyArray, primaryKeyArray);
            }
            else
            {
                var compositeKey = CreateCompositeKey(indexKeyArray, primaryKeyArray);
                m_store.Put(compositeKey, ReadOnlySpan<byte>.Empty);
            }
            
            Interlocked.Increment(ref m_count);
        }

        /// <inheritdoc/>
        public bool Remove(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey)
        {
            ThrowIfDisposed();

            var indexKeyArray = indexKey.ToArray();
            var primaryKeyArray = primaryKey.ToArray();

            bool removed;
            if (IsUnique)
            {
                var existing = m_store.Get(indexKeyArray);
                if (existing == null || !m_comparer.Equals(existing, primaryKeyArray))
                    return false;

                removed = m_store.Delete(indexKeyArray);
            }
            else
            {
                var compositeKey = CreateCompositeKey(indexKeyArray, primaryKeyArray);
                removed = m_store.Delete(compositeKey);
            }

            if (removed)
                Interlocked.Decrement(ref m_count);

            return removed;
        }

        /// <inheritdoc/>
        public int RemoveAll(ReadOnlySpan<byte> indexKey)
        {
            ThrowIfDisposed();

            var indexKeyArray = indexKey.ToArray();

            if (IsUnique)
            {
                if (m_store.Delete(indexKeyArray))
                {
                    Interlocked.Decrement(ref m_count);
                    return 1;
                }
                return 0;
            }
            else
            {
                var startKey = CreatePrefixStartKey(indexKeyArray);
                var endKey = CreatePrefixEndKey(indexKeyArray);
                
                var keysToDelete = m_store.Scan(startKey, endKey)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var key in keysToDelete)
                {
                    m_store.Delete(key);
                    Interlocked.Decrement(ref m_count);
                }

                return keysToDelete.Count;
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            ThrowIfDisposed();
            
            var allKeys = m_store.Scan(null, null)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in allKeys)
            {
                m_store.Delete(key);
            }
            
            Interlocked.Exchange(ref m_count, 0);
        }

        #endregion

        #region Flush

        /// <inheritdoc/>
        public void Flush()
        {
            ThrowIfDisposed();
            m_store.Flush();
        }

        /// <inheritdoc/>
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            return m_store.FlushAsync(cancellationToken);
        }

        #endregion

        #region Key Management

        /// <summary>
        /// Creates a composite key from index key and primary key.
        /// Format: indexKey + 0x00 + primaryKey + indexKeyLength (4 bytes)
        /// </summary>
        private static byte[] CreateCompositeKey(byte[] indexKey, ReadOnlySpan<byte> primaryKey)
        {
            var composite = new byte[indexKey.Length + 1 + primaryKey.Length + 4];
            indexKey.CopyTo(composite, 0);
            composite[indexKey.Length] = KEY_SEPARATOR;
            primaryKey.CopyTo(composite.AsSpan(indexKey.Length + 1));
            BinaryPrimitives.WriteInt32LittleEndian(composite.AsSpan(indexKey.Length + 1 + primaryKey.Length), indexKey.Length);
            return composite;
        }

        /// <summary>
        /// Splits a composite key into index key and primary key.
        /// </summary>
        private static (byte[] IndexKey, byte[] PrimaryKey) SplitCompositeKey(byte[] compositeKey)
        {
            if (compositeKey.Length < 5)
                return (compositeKey, []);

            int indexKeyLength = BinaryPrimitives.ReadInt32LittleEndian(compositeKey.AsSpan(compositeKey.Length - 4));
            
            if (indexKeyLength < 0 || indexKeyLength >= compositeKey.Length - 4)
                return (compositeKey, []);

            if (compositeKey[indexKeyLength] != KEY_SEPARATOR)
                return (compositeKey, []);

            var indexKey = compositeKey[..indexKeyLength];
            var primaryKey = compositeKey[(indexKeyLength + 1)..(compositeKey.Length - 4)];
            return (indexKey, primaryKey);
        }

        /// <summary>
        /// Creates the start key for prefix scanning (inclusive).
        /// </summary>
        private static byte[] CreatePrefixStartKey(byte[] prefix)
        {
            var startKey = new byte[prefix.Length + 1];
            prefix.CopyTo(startKey, 0);
            startKey[prefix.Length] = KEY_SEPARATOR;
            return startKey;
        }

        /// <summary>
        /// Creates the end key for prefix scanning (exclusive).
        /// </summary>
        private static byte[] CreatePrefixEndKey(byte[] prefix)
        {
            var endKey = new byte[prefix.Length + 1];
            prefix.CopyTo(endKey, 0);
            endKey[prefix.Length] = (byte)(KEY_SEPARATOR + 1);
            return endKey;
        }

        private long CountEntries()
        {
            return m_store.Scan(null, null).LongCount();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!m_disposed)
            {
                if (m_ownsStore)
                {
                    m_store.Dispose();
                }
                m_disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public bool IsUnique { get; }

        /// <inheritdoc/>
        public long Count => Interlocked.Read(ref m_count);

        #endregion
    }
}
