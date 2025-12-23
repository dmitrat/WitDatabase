using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Mvcc;

namespace OutWit.Database.Core.Stores
{
    /// <summary>
    /// Key-value store wrapper that provides MVCC (Multi-Version Concurrency Control) support.
    /// Stores multiple versions of each key for snapshot isolation.
    /// 
    /// Storage format:
    /// - Each key can have multiple versions stored as: [key][version_suffix] -> MvccRecord
    /// - Version suffix is the inverted timestamp (MaxValue - timestamp) for descending order
    /// - This allows efficient retrieval of the latest version via prefix scan
    /// </summary>
    public sealed class MvccKeyValueStore : IMvccStore
    {
        #region Constants

        private const int VERSION_SUFFIX_SIZE = 8;
        private const string PROVIDER_KEY_PREFIX = "mvcc:";

        #endregion

        #region Fields

        private readonly IKeyValueStore m_innerStore;
        private readonly ITransactionTimestampManager m_timestampManager;
        private readonly bool m_ownsStore;
        private readonly ByteArrayComparer m_comparer;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an MVCC store wrapper.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <param name="timestampManager">The transaction timestamp manager.</param>
        /// <param name="ownsStore">If true, disposes the inner store when this is disposed.</param>
        public MvccKeyValueStore(
            IKeyValueStore innerStore, 
            ITransactionTimestampManager timestampManager,
            bool ownsStore = true)
        {
            m_innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
            m_timestampManager = timestampManager ?? throw new ArgumentNullException(nameof(timestampManager));
            m_ownsStore = ownsStore;
            m_comparer = ByteArrayComparer.Default;
        }

        #endregion

        #region Get

        /// <inheritdoc/>
        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            // Default: get latest committed version
            return GetAsOf(key, m_timestampManager.CurrentTimestamp, transactionId: 0);
        }

        /// <inheritdoc/>
        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Get(key));
        }

        /// <inheritdoc/>
        public byte[]? GetAsOf(ReadOnlySpan<byte> key, long snapshotTimestamp, long transactionId = 0)
        {
            ThrowIfDisposed();
            var record = GetRecordAsOf(key, snapshotTimestamp, transactionId);
            return record?.Value;
        }

        /// <inheritdoc/>
        public MvccRecord? GetRecordAsOf(ReadOnlySpan<byte> key, long snapshotTimestamp, long transactionId = 0)
        {
            ThrowIfDisposed();

            var keyArray = key.ToArray();
            var startKey = CreateVersionedKey(keyArray, long.MaxValue);  // Start from newest
            var endKey = CreateVersionedKeyEnd(keyArray);

            foreach (var (versionedKey, data) in m_innerStore.Scan(startKey, endKey))
            {
                if (!MvccRecord.TryDeserialize(data, out var record))
                    continue;

                // Check visibility
                if (transactionId != 0)
                {
                    // Transaction context: use full visibility rules
                    if (record.IsVisibleTo(
                        snapshotTimestamp,
                        transactionId,
                        m_timestampManager.IsCommitted,
                        m_timestampManager.GetCommitTimestamp))
                    {
                        return record;
                    }
                }
                else
                {
                    // Non-transactional read: only committed data
                    if (record.IsVisibleAsOf(snapshotTimestamp))
                    {
                        return record;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Put

        /// <inheritdoc/>
        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            ThrowIfDisposed();
            var timestamp = m_timestampManager.GetNextTimestamp();
            PutVersion(key, value, timestamp, transactionId: 0);
        }

        /// <inheritdoc/>
        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Put(key, value);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void PutVersion(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, long timestamp, long transactionId = 0)
        {
            ThrowIfDisposed();

            var keyArray = key.ToArray();
            var valueArray = value.ToArray();

            // Mark previous version as deleted (if exists and not already deleted)
            MarkPreviousVersionDeleted(keyArray, timestamp, transactionId);

            // Create new version
            var record = new MvccRecord(valueArray, timestamp, transactionId);
            var versionedKey = CreateVersionedKey(keyArray, timestamp);

            m_innerStore.Put(versionedKey, record.Serialize());
        }

        #endregion

        #region Delete

        /// <inheritdoc/>
        public bool Delete(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            var timestamp = m_timestampManager.GetNextTimestamp();
            return DeleteVersion(key, timestamp, transactionId: 0);
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Delete(key));
        }

        /// <inheritdoc/>
        public bool DeleteVersion(ReadOnlySpan<byte> key, long timestamp, long transactionId = 0)
        {
            ThrowIfDisposed();

            var keyArray = key.ToArray();

            // Find current visible version
            var currentRecord = GetRecordAsOf(key, timestamp, transactionId);
            if (currentRecord == null)
                return false;

            // Mark the current version as deleted
            return MarkPreviousVersionDeleted(keyArray, timestamp, transactionId);
        }

        #endregion

        #region Transaction Operations

        /// <inheritdoc/>
        public void CommitTransaction(long transactionId, long commitTimestamp)
        {
            ThrowIfDisposed();

            if (transactionId == 0)
                throw new ArgumentException("Transaction ID cannot be 0.", nameof(transactionId));

            // Scan all records and commit those belonging to this transaction
            foreach (var (key, data) in m_innerStore.Scan(null, null))
            {
                if (!MvccRecord.TryDeserialize(data, out var record))
                    continue;

                if (record.TransactionId == transactionId)
                {
                    // Mark as committed with the commit timestamp
                    var committedRecord = record.AsCommitted(commitTimestamp);
                    m_innerStore.Put(key, committedRecord.Serialize());
                }
            }

            m_timestampManager.MarkCommitted(transactionId, commitTimestamp);
        }

        /// <inheritdoc/>
        public void RollbackTransaction(long transactionId)
        {
            ThrowIfDisposed();

            if (transactionId == 0)
                throw new ArgumentException("Transaction ID cannot be 0.", nameof(transactionId));

            // Find and remove all records belonging to this transaction
            var keysToDelete = new List<byte[]>();

            foreach (var (key, data) in m_innerStore.Scan(null, null))
            {
                if (!MvccRecord.TryDeserialize(data, out var record))
                    continue;

                if (record.TransactionId == transactionId)
                {
                    keysToDelete.Add(key);
                }
            }

            foreach (var key in keysToDelete)
            {
                m_innerStore.Delete(key);
            }

            m_timestampManager.UnregisterTransaction(transactionId);
        }

        #endregion

        #region Scan

        /// <inheritdoc/>
        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
        {
            ThrowIfDisposed();
            return ScanAsOf(startKey, endKey, m_timestampManager.CurrentTimestamp, transactionId: 0);
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
            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public IEnumerable<(byte[] Key, byte[] Value)> ScanAsOf(
            byte[]? startKey, 
            byte[]? endKey, 
            long snapshotTimestamp, 
            long transactionId = 0)
        {
            ThrowIfDisposed();

            var seenKeys = new HashSet<byte[]>(m_comparer);
            byte[]? currentKey = null;

            // Create versioned scan range
            var versionedStartKey = startKey != null 
                ? CreateVersionedKey(startKey, long.MaxValue) 
                : null;
            var versionedEndKey = endKey != null 
                ? CreateVersionedKeyEnd(endKey) 
                : null;

            foreach (var (versionedKey, data) in m_innerStore.Scan(versionedStartKey, versionedEndKey))
            {
                var originalKey = ExtractOriginalKey(versionedKey);
                
                // Skip if we've already returned this key
                if (seenKeys.Contains(originalKey))
                    continue;

                // Check if key is in range
                if (endKey != null && m_comparer.Compare(originalKey, endKey) >= 0)
                    break;

                if (!MvccRecord.TryDeserialize(data, out var record))
                    continue;

                // Check visibility
                bool isVisible;
                if (transactionId != 0)
                {
                    isVisible = record.IsVisibleTo(
                        snapshotTimestamp,
                        transactionId,
                        m_timestampManager.IsCommitted,
                        m_timestampManager.GetCommitTimestamp);
                }
                else
                {
                    isVisible = record.IsVisibleAsOf(snapshotTimestamp);
                }

                if (isVisible)
                {
                    seenKeys.Add(originalKey);
                    yield return (originalKey, record.Value);
                }
            }
        }

        #endregion

        #region Garbage Collection

        /// <inheritdoc/>
        public int GarbageCollect(long minActiveSnapshotTimestamp)
        {
            ThrowIfDisposed();

            var keysToDelete = new List<byte[]>();
            var keyVersions = new Dictionary<byte[], List<(byte[] VersionedKey, MvccRecord Record)>>(m_comparer);

            // Group versions by original key
            foreach (var (versionedKey, data) in m_innerStore.Scan(null, null))
            {
                var originalKey = ExtractOriginalKey(versionedKey);
                
                if (!MvccRecord.TryDeserialize(data, out var record))
                    continue;

                if (!keyVersions.TryGetValue(originalKey, out var versions))
                {
                    versions = new List<(byte[], MvccRecord)>();
                    keyVersions[originalKey] = versions;
                }

                versions.Add((versionedKey, record));
            }

            // For each key, determine which versions can be removed
            foreach (var (originalKey, versions) in keyVersions)
            {
                // Sort by effective timestamp descending (newest first)
                versions.Sort((a, b) => b.Record.EffectiveTimestamp.CompareTo(a.Record.EffectiveTimestamp));

                var keptVisibleVersion = false;
                
                foreach (var (versionedKey, record) in versions)
                {
                    // Keep all uncommitted versions - they belong to active transactions
                    if (!record.IsCommitted)
                        continue;

                    var effectiveTs = record.EffectiveTimestamp;

                    // Keep the newest version that is visible to minActiveSnapshotTimestamp
                    if (!keptVisibleVersion)
                    {
                        // This version is visible if it was created before minActiveSnapshot
                        // and either not deleted or deleted after minActiveSnapshot
                        if (effectiveTs <= minActiveSnapshotTimestamp)
                        {
                            if (!record.IsDeleted || record.DeleteTimestamp > minActiveSnapshotTimestamp)
                            {
                                // Keep this version - it's the latest visible one
                                keptVisibleVersion = true;
                                continue;
                            }
                        }
                        
                        // Version is newer than minActiveSnapshot - keep it, someone might need it
                        if (effectiveTs > minActiveSnapshotTimestamp)
                        {
                            continue;
                        }
                    }

                    // Remove old versions that:
                    // 1. Are older than the latest visible version
                    // 2. Were created before minActiveSnapshotTimestamp
                    if (keptVisibleVersion && effectiveTs < minActiveSnapshotTimestamp)
                    {
                        keysToDelete.Add(versionedKey);
                    }
                }
            }

            // Delete old versions
            foreach (var key in keysToDelete)
            {
                m_innerStore.Delete(key);
            }

            return keysToDelete.Count;
        }

        #endregion

        #region Version Info

        /// <inheritdoc/>
        public int GetVersionCount(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();

            var keyArray = key.ToArray();
            var startKey = CreateVersionedKey(keyArray, long.MaxValue);
            var endKey = CreateVersionedKeyEnd(keyArray);

            var count = 0;
            foreach (var _ in m_innerStore.Scan(startKey, endKey))
            {
                count++;
            }

            return count;
        }

        /// <inheritdoc/>
        public IReadOnlyList<MvccRecord> GetAllVersions(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();

            var keyArray = key.ToArray();
            var startKey = CreateVersionedKey(keyArray, long.MaxValue);
            var endKey = CreateVersionedKeyEnd(keyArray);

            var versions = new List<MvccRecord>();
            foreach (var (_, data) in m_innerStore.Scan(startKey, endKey))
            {
                if (MvccRecord.TryDeserialize(data, out var record))
                {
                    versions.Add(record);
                }
            }

            return versions.AsReadOnly();
        }

        #endregion

        #region Flush

        /// <inheritdoc/>
        public void Flush()
        {
            ThrowIfDisposed();
            m_innerStore.Flush();
        }

        /// <inheritdoc/>
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return m_innerStore.FlushAsync(cancellationToken);
        }

        #endregion

        #region Tools

        private bool MarkPreviousVersionDeleted(byte[] key, long deleteTimestamp, long transactionId)
        {
            var startKey = CreateVersionedKey(key, long.MaxValue);
            var endKey = CreateVersionedKeyEnd(key);

            foreach (var (versionedKey, data) in m_innerStore.Scan(startKey, endKey))
            {
                if (!MvccRecord.TryDeserialize(data, out var record))
                    continue;

                // Skip already deleted versions
                if (record.IsDeleted)
                    continue;

                // For transactional deletes, only delete own uncommitted or committed versions
                if (transactionId != 0)
                {
                    if (record.TransactionId != transactionId && !record.IsCommitted)
                        continue;
                }

                // Mark as deleted
                var deletedRecord = record.WithDeleteTimestamp(deleteTimestamp);
                m_innerStore.Put(versionedKey, deletedRecord.Serialize());
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a versioned key by appending inverted timestamp.
        /// Inverted timestamp ensures newest versions come first in sorted order.
        /// </summary>
        private static byte[] CreateVersionedKey(byte[] key, long timestamp)
        {
            var result = new byte[key.Length + VERSION_SUFFIX_SIZE];
            key.CopyTo(result, 0);
            
            // Invert timestamp so newest comes first in sorted order
            var invertedTimestamp = long.MaxValue - timestamp;
            BinaryPrimitives.WriteInt64BigEndian(result.AsSpan(key.Length), invertedTimestamp);
            
            return result;
        }

        /// <summary>
        /// Creates the end key for version scanning (key + 0xFF suffix).
        /// </summary>
        private static byte[] CreateVersionedKeyEnd(byte[] key)
        {
            var result = new byte[key.Length + VERSION_SUFFIX_SIZE];
            key.CopyTo(result, 0);
            Array.Fill(result, (byte)0xFF, key.Length, VERSION_SUFFIX_SIZE);
            return result;
        }

        /// <summary>
        /// Extracts the original key from a versioned key.
        /// </summary>
        private static byte[] ExtractOriginalKey(byte[] versionedKey)
        {
            if (versionedKey.Length < VERSION_SUFFIX_SIZE)
                return versionedKey;
            
            return versionedKey.AsSpan(0, versionedKey.Length - VERSION_SUFFIX_SIZE).ToArray();
        }

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

            if (m_ownsStore)
            {
                m_innerStore.Dispose();
            }
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public string ProviderKey => PROVIDER_KEY_PREFIX + m_innerStore.ProviderKey;

        /// <summary>
        /// Gets the underlying key-value store.
        /// </summary>
        public IKeyValueStore InnerStore => m_innerStore;

        /// <summary>
        /// Gets the transaction timestamp manager.
        /// </summary>
        public ITransactionTimestampManager TimestampManager => m_timestampManager;

        #endregion
    }
}
