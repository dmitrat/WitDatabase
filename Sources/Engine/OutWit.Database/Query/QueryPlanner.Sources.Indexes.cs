using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Model;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Query;

/// <summary>
/// Index optimization and iterator creation for QueryPlanner.
/// </summary>
public sealed partial class QueryPlanner
{
    #region Optimized Table Iterator

    /// <summary>
    /// Creates an optimized table iterator, potentially using an index.
    /// </summary>
    private IResultIterator CreateOptimizedTableIterator(string tableName, string alias, Parser.Expressions.WitSqlExpression? whereClause)
    {
        // Get table definition (may be null for mocked databases in tests)
        var table = m_context.Database.GetTable(tableName);
        
        // If table definition is not available, fall back to simple table scan
        if (table == null)
        {
            return WrapWithAlias(m_context.Database.CreateTableScan(tableName), alias);
        }

        // Get available indexes for this table
        var indexes = m_context.Database.GetTableIndexes(tableName).ToList();

        // Try to find the best index strategy
        IndexStrategy? strategy = null;
        if (whereClause != null && indexes.Count > 0)
        {
            // Estimate row count (we don't have statistics, so use a heuristic)
            long estimatedRowCount = EstimateTableRowCount(tableName);
            
            if (estimatedRowCount >= MIN_ROWS_FOR_INDEX)
            {
                // What the indexes actually hold, so a range predicate is estimated from the data
                // rather than from a flat 20% of the table. Built per plan, and it reads nothing until
                // a range predicate asks - an equality never pays for it.
                var statistics = new IndexRangeStatistics(m_context.Database, table);

                strategy = m_optimizer.FindBestIndex(tableName, whereClause, indexes, estimatedRowCount, statistics);
            }
        }

        IResultIterator iterator;

        if (strategy != null)
        {
            // Use index-based access
            iterator = CreateIndexIterator(tableName, strategy);
        }
        else
        {
            // Fall back to table scan
            iterator = m_context.Database.CreateTableScan(tableName);
        }

        return WrapWithAlias(iterator, alias);
    }

    /// <summary>
    /// Creates an index-based iterator based on the strategy.
    /// </summary>
    private IResultIterator CreateIndexIterator(string tableName, IndexStrategy strategy)
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        switch (strategy.AccessType)
        {
            case IndexAccessType.Seek:
                // Equality lookup
                if (strategy.SeekValues is { Count: > 0 })
                {
                    // Composite index: pass all column values for correct key construction
                    var seekValues = strategy.SeekValues
                        .Select(v => evaluator.Evaluate(v, dummyRow))
                        .ToArray();
                    return m_context.Database.CreateIndexSeek(
                        tableName,
                        strategy.IndexName,
                        seekValues);
                }
                else
                {
                    var seekValue = evaluator.Evaluate(strategy.SeekValue!, dummyRow);
                    return m_context.Database.CreateIndexSeek(
                        tableName,
                        strategy.IndexName,
                        [seekValue]);
                }

            case IndexAccessType.RangeScan:
                // Range scan - explicitly handle nullable WitSqlValue
                WitSqlValue? startValue = null;
                WitSqlValue? endValue = null;
                
                if (strategy.RangeStart != null)
                {
                    startValue = evaluator.Evaluate(strategy.RangeStart, dummyRow);
                }
                
                if (strategy.RangeEnd != null)
                {
                    endValue = evaluator.Evaluate(strategy.RangeEnd, dummyRow);
                }

                return m_context.Database.CreateIndexRangeScan(
                    tableName,
                    strategy.IndexName,
                    startValue,
                    strategy.RangeStartInclusive,
                    endValue,
                    strategy.RangeEndInclusive);

            default:
                throw new NotSupportedException($"Index access type not supported: {strategy.AccessType}");
        }
    }

    /// <summary>
    /// Estimates the row count for a table, from the catalog's own counter.
    /// </summary>
    /// <remarks>
    /// This used to open a table scan and read up to 1,000 rows **on every query execution**, to
    /// produce an estimate that decides whether an index is worth using. Measured in phase 10, that
    /// cost 1,317 KB and ~0.49 ms per query against any table carrying an index - about 200x the
    /// lookup it was choosing. The cost grew linearly below 1,000 rows and was flat above them,
    /// landing on the old sample limit to three significant figures.
    ///
    /// It bought nothing. The cost model in <c>OptimizerQuery</c> is homogeneous in this value - a
    /// table scan is costed at <c>N x 1.0</c> and an index range at <c>N x 0.2 x 0.5</c>, so
    /// <c>N</c> cancels out of the comparison and the same plan is chosen whatever the estimate
    /// says. The old code also returned <c>count * 10</c> whenever it hit the cap, so every table
    /// with 1,000 rows or more was reported as exactly 10,000 regardless of its real size.
    ///
    /// The catalog already maintains a per-table row count and answers in O(1) - it is what makes
    /// <c>SELECT COUNT(*)</c> flat in table size. That is a better estimate than the old one and
    /// costs nothing.
    ///
    /// **The caveat, stated here rather than discovered later:** this counter is separate state from
    /// the rows and the two can disagree after a crash. That disqualifies it from answering a user's
    /// <c>COUNT(*)</c>, and is irrelevant for choosing a plan - a wrong estimate picks a slower plan,
    /// it never returns a wrong answer.
    /// </remarks>
    private long EstimateTableRowCount(string tableName)
    {
        var count = m_context.Database.GetTableRowCount(tableName);

        // The catalog answers -1 for a table it does not know. Keep the old fallback rather than
        // returning something that would switch index use off: FindBestIndex refuses any estimate
        // at or below zero, so passing -1 through would silently disable every index on a path that
        // used to use them.
        return count >= 0 ? count : 100;
    }

    #endregion
}
