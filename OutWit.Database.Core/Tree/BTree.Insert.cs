using System.Buffers;
using System.Buffers.Binary;
using OutWit.Database.Core.Cache;

namespace OutWit.Database.Core.Tree;

public sealed partial class BTree
{
    #region Insert

    /// <summary>
    /// Inserts a key-value pair into the tree.
    /// </summary>
    public bool Insert(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        ValidateValue(value);
        
        // Rent arrays from pool to avoid allocations
        var pathPages = ArrayPool<uint>.Shared.Rent(MAX_TREE_DEPTH);
        var pathChildIndices = ArrayPool<int>.Shared.Rent(MAX_TREE_DEPTH);
        
        try
        {
            int pathLength = 0;
            uint currentPage = m_rootPageNumber;
            
            // Navigate to leaf, recording path
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
            
            // Insert into leaf
            var leafPage = m_pageManager.GetPage(currentPage);
            var leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
            
            int insertIndex = leafNode.SearchKey(key);
            if (insertIndex >= 0)
            {
                // Key exists
                m_pageManager.ReleasePage(currentPage);
                return false;
            }
            
            insertIndex = ~insertIndex;
            
            // Determine if value needs overflow
            bool needsOverflow = value.Length > m_maxInlineValueSize;
            uint overflowPage = 0;
            
            if (needsOverflow)
            {
                m_pageManager.ReleasePage(currentPage);
                overflowPage = m_overflowManager.StoreOverflow(value);
                leafPage = m_pageManager.GetPage(currentPage);
                leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
            }
            
            // Try to insert (with compaction if needed)
            bool canInsert;
            if (needsOverflow)
            {
                canInsert = leafNode.InsertLeafWithCompaction(insertIndex, key, 
                    CreateOverflowRef(overflowPage, value.Length));
            }
            else
            {
                canInsert = leafNode.InsertLeafWithCompaction(insertIndex, key, value);
            }
            
            if (canInsert)
            {
                leafPage.MarkDirty();
                m_pageManager.ReleasePage(currentPage);
                
                m_entryCount++;
                m_entryCountDirty = true;
                return true;
            }
            
            // Need to split - propagate up
            var splitResult = SplitLeaf(leafPage, ref leafNode, insertIndex, key, value, needsOverflow, overflowPage);
            m_pageManager.ReleasePage(currentPage);
            
            // Propagate split up the tree
            return PropagateSplitUp(pathPages, pathChildIndices, pathLength, splitResult);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(pathPages);
            ArrayPool<int>.Shared.Return(pathChildIndices);
        }
    }

    /// <summary>
    /// Inserts or updates a key-value pair.
    /// Returns true if a new key was inserted, false if an existing key was updated.
    /// </summary>
    public bool Upsert(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        ValidateValue(value);
        
        // Rent arrays from pool to avoid allocations
        var pathPages = ArrayPool<uint>.Shared.Rent(MAX_TREE_DEPTH);
        var pathChildIndices = ArrayPool<int>.Shared.Rent(MAX_TREE_DEPTH);
        
        try
        {
            int pathLength = 0;
            uint currentPage = m_rootPageNumber;
            
            // Navigate to leaf, recording path
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
            
            // Check if key exists in leaf
            var leafPage = m_pageManager.GetPage(currentPage);
            var leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
            
            int searchIndex = leafNode.SearchKey(key);
            
            if (searchIndex >= 0)
            {
                // Key exists - update value
                m_pageManager.ReleasePage(currentPage);
                UpdateValue(currentPage, searchIndex, value);
                return false; // Updated, not inserted
            }
            
            // Key doesn't exist - insert
            int insertIndex = ~searchIndex;
            
            // Determine if value needs overflow
            bool needsOverflow = value.Length > m_maxInlineValueSize;
            uint overflowPage = 0;
            
            if (needsOverflow)
            {
                m_pageManager.ReleasePage(currentPage);
                overflowPage = m_overflowManager.StoreOverflow(value);
                leafPage = m_pageManager.GetPage(currentPage);
                leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
            }
            
            // Try to insert (with compaction if needed)
            bool canInsert;
            if (needsOverflow)
            {
                canInsert = leafNode.InsertLeafWithCompaction(insertIndex, key, 
                    CreateOverflowRef(overflowPage, value.Length));
            }
            else
            {
                canInsert = leafNode.InsertLeafWithCompaction(insertIndex, key, value);
            }
            
            if (canInsert)
            {
                leafPage.MarkDirty();
                m_pageManager.ReleasePage(currentPage);
                
                m_entryCount++;
                m_entryCountDirty = true;
                return true;
            }
            
            // Need to split - propagate up
            var splitResult = SplitLeaf(leafPage, ref leafNode, insertIndex, key, value, needsOverflow, overflowPage);
            m_pageManager.ReleasePage(currentPage);
            
            // Propagate split up the tree
            return PropagateSplitUp(pathPages, pathChildIndices, pathLength, splitResult);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(pathPages);
            ArrayPool<int>.Shared.Return(pathChildIndices);
        }
    }

    /// <summary>
    /// Propagates a split up the tree, creating new root if necessary.
    /// </summary>
    private bool PropagateSplitUp(uint[] pathPages, int[] pathChildIndices, int pathLength, SplitResult splitResult)
    {
        byte[] separatorKey = splitResult.SplitKey!;
        uint leftChild = splitResult.LeftPage;
        uint rightChild = splitResult.RightPage;
        
        for (int i = pathLength - 1; i >= 0; i--)
        {
            uint parentPage = pathPages[i];
            int childIndex = pathChildIndices[i];
            
            var parent = m_pageManager.GetPage(parentPage);
            var parentNode = new BTreeNode(parent.Data, PageSize, parentPage);
            
            if (parentNode.CanInsertInternal(separatorKey.Length))
            {
                parentNode.InsertInternal(childIndex, separatorKey, leftChild);
                
                // Update the old child pointer (now shifted) to rightChild
                if (childIndex + 1 < parentNode.KeyCount)
                {
                    parentNode.SetChild(childIndex + 1, rightChild);
                }
                else
                {
                    parentNode.RightmostChild = rightChild;
                }
                
                parent.MarkDirty();
                m_pageManager.ReleasePage(parentPage);
                
                m_entryCount++;
                m_entryCountDirty = true;
                return true;
            }
            
            // Need to split internal node
            var internalSplit = SplitInternal(parent, ref parentNode, childIndex, separatorKey, leftChild, rightChild);
            m_pageManager.ReleasePage(parentPage);
            
            separatorKey = internalSplit.SplitKey!;
            leftChild = internalSplit.LeftPage;
            rightChild = internalSplit.RightPage;
        }
        
        // Reached root - create new root
        m_rootPageNumber = CreateInternalNode(separatorKey, leftChild, rightChild);
        UpdateSchemaRootPage();
        
        m_entryCount++;
        m_entryCountDirty = true;
        return true;
    }

    #endregion

    #region Split Operations

    private SplitResult SplitLeaf(CachedPage page, ref BTreeNode node, 
        int insertPoint, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        bool needsOverflow, uint overflowPage)
    {
        node.CollectLeafEntries(out var keys, out var values);
        
        int totalCount = keys.Length + 1;
        var allKeys = new byte[totalCount][];
        var allValues = new byte[totalCount][];
        
        for (int i = 0; i < insertPoint; i++)
        {
            allKeys[i] = keys[i];
            allValues[i] = values[i];
        }
        
        if (needsOverflow)
        {
            var overflowRef = new byte[BTreeNode.OVERFLOW_REF_SIZE];
            overflowRef[0] = BTreeNode.OVERFLOW_MARKER;
            BinaryPrimitives.WriteUInt32LittleEndian(overflowRef.AsSpan(1), overflowPage);
            BinaryPrimitives.WriteInt32LittleEndian(overflowRef.AsSpan(5), value.Length);
            allValues[insertPoint] = overflowRef;
        }
        else
        {
            allValues[insertPoint] = value.ToArray();
        }
        allKeys[insertPoint] = key.ToArray();
        
        for (int i = insertPoint; i < keys.Length; i++)
        {
            allKeys[i + 1] = keys[i];
            allValues[i + 1] = values[i];
        }
        
        uint rightPageNum = CreateLeafNode();
        var rightPage = m_pageManager.GetPage(rightPageNum);
        var rightNode = new BTreeNode(rightPage.Data, PageSize, rightPageNum);
        
        int splitPoint = totalCount / 2;
        
        uint oldNextLeaf = node.NextLeaf;
        node.Clear();
        
        for (int i = 0; i < splitPoint; i++)
        {
            node.InsertLeaf(i, allKeys[i], allValues[i]);
        }
        
        for (int i = splitPoint; i < totalCount; i++)
        {
            rightNode.InsertLeaf(i - splitPoint, allKeys[i], allValues[i]);
        }
        
        node.NextLeaf = rightPageNum;
        rightNode.PrevLeaf = node.PageNumber;
        rightNode.NextLeaf = oldNextLeaf;
        
        if (oldNextLeaf != 0)
        {
            var nextPage = m_pageManager.GetPage(oldNextLeaf);
            var nextNode = new BTreeNode(nextPage.Data, PageSize, oldNextLeaf);
            nextNode.PrevLeaf = rightPageNum;
            nextPage.MarkDirty();
            m_pageManager.ReleasePage(oldNextLeaf);
        }
        
        page.MarkDirty();
        rightPage.MarkDirty();
        m_pageManager.ReleasePage(rightPageNum);
        
        return new SplitResult
        {
            SplitKey = allKeys[splitPoint],
            LeftPage = node.PageNumber,
            RightPage = rightPageNum
        };
    }

    private SplitResult SplitInternal(CachedPage page, ref BTreeNode node,
        int insertIndex, byte[] separatorKey, uint leftChild, uint rightChild)
    {
        node.CollectInternalEntries(out var keys, out var children);
        
        int totalKeys = keys.Length + 1;
        var allKeys = new byte[totalKeys][];
        var allChildren = new uint[totalKeys + 1];
        
        for (int i = 0; i < insertIndex; i++)
        {
            allKeys[i] = keys[i];
            allChildren[i] = children[i];
        }
        
        allKeys[insertIndex] = separatorKey;
        allChildren[insertIndex] = leftChild;
        allChildren[insertIndex + 1] = rightChild;
        
        for (int i = insertIndex; i < keys.Length; i++)
        {
            allKeys[i + 1] = keys[i];
            allChildren[i + 2] = children[i + 1];
        }
        
        uint rightPageNum = CreateInternalNode();
        var rightPage = m_pageManager.GetPage(rightPageNum);
        var rightNode = new BTreeNode(rightPage.Data, PageSize, rightPageNum);
        
        int splitPoint = totalKeys / 2;
        byte[] middleKey = allKeys[splitPoint];
        
        node.Clear();
        
        for (int i = 0; i < splitPoint; i++)
        {
            node.InsertInternal(i, allKeys[i], allChildren[i]);
        }
        node.RightmostChild = allChildren[splitPoint];
        
        for (int i = splitPoint + 1; i < totalKeys; i++)
        {
            rightNode.InsertInternal(i - splitPoint - 1, allKeys[i], allChildren[i]);
        }
        rightNode.RightmostChild = allChildren[totalKeys];
        
        page.MarkDirty();
        rightPage.MarkDirty();
        m_pageManager.ReleasePage(rightPageNum);
        
        return new SplitResult
        {
            SplitKey = middleKey,
            LeftPage = node.PageNumber,
            RightPage = rightPageNum
        };
    }

    private readonly struct SplitResult
    {
        public byte[]? SplitKey { get; init; }
        public uint LeftPage { get; init; }
        public uint RightPage { get; init; }
    }

    #endregion
}
