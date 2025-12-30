using OutWit.Database.Constants;
using OutWit.Database.Context;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Optimizers;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Query;

/// <summary>
/// Plans SELECT queries by building iterator trees.
/// Implements the Volcano/Iterator model for query execution.
/// </summary>
public sealed partial class QueryPlanner
{
    #region Constants

    /// <summary>
    /// Maximum recursion depth for recursive CTEs to prevent infinite loops.
    /// </summary>
    private const int MAX_RECURSION_DEPTH = 1000;

    /// <summary>
    /// Minimum estimated row count to consider index usage.
    /// For very small tables, full scan is often faster.
    /// </summary>
    private const long MIN_ROWS_FOR_INDEX = 10;

    #endregion

    #region Fields

    private readonly ContextExecution m_context;
    private readonly OptimizerQuery m_optimizer;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new query planner.
    /// </summary>
    /// <param name="context">The execution context.</param>
    public QueryPlanner(ContextExecution context)
    {
        m_context = context;
        m_optimizer = new OptimizerQuery();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Builds an iterator tree for a SELECT statement.
    /// </summary>
    /// <param name="select">The SELECT statement to plan.</param>
    /// <returns>The root iterator of the query plan.</returns>
    public IResultIterator Plan(WitSqlStatementSelect select)
    {
        // Register CTE definitions if present
        RegisterCteDefinitions(select);

        // Build base iterator from FROM clause (with optimization)
        var iterator = CreateSourceIterator(select);

        // Apply WHERE filter (predicates not handled by index)
        iterator = ApplyWhereClause(iterator, select.WhereClause, select);

        // Apply FOR UPDATE/SHARE locking (before aggregation/ordering)
        iterator = ApplyLockingClause(iterator, select);

        // Handle aggregation vs non-aggregation paths
        if (IsAggregateQuery(select))
        {
            iterator = PlanAggregateQuery(iterator, select);
        }
        else
        {
            iterator = PlanNonAggregateQuery(iterator, select);
        }

        // Handle set operations (UNION, INTERSECT, EXCEPT)
        iterator = ApplySetOperations(iterator, select);

        return iterator;
    }

    #endregion

    #region Query Planning

    private IResultIterator PlanAggregateQuery(IResultIterator iterator, WitSqlStatementSelect select)
    {
        // Check if we can use streaming aggregation (more efficient)
        // Requirements: no GROUP BY, no HAVING, simple aggregates only
        bool canUseStreaming = 
            (select.GroupByClause == null || select.GroupByClause.Count == 0) &&
            select.HavingClause == null &&
            IteratorStreamingAggregate.CanUseStreamingAggregation(select.SelectList);

        if (canUseStreaming)
        {
            // Use streaming aggregation - O(1) memory
            iterator = new IteratorStreamingAggregate(iterator, select.SelectList, m_context);
        }
        else
        {
            // Use full GROUP BY aggregation - stores all rows
            iterator = new IteratorGroupBy(
                iterator, 
                select.GroupByClause, 
                select.SelectList, 
                m_context,
                select.HavingClause);
        }

        // ORDER BY (after aggregation)
        iterator = ApplyOrderByClause(iterator, select.OrderByClause);

        // LIMIT/OFFSET
        iterator = ApplyLimitClause(iterator, select.LimitCount, select.LimitOffset);

        // DISTINCT (after aggregation)
        iterator = ApplyDistinct(iterator, select.IsDistinct);

        return iterator;
    }

    private IResultIterator PlanNonAggregateQuery(IResultIterator iterator, WitSqlStatementSelect select)
    {
        // Check if query contains window functions
        bool hasWindowFunctions = HasWindowFunctions(select.SelectList);

        if (hasWindowFunctions)
        {
            // Window functions need all data to be processed first
            // Apply ORDER BY before window processing if needed for window functions
            // Note: Window functions have their own ORDER BY in OVER clause
            
            // Window function iterator processes all rows and computes window values
            iterator = new IteratorWindow(iterator, select.SelectList, m_context);
            
            // ORDER BY - apply after window functions for final ordering
            iterator = ApplyOrderByClause(iterator, select.OrderByClause);

            // LIMIT/OFFSET - after ordering
            iterator = ApplyLimitClause(iterator, select.LimitCount, select.LimitOffset);

            // DISTINCT - after window processing
            iterator = ApplyDistinct(iterator, select.IsDistinct);
        }
        else
        {
            // Original non-window path
            // ORDER BY - before projection to access original column names
            iterator = ApplyOrderByClause(iterator, select.OrderByClause);

            // LIMIT/OFFSET - before projection for efficiency
            iterator = ApplyLimitClause(iterator, select.LimitCount, select.LimitOffset);

            // Projection (SELECT columns)
            iterator = ApplyProjection(iterator, select.SelectList);

            // DISTINCT - after projection so internal columns (like _rowid) are excluded
            iterator = ApplyDistinct(iterator, select.IsDistinct);
        }

        return iterator;
    }

    #endregion
}
