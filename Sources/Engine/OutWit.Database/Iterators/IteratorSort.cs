using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Schema.Clauses;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that sorts all rows from a source iterator by ORDER BY clause.
/// This is a blocking operator - it must read all rows before returning any.
/// </summary>
public sealed class IteratorSort : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly IReadOnlyList<ClauseOrderByItem> m_orderBy;
    private readonly ExpressionEvaluator m_evaluator;
    private List<WitSqlRow>? m_sortedRows;
    private int m_position;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new sort iterator.
    /// </summary>
    /// <param name="source">The source iterator to sort.</param>
    /// <param name="orderBy">The ORDER BY clause defining sort order.</param>
    /// <param name="context">The execution context.</param>
    public IteratorSort(IResultIterator source, IReadOnlyList<ClauseOrderByItem> orderBy, ContextExecution context)
    {
        m_source = source;
        m_orderBy = orderBy;
        m_evaluator = new ExpressionEvaluator(context);
        m_position = -1;
    }

    #endregion

    #region Functions

    private int CompareRows(WitSqlRow a, WitSqlRow b)
    {
        foreach (var order in m_orderBy)
        {
            var valA = m_evaluator.Evaluate(order.Expression, a);
            var valB = m_evaluator.Evaluate(order.Expression, b);

            var cmp = valA.CompareTo(valB);
            if (cmp != 0)
                return order.Descending ? -cmp : cmp;
        }
        return 0;
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();

        var rows = new List<WitSqlRow>();
        while (m_source.MoveNext())
        {
            rows.Add(m_source.Current);
        }

        rows.Sort(CompareRows);
        m_sortedRows = rows;
        m_position = -1;
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (m_sortedRows == null)
            throw new InvalidOperationException("Iterator not open");

        m_position++;
        return m_position < m_sortedRows.Count;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
        m_sortedRows = null;
        m_position = -1;
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
    public override WitSqlRow Current
    {
        get
        {
            if (m_sortedRows == null || m_position < 0 || m_position >= m_sortedRows.Count)
                throw new InvalidOperationException("No current row available");
            return m_sortedRows[m_position];
        }
    }


    #endregion
}