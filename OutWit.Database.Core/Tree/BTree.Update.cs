using System.Buffers;
using System.Buffers.Binary;

namespace OutWit.Database.Core.Tree;

public sealed partial class BTree
{
    #region Update

    private void UpdateValue(uint pageNumber, int index, ReadOnlySpan<byte> newValue)
    {
        // First, get the key and check for overflow - we need the key before any modifications
        byte[] key;
        bool wasOverflow;
        uint oldOverflowPage = 0;
        
        {
            var page = m_pageManager.GetPage(pageNumber);
            var node = new BTreeNode(page.Data, PageSize, pageNumber);
            
            // Verify index is still valid
            if (index < 0 || index >= node.KeyCount)
            {
                m_pageManager.ReleasePage(pageNumber);
                throw new InvalidOperationException($"Index {index} is out of range for node with {node.KeyCount} keys");
            }
            
            // Save key before any modifications
            key = node.GetKey(index).ToArray();
            wasOverflow = node.IsOverflowValue(index);
            
            if (wasOverflow)
            {
                oldOverflowPage = node.GetOverflowPage(index);
            }
            
            m_pageManager.ReleasePage(pageNumber);
        }
        
        // Free old overflow if needed
        if (wasOverflow)
        {
            m_overflowManager.FreeOverflow(oldOverflowPage);
        }
        
        // Determine if new value needs overflow
        bool needsOverflow = newValue.Length > m_maxInlineValueSize;
        uint newOverflowPage = 0;
        byte[]? overflowRef = null;
        
        if (needsOverflow)
        {
            newOverflowPage = m_overflowManager.StoreOverflow(newValue);
            overflowRef = new byte[BTreeNode.OVERFLOW_REF_SIZE];
            overflowRef[0] = BTreeNode.OVERFLOW_MARKER;
            BinaryPrimitives.WriteUInt32LittleEndian(overflowRef.AsSpan(1), newOverflowPage);
            BinaryPrimitives.WriteInt32LittleEndian(overflowRef.AsSpan(5), newValue.Length);
        }
        
        var updatePage = m_pageManager.GetPage(pageNumber);
        var updateNode = new BTreeNode(updatePage.Data, PageSize, pageNumber);
        
        try
        {
            // Re-find the key position since page might have changed
            int currentIndex = updateNode.SearchKey(key);
            if (currentIndex < 0)
            {
                throw new InvalidOperationException($"Key disappeared during update operation");
            }
            
            ReadOnlySpan<byte> valueToStore = needsOverflow ? overflowRef : newValue;
            
            // Try update in place first (works if same size)
            if (updateNode.UpdateValue(currentIndex, valueToStore))
            {
                updatePage.MarkDirty();
                return;
            }
            
            // Can't update in place - need to remove and reinsert
            // First check if we have enough space with compaction
            int newCellSize = BTreeNode.CalculateLeafEntrySize(key.Length, valueToStore.Length);
            int dirEntrySize = 2; // CELL_DIR_ENTRY_SIZE
            int neededSpace = newCellSize + dirEntrySize;
            
            // Calculate space available after removing old entry and compacting
            int availableSpace = updateNode.GetUsableSpaceAfterCompaction() + dirEntrySize;
            
            if (availableSpace >= neededSpace)
            {
                // Remove old entry
                updateNode.RemoveAt(currentIndex);
                
                // Find new insert position
                int newIndex = updateNode.SearchKey(key);
                if (newIndex >= 0)
                {
                    throw new InvalidOperationException($"Key reappeared after removal");
                }
                newIndex = ~newIndex;
                
                // Try insert with compaction
                bool inserted;
                if (needsOverflow)
                {
                    inserted = updateNode.InsertLeafWithCompaction(newIndex, key, overflowRef!);
                }
                else
                {
                    inserted = updateNode.InsertLeafWithCompaction(newIndex, key, valueToStore);
                }
                
                if (inserted)
                {
                    updatePage.MarkDirty();
                    return;
                }
            }
            else
            {
                // Not enough space even with compaction - remove entry for split path
                updateNode.RemoveAt(currentIndex);
            }
            
            // Not enough space in same page - use regular Insert which can split
            updatePage.MarkDirty();
            m_pageManager.ReleasePage(pageNumber);
            updatePage = null!;
            
            // Decrement count since we removed
            m_entryCount--;
            
            // Insert using normal path (which can trigger splits)
            bool insertResult;
            if (needsOverflow)
            {
                insertResult = InsertWithOverflowRef(key, overflowRef!, newOverflowPage, newValue.Length);
            }
            else
            {
                insertResult = Insert(key, newValue);
            }
            
            if (!insertResult)
            {
                throw new InvalidOperationException("Failed to reinsert key after update - key already exists?");
            }
            
            return;
        }
        finally
        {
            if (updatePage != null!)
            {
                m_pageManager.ReleasePage(pageNumber);
            }
        }
    }

    /// <summary>
    /// Internal method to insert with pre-created overflow reference.
    /// </summary>
    private bool InsertWithOverflowRef(byte[] key, byte[] overflowRef, uint overflowPage, int totalLength)
    {
        var pathPages = ArrayPool<uint>.Shared.Rent(MAX_TREE_DEPTH);
        var pathChildIndices = ArrayPool<int>.Shared.Rent(MAX_TREE_DEPTH);
        
        try
        {
            int pathLength = 0;
            uint currentPage = m_rootPageNumber;
            
            // Navigate to leaf
            while (true)
            {
                var page = m_pageManager.GetPage(currentPage);
                var node = new BTreeNode(page.Data, PageSize, currentPage);
                
                if (node.IsLeaf)
                {
                    m_pageManager.ReleasePage(currentPage);
                    break;
                }
                
                int childIndex = node.FindChildIndex(key);
                uint childPage = childIndex < node.KeyCount 
                    ? node.GetChild(childIndex) 
                    : node.RightmostChild;
                
                pathPages[pathLength] = currentPage;
                pathChildIndices[pathLength] = childIndex;
                pathLength++;
                
                m_pageManager.ReleasePage(currentPage);
                currentPage = childPage;
            }
            
            var leafPage = m_pageManager.GetPage(currentPage);
            var leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
            
            int insertIndex = leafNode.SearchKey(key);
            if (insertIndex >= 0)
            {
                m_pageManager.ReleasePage(currentPage);
                return false; // Key exists
            }
            insertIndex = ~insertIndex;
            
            // Try insert with compaction
            if (leafNode.InsertLeafWithCompaction(insertIndex, key, overflowRef))
            {
                leafPage.MarkDirty();
                m_pageManager.ReleasePage(currentPage);
                m_entryCount++;
                m_entryCountDirty = true;
                return true;
            }
            
            // Need split
            var splitResult = SplitLeaf(leafPage, ref leafNode, insertIndex, key, overflowRef, 
                needsOverflow: false, overflowPage: 0);
            m_pageManager.ReleasePage(currentPage);
            
            return PropagateSplitUp(pathPages, pathChildIndices, pathLength, splitResult);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(pathPages);
            ArrayPool<int>.Shared.Return(pathChildIndices);
        }
    }

    #endregion
}
