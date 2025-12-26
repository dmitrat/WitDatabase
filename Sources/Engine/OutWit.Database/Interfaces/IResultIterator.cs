namespace OutWit.Database.Interfaces;

/// <summary>
/// Base interface for all query execution iterators.
/// Implements the Volcano/Iterator model for query execution.
/// </summary>
/// <remarks>
/// <para>
/// The iterator model processes rows one at a time in a pull-based fashion.
/// Each iterator requests rows from its children on demand.
/// </para>
/// <para>
/// Lifecycle: Open() -> MoveNext()/Current -> Reset() or Dispose()
/// </para>
/// </remarks>
public interface IResultIterator : IDisposable
{
    /// <summary>
    /// Gets the column schema for results.
    /// </summary>
    IReadOnlyList<WitSqlColumnInfo> Schema { get; }

    /// <summary>
    /// Initializes the iterator before first use.
    /// Must be called before MoveNext().
    /// </summary>
    void Open();

    /// <summary>
    /// Moves to the next row.
    /// </summary>
    /// <returns>True if there is another row; false when no more rows.</returns>
    bool MoveNext();

    /// <summary>
    /// Gets the current row values.
    /// Only valid after MoveNext() returns true.
    /// </summary>
    WitSqlRow Current { get; }

    /// <summary>
    /// Resets the iterator to the beginning.
    /// After reset, Open() must be called again before use.
    /// </summary>
    void Reset();

    /// <summary>
    /// Gets the estimated row count for query planning.
    /// </summary>
    /// <returns>Estimated row count, or -1 if unknown.</returns>
    long EstimatedRowCount => -1;
}