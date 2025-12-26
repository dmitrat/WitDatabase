using System.Buffers.Binary;
using System.Text;

namespace OutWit.Database.Model;

/// <summary>
/// Efficient span-based reader for deserializing binary data.
/// Used for reading row data from storage.
/// </summary>
public ref struct SpanReader
{
    #region Fields

    private readonly ReadOnlySpan<byte> m_data;
    private int m_position;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new span reader over the specified data.
    /// </summary>
    /// <param name="data">The data to read from.</param>
    public SpanReader(ReadOnlySpan<byte> data)
    {
        m_data = data;
        m_position = 0;
    }

    #endregion

    #region Read Functions

    /// <summary>
    /// Reads a single byte.
    /// </summary>
    public byte ReadByte()
    {
        return m_data[m_position++];
    }

    /// <summary>
    /// Reads a signed byte.
    /// </summary>
    public sbyte ReadSByte()
    {
        return (sbyte)m_data[m_position++];
    }

    /// <summary>
    /// Reads a boolean value.
    /// </summary>
    public bool ReadBool()
    {
        return m_data[m_position++] != 0;
    }

    /// <summary>
    /// Reads a signed 16-bit integer in little-endian format.
    /// </summary>
    public short ReadInt16()
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(m_data.Slice(m_position));
        m_position += 2;
        return value;
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer in little-endian format.
    /// </summary>
    public ushort ReadUInt16()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(m_data.Slice(m_position));
        m_position += 2;
        return value;
    }

    /// <summary>
    /// Reads a signed 32-bit integer in little-endian format.
    /// </summary>
    public int ReadInt32()
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(m_data.Slice(m_position));
        m_position += 4;
        return value;
    }

    /// <summary>
    /// Reads a signed 64-bit integer in little-endian format.
    /// </summary>
    public long ReadInt64()
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(m_data.Slice(m_position));
        m_position += 8;
        return value;
    }

    /// <summary>
    /// Reads a 16-bit floating point number (Half) in little-endian format.
    /// </summary>
    public Half ReadHalf()
    {
        var value = BinaryPrimitives.ReadHalfLittleEndian(m_data.Slice(m_position));
        m_position += 2;
        return value;
    }

    /// <summary>
    /// Reads a 32-bit floating point number in little-endian format.
    /// </summary>
    public float ReadSingle()
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(m_data.Slice(m_position));
        m_position += 4;
        return value;
    }

    /// <summary>
    /// Reads a 64-bit floating point number in little-endian format.
    /// </summary>
    public double ReadDouble()
    {
        var value = BinaryPrimitives.ReadDoubleLittleEndian(m_data.Slice(m_position));
        m_position += 8;
        return value;
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 string.
    /// </summary>
    public string ReadString()
    {
        var length = ReadInt32();
        var value = Encoding.UTF8.GetString(m_data.Slice(m_position, length));
        m_position += length;
        return value;
    }

    /// <summary>
    /// Reads a length-prefixed byte array.
    /// </summary>
    public byte[] ReadBlob()
    {
        var length = ReadInt32();
        var value = m_data.Slice(m_position, length).ToArray();
        m_position += length;
        return value;
    }

    /// <summary>
    /// Reads a fixed number of bytes without length prefix.
    /// </summary>
    /// <param name="count">The number of bytes to read.</param>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        var value = m_data.Slice(m_position, count);
        m_position += count;
        return value;
    }

    /// <summary>
    /// Reads a decimal value (128 bits as 4 integers).
    /// </summary>
    public decimal ReadDecimal()
    {
        var bits = new int[4];
        for (int i = 0; i < 4; i++)
        {
            bits[i] = ReadInt32();
        }
        return new decimal(bits);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the current read position.
    /// </summary>
    public int Position => m_position;

    /// <summary>
    /// Gets the number of bytes remaining to read.
    /// </summary>
    public int Remaining => m_data.Length - m_position;

    #endregion
}
