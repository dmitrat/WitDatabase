using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Pages;

namespace OutWit.Database.Core.Tree;

/// <summary>
/// B+Tree implementation for WitDB.
/// Provides efficient key-value storage with O(log n) operations.
/// </summary>
/// <remarks>
/// Features:
/// - Zero-copy node access via ref structs
/// - Overflow page support for large values
/// - Persisted entry count (lazy save)
/// - Iterative insert/delete (no recursion)
/// - ArrayPool for path arrays to avoid allocations
/// </remarks>
public sealed class BTree : IDisposable, IAsyncDisposable
{
    #region Constants

    /// <summary>Maximum key size (to ensure at least 2 keys per page).</summary>
    public const int MAX_KEY_SIZE = 1024;
    
    /// <summary>Maximum value size (for validation, larger uses overflow).</summary>
    public const int MAX_VALUE_SIZE = int.MaxValue / 2;
    
    /// <summary>Offset in root page where entry count is stored.</summary>
    private const int ENTRY_COUNT_OFFSET = 12;
    
    /// <summary>Maximum tree depth (enough for billions of entries).</summary>
    private const int MAX_TREE_DEPTH = 32;

    #endregion

    #region Fields

    private readonly PageManager m_pageManager;
    private readonly OverflowPageManager m_overflowManager;
    private readonly int m_maxInlineValueSize;
    
    private uint m_rootPageNumber;
    private long m_entryCount;
    private bool m_entryCountDirty;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new B+Tree using the specified page manager.
    /// </summary>
    public BTree(PageManager pageManager, uint rootPageNumber = 0)
    {
        m_pageManager = pageManager ?? throw new ArgumentNullException(nameof(pageManager));
        
        // Calculate max inline value size - ensure at least 4 entries per leaf
        int availablePerEntry = (m_pageManager.PageSize - BTreeNode.CELL_DIR_OFFSET) / 4;
        int overheadPerEntry = 4 + 50 + 2; // varints + typical key + dir entry
        m_maxInlineValueSize = Math.Max(64, Math.Min(availablePerEntry - overheadPerEntry, m_pageManager.PageSize / 4));
        
        m_overflowManager = new OverflowPageManager(pageManager, m_maxInlineValueSize);

        if (rootPageNumber == 0)
        {
            m_rootPageNumber = CreateLeafNode();
            m_entryCount = 0;
            m_entryCountDirty = true;
            SaveEntryCountIfDirty();
            UpdateSchemaRootPage();
        }
        else
        {
            m_rootPageNumber = rootPageNumber;
            m_entryCount = LoadEntryCount();
            m_entryCountDirty = false;
        }
    }

    #endregion

    #region Search

    /// <summary>
    /// Searches for a key and returns its value, or null if not found.
    /// </summary>
    public byte[]? Search(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        
        var (leafPage, index, found) = FindLeafInfo(key);
        
        if (!found)
            return null;
        
        var page = m_pageManager.GetPage(leafPage);
        try
        {
            var node = new BTreeNode(page.Data, PageSize, leafPage);
            
            if (node.IsOverflowValue(index))
            {
                uint overflowPage = node.GetOverflowPage(index);
                m_pageManager.ReleasePage(leafPage);
                return m_overflowManager.ReadOverflow(overflowPage);
            }
            
            var result = node.GetValue(index).ToArray();
            m_pageManager.ReleasePage(leafPage);
            return result;
        }
        catch
        {
            m_pageManager.ReleasePage(leafPage);
            throw;
        }
    }

    /// <summary>
    /// Checks if a key exists in the tree.
    /// </summary>
    public bool ContainsKey(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        
        var (_, _, found) = FindLeafInfo(key);
        return found;
    }

    /// <summary>
    /// Finds the leaf page info that should contain the key.
    /// </summary>
    private (uint PageNumber, int Index, bool Found) FindLeafInfo(ReadOnlySpan<byte> key)
    {
        uint currentPage = m_rootPageNumber;
        
        while (true)
        {
            var page = m_pageManager.GetPage(currentPage);
            var node = new BTreeNode(page.Data, PageSize, currentPage);
            
            if (node.IsLeaf)
            {
                int index = node.SearchKey(key);
                bool found = index >= 0;
                m_pageManager.ReleasePage(currentPage);
                return (currentPage, found ? index : ~index, found);
            }
            
            int childIndex = node.FindChildIndex(key);
            uint childPage = childIndex < node.KeyCount 
                ? node.GetChild(childIndex) 
                : node.RightmostChild;
            
            m_pageManager.ReleasePage(currentPage);
            currentPage = childPage;
        }
    }

    #endregion

    #region Insert (Iterative)

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
            
            int effectiveValueLength = needsOverflow ? BTreeNode.OVERFLOW_REF_SIZE : value.Length;
            
            // Try to insert
            if (leafNode.CanInsertLeaf(key.Length, effectiveValueLength))
            {
                if (needsOverflow)
                {
                    leafNode.InsertLeafOverflow(insertIndex, key, overflowPage, value.Length);
                }
                else
                {
                    leafNode.InsertLeaf(insertIndex, key, value);
                }
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
                    // Can fit - insert and update child pointers
                    parentNode.InsertInternal(childIndex, separatorKey, leftChild);
                    
                    if (childIndex + 1 <= parentNode.KeyCount - 1)
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
        finally
        {
            ArrayPool<uint>.Shared.Return(pathPages);
            ArrayPool<int>.Shared.Return(pathChildIndices);
        }
    }

    /// <summary>
    /// Inserts or updates a key-value pair.
    /// </summary>
    public bool Upsert(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        ValidateValue(value);
        
        var (leafPage, index, found) = FindLeafInfo(key);
        
        if (found)
        {
            UpdateValue(leafPage, index, value);
            return false;
        }
        
        return Insert(key, value);
    }

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

    #region Update

    private void UpdateValue(uint pageNumber, int index, ReadOnlySpan<byte> newValue)
    {
        // Free old overflow if exists
        {
            var page = m_pageManager.GetPage(pageNumber);
            var node = new BTreeNode(page.Data, PageSize, pageNumber);
            
            if (node.IsOverflowValue(index))
            {
                uint oldOverflow = node.GetOverflowPage(index);
                m_pageManager.ReleasePage(pageNumber);
                m_overflowManager.FreeOverflow(oldOverflow);
            }
            else
            {
                m_pageManager.ReleasePage(pageNumber);
            }
        }
        
        // Store new value
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
            ReadOnlySpan<byte> valueToStore = needsOverflow ? overflowRef : newValue;
            
            if (!updateNode.UpdateValue(index, valueToStore))
            {
                var key = updateNode.GetKey(index).ToArray();
                updateNode.RemoveAt(index);
                
                if (needsOverflow)
                {
                    updateNode.InsertLeafOverflow(index, key, newOverflowPage, newValue.Length);
                }
                else
                {
                    updateNode.InsertLeaf(index, key, newValue);
                }
            }
            
            updatePage.MarkDirty();
        }
        finally
        {
            m_pageManager.ReleasePage(pageNumber);
        }
    }

    #endregion

    #region Delete

    /// <summary>
    /// Deletes a key from the tree.
    /// </summary>
    public bool Delete(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        
        var (leafPage, index, found) = FindLeafInfo(key);
        
        if (!found)
            return false;
        
        var page = m_pageManager.GetPage(leafPage);
        var node = new BTreeNode(page.Data, PageSize, leafPage);
        
        try
        {
            // Free overflow if exists
            if (node.IsOverflowValue(index))
            {
                uint overflowPage = node.GetOverflowPage(index);
                m_pageManager.ReleasePage(leafPage);
                m_overflowManager.FreeOverflow(overflowPage);
                
                page = m_pageManager.GetPage(leafPage);
                node = new BTreeNode(page.Data, PageSize, leafPage);
            }
            
            node.RemoveAt(index);
            page.MarkDirty();
            
            m_entryCount--;
            m_entryCountDirty = true;
            
            return true;
        }
        finally
        {
            m_pageManager.ReleasePage(leafPage);
        }
    }

    #endregion

    #region Range Scan

    /// <summary>
    /// Returns all key-value pairs in order.
    /// </summary>
    public IEnumerable<(byte[] Key, byte[] Value)> GetAll()
    {
        return GetRange(null, null);
    }

    /// <summary>
    /// Returns key-value pairs in the specified range.
    /// </summary>
    /// <param name="minKey">Start of range (inclusive), or null for beginning.</param>
    /// <param name="maxKey">End of range (exclusive), or null for end.</param>
    public IEnumerable<(byte[] Key, byte[] Value)> GetRange(byte[]? minKey, byte[]? maxKey)
    {
        ThrowIfDisposed();
        
        uint currentPage;
        int startIndex;
        
        if (minKey == null)
        {
            currentPage = FindLeftmostLeaf();
            startIndex = 0;
        }
        else
        {
            var (leafPage, index, _) = FindLeafInfo(minKey);
            currentPage = leafPage;
            startIndex = index;
        }
        
        while (currentPage != 0)
        {
            // Collect results from current page
            var pageResults = CollectPageEntries(currentPage, startIndex, maxKey, exclusive: true, out uint nextLeaf, out bool done);
            
            foreach (var item in pageResults)
                yield return item;
            
            if (done)
                yield break;
            
            currentPage = nextLeaf;
            startIndex = 0;
        }
    }

    /// <summary>
    /// Returns key-value pairs in the specified range (inclusive end).
    /// </summary>
    public IEnumerable<(byte[] Key, byte[] Value)> GetRangeInclusive(byte[]? minKey, byte[]? maxKey)
    {
        ThrowIfDisposed();
        
        uint currentPage;
        int startIndex;
        
        if (minKey == null)
        {
            currentPage = FindLeftmostLeaf();
            startIndex = 0;
        }
        else
        {
            var (leafPage, index, _) = FindLeafInfo(minKey);
            currentPage = leafPage;
            startIndex = index;
        }
        
        while (currentPage != 0)
        {
            // Collect results from current page
            var pageResults = CollectPageEntries(currentPage, startIndex, maxKey, exclusive: false, out uint nextLeaf, out bool done);
            
            foreach (var item in pageResults)
                yield return item;
            
            if (done)
                yield break;
            
            currentPage = nextLeaf;
            startIndex = 0;
        }
    }

    /// <summary>
    /// Collects entries from a single leaf page without crossing yield boundary.
    /// </summary>
    private List<(byte[] Key, byte[] Value)> CollectPageEntries(
        uint pageNumber, int startIndex, byte[]? maxKey, bool exclusive,
        out uint nextLeaf, out bool reachedEnd)
    {
        var results = new List<(byte[] Key, byte[] Value)>();
        reachedEnd = false;
        
        var page = m_pageManager.GetPage(pageNumber);
        var node = new BTreeNode(page.Data, PageSize, pageNumber);
        nextLeaf = node.NextLeaf;
        int keyCount = node.KeyCount;
        
        for (int i = startIndex; i < keyCount; i++)
        {
            var keyBytes = node.GetKey(i).ToArray();
            
            // Check end boundary
            if (maxKey != null)
            {
                int cmp = keyBytes.AsSpan().SequenceCompareTo(maxKey);
                if (exclusive ? cmp >= 0 : cmp > 0)
                {
                    reachedEnd = true;
                    break;
                }
            }
            
            byte[] valueBytes;
            if (node.IsOverflowValue(i))
            {
                uint overflowPage = node.GetOverflowPage(i);
                m_pageManager.ReleasePage(pageNumber);
                valueBytes = m_overflowManager.ReadOverflow(overflowPage);
                page = m_pageManager.GetPage(pageNumber);
                node = new BTreeNode(page.Data, PageSize, pageNumber);
            }
            else
            {
                valueBytes = node.GetValue(i).ToArray();
            }
            
            results.Add((keyBytes, valueBytes));
        }
        
        m_pageManager.ReleasePage(pageNumber);
        return results;
    }

    /// <summary>
    /// Returns the number of entries in the tree.
    /// </summary>
    public long Count()
    {
        ThrowIfDisposed();
        return m_entryCount;
    }

    private uint FindLeftmostLeaf()
    {
        uint currentPage = m_rootPageNumber;
        
        while (true)
        {
            var page = m_pageManager.GetPage(currentPage);
            var node = new BTreeNode(page.Data, PageSize, currentPage);
            
            if (node.IsLeaf)
            {
                m_pageManager.ReleasePage(currentPage);
                return currentPage;
            }
            
            uint childPage = node.GetChild(0);
            m_pageManager.ReleasePage(currentPage);
            currentPage = childPage;
        }
    }

    #endregion

    #region Count Persistence

    private void SaveEntryCountIfDirty()
    {
        if (!m_entryCountDirty)
            return;
        
        var page = m_pageManager.GetPage(m_rootPageNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(page.Data[ENTRY_COUNT_OFFSET..], (uint)m_entryCount);
        page.MarkDirty();
        m_pageManager.ReleasePage(m_rootPageNumber);
        m_entryCountDirty = false;
    }

    private long LoadEntryCount()
    {
        var page = m_pageManager.GetPage(m_rootPageNumber);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(page.ReadOnlyData[ENTRY_COUNT_OFFSET..]);
        m_pageManager.ReleasePage(m_rootPageNumber);
        return count;
    }

    #endregion

    #region Node Management

    private uint CreateLeafNode()
    {
        var (pageNumber, page) = m_pageManager.AllocatePage(PageType.Leaf);
        BTreeNode.Initialize(page.Data, PageSize, isLeaf: true, pageNumber);
        page.MarkDirty();
        m_pageManager.ReleasePage(pageNumber);
        return pageNumber;
    }

    private uint CreateInternalNode()
    {
        var (pageNumber, page) = m_pageManager.AllocatePage(PageType.Internal);
        BTreeNode.Initialize(page.Data, PageSize, isLeaf: false, pageNumber);
        page.MarkDirty();
        m_pageManager.ReleasePage(pageNumber);
        return pageNumber;
    }

    private uint CreateInternalNode(byte[] key, uint leftChild, uint rightChild)
    {
        var (pageNumber, page) = m_pageManager.AllocatePage(PageType.Internal);
        BTreeNode.Initialize(page.Data, PageSize, isLeaf: false, pageNumber);
        
        var node = new BTreeNode(page.Data, PageSize, pageNumber);
        node.InsertInternal(0, key, leftChild);
        node.RightmostChild = rightChild;
        
        page.MarkDirty();
        m_pageManager.ReleasePage(pageNumber);
        return pageNumber;
    }

    private void UpdateSchemaRootPage()
    {
        m_pageManager.SetSchemaRootPage(m_rootPageNumber);
    }

    #endregion

    #region Validation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
            throw new ArgumentException("Key cannot be empty", nameof(key));
        if (key.Length > MAX_KEY_SIZE)
            throw new ArgumentException($"Key too large: {key.Length} > {MAX_KEY_SIZE}", nameof(key));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateValue(ReadOnlySpan<byte> value)
    {
        if (value.Length > MAX_VALUE_SIZE)
            throw new ArgumentException($"Value too large: {value.Length} > {MAX_VALUE_SIZE}", nameof(value));
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        if (!m_disposed)
        {
            SaveEntryCountIfDirty();
            m_pageManager.Flush();
            m_overflowManager.Dispose();
            m_disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!m_disposed)
        {
            SaveEntryCountIfDirty();
            await m_pageManager.FlushAsync().ConfigureAwait(false);
            m_overflowManager.Dispose();
            m_disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region Properties

    /// <summary>Gets the root page number of this tree.</summary>
    public uint RootPageNumber => m_rootPageNumber;

    /// <summary>Gets the page size used by this tree.</summary>
    public int PageSize => m_pageManager.PageSize;

    /// <summary>Gets the maximum inline value size.</summary>
    public int MaxInlineValueSize => m_maxInlineValueSize;

    #endregion
}
