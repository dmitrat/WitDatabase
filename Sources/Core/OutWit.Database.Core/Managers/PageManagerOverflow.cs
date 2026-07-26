using System.Buffers;
using System.Buffers.Binary;
using OutWit.Database.Core.Pages;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Managers
{
    /// <summary>
    /// Manages overflow pages for values larger than a single page.
    /// Overflow pages are chained together to store arbitrarily large BLOBs.
    /// </summary>
    public sealed class PageManagerOverflow : IDisposable
    {
        #region Classes

        /// <summary>
        /// Information about an overflow page chain.
        /// </summary>
        public readonly record struct OverflowInfo(uint FirstPage, int TotalLength, int PageCount);

        #endregion

        #region Constants

        private const int OVERFLOW_HEADER_SIZE = 16;
        private const byte OVERFLOW_PAGE_TYPE = 0x03;

        #endregion

        #region Fields

        private readonly PageManager m_pageManager;
        private readonly int m_pageSize;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an overflow page manager.
        /// </summary>
        public PageManagerOverflow(PageManager pageManager, int? maxInlineSize = null)
        {
            m_pageManager = pageManager ?? throw new ArgumentNullException(nameof(pageManager));
            m_pageSize = pageManager.PageSize;
            MaxInlineSize = maxInlineSize ?? m_pageSize / 4;
        }

        #endregion

        #region Sync Operations

        /// <summary>
        /// Checks if the value needs overflow pages.
        /// </summary>
        public bool NeedsOverflow(int valueLength) => valueLength > MaxInlineSize;

        /// <summary>
        /// Stores a large value in overflow pages.
        /// </summary>
        public uint StoreOverflow(ReadOnlySpan<byte> data)
        {
            ThrowIfDisposed();
        
            if (data.Length <= MaxInlineSize)
                throw new ArgumentException($"Value is small enough for inline storage ({data.Length} <= {MaxInlineSize})", nameof(data));

            var dataPerPage = DataSizePerPage;
            var pageCount = (data.Length + dataPerPage - 1) / dataPerPage;
        
            // Allocate all pages at once using batch API
            uint[] pageNumbers;
            try
            {
                pageNumbers = m_pageManager.AllocatePages(PageType.Overflow, pageCount);
            }
            catch
            {
                throw;
            }

            // Write data to all pages in single pass
            int offset = 0;
            for (int i = 0; i < pageCount; i++)
            {
                uint pageNum = pageNumbers[i];
                uint nextPage = i < pageCount - 1 ? pageNumbers[i + 1] : 0;
                
                var cachedPage = m_pageManager.GetPage(pageNum);
                try
                {
                    int dataLen = Math.Min(dataPerPage, data.Length - offset);
            
                    var pageData = cachedPage.Data;
            
                    pageData[0] = OVERFLOW_PAGE_TYPE;
                    pageData[1] = 0;
                    BinaryPrimitives.WriteUInt32LittleEndian(pageData[2..], nextPage);
                    BinaryPrimitives.WriteInt32LittleEndian(pageData[6..], dataLen);
                    BinaryPrimitives.WriteInt32LittleEndian(pageData[10..], i == 0 ? data.Length : 0);
            
                    data.Slice(offset, dataLen).CopyTo(pageData[OVERFLOW_HEADER_SIZE..]);
            
                    cachedPage.MarkDirty();
                    offset += dataLen;
                }
                finally
                {
                    m_pageManager.ReleasePage(pageNum);
                }
            }
        
            return pageNumbers[0];
        }

        /// <summary>
        /// Reads a value from overflow pages into an existing buffer.
        /// </summary>
        /// <param name="firstPage">First overflow page number.</param>
        /// <param name="destination">Destination buffer (must be large enough).</param>
        /// <returns>Number of bytes read.</returns>
        public int ReadOverflow(uint firstPage, Span<byte> destination)
        {
            ThrowIfDisposed();
        
            var firstCachedPage = m_pageManager.GetPage(firstPage);
            int totalLength;
            uint nextPage;
            int firstDataLen;
            
            try
            {
                var pageData = firstCachedPage.ReadOnlyData;
            
                if (pageData[0] != OVERFLOW_PAGE_TYPE)
                    throw new InvalidDataException($"Page {firstPage} is not an overflow page");
            
                totalLength = BinaryPrimitives.ReadInt32LittleEndian(pageData[10..]);
                
                if (destination.Length < totalLength)
                    throw new ArgumentException($"Destination buffer too small: {destination.Length} < {totalLength}", nameof(destination));
            
                nextPage = BinaryPrimitives.ReadUInt32LittleEndian(pageData[2..]);
                firstDataLen = BinaryPrimitives.ReadInt32LittleEndian(pageData[6..]);
                
                pageData.Slice(OVERFLOW_HEADER_SIZE, firstDataLen).CopyTo(destination);
            }
            finally
            {
                m_pageManager.ReleasePage(firstPage);
            }
            
            int offset = firstDataLen;
            uint currentPage = nextPage;
            
            while (currentPage != 0 && offset < totalLength)
            {
                var cachedPage = m_pageManager.GetPage(currentPage);
                try
                {
                    var pageData = cachedPage.ReadOnlyData;
                
                    nextPage = BinaryPrimitives.ReadUInt32LittleEndian(pageData[2..]);
                    int dataLen = BinaryPrimitives.ReadInt32LittleEndian(pageData[6..]);
                
                    pageData.Slice(OVERFLOW_HEADER_SIZE, dataLen).CopyTo(destination[offset..]);
                
                    offset += dataLen;
                }
                finally
                {
                    m_pageManager.ReleasePage(currentPage);
                }
                
                currentPage = nextPage;
            }
        
            return totalLength;
        }

        /// <summary>
        /// Reads a value from overflow pages.
        /// </summary>
        public byte[] ReadOverflow(uint firstPage)
        {
            ThrowIfDisposed();
        
            var info = GetOverflowInfo(firstPage);
            var result = new byte[info.TotalLength];
            ReadOverflow(firstPage, result);
            return result;
        }

        /// <summary>
        /// Frees all pages in an overflow chain.
        /// </summary>
        public void FreeOverflow(uint firstPage)
        {
            ThrowIfDisposed();
        
            // Collect all page numbers using stackalloc for small chains
            Span<uint> stackBuffer = stackalloc uint[16];
            int count = 0;
            uint currentPage = firstPage;
            uint[]? heapBuffer = null;
        
            while (currentPage != 0)
            {
                if (count >= stackBuffer.Length)
                {
                    // Switch to heap
                    if (heapBuffer == null)
                    {
                        heapBuffer = new uint[64];
                        stackBuffer[..count].CopyTo(heapBuffer);
                    }
                    else if (count >= heapBuffer.Length)
                    {
                        Array.Resize(ref heapBuffer, heapBuffer.Length * 2);
                    }
                }
                
                if (heapBuffer != null)
                    heapBuffer[count] = currentPage;
                else
                    stackBuffer[count] = currentPage;
                count++;
                
                // Capture before the read overwrites currentPage with the NEXT page number.
                // Releasing `currentPage` after the read released the next link instead of the page
                // just pinned, so every page of a freed chain except the last stayed pinned for the
                // lifetime of the page manager - until the cache reported that everything was pinned.
                // FreeOverflowAsync already got this right.
                var pinnedPage = currentPage;
                var cachedPage = m_pageManager.GetPage(pinnedPage);
                try
                {
                    currentPage = BinaryPrimitives.ReadUInt32LittleEndian(cachedPage.ReadOnlyData[2..]);
                }
                finally
                {
                    m_pageManager.ReleasePage(pinnedPage);
                }
            }
            
            // Free all pages using batch API
            if (heapBuffer != null)
                m_pageManager.FreePages(heapBuffer.AsSpan(0, count));
            else
                m_pageManager.FreePages(stackBuffer[..count]);
        }

        /// <summary>
        /// Gets information about an overflow chain.
        /// </summary>
        public OverflowInfo GetOverflowInfo(uint firstPage)
        {
            ThrowIfDisposed();
        
            var cachedPage = m_pageManager.GetPage(firstPage);
            try
            {
                var pageData = cachedPage.ReadOnlyData;
            
                if (pageData[0] != OVERFLOW_PAGE_TYPE)
                    throw new InvalidDataException($"Page {firstPage} is not an overflow page");
            
                int totalLength = BinaryPrimitives.ReadInt32LittleEndian(pageData[10..]);
                int pageCount = (totalLength + DataSizePerPage - 1) / DataSizePerPage;
            
                return new OverflowInfo(firstPage, totalLength, pageCount);
            }
            finally
            {
                m_pageManager.ReleasePage(firstPage);
            }
        }

        /// <summary>
        /// Gets the total length of overflow data without reading all pages.
        /// </summary>
        public int GetOverflowLength(uint firstPage)
        {
            ThrowIfDisposed();
            
            var cachedPage = m_pageManager.GetPage(firstPage);
            try
            {
                var pageData = cachedPage.ReadOnlyData;
                
                if (pageData[0] != OVERFLOW_PAGE_TYPE)
                    throw new InvalidDataException($"Page {firstPage} is not an overflow page");
                
                return BinaryPrimitives.ReadInt32LittleEndian(pageData[10..]);
            }
            finally
            {
                m_pageManager.ReleasePage(firstPage);
            }
        }

        #endregion

        #region Async Operations

        /// <summary>
        /// Reads a value from overflow pages asynchronously.
        /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
        /// </summary>
        /// <param name="firstPage">First overflow page number.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The overflow data.</returns>
        public async ValueTask<byte[]> ReadOverflowAsync(uint firstPage, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            // Get info first to know total length
            var info = await GetOverflowInfoAsync(firstPage, cancellationToken).ConfigureAwait(false);
            var result = new byte[info.TotalLength];
            await ReadOverflowAsync(firstPage, result, cancellationToken).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Reads a value from overflow pages into an existing buffer asynchronously.
        /// </summary>
        /// <param name="firstPage">First overflow page number.</param>
        /// <param name="destination">Destination buffer (must be large enough).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of bytes read.</returns>
        public async ValueTask<int> ReadOverflowAsync(uint firstPage, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            var firstCachedPage = await m_pageManager.GetPageAsync(firstPage, cancellationToken).ConfigureAwait(false);
            int totalLength;
            uint nextPage;
            int firstDataLen;
            
            try
            {
                var pageData = firstCachedPage.ReadOnlyData;
                
                if (pageData[0] != OVERFLOW_PAGE_TYPE)
                    throw new InvalidDataException($"Page {firstPage} is not an overflow page");
                
                totalLength = BinaryPrimitives.ReadInt32LittleEndian(pageData[10..]);
                
                if (destination.Length < totalLength)
                    throw new ArgumentException($"Destination buffer too small: {destination.Length} < {totalLength}", nameof(destination));
                
                nextPage = BinaryPrimitives.ReadUInt32LittleEndian(pageData[2..]);
                firstDataLen = BinaryPrimitives.ReadInt32LittleEndian(pageData[6..]);
                
                pageData.Slice(OVERFLOW_HEADER_SIZE, firstDataLen).CopyTo(destination.Span);
            }
            finally
            {
                m_pageManager.ReleasePage(firstPage);
            }
            
            int offset = firstDataLen;
            uint currentPage = nextPage;
            
            while (currentPage != 0 && offset < totalLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var cachedPage = await m_pageManager.GetPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
                try
                {
                    var pageData = cachedPage.ReadOnlyData;
                    
                    nextPage = BinaryPrimitives.ReadUInt32LittleEndian(pageData[2..]);
                    int dataLen = BinaryPrimitives.ReadInt32LittleEndian(pageData[6..]);
                    
                    pageData.Slice(OVERFLOW_HEADER_SIZE, dataLen).CopyTo(destination.Span[offset..]);
                    
                    offset += dataLen;
                }
                finally
                {
                    m_pageManager.ReleasePage(currentPage);
                }
                
                currentPage = nextPage;
            }
            
            return totalLength;
        }

        /// <summary>
        /// Gets information about an overflow chain asynchronously.
        /// </summary>
        /// <param name="firstPage">First overflow page number.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Overflow chain information.</returns>
        public async ValueTask<OverflowInfo> GetOverflowInfoAsync(uint firstPage, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            var cachedPage = await m_pageManager.GetPageAsync(firstPage, cancellationToken).ConfigureAwait(false);
            try
            {
                var pageData = cachedPage.ReadOnlyData;
                
                if (pageData[0] != OVERFLOW_PAGE_TYPE)
                    throw new InvalidDataException($"Page {firstPage} is not an overflow page");
                
                int totalLength = BinaryPrimitives.ReadInt32LittleEndian(pageData[10..]);
                int pageCount = (totalLength + DataSizePerPage - 1) / DataSizePerPage;
                
                return new OverflowInfo(firstPage, totalLength, pageCount);
            }
            finally
            {
                m_pageManager.ReleasePage(firstPage);
            }
        }

        /// <summary>
        /// Stores a large value in overflow pages asynchronously.
        /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
        /// </summary>
        /// <param name="data">Data to store.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>First overflow page number.</returns>
        public async ValueTask<uint> StoreOverflowAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (data.Length <= MaxInlineSize)
                throw new ArgumentException($"Value is small enough for inline storage ({data.Length} <= {MaxInlineSize})", nameof(data));

            var dataPerPage = DataSizePerPage;
            var pageCount = (data.Length + dataPerPage - 1) / dataPerPage;
            
            // Allocate pages one at a time (async)
            var pageNumbers = new uint[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (pageNum, page) = await m_pageManager.AllocatePageAsync(PageType.Overflow, cancellationToken).ConfigureAwait(false);
                pageNumbers[i] = pageNum;
                m_pageManager.ReleasePage(pageNum);
            }

            // Write data to all pages
            int offset = 0;
            for (int i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                uint pageNum = pageNumbers[i];
                uint nextPage = i < pageCount - 1 ? pageNumbers[i + 1] : 0;
                
                var cachedPage = await m_pageManager.GetPageAsync(pageNum, cancellationToken).ConfigureAwait(false);
                try
                {
                    int dataLen = Math.Min(dataPerPage, data.Length - offset);
                    
                    var pageData = cachedPage.Data;
                    
                    pageData[0] = OVERFLOW_PAGE_TYPE;
                    pageData[1] = 0;
                    BinaryPrimitives.WriteUInt32LittleEndian(pageData[2..], nextPage);
                    BinaryPrimitives.WriteInt32LittleEndian(pageData[6..], dataLen);
                    BinaryPrimitives.WriteInt32LittleEndian(pageData[10..], i == 0 ? data.Length : 0);
                    
                    data.Span.Slice(offset, dataLen).CopyTo(pageData[OVERFLOW_HEADER_SIZE..]);
                    
                    cachedPage.MarkDirty();
                    offset += dataLen;
                }
                finally
                {
                    m_pageManager.ReleasePage(pageNum);
                }
            }
            
            return pageNumbers[0];
        }

        /// <summary>
        /// Frees all pages in an overflow chain asynchronously.
        /// </summary>
        /// <param name="firstPage">First overflow page number.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask FreeOverflowAsync(uint firstPage, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            var pageNumbers = new List<uint>();
            uint currentPage = firstPage;
            
            while (currentPage != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                pageNumbers.Add(currentPage);
                
                var cachedPage = await m_pageManager.GetPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
                try
                {
                    currentPage = BinaryPrimitives.ReadUInt32LittleEndian(cachedPage.ReadOnlyData[2..]);
                }
                finally
                {
                    m_pageManager.ReleasePage(pageNumbers[^1]);
                }
            }
            
            // Free all pages
            foreach (var pageNum in pageNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await m_pageManager.FreePageAsync(pageNum, cancellationToken).ConfigureAwait(false);
            }
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            m_disposed = true;
        }

        #endregion

        #region Properties

        public int DataSizePerPage => m_pageSize - OVERFLOW_HEADER_SIZE;

        public int MaxInlineSize { get; }

        #endregion
    }
}
