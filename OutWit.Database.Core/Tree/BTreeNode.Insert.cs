using System.Buffers.Binary;

namespace OutWit.Database.Core.Tree;

public ref partial struct BTreeNode
{
    #region Size Calculation

    /// <summary>
    /// Calculates the size needed to store a leaf entry.
    /// </summary>
    public static int CalculateLeafEntrySize(int keyLength, int valueLength)
    {
        return Encoding.VarInt.GetEncodedLengthUnsigned((ulong)keyLength) +
               Encoding.VarInt.GetEncodedLengthUnsigned((ulong)valueLength) +
               keyLength + valueLength;
    }

    /// <summary>
    /// Calculates the size needed to store an internal entry.
    /// </summary>
    public static int CalculateInternalEntrySize(int keyLength)
    {
        return Encoding.VarInt.GetEncodedLengthUnsigned((ulong)keyLength) + keyLength + 4;
    }

    /// <summary>
    /// Checks if there's enough space to insert a leaf entry.
    /// </summary>
    public readonly bool CanInsertLeaf(int keyLength, int valueLength)
    {
        int entrySize = CalculateLeafEntrySize(keyLength, valueLength);
        int dirEntrySize = CELL_DIR_ENTRY_SIZE;
        return GetFreeSpace() >= entrySize + dirEntrySize;
    }

    /// <summary>
    /// Checks if there's enough space to insert an internal entry.
    /// </summary>
    public readonly bool CanInsertInternal(int keyLength)
    {
        int entrySize = CalculateInternalEntrySize(keyLength);
        int dirEntrySize = CELL_DIR_ENTRY_SIZE;
        return GetFreeSpace() >= entrySize + dirEntrySize;
    }

    #endregion

    #region Insert

    /// <summary>
    /// Inserts a key-value pair at the specified index (leaf nodes only).
    /// </summary>
    public bool InsertLeaf(int index, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (!IsLeaf)
            ThrowNotLeaf();
        
        int cellSize = CalculateLeafEntrySize(key.Length, value.Length);
        
        if (!CanInsertLeaf(key.Length, value.Length))
            return false;

        int keyCount = KeyCount;
        
        // Shift cell directory entries
        for (int i = keyCount; i > index; i--)
        {
            SetCellOffset(i, GetCellOffset(i - 1));
        }
        
        // Allocate space at beginning of cell area
        int newCellStart = CellAreaStart - cellSize;
        CellAreaStart = (ushort)newCellStart;
        SetCellOffset(index, newCellStart);
        
        // Write cell data
        int offset = newCellStart;
        offset += Encoding.VarInt.EncodeUnsigned(m_data[offset..], (ulong)key.Length);
        offset += Encoding.VarInt.EncodeUnsigned(m_data[offset..], (ulong)value.Length);
        key.CopyTo(m_data[offset..]);
        offset += key.Length;
        value.CopyTo(m_data[offset..]);

        KeyCount++;
        return true;
    }

    /// <summary>
    /// Inserts an overflow reference at the specified index (leaf nodes only).
    /// </summary>
    public bool InsertLeafOverflow(int index, ReadOnlySpan<byte> key, uint overflowPage, int totalLength)
    {
        var overflowRef = new byte[OVERFLOW_REF_SIZE];
        overflowRef[0] = OVERFLOW_MARKER;
        BinaryPrimitives.WriteUInt32LittleEndian(overflowRef.AsSpan(1), overflowPage);
        BinaryPrimitives.WriteInt32LittleEndian(overflowRef.AsSpan(5), totalLength);
        
        return InsertLeaf(index, key, overflowRef);
    }

    /// <summary>
    /// Inserts a key-child pair at the specified index (internal nodes only).
    /// </summary>
    public bool InsertInternal(int index, ReadOnlySpan<byte> key, uint childPageNumber)
    {
        if (IsLeaf)
            ThrowNotInternal();
        
        int cellSize = CalculateInternalEntrySize(key.Length);
        
        if (!CanInsertInternal(key.Length))
            return false;

        int keyCount = KeyCount;
        
        // Shift cell directory entries
        for (int i = keyCount; i > index; i--)
        {
            SetCellOffset(i, GetCellOffset(i - 1));
        }
        
        // Allocate space
        int newCellStart = CellAreaStart - cellSize;
        CellAreaStart = (ushort)newCellStart;
        SetCellOffset(index, newCellStart);
        
        // Write cell data
        int offset = newCellStart;
        offset += Encoding.VarInt.EncodeUnsigned(m_data[offset..], (ulong)key.Length);
        key.CopyTo(m_data[offset..]);
        offset += key.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(m_data[offset..], childPageNumber);

        KeyCount++;
        return true;
    }

    /// <summary>
    /// Attempts to insert a leaf entry, compacting first if necessary.
    /// Returns true if insert succeeded, false if still not enough space after compaction.
    /// </summary>
    public bool InsertLeafWithCompaction(int index, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (!IsLeaf)
            ThrowNotLeaf();
        
        // Try direct insert first
        if (CanInsertLeaf(key.Length, value.Length))
        {
            return InsertLeaf(index, key, value);
        }
        
        // Check if compaction would help
        int needed = CalculateLeafEntrySize(key.Length, value.Length) + CELL_DIR_ENTRY_SIZE;
        int availableAfterCompaction = GetUsableSpaceAfterCompaction();
        
        if (availableAfterCompaction < needed)
        {
            // Even after compaction, not enough space
            return false;
        }
        
        // Compact and retry
        Compact();
        return InsertLeaf(index, key, value);
    }

    /// <summary>
    /// Attempts to insert an internal entry, compacting first if necessary.
    /// Returns true if insert succeeded, false if still not enough space after compaction.
    /// </summary>
    public bool InsertInternalWithCompaction(int index, ReadOnlySpan<byte> key, uint childPageNumber)
    {
        if (IsLeaf)
            ThrowNotInternal();
        
        // Try direct insert first
        if (CanInsertInternal(key.Length))
        {
            return InsertInternal(index, key, childPageNumber);
        }
        
        // Check if compaction would help
        int needed = CalculateInternalEntrySize(key.Length) + CELL_DIR_ENTRY_SIZE;
        int availableAfterCompaction = GetUsableSpaceAfterCompaction();
        
        if (availableAfterCompaction < needed)
        {
            return false;
        }
        
        // Compact and retry
        Compact();
        return InsertInternal(index, key, childPageNumber);
    }

    #endregion
}
