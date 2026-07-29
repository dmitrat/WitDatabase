using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.LSM
{
    /// <summary>
    /// Configuration options for LSM-Tree.
    /// </summary>
    public sealed class LsmOptions
    {
        /// <summary>
        /// Maximum size of MemTable in bytes before flushing to SSTable.
        /// Default: 4 MB
        /// </summary>
        public long MemTableSizeLimit { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Target size for SSTable data blocks in bytes.
        /// Default: 4 KB
        /// </summary>
        public int BlockSize { get; set; } = 4096;

        /// <summary>
        /// Whether to enable Write-Ahead Log for durability.
        /// If false, data may be lost on crash but writes are faster.
        /// Default: true
        /// </summary>
        public bool EnableWal { get; set; } = true;

        /// <summary>
        /// Whether to sync WAL to disk after each write operation.
        /// If false, relies on OS buffering (faster but less durable per-write).
        /// Data is still synced on transaction commit and explicit Flush() calls.
        /// Default: false (matches SQLite behavior - sync on commit, not per-write)
        /// </summary>
        /// <remarks>
        /// Setting to true provides maximum durability but significantly impacts performance:
        /// - Each write triggers fsync (~0.5-1ms per call on SSD)
        /// - 10K writes with SyncWrites=true: ~10 seconds
        /// - 10K writes with SyncWrites=false: ~100-500ms
        /// 
        /// For most use cases, SyncWrites=false with proper transaction usage provides
        /// sufficient durability while maintaining good performance.
        /// </remarks>
        public bool SyncWrites { get; set; } = false;

        /// <summary>
        /// Maximum number of Level-0 SSTables before triggering compaction.
        /// Default: 4
        /// </summary>
        public int Level0CompactionTrigger { get; set; } = 4;

        /// <summary>
        /// Optional block encryptor for encrypting WAL and SSTables.
        /// Default: null (no encryption)
        /// </summary>
        public IBlockEncryptor? Encryptor { get; set; }

        /// <summary>
        /// Whether to enable block cache for SSTable reads.
        /// Default: true
        /// </summary>
        public bool EnableBlockCache { get; set; } = true;

        /// <summary>
        /// Maximum size of block cache in bytes.
        /// Only used if EnableBlockCache is true.
        /// Default: 64 MB
        /// </summary>
        public long BlockCacheSizeBytes { get; set; } = 64 * 1024 * 1024;

        /// <summary>
        /// Whether to run compaction in background thread.
        /// If false, compaction runs synchronously during flush.
        /// Default: true
        /// </summary>
        public bool BackgroundCompaction { get; set; } = true;

        /// <summary>
        /// Whether to enable parallel MemTable flush.
        /// When enabled, multiple MemTables can be flushed concurrently.
        /// Default: false
        /// </summary>
        public bool EnableParallelFlush { get; set; } = false;

        /// <summary>
        /// Maximum number of concurrent MemTable flush operations.
        /// Only used if EnableParallelFlush is true.
        /// Default: 2
        /// </summary>
        public int MaxParallelFlushes { get; set; } = 2;

        /// <summary>
        /// Maximum number of immutable MemTables waiting for flush.
        /// When exceeded, writes will block until flush completes.
        /// Only used if EnableParallelFlush is true.
        /// Default: 4
        /// </summary>
        public int MaxImmutableMemTables { get; set; } = 4;

        /// <summary>
        /// Creates default options.
        /// </summary>
        /// <summary>
        /// Where SSTable output files come from. Null uses ordinary files on disk.
        /// </summary>
        /// <remarks>
        /// The seam exists so a test can count the syncs the LSM path asks for, or fail one
        /// part-way. Both were impossible while SSTableBuilder opened its own FileStream, which is
        /// why two findings about this path sat unverified.
        /// </remarks>
        public ISstableFileFactory? SstableFileFactory { get; set; }

        public static LsmOptions Default => new();
    }
}

