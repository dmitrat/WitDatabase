using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace OutWit.Database.Core.Tree;

public ref partial struct BTreeNode
{
    #region Cell Directory Access

    /// <summary>
    /// Gets the offset of the cell at the specified index from the directory.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetCellOffset(int index)
    {
        int dirOffset = CELL_DIR_OFFSET + index * CELL_DIR_ENTRY_SIZE;
        return BinaryPrimitives.ReadUInt16LittleEndian(m_data[dirOffset..]);
    }

    /// <summary>
    /// Sets the offset in the cell directory.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetCellOffset(int index, int offset)
    {
        int dirOffset = CELL_DIR_OFFSET + index * CELL_DIR_ENTRY_SIZE;
        BinaryPrimitives.WriteUInt16LittleEndian(m_data[dirOffset..], (ushort)offset);
    }

    /// <summary>
    /// Gets the size of the cell at the specified offset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int GetCellSize(int offset)
    {
        var (keyLength, keyLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
        offset += keyLenBytes;
        
        if (IsLeaf)
        {
            var (valueLength, valueLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
            return keyLenBytes + valueLenBytes + (int)keyLength + (int)valueLength;
        }
        else
        {
            return keyLenBytes + (int)keyLength + 4;
        }
    }

    /// <summary>
    /// Gets the total used space (cell directory + cell data).
    /// </summary>
    public readonly int GetUsedSpace()
    {
        int keyCount = KeyCount;
        int dirSize = keyCount * CELL_DIR_ENTRY_SIZE;
        int cellDataSize = m_pageSize - CellAreaStart;
        return dirSize + cellDataSize;
    }

    /// <summary>
    /// Gets available space for new entries.
    /// </summary>
    public readonly int GetFreeSpace()
    {
        int dirEnd = CELL_DIR_OFFSET + KeyCount * CELL_DIR_ENTRY_SIZE;
        return CellAreaStart - dirEnd;
    }

    #endregion

    #region Key/Value Access

    /// <summary>
    /// Gets the key at the specified index.
    /// </summary>
    public readonly ReadOnlySpan<byte> GetKey(int index)
    {
        if ((uint)index >= (uint)KeyCount)
            ThrowIndexOutOfRange(index);

        int offset = GetCellOffset(index);
        var (keyLength, keyLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
        offset += keyLenBytes;
        
        if (IsLeaf)
        {
            var (_, valueLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
            offset += valueLenBytes;
        }
        
        return m_data.Slice(offset, (int)keyLength);
    }

    /// <summary>
    /// Gets the value at the specified index (leaf nodes only).
    /// </summary>
    public readonly ReadOnlySpan<byte> GetValue(int index)
    {
        if (!IsLeaf)
            ThrowNotLeaf();
        if ((uint)index >= (uint)KeyCount)
            ThrowIndexOutOfRange(index);

        int offset = GetCellOffset(index);
        var (keyLength, keyLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
        offset += keyLenBytes;
        
        var (valueLength, valueLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
        offset += valueLenBytes + (int)keyLength;
        
        return m_data.Slice(offset, (int)valueLength);
    }

    /// <summary>
    /// Checks if the value at the specified index is stored in overflow pages.
    /// </summary>
    public readonly bool IsOverflowValue(int index)
    {
        var value = GetValue(index);
        return value.Length == OVERFLOW_REF_SIZE && value[0] == OVERFLOW_MARKER;
    }

    /// <summary>
    /// Gets the overflow page number for a value stored in overflow pages.
    /// </summary>
    public readonly uint GetOverflowPage(int index)
    {
        var value = GetValue(index);
        if (value.Length != OVERFLOW_REF_SIZE || value[0] != OVERFLOW_MARKER)
            throw new InvalidOperationException("Value is not an overflow reference");
        
        return BinaryPrimitives.ReadUInt32LittleEndian(value[1..]);
    }

    /// <summary>
    /// Gets the total length of an overflow value.
    /// </summary>
    public readonly int GetOverflowLength(int index)
    {
        var value = GetValue(index);
        if (value.Length != OVERFLOW_REF_SIZE || value[0] != OVERFLOW_MARKER)
            throw new InvalidOperationException("Value is not an overflow reference");
        
        return BinaryPrimitives.ReadInt32LittleEndian(value[5..]);
    }

    /// <summary>
    /// Gets the child page number at the specified index (internal nodes only).
    /// </summary>
    public readonly uint GetChild(int index)
    {
        if (IsLeaf)
            ThrowNotInternal();
        
        if (index == KeyCount)
            return RightmostChild;
        
        if ((uint)index > (uint)KeyCount)
            ThrowIndexOutOfRange(index);

        int offset = GetCellOffset(index);
        var (keyLength, keyLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
        offset += keyLenBytes + (int)keyLength;
        
        return BinaryPrimitives.ReadUInt32LittleEndian(m_data[offset..]);
    }

    /// <summary>
    /// Sets the child page number at the specified index (internal nodes only).
    /// </summary>
    public void SetChild(int index, uint childPageNumber)
    {
        if (IsLeaf)
            ThrowNotInternal();
        
        if (index == KeyCount)
        {
            RightmostChild = childPageNumber;
            return;
        }
        
        if ((uint)index > (uint)KeyCount)
            ThrowIndexOutOfRange(index);

        int offset = GetCellOffset(index);
        var (keyLength, keyLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
        offset += keyLenBytes + (int)keyLength;
        
        BinaryPrimitives.WriteUInt32LittleEndian(m_data[offset..], childPageNumber);
    }

    #endregion
}
