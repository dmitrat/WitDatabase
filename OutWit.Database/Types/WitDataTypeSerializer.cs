using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OutWit.Database.Core.Encoding;

namespace OutWit.Database.Types;

/// <summary>
/// Serializes and deserializes values to/from binary format.
/// </summary>
public static class WitDataTypeSerializer
{
    #region Constants

    private static readonly Encoding ENCODING = Encoding.UTF8;

    #endregion
    
    // ========== Integers (VarInt encoded for variable, fixed for others) ==========

    #region Int8 (sbyte) 1

    public static sbyte ReadInt8(ReadOnlySpan<byte> buffer)
    {
        return (sbyte)buffer[0];
    }

    public static int Write(Span<byte> buffer, sbyte value)
    {
        buffer[0] = (byte)value;
        return 1;
    }

    #endregion

    #region UInt8 (byte) 2
    public static byte ReadUInt8(ReadOnlySpan<byte> buffer)
    {
        return buffer[0];
    }

    public static int Write(Span<byte> buffer, byte value)
    {
        buffer[0] = value;
        return 1;
    }

    #endregion

    #region Int16 (short) 3
    public static short ReadInt16(ReadOnlySpan<byte> buffer)
    {
        return BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }

    public static int Write(Span<byte> buffer, short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        return 2;
    }

    #endregion

    #region UInt16 (ushort) 4
    public static ushort ReadUInt16(ReadOnlySpan<byte> buffer)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    public static int Write(Span<byte> buffer, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        return 2;
    }

    #endregion

    #region Int32 (int) 5
    public static (int Value, int BytesRead) ReadInt32(ReadOnlySpan<byte> buffer)
    {
        var (value, bytesRead) = VarInt.Decode(buffer);
        return ((int)value, bytesRead);
    }

    public static int Write(Span<byte> buffer, int value)
    {
        return VarInt.Encode(buffer, value);
    }

    /// <summary>
    /// Gets the encoded size of an int32 (VarInt).
    /// </summary>
    public static int GetEncodedSize(int value)
    {
        return VarInt.GetEncodedLength(value);
    }

    #endregion

    #region UInt32 (uint) 6
    public static (uint Value, int BytesRead) ReadUInt32(ReadOnlySpan<byte> buffer)
    {
        var (value, bytesRead) = VarInt.DecodeUnsigned(buffer);
        return ((uint)value, bytesRead);
    }

    public static int Write(Span<byte> buffer, uint value)
    {
        return VarInt.EncodeUnsigned(buffer, value);
    }

    /// <summary>
    /// Gets the encoded size of a uint32 (VarInt).
    /// </summary>
    public static int GetEncodedSize(uint value)
    {
        return VarInt.GetEncodedLengthUnsigned(value);
    }

    #endregion

    #region Int64 (long) 7

    public static (long Value, int BytesRead) ReadInt64(ReadOnlySpan<byte> buffer)
    {
        return VarInt.Decode(buffer);
    }


    public static int Write(Span<byte> buffer, long value)
    {
        return VarInt.Encode(buffer, value);
    }

    /// <summary>
    /// Gets the encoded size of an int64 (VarInt).
    /// </summary>
    public static int GetEncodedSize(long value)
    {
        return VarInt.GetEncodedLength(value);
    }

    #endregion

    #region UInt64 (ulong) 8

    public static (ulong Value, int BytesRead) ReadUInt64(ReadOnlySpan<byte> buffer)
    {
        return VarInt.DecodeUnsigned(buffer);
    }

    public static int Write(Span<byte> buffer, ulong value)
    {
        return VarInt.EncodeUnsigned(buffer, value);
    }

    /// <summary>
    /// Gets the encoded size of a uint64 (VarInt).
    /// </summary>
    public static int GetEncodedSize(ulong value)
    {
        return VarInt.GetEncodedLengthUnsigned(value);
    }

    #endregion

    // ========== Floating Point ==========

    #region Float16 (Half) 10
    public static Half ReadFloat16(ReadOnlySpan<byte> buffer)
    {
        return BinaryPrimitives.ReadHalfLittleEndian(buffer);
    }

    public static int Write(Span<byte> buffer, Half value)
    {
        BinaryPrimitives.WriteHalfLittleEndian(buffer, value);
        return 2;
    }

    #endregion

    #region Float32 (float) 11
    public static float ReadFloat32(ReadOnlySpan<byte> buffer)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    public static int Write(Span<byte> buffer, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        return 4;
    }

    #endregion

    #region Float64 (double) 12
    public static double ReadFloat64(ReadOnlySpan<byte> buffer)
    {
        return BinaryPrimitives.ReadDoubleLittleEndian(buffer);
    }

    public static int Write(Span<byte> buffer, double value)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        return 8;
    }

    #endregion

    #region Decimal (decimal) 13

    public static decimal ReadDecimal(ReadOnlySpan<byte> buffer)
    {
        int lo = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        int mid = BinaryPrimitives.ReadInt32LittleEndian(buffer[4..]);
        int hi = BinaryPrimitives.ReadInt32LittleEndian(buffer[8..]);
        int flags = BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]);

        return new decimal([lo, mid, hi, flags]);
    }

    public static int Write(Span<byte> buffer, decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);

        BinaryPrimitives.WriteInt32LittleEndian(buffer, bits[0]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], bits[1]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], bits[2]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[12..], bits[3]);
        return 16;
    }

    #endregion

    // ========== Boolean ==========

    #region Boolean (bool) 20
    public static bool ReadBoolean(ReadOnlySpan<byte> buffer)
    {
        return buffer[0] != 0;
    }

    public static int Write(Span<byte> buffer, bool value)
    {
        buffer[0] = value ? (byte)1 : (byte)0;
        return 1;
    }

    #endregion

    // ========== Date and Time ==========

    #region DateOnly (DateOnly) 30

    public static DateOnly ReadDateOnly(ReadOnlySpan<byte> buffer)
    {
        int dayNumber = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        return DateOnly.FromDayNumber(dayNumber);
    }

    public static int Write(Span<byte> buffer, DateOnly value)
    {
        int dayNumber = value.DayNumber;
        BinaryPrimitives.WriteInt32LittleEndian(buffer, dayNumber);
        return 4;
    }

    #endregion

    #region TimeOnly (TimeOnly) 31

    public static TimeOnly ReadTimeOnly(ReadOnlySpan<byte> buffer)
    {
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer);
        return new TimeOnly(ticks);
    }

    public static int Write(Span<byte> buffer, TimeOnly value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value.Ticks);
        return 8;
    }

    #endregion

    #region DateTime (DateTime) 32

    public static DateTime ReadDateTime(ReadOnlySpan<byte> buffer)
    {
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer);
        return new DateTime(ticks);
    }

    public static int Write(Span<byte> buffer, DateTime value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value.Ticks);
        return 8;
    }

    #endregion

    #region DateTimeOffset (DateTimeOffset) 33

    public static DateTimeOffset ReadDateTimeOffset(ReadOnlySpan<byte> buffer)
    {
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer);
        short offsetMinutes = BinaryPrimitives.ReadInt16LittleEndian(buffer[8..]);
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    public static int Write(Span<byte> buffer, DateTimeOffset value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value.Ticks);
        BinaryPrimitives.WriteInt16LittleEndian(buffer[8..], (short)value.Offset.TotalMinutes);
        return 10;
    }

    #endregion

    #region TimeSpan (TimeSpan) 34

    public static TimeSpan ReadTimeSpan(ReadOnlySpan<byte> buffer)
    {
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer);
        return new TimeSpan(ticks);
    }

    public static int Write(Span<byte> buffer, TimeSpan value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value.Ticks);
        return 8;
    }

    #endregion

    // ========== Unique Identifier ==========

    #region Guid (Guid) 40
    
    public static Guid ReadGuid(ReadOnlySpan<byte> buffer)
    {
        return new Guid(buffer[..16]);
    }

    public static int Write(Span<byte> buffer, Guid value)
    {
        value.TryWriteBytes(buffer);
        return 16;
    }

    #endregion

    // ========== Strings ==========

    #region StringFixed (string) 50

    /// <summary>
    /// Reads a fixed-length string (trimming trailing zeros).
    /// </summary>
    public static string ReadStringFixed(ReadOnlySpan<byte> buffer, int fixedLength)
    {
        int actualLength = buffer[..fixedLength].IndexOf((byte)0);
        if (actualLength < 0) actualLength = fixedLength;

        return ENCODING.GetString(buffer[..actualLength]);
    }

    /// <summary>
    /// Writes a fixed-length string (padded with zeros if shorter).
    /// </summary>
    public static int WriteStringFixed(Span<byte> buffer, string value, int fixedLength)
    {
        buffer[..fixedLength].Clear();
        if (!string.IsNullOrEmpty(value))
        {
            int byteCount = Math.Min(ENCODING.GetByteCount(value), fixedLength);
            ENCODING.GetBytes(value.AsSpan(), buffer[..byteCount]);
        }
        return fixedLength;
    }

    #endregion

    #region StringVariable (string) 51

    /// <summary>
    /// Reads a variable-length string (length prefix + UTF-8 bytes).
    /// </summary>
    public static (string Value, int BytesRead) ReadString(ReadOnlySpan<byte> buffer)
    {
        var (length, lengthBytes) = VarInt.DecodeUnsigned(buffer);
        if (length == 0)
        {
            return (string.Empty, lengthBytes);
        }

        string value = ENCODING.GetString(buffer.Slice(lengthBytes, (int)length));
        return (value, lengthBytes + (int)length);
    }

    /// <summary>
    /// Writes a variable-length string (length prefix + UTF-8 bytes).
    /// </summary>
    public static int WriteString(Span<byte> buffer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return VarInt.EncodeUnsigned(buffer, 0);
        }

        int byteCount = ENCODING.GetByteCount(value);
        int lengthBytes = VarInt.EncodeUnsigned(buffer, (ulong)byteCount);
        ENCODING.GetBytes(value, buffer[lengthBytes..]);
        return lengthBytes + byteCount;
    }

    /// <summary>
    /// Gets the encoded size of a variable-length string.
    /// </summary>
    public static int GetEncodedSize(string value)
    {
        if (string.IsNullOrEmpty(value)) return 1; // Just length prefix (0)

        int byteCount = ENCODING.GetByteCount(value);
        return VarInt.GetEncodedLengthUnsigned((ulong)byteCount) + byteCount;
    }

    #endregion

    // ========== Binary ==========

    #region BinaryFixed (byte[]) 60

    /// <summary>
    /// Reads fixed-length binary data.
    /// </summary>
    public static byte[] ReadBinaryFixed(ReadOnlySpan<byte> buffer, int fixedLength)
    {
        return buffer[..fixedLength].ToArray();
    }

    /// <summary>
    /// Writes fixed-length binary data (padded with zeros if shorter).
    /// </summary>
    public static int WriteBinaryFixed(Span<byte> buffer, ReadOnlySpan<byte> value, int fixedLength)
    {
        buffer[..fixedLength].Clear();
        int copyLength = Math.Min(value.Length, fixedLength);
        value[..copyLength].CopyTo(buffer);
        return fixedLength;
    }

    #endregion

    #region BinaryVariable (byte[]) 61

    /// <summary>
    /// Reads variable-length binary data (length prefix + bytes).
    /// </summary>
    public static (byte[] Value, int BytesRead) ReadBinary(ReadOnlySpan<byte> buffer)
    {
        var (length, lengthBytes) = VarInt.DecodeUnsigned(buffer);
        if (length == 0)
        {
            return ([], lengthBytes);
        }

        byte[] value = buffer.Slice(lengthBytes, (int)length).ToArray();
        return (value, lengthBytes + (int)length);
    }

    /// <summary>
    /// Writes variable-length binary data (length prefix + bytes).
    /// </summary>
    public static int WriteBinary(Span<byte> buffer, ReadOnlySpan<byte> value)
    {
        int lengthBytes = VarInt.EncodeUnsigned(buffer, (ulong)value.Length);
        value.CopyTo(buffer[lengthBytes..]);
        return lengthBytes + value.Length;
    }

    /// <summary>
    /// Gets the encoded size of a variable-length binary.
    /// </summary>
    public static int GetEncodedSize(ReadOnlySpan<byte> value)
    {
        return VarInt.GetEncodedLengthUnsigned((ulong)value.Length) + value.Length;
    }

    #endregion

    // ========== Common ==========

    #region GetFixedSize

    /// <summary>
    /// Gets the fixed size in bytes for a data type, or -1 for variable types.
    /// </summary>
    public static int GetFixedSize(WitDataType type) => type switch
    {
        WitDataType.Null => 0,
        WitDataType.Int8 or WitDataType.UInt8 or WitDataType.Boolean => 1,
        WitDataType.Int16 or WitDataType.UInt16 or WitDataType.Float16 => 2,
        WitDataType.Float32 => 4,
        WitDataType.Int32 or WitDataType.UInt32 => -1, // VarInt
        WitDataType.Int64 or WitDataType.UInt64 => -1, // VarInt
        WitDataType.Float64 or WitDataType.DateTime or WitDataType.TimeOnly or WitDataType.TimeSpan => 8,
        WitDataType.DateOnly => 4,
        WitDataType.DateTimeOffset => 10,
        WitDataType.Decimal or WitDataType.Guid => 16,
        WitDataType.StringFixed or WitDataType.BinaryFixed => -1, // Need schema info
        WitDataType.StringVariable or WitDataType.BinaryVariable => -1,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    #endregion
}
