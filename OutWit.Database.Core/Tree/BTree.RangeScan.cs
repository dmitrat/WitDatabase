namespace OutWit.Database.Core.Tree;

public sealed partial class BTree
{
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

    #endregion
}
