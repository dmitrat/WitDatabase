using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// An <see cref="IStorage"/> that plays the part of the operating system between the database and
/// the disk, so that a power failure can be modelled deterministically.
/// </summary>
/// <remarks>
/// <b>Why a model rather than a real crash.</b> A process kill is not a power cut: the operating
/// system is still running afterwards and writes its page cache back, so data that was never fsynced
/// survives anyway and a durability test goes green on a live defect. Killing the machine would
/// settle it and cannot run in CI. So the cache is modelled instead: a write lands in
/// <see cref="m_cache"/> and goes no further, <see cref="Flush"/> promotes what is cached onto the
/// inner storage - the "media" - and <see cref="PowerCut"/> discards whatever was never promoted.
///
/// <b>What it proves, and what it does not.</b> It proves the database did not <i>ask</i> for
/// durability at the point it claims to have achieved it, and that under these semantics the data is
/// gone. It does not prove behaviour on real hardware: a disk with a write-back cache, a filesystem
/// with different ordering guarantees, or an fsync that lies can all change the outcome. Say both
/// halves when reporting a finding from it.
///
/// <b>Its own controls</b> live in <c>PowerCutStorageControlTests</c>: a write that is flushed must
/// survive a cut, and the same write without the flush must not. If the first fails the model is too
/// aggressive; if the second passes it is not modelling anything. <see cref="FlushCount"/> exists so
/// that "this path never fsyncs" can be <i>counted</i> rather than inferred - and counted against a
/// path that does, or a zero would be indistinguishable from a broken counter.
/// </remarks>
public sealed class PowerCutStorage : IStorage
{
    #region Fields

    private readonly IStorage m_media;
    private readonly bool m_ownsMedia;

    /// <summary>Pages written but not yet promoted to the media - the operating system's cache.</summary>
    private readonly Dictionary<long, byte[]> m_cache = new();

    private readonly Lock m_lock = new();

    private long m_pageCount;

    #endregion

    #region Constructors

    public PowerCutStorage(IStorage media, bool ownsMedia = true)
    {
        m_media = media;
        m_ownsMedia = ownsMedia;
        m_pageCount = media.PageCount;
    }

    #endregion

    #region Power cut

    /// <summary>
    /// Cuts the power: everything written and not flushed never reaches the media.
    /// </summary>
    /// <returns>How many pages were lost, which is what a report should quote.</returns>
    public int PowerCut()
    {
        lock (m_lock)
        {
            var lost = m_cache.Count;

            m_cache.Clear();
            m_pageCount = m_media.PageCount;

            return lost;
        }
    }

    /// <summary>How many times the database asked for its writes to be made durable.</summary>
    public int FlushCount { get; private set; }

    /// <summary>How many pages are written but not yet durable.</summary>
    public int PagesAtRisk
    {
        get
        {
            lock (m_lock)
                return m_cache.Count;
        }
    }

    #endregion

    #region Read

    public void ReadPage(long pageNumber, Span<byte> buffer)
    {
        lock (m_lock)
        {
            // A read sees the cache first, exactly as it would through the operating system: until
            // the power goes the process cannot tell a cached page from a durable one, and that is
            // the whole reason the defect is invisible in an ordinary test.
            if (m_cache.TryGetValue(pageNumber, out var cached))
            {
                cached.AsSpan(0, Math.Min(cached.Length, buffer.Length)).CopyTo(buffer);
                return;
            }
        }

        m_media.ReadPage(pageNumber, buffer);
    }

    public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadPage(pageNumber, buffer.Span);
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Write

    public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
    {
        lock (m_lock)
        {
            m_cache[pageNumber] = buffer.ToArray();

            if (pageNumber + 1 > m_pageCount)
                m_pageCount = pageNumber + 1;
        }
    }

    public ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        WritePage(pageNumber, buffer.Span);
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Flush

    public void Flush()
    {
        KeyValuePair<long, byte[]>[] pending;

        lock (m_lock)
        {
            pending = m_cache.ToArray();
            m_cache.Clear();
        }

        if (pending.Length > 0)
        {
            var highest = pending.Max(p => p.Key);
            if (highest + 1 > m_media.PageCount)
                m_media.SetSize(highest + 1);

            foreach (var (pageNumber, data) in pending)
                m_media.WritePage(pageNumber, data);
        }

        m_media.Flush();

        FlushCount++;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Flush();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region SetSize

    public void SetSize(long pageCount)
    {
        lock (m_lock)
        {
            if (pageCount > m_pageCount)
                m_pageCount = pageCount;
        }
    }

    #endregion

    #region Properties

    public int PageSize => m_media.PageSize;

    public long PageCount
    {
        get
        {
            lock (m_lock)
                return m_pageCount;
        }
    }

    public bool IsReadOnly => m_media.IsReadOnly;

    // The media's own key, so that a database written through this decorator can be reopened
    // directly on the media afterwards - which is how a test inspects what survived.
    public string ProviderKey => m_media.ProviderKey;

    #endregion

    #region IDisposable

    public void Dispose()
    {
        // Deliberately does NOT flush. Disposing is not a promise of durability, and a decorator that
        // quietly flushed here would make every cut look survivable.
        if (m_ownsMedia)
            m_media.Dispose();
    }

    #endregion
}
