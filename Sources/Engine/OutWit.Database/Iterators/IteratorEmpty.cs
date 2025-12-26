namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that returns no rows.
/// Used for queries that are known to return empty results.
/// </summary>
public sealed class IteratorEmpty : IteratorBase
{
    #region Constructors

    /// <summary>
    /// Creates a new empty iterator with the specified schema.
    /// </summary>
    /// <param name="schema">The column schema for the empty result set.</param>
    public IteratorEmpty(IReadOnlyList<WitSqlColumnInfo> schema)
    {
        Schema = schema;
    }

    /// <summary>
    /// Creates a new empty iterator with no columns.
    /// </summary>
    public IteratorEmpty() : this([])
    {
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        return false;
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema { get; }

    /// <inheritdoc/>
    public override WitSqlRow Current => throw new InvalidOperationException("No current row in empty iterator");

    /// <inheritdoc/>
    public override long EstimatedRowCount => 0;

    #endregion
}