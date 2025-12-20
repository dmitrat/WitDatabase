using OutWit.Database.Core.Comparers;

namespace OutWit.Database.Core.LSM
{
    /// <summary>
    /// Compactor merges multiple SSTables into fewer, larger tables.
    /// This reduces read amplification and reclaims space from tombstones.
    /// </summary>
    public sealed class Compactor
    {
        #region Fields

        private readonly string m_directory;

        private readonly int m_blockSize;

        private readonly ByteArrayComparer m_comparer = ByteArrayComparer.Default;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new Compactor for the specified directory.
        /// </summary>
        /// <param name="directory">Directory containing SSTables.</param>
        /// <param name="blockSize">Block size for output SSTables.</param>
        public Compactor(string directory, int blockSize = 4096)
        {
            m_directory = directory;
            m_blockSize = blockSize;
        }

        #endregion

        #region Functions

        /// <summary>
        /// Compacts multiple SSTables into a single SSTable.
        /// Removes duplicate keys (keeping newest) and tombstones.
        /// </summary>
        /// <param name="inputFiles">SSTable files to compact (oldest to newest).</param>
        /// <param name="outputFile">Output SSTable file path.</param>
        /// <returns>Compaction result with statistics.</returns>
        public CompactionResult Compact(IReadOnlyList<string> inputFiles, string outputFile)
        {
            if (inputFiles.Count == 0)
                return new CompactionResult { InputFiles = 0, OutputEntries = 0 };

            // Open all input readers
            var readers = inputFiles.Select(f => new SSTableReader(f)).ToList();

            try
            {
                // Create merge iterator
                var mergeIterator = new MergeIterator(readers, m_comparer);

                // Write to output
                int inputEntries = 0;
                int outputEntries = 0;
                int tombstonesRemoved = 0;

                using (var builder = new SSTableBuilder(outputFile, m_blockSize))
                {
                    byte[]? lastKey = null;

                    foreach (var (key, value, _) in mergeIterator.Iterate())
                    {
                        inputEntries++;

                        // Skip duplicate keys (we already have newest)
                        if (lastKey != null && m_comparer.Compare(key, lastKey) == 0)
                            continue;

                        lastKey = key;

                        // Skip tombstones during compaction (they've done their job)
                        if (value == null)
                        {
                            tombstonesRemoved++;
                            continue;
                        }

                        builder.Add(key, value);
                        outputEntries++;
                    }

                    builder.Finish();
                }

                return new CompactionResult
                {
                    InputFiles = inputFiles.Count,
                    InputEntries = inputEntries,
                    OutputEntries = outputEntries,
                    TombstonesRemoved = tombstonesRemoved,
                    OutputFile = outputFile
                };
            }
            finally
            {
                foreach (var reader in readers)
                {
                    reader.Dispose();
                }
            }
        }

        /// <summary>
        /// Performs level-based compaction on Level-0 SSTables.
        /// Merges all L0 files into a single file.
        /// </summary>
        /// <param name="level0Files">Level-0 SSTable files.</param>
        /// <param name="nextFileId">Next available file ID.</param>
        /// <returns>Compaction result, or null if no compaction needed.</returns>
        public CompactionResult? CompactLevel0(IReadOnlyList<string> level0Files, ref int nextFileId)
        {
            if (level0Files.Count < 2)
                return null;

            var outputPath = Path.Combine(m_directory, $"sst_{nextFileId:D6}.sst");
            nextFileId++;

            var result = Compact(level0Files, outputPath);

            // Delete old files
            foreach (var file in level0Files)
            {
                try { File.Delete(file); } catch { }
            }

            return result;
        }

        #endregion
    }
}
