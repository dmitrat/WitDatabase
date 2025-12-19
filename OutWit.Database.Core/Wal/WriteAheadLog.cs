using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Utils;

namespace OutWit.Database.Core.Wal;

/// <summary>
/// Unified Write-Ahead Log implementation with transaction support.
/// Features:
/// - CRC32 integrity checking
/// - Optional encryption via IBlockEncryptor
/// - ArrayPool for reduced allocations
/// - Transaction support with begin/commit/rollback
/// 
/// Use this WAL for BTree stores that need transaction support.
/// For LSM stores, use <see cref="OutWit.Database.Core.LSM.WriteAheadLog"/>.
/// </summary>
public sealed class WriteAheadLog : WriteAheadLogBase, IWriteAheadLog
{
    #region Constants

    private const uint MAGIC = 0x57414C32; // "WAL2" - unified WAL v2
    private const uint MAGIC_ENCRYPTED = 0x57414C45; // "WALE" - encrypted
    private const int TX_HEADER_SIZE = 16; // Magic(4) + Version(4) + EntryCounter(8)

    #endregion

    #region Constructors

    /// <summary>
    /// Creates or opens a WAL file.
    /// </summary>
    /// <param name="filePath">Path to the WAL file.</param>
    /// <param name="encryptor">Optional encryptor for encrypting entries.</param>
    /// <param name="createNew">If true, creates a new WAL (overwrites existing).</param>
    public WriteAheadLog(string filePath, IBlockEncryptor? encryptor = null, bool createNew = false)
        : base(filePath, MAGIC, MAGIC_ENCRYPTED, TX_HEADER_SIZE, hasVersion: true, encryptor, createNew)
    {
    }

    #endregion

    #region IWriteAheadLog Implementation

    /// <inheritdoc/>
    public void AppendPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, long transactionId = 0)
    {
        ThrowIfDisposed();
        AppendDataEntry(WalEntryType.Put, transactionId, key, value);
    }

    /// <inheritdoc/>
    public void AppendDelete(ReadOnlySpan<byte> key, long transactionId = 0)
    {
        ThrowIfDisposed();
        AppendDataEntry(WalEntryType.Delete, transactionId, key, ReadOnlySpan<byte>.Empty);
    }

    /// <inheritdoc/>
    public void AppendBeginTransaction(long transactionId)
    {
        ThrowIfDisposed();
        AppendControlEntry(WalEntryType.BeginTransaction, transactionId);
    }

    /// <inheritdoc/>
    public void AppendCommitTransaction(long transactionId)
    {
        ThrowIfDisposed();
        lock (WriteLock)
        {
            AppendControlEntryInternal(WalEntryType.CommitTransaction, transactionId);
            FlushToDisk();
        }
    }

    /// <inheritdoc/>
    public void AppendRollbackTransaction(long transactionId)
    {
        ThrowIfDisposed();
        AppendControlEntry(WalEntryType.RollbackTransaction, transactionId);
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
                            visitor.OnPut(entry.Value.TransactionId, entry.Value.Key!, entry.Value.Value!);
                            break;
                        case WalEntryType.Delete:
                            visitor.OnDelete(entry.Value.TransactionId, entry.Value.Key!);
                            break;
                        case WalEntryType.BeginTransaction:
                            visitor.OnBeginTransaction(entry.Value.TransactionId);
                            break;
                        case WalEntryType.CommitTransaction:
                            visitor.OnCommitTransaction(entry.Value.TransactionId);
                            break;
                        case WalEntryType.RollbackTransaction:
                            visitor.OnRollbackTransaction(entry.Value.TransactionId);
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

    #region Private Methods

    private void AppendControlEntry(WalEntryType type, long transactionId)
    {
        lock (WriteLock)
        {
            AppendControlEntryInternal(type, transactionId);
        }
    }

    private void AppendControlEntryInternal(WalEntryType type, long transactionId)
    {
        SeekToEnd();

        // Control entry format: [CRC32:4][Type:1][TxId:8]
        const int entrySize = 4 + 1 + 8;
        Span<byte> buffer = stackalloc byte[entrySize];
        
        // Write entry data (after CRC placeholder)
        buffer[4] = (byte)type;
        BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(5), transactionId);
        
        // Calculate and write CRC
        var crc = Crc32.Calculate(buffer.Slice(4));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, crc);

        WriteEntryData(buffer);
    }

    private void AppendDataEntry(WalEntryType type, long transactionId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        lock (WriteLock)
        {
            SeekToEnd();

            // Data entry format: [CRC32:4][Type:1][TxId:8][KeyLen:4][Key][ValueLen:4][Value]
            var entryDataSize = 1 + 8 + 4 + key.Length + 4 + value.Length;
            var totalSize = 4 + entryDataSize; // CRC + data
            
            var rentedBuffer = ArrayPool<byte>.Shared.Rent(totalSize);
            try
            {
                var span = rentedBuffer.AsSpan(0, totalSize);
                var dataSpan = span.Slice(4); // After CRC
                
                dataSpan[0] = (byte)type;
                BinaryPrimitives.WriteInt64LittleEndian(dataSpan.Slice(1), transactionId);
                BinaryPrimitives.WriteInt32LittleEndian(dataSpan.Slice(9), key.Length);
                key.CopyTo(dataSpan.Slice(13));
                BinaryPrimitives.WriteInt32LittleEndian(dataSpan.Slice(13 + key.Length), value.Length);
                value.CopyTo(dataSpan.Slice(17 + key.Length));
                
                // Calculate and write CRC
                var crc = Crc32.Calculate(dataSpan);
                BinaryPrimitives.WriteUInt32LittleEndian(span, crc);

                WriteEntryData(span);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    private (WalEntryType Type, long TransactionId, byte[]? Key, byte[]? Value)? ReadEntry(BinaryReader reader, long entryId)
    {
        // Try reading encrypted data first
        var encryptedData = ReadEntryData(reader, entryId);
        if (encryptedData != null)
        {
            return ParseEntryData(encryptedData);
        }

        // Unencrypted read
        var expectedCrc = reader.ReadUInt32();
        var type = (WalEntryType)reader.ReadByte();
        var txId = reader.ReadInt64();

        switch (type)
        {
            case WalEntryType.BeginTransaction:
            case WalEntryType.CommitTransaction:
            case WalEntryType.RollbackTransaction:
                // Verify CRC for control entry
                Span<byte> controlData = stackalloc byte[9];
                controlData[0] = (byte)type;
                BinaryPrimitives.WriteInt64LittleEndian(controlData.Slice(1), txId);
                if (!Crc32.Verify(controlData, expectedCrc)) return null;
                return (type, txId, null, null);

            case WalEntryType.Put:
            case WalEntryType.Delete:
                var keyLen = reader.ReadInt32();
                if (keyLen < 0 || keyLen > MAX_KEY_SIZE) return null;
                var key = reader.ReadBytes(keyLen);

                var valueLen = reader.ReadInt32();
                if (valueLen < 0 || valueLen > MAX_VALUE_SIZE) return null;
                var value = valueLen > 0 ? reader.ReadBytes(valueLen) : Array.Empty<byte>();

                // Verify CRC
                var dataLen = 1 + 8 + 4 + keyLen + 4 + valueLen;
                var rentedBuffer = ArrayPool<byte>.Shared.Rent(dataLen);
                try
                {
                    var span = rentedBuffer.AsSpan(0, dataLen);
                    span[0] = (byte)type;
                    BinaryPrimitives.WriteInt64LittleEndian(span.Slice(1), txId);
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9), keyLen);
                    key.CopyTo(span.Slice(13));
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(13 + keyLen), valueLen);
                    if (value.Length > 0) value.CopyTo(span.Slice(17 + keyLen));
                    
                    if (!Crc32.Verify(span, expectedCrc)) return null;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }

                return (type, txId, key, value);

            default:
                return null;
        }
    }

    private static (WalEntryType Type, long TransactionId, byte[]? Key, byte[]? Value)? ParseEntryData(byte[] entryData)
    {
        if (entryData.Length < 13) return null; // CRC(4) + Type(1) + TxId(8)
        
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(entryData);
        var dataSpan = entryData.AsSpan(4);
        
        if (!Crc32.Verify(dataSpan, storedCrc)) return null;

        var entryType = (WalEntryType)dataSpan[0];
        var transactionId = BinaryPrimitives.ReadInt64LittleEndian(dataSpan.Slice(1));

        switch (entryType)
        {
            case WalEntryType.BeginTransaction:
            case WalEntryType.CommitTransaction:
            case WalEntryType.RollbackTransaction:
                return (entryType, transactionId, null, null);

            case WalEntryType.Put:
            case WalEntryType.Delete:
                if (dataSpan.Length < 17) return null;
                var kLen = BinaryPrimitives.ReadInt32LittleEndian(dataSpan.Slice(9));
                if (kLen < 0 || 13 + kLen + 4 > dataSpan.Length) return null;
                var k = dataSpan.Slice(13, kLen).ToArray();
                var vLen = BinaryPrimitives.ReadInt32LittleEndian(dataSpan.Slice(13 + kLen));
                var v = vLen > 0 ? dataSpan.Slice(17 + kLen, vLen).ToArray() : Array.Empty<byte>();
                return (entryType, transactionId, k, v);

            default:
                return null;
        }
    }

    #endregion
}
