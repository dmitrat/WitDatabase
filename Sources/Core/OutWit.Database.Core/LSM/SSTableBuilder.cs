using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.LSM
{
    /// <summary>
    /// Builds immutable SSTable files from sorted key-value pairs.
    /// 
    /// SSTable Format V2 (with Bloom filter):
    /// [Data Block 1] [Data Block 2] ... [Data Block N] [Index Block] [Bloom Filter] [Footer]
    /// 
    /// Data Block: [Entry1][Entry2]...[EntryN]
    /// Entry: [KeyLen:4][Key][ValueLen:4][Value] (ValueLen = -1 for tombstone)
    /// 
    /// Index Block: [FirstKey1][BlockOffset1][BlockSize1]...
    /// 
    /// Bloom Filter: [FilterData:N bytes]
    /// 
    /// Footer V2 (40 bytes):
    /// [IndexOffset:8][IndexSize:4][EntryCount:4][Flags:4]
    /// [BloomOffset:8][BloomSize:4][BloomHashCount:4]
    /// [Magic:4]
    /// 
    /// Flags: bit 0 = encrypted, bit 1 = has bloom filter
    /// </summary>
    public sealed class SSTableBuilder : IDisposable
    {
        #region Constants

        private const uint MAGIC = 0x53535431; // "SST1"
        private const uint FLAG_ENCRYPTED = 0x01;
        private const uint FLAG_HAS_BLOOM = 0x02;
        internal const long INDEX_BLOCK_ID = -1; // Special block ID for index block encryption
        internal const long BLOOM_BLOCK_ID = -2; // Special block ID for bloom filter encryption
        private const int DEFAULT_BLOCK_SIZE = 4096;
        private const int FOOTER_SIZE_V2 = 44; // Updated to include bloomBitSize

        #endregion

        #region Fields

        private readonly ISstableFile m_file;
        private readonly Stream m_stream;
        private readonly BinaryWriter m_writer;
        private readonly int m_targetBlockSize;
        private readonly IBlockEncryptor? m_encryptor;
        private readonly List<IndexEntry> m_indexEntries = [];
        private readonly List<byte[]> m_keys = []; // Store keys for Bloom filter
    
        private MemoryStream m_currentBlock = new();
        private byte[]? m_currentBlockFirstKey;
        private int m_entryCount;
        private int m_blockCounter;
        private bool m_finished;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new SSTableBuilder.
        /// </summary>
        /// <param name="filePath">Output file path.</param>
        /// <param name="targetBlockSize">Target size for data blocks.</param>
        /// <param name="encryptor">Optional block encryptor.</param>
        /// <param name="fileFactory">
        /// Where the output file comes from. Defaults to an ordinary file on disk; a test supplies
        /// its own to count the syncs this builder asks for, or to fail one part-way.
        /// </param>
        public SSTableBuilder(
            string filePath, 
            int targetBlockSize = DEFAULT_BLOCK_SIZE, 
            IBlockEncryptor? encryptor = null,
            ISstableFileFactory? fileFactory = null)
        {
            FilePath = filePath;
            m_targetBlockSize = targetBlockSize;
            m_encryptor = encryptor;
            m_file = (fileFactory ?? SstableFileFactory.Default).Create(filePath);
            m_stream = m_file.Stream;
            m_writer = new BinaryWriter(m_stream);
        }

        #endregion

        #region Functions

        /// <summary>
        /// Adds a key-value pair. Must be called in sorted key order!
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value, or null for tombstone.</param>
        public void Add(byte[] key, byte[]? value)
        {
            ThrowIfDisposed();
            if (m_finished)
                throw new InvalidOperationException("SSTableBuilder has already been finished.");

            // Store key for Bloom filter (built at finish time)
            m_keys.Add(key);

            // Entry format: [KeyLen:4][Key][ValueLen:4][Value]
            // ValueLen = -1 for tombstone
            var entrySize = 4 + key.Length + 4 + (value?.Length ?? 0);

            // Check if we need to start a new block
            if (m_currentBlock.Length > 0 && m_currentBlock.Length + entrySize > m_targetBlockSize)
            {
                FlushBlock();
            }

            // Track first key of block
            if (m_currentBlock.Length == 0)
            {
                m_currentBlockFirstKey = key;
            }

            // Write entry to current block
            var entryWriter = new BinaryWriter(m_currentBlock);
            entryWriter.Write(key.Length);
            entryWriter.Write(key);
            entryWriter.Write(value?.Length ?? -1);
            if (value != null)
            {
                entryWriter.Write(value);
            }

            m_entryCount++;
        }

        /// <summary>
        /// Finishes writing the SSTable and returns metadata.
        /// </summary>
        /// <returns>SSTable metadata.</returns>
        public SSTableInfo Finish()
        {
            ThrowIfDisposed();
            if (m_finished)
                throw new InvalidOperationException("SSTableBuilder has already been finished.");

            // Flush remaining block
            if (m_currentBlock.Length > 0)
            {
                FlushBlock();
            }

            // Write index block
            var indexOffset = m_stream.Position;
            using var indexStream = new MemoryStream();
            using var indexWriter = new BinaryWriter(indexStream);
        
            foreach (var entry in m_indexEntries)
            {
                indexWriter.Write(entry.FirstKey.Length);
                indexWriter.Write(entry.FirstKey);
                indexWriter.Write(entry.BlockOffset);
                indexWriter.Write(entry.BlockSize);
            }
        
            var indexData = indexStream.ToArray();
            WriteBlock(indexData, INDEX_BLOCK_ID);
            var indexSize = (int)(m_stream.Position - indexOffset);

            // Build and write Bloom filter (now with correct size)
            var bloomOffset = m_stream.Position;
            var bloomFilter = new BloomFilter(Math.Max(100, m_entryCount), 0.01);
            foreach (var key in m_keys)
            {
                bloomFilter.Add(key);
            }
            var bloomData = bloomFilter.ToBytes();
            WriteBlock(bloomData, BLOOM_BLOCK_ID);
            var bloomSizeInBytes = bloomData.Length;
            var bloomBitSize = bloomFilter.Size; // Original bit size (may be less than bytes * 8)
            var bloomHashCount = bloomFilter.HashCount;

            // Write footer (never encrypted) - 44 bytes now (added bloomBitSize)
            uint flags = FLAG_HAS_BLOOM;
            if (m_encryptor != null) flags |= FLAG_ENCRYPTED;
            
            m_writer.Write(indexOffset);         // 8 bytes [0-7]
            m_writer.Write(indexSize);           // 4 bytes [8-11]
            m_writer.Write(m_entryCount);        // 4 bytes [12-15]
            m_writer.Write(flags);               // 4 bytes [16-19]
            m_writer.Write(bloomOffset);         // 8 bytes [20-27]
            m_writer.Write(bloomSizeInBytes);    // 4 bytes [28-31] - size on disk
            m_writer.Write(bloomBitSize);        // 4 bytes [32-35] - original bit size
            m_writer.Write(bloomHashCount);      // 4 bytes [36-39]
            m_writer.Write(MAGIC);               // 4 bytes [40-43] = total 44

            m_writer.Flush();

            // And ask for the media, not just the operating system. Without this the WAL holding the
            // same data was truncated while the SSTable was still in the page cache, so a power
            // failure in that window lost everything the memtable had accumulated - with SyncWrites
            // set as well, because SyncWrites only ever synced the WAL.
            //
            // It is unconditional rather than tied to SyncWrites: a memtable flush is the moment the
            // only other durable copy is destroyed, and it happens once per memtable rather than per
            // write, so the cost is one sync per few megabytes.
            m_file.Sync();

            m_finished = true;

            return new SSTableInfo
            {
                FilePath = FilePath,
                EntryCount = m_entryCount,
                FileSize = m_stream.Length,
                BlockCount = m_indexEntries.Count,
                Encrypted = m_encryptor != null,
                HasBloomFilter = true
            };
        }

        private void FlushBlock()
        {
            var blockOffset = m_stream.Position;
            var blockData = m_currentBlock.ToArray();
        
            var writtenSize = WriteBlock(blockData, m_blockCounter++);

            m_indexEntries.Add(new IndexEntry
            {
                FirstKey = m_currentBlockFirstKey!,
                BlockOffset = blockOffset,
                BlockSize = writtenSize
            });

            m_currentBlock = new MemoryStream();
            m_currentBlockFirstKey = null;
        }

        private int WriteBlock(byte[] data, long blockId)
        {
            if (m_encryptor != null)
            {
                var encrypted = m_encryptor.Encrypt(data, blockId);
                m_writer.Write(encrypted.Length);
                m_stream.Write(encrypted);
                return 4 + encrypted.Length;
            }
            else
            {
                m_stream.Write(data);
                return data.Length;
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

            if (!m_finished && m_entryCount > 0)
            {
                try { Finish(); } catch { /* Best effort */ }
            }

            m_writer.Flush();

            // Only a finished table is published. Until then it sits under a name the store does not
            // look for, so a crash part-way through leaves a fragment nobody will try to read - the
            // next open used to fail outright on one, because both the memtable flush and the
            // compactor wrote straight to the final name.
            if (m_finished)
                m_file.Publish();

            m_writer.Dispose();
            m_file.Dispose();
            m_currentBlock.Dispose();
        }

        #endregion
        
        #region Properties

        /// <summary>
        /// Gets the output file path.
        /// </summary>
        public string FilePath { get; }

        #endregion
    }
}

