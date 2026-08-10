using OutWit.Database.Interfaces;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Narrows a grouped result back to the columns the query actually asked for.
/// </summary>
/// <remarks>
/// <para>
/// A grouped row is built out of the SELECT list and nothing else, so an <c>ORDER BY</c> or
/// <c>HAVING</c> naming a column the query GROUPS BY - but does not select - was evaluated against a
/// row that does not have it. The planner answers that by carrying the grouping expressions on the
/// grouped row as extra trailing columns; this iterator drops them again once the sort has used
/// them, so the caller sees exactly the SELECT list.
/// </para>
/// <para>
/// Trimming is by COUNT rather than by name - a carried grouping key keeps its own name, and that
/// name may legitimately be the name of a select item as well.
/// </para>
/// </remarks>
internal sealed class IteratorHideGroupingKeys : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;
    private readonly string[] m_columnNames;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an iterator that shows the first <paramref name="visibleColumns"/> columns of its
    /// source and hides the rest.
    /// </summary>
    public IteratorHideGroupingKeys(IResultIterator source, int visibleColumns)
    {
        m_source = source;
        m_schema = [.. source.Schema.Take(visibleColumns)];
        m_columnNames = [.. m_schema.Select(column => column.Name)];
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (!m_source.MoveNext())
            return false;

        var sourceRow = m_source.Current;
        var values = new WitSqlValue[m_columnNames.Length];

        for (int i = 0; i < values.Length; i++)
            values[i] = sourceRow[i];

        m_current = new WitSqlRow(values, m_columnNames);
        return true;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
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
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion
}
