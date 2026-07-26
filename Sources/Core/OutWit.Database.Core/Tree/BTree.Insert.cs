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
                overflowPage = m_pageManagerOverflowManager.StoreOverflow(value);
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
                overflowPage = m_pageManagerOverflowManager.StoreOverflow(value);
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
    /// Inserts or updates a key-value pair asynchronously.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a new key was inserted, false if an existing key was updated.</returns>
    public async ValueTask<bool> UpsertAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(key.Span);
        ValidateValue(value.Span);
        
        // Use regular arrays for async (can't use stackalloc/ArrayPool across awaits easily)
        var pathPages = new uint[MAX_TREE_DEPTH];
        var pathChildIndices = new int[MAX_TREE_DEPTH];
        
        int pathLength = 0;
        uint currentPage = m_rootPageNumber;
        
        // Navigate to leaf, recording path
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var page = await m_pageManager.GetPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
            var node = new BTreeNode(page.Data, PageSize, currentPage);
            
            if (node.IsLeaf)
            {
                m_pageManager.ReleasePage(currentPage);
                break;
            }
            
            int childIndex = node.FindChildIndex(key.Span);
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
        var leafPage = await m_pageManager.GetPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
        var leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
        
        int searchIndex = leafNode.SearchKey(key.Span);
        
        if (searchIndex >= 0)
        {
            // Key exists - update value
            m_pageManager.ReleasePage(currentPage);
            await UpdateValueAsync(currentPage, searchIndex, value, cancellationToken).ConfigureAwait(false);
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
            overflowPage = await m_pageManagerOverflowManager.StoreOverflowAsync(value, cancellationToken).ConfigureAwait(false);
            leafPage = await m_pageManager.GetPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
            leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
        }
        
        // Try to insert (with compaction if needed)
        bool canInsert;
        if (needsOverflow)
        {
            canInsert = leafNode.InsertLeafWithCompaction(insertIndex, key.Span, 
                CreateOverflowRef(overflowPage, value.Length));
        }
        else
        {
            canInsert = leafNode.InsertLeafWithCompaction(insertIndex, key.Span, value.Span);
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
        var splitResult = await SplitLeafAsync(leafPage, currentPage, insertIndex, key, value, needsOverflow, overflowPage, cancellationToken).ConfigureAwait(false);
        m_pageManager.ReleasePage(currentPage);
        
        // Propagate split up the tree
        return await PropagateSplitUpAsync(pathPages, pathChildIndices, pathLength, splitResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a key-value pair asynchronously.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if inserted, false if key already exists.</returns>
    public async ValueTask<bool> InsertAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(key.Span);
        ValidateValue(value.Span);
        
        var pathPages = new uint[MAX_TREE_DEPTH];
        var pathChildIndices = new int[MAX_TREE_DEPTH];
        
        int pathLength = 0;
        uint currentPage = m_rootPageNumber;
        
        // Navigate to leaf, recording path
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var page = await m_pageManager.GetPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
            var node = new BTreeNode(page.Data, PageSize, currentPage);
            
            if (node.IsLeaf)
            {
                m_pageManager.ReleasePage(currentPage);
                break;
            }
            
            int childIndex = node.FindChildIndex(key.Span);
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
        var leafPage = await m_pageManager.GetPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
        var leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
        
        int insertIndex = leafNode.SearchKey(key.Span);
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
            overflowPage = await m_pageManagerOverflowManager.StoreOverflowAsync(value, cancellationToken).ConfigureAwait(false);
            leafPage = await m_pageManager.GetPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
            leafNode = new BTreeNode(leafPage.Data, PageSize, currentPage);
        }
        
        // Try to insert (with compaction if needed)
        bool canInsert;
        if (needsOverflow)
        {
            canInsert = leafNode.InsertLeafWithCompaction(insertIndex, key.Span, 
                CreateOverflowRef(overflowPage, value.Length));
        }
        else
        {
            canInsert = leafNode.InsertLeafWithCompaction(insertIndex, key.Span, value.Span);
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
        var splitResult = await SplitLeafAsync(leafPage, currentPage, insertIndex, key, value, needsOverflow, overflowPage, cancellationToken).ConfigureAwait(false);
        m_pageManager.ReleasePage(currentPage);
        
        // Propagate split up the tree
        return await PropagateSplitUpAsync(pathPages, pathChildIndices, pathLength, splitResult, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Propagates a split up the tree asynchronously.
    /// </summary>
    private async ValueTask<bool> PropagateSplitUpAsync(uint[] pathPages, int[] pathChildIndices, int pathLength, SplitResult splitResult, CancellationToken cancellationToken)
    {
        byte[] separatorKey = splitResult.SplitKey!;
        uint leftChild = splitResult.LeftPage;
        uint rightChild = splitResult.RightPage;
        
        for (int i = pathLength - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            uint parentPage = pathPages[i];
            int childIndex = pathChildIndices[i];
            
            var parent = await m_pageManager.GetPageAsync(parentPage, cancellationToken).ConfigureAwait(false);
            var parentNode = new BTreeNode(parent.Data, PageSize, parentPage);
            
            if (parentNode.CanInsertInternal(separatorKey.Length))
            {
                parentNode.InsertInternal(childIndex, separatorKey, leftChild);
                
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
            
            // Need to split internal node - pass pageNumber instead of node
            var internalSplit = await SplitInternalAsync(parent, parentPage, childIndex, separatorKey, leftChild, rightChild, cancellationToken).ConfigureAwait(false);
            m_pageManager.ReleasePage(parentPage);
            
            separatorKey = internalSplit.SplitKey!;
            leftChild = internalSplit.LeftPage;
            rightChild = internalSplit.RightPage;
        }
        
        // Reached root - create new root
        m_rootPageNumber = await CreateInternalNodeAsync(separatorKey, leftChild, rightChild, cancellationToken).ConfigureAwait(false);
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
        
        // Split by BYTES, not by count. A count-based midpoint assumes every entry is the same size;
        // with mixed sizes one half could exceed the page, InsertLeaf below would return false, and
        // the entry was silently dropped while Insert reported success.
        int splitPoint = CalculateLeafSplitPoint(allKeys, allValues, totalCount);

        uint oldNextLeaf = node.NextLeaf;
        node.Clear();

        for (int i = 0; i < splitPoint; i++)
        {
            if (!node.InsertLeaf(i, allKeys[i], allValues[i]))
                ThrowSplitOverflow(i, splitPoint, totalCount, left: true);
        }

        for (int i = splitPoint; i < totalCount; i++)
        {
            if (!rightNode.InsertLeaf(i - splitPoint, allKeys[i], allValues[i]))
                ThrowSplitOverflow(i, splitPoint, totalCount, left: false);
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

    private async ValueTask<SplitResult> SplitLeafAsync(CachedPage page, uint pageNumber,
        int insertPoint, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value,
        bool needsOverflow, uint overflowPage, CancellationToken cancellationToken)
    {
        // Create BTreeNode locally (ref struct can't cross await)
        var node = new BTreeNode(page.Data, PageSize, pageNumber);
        
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
        
        uint rightPageNum = await CreateLeafNodeAsync(cancellationToken).ConfigureAwait(false);
        var rightPage = await m_pageManager.GetPageAsync(rightPageNum, cancellationToken).ConfigureAwait(false);
        var rightNode = new BTreeNode(rightPage.Data, PageSize, rightPageNum);
        
        // Size-aware, and checked - see the synchronous SplitLeaf for why.
        int splitPoint = CalculateLeafSplitPoint(allKeys, allValues, totalCount);

        // Re-create node after await (page content unchanged)
        node = new BTreeNode(page.Data, PageSize, pageNumber);
        uint oldNextLeaf = node.NextLeaf;
        node.Clear();

        for (int i = 0; i < splitPoint; i++)
        {
            if (!node.InsertLeaf(i, allKeys[i], allValues[i]))
                ThrowSplitOverflow(i, splitPoint, totalCount, left: true);
        }

        for (int i = splitPoint; i < totalCount; i++)
        {
            if (!rightNode.InsertLeaf(i - splitPoint, allKeys[i], allValues[i]))
                ThrowSplitOverflow(i, splitPoint, totalCount, left: false);
        }
        
        node.NextLeaf = rightPageNum;
        rightNode.PrevLeaf = pageNumber;
        rightNode.NextLeaf = oldNextLeaf;
        
        if (oldNextLeaf != 0)
        {
            var nextPage = await m_pageManager.GetPageAsync(oldNextLeaf, cancellationToken).ConfigureAwait(false);
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
            LeftPage = pageNumber,
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

    private async ValueTask<SplitResult> SplitInternalAsync(CachedPage page, uint pageNumber,
        int insertIndex, byte[] separatorKey, uint leftChild, uint rightChild, CancellationToken cancellationToken)
    {
        // Create BTreeNode locally (ref struct can't cross await)
        var node = new BTreeNode(page.Data, PageSize, pageNumber);
        
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
        
        uint rightPageNum = await CreateInternalNodeAsync(cancellationToken).ConfigureAwait(false);
        var rightPage = await m_pageManager.GetPageAsync(rightPageNum, cancellationToken).ConfigureAwait(false);
        var rightNode = new BTreeNode(rightPage.Data, PageSize, rightPageNum);
        
        int splitPoint = totalKeys / 2;
        byte[] middleKey = allKeys[splitPoint];
        
        // Re-create node after await
        node = new BTreeNode(page.Data, PageSize, pageNumber);
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

    #region Async Node Creation

    private async ValueTask<uint> CreateLeafNodeAsync(CancellationToken cancellationToken)
    {
        var (pageNumber, page) = await m_pageManager.AllocatePageAsync(Pages.PageType.Leaf, cancellationToken).ConfigureAwait(false);
        BTreeNode.Initialize(page.Data, PageSize, isLeaf: true, pageNumber);
        page.MarkDirty();
        m_pageManager.ReleasePage(pageNumber);
        return pageNumber;
    }

    private async ValueTask<uint> CreateInternalNodeAsync(CancellationToken cancellationToken)
    {
        var (pageNumber, page) = await m_pageManager.AllocatePageAsync(Pages.PageType.Internal, cancellationToken).ConfigureAwait(false);
        BTreeNode.Initialize(page.Data, PageSize, isLeaf: false, pageNumber);
        page.MarkDirty();
        m_pageManager.ReleasePage(pageNumber);
        return pageNumber;
    }

    private async ValueTask<uint> CreateInternalNodeAsync(byte[] key, uint leftChild, uint rightChild, CancellationToken cancellationToken)
    {
        var (pageNumber, page) = await m_pageManager.AllocatePageAsync(Pages.PageType.Internal, cancellationToken).ConfigureAwait(false);
        BTreeNode.Initialize(page.Data, PageSize, isLeaf: false, pageNumber);
        
        var node = new BTreeNode(page.Data, PageSize, pageNumber);
        node.InsertInternal(0, key, leftChild);
        node.RightmostChild = rightChild;
        
        page.MarkDirty();
        m_pageManager.ReleasePage(pageNumber);
        return pageNumber;
    }

    #endregion

    #region Split Point

    /// <summary>
    /// Chooses the split index that puts roughly half the BYTES on each side.
    /// </summary>
    /// <remarks>
    /// The previous <c>totalCount / 2</c> assumed uniform entry sizes. With mixed sizes - a handful
    /// of large values among small ones - the heavier half could exceed the page, and since the
    /// replay loops discarded <c>InsertLeaf</c>'s result the overflowing entries were dropped while
    /// <c>Insert</c> returned true. Both sides are guaranteed at least one entry, so the split always
    /// makes progress.
    /// </remarks>
    private static int CalculateLeafSplitPoint(byte[][] keys, byte[][] values, int totalCount)
    {
        if (totalCount <= 2)
            return 1;

        var sizes = new int[totalCount];
        long totalSize = 0;

        for (int i = 0; i < totalCount; i++)
        {
            sizes[i] = BTreeNode.CalculateLeafEntrySize(keys[i].Length, values[i].Length)
                       + BTreeNode.CellDirectoryEntrySize;
            totalSize += sizes[i];
        }

        long half = totalSize / 2;
        long accumulated = 0;

        for (int i = 0; i < totalCount - 1; i++)
        {
            accumulated += sizes[i];
            if (accumulated >= half)
                return i + 1;
        }

        return totalCount - 1;
    }

    /// <summary>
    /// A replayed entry did not fit the page it was assigned to.
    /// </summary>
    /// <remarks>
    /// Unreachable once the split point is size-aware and oversized values have gone to overflow
    /// pages. If it ever fires, losing the entry silently - which is what discarding InsertLeaf's
    /// result did - is far worse than failing here.
    /// </remarks>
    private static void ThrowSplitOverflow(int index, int splitPoint, int totalCount, bool left)
    {
        throw new InvalidOperationException(
            $"B+Tree leaf split could not place entry {index} of {totalCount} into the " +
            $"{(left ? "left" : "right")} page (split point {splitPoint}). The tree has not been " +
            $"modified beyond this node; please report this with the key sizes involved.");
    }

    #endregion
}
