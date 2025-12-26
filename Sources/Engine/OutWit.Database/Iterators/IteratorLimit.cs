using OutWit.Database.Interfaces;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that limits the number of rows returned from a source iterator.
/// Supports LIMIT and OFFSET for pagination.
/// </summary>
public sealed class IteratorLimit : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly long m_limit;
    private readonly long m_offset;
    private long m_returned;
    private long m_skipped;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new limit iterator.
    /// </summary>
    /// <param name="source">The source iterator.</param>
    /// <param name="limit">Maximum number of rows to return.</param>
    /// <param name="offset">Number of rows to skip before returning.</param>
    public IteratorLimit(IResultIterator source, long limit, long offset = 0)
    {
        m_source = source;
        m_limit = limit;
        m_offset = offset;
    }

    #endregion

    #region IResultIterator
    
    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();
        m_returned = 0;
        m_skipped = 0;
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        // Skip offset rows
        while (m_skipped < m_offset && m_source.MoveNext())
        {
            m_skipped++;
        }

        if (m_returned >= m_limit)
            return false;

        if (m_source.MoveNext())
        {
            m_returned++;
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
        m_returned = 0;
        m_skipped = 0;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_source.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_source.Schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_source.Current;

    #endregion
}