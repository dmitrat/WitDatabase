using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Pages;

namespace OutWit.Database.Core.Tree;

/// <summary>
/// Represents a B+Tree node stored in a single page.
/// Works directly with page data without copying.
/// </summary>
/// <remarks>
/// Page layout:
/// [0-15]   Page header (16 bytes)
/// [16-31]  Node header (16 bytes)
/// [32-N]   Cell directory (2 bytes per cell, stores offsets)
/// [N-end]  Cell data (grows from end of page backwards)
/// 
/// Node header layout:
/// [0-1]   KeyCount: 2 bytes
/// [2]     Flags: 1 byte (bit 0 = IsLeaf)
/// [3-6]   NextLeaf: 4 bytes (leaf nodes only)
/// [7-10]  PrevLeaf: 4 bytes (leaf nodes only)
/// [11-12] CellAreaStart: 2 bytes (offset where cell data begins)
/// [13-15] Reserved: 3 bytes
/// 
/// Cell directory starts at offset 32, each entry is 2 bytes (offset to cell)
/// Cell data grows from end of page backwards
/// 
/// Leaf cell format:
/// [KeyLength: VarInt][ValueLength: VarInt][Key bytes][Value bytes]
/// 
/// Internal cell format:
/// [KeyLength: VarInt][Key bytes][ChildPageNumber: 4 bytes]
/// </remarks>
public ref struct BTreeNode
{
    #region Constants

    /// <summary>Offset of node header after page header.</summary>
    private const int NODE_HEADER_OFFSET = PageHeader.PAGE_HEADER_SIZE;
    
    /// <summary>Size of node header.</summary>
    public const int NODE_HEADER_SIZE = 16;
    
    /// <summary>Offset where cell directory starts.</summary>
    public const int CELL_DIR_OFFSET = NODE_HEADER_OFFSET + NODE_HEADER_SIZE;
    
    /// <summary>Size of each cell directory entry.</summary>
    private const int CELL_DIR_ENTRY_SIZE = 2;
    
    // Node header field offsets (relative to NODE_HEADER_OFFSET)
    private const int KEY_COUNT_OFFSET = 0;
    private const int FLAGS_OFFSET = 2;
    private const int NEXT_LEAF_OFFSET = 3;
    private const int PREV_LEAF_OFFSET = 7;
    private const int CELL_AREA_START_OFFSET = 11;
    
    /// <summary>Marker byte indicating value is stored in overflow pages.</summary>
    public const byte OVERFLOW_MARKER = 0xFF;
    
    /// <summary>Size of overflow reference: marker + page number + total length.</summary>
    public const int OVERFLOW_REF_SIZE = 1 + 4 + 4;
    
    /// <summary>Minimum fill factor before merge (50%).</summary>
    public const int MIN_FILL_PERCENT = 50;

    #endregion

    #region Fields

    private readonly Span<byte> m_data;
    private readonly int m_pageSize;
    private readonly uint m_pageNumber;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a node view over existing page data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BTreeNode(Span<byte> data, int pageSize, uint pageNumber)
    {
        m_data = data;
        m_pageSize = pageSize;
        m_pageNumber = pageNumber;
    }

    /// <summary>
    /// Initializes a new empty node.
    /// </summary>
    public static void Initialize(Span<byte> data, int pageSize, bool isLeaf, uint pageNumber)
    {
        // Clear entire page
        data.Clear();
        
        // Initialize page header
        var header = PageHeader.CreateEmpty(
            isLeaf ? PageType.Leaf : PageType.Internal,
            pageSize);
        header.WriteTo(data);
        
        // Initialize node header
        BinaryPrimitives.WriteUInt16LittleEndian(data[(NODE_HEADER_OFFSET + KEY_COUNT_OFFSET)..], 0);
        data[NODE_HEADER_OFFSET + FLAGS_OFFSET] = isLeaf ? (byte)0x01 : (byte)0x00;
        
        // Clear leaf links
        BinaryPrimitives.WriteUInt32LittleEndian(data[(NODE_HEADER_OFFSET + NEXT_LEAF_OFFSET)..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data[(NODE_HEADER_OFFSET + PREV_LEAF_OFFSET)..], 0);
        
        // Cell area starts at end of page
        BinaryPrimitives.WriteUInt16LittleEndian(data[(NODE_HEADER_OFFSET + CELL_AREA_START_OFFSET)..], (ushort)pageSize);
    }

    #endregion

    #region Cell Directory Access (O(1))

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
    private void SetCellOffset(int index, int offset)
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

    #region Search

    /// <summary>
    /// Binary search for a key. Returns index if found, or ~insertPoint if not found.
    /// </summary>
    public readonly int SearchKey(ReadOnlySpan<byte> key)
    {
        int low = 0;
        int high = KeyCount - 1;
        
        while (low <= high)
        {
            int mid = (low + high) >> 1;
            var midKey = GetKey(mid);
            
            int cmp = midKey.SequenceCompareTo(key);
            
            if (cmp == 0)
                return mid;
            else if (cmp < 0)
                low = mid + 1;
            else
                high = mid - 1;
        }
        
        return ~low;
    }

    /// <summary>
    /// Finds the child index for navigation in internal nodes using binary search.
    /// Returns the index of the first key greater than the search key,
    /// or KeyCount if the search key is >= all keys.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int FindChildIndex(ReadOnlySpan<byte> key)
    {
        int low = 0;
        int high = KeyCount;
        
        while (low < high)
        {
            int mid = (low + high) >> 1;
            var midKey = GetKey(mid);
            
            // If key >= midKey, search in right half
            if (key.SequenceCompareTo(midKey) >= 0)
                low = mid + 1;
            else
                high = mid;
        }
        
        return low;
    }

    #endregion

    #region Insert

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

    #endregion

    #region Update

    /// <summary>
    /// Updates the value at the specified index.
    /// </summary>
    public bool UpdateValue(int index, ReadOnlySpan<byte> newValue)
    {
        if (!IsLeaf)
            ThrowNotLeaf();
        if ((uint)index >= (uint)KeyCount)
            ThrowIndexOutOfRange(index);

        var oldValue = GetValue(index);
        
        // If same size, update in place
        if (newValue.Length == oldValue.Length)
        {
            int offset = GetCellOffset(index);
            var (keyLength, keyLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
            offset += keyLenBytes;
            var (_, valueLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
            offset += valueLenBytes + (int)keyLength;
            
            newValue.CopyTo(m_data[offset..]);
            return true;
        }
        
        // Different size - need to delete and re-insert
        var key = GetKey(index).ToArray();
        int oldCellSize = CalculateLeafEntrySize(key.Length, oldValue.Length);
        int newCellSize = CalculateLeafEntrySize(key.Length, newValue.Length);
        int sizeDiff = newCellSize - oldCellSize;
        
        // Check if we have space (considering we free the old cell)
        if (GetFreeSpace() + oldCellSize < newCellSize + CELL_DIR_ENTRY_SIZE)
            return false;
        
        RemoveAt(index);
        return InsertLeaf(index, key, newValue);
    }

    #endregion

    #region Remove

    /// <summary>
    /// Removes the entry at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)KeyCount)
            ThrowIndexOutOfRange(index);

        int keyCount = KeyCount;
        
        // Note: We don't reclaim cell space immediately (would require defragmentation)
        // The space will be reclaimed on next split/merge or compaction
        
        // Shift cell directory entries
        for (int i = index; i < keyCount - 1; i++)
        {
            SetCellOffset(i, GetCellOffset(i + 1));
        }

        KeyCount--;
    }

    /// <summary>
    /// Checks if the node is underfull and needs merging.
    /// </summary>
    public readonly bool IsUnderfull()
    {
        // Consider underfull if less than 25% full (allow some slack before merge)
        int totalCapacity = m_pageSize - CELL_DIR_OFFSET;
        int used = GetUsedSpace();
        return used < totalCapacity / 4 && KeyCount > 0;
    }

    /// <summary>
    /// Gets the minimum number of keys this node should have.
    /// </summary>
    public readonly int MinKeys => 1; // Simplified - in production would be based on fill factor

    #endregion

    #region Merge/Redistribute

    /// <summary>
    /// Moves entries from this node to the target node (for redistribution).
    /// </summary>
    public void MoveEntriesToRight(ref BTreeNode target, int count)
    {
        if (count <= 0 || count > KeyCount)
            return;

        int sourceKeyCount = KeyCount;
        int startIndex = sourceKeyCount - count;
        
        if (IsLeaf)
        {
            // Move from end of this node to beginning of target
            for (int i = 0; i < count; i++)
            {
                var key = GetKey(startIndex + i).ToArray();
                var value = GetValue(startIndex + i).ToArray();
                
                // Shift target entries right
                int targetCount = target.KeyCount;
                for (int j = targetCount; j > 0; j--)
                {
                    target.SetCellOffset(j, target.GetCellOffset(j - 1));
                }
                
                // Insert at beginning of target
                int cellSize = CalculateLeafEntrySize(key.Length, value.Length);
                int newCellStart = target.CellAreaStart - cellSize;
                target.CellAreaStart = (ushort)newCellStart;
                target.SetCellOffset(0, newCellStart);
                
                int offset = newCellStart;
                offset += Encoding.VarInt.EncodeUnsigned(target.m_data[offset..], (ulong)key.Length);
                offset += Encoding.VarInt.EncodeUnsigned(target.m_data[offset..], (ulong)value.Length);
                key.CopyTo(target.m_data[offset..]);
                offset += key.Length;
                value.CopyTo(target.m_data[offset..]);
                
                target.KeyCount++;
            }
        }
        else
        {
            // Internal node - similar but with children
            for (int i = 0; i < count; i++)
            {
                var key = GetKey(startIndex + i).ToArray();
                uint child = GetChild(startIndex + i);
                
                int targetCount = target.KeyCount;
                for (int j = targetCount; j > 0; j--)
                {
                    target.SetCellOffset(j, target.GetCellOffset(j - 1));
                }
                
                int cellSize = CalculateInternalEntrySize(key.Length);
                int newCellStart = target.CellAreaStart - cellSize;
                target.CellAreaStart = (ushort)newCellStart;
                target.SetCellOffset(0, newCellStart);
                
                int offset = newCellStart;
                offset += Encoding.VarInt.EncodeUnsigned(target.m_data[offset..], (ulong)key.Length);
                key.CopyTo(target.m_data[offset..]);
                offset += key.Length;
                BinaryPrimitives.WriteUInt32LittleEndian(target.m_data[offset..], child);
                
                target.KeyCount++;
            }
        }
        
        // Remove moved entries from source
        KeyCount = (ushort)(sourceKeyCount - count);
    }

    /// <summary>
    /// Merges all entries from the source node into this node.
    /// </summary>
    public bool MergeFrom(ref BTreeNode source)
    {
        int sourceCount = source.KeyCount;
        
        if (IsLeaf)
        {
            for (int i = 0; i < sourceCount; i++)
            {
                var key = source.GetKey(i).ToArray();
                var value = source.GetValue(i).ToArray();
                
                if (!CanInsertLeaf(key.Length, value.Length))
                    return false;
                
                InsertLeaf(KeyCount, key, value);
            }
            
            // Update leaf links
            NextLeaf = source.NextLeaf;
        }
        else
        {
            for (int i = 0; i < sourceCount; i++)
            {
                var key = source.GetKey(i).ToArray();
                uint child = source.GetChild(i);
                
                if (!CanInsertInternal(key.Length))
                    return false;
                
                InsertInternal(KeyCount, key, child);
            }
            
            RightmostChild = source.RightmostChild;
        }
        
        return true;
    }

    #endregion

    #region Split Helpers

    /// <summary>
    /// Collects all entries for splitting.
    /// </summary>
    public readonly void CollectLeafEntries(out byte[][] keys, out byte[][] values)
    {
        int count = KeyCount;
        keys = new byte[count][];
        values = new byte[count][];
        
        for (int i = 0; i < count; i++)
        {
            keys[i] = GetKey(i).ToArray();
            values[i] = GetValue(i).ToArray();
        }
    }

    /// <summary>
    /// Collects all entries for splitting internal nodes.
    /// </summary>
    public readonly void CollectInternalEntries(out byte[][] keys, out uint[] children)
    {
        int count = KeyCount;
        keys = new byte[count][];
        children = new uint[count + 1];
        
        for (int i = 0; i < count; i++)
        {
            keys[i] = GetKey(i).ToArray();
            children[i] = GetChild(i);
        }
        children[count] = RightmostChild;
    }

    /// <summary>
    /// Clears all entries from the node.
    /// </summary>
    public void Clear()
    {
        KeyCount = 0;
        CellAreaStart = (ushort)m_pageSize;
    }

    #endregion

    #region Properties

    /// <summary>Page number of this node.</summary>
    public readonly uint PageNumber => m_pageNumber;

    /// <summary>Number of keys in this node.</summary>
    public ushort KeyCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => BinaryPrimitives.ReadUInt16LittleEndian(m_data[(NODE_HEADER_OFFSET + KEY_COUNT_OFFSET)..]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => BinaryPrimitives.WriteUInt16LittleEndian(m_data[(NODE_HEADER_OFFSET + KEY_COUNT_OFFSET)..], value);
    }

    /// <summary>Whether this is a leaf node.</summary>
    public readonly bool IsLeaf
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (m_data[NODE_HEADER_OFFSET + FLAGS_OFFSET] & 0x01) != 0;
    }

    /// <summary>Pointer to next leaf (for range scans).</summary>
    public uint NextLeaf
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => BinaryPrimitives.ReadUInt32LittleEndian(m_data[(NODE_HEADER_OFFSET + NEXT_LEAF_OFFSET)..]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => BinaryPrimitives.WriteUInt32LittleEndian(m_data[(NODE_HEADER_OFFSET + NEXT_LEAF_OFFSET)..], value);
    }

    /// <summary>Pointer to previous leaf.</summary>
    public uint PrevLeaf
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => BinaryPrimitives.ReadUInt32LittleEndian(m_data[(NODE_HEADER_OFFSET + PREV_LEAF_OFFSET)..]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => BinaryPrimitives.WriteUInt32LittleEndian(m_data[(NODE_HEADER_OFFSET + PREV_LEAF_OFFSET)..], value);
    }

    /// <summary>Start offset of cell data area.</summary>
    public ushort CellAreaStart
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => BinaryPrimitives.ReadUInt16LittleEndian(m_data[(NODE_HEADER_OFFSET + CELL_AREA_START_OFFSET)..]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => BinaryPrimitives.WriteUInt16LittleEndian(m_data[(NODE_HEADER_OFFSET + CELL_AREA_START_OFFSET)..], value);
    }

    /// <summary>Rightmost child pointer (internal nodes). Stored in PageHeader.RightChild.</summary>
    public uint RightmostChild
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => BinaryPrimitives.ReadUInt32LittleEndian(m_data[8..]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => BinaryPrimitives.WriteUInt32LittleEndian(m_data[8..], value);
    }

    /// <summary>Raw data span.</summary>
    public readonly Span<byte> Data => m_data;

    #endregion

    #region Exceptions

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowIndexOutOfRange(int index) =>
        throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotLeaf() =>
        throw new InvalidOperationException("Operation only valid for leaf nodes");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotInternal() =>
        throw new InvalidOperationException("Operation only valid for internal nodes");

    #endregion
}
