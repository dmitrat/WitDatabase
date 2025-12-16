using System.Buffers.Binary;
using OutWit.Database.Core.Encoding;

namespace OutWit.Database.Core.Pages;

/// <summary>
/// Represents a database page with cell management capabilities.
/// Cells are stored from the end of the page growing upward,
/// while cell pointers are stored after the header growing downward.
/// </summary>
public ref struct Page
{
    #region Fields

    private readonly Span<byte> m_data;

    private readonly int m_pageSize;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a Page wrapper around the provided buffer.
    /// </summary>
    public Page(Span<byte> data)
    {
        m_data = data;
        m_pageSize = data.Length;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the page as empty with the specified type.
    /// </summary>
    public void Initialize(PageType pageType)
    {
        m_data.Clear();
        Header = PageHeader.CreateEmpty(pageType, m_pageSize);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Gets the cell pointer (offset) at the specified index.
    /// </summary>
    public readonly ushort GetCellPointer(int index)
    {
        if (index < 0 || index >= Header.CellCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        int offset = CellPointerStart + (index * 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(m_data[offset..]);
    }

    /// <summary>
    /// Sets the cell pointer at the specified index.
    /// </summary>
    public void SetCellPointer(int index, ushort offset)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        int pointerOffset = CellPointerStart + (index * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(m_data[pointerOffset..], offset);
    }

    /// <summary>
    /// Gets the cell content at the specified index.
    /// </summary>
    public readonly ReadOnlySpan<byte> GetCell(int index)
    {
        ushort cellOffset = GetCellPointer(index);

        // Read cell size (first varint in cell) - this is the payload size
        var (payloadSize, headerBytes) = VarInt.DecodeUnsigned(m_data[cellOffset..]);

        // Total cell size = varint header + payload
        int totalCellSize = headerBytes + (int)payloadSize;

        return m_data.Slice(cellOffset, totalCellSize);
    }

    /// <summary>
    /// Gets the total size of a cell at the given offset.
    /// </summary>
    private readonly int GetCellSize(int cellOffset)
    {
        var (payloadSize, headerBytes) = VarInt.DecodeUnsigned(m_data[cellOffset..]);
        return headerBytes + (int)payloadSize;
    }

    /// <summary>
    /// Inserts a new cell into the page.
    /// Returns the index where the cell was inserted, or -1 if not enough space.
    /// </summary>
    public int InsertCell(int index, ReadOnlySpan<byte> cellContent)
    {
        var header = Header;
        int cellSize = cellContent.Length;

        // Validate index
        if (index < 0 || index > header.CellCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        // Check if there's enough space
        int requiredSpace = cellSize + 2; // cell content + pointer
        int cellPointerEnd = CellPointerStart + (header.CellCount * 2);
        int contiguousFreeSpace = header.FreeSpaceStart - cellPointerEnd;

        if (requiredSpace > contiguousFreeSpace + header.FragmentedFreeSpace)
        {
            return -1; // Not enough space even after defragmentation
        }

        // Defragment if needed
        if (requiredSpace > contiguousFreeSpace)
        {
            Defragment();
            header = Header;
        }

        // Recalculate after potential defragmentation
        cellPointerEnd = CellPointerStart + (header.CellCount * 2);

        // Verify we have space for the new pointer
        int newCellOffset = header.FreeSpaceStart - cellSize;
        if (newCellOffset < cellPointerEnd + 2)
        {
            return -1; // Safety check
        }

        // Allocate space for cell content (from end, growing up)
        ushort cellOffset = (ushort)newCellOffset;
        cellContent.CopyTo(m_data[cellOffset..]);

        // Shift existing pointers if inserting in the middle
        if (index < header.CellCount)
        {
            int shiftStart = CellPointerStart + (index * 2);
            int shiftEnd = CellPointerStart + (header.CellCount * 2);

            // Move pointers to make room (copy backwards to avoid overlap issues)
            for (int i = shiftEnd - 1; i >= shiftStart; i--)
            {
                m_data[i + 2] = m_data[i];
            }
        }

        // Write the new cell pointer
        SetCellPointer(index, cellOffset);

        // Update header
        header.CellCount++;
        header.FreeSpaceStart = cellOffset;
        Header = header;

        return index;
    }

    /// <summary>
    /// Deletes a cell at the specified index.
    /// </summary>
    public void DeleteCell(int index)
    {
        var header = Header;

        if (index < 0 || index >= header.CellCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        // Get size of deleted cell for fragmentation tracking
        ushort cellOffset = GetCellPointer(index);
        int cellSize = GetCellSize(cellOffset);

        // Shift pointers down
        if (index < header.CellCount - 1)
        {
            int shiftStart = CellPointerStart + ((index + 1) * 2);
            int shiftEnd = CellPointerStart + (header.CellCount * 2);
            int destStart = CellPointerStart + (index * 2);

            m_data[shiftStart..shiftEnd].CopyTo(m_data[destStart..]);
        }

        // Update header
        header.CellCount--;
        header.FragmentedFreeSpace += (ushort)cellSize;
        Header = header;
    }

    /// <summary>
    /// Defragments the page by compacting all cells.
    /// </summary>
    public void Defragment()
    {
        var header = Header;
        if (header.CellCount == 0)
        {
            header.FreeSpaceStart = (ushort)m_pageSize;
            header.FragmentedFreeSpace = 0;
            Header = header;
            return;
        }

        int cellCount = header.CellCount;

        // Collect cell info into stackalloc arrays for small counts, heap for large
        Span<ushort> offsets = cellCount <= 128 
            ? stackalloc ushort[cellCount] 
            : new ushort[cellCount];
        Span<ushort> sizes = cellCount <= 128 
            ? stackalloc ushort[cellCount] 
            : new ushort[cellCount];

        for (int i = 0; i < cellCount; i++)
        {
            offsets[i] = GetCellPointer(i);
            sizes[i] = (ushort)GetCellSize(offsets[i]);
        }

        // Create index array for sorting
        Span<int> sortedIndices = cellCount <= 128 
            ? stackalloc int[cellCount] 
            : new int[cellCount];
        for (int i = 0; i < cellCount; i++)
            sortedIndices[i] = i;

        // Sort indices by offset descending using insertion sort (good for small arrays)
        // or heap-based approach for larger arrays
        if (cellCount <= 16)
        {
            // Insertion sort for very small arrays - O(n²) but low overhead
            for (int i = 1; i < cellCount; i++)
            {
                int key = sortedIndices[i];
                int j = i - 1;
                while (j >= 0 && offsets[sortedIndices[j]] < offsets[key])
                {
                    sortedIndices[j + 1] = sortedIndices[j];
                    j--;
                }
                sortedIndices[j + 1] = key;
            }
        }
        else
        {
            // For larger arrays, use Array.Sort with custom comparison
            // Convert to array, sort, copy back
            int[] indices = sortedIndices.ToArray();
            ushort[] offsetsCopy = offsets.ToArray();
            Array.Sort(indices, (a, b) => offsetsCopy[b].CompareTo(offsetsCopy[a])); // Descending
            indices.CopyTo(sortedIndices);
        }

        // Compact cells from end of page, processing highest offset first
        // This ensures we never overwrite data we haven't copied yet
        int writePosition = m_pageSize;
        Span<ushort> newPointers = cellCount <= 128 
            ? stackalloc ushort[cellCount] 
            : new ushort[cellCount];

        for (int i = 0; i < cellCount; i++)
        {
            int cellIndex = sortedIndices[i];
            ushort oldOffset = offsets[cellIndex];
            int size = sizes[cellIndex];

            writePosition -= size;

            // Copy cell data (handles overlapping regions correctly by processing high-to-low)
            if (writePosition != oldOffset)
            {
                m_data.Slice(oldOffset, size).CopyTo(m_data[writePosition..]);
            }

            newPointers[cellIndex] = (ushort)writePosition;
        }

        // Update all pointers
        for (int i = 0; i < cellCount; i++)
        {
            SetCellPointer(i, newPointers[i]);
        }

        // Update header
        header.FreeSpaceStart = (ushort)writePosition;
        header.FragmentedFreeSpace = 0;
        Header = header;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the page header.
    /// </summary>
    public PageHeader Header
    {
        readonly get => PageHeader.ReadFrom(m_data);
        set => value.WriteTo(m_data);
    }

    /// <summary>
    /// Gets the raw page data.
    /// </summary>
    public readonly Span<byte> Data => m_data;

    /// <summary>
    /// Gets the usable space in the page (excluding header).
    /// </summary>
    public readonly int UsableSpace => m_pageSize - PageHeader.PAGE_HEADER_SIZE;

    /// <summary>
    /// Gets the offset where cell pointers start (after header).
    /// </summary>
    public readonly int CellPointerStart => PageHeader.PAGE_HEADER_SIZE;

    /// <summary>
    /// Gets the size of cell pointer array in bytes.
    /// </summary>
    public readonly int CellPointerArraySize => Header.CellCount * 2;

    /// <summary>
    /// Gets the amount of contiguous free space available.
    /// </summary>
    public readonly int FreeSpace
    {
        get
        {
            var header = Header;
            int cellPointerEnd = CellPointerStart + CellPointerArraySize;
            return header.FreeSpaceStart - cellPointerEnd + header.FragmentedFreeSpace;
        }
    }

    #endregion
}
