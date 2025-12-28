using OutWit.Database.Interfaces;
using OutWit.Database.Model;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator for EXCEPT operations.
/// Returns rows from left that don't exist in right.
/// </summary>
public sealed class IteratorExcept : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_left;
    private readonly IResultIterator m_right;
    private readonly bool m_isAll;
    private HashSet<RowKey>? m_rightRows;
    private Dictionary<RowKey, int>? m_rightRowCounts;
    private HashSet<RowKey>? m_returnedRows;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new EXCEPT iterator.
    /// </summary>
    /// <param name="left">The left iterator.</param>
    /// <param name="right">The right iterator.</param>
    /// <param name="isAll">If true, preserves duplicates (EXCEPT ALL); if false, removes duplicates.</param>
    public IteratorExcept(IResultIterator left, IResultIterator right, bool isAll)
    {
        m_left = left;
        m_right = right;
        m_isAll = isAll;
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_left.Open();
        m_right.Open();

        // Buffer all right rows
        if (m_isAll)
        {
            // For EXCEPT ALL, track counts
            m_rightRowCounts = [];
            while (m_right.MoveNext())
            {
                var key = new RowKey(m_right.Current);
                m_rightRowCounts.TryGetValue(key, out var count);
                m_rightRowCounts[key] = count + 1;
            }
        }
        else
        {
            // For EXCEPT, track existence
            m_rightRows = [];
            m_returnedRows = [];
            while (m_right.MoveNext())
            {
                m_rightRows.Add(new RowKey(m_right.Current));
            }
        }
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        while (m_left.MoveNext())
        {
            var key = new RowKey(m_left.Current);

            if (m_isAll)
            {
                // For EXCEPT ALL, return if not in right or count exhausted
                if (m_rightRowCounts!.TryGetValue(key, out var count) && count > 0)
                {
                    m_rightRowCounts[key] = count - 1;
                    continue; // Skip this row
                }

                m_current = m_left.Current;
                return true;
            }
            else
            {
                // For EXCEPT, skip if in right, and skip duplicates from left
                if (m_rightRows!.Contains(key))
                    continue;

                if (!m_returnedRows!.Add(key))
                    continue; // Skip duplicate from left

                m_current = m_left.Current;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_left.Reset();
        m_right.Reset();
        m_rightRows?.Clear();
        m_rightRowCounts?.Clear();
        m_returnedRows?.Clear();
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_left.Dispose();
        m_right.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_left.Schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion
}
