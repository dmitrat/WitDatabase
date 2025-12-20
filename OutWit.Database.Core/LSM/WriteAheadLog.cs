using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Utils;
using OutWit.Database.Core.Wal;

namespace OutWit.Database.Core.LSM;

/// <summary>
/// Write-Ahead Log for durability in LSM-Tree.
/// All mutations are first written here before being applied to MemTable.
/// Provides crash recovery by replaying the log on startup.
/// 
/// This is a specialized WAL for LSM that doesn't need transaction support.
/// For transactional WAL, use <see cref="OutWit.Database.Core.Wal.WriteAheadLog"/>.
/// </summary>
public sealed class WriteAheadLog : WriteAheadLogBase, IWriteAheadLog
{
    #region Constants

    private const uint MAGIC = 0x57414C31; // "WAL1" - LSM WAL v1
    private const uint MAGIC_ENCRYPTED = 0x57414C45; // "WALE" - encrypted WAL
    private const int LSM_HEADER_SIZE = 12; // Magic(4) + EntryCounter(8) - no version

    #endregion

    #region Constructors

    /// <summary>
    /// Creates or opens a WAL file.
    /// </summary>
    /// <param name="filePath">Path to the WAL file.</param>
    /// <param name="createNew">If true, creates a new WAL (overwrites existing).</param>
    /// <param name="encryptor">Optional encryptor for encrypting entries.</param>
    public WriteAheadLog(string filePath, bool createNew = false, IBlockEncryptor? encryptor = null)
        : base(filePath, MAGIC, MAGIC_ENCRYPTED, LSM_HEADER_SIZE, hasVersion: false, encryptor, createNew)
    {
    }

    #endregion

    #region IWriteAheadLog Implementation

    /// <inheritdoc/>
    public void AppendPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, long transactionId = 0)
    {
        ThrowIfDisposed();
        AppendEntry(WalEntryType.Put, key, value);
    }

    /// <inheritdoc/>
    public void AppendDelete(ReadOnlySpan<byte> key, long transactionId = 0)
    {
        ThrowIfDisposed();
        AppendEntry(WalEntryType.Delete, key, ReadOnlySpan<byte>.Empty);
    }

    /// <inheritdoc/>
    public void AppendBeginTransaction(long transactionId)
    {
        // LSM WAL doesn't support transactions - no-op
    }

    /// <inheritdoc/>
    public void AppendCommitTransaction(long transactionId)
    {
        // LSM WAL doesn't support transactions - just sync
        Sync();
    }

    /// <inheritdoc/>
    public void AppendRollbackTransaction(long transactionId)
    {
        // LSM WAL doesn't support transactions - no-op
    }

    /// <inheritdoc/>
    public int Replay(IWalReplayVisitor visitor)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(visitor);

        lock (WriteLock)
        {
            SeekTo(HeaderSize);
            using var reader = CreateReader();
            int count = 0;
            long entryId = 0;

            while (GetPosition() < GetLength())
            {
                try
                {
                    var entry = ReadEntry(reader, entryId++);
                    if (entry == null) break;

                    switch (entry.Value.Type)
                    {
                        case WalEntryType.Put:
                            visitor.OnPut(0, entry.Value.Key, entry.Value.Value!);
                            break;
                        case WalEntryType.Delete:
                            visitor.OnDelete(0, entry.Value.Key);
                            break;
                    }
                    count++;
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (CryptographicException)
                {
                    break;
                }
            }

            return count;
        }
    }

    #endregion

    #region Legacy Methods

    /// <summary>
    /// Replays all entries in the log using callbacks.
    /// </summary>
    /// <param name="onPut">Called for each Put entry.</param>
    /// <param name="onDelete">Called for each Delete entry.</param>
    /// <returns>Number of entries replayed.</returns>
    public int Replay(Action<byte[], byte[]> onPut, Action<byte[]> onDelete)
    {
        return Replay(new WalReplayVisitorSimple(onPut, onDelete));
    }

    #endregion

    #region Private Methods

    private void AppendEntry(WalEntryType type, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        lock (WriteLock)
        {
            SeekToEnd();

            // Entry format: [CRC32:4][Type:1][KeyLen:4][Key][ValueLen:4][Value]
            var entryDataLength = 1 + 4 + key.Length + 4 + value.Length;
            var dataWithCrcLength = 4 + entryDataLength;
            
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
                var crc = Crc32.Calculate(span);
                BinaryPrimitives.WriteUInt32LittleEndian(rentedBuffer.AsSpan(0, 4), crc);

                var dataWithCrc = rentedBuffer.AsSpan(0, dataWithCrcLength);
                WriteEntryData(dataWithCrc);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    private (WalEntryType Type, byte[] Key, byte[]? Value)? ReadEntry(BinaryReader reader, long entryId)
    {
        // Try reading encrypted data first
        var encryptedData = ReadEntryData(reader, entryId);
        if (encryptedData != null)
        {
            return ParseEncryptedEntry(encryptedData);
        }

        // Unencrypted read
        var expectedCrc = reader.ReadUInt32();
        var type = (WalEntryType)reader.ReadByte();

        var keyLen = reader.ReadInt32();
        if (keyLen < 0 || keyLen > MAX_KEY_SIZE) return null;
        var key = reader.ReadBytes(keyLen);

        var valueLen = reader.ReadInt32();
        if (valueLen < 0 || valueLen > MAX_VALUE_SIZE) return null;
        var value = valueLen > 0 ? reader.ReadBytes(valueLen) : null;

        // Verify CRC
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

            if (!Crc32.Verify(span, expectedCrc)) return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }

        return (type, key, value);
    }

    private static (WalEntryType Type, byte[] Key, byte[]? Value)? ParseEncryptedEntry(byte[] dataWithCrc)
    {
        if (dataWithCrc.Length < 5) return null;
        
        var crcSpan = dataWithCrc.AsSpan();
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(crcSpan);
        var entrySpan = crcSpan.Slice(4);

        if (!Crc32.Verify(entrySpan, storedCrc)) return null;

        var entryType = (WalEntryType)entrySpan[0];
        var kLen = BinaryPrimitives.ReadInt32LittleEndian(entrySpan.Slice(1));
        if (kLen < 0 || kLen > entrySpan.Length - 9) return null;
        var k = entrySpan.Slice(5, kLen).ToArray();
        var vLen = BinaryPrimitives.ReadInt32LittleEndian(entrySpan.Slice(5 + kLen));
        var v = vLen > 0 ? entrySpan.Slice(9 + kLen, vLen).ToArray() : null;

        return (entryType, k, v);
    }

    #endregion
}
