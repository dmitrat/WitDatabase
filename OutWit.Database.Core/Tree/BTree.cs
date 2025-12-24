using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using OutWit.Database.Core.Cache;
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
/// 
/// File organization:
/// - BTree.cs: Core structure (constants, fields, constructor, properties, dispose)
/// - BTree.Search.cs: Search operations
/// - BTree.Insert.cs: Insert and split operations
/// - BTree.Update.cs: Update operations
/// - BTree.Delete.cs: Delete operations
/// - BTree.RangeScan.cs: Range scan operations
/// </remarks>
public sealed partial class BTree : IDisposable, IAsyncDisposable
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
    private readonly PageManagerOverflow m_pageManagerOverflowManager;
    private readonly int m_maxInlineValueSize;
    
    private uint m_rootPageNumber;
    private long m_entryCount;
    private bool m_entryCountDirty;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new B+Tree using the specified page manager.
    /// For async environments (e.g., WASM), use <see cref="CreateAsync"/> instead.
    /// </summary>
    public BTree(PageManager pageManager, uint rootPageNumber = 0)
    {
        m_pageManager = pageManager ?? throw new ArgumentNullException(nameof(pageManager));
        
        // Calculate max inline value size - ensure at least 4 entries per leaf
        int availablePerEntry = (m_pageManager.PageSize - BTreeNode.CELL_DIR_OFFSET) / 4;
        int overheadPerEntry = 4 + 50 + 2; // varints + typical key + dir entry
        m_maxInlineValueSize = Math.Max(64, Math.Min(availablePerEntry - overheadPerEntry, m_pageManager.PageSize / 4));
        
        m_pageManagerOverflowManager = new PageManagerOverflow(pageManager, m_maxInlineValueSize);

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

    /// <summary>
    /// Private constructor for async factory pattern.
    /// </summary>
    private BTree(PageManager pageManager, uint rootPageNumber, long entryCount, bool entryCountDirty, int maxInlineValueSize)
    {
        m_pageManager = pageManager;
        m_rootPageNumber = rootPageNumber;
        m_entryCount = entryCount;
        m_entryCountDirty = entryCountDirty;
        m_maxInlineValueSize = maxInlineValueSize;
        m_pageManagerOverflowManager = new PageManagerOverflow(pageManager, maxInlineValueSize);
    }

    #endregion

    #region Static Factory Methods

    /// <summary>
    /// Creates a new B+Tree asynchronously.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="pageManager">The page manager to use.</param>
    /// <param name="rootPageNumber">Root page number, or 0 to create new tree.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An initialized BTree.</returns>
    public static async ValueTask<BTree> CreateAsync(
        PageManager pageManager, 
        uint rootPageNumber = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageManager);
        cancellationToken.ThrowIfCancellationRequested();
        
        // Calculate max inline value size - ensure at least 4 entries per leaf
        int availablePerEntry = (pageManager.PageSize - BTreeNode.CELL_DIR_OFFSET) / 4;
        int overheadPerEntry = 4 + 50 + 2; // varints + typical key + dir entry
        int maxInlineValueSize = Math.Max(64, Math.Min(availablePerEntry - overheadPerEntry, pageManager.PageSize / 4));

        if (rootPageNumber == 0)
        {
            // Create new tree with async page allocation
            uint newRootPageNumber = await CreateLeafNodeAsync(pageManager, cancellationToken).ConfigureAwait(false);
            
            var tree = new BTree(pageManager, newRootPageNumber, entryCount: 0, entryCountDirty: true, maxInlineValueSize);
            
            await tree.SaveEntryCountIfDirtyAsync(cancellationToken).ConfigureAwait(false);
            tree.UpdateSchemaRootPage();
            
            return tree;
        }
        else
        {
            // Try to load existing tree - validate the page first
            bool isValidRoot = await IsValidRootPageAsync(pageManager, rootPageNumber, cancellationToken).ConfigureAwait(false);
            
            if (!isValidRoot)
            {
                // Root page is invalid (empty, corrupted, or wrong type) - create new tree
                uint newRootPageNumber = await CreateLeafNodeAsync(pageManager, cancellationToken).ConfigureAwait(false);
                
                var newTree = new BTree(pageManager, newRootPageNumber, entryCount: 0, entryCountDirty: true, maxInlineValueSize);
                
                await newTree.SaveEntryCountIfDirtyAsync(cancellationToken).ConfigureAwait(false);
                newTree.UpdateSchemaRootPage();
                
                return newTree;
            }
            
            // Load existing tree with async page access
            long entryCount = await LoadEntryCountStaticAsync(pageManager, rootPageNumber, cancellationToken).ConfigureAwait(false);
            return new BTree(pageManager, rootPageNumber, entryCount, entryCountDirty: false, maxInlineValueSize);
        }
    }

    /// <summary>
    /// Checks if a page is a valid BTree root page.
    /// </summary>
    private static async ValueTask<bool> IsValidRootPageAsync(PageManager pageManager, uint pageNumber, CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageManager.GetPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
            try
            {
                // Check if page has valid BTree structure
                // A valid root page should have a valid page type (Leaf or Internal)
                var data = page.ReadOnlyData;
                
                // Check if data is all zeros (uninitialized page)
                bool allZeros = true;
                for (int i = 0; i < Math.Min(16, data.Length); i++)
                {
                    if (data[i] != 0)
                    {
                        allZeros = false;
                        break;
                    }
                }
                
                if (allZeros)
                    return false;
                
                // Check page type from header
                var pageType = (PageType)data[0];
                return pageType == PageType.Leaf || pageType == PageType.Internal;
            }
            finally
            {
                pageManager.ReleasePage(pageNumber);
            }
        }
        catch
        {
            return false;
        }
    }

    private static async ValueTask<uint> CreateLeafNodeAsync(PageManager pageManager, CancellationToken cancellationToken)
    {
        var (pageNumber, page) = await pageManager.AllocatePageAsync(PageType.Leaf, cancellationToken).ConfigureAwait(false);
        BTreeNode.Initialize(page.Data, pageManager.PageSize, isLeaf: true, pageNumber);
        page.MarkDirty();
        pageManager.ReleasePage(pageNumber);
        return pageNumber;
    }

    private static long LoadEntryCountStatic(PageManager pageManager, uint rootPageNumber)
    {
        var page = pageManager.GetPage(rootPageNumber);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(page.ReadOnlyData[ENTRY_COUNT_OFFSET..]);
        pageManager.ReleasePage(rootPageNumber);
        return count;
    }

    private static async ValueTask<long> LoadEntryCountStaticAsync(PageManager pageManager, uint rootPageNumber, CancellationToken cancellationToken)
    {
        var page = await pageManager.GetPageAsync(rootPageNumber, cancellationToken).ConfigureAwait(false);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(page.ReadOnlyData[ENTRY_COUNT_OFFSET..]);
        pageManager.ReleasePage(rootPageNumber);
        return count;
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

    #region Count

    /// <summary>
    /// Returns the number of entries in the tree.
    /// </summary>
    public long Count()
    {
        ThrowIfDisposed();
        return m_entryCount;
    }

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

    private async ValueTask SaveEntryCountIfDirtyAsync(CancellationToken cancellationToken = default)
    {
        if (!m_entryCountDirty)
            return;
        
        var page = await m_pageManager.GetPageAsync(m_rootPageNumber, cancellationToken).ConfigureAwait(false);
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

    #region Helpers

    /// <summary>
    /// Creates an overflow reference byte array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] CreateOverflowRef(uint overflowPage, int totalLength)
    {
        var overflowRef = new byte[BTreeNode.OVERFLOW_REF_SIZE];
        overflowRef[0] = BTreeNode.OVERFLOW_MARKER;
        BinaryPrimitives.WriteUInt32LittleEndian(overflowRef.AsSpan(1), overflowPage);
        BinaryPrimitives.WriteInt32LittleEndian(overflowRef.AsSpan(5), totalLength);
        return overflowRef;
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
            m_pageManagerOverflowManager.Dispose();
            m_disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!m_disposed)
        {
            // Use async version to avoid deadlock in WASM
            await SaveEntryCountIfDirtyAsync().ConfigureAwait(false);
            await m_pageManager.FlushAsync().ConfigureAwait(false);
            m_pageManagerOverflowManager.Dispose();
            m_disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion
}
