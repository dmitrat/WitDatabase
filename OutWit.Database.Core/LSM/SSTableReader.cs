using System.Buffers;
using System.Buffers.Binary;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.LSM
{
    /// <summary>
    /// Reads immutable SSTable files.
    /// Supports point lookups and range scans via block index.
    /// Uses Bloom filter to skip reads for non-existent keys.
    /// Optionally uses BlockCache to reduce disk I/O.
    /// Uses ArrayPool to reduce allocations during reads.
    /// Handles both encrypted and unencrypted SSTables.
    /// Thread-safe for concurrent reads.
    /// </summary>
    public sealed class SSTableReader : IDisposable
    {
        #region Constants

        private const uint MAGIC = 0x53535431; // "SST1"
        private const uint FLAG_ENCRYPTED = 0x01;
        private const uint FLAG_HAS_BLOOM = 0x02;
        private const int FOOTER_SIZE_V1 = 8 + 4 + 4 + 4; // IndexOffset + IndexSize + EntryCount + Magic = 20 bytes
        private const int FOOTER_SIZE_V2_OLD = 8 + 4 + 4 + 4 + 4; // + Flags = 24 bytes
        private const int FOOTER_SIZE_V2 = 44; // With bloom: + BloomOffset(8) + BloomSizeBytes(4) + BloomBitSize(4) + HashCount(4)

        #endregion

        #region Fields

        private readonly FileStream m_stream;
        private readonly Lock m_streamLock = new();
        private readonly IBlockEncryptor? m_encryptor;
        private readonly BlockCache? m_cache;
        private readonly List<IndexEntry> m_index = [];
        private readonly ByteArrayComparer m_comparer = ByteArrayComparer.Default;
        private BloomFilter? m_bloomFilter;
        private bool m_encrypted;
        private bool m_hasBloomFilter;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Opens an SSTable file for reading.
        /// </summary>
        /// <param name="filePath">Path to the SSTable file.</param>
        /// <param name="encryptor">Optional block encryptor for decryption.</param>
        /// <param name="cache">Optional block cache for caching reads.</param>
        public SSTableReader(string filePath, IBlockEncryptor? encryptor = null, BlockCache? cache = null)
        {
            FilePath = filePath;
            m_encryptor = encryptor;
            m_cache = cache;
            m_stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            LoadIndex();
        }

        #endregion

        #region Functions

        /// <summary>
        /// Tries to get a value by key.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="value">The value if found (null for tombstone).</param>
        /// <returns>True if key was found (including tombstones).</returns>
        public bool TryGet(ReadOnlySpan<byte> key, out byte[]? value)
        {
            ThrowIfDisposed();
            value = null;

            // Check Bloom filter first (fast path for non-existent keys)
            if (m_bloomFilter != null && !m_bloomFilter.MightContain(key))
            {
                return false; // Bloom filter says definitely not here
            }

            // Find the block that might contain the key
            var blockIndex = FindBlockForKey(key);
            if (blockIndex < 0)
                return false;

            // Search within the block (may use cache)
            var block = ReadBlockCached(blockIndex);
            if (block == null) return false; // Decryption failed
            return SearchInBlock(block, key, out value);
        }

        /// <summary>
        /// Scans all entries in the SSTable.
        /// </summary>
        public IEnumerable<(byte[] Key, byte[]? Value)> Scan()
        {
            ThrowIfDisposed();

            for (int i = 0; i < m_index.Count; i++)
            {
                var block = ReadBlockCached(i);
                if (block == null) continue; // Skip on decryption failure
                foreach (var entry in ParseBlock(block))
                {
                    yield return entry;
                }
            }
        }

        /// <summary>
        /// Scans entries in a key range.
        /// </summary>
        /// <param name="startKey">Start of range (inclusive), or null for beginning.</param>
        /// <param name="endKey">End of range (exclusive), or null for end.</param>
        public IEnumerable<(byte[] Key, byte[]? Value)> Scan(byte[]? startKey, byte[]? endKey)
        {
            ThrowIfDisposed();

            // Find starting block
            int startBlock = 0;
            if (startKey != null)
            {
                startBlock = FindBlockForKey(startKey);
                if (startBlock < 0) startBlock = 0;
            }

            // Scan blocks
            for (int i = startBlock; i < m_index.Count; i++)
            {
                var block = ReadBlockCached(i);
                if (block == null) continue;
                foreach (var entry in ParseBlock(block))
                {
                    // Skip entries before start
                    if (startKey != null && m_comparer.Compare(entry.Key, startKey) < 0)
                        continue;

                    // Stop at end
                    if (endKey != null && m_comparer.Compare(entry.Key, endKey) >= 0)
                        yield break;

                    yield return entry;
                }
            }
        }

        private int FindBlockForKey(ReadOnlySpan<byte> key)
        {
            // Binary search for the last block where FirstKey <= key
            int left = 0, right = m_index.Count - 1;
            int result = -1;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                var cmp = key.SequenceCompareTo(m_index[mid].FirstKey);

                if (cmp >= 0)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return result;
        }

        /// <summary>
        /// Reads a block, using cache if available.
        /// </summary>
        private byte[]? ReadBlockCached(int blockIndex)
        {
            // Try cache first
            if (m_cache != null && m_cache.TryGet(FilePath, blockIndex, out var cached))
            {
                return cached;
            }

            // Read from disk
            var block = ReadBlockFromDisk(blockIndex);
            
            // Store in cache (if available and read succeeded)
            if (block != null && m_cache != null)
            {
                m_cache.Put(FilePath, blockIndex, block);
            }

            return block;
        }

        private byte[]? ReadBlockFromDisk(int blockIndex)
        {
            var entry = m_index[blockIndex];
            
            lock (m_streamLock)
            {
                m_stream.Position = entry.BlockOffset;

                if (m_encrypted)
                {
                    // Read encrypted block: [len:4][encrypted data]
                    Span<byte> lenBuf = stackalloc byte[4];
                    m_stream.ReadExactly(lenBuf);
                    var encLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            
                    // Use ArrayPool for encrypted data
                    var rentedEncrypted = ArrayPool<byte>.Shared.Rent(encLen);
                    try
                    {
                        m_stream.ReadExactly(rentedEncrypted.AsSpan(0, encLen));
            
                        if (m_encryptor == null)
                            throw new InvalidOperationException("SSTable is encrypted but no encryptor provided");
            
                        return m_encryptor.Decrypt(rentedEncrypted.AsSpan(0, encLen).ToArray(), blockIndex);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rentedEncrypted);
                    }
                }
                else
                {
                    var block = new byte[entry.BlockSize];
                    m_stream.ReadExactly(block);
                    return block;
                }
            }
        }

        private byte[]? ReadIndexBlock(long indexOffset, int indexSize)
        {
            lock (m_streamLock)
            {
                m_stream.Position = indexOffset;

                if (m_encrypted)
                {
                    Span<byte> lenBuf = stackalloc byte[4];
                    m_stream.ReadExactly(lenBuf);
                    var encLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            
                    var rentedEncrypted = ArrayPool<byte>.Shared.Rent(encLen);
                    try
                    {
                        m_stream.ReadExactly(rentedEncrypted.AsSpan(0, encLen));
            
                        if (m_encryptor == null)
                            throw new InvalidOperationException("SSTable is encrypted but no encryptor provided");
            
                        return m_encryptor.Decrypt(rentedEncrypted.AsSpan(0, encLen).ToArray(), SSTableBuilder.INDEX_BLOCK_ID);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rentedEncrypted);
                    }
                }
                else
                {
                    var indexData = new byte[indexSize];
                    m_stream.ReadExactly(indexData);
                    return indexData;
                }
            }
        }

        private byte[]? ReadBloomFilter(long bloomOffset, int bloomSize)
        {
            lock (m_streamLock)
            {
                m_stream.Position = bloomOffset;

                if (m_encrypted)
                {
                    Span<byte> lenBuf = stackalloc byte[4];
                    m_stream.ReadExactly(lenBuf);
                    var encLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            
                    var rentedEncrypted = ArrayPool<byte>.Shared.Rent(encLen);
                    try
                    {
                        m_stream.ReadExactly(rentedEncrypted.AsSpan(0, encLen));
            
                        if (m_encryptor == null)
                            throw new InvalidOperationException("SSTable is encrypted but no encryptor provided");
            
                        return m_encryptor.Decrypt(rentedEncrypted.AsSpan(0, encLen).ToArray(), SSTableBuilder.BLOOM_BLOCK_ID);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rentedEncrypted);
                    }
                }
                else
                {
                    var bloomData = new byte[bloomSize];
                    m_stream.ReadExactly(bloomData);
                    return bloomData;
                }
            }
        }

        private static bool SearchInBlock(byte[] block, ReadOnlySpan<byte> targetKey, out byte[]? value)
        {
            value = null;
            int offset = 0;

            while (offset < block.Length)
            {
                var keyLen = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(offset));
                offset += 4;

                var key = block.AsSpan(offset, keyLen);
                offset += keyLen;

                var valueLen = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(offset));
                offset += 4;

                var cmp = targetKey.SequenceCompareTo(key);
                if (cmp == 0)
                {
                    // Found! Extract value
                    if (valueLen >= 0)
                    {
                        value = block.AsSpan(offset, valueLen).ToArray();
                    }
                    return true;
                }
                
                // Skip value bytes
                if (valueLen >= 0)
                {
                    offset += valueLen;
                }

                if (cmp < 0)
                {
                    return false; // Key not found, and we've passed where it would be
                }
            }

            return false;
        }

        private static IEnumerable<(byte[] Key, byte[]? Value)> ParseBlock(byte[] block)
        {
            int offset = 0;

            while (offset < block.Length)
            {
                var keyLen = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(offset));
                offset += 4;

                var key = block.AsSpan(offset, keyLen).ToArray();
                offset += keyLen;

                var valueLen = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(offset));
                offset += 4;

                byte[]? value = null;
                if (valueLen >= 0)
                {
                    value = block.AsSpan(offset, valueLen).ToArray();
                    offset += valueLen;
                }

                yield return (key, value);
            }
        }

        private void LoadIndex()
        {
            if (m_stream.Length < FOOTER_SIZE_V2)
            {
                // Try older formats
                LoadIndexLegacy();
                return;
            }

            // Try new format footer first (with bloom filter) - 44 bytes
            m_stream.Position = m_stream.Length - FOOTER_SIZE_V2;
            Span<byte> footer = stackalloc byte[FOOTER_SIZE_V2];
            m_stream.ReadExactly(footer);

            // Footer V2 layout (44 bytes):
            // [0-7]   IndexOffset: 8 bytes
            // [8-11]  IndexSize: 4 bytes
            // [12-15] EntryCount: 4 bytes
            // [16-19] Flags: 4 bytes
            // [20-27] BloomOffset: 8 bytes
            // [28-31] BloomSizeBytes: 4 bytes (size on disk)
            // [32-35] BloomBitSize: 4 bytes (original bit size)
            // [36-39] BloomHashCount: 4 bytes
            // [40-43] Magic: 4 bytes
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(40));
            
            if (magic == MAGIC)
            {
                // New V2 format with bloom filter
                var indexOffset = BinaryPrimitives.ReadInt64LittleEndian(footer);
                var indexSize = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(8));
                EntryCount = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(12));
                var flags = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(16));
                var bloomOffset = BinaryPrimitives.ReadInt64LittleEndian(footer.Slice(20));
                var bloomSizeBytes = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(28));
                var bloomBitSize = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(32));
                var bloomHashCount = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(36));

                m_encrypted = (flags & FLAG_ENCRYPTED) != 0;
                m_hasBloomFilter = (flags & FLAG_HAS_BLOOM) != 0;

                if (m_encrypted && m_encryptor == null)
                    throw new InvalidOperationException("SSTable is encrypted but no encryptor provided");

                // Load index
                var indexData = ReadIndexBlock(indexOffset, indexSize);
                if (indexData == null)
                    throw new InvalidDataException("Failed to decrypt index block");
                ParseIndexBlock(indexData);

                // Load bloom filter
                if (m_hasBloomFilter && bloomSizeBytes > 0)
                {
                    var bloomData = ReadBloomFilter(bloomOffset, bloomSizeBytes);
                    if (bloomData != null)
                    {
                        // Use original bit size for correct deserialization
                        m_bloomFilter = new BloomFilter(bloomData, bloomHashCount, bloomBitSize);
                    }
                }
            }
            else
            {
                // Fall back to legacy format
                LoadIndexLegacy();
            }
        }

        private void LoadIndexLegacy()
        {
            // Try old format without bloom (24 bytes footer)
            if (m_stream.Length < FOOTER_SIZE_V2_OLD)
                throw new InvalidDataException("SSTable file is too small");

            m_stream.Position = m_stream.Length - FOOTER_SIZE_V2_OLD;
            Span<byte> footer = stackalloc byte[FOOTER_SIZE_V2_OLD];
            m_stream.ReadExactly(footer);

            var indexOffset = BinaryPrimitives.ReadInt64LittleEndian(footer);
            var indexSize = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(8));
            EntryCount = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(12));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(16));
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(20));

            if (magic != MAGIC)
            {
                // Try even older format (20 bytes, no flags)
                m_stream.Position = m_stream.Length - FOOTER_SIZE_V1;
                Span<byte> footerV1 = stackalloc byte[FOOTER_SIZE_V1];
                m_stream.ReadExactly(footerV1);

                indexOffset = BinaryPrimitives.ReadInt64LittleEndian(footerV1);
                indexSize = BinaryPrimitives.ReadInt32LittleEndian(footerV1.Slice(8));
                EntryCount = BinaryPrimitives.ReadInt32LittleEndian(footerV1.Slice(12));
                magic = BinaryPrimitives.ReadUInt32LittleEndian(footerV1.Slice(16));
                flags = 0;

                if (magic != MAGIC)
                    throw new InvalidDataException($"Invalid SSTable magic: expected 0x{MAGIC:X8}, got 0x{magic:X8}");
            }

            m_encrypted = (flags & FLAG_ENCRYPTED) != 0;
            m_hasBloomFilter = false;

            if (m_encrypted && m_encryptor == null)
                throw new InvalidOperationException("SSTable is encrypted but no encryptor provided");

            var indexData = ReadIndexBlock(indexOffset, indexSize);
            if (indexData == null)
                throw new InvalidDataException("Failed to decrypt index block");
            ParseIndexBlock(indexData);
        }

        private void ParseIndexBlock(byte[] indexData)
        {
            int offset = 0;
            while (offset < indexData.Length)
            {
                var keyLen = BinaryPrimitives.ReadInt32LittleEndian(indexData.AsSpan(offset));
                offset += 4;

                var firstKey = indexData.AsSpan(offset, keyLen).ToArray();
                offset += keyLen;

                var blockOffset = BinaryPrimitives.ReadInt64LittleEndian(indexData.AsSpan(offset));
                offset += 8;

                var blockSize = BinaryPrimitives.ReadInt32LittleEndian(indexData.AsSpan(offset));
                offset += 4;

                m_index.Add(new IndexEntry
                {
                    FirstKey = firstKey,
                    BlockOffset = blockOffset,
                    BlockSize = blockSize
                });
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
            m_stream.Dispose();
            // Note: We don't dispose the cache - it's shared
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the file path.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Gets the total number of entries.
        /// </summary>
        public int EntryCount { get; private set; }

        /// <summary>
        /// Gets the file size in bytes.
        /// </summary>
        public long FileSize => m_stream.Length;

        /// <summary>
        /// Gets whether this SSTable is encrypted.
        /// </summary>
        public bool IsEncrypted => m_encrypted;

        /// <summary>
        /// Gets whether this SSTable has a Bloom filter.
        /// </summary>
        public bool HasBloomFilter => m_hasBloomFilter;

        #endregion
    }
}
