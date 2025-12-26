using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that returns a single pre-computed row.
/// Used for SELECT without FROM clause (e.g., SELECT 1 + 1).
/// </summary>
public sealed class IteratorSingleRow : IteratorBase
{
    #region Fields

    private bool m_returned;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new single row iterator.
    /// </summary>
    /// <param name="values">The values for the single row.</param>
    /// <param name="columnNames">The column names.</param>
    public IteratorSingleRow(WitSqlValue[] values, string[] columnNames)
    {
        Current = new WitSqlRow(values, columnNames);
        Schema = BuildSchema(values, columnNames);
    }

    #endregion

    #region Functions

    private static List<WitSqlColumnInfo> BuildSchema(WitSqlValue[] values, string[] columnNames)
    {
        var schema = new List<WitSqlColumnInfo>(columnNames.Length);
        for (int i = 0; i < columnNames.Length; i++)
        {
            schema.Add(new WitSqlColumnInfo
            {
                Name = columnNames[i],
                Type = values[i].Type
            });
        }
        return schema;
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_returned = false;
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (m_returned)
            return false;
        m_returned = true;
        return true;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_returned = false;
    }

    #endregion

    #region Properties
    
    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema { get; }

    /// <inheritdoc/>
    public override WitSqlRow Current { get; }

    /// <inheritdoc/>
    public override long EstimatedRowCount => 1;

    #endregion
}