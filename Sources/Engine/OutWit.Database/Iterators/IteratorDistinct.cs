using OutWit.Database.Interfaces;
using OutWit.Database.Model;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that removes duplicate rows from a source iterator.
/// Used for DISTINCT queries. Stores full row values for accurate comparison.
/// </summary>
public sealed class IteratorDistinct : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly HashSet<RowKey> m_seenRows;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new distinct iterator.
    /// </summary>
    /// <param name="source">The source iterator.</param>
    public IteratorDistinct(IResultIterator source)
    {
        m_source = source;
        m_seenRows = new HashSet<RowKey>();
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();
        m_seenRows.Clear();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        while (m_source.MoveNext())
        {
            var key = new RowKey(m_source.Current);
            if (m_seenRows.Add(key))
            {
                m_current = m_source.Current;
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
        m_seenRows.Clear();
        m_current = default;
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
    public override WitSqlRow Current => m_current;

    #endregion
}