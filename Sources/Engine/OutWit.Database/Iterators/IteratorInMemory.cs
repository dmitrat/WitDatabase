using OutWit.Database.Interfaces;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator over an in-memory collection of rows.
/// Used for recursive CTE working tables and other in-memory operations.
/// </summary>
public sealed class IteratorInMemory : IteratorBase
{
    #region Fields

    private readonly IReadOnlyList<WitSqlRow> m_rows;
    private int m_currentIndex;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new in-memory iterator.
    /// </summary>
    /// <param name="rows">The rows to iterate over.</param>
    /// <param name="schema">The schema of the rows.</param>
    public IteratorInMemory(IReadOnlyList<WitSqlRow> rows, IReadOnlyList<WitSqlColumnInfo> schema)
    {
        m_rows = rows;
        Schema = schema;
        m_currentIndex = -1;
    }

    /// <summary>
    /// Creates a new in-memory iterator, inferring schema from the first row.
    /// </summary>
    /// <param name="rows">The rows to iterate over.</param>
    public IteratorInMemory(IReadOnlyList<WitSqlRow> rows)
    {
        m_rows = rows;
        Schema = BuildSchemaFromRows(rows);
        m_currentIndex = -1;
    }

    #endregion

    #region Functions

    private static IReadOnlyList<WitSqlColumnInfo> BuildSchemaFromRows(IReadOnlyList<WitSqlRow> rows)
    {
        if (rows.Count == 0)
            return Array.Empty<WitSqlColumnInfo>();

        var firstRow = rows[0];
        var schema = new List<WitSqlColumnInfo>(firstRow.ColumnCount);

        for (int i = 0; i < firstRow.ColumnCount; i++)
        {
            schema.Add(new WitSqlColumnInfo
            {
                Name = firstRow.ColumnNames[i],
                Type = firstRow.Values[i].Type,
                IsNullable = true
            });
        }

        return schema;
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema { get; }

    /// <inheritdoc/>
    public override WitSqlRow Current => m_rows[m_currentIndex];

    /// <inheritdoc/>
    public override long EstimatedRowCount => m_rows.Count;

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_currentIndex = -1;
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        m_currentIndex++;
        return m_currentIndex < m_rows.Count;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_currentIndex = -1;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        // Nothing to dispose - rows are owned by caller
    }

    #endregion
}
