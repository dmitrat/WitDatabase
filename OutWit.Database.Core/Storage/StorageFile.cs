using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Storage
{
    /// <summary>
    /// File system storage implementation using FileStream.
    /// Provides durable storage with support for file locking.
    /// Thread-safe for concurrent access.
    /// </summary>
    public sealed class StorageFile : IStorage
    {
        #region Constants

        /// <summary>
        /// Provider key for file storage.
        /// </summary>
        public const string PROVIDER_KEY = "file";

        #endregion

        #region Fields

        private readonly FileStream m_stream;

        private readonly int m_pageSize;

        private readonly bool m_isReadOnly;

        private readonly Lock m_lock = new();

        private long m_cachedPageCount;

        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Opens an existing database file or creates a new one.
        /// </summary>
        /// <param name="path">Path to the database file</param>
        /// <param name="pageSize">Page size (only used when creating new file)</param>
        /// <param name="readOnly">Whether to open in read-only mode</param>
        public StorageFile(string path, int pageSize = DatabaseConstants.DEFAULT_PAGE_SIZE, bool readOnly = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (pageSize < DatabaseConstants.MIN_PAGE_SIZE || pageSize > DatabaseConstants.MAX_PAGE_SIZE)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            m_pageSize = pageSize;
            m_isReadOnly = readOnly;

            var fileMode = readOnly ? FileMode.Open : FileMode.OpenOrCreate;
            var fileAccess = readOnly ? FileAccess.Read : FileAccess.ReadWrite;
            var fileShare = readOnly ? FileShare.Read : FileShare.None;

            m_stream = new FileStream(
                path,
                fileMode,
                fileAccess,
                fileShare,
                bufferSize: pageSize,
                FileOptions.RandomAccess);

            // Cache initial page count
            m_cachedPageCount = m_stream.Length / m_pageSize;

            // If new file, ensure it has at least one page
            if (m_stream.Length == 0 && !readOnly)
            {
                SetSize(1);
            }
        }

        #endregion

        #region Create

        /// <summary>
        /// Creates a new database file. Throws if file already exists.
        /// </summary>
        public static StorageFile Create(string path, int pageSize = DatabaseConstants.DEFAULT_PAGE_SIZE)
        {
            if (File.Exists(path))
                throw new IOException($"File already exists: {path}");
        
            return new StorageFile(path, pageSize, readOnly: false);
        }

        #endregion

        #region Open

        /// <summary>
        /// Opens an existing database file.
        /// </summary>
        public static StorageFile Open(string path, bool readOnly = false)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Database file not found", path);

            return new StorageFile(path, readOnly: readOnly);
        }

        #endregion

        #region Read

        /// <inheritdoc/>
        public void ReadPage(long pageNumber, Span<byte> buffer)
        {
            ThrowIfDisposed();
            ValidatePageNumberForRead(pageNumber);
            ValidateBuffer(buffer);

            var offset = pageNumber * m_pageSize;
            
            lock (m_lock)
            {
                m_stream.Seek(offset, SeekOrigin.Begin);

                var bytesRead = m_stream.Read(buffer[..m_pageSize]);
                if (bytesRead < m_pageSize)
                {
                    // Fill remaining with zeros if file is shorter
                    buffer[bytesRead..m_pageSize].Clear();
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidatePageNumberForRead(pageNumber);
            ValidateBuffer(buffer.Span);

            var offset = pageNumber * m_pageSize;
            
            var bytesRead = await RandomAccess.ReadAsync(m_stream.SafeFileHandle, buffer[..m_pageSize], offset, cancellationToken);
            if (bytesRead < m_pageSize)
            {
                buffer.Span[bytesRead..m_pageSize].Clear();
            }
        }

        #endregion

        #region Write

        /// <inheritdoc/>
        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
        {
            ThrowIfDisposed();
            ThrowIfReadOnly();
            ValidatePageNumberForWrite(pageNumber);
            ValidateBuffer(buffer);

            var offset = pageNumber * m_pageSize;
            
            lock (m_lock)
            {
                m_stream.Seek(offset, SeekOrigin.Begin);
                m_stream.Write(buffer[..m_pageSize]);
            }
        }

        /// <inheritdoc/>
        public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ThrowIfReadOnly();
            ValidatePageNumberForWrite(pageNumber);
            ValidateBuffer(buffer.Span);

            var offset = pageNumber * m_pageSize;
            
            await RandomAccess.WriteAsync(m_stream.SafeFileHandle, buffer[..m_pageSize], offset, cancellationToken);
        }

        #endregion

        #region Flush

        /// <inheritdoc/>
        public void Flush()
        {
            ThrowIfDisposed();
            
            lock (m_lock)
            {
                m_stream.Flush(flushToDisk: true);
            }
        }

        /// <inheritdoc/>
        public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            await m_stream.FlushAsync(cancellationToken);
            
            lock (m_lock)
            {
                m_stream.Flush(flushToDisk: true);
            }
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        private void ThrowIfReadOnly()
        {
            if (m_isReadOnly)
                throw new InvalidOperationException("Storage is read-only");
        }

        private void ValidatePageNumberForRead(long pageNumber)
        {
            if (pageNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number cannot be negative");

            // Use cached value for fast validation (read without lock)
            if (pageNumber >= Volatile.Read(ref m_cachedPageCount))
                throw new ArgumentOutOfRangeException(nameof(pageNumber),
                    $"Page number must be less than {m_cachedPageCount}");
        }

        private void ValidatePageNumberForWrite(long pageNumber)
        {
            if (pageNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number cannot be negative");
        }

        private void ValidateBuffer(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < m_pageSize)
                throw new ArgumentException($"Buffer must be at least {m_pageSize} bytes", nameof(buffer));
        }

        #endregion

        #region SetSize

        /// <inheritdoc/>
        public void SetSize(long pageCount)
        {
            ThrowIfDisposed();
            ThrowIfReadOnly();

            if (pageCount < 0)
                throw new ArgumentOutOfRangeException(nameof(pageCount));
            
            var newSize = pageCount * m_pageSize;
            
            lock (m_lock)
            {
                m_stream.SetLength(newSize);
                Volatile.Write(ref m_cachedPageCount, pageCount);
            }
        }

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                lock (m_lock)
                {
                    if (!m_disposed)
                    {
                        m_stream.Dispose();
                        m_disposed = true;
                    }
                }
            }
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public int PageSize => m_pageSize;

        /// <inheritdoc/>
        public long PageCount => Volatile.Read(ref m_cachedPageCount);

        /// <inheritdoc/>
        public bool IsReadOnly => m_isReadOnly;

        /// <inheritdoc/>
        public string ProviderKey => PROVIDER_KEY;

        #endregion
    }
}
