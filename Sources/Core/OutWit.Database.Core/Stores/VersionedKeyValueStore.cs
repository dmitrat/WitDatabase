using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Stores
{
    /// <summary>
    /// Wrapper that adds row versioning to any IKeyValueStore.
    /// Each value is stored with a version prefix (8 bytes).
    /// Thread-safe version counter with atomic increments.
    /// </summary>
    public sealed class VersionedKeyValueStore : IVersionedKeyValueStore
    {
        #region Constants

        private const int VERSION_SIZE = 8; // sizeof(long)
        private const string VERSION_COUNTER_KEY = "\0\0_version_counter_";

        #endregion

        #region Fields

        private readonly IKeyValueStore m_innerStore;
        private readonly bool m_ownsStore;
        private readonly object m_versionLock = new();
        private long m_globalVersion;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a versioned wrapper around the specified store.
        /// </summary>
        /// <param name="innerStore">The underlying store.</param>
        /// <param name="ownsStore">If true, disposes the inner store when this is disposed.</param>
        public VersionedKeyValueStore(IKeyValueStore innerStore, bool ownsStore = true)
        {
            m_innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
            m_ownsStore = ownsStore;
            
            // Load global version counter
            m_globalVersion = LoadGlobalVersion();
        }

        #endregion

        #region Get

        /// <inheritdoc/>
        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            
            var stored = m_innerStore.Get(key);
            if (stored == null || stored.Length < VERSION_SIZE)
                return null;
            
            // Skip version prefix, return value only
            return stored.AsSpan(VERSION_SIZE).ToArray();
        }

        /// <inheritdoc/>
        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Get(key));
        }

        /// <inheritdoc/>
        public (byte[] Value, long Version)? GetWithVersion(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            
            var stored = m_innerStore.Get(key);
            if (stored == null || stored.Length < VERSION_SIZE)
                return null;
            
            var version = BinaryPrimitives.ReadInt64LittleEndian(stored.AsSpan(0, VERSION_SIZE));
            var value = stored.AsSpan(VERSION_SIZE).ToArray();
            
            return (value, version);
        }

        /// <inheritdoc/>
        public ValueTask<(byte[] Value, long Version)?> GetWithVersionAsync(
            byte[] key, 
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GetWithVersion(key));
        }

        /// <inheritdoc/>
        public long? GetVersion(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            
            var stored = m_innerStore.Get(key);
            if (stored == null || stored.Length < VERSION_SIZE)
                return null;
            
            return BinaryPrimitives.ReadInt64LittleEndian(stored.AsSpan(0, VERSION_SIZE));
        }

        #endregion

        #region Put

        /// <inheritdoc/>
        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            PutWithVersion(key, value);
        }

        /// <inheritdoc/>
        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Put(key, value);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public long PutWithVersion(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            ThrowIfDisposed();
            
            var newVersion = IncrementVersion();
            var versionedValue = CreateVersionedValue(value, newVersion);
            
            m_innerStore.Put(key, versionedValue);
            
            return newVersion;
        }

        /// <inheritdoc/>
        public ValueTask<long> PutWithVersionAsync(
            byte[] key, 
            byte[] value, 
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PutWithVersion(key, value));
        }

        #endregion

        #region Conditional Put

        /// <inheritdoc/>
        public (bool Success, long NewVersion) ConditionalPut(
            ReadOnlySpan<byte> key, 
            ReadOnlySpan<byte> value, 
            long expectedVersion)
        {
            ThrowIfDisposed();
            
            lock (m_versionLock)
            {
                var currentVersion = GetVersion(key);
                
                // Check version match
                if (currentVersion != expectedVersion)
                {
                    return (false, currentVersion ?? 0);
                }
                
                var newVersion = IncrementVersionUnsafe();
                var versionedValue = CreateVersionedValue(value, newVersion);
                
                m_innerStore.Put(key, versionedValue);
                
                return (true, newVersion);
            }
        }

        /// <inheritdoc/>
        public (bool Success, long NewVersion) ConditionalPutAsync(
            byte[] key, 
            byte[] value, 
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ConditionalPut(key, value, expectedVersion);
        }

        #endregion

        #region Delete

        /// <inheritdoc/>
        public bool Delete(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            return m_innerStore.Delete(key);
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Delete(key));
        }

        /// <inheritdoc/>
        public bool ConditionalDelete(ReadOnlySpan<byte> key, long expectedVersion)
        {
            ThrowIfDisposed();
            
            lock (m_versionLock)
            {
                var currentVersion = GetVersion(key);
                
                if (currentVersion != expectedVersion)
                {
                    return false;
                }
                
                return m_innerStore.Delete(key);
            }
        }

        /// <inheritdoc/>
        public ValueTask<bool> ConditionalDeleteAsync(
            byte[] key, 
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ConditionalDelete(key, expectedVersion));
        }

        #endregion

        #region Scan

        /// <inheritdoc/>
        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
        {
            ThrowIfDisposed();
            
            foreach (var (key, stored) in m_innerStore.Scan(startKey, endKey))
            {
                // Skip system keys
                if (IsSystemKey(key))
                    continue;
                
                if (stored.Length >= VERSION_SIZE)
                {
                    var value = stored.AsSpan(VERSION_SIZE).ToArray();
                    yield return (key, value);
                }
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
            byte[]? startKey, 
            byte[]? endKey, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            await foreach (var (key, stored) in m_innerStore.ScanAsync(startKey, endKey, cancellationToken))
            {
                // Skip system keys
                if (IsSystemKey(key))
                    continue;
                
                if (stored.Length >= VERSION_SIZE)
                {
                    var value = stored.AsSpan(VERSION_SIZE).ToArray();
                    yield return (key, value);
                }
            }
        }

        /// <summary>
        /// Scans with version information.
        /// </summary>
        public IEnumerable<(byte[] Key, byte[] Value, long Version)> ScanWithVersion(
            byte[]? startKey, 
            byte[]? endKey)
        {
            ThrowIfDisposed();
            
            foreach (var (key, stored) in m_innerStore.Scan(startKey, endKey))
            {
                if (IsSystemKey(key))
                    continue;
                
                if (stored.Length >= VERSION_SIZE)
                {
                    var version = BinaryPrimitives.ReadInt64LittleEndian(stored.AsSpan(0, VERSION_SIZE));
                    var value = stored.AsSpan(VERSION_SIZE).ToArray();
                    yield return (key, value, version);
                }
            }
        }

        #endregion

        #region Flush

        /// <inheritdoc/>
        public void Flush()
        {
            ThrowIfDisposed();
            SaveGlobalVersion();
            m_innerStore.Flush();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Forwarded rather than left to the interface default, which would call <see cref="Flush"/>
        /// and reorganise nothing. The global version is saved first for the same reason it is in
        /// <see cref="Flush"/>.
        /// </remarks>
        public void Checkpoint()
        {
            ThrowIfDisposed();
            SaveGlobalVersion();
            m_innerStore.Checkpoint();
        }

        /// <inheritdoc/>
        public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            SaveGlobalVersion();
            await m_innerStore.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Version Management

        private long IncrementVersion()
        {
            lock (m_versionLock)
            {
                return IncrementVersionUnsafe();
            }
        }

        private long IncrementVersionUnsafe()
        {
            return ++m_globalVersion;
        }

        private long LoadGlobalVersion()
        {
            var keyBytes = System.Text.Encoding.UTF8.GetBytes(VERSION_COUNTER_KEY);
            var stored = m_innerStore.Get(keyBytes);
            
            if (stored == null || stored.Length < VERSION_SIZE)
                return 0;
            
            return BinaryPrimitives.ReadInt64LittleEndian(stored);
        }

        private void SaveGlobalVersion()
        {
            lock (m_versionLock)
            {
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(VERSION_COUNTER_KEY);
                var valueBytes = new byte[VERSION_SIZE];
                BinaryPrimitives.WriteInt64LittleEndian(valueBytes, m_globalVersion);
                
                m_innerStore.Put(keyBytes, valueBytes);
            }
        }

        private static byte[] CreateVersionedValue(ReadOnlySpan<byte> value, long version)
        {
            var result = new byte[VERSION_SIZE + value.Length];
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(0, VERSION_SIZE), version);
            value.CopyTo(result.AsSpan(VERSION_SIZE));
            return result;
        }

        private static bool IsSystemKey(byte[] key)
        {
            // System keys start with \0\0
            return key.Length >= 2 && key[0] == 0 && key[1] == 0;
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            
            SaveGlobalVersion();
            
            if (m_ownsStore)
            {
                m_innerStore.Dispose();
            }
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public long CurrentGlobalVersion
        {
            get
            {
                lock (m_versionLock)
                {
                    return m_globalVersion;
                }
            }
        }

        /// <inheritdoc/>
        public string ProviderKey => $"versioned:{m_innerStore.ProviderKey}";

        /// <summary>
        /// Gets the underlying store.
        /// </summary>
        public IKeyValueStore InnerStore => m_innerStore;

        #endregion
    }
}
