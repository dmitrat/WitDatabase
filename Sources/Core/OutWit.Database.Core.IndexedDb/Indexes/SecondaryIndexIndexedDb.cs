using System.Buffers.Binary;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.IndexedDb.Indexes;

/// <summary>
/// Secondary index implementation backed by IndexedDB.
/// Uses a separate object store within the same database for index data.
/// </summary>
/// <remarks>
/// The index stores composite keys: indexKey + 0x00 + primaryKey + indexKeyLength.
/// For unique indexes, the primaryKey is the value; for non-unique indexes, 
/// it's part of the key to allow multiple primary keys per index key.
/// </remarks>
public sealed class SecondaryIndexIndexedDb : ISecondaryIndex
{
    #region Constants

    /// <summary>Separator byte between index key and primary key in composite key.</summary>
    private const byte KEY_SEPARATOR = 0x00;

    #endregion

    #region Fields

    private readonly IndexedDbIndexInterop m_interop;
    private readonly bool m_ownsInterop;
    private readonly ByteArrayComparer m_comparer;
    private long m_count;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new secondary index using IndexedDB.
    /// </summary>
    /// <param name="name">The name of the index (used as object store name).</param>
    /// <param name="interop">The IndexedDB index interop.</param>
    /// <param name="isUnique">Whether this is a unique index.</param>
    /// <param name="ownsInterop">If true, disposes the interop when this index is disposed.</param>
    public SecondaryIndexIndexedDb(string name, IndexedDbIndexInterop interop, bool isUnique, bool ownsInterop = true)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        Name = name;
        m_interop = interop ?? throw new ArgumentNullException(nameof(interop));
        IsUnique = isUnique;
        m_ownsInterop = ownsInterop;
        m_comparer = ByteArrayComparer.Default;
        
        // Load count synchronously on creation (acceptable in WASM single-threaded)
        m_count = m_interop.CountAsync().AsTask().GetAwaiter().GetResult();
    }

    #endregion

    #region Lookup

    /// <inheritdoc/>
    public IEnumerable<byte[]> Find(ReadOnlySpan<byte> indexKey)
    {
        ThrowIfDisposed();

        var indexKeyArray = indexKey.ToArray();
        return FindInternalAsync(indexKeyArray).ToBlockingEnumerable();
    }

    private async IAsyncEnumerable<byte[]> FindInternalAsync(byte[] indexKeyArray)
    {
        if (IsUnique)
        {
            var value = await m_interop.GetAsync(indexKeyArray);
            if (value != null)
                yield return value;
        }
        else
        {
            var startKey = CreatePrefixStartKey(indexKeyArray);
            var endKey = CreatePrefixEndKey(indexKeyArray);

            await foreach (var (key, _) in m_interop.ScanAsync(startKey, endKey))
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
        return FindRangeInternalAsync(startKey, endKey).ToBlockingEnumerable();
    }

    private async IAsyncEnumerable<(byte[] IndexKey, byte[] PrimaryKey)> FindRangeInternalAsync(byte[]? startKey, byte[]? endKey)
    {
        if (IsUnique)
        {
            await foreach (var (indexKey, primaryKey) in m_interop.ScanAsync(startKey, endKey))
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

            await foreach (var (compositeKey, _) in m_interop.ScanAsync(scanStart, scanEnd))
            {
                var (indexKey, primaryKey) = SplitCompositeKey(compositeKey);
                if (primaryKey.Length > 0)
                    yield return (indexKey, primaryKey);
            }
        }
    }

    /// <inheritdoc/>
    public (byte[] IndexKey, byte[] PrimaryKey)? GetFirstEntry()
    {
        ThrowIfDisposed();
        return GetFirstEntryAsync().AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask<(byte[] IndexKey, byte[] PrimaryKey)?> GetFirstEntryAsync()
    {
        // Get the first entry from the store
        await foreach (var (key, value) in m_interop.ScanAsync(null, null))
        {
            if (IsUnique)
            {
                // For unique indexes: key is indexKey, value is primaryKey
                return (key, value);
            }
            else
            {
                // For non-unique indexes: key is composite, need to split
                var (indexKey, primaryKey) = SplitCompositeKey(key);
                if (primaryKey.Length > 0)
                    return (indexKey, primaryKey);
            }
            break; // Only need first entry
        }
        return null;
    }

    /// <inheritdoc/>
    public (byte[] IndexKey, byte[] PrimaryKey)? GetLastEntry()
    {
        ThrowIfDisposed();
        return GetLastEntryAsync().AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask<(byte[] IndexKey, byte[] PrimaryKey)?> GetLastEntryAsync()
    {
        // Get the last entry from the store by iterating (IndexedDB doesn't have efficient reverse scan)
        (byte[] Key, byte[] Value)? lastEntry = null;
        
        await foreach (var entry in m_interop.ScanAsync(null, null))
        {
            lastEntry = entry;
        }

        if (lastEntry == null)
            return null;

        if (IsUnique)
        {
            // For unique indexes: key is indexKey, value is primaryKey
            return (lastEntry.Value.Key, lastEntry.Value.Value);
        }
        else
        {
            // For non-unique indexes: key is composite, need to split
            var (indexKey, primaryKey) = SplitCompositeKey(lastEntry.Value.Key);
            if (primaryKey.Length > 0)
                return (indexKey, primaryKey);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool Contains(ReadOnlySpan<byte> indexKey)
    {
        ThrowIfDisposed();

        var indexKeyArray = indexKey.ToArray();

        if (IsUnique)
        {
            return m_interop.GetAsync(indexKeyArray).AsTask().GetAwaiter().GetResult() != null;
        }
        else
        {
            var startKey = CreatePrefixStartKey(indexKeyArray);
            var endKey = CreatePrefixEndKey(indexKeyArray);
            return m_interop.HasAnyAsync(startKey, endKey).AsTask().GetAwaiter().GetResult();
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
            var value = m_interop.GetAsync(indexKeyArray).AsTask().GetAwaiter().GetResult();
            return value != null && m_comparer.Equals(value, primaryKeyArray);
        }
        else
        {
            var compositeKey = CreateCompositeKey(indexKeyArray, primaryKeyArray);
            return m_interop.GetAsync(compositeKey).AsTask().GetAwaiter().GetResult() != null;
        }
    }

    #endregion

    #region Modification

    /// <inheritdoc/>
    public void Add(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey)
    {
        ThrowIfDisposed();
        AddAsync(indexKey.ToArray(), primaryKey.ToArray()).AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask AddAsync(byte[] indexKeyArray, byte[] primaryKeyArray)
    {
        if (IsUnique)
        {
            var existing = await m_interop.GetAsync(indexKeyArray);
            if (existing != null)
            {
                throw new UniqueIndexViolationException(
                    $"Unique index '{Name}' already contains an entry for this key.");
            }
            
            await m_interop.PutAsync(indexKeyArray, primaryKeyArray);
        }
        else
        {
            var compositeKey = CreateCompositeKey(indexKeyArray, primaryKeyArray);
            await m_interop.PutAsync(compositeKey, []);
        }
        
        Interlocked.Increment(ref m_count);
    }

    /// <inheritdoc/>
    public bool Remove(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey)
    {
        ThrowIfDisposed();
        return RemoveAsync(indexKey.ToArray(), primaryKey.ToArray()).AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask<bool> RemoveAsync(byte[] indexKeyArray, byte[] primaryKeyArray)
    {
        bool removed;
        if (IsUnique)
        {
            var existing = await m_interop.GetAsync(indexKeyArray);
            if (existing == null || !m_comparer.Equals(existing, primaryKeyArray))
                return false;

            removed = await m_interop.DeleteAsync(indexKeyArray);
        }
        else
        {
            var compositeKey = CreateCompositeKey(indexKeyArray, primaryKeyArray);
            removed = await m_interop.DeleteAsync(compositeKey);
        }

        if (removed)
            Interlocked.Decrement(ref m_count);

        return removed;
    }

    /// <inheritdoc/>
    public int RemoveAll(ReadOnlySpan<byte> indexKey)
    {
        ThrowIfDisposed();
        return RemoveAllAsync(indexKey.ToArray()).AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask<int> RemoveAllAsync(byte[] indexKeyArray)
    {
        if (IsUnique)
        {
            if (await m_interop.DeleteAsync(indexKeyArray))
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

            var count = await m_interop.DeleteRangeAsync(startKey, endKey);
            Interlocked.Add(ref m_count, -count);
            return count;
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        ThrowIfDisposed();
        m_interop.ClearAsync().AsTask().GetAwaiter().GetResult();
        Interlocked.Exchange(ref m_count, 0);
    }

    #endregion

    #region Flush

    /// <inheritdoc/>
    public void Flush()
    {
        ThrowIfDisposed();
        // IndexedDB commits automatically after each transaction
    }

    /// <inheritdoc/>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        // IndexedDB commits automatically after each transaction
        return ValueTask.CompletedTask;
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

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        if (m_ownsInterop)
        {
            m_interop.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
