using OutWit.Database.Interfaces;
using OutWit.Database.Sql;
using OutWit.Database.Types;

namespace OutWit.Database.Iterators;

/// <summary>
/// Renames a subquery's output columns positionally, for a derived column list.
/// </summary>
/// <remarks>
/// <para>
/// <c>(SELECT Id, Name FROM T) AS V (Key, Label)</c> - the names come from the list, matched to the
/// subquery's columns by position, which is how PostgreSQL and SQL Server define it. SQLite rejects
/// the syntax entirely; the dialect oracle is what showed that following SQLite here would have been
/// following the wrong engine.
/// </para>
/// <para>
/// A list that does not match the subquery's width is refused rather than padded or truncated. Both
/// targets refuse it, and a silently mismatched rename is a query whose columns mean something other
/// than what they are called.
/// </para>
/// </remarks>
public sealed class IteratorRenameColumns : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly string[] m_names;
    private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;

    private WitSqlRow m_current;

    #endregion

    #region Constructors

    public IteratorRenameColumns(IResultIterator source, IReadOnlyList<string> names)
    {
        var visible = source.Schema
            .Where(column => !IteratorExcludeInternal.IsInternalColumn(column.Name))
            .ToArray();

        if (names.Count != visible.Length)
        {
            throw new InvalidOperationException(
                $"The derived column list names {names.Count} column(s) but the subquery produces " +
                $"{visible.Length}.");
        }

        m_source = source;
        m_names = names.ToArray();
        m_current = new WitSqlRow([], []);

        m_schema = visible
            .Select((column, i) => new WitSqlColumnInfo
            {
                Name = m_names[i],
                Type = column.Type,
                IsNullable = column.IsNullable
            })
            .ToList();
    }

    #endregion

    #region IResultIterator

    public override void Open()
    {
        base.Open();
        m_source.Open();
    }

    public override bool MoveNext()
    {
        if (!m_source.MoveNext())
            return false;

        var source = m_source.Current;
        var values = new Values.WitSqlValue[m_names.Length];

        for (var i = 0; i < m_names.Length; i++)
            values[i] = source[i];

        m_current = new WitSqlRow(values, m_names);
        return true;
    }

    public override void Reset()
    {
        m_source.Reset();
        base.Reset();
    }

    public override void Dispose()
    {
        m_source.Dispose();
        base.Dispose();
    }

    public override long EstimatedRowCount => m_source.EstimatedRowCount;

    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    public override WitSqlRow Current => m_current;

    #endregion
}
