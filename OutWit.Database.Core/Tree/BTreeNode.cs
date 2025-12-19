using System.Buffers.Binary;
using System.Runtime.CompilerServices;
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
/// 
/// File organization:
/// - BTreeNode.cs: Core structure (constants, fields, constructor, properties)
/// - BTreeNode.CellAccess.cs: Cell directory and key/value access
/// - BTreeNode.Search.cs: Search operations
/// - BTreeNode.Insert.cs: Insert operations
/// - BTreeNode.Update.cs: Update operations
/// - BTreeNode.Remove.cs: Remove and compaction operations
/// - BTreeNode.Split.cs: Merge, redistribute, and split helpers
/// </remarks>
public ref partial struct BTreeNode
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
