namespace OutWit.Database.Core.Tree;

public sealed partial class BTree
{
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

    /// <summary>
    /// Finds the leftmost leaf page in the tree.
    /// </summary>
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
}
