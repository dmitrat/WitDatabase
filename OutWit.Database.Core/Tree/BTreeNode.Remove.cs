namespace OutWit.Database.Core.Tree;

public ref partial struct BTreeNode
{
    #region Remove

    /// <summary>
    /// Removes the entry at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)KeyCount)
            ThrowIndexOutOfRange(index);

        int keyCount = KeyCount;
        
        // Note: We don't reclaim cell space immediately
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
    public readonly int MinKeys => 1;

    #endregion

    #region Compaction

    /// <summary>
    /// Gets the amount of fragmented (dead) space in the cell area.
    /// This is space that was used by deleted entries but not yet reclaimed.
    /// </summary>
    public readonly int GetFragmentedSpace()
    {
        if (KeyCount == 0)
            return m_pageSize - CellAreaStart;
        
        // Calculate actual used cell data size
        int actualCellDataSize = 0;
        int keyCount = KeyCount;
        for (int i = 0; i < keyCount; i++)
        {
            int offset = GetCellOffset(i);
            actualCellDataSize += GetCellSize(offset);
        }
        
        // Fragmented space = allocated cell area - actual used
        int allocatedCellArea = m_pageSize - CellAreaStart;
        return allocatedCellArea - actualCellDataSize;
    }

    /// <summary>
    /// Checks if compaction would free significant space.
    /// Returns true if fragmented space is more than 25% of page or more than 256 bytes.
    /// </summary>
    public readonly bool NeedsCompaction()
    {
        int fragmented = GetFragmentedSpace();
        return fragmented > m_pageSize / 4 || fragmented > 256;
    }

    /// <summary>
    /// Gets the total usable space after compaction.
    /// This is the space that would be available for new entries after compacting.
    /// </summary>
    public readonly int GetUsableSpaceAfterCompaction()
    {
        return GetFreeSpace() + GetFragmentedSpace();
    }

    /// <summary>
    /// Compacts the node by eliminating fragmented space in the cell area.
    /// This reorganizes cells to be contiguous, reclaiming space from deleted entries.
    /// </summary>
    /// <remarks>
    /// The compaction works by:
    /// 1. Collecting all live cells (those referenced by directory)
    /// 2. Rewriting them contiguously from the end of the page
    /// 3. Updating directory offsets
    /// 
    /// This is O(n) where n is the number of entries.
    /// </remarks>
    public void Compact()
    {
        int keyCount = KeyCount;
        if (keyCount == 0)
        {
            // No entries - just reset cell area
            CellAreaStart = (ushort)m_pageSize;
            return;
        }
        
        // Collect all cell data with their sizes
        Span<(int offset, int size)> cells = keyCount <= 64 
            ? stackalloc (int, int)[keyCount] 
            : new (int, int)[keyCount];
        
        int totalCellDataSize = 0;
        for (int i = 0; i < keyCount; i++)
        {
            int offset = GetCellOffset(i);
            int size = GetCellSize(offset);
            cells[i] = (offset, size);
            totalCellDataSize += size;
        }
        
        // Check if compaction is needed
        int currentCellAreaSize = m_pageSize - CellAreaStart;
        if (totalCellDataSize == currentCellAreaSize)
        {
            // No fragmentation - nothing to do
            return;
        }
        
        // Allocate temporary buffer for cell data
        byte[] tempBuffer = new byte[totalCellDataSize];
        int tempOffset = 0;
        
        // Copy all cell data to temp buffer
        for (int i = 0; i < keyCount; i++)
        {
            var (cellOffset, cellSize) = cells[i];
            m_data.Slice(cellOffset, cellSize).CopyTo(tempBuffer.AsSpan(tempOffset));
            tempOffset += cellSize;
        }
        
        // Clear the old cell area
        m_data[CellAreaStart..].Clear();
        
        // Write cells back contiguously from end of page
        int newCellAreaStart = m_pageSize;
        tempOffset = 0;
        
        for (int i = 0; i < keyCount; i++)
        {
            var (_, cellSize) = cells[i];
            newCellAreaStart -= cellSize;
            tempBuffer.AsSpan(tempOffset, cellSize).CopyTo(m_data[newCellAreaStart..]);
            SetCellOffset(i, newCellAreaStart);
            tempOffset += cellSize;
        }
        
        CellAreaStart = (ushort)newCellAreaStart;
    }

    #endregion
}
