using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Stores;

/// <summary>
/// In-memory key-value store using SortedDictionary.
/// Thread-safe for concurrent reads and exclusive writes.
/// Does not persist data - suitable for testing or temporary storage.
/// </summary>
public sealed class StoreInMemory : IKeyValueStore
{
    #region Constants

    /// <summary>
    /// Provider key for in-memory store.
    /// </summary>
    public const string PROVIDER_KEY = "inmemory";

    #endregion

    #region Fields

    private readonly SortedDictionary<byte[], byte[]> m_data;

    private readonly Lock m_lock = new();

    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new empty in-memory store.
    /// </summary>
    public StoreInMemory()
    {
        m_data = new SortedDictionary<byte[], byte[]>(ByteArrayComparer.Default);
    }

    #endregion

    #region Get

    /// <inheritdoc/>
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();

        lock (m_lock)
        {
            // Use span-based lookup to avoid allocation on cache hit
            return FindValue(key);
        }
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

        lock (m_lock)
        {
            // Try to find existing key to update in-place
            byte[]? existingKey = FindKey(key);
            
            if (existingKey != null)
            {
                // Key exists - only allocate new value array
                m_data[existingKey] = value.ToArray();
            }
            else
            {
                // New key - must allocate both
                m_data[key.ToArray()] = value.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        ThrowIfDisposed();

        lock (m_lock)
        {
            // Async version receives arrays directly - use them without copying
            byte[]? existingKey = FindKey(key);
            
            if (existingKey != null)
            {
                m_data[existingKey] = value;
            }
            else
            {
                m_data[key] = value;
            }
        }
        
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Delete

    /// <inheritdoc/>
    public bool Delete(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();

        lock (m_lock)
        {
            byte[]? existingKey = FindKey(key);
            if (existingKey != null)
            {
                return m_data.Remove(existingKey);
            }
            return false;
        }
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

        lock (m_lock)
        {
            var results = new List<(byte[] Key, byte[] Value)>();
            var comparer = ByteArrayComparer.Default;

            foreach (var kvp in m_data)
            {
                if (startKey != null && comparer.Compare(kvp.Key, startKey) < 0)
                    continue;
                if (endKey != null && comparer.Compare(kvp.Key, endKey) >= 0)
                    break;

                results.Add((kvp.Key, kvp.Value));
            }

            return results;
        }
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
        // No-op for in-memory store
    }

    /// <inheritdoc/>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Tools

    /// <summary>
    /// Finds an existing key in the dictionary that matches the span.
    /// Returns null if not found.
    /// </summary>
    private byte[]? FindKey(ReadOnlySpan<byte> key)
    {
        // Linear search through keys - SortedDictionary doesn't support span-based lookup
        // For production use, consider a custom data structure
        foreach (var kvp in m_data)
        {
            if (key.SequenceEqual(kvp.Key))
                return kvp.Key;
            
            // Since dictionary is sorted, we can break early if we've passed the key
            if (ByteArrayComparer.Default.Compare(kvp.Key, key) > 0)
                break;
        }
        return null;
    }

    /// <summary>
    /// Finds the value for a key using span comparison.
    /// Returns null if not found.
    /// </summary>
    private byte[]? FindValue(ReadOnlySpan<byte> key)
    {
        foreach (var kvp in m_data)
        {
            int cmp = ByteArrayComparer.Default.Compare(kvp.Key, key);
            if (cmp == 0)
                return kvp.Value;
            if (cmp > 0)
                break; // Passed it in sorted order
        }
        return null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed) 
            return;

        lock (m_lock)
        {
            if (m_disposed)
                return;
                
            m_disposed = true;
            m_data.Clear();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the number of key-value pairs in the store.
    /// </summary>
    public int Count
    {
        get
        {
            lock (m_lock)
            {
                return m_data.Count;
            }
        }
    }

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    #endregion
}
