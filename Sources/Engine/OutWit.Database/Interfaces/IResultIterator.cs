namespace OutWit.Database.Interfaces;

/// <summary>
/// Base interface for all query execution iterators.
/// Implements the Volcano/Iterator model for query execution.
/// </summary>
public interface IResultIterator : IDisposable
{
    /// <summary>
    /// Gets the column schema for results.
    /// </summary>
    IReadOnlyList<WitSqlColumnInfo> Schema { get; }

    /// <summary>
    /// Initializes the iterator before first use.
    /// </summary>
    void Open();

    /// <summary>
    /// Moves to the next row.
    /// </summary>
    /// <returns>True if there is another row; false when no more rows.</returns>
    bool MoveNext();

    /// <summary>
    /// Gets the current row values.
    /// </summary>
    WitSqlRow Current { get; }

    /// <summary>
    /// Resets the iterator to the beginning.
    /// </summary>
    void Reset();

    /// <summary>
    /// Gets the estimated row count for query planning. Returns -1 if unknown.
    /// </summary>
    long EstimatedRowCount => -1;
}