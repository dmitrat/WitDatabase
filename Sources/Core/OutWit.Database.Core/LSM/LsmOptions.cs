using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;

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
        /// <summary>
        /// Builds options from connection-string parameters.
        /// </summary>
        /// <remarks>
        /// This mapping existed in ProviderRegistration and was reachable only when a store was
        /// created through the provider registry. <c>WitDatabaseBuilder.BuildLsmStore</c>
        /// constructs <c>StoreLsm</c> directly and asked only for a ready-made options object,
        /// falling back to <c>new LsmOptions()</c> - so **every LSM setting in a connection string
        /// was silently dropped**: MemTableSize, SyncWrites, EnableWal, BlockSize,
        /// CompactionTrigger, the block cache, all of it. The secondary index stores did the same.
        ///
        /// It was invisible because the defaults are reasonable and because the one test that
        /// claimed to cover it - MemTableSizeParameterWorksTest - passed for a different reason:
        /// every commit used to flush the MemTable regardless of its size, so SSTables appeared
        /// whatever the limit said. Measured: 5 MB written with MemTableSize=1024 and with the 4 MB
        /// default produced exactly one SSTable each, where a 1 KB limit should produce thousands.
        ///
        /// Living on LsmOptions rather than in the registry is what makes it reachable from every
        /// place that builds a store - the registry, the main store, and the per-index stores.
        /// </remarks>
        public static LsmOptions FromParameters(ProviderParameters parameters)
        {
            var options = new LsmOptions();

            if (parameters == null)
                return options;

            // Both camelCase and PascalCase spellings are accepted, because a connection string is
            // written by hand and the ADO.NET builder does not normalise unknown keys.
            options.SyncWrites = Bool(parameters, options.SyncWrites, "SyncWrites", "syncWrites");
            options.EnableWal = Bool(parameters, options.EnableWal, "EnableWal", "enableWal");
            options.EnableBlockCache = Bool(parameters, options.EnableBlockCache,
                "EnableBlockCache", "enableBlockCache");
            options.BackgroundCompaction = Bool(parameters, options.BackgroundCompaction,
                "BackgroundCompaction", "backgroundCompaction");

            options.MemTableSizeLimit = Long(parameters, options.MemTableSizeLimit,
                "MemTableSize", "memTableSize", "MemTableSizeLimit");
            options.BlockCacheSizeBytes = Long(parameters, options.BlockCacheSizeBytes,
                "BlockCacheSize", "blockCacheSize", "BlockCacheSizeBytes");

            options.BlockSize = Int(parameters, options.BlockSize, "BlockSize", "blockSize");
            options.Level0CompactionTrigger = Int(parameters, options.Level0CompactionTrigger,
                "CompactionTrigger", "compactionTrigger", "Level0CompactionTrigger");

            return options;
        }

        private static bool Bool(ProviderParameters p, bool fallback, params string[] names)
        {
            foreach (var value in Values(p, names))
            {
                if (value is bool b)
                    return b;

                if (value is string s)
                {
                    switch (s.Trim().ToLowerInvariant())
                    {
                        case "true": case "yes": case "1": case "on": return true;
                        case "false": case "no": case "0": case "off": return false;
                    }

                    if (bool.TryParse(s, out var parsed))
                        return parsed;
                }
            }

            return fallback;
        }

        private static long Long(ProviderParameters p, long fallback, params string[] names)
        {
            foreach (var value in Values(p, names))
            {
                if (value is long l) return l;
                if (value is int i) return i;
                if (value is string s && long.TryParse(s, out var parsed)) return parsed;
            }

            return fallback;
        }

        private static int Int(ProviderParameters p, int fallback, params string[] names)
        {
            foreach (var value in Values(p, names))
            {
                if (value is int i) return i;
                if (value is long l) return (int)l;
                if (value is string s && int.TryParse(s, out var parsed)) return parsed;
            }

            return fallback;
        }

        private static IEnumerable<object> Values(ProviderParameters p, string[] names)
        {
            foreach (var name in names)
            {
                if (!p.Has(name))
                    continue;

                var value = p.Get<object?>(name, null);
                if (value != null)
                    yield return value;
            }
        }

    }
}

