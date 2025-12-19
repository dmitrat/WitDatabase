namespace OutWit.Database.Core.Tree;

public sealed partial class BTree
{
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
}
