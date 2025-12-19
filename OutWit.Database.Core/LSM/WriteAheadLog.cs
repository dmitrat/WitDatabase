using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.LSM
{
    /// <summary>
    /// Write-Ahead Log for durability in LSM-Tree.
    /// All mutations are first written here before being applied to MemTable.
    /// Provides crash recovery by replaying the log on startup.
    /// Supports optional encryption via IBlockEncryptor.
    /// Uses ArrayPool to reduce allocations.
    /// </summary>
    public sealed class WriteAheadLog : IDisposable
    {
        #region Constants

        private const uint MAGIC = 0x57414C31; // "WAL1"
        private const uint MAGIC_ENCRYPTED = 0x57414C45; // "WALE" - encrypted WAL
        private const int HEADER_SIZE = 12; // Magic(4) + EntryCounter(8)

        #endregion

        #region Fields

        private readonly FileStream m_stream;
        private readonly BinaryWriter m_writer;
        private readonly IBlockEncryptor? m_encryptor;
        private readonly bool m_isEncrypted;
        private readonly object m_writeLock = new();
        private long m_entryCounter;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates or opens a WAL file.
        /// </summary>
        /// <param name="filePath">Path to the WAL file.</param>
        /// <param name="createNew">If true, creates a new WAL (overwrites existing).</param>
        /// <param name="encryptor">Optional encryptor for encrypting entries.</param>
        public WriteAheadLog(string filePath, bool createNew = false, IBlockEncryptor? encryptor = null)
        {
            FilePath = filePath;
            m_encryptor = encryptor;
            m_isEncrypted = encryptor != null;

            var mode = createNew ? FileMode.Create : FileMode.OpenOrCreate;
            m_stream = new FileStream(filePath, mode, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough);
            m_writer = new BinaryWriter(m_stream);

            if (createNew || m_stream.Length == 0)
            {
                // Write header
                WriteHeader();
            }
            else
            {
                // Validate existing header and load entry counter
                ValidateHeader();
            }
        }

        #endregion

        #region Functions

        /// <summary>
        /// Appends a Put operation to the log.
        /// </summary>
        public void AppendPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            ThrowIfDisposed();
            AppendEntry(WalEntryType.Put, key, value);
        }

        /// <summary>
        /// Appends a Delete operation to the log.
        /// </summary>
        public void AppendDelete(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();
            AppendEntry(WalEntryType.Delete, key, ReadOnlySpan<byte>.Empty);
        }

        /// <summary>
        /// Ensures all pending writes are flushed to disk.
        /// </summary>
        public void Sync()
        {
            ThrowIfDisposed();
            lock (m_writeLock)
            {
                UpdateHeader();
                m_writer.Flush();
                m_stream.Flush(flushToDisk: true);
            }
        }

        /// <summary>
        /// Replays all entries in the log.
        /// </summary>
        /// <param name="onPut">Called for each Put entry.</param>
        /// <param name="onDelete">Called for each Delete entry.</param>
        /// <returns>Number of entries replayed.</returns>
        public int Replay(Action<byte[], byte[]> onPut, Action<byte[]> onDelete)
        {
            ThrowIfDisposed();

            lock (m_writeLock)
            {
                m_stream.Position = HEADER_SIZE;
                using var reader = new BinaryReader(m_stream, System.Text.Encoding.UTF8, leaveOpen: true);
                int count = 0;
                long entryId = 0;

                while (m_stream.Position < m_stream.Length)
                {
                    try
                    {
                        var entry = ReadEntry(reader, entryId++);
                        if (entry == null) break; // Corrupted or incomplete entry

                        switch (entry.Value.Type)
                        {
                            case WalEntryType.Put:
                                onPut(entry.Value.Key, entry.Value.Value!);
                                break;
                            case WalEntryType.Delete:
                                onDelete(entry.Value.Key);
                                break;
                        }
                        count++;
                    }
                    catch (EndOfStreamException)
                    {
                        break; // Incomplete write, stop here
                    }
                    catch (CryptographicException)
                    {
                        break; // Encryption error - stop
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Truncates the WAL (after successful flush to SSTable).
        /// </summary>
        public void Truncate()
        {
            ThrowIfDisposed();

            lock (m_writeLock)
            {
                m_stream.SetLength(0);
                m_stream.Position = 0;
                m_entryCounter = 0;
                WriteHeader();
            }
        }

        private void WriteHeader()
        {
            var magic = m_isEncrypted ? MAGIC_ENCRYPTED : MAGIC;
            m_writer.Write(magic);
            m_writer.Write(m_entryCounter);
            m_writer.Flush();
        }

        private void UpdateHeader()
        {
            var pos = m_stream.Position;
            m_stream.Position = 4; // After magic
            m_writer.Write(m_entryCounter);
            m_stream.Position = pos;
        }

        private void AppendEntry(WalEntryType type, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            // Entry format (unencrypted):
            // [CRC32: 4 bytes] [Type: 1 byte] [KeyLen: 4 bytes] [Key: N bytes] [ValueLen: 4 bytes] [Value: M bytes]
            // 
            // Entry format (encrypted):
            // [EncryptedLen: 4 bytes] [EncryptedData: variable]
            // Where EncryptedData = Encrypt([CRC32][Type][KeyLen][Key][ValueLen][Value])

            lock (m_writeLock)
            {
                m_stream.Position = m_stream.Length;

                // Build entry data: [Type][KeyLen][Key][ValueLen][Value]
                var entryDataLength = 1 + 4 + key.Length + 4 + value.Length;
                var dataWithCrcLength = 4 + entryDataLength;
                
                // Use ArrayPool for larger buffers
                var rentedBuffer = ArrayPool<byte>.Shared.Rent(dataWithCrcLength);
                try
                {
                    var span = rentedBuffer.AsSpan(4, entryDataLength);
                    span[0] = (byte)type;
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(1), key.Length);
                    key.CopyTo(span.Slice(5));
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5 + key.Length), value.Length);
                    value.CopyTo(span.Slice(9 + key.Length));

                    // Calculate CRC32 for entry data
                    var crc = CalculateCrc32(span);
                    BinaryPrimitives.WriteUInt32LittleEndian(rentedBuffer.AsSpan(0, 4), crc);

                    var dataWithCrc = rentedBuffer.AsSpan(0, dataWithCrcLength);

                    if (m_isEncrypted)
                    {
                        // Encrypt the entry
                        var encrypted = m_encryptor!.Encrypt(dataWithCrc.ToArray(), m_entryCounter);
                        m_writer.Write(encrypted.Length);
                        m_writer.Write(encrypted);
                    }
                    else
                    {
                        // Write unencrypted
                        m_stream.Write(dataWithCrc);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }

                m_entryCounter++;
                m_writer.Flush();
            }
        }

        private (WalEntryType Type, byte[] Key, byte[]? Value)? ReadEntry(BinaryReader reader, long entryId)
        {
            byte[] dataWithCrc;

            if (m_isEncrypted)
            {
                // Read encrypted entry
                var encLen = reader.ReadInt32();
                if (encLen < 0 || encLen > 100 * 1024 * 1024) return null; // Sanity check
                
                // Use ArrayPool for reading encrypted data
                var rentedBuffer = ArrayPool<byte>.Shared.Rent(encLen);
                try
                {
                    m_stream.ReadExactly(rentedBuffer.AsSpan(0, encLen));
                    var decrypted = m_encryptor!.Decrypt(rentedBuffer.AsSpan(0, encLen).ToArray(), entryId);
                    if (decrypted == null) return null; // Decryption failed
                    dataWithCrc = decrypted;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
            else
            {
                // Read CRC + entry
                var expectedCrc = reader.ReadUInt32();
            
                // Read type
                var type = (WalEntryType)reader.ReadByte();

                // Read key
                var keyLen = reader.ReadInt32();
                if (keyLen < 0 || keyLen > 1024 * 1024) return null;
                var key = reader.ReadBytes(keyLen);

                // Read value
                var valueLen = reader.ReadInt32();
                if (valueLen < 0 || valueLen > 100 * 1024 * 1024) return null;
                var value = valueLen > 0 ? reader.ReadBytes(valueLen) : null;

                // Verify CRC using pooled buffer
                var entryDataLength = 1 + 4 + keyLen + 4 + valueLen;
                var rentedBuffer = ArrayPool<byte>.Shared.Rent(entryDataLength);
                try
                {
                    var span = rentedBuffer.AsSpan(0, entryDataLength);
                    span[0] = (byte)type;
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(1), keyLen);
                    key.CopyTo(span.Slice(5));
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5 + keyLen), valueLen);
                    if (value != null) value.CopyTo(span.Slice(9 + keyLen));

                    var actualCrc = CalculateCrc32(span);
                    if (actualCrc != expectedCrc) return null;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }

                return (type, key, value);
            }

            // Parse decrypted data
            if (dataWithCrc.Length < 5) return null;
            var crcSpan = dataWithCrc.AsSpan();
            var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(crcSpan);
            var entrySpan = crcSpan.Slice(4);

            // Verify CRC
            var computedCrc = CalculateCrc32(entrySpan);
            if (computedCrc != storedCrc) return null;

            // Parse entry
            var entryType = (WalEntryType)entrySpan[0];
            var kLen = BinaryPrimitives.ReadInt32LittleEndian(entrySpan.Slice(1));
            if (kLen < 0 || kLen > entrySpan.Length - 9) return null;
            var k = entrySpan.Slice(5, kLen).ToArray();
            var vLen = BinaryPrimitives.ReadInt32LittleEndian(entrySpan.Slice(5 + kLen));
            var v = vLen > 0 ? entrySpan.Slice(9 + kLen, vLen).ToArray() : null;

            return (entryType, k, v);
        }

        private void ValidateHeader()
        {
            if (m_stream.Length < HEADER_SIZE)
                throw new InvalidDataException("WAL file is too small");

            m_stream.Position = 0;
            Span<byte> header = stackalloc byte[HEADER_SIZE];
            m_stream.ReadExactly(header);

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            var expectedMagic = m_isEncrypted ? MAGIC_ENCRYPTED : MAGIC;
        
            // Allow reading both encrypted and unencrypted if no encryptor provided
            if (magic != MAGIC && magic != MAGIC_ENCRYPTED)
                throw new InvalidDataException($"Invalid WAL magic number: got 0x{magic:X8}");
        
            if (m_isEncrypted && magic != MAGIC_ENCRYPTED)
                throw new InvalidDataException("WAL file is not encrypted but encryptor was provided");
        
            if (!m_isEncrypted && magic == MAGIC_ENCRYPTED)
                throw new InvalidDataException("WAL file is encrypted but no encryptor was provided");

            m_entryCounter = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(4));
        }

        #endregion

        #region Tools

        private static uint CalculateCrc32(ReadOnlySpan<byte> data)
        {
            // Simple CRC32 implementation (polynomial 0xEDB88320)
            uint crc = 0xFFFFFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc >> 1) ^ (0xEDB88320 & (~(crc & 1) + 1));
                }
            }
            return ~crc;
        }


        private void ThrowIfDisposed()
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(WriteAheadLog));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;

            lock (m_writeLock)
            {
                try { UpdateHeader(); } catch { }
            }
            m_writer.Dispose();
            m_stream.Dispose();
        }

        #endregion


        #region Properties

        /// <summary>
        /// Gets the file path of this WAL.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Gets the current size of the WAL file in bytes.
        /// </summary>
        public long Size => m_stream.Length;

        /// <summary>
        /// Gets whether this WAL is encrypted.
        /// </summary>
        public bool IsEncrypted => m_isEncrypted;

        #endregion
    }
}
