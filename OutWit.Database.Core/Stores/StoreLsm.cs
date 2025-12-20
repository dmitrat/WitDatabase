using System.Runtime.CompilerServices;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;

namespace OutWit.Database.Core.Stores
{
    /// <summary>
    /// Main LSM-Tree implementation providing key-value storage.
    /// Combines MemTable, WAL, and SSTables for efficient reads and writes.
    /// Thread-safe: concurrent reads allowed, writes are serialized.
    /// </summary>
    public sealed class StoreLsm : IKeyValueStore
    {
        #region Constants

        /// <summary>
        /// Provider key for LSM-Tree store.
        /// </summary>
        public const string PROVIDER_KEY = "lsm";

        #endregion

        #region Fields

        private readonly string m_directory;
        private readonly LsmOptions m_options;
        private readonly LsmStatistics m_statistics = new();
        private readonly ReaderWriterLockSlim m_sstableLock = new(LockRecursionPolicy.NoRecursion);
        private readonly Lock m_writeLock = new();
        private readonly Lock m_compactionLock = new();
        private readonly List<SSTableReader> m_sstables = [];
        private readonly BlockCache? m_blockCache;

        private MemTable m_activeMemTable;
        private MemTable? m_immutableMemTable;
        private WriteAheadLog? m_wal;
        private Task? m_compactionTask;
        private int m_nextSstableId;
        private bool m_compactionPending;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates or opens an LSM-Tree in the specified directory.
        /// </summary>
        /// <param name="directory">Directory for data storage.</param>
        /// <param name="options">Configuration options.</param>
        public StoreLsm(string directory, LsmOptions? options = null)
        {
            m_directory = directory;
            m_options = options ?? LsmOptions.Default;
            m_activeMemTable = new MemTable();

            // Initialize block cache if enabled
            if (m_options.EnableBlockCache)
            {
                m_blockCache = new BlockCache(m_options.BlockCacheSizeBytes);
            }

            // Ensure directory exists
            System.IO.Directory.CreateDirectory(directory);

            // Initialize WAL if enabled
            if (m_options.EnableWal)
            {
                var walPath = Path.Combine(directory, "wal.log");
                m_wal = new WriteAheadLog(walPath, createNew: false, encryptor: m_options.Encryptor);
            }

            // Load existing SSTables and recover from WAL
            Recover();
        }

        #endregion

        #region Get

        /// <summary>
        /// Gets the value for a key.
        /// </summary>
        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            m_statistics.RecordGet();

            // 1. Check active MemTable (thread-safe via MemTable's internal lock)
            if (m_activeMemTable.TryGet(key, out var value))
            {
                return value; // null = tombstone (deleted)
            }

            // 2. Check immutable MemTable (if flushing)
            var immutable = Volatile.Read(ref m_immutableMemTable);
            if (immutable?.TryGet(key, out value) == true)
            {
                return value;
            }

            // 3. Check SSTables (newest to oldest) - read lock allows concurrent readers
            m_sstableLock.EnterReadLock();
            try
            {
                for (int i = m_sstables.Count - 1; i >= 0; i--)
                {
                    if (m_sstables[i].TryGet(key, out value))
                    {
                        m_statistics.RecordBloomFilterMiss(); // Had to read from disk
                        return value;
                    }
                }
                // If we checked SSTables but found nothing, bloom filters helped
                if (m_sstables.Count > 0)
                {
                    m_statistics.RecordBloomFilterHit();
                }
            }
            finally
            {
                m_sstableLock.ExitReadLock();
            }

            return null;
        }

        /// <inheritdoc/>
        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Get(key));
        }

        #endregion

        #region Put

        /// <summary>
        /// Inserts or updates a key-value pair.
        /// </summary>
        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            ThrowIfDisposed();
            m_statistics.RecordPut();
            m_statistics.RecordBytesWritten(key.Length + value.Length);

            lock (m_writeLock)
            {
                // Write to WAL first for durability
                m_wal?.AppendPut(key, value);
                if (m_options.SyncWrites)
                {
                    m_wal?.Sync();
                }

                // Write to MemTable
                m_activeMemTable.Put(key, value);

                // Check if MemTable needs to be flushed
                if (m_activeMemTable.ApproximateSize >= m_options.MemTableSizeLimit)
                {
                    FlushMemTableInternal();
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Put(key, value);
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Delete

        /// <summary>
        /// Deletes a key from the store.
        /// </summary>
        public bool Delete(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            m_statistics.RecordDelete();

            lock (m_writeLock)
            {
                // Write tombstone to WAL
                m_wal?.AppendDelete(key);
                if (m_options.SyncWrites)
                {
                    m_wal?.Sync();
                }

                // Write tombstone to MemTable
                m_activeMemTable.Delete(key);

                // Check if MemTable needs to be flushed
                if (m_activeMemTable.ApproximateSize >= m_options.MemTableSizeLimit)
                {
                    FlushMemTableInternal();
                }
            }

            return true; // LSM-Tree always "deletes" (writes tombstone)
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Delete(key));
        }

        #endregion

        #region Scan

        /// <summary>
        /// Scans key-value pairs in the specified range.
        /// Uses streaming merge to avoid materializing all entries in memory.
        /// </summary>
        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
        {
            ThrowIfDisposed();
            m_statistics.RecordScan();

            var comparer = ByteArrayComparer.Default;
            
            // Collect all sources with their priorities (higher = newer)
            var sources = new List<(IEnumerable<(byte[] Key, byte[]? Value)> Source, int Priority)>();
            int priority = int.MaxValue;

            // 1. Active MemTable (highest priority)
            sources.Add((m_activeMemTable.Scan(startKey, endKey), priority--));

            // 2. Immutable MemTable
            var immutable = Volatile.Read(ref m_immutableMemTable);
            if (immutable != null)
            {
                sources.Add((immutable.Scan(startKey, endKey), priority--));
            }

            // 3. SSTables (newest to oldest)
            m_sstableLock.EnterReadLock();
            try
            {
                for (int i = m_sstables.Count - 1; i >= 0; i--)
                {
                    sources.Add((m_sstables[i].Scan(startKey, endKey), priority--));
                }
            }
            finally
            {
                m_sstableLock.ExitReadLock();
            }

            // Use heap-based merge to stream results
            return MergeScan(sources, comparer);
        }

        /// <summary>
        /// Merges multiple sorted sources using a priority queue.
        /// </summary>
        private static IEnumerable<(byte[] Key, byte[] Value)> MergeScan(
            List<(IEnumerable<(byte[] Key, byte[]? Value)> Source, int Priority)> sources,
            ByteArrayComparer comparer)
        {
            // Initialize iterators and heap
            var iterators = sources.Select(s => s.Source.GetEnumerator()).ToArray();
            var priorities = sources.Select(s => s.Priority).ToArray();
            
            var heap = new PriorityQueue<(byte[] Key, byte[]? Value, int SourceIndex), (byte[] Key, int InversePriority)>(
                LsmMergePriorityComparer.Instance);

            // Populate heap with first entry from each source
            for (int i = 0; i < iterators.Length; i++)
            {
                if (iterators[i].MoveNext())
                {
                    var entry = iterators[i].Current;
                    // Use negative priority so higher priority sources come first for same key
                    heap.Enqueue((entry.Key, entry.Value, i), (entry.Key, -priorities[i]));
                }
            }

            byte[]? lastKey = null;

            while (heap.Count > 0)
            {
                var (key, value, sourceIndex) = heap.Dequeue();

                // Skip duplicates (we already yielded the newest version)
                if (lastKey != null && comparer.Compare(lastKey, key) == 0)
                {
                    // Advance this source and re-add to heap
                    if (iterators[sourceIndex].MoveNext())
                    {
                        var next = iterators[sourceIndex].Current;
                        heap.Enqueue((next.Key, next.Value, sourceIndex), (next.Key, -priorities[sourceIndex]));
                    }
                    continue;
                }

                lastKey = key;

                // Skip tombstones
                if (value != null)
                {
                    yield return (key, value);
                }

                // Advance the source iterator
                if (iterators[sourceIndex].MoveNext())
                {
                    var next = iterators[sourceIndex].Current;
                    heap.Enqueue((next.Key, next.Value, sourceIndex), (next.Key, -priorities[sourceIndex]));
                }
            }

            // Dispose iterators
            foreach (var iter in iterators)
            {
                iter.Dispose();
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

        /// <summary>
        /// Forces flush of MemTable to SSTable.
        /// </summary>
        public void Flush()
        {
            ThrowIfDisposed();

            lock (m_writeLock)
            {
                if (m_activeMemTable.Count > 0)
                {
                    FlushMemTableInternal();
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Flush();
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Compaction

        /// <summary>
        /// Forces compaction of SSTables. Waits for completion.
        /// </summary>
        public void Compact()
        {
            ThrowIfDisposed();
            
            if (m_options.BackgroundCompaction)
            {
                // Trigger and wait for background compaction
                ScheduleBackgroundCompaction();
                WaitForCompaction();
            }
            else
            {
                ExecuteCompaction();
            }
        }

        /// <summary>
        /// Waits for any pending background compaction to complete.
        /// </summary>
        public void WaitForCompaction()
        {
            var task = Volatile.Read(ref m_compactionTask);
            task?.Wait();
        }

        private void ScheduleBackgroundCompaction()
        {
            lock (m_compactionLock)
            {
                // Check if compaction is needed
                m_sstableLock.EnterReadLock();
                try
                {
                    if (m_sstables.Count < m_options.Level0CompactionTrigger)
                        return;
                }
                finally
                {
                    m_sstableLock.ExitReadLock();
                }

                // Check if compaction is already pending
                if (m_compactionPending)
                    return;

                m_compactionPending = true;

                // Start background compaction
                m_compactionTask = Task.Run(() =>
                {
                    try
                    {
                        ExecuteCompaction();
                    }
                    finally
                    {
                        lock (m_compactionLock)
                        {
                            m_compactionPending = false;
                        }
                    }
                });
            }
        }

        private void ExecuteCompaction()
        {
            // Get current SSTable files
            List<string> filesToCompact;
            int currentSstableCount;
            m_sstableLock.EnterReadLock();
            try
            {
                currentSstableCount = m_sstables.Count;
                if (currentSstableCount < m_options.Level0CompactionTrigger)
                    return;

                filesToCompact = m_sstables.Select(s => s.FilePath).ToList();
            }
            finally
            {
                m_sstableLock.ExitReadLock();
            }

            // Generate output path
            // Use lock to prevent race with FlushMemTableInternal
            int outputId;
            lock (m_writeLock)
            {
                outputId = m_nextSstableId++;
            }
            var outputPath = Path.Combine(m_directory, $"sst_{outputId:D6}.sst");

            // Perform compaction (outside of any locks)
            var compactor = new Compactor(m_directory, m_options.BlockSize);
            var result = compactor.Compact(filesToCompact, outputPath);
            
            m_statistics.RecordCompaction();
            
            if (result.OutputEntries == 0 && filesToCompact.Count > 0)
            {
                // All entries were tombstones, delete output
                try { File.Delete(outputPath); } catch { }
                outputPath = null;
            }

            // Swap SSTables atomically
            m_sstableLock.EnterWriteLock();
            try
            {
                // Verify SSTables haven't changed during compaction
                // If they have, abort this compaction
                var currentFiles = m_sstables.Select(s => s.FilePath).ToList();
                if (!filesToCompact.SequenceEqual(currentFiles))
                {
                    // SSTables changed, delete our output and abort
                    if (outputPath != null)
                    {
                        try { File.Delete(outputPath); } catch { }
                    }
                    return;
                }

                // Invalidate cache for old files
                if (m_blockCache != null)
                {
                    foreach (var file in filesToCompact)
                    {
                        m_blockCache.Invalidate(file);
                    }
                }

                // Close old readers
                foreach (var sst in m_sstables)
                {
                    sst.Dispose();
                }
                m_sstables.Clear();

                // Delete old files
                foreach (var file in filesToCompact)
                {
                    try { File.Delete(file); } catch { }
                }

                // Open new compacted SSTable with cache
                if (outputPath != null && File.Exists(outputPath))
                {
                    m_sstables.Add(new SSTableReader(outputPath, m_options.Encryptor, m_blockCache));
                }
            }
            finally
            {
                m_sstableLock.ExitWriteLock();
            }
        }

        #endregion

        #region Functions

        /// <summary>
        /// Flushes MemTable to SSTable. Must be called with m_writeLock held.
        /// </summary>
        private void FlushMemTableInternal()
        {
            m_statistics.RecordFlush();
            
            // Move active to immutable
            var oldActive = m_activeMemTable;
            Volatile.Write(ref m_immutableMemTable, oldActive);
            m_activeMemTable = new MemTable();

            // Generate SSTable path (m_writeLock already held, so safe)
            var sstableId = m_nextSstableId++;
            var sstablePath = Path.Combine(m_directory, $"sst_{sstableId:D6}.sst");

            using (var builder = new SSTableBuilder(sstablePath, m_options.BlockSize, m_options.Encryptor))
            {
                foreach (var entry in oldActive.GetAllEntries())
                {
                    builder.Add(entry.Key, entry.Value);
                }
                builder.Finish();
            }

            // Add new SSTable to list (write lock to prevent concurrent reads during add)
            m_sstableLock.EnterWriteLock();
            try
            {
                m_sstables.Add(new SSTableReader(sstablePath, m_options.Encryptor, m_blockCache));
            }
            finally
            {
                m_sstableLock.ExitWriteLock();
            }

            // Clear immutable MemTable
            oldActive.Dispose();
            Volatile.Write(ref m_immutableMemTable, null);

            // Truncate WAL
            m_wal?.Truncate();

            // Check if compaction needed
            if (m_sstables.Count >= m_options.Level0CompactionTrigger)
            {
                if (m_options.BackgroundCompaction)
                {
                    ScheduleBackgroundCompaction();
                }
                else
                {
                    ExecuteCompaction();
                }
            }
        }

        private void Recover()
        {
            // Load existing SSTables
            var sstableFiles = System.IO.Directory.GetFiles(m_directory, "sst_*.sst")
                .OrderBy(f => f)
                .ToList();

            foreach (var file in sstableFiles)
            {
                m_sstables.Add(new SSTableReader(file, m_options.Encryptor, m_blockCache));

                // Extract ID from filename
                var filename = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(filename.Replace("sst_", ""), out var id))
                {
                    m_nextSstableId = Math.Max(m_nextSstableId, id + 1);
                }
            }

            // Replay WAL
            if (m_wal != null)
            {
                var replayed = m_wal.Replay(
                    onPut: (key, value) => m_activeMemTable.Put(key, value),
                    onDelete: (key) => m_activeMemTable.Delete(key)
                );

                if (replayed > 0)
                {
                    // WAL had entries - flush if large
                    if (m_activeMemTable.ApproximateSize >= m_options.MemTableSizeLimit)
                    {
                        FlushMemTableInternal();
                    }
                }
            }
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

            // Wait for background compaction to complete
            WaitForCompaction();

            lock (m_writeLock)
            {
                // Flush remaining data
                if (m_activeMemTable.Count > 0)
                {
                    try { FlushMemTableInternal(); } catch { /* Best effort */ }
                }

                m_activeMemTable.Dispose();
                m_immutableMemTable?.Dispose();
                m_wal?.Dispose();

                m_sstableLock.EnterWriteLock();
                try
                {
                    foreach (var sst in m_sstables)
                    {
                        sst.Dispose();
                    }
                    m_sstables.Clear();
                }
                finally
                {
                    m_sstableLock.ExitWriteLock();
                }
            }

            m_sstableLock.Dispose();
            m_blockCache?.Dispose();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the directory where data is stored.
        /// </summary>
        public string Directory => m_directory;

        /// <summary>
        /// Gets the number of SSTables on disk.
        /// </summary>
        public int SSTableCount
        {
            get
            {
                m_sstableLock.EnterReadLock();
                try
                {
                    return m_sstables.Count;
                }
                finally
                {
                    m_sstableLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets the block cache, if enabled. Returns null if caching is disabled.
        /// </summary>
        public BlockCache? BlockCache => m_blockCache;

        /// <summary>
        /// Gets whether a background compaction is currently running.
        /// </summary>
        public bool IsCompacting => Volatile.Read(ref m_compactionPending);

        /// <summary>
        /// Gets the statistics for this LSM-Tree.
        /// </summary>
        public LsmStatistics Statistics => m_statistics;

        /// <inheritdoc/>
        public string ProviderKey => PROVIDER_KEY;

        #endregion
    }
}
