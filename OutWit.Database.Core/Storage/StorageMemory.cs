using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Storage
{
    /// <summary>
    /// In-memory storage implementation for temporary databases and testing.
    /// Data is lost when the storage is disposed.
    /// </summary>
    public sealed class StorageMemory : IStorage
    {
        #region Constants

        private const int INITIAL_CAPACITY_PAGES = 16;
        private const double GROWTH_FACTOR = 1.5;

        /// <summary>
        /// Provider key for memory storage.
        /// </summary>
        public const string PROVIDER_KEY = "memory";

        #endregion

        #region Fields

        private readonly int m_pageSize;

        private byte[] m_data;

        private long m_pageCount;

        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new in-memory storage with the specified page size.
        /// </summary>
        /// <param name="pageSize">Size of each page in bytes</param>
        /// <param name="initialPageCount">Initial number of pages to allocate</param>
        public StorageMemory(int pageSize = DatabaseConstants.DEFAULT_PAGE_SIZE, int initialPageCount = 1)
        {
            if (pageSize < DatabaseConstants.MIN_PAGE_SIZE || pageSize > DatabaseConstants.MAX_PAGE_SIZE)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            m_pageSize = pageSize;
            
            // Allocate with some headroom for growth
            int initialCapacity = Math.Max(initialPageCount, INITIAL_CAPACITY_PAGES);
            m_data = new byte[initialCapacity * pageSize];
            m_pageCount = initialPageCount;
        }

        #endregion

        #region Read

        /// <inheritdoc/>
        public void ReadPage(long pageNumber, Span<byte> buffer)
        {
            ThrowIfDisposed();
            ValidatePageNumber(pageNumber);
            ValidateBuffer(buffer);

            var offset = pageNumber * m_pageSize;
            m_data.AsSpan((int)offset, m_pageSize).CopyTo(buffer);
        }

        /// <inheritdoc/>
        public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadPage(pageNumber, buffer.Span);
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Write

        /// <inheritdoc/>
        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
        {
            ThrowIfDisposed();
            ValidatePageNumber(pageNumber);
            ValidateBuffer(buffer);

            var offset = pageNumber * m_pageSize;
            buffer[..m_pageSize].CopyTo(m_data.AsSpan((int)offset, m_pageSize));
        }

        /// <inheritdoc/>
        public ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WritePage(pageNumber, buffer.Span);
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Flush

        /// <inheritdoc/>
        public void Flush()
        {
            ThrowIfDisposed();
            // No-op for memory storage
        }

        /// <inheritdoc/>
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Flush();
            return ValueTask.CompletedTask;
        }

        #endregion

        #region SetSize

        /// <inheritdoc/>
        public void SetSize(long pageCount)
        {
            ThrowIfDisposed();

            if (pageCount < 0)
                throw new ArgumentOutOfRangeException(nameof(pageCount));

            var requiredSize = pageCount * m_pageSize;
            
            // Only resize if we need more capacity
            if (requiredSize > m_data.Length)
            {
                // Exponential growth to avoid frequent reallocations
                var newCapacity = m_data.Length;
                while (newCapacity < requiredSize)
                {
                    newCapacity = (int)(newCapacity * GROWTH_FACTOR);
                }
                
                var newData = new byte[newCapacity];
                m_data.AsSpan(0, (int)(m_pageCount * m_pageSize)).CopyTo(newData);
                m_data = newData;
            }
            
            // Clear any newly exposed pages
            if (pageCount > m_pageCount)
            {
                var startOffset = (int)(m_pageCount * m_pageSize);
                var endOffset = (int)(pageCount * m_pageSize);
                m_data.AsSpan(startOffset, endOffset - startOffset).Clear();
            }
            
            m_pageCount = pageCount;
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        private void ValidatePageNumber(long pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= m_pageCount)
                throw new ArgumentOutOfRangeException(nameof(pageNumber), 
                    $"Page number must be between 0 and {m_pageCount - 1}");
        }

        private void ValidateBuffer(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < m_pageSize)
                throw new ArgumentException($"Buffer must be at least {m_pageSize} bytes", nameof(buffer));
        }

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_data = [];
                m_disposed = true;
            }
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public int PageSize => m_pageSize;

        /// <inheritdoc/>
        public long PageCount => m_pageCount;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public string ProviderKey => PROVIDER_KEY;

        /// <summary>
        /// Gets a read-only view of the underlying data for testing purposes.
        /// </summary>
        public ReadOnlyMemory<byte> Data => m_data.AsMemory(0, (int)(m_pageCount * m_pageSize));

        /// <summary>
        /// Gets the current capacity in pages (may be larger than PageCount).
        /// </summary>
        public long Capacity => m_data.Length / m_pageSize;

        #endregion
    }
}
