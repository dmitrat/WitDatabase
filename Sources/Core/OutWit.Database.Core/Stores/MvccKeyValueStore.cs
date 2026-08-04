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
    public sealed class MvccKeyValueStore : IMvccStore, IAsyncDisposable
    {
        #region Constants

        private const int VERSION_SUFFIX_SIZE = 8;
        private const string PROVIDER_KEY_PREFIX = "mvcc:";
        
        /// <summary>
        /// Key for storing the maximum timestamp used in the MVCC store.
        /// This enables O(1) timestamp recovery on database reopen.
        /// </summary>
        private static readonly byte[] MAX_TIMESTAMP_KEY = "$mvcc:max_timestamp"u8.ToArray();

        #endregion

        #region Fields

        private readonly IKeyValueStore m_innerStore;
        private readonly ITransactionTimestampManager m_timestampManager;
        private readonly bool m_ownsStore;
        private readonly ByteArrayComparer m_comparer;
        private readonly object m_maxTimestampLock = new();
        private long m_cachedMaxTimestamp;
        private bool m_maxTimestampDirty;
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
            
            // Load cached max timestamp from store
            m_cachedMaxTimestamp = ReadCachedMaxTimestamp(innerStore);
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// Reads the cached maximum timestamp from the store.
        /// This is the preferred method for initializing timestamp manager on database reopen.
        /// Returns 0 if no cached value exists.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <returns>The cached max timestamp, or 0 if not found.</returns>
        public static long ReadCachedMaxTimestamp(IKeyValueStore innerStore)
        {
            if (innerStore == null)
                throw new ArgumentNullException(nameof(innerStore));

            var data = innerStore.Get(MAX_TIMESTAMP_KEY);
            if (data == null || data.Length != 8)
                return 0;

            return BinaryPrimitives.ReadInt64LittleEndian(data);
        }

        /// <summary>
        /// Reads the cached maximum timestamp from the store asynchronously.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The cached max timestamp, or 0 if not found.</returns>
        public static async ValueTask<long> ReadCachedMaxTimestampAsync(
            IKeyValueStore innerStore,
            CancellationToken cancellationToken = default)
        {
            if (innerStore == null)
                throw new ArgumentNullException(nameof(innerStore));

            var data = await innerStore.GetAsync(MAX_TIMESTAMP_KEY, cancellationToken).ConfigureAwait(false);
            if (data == null || data.Length != 8)
                return 0;

            return BinaryPrimitives.ReadInt64LittleEndian(data);
        }

        /// <summary>
        /// Scans an inner store to find the maximum timestamp among existing MVCC records.
        /// This is a fallback method when cached value is not available.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store containing MVCC records.</param>
        /// <returns>The maximum timestamp found, or 0 if no records exist.</returns>
        /// <remarks>
        /// This method performs a full scan of the store, which may be slow for large databases.
        /// Prefer using <see cref="ReadCachedMaxTimestamp"/> when possible.
        /// </remarks>
        public static long FindMaxTimestamp(IKeyValueStore innerStore)
        {
            if (innerStore == null)
                throw new ArgumentNullException(nameof(innerStore));

            long maxTimestamp = 0;
            var comparer = ByteArrayComparer.Default;

            foreach (var (key, data) in innerStore.Scan(null, null))
            {
                // Skip the max_timestamp metadata key itself
                if (comparer.Compare(key, MAX_TIMESTAMP_KEY) == 0)
                    continue;
                    
                if (!MvccRecord.TryDeserialize(data, out var record))
                    continue;

                // Check CreateTimestamp
                if (record.CreateTimestamp > maxTimestamp)
                    maxTimestamp = record.CreateTimestamp;

                // Check CommitTimestamp (if it's a valid timestamp)
                if (record.CommitTimestamp > 0 && record.CommitTimestamp < long.MaxValue && 
                    record.CommitTimestamp > maxTimestamp)
                    maxTimestamp = record.CommitTimestamp;

                // Check DeleteTimestamp (if not NOT_DELETED)
                if (record.DeleteTimestamp != MvccRecord.NOT_DELETED && 
                    record.DeleteTimestamp > maxTimestamp)
                    maxTimestamp = record.DeleteTimestamp;
            }

            return maxTimestamp;
        }
        
        /// <summary>
        /// Scans an inner store asynchronously to find the maximum timestamp among existing MVCC records.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store containing MVCC records.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The maximum timestamp found, or 0 if no records exist.</returns>
        /// <remarks>
        /// This method performs a full scan of the store, which may be slow for large databases.
        /// Prefer using <see cref="ReadCachedMaxTimestampAsync"/> when possible.
        /// </remarks>
        public static async ValueTask<long> FindMaxTimestampAsync(
            IKeyValueStore innerStore, 
            CancellationToken cancellationToken = default)
        {
            if (innerStore == null)
                throw new ArgumentNullException(nameof(innerStore));

            long maxTimestamp = 0;
            var comparer = ByteArrayComparer.Default;

            await foreach (var (key, data) in innerStore.ScanAsync(null, null, cancellationToken).ConfigureAwait(false))
            {
                // Skip the max_timestamp metadata key itself
                if (comparer.Compare(key, MAX_TIMESTAMP_KEY) == 0)
                    continue;
                    
                if (!MvccRecord.TryDeserialize(data, out var record))
                    continue;

                if (record.CreateTimestamp > maxTimestamp)
                    maxTimestamp = record.CreateTimestamp;

                if (record.CommitTimestamp > 0 && record.CommitTimestamp < long.MaxValue && 
                    record.CommitTimestamp > maxTimestamp)
                    maxTimestamp = record.CommitTimestamp;

                if (record.DeleteTimestamp != MvccRecord.NOT_DELETED && 
                    record.DeleteTimestamp > maxTimestamp)
                    maxTimestamp = record.DeleteTimestamp;
            }

            return maxTimestamp;
        }

        /// <summary>
        /// Recovers the maximum timestamp, preferring cached value over full scan.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <returns>The maximum timestamp found.</returns>
        public static long RecoverMaxTimestamp(IKeyValueStore innerStore)
        {
            // First try to read cached value (O(1))
            var cached = ReadCachedMaxTimestamp(innerStore);
            if (cached > 0)
                return cached;

            // Fallback to full scan (O(n)) for databases created before caching was added
            return FindMaxTimestamp(innerStore);
        }

        /// <summary>
        /// Recovers the maximum timestamp asynchronously, preferring cached value over full scan.
        /// </summary>
        /// <param name="innerStore">The underlying key-value store.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The maximum timestamp found.</returns>
        public static async ValueTask<long> RecoverMaxTimestampAsync(
            IKeyValueStore innerStore,
            CancellationToken cancellationToken = default)
        {
            // First try to read cached value (O(1))
            var cached = await ReadCachedMaxTimestampAsync(innerStore, cancellationToken).ConfigureAwait(false);
            if (cached > 0)
                return cached;

            // Fallback to full scan (O(n)) for databases created before caching was added
            return await FindMaxTimestampAsync(innerStore, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Get

        /// <inheritdoc/>
        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            // For non-transactional reads, use MaxValue to see all committed data
            // This handles the case when database is reopened with fresh timestamp manager
            return GetAsOf(key, long.MaxValue, transactionId: 0);
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

            // Snapshots read the published watermark, so a write that never publishes would be
            // invisible to every transaction, for ever. Publishing after the version is installed
            // gives this single-key write the same all-or-nothing property a commit has.
            m_timestampManager.Publish(timestamp);
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
            
            // Update cached max timestamp
            UpdateCachedMaxTimestamp(timestamp);
        }

        #endregion

        #region Delete

        /// <inheritdoc/>
        public bool Delete(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            var timestamp = m_timestampManager.GetNextTimestamp();
            var deleted = DeleteVersion(key, timestamp, transactionId: 0);

            // See Put: unpublished writes are invisible to snapshot readers.
            m_timestampManager.Publish(timestamp);

            return deleted;
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
            var result = MarkPreviousVersionDeleted(keyArray, timestamp, transactionId);
            
            if (result)
            {
                // Update cached max timestamp (delete timestamp is used)
                UpdateCachedMaxTimestamp(timestamp);
            }
            
            return result;
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
                // Skip metadata keys
                if (IsMetadataKey(key))
                    continue;
                    
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
            
            // Update cached max timestamp
            UpdateCachedMaxTimestamp(commitTimestamp);
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
                // Skip metadata keys
                if (IsMetadataKey(key))
                    continue;
                    
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
            // For non-transactional reads, use MaxValue to see all committed data
            // This handles the case when database is reopened with fresh timestamp manager
            return ScanAsOf(startKey, endKey, long.MaxValue, transactionId: 0);
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
                // Skip metadata keys
                if (IsMetadataKey(versionedKey))
                    continue;
                    
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
            PersistMaxTimestampIfNeeded();
            m_innerStore.Flush();
        }

        /// <inheritdoc/>
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            PersistMaxTimestampIfNeeded();
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
        /// Updates the cached max timestamp if the new timestamp is greater.
        /// Uses in-memory cache to avoid I/O on every write.
        /// Persists to store on Flush or periodic intervals.
        /// </summary>
        private void UpdateCachedMaxTimestamp(long timestamp)
        {
            lock (m_maxTimestampLock)
            {
                if (timestamp > m_cachedMaxTimestamp)
                {
                    m_cachedMaxTimestamp = timestamp;
                    m_maxTimestampDirty = true;
                }
            }
        }
        
        /// <summary>
        /// Persists the cached max timestamp to the store if dirty.
        /// Called on Flush and Dispose.
        /// </summary>
        private void PersistMaxTimestampIfNeeded()
        {
            long timestampToWrite;
            lock (m_maxTimestampLock)
            {
                if (!m_maxTimestampDirty)
                    return;
                    
                timestampToWrite = m_cachedMaxTimestamp;
                m_maxTimestampDirty = false;
            }
            
            Span<byte> data = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(data, timestampToWrite);
            m_innerStore.Put(MAX_TIMESTAMP_KEY, data);
        }
        
        /// <summary>
        /// Checks whether a key is this store's own metadata rather than a versioned record.
        /// </summary>
        /// <remarks>
        /// This used to be "starts with <c>$</c>", which swallowed a whole namespace that is not this
        /// store's to claim. The SQL engine keeps its schema catalog under <c>$schema:</c>, so a row
        /// count written inside a transaction was committed and then skipped here - left uncommitted,
        /// invisible to every reader, while the transaction reported success.
        ///
        /// There is exactly one metadata key, and the rest of this class already matches it exactly
        /// (see the scan filters). The <c>MvccRecord.TryDeserialize</c> check at every call site
        /// rejects anything that is not a versioned record anyway, so the prefix test was never
        /// carrying its weight - only its collision.
        /// </remarks>
        private static bool IsMetadataKey(byte[] key)
        {
            return key.AsSpan().SequenceEqual(MAX_TIMESTAMP_KEY);
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

            // Persist max timestamp before disposing
            try
            {
                PersistMaxTimestampIfNeeded();
            }
            catch
            {
                // Ignore errors during dispose - store might already be disposed
            }

            if (m_ownsStore)
            {
                m_innerStore.Dispose();
            }
        }

        /// <summary>
        /// The same shutdown, passing the asynchronous close down to the store underneath.
        /// </summary>
        /// <remarks>
        /// The link that would otherwise break the chain in the default configuration: the MVCC store
        /// sits between the transactional store and the B+Tree one, and a synchronous close here reaches
        /// a synchronous storage write however asynchronous both its neighbours are.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed) return;
            m_disposed = true;

            try
            {
                PersistMaxTimestampIfNeeded();
            }
            catch
            {
                // Ignore errors during dispose - store might already be disposed
            }

            if (!m_ownsStore)
                return;

            if (m_innerStore is IAsyncDisposable asyncStore)
                await asyncStore.DisposeAsync().ConfigureAwait(false);
            else
                m_innerStore.Dispose();
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
