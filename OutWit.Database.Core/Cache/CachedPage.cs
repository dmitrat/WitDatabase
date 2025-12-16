using System.Buffers;
using System.Threading;

namespace OutWit.Database.Core.Cache;

/// <summary>
/// Represents a cached page with its data and metadata.
/// </summary>
public sealed class CachedPage : IDisposable
{
    #region Fields

    private readonly byte[] m_rentedBuffer;

    private readonly int m_pageSize;

    private volatile bool m_disposed;

    private volatile bool m_isDirty;

    private int m_referenceCount;

    private volatile bool m_referenced;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new cached page with data from the array pool.
    /// </summary>
    /// <param name="pageNumber">The page number in the database file.</param>
    /// <param name="pageSize">The size of the page in bytes.</param>
    public CachedPage(long pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        m_pageSize = pageSize;
        m_rentedBuffer = ArrayPool<byte>.Shared.Rent(pageSize);
        m_isDirty = false;
        m_referenceCount = 0;
        m_referenced = false;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Marks this page as modified.
    /// </summary>
    public void MarkDirty()
    {
        ThrowIfDisposed();
        m_isDirty = true;
    }

    /// <summary>
    /// Clears the dirty flag (after flushing to storage).
    /// </summary>
    internal void ClearDirty()
    {
        m_isDirty = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }
    
    /// <summary>
    /// Atomically increments the reference count.
    /// </summary>
    internal int IncrementReferenceCount() => Interlocked.Increment(ref m_referenceCount);

    /// <summary>
    /// Atomically decrements the reference count, ensuring it doesn't go below zero.
    /// </summary>
    internal int DecrementReferenceCount()
    {
        int newValue;
        int currentValue;
        do
        {
            currentValue = Volatile.Read(ref m_referenceCount);
            newValue = Math.Max(0, currentValue - 1);
        } while (Interlocked.CompareExchange(ref m_referenceCount, newValue, currentValue) != currentValue);

        return newValue;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!m_disposed)
        {
            m_disposed = true;
            ArrayPool<byte>.Shared.Return(m_rentedBuffer);
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Page number in the database file
    /// </summary>
    public long PageNumber { get; }

    /// <summary>
    /// Whether this page has been modified since being loaded
    /// </summary>
    public bool IsDirty => m_isDirty;

    /// <summary>
    /// Whether this page has been disposed
    /// </summary>
    public bool IsDisposed => m_disposed;

    /// <summary>
    /// The page data as a span
    /// </summary>
    public Span<byte> Data
    {
        get
        {
            ThrowIfDisposed();
            return m_rentedBuffer.AsSpan(0, m_pageSize);
        }
    }

    /// <summary>
    /// The page data as a readonly span
    /// </summary>
    public ReadOnlySpan<byte> ReadOnlyData
    {
        get
        {
            ThrowIfDisposed();
            return m_rentedBuffer.AsSpan(0, m_pageSize);
        }
    }

    /// <summary>
    /// The page data as memory
    /// </summary>
    public Memory<byte> Memory
    {
        get
        {
            ThrowIfDisposed();
            return m_rentedBuffer.AsMemory(0, m_pageSize);
        }
    }

    /// <summary>
    /// Reference count for tracking active users.
    /// Thread-safe via Interlocked operations.
    /// </summary>
    internal int ReferenceCount
    {
        get => Volatile.Read(ref m_referenceCount);
        set => Volatile.Write(ref m_referenceCount, value);
    }

    /// <summary>
    /// Referenced bit for Clock algorithm (second chance)
    /// </summary>
    internal bool Referenced
    {
        get => m_referenced;
        set => m_referenced = value;
    }

    #endregion
}
