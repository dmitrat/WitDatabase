namespace OutWit.Database.Optimizers;

/// <summary>
/// Tells the optimizer where a value sits inside the range of values an index actually holds.
/// </summary>
/// <remarks>
/// <para>
/// The optimizer chooses between an index and a table scan by estimating how many rows a predicate
/// returns, and for a range predicate it had nothing to go on: every one was estimated at a flat 20% of
/// the table, whatever the bound. Measured on 1,000 rows holding the values 1..1000, that is
/// <b>200x too high</b> for <c>Value &gt; 999</c> and <b>five times too low</b> for <c>Value &gt; 0</c>.
/// </para>
/// <para>
/// <b>Deliberately a fraction rather than a row count.</b> The estimator knows the shape of the index;
/// the optimizer knows the table's row count and what the predicate does with it. Splitting them that
/// way keeps the estimator free of any assumption about how its answer is used.
/// </para>
/// <para>
/// <b>Null means "no idea", and it must stay cheap to say so.</b> An implementation that cannot answer
/// without reading the index should answer null: a wrong estimate picks a slower plan, but an expensive
/// estimate is paid on every query - which is the defect 11.1.0 removed, where the planner scanned
/// 1,000 rows per execution.
/// </para>
/// </remarks>
public interface IIndexRangeStatistics
{
    /// <summary>
    /// The fraction of the index's keys that sort below <paramref name="value"/>, in 0..1, or
    /// <c>null</c> when it cannot be determined cheaply.
    /// </summary>
    /// <param name="indexName">The index to ask about.</param>
    /// <param name="value">The value from the predicate.</param>
    double? FractionBelow(string indexName, object? value);
}
