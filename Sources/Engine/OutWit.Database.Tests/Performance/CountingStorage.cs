using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// A storage that counts what passes through it.
/// </summary>
/// <remarks>
/// It exists because a wall clock cannot say what these tests mean. "An insert takes less than a
/// millisecond" is a statement about the disk under the machine running it - the same engine in
/// memory does the same work in 0.05 ms - so a bound in milliseconds is red on one machine and
/// vacuous on another, and it is red here for reasons that have nothing to do with the engine.
/// A COUNT of pages read and written does not move with the machine, the load or the cache, and it
/// is what the claims underneath these tests were always about: whether an insert touches the disk
/// for the row-id counter, and whether its cost grows with the rows already in the table.
/// </remarks>
public sealed class CountingStorage : IStorage
{
    #region Fields

    private readonly IStorage m_inner;

    #endregion

    #region Constructors

    public CountingStorage(IStorage inner) => m_inner = inner;

    #endregion

    #region Counters

    public int PagesRead { get; private set; }

    public int PagesWritten { get; private set; }

    public int Flushes { get; private set; }

    public void Reset()
    {
        PagesRead = 0;
        PagesWritten = 0;
        Flushes = 0;
    }

    /// <summary>Everything at once, for a test's own output.</summary>
    public override string ToString() =>
        $"read {PagesRead}, written {PagesWritten}, flushed {Flushes}";

    #endregion

    #region IStorage

    public void ReadPage(long pageNumber, Span<byte> buffer)
    {
        PagesRead++;
        m_inner.ReadPage(pageNumber, buffer);
    }

    public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        PagesRead++;
        return m_inner.ReadPageAsync(pageNumber, buffer, cancellationToken);
    }

    public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
    {
        PagesWritten++;
        m_inner.WritePage(pageNumber, buffer);
    }

    public ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        PagesWritten++;
        return m_inner.WritePageAsync(pageNumber, buffer, cancellationToken);
    }

    public void Flush()
    {
        Flushes++;
        m_inner.Flush();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Flushes++;
        return m_inner.FlushAsync(cancellationToken);
    }

    public void SetSize(long pageCount) => m_inner.SetSize(pageCount);

    public ValueTask SetSizeAsync(long pageCount, CancellationToken cancellationToken = default) =>
        m_inner.SetSizeAsync(pageCount, cancellationToken);

    public int PageSize => m_inner.PageSize;

    public long PageCount => m_inner.PageCount;

    public bool IsReadOnly => m_inner.IsReadOnly;

    public string ProviderKey => m_inner.ProviderKey;

    public void Dispose() => m_inner.Dispose();

    #endregion
}
