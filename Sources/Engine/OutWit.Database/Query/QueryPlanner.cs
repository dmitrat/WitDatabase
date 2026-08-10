using OutWit.Database.Constants;
using OutWit.Database.Context;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Optimizers;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Values;

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

        // A VALUES query has its rows written out rather than read from anywhere, so it short-cuts
        // every step below - there is no source to build, nothing to filter and nothing to group.
        // ORDER BY and LIMIT still apply, which is why they are re-applied rather than skipped.
        if (select.ValuesRows is { } valuesRows)
        {
            IResultIterator values = new IteratorValues(valuesRows, m_context);
            values = ApplyOrderByClause(values, select.OrderByClause);
            return ApplyLimitClause(values, select.LimitCount, select.LimitOffset);
        }

        // Try optimized path for simple COUNT(*) without WHERE
        if (TryOptimizeSimpleCountStar(select, out var optimizedIterator))
        {
            return optimizedIterator!;
        }

        // Try optimized path for simple MIN/MAX on indexed column
        if (TryOptimizeSimpleMinMax(select, out optimizedIterator))
        {
            return optimizedIterator!;
        }

        // Build base iterator from FROM clause (with optimization)
        var iterator = CreateSourceIterator(select);

        // Apply WHERE filter (predicates not handled by index)
        iterator = ApplyWhereClause(iterator, select.WhereClause, select);

        // Apply FOR UPDATE/SHARE locking (before aggregation/ordering)
        iterator = ApplyLockingClause(iterator, select);

        // A trailing ORDER BY, LIMIT and OFFSET belong to the WHOLE set expression, not to the arm
        // they are written after - there is no way to attach one to an arm without parentheses. They
        // were applied inside the branch below and the union was then wrapped around them, so a
        // sorted union came back sorted per arm and `LIMIT 1` over one returned everything the
        // second arm had. KnownIssues 18.
        //
        // DISTINCT is NOT deferred, because `SELECT DISTINCT a FROM t UNION ALL SELECT b FROM u`
        // de-duplicates the FIRST ARM - that is where SQL puts it, and it is what this engine
        // already did.
        var setOperationFollows = select.SetOperations is { Count: > 0 };

        // Handle aggregation vs non-aggregation paths
        if (IsAggregateQuery(select))
        {
            iterator = PlanAggregateQuery(iterator, select, setOperationFollows);
        }
        else
        {
            iterator = PlanNonAggregateQuery(iterator, select, setOperationFollows);
        }

        if (!setOperationFollows)
            return iterator;

        // Handle set operations (UNION, INTERSECT, EXCEPT)
        iterator = ApplySetOperations(iterator, select);

        // And then order and cut the COMBINED result. No select list is passed: the rows here are
        // already the output, so an ORDER BY position counts its columns, and a name is looked up on
        // the result row - which is why a column that is not in the result is now an error instead of
        // a silent ordering of one arm by something nobody asked to see.
        ValidateSetOperationOrderBy(select.OrderByClause, iterator.Schema);

        iterator = ApplyOrderByClause(iterator, select.OrderByClause);

        return ApplyLimitClause(iterator, select.LimitCount, select.LimitOffset);
    }

    #endregion

    #region Simple COUNT(*) Optimization

    /// <summary>
    /// Tries to optimize simple COUNT(*) queries without WHERE clause.
    /// Returns true if optimization was applied.
    /// </summary>
    /// <remarks>
    /// This optimization is disabled when a transaction is active because
    /// the cached row count may not reflect uncommitted changes from the transaction.
    /// The row count cache is updated immediately on INSERT/DELETE, but the actual
    /// data is only committed when the transaction commits. If the transaction is
    /// rolled back, the row count cache would be stale.
    /// </remarks>
    private bool TryOptimizeSimpleCountStar(WitSqlStatementSelect select, out IResultIterator? iterator)
    {
        iterator = null;

        // Requirements for optimization:
        // 1. Single COUNT(*) in SELECT list
        // 2. FROM is a single simple table (no joins, no subqueries)
        // 3. No WHERE clause
        // 4. No GROUP BY
        // 5. No HAVING
        // 6. No CTEs used
        // 7. No active transaction (row count cache may be stale during transactions)

        // IMPORTANT: Skip optimization if there's an active transaction.
        // The row count cache is updated immediately on INSERT/DELETE,
        // but if the transaction is rolled back, the cache would be wrong.
        if (m_context.Database is Engine.WitSqlEngine engine && engine.CurrentTransaction != null)
            return false;

        // Check for WHERE clause
        if (select.WhereClause != null)
            return false;

        // Check for GROUP BY
        if (select.GroupByClause is { Count: > 0 })
            return false;

        // Check for HAVING
        if (select.HavingClause != null)
            return false;

        // Check SELECT list is exactly one COUNT(*)
        if (!IsSimpleCountStar(select.SelectList))
            return false;

        // Check FROM is a single simple table
        if (select.FromClause == null || select.FromClause.Count != 1)
            return false;

        if (select.FromClause[0] is not TableSourceSimple simpleTable)
            return false;

        // Check no CTEs are defined (they might affect the table)
        if (select.CteDefinitions is { Count: > 0 })
            return false;

        // Get the table name
        var tableName = simpleTable.TableName;

        // Get cached row count
        var rowCount = m_context.Database.GetTableRowCount(tableName);
        if (rowCount < 0)
        {
            // Row count not available (table doesn't exist or count unknown)
            return false;
        }

        // Get the alias for the column
        var selectItem = select.SelectList[0];
        var columnName = selectItem.Alias ?? "COUNT";

        // Create a constant iterator returning the row count
        iterator = new IteratorConstant(rowCount, columnName);

        return true;
    }

    /// <summary>
    /// Checks if SELECT list is exactly one COUNT(*) aggregate.
    /// </summary>
    private static bool IsSimpleCountStar(IReadOnlyList<ClauseSelectItem> selectList)
    {
        if (selectList.Count != 1)
            return false;

        var item = selectList[0];
        if (item.Expression is not WitSqlExpressionFunctionCall func)
            return false;

        // Must be COUNT function with star
        return func.FunctionName.Equals("COUNT", StringComparison.OrdinalIgnoreCase) 
               && func.IsStar
               && func.Over == null; // Not a window function
    }

    #endregion

    #region Simple MIN/MAX Optimization

    /// <summary>
    /// Tries to optimize simple MIN/MAX queries when an index exists on the column.
    /// Returns true if optimization was applied.
    /// </summary>
    /// <remarks>
    /// This optimization is disabled when a transaction is active because
    /// the index may not reflect uncommitted changes from the transaction.
    /// </remarks>
    private bool TryOptimizeSimpleMinMax(WitSqlStatementSelect select, out IResultIterator? iterator)
    {
        iterator = null;

        // Requirements for optimization:
        // 1. Single MIN(col) or MAX(col) in SELECT list
        // 2. FROM is a single simple table (no joins, no subqueries)
        // 3. No WHERE clause
        // 4. No GROUP BY
        // 5. No HAVING
        // 6. No CTEs used
        // 7. Index exists on the column
        // 8. No active transaction (index may not reflect uncommitted changes)

        // IMPORTANT: Skip optimization if there's an active transaction.
        // Index updates may not be transactional.
        if (m_context.Database is Engine.WitSqlEngine engine && engine.CurrentTransaction != null)
            return false;

        // Check for WHERE clause
        if (select.WhereClause != null)
            return false;

        // Check for GROUP BY
        if (select.GroupByClause is { Count: > 0 })
            return false;

        // Check for HAVING
        if (select.HavingClause != null)
            return false;

        // Check SELECT list is exactly one MIN or MAX
        if (!TryGetSimpleMinMaxInfo(select.SelectList, out var columnName, out var isMin, out var alias))
            return false;

        // Check FROM is a single simple table
        if (select.FromClause == null || select.FromClause.Count != 1)
            return false;

        if (select.FromClause[0] is not TableSourceSimple simpleTable)
            return false;

        // Check no CTEs are defined
        if (select.CteDefinitions is { Count: > 0 })
            return false;

        var tableName = simpleTable.TableName;

        // Find an index on this column
        var indexes = m_context.Database.GetTableIndexes(tableName);
        var matchingIndex = indexes.FirstOrDefault(idx => 
            idx.Columns.Count > 0 && 
            idx.Columns[0].Equals(columnName, StringComparison.OrdinalIgnoreCase));

        if (matchingIndex == null)
            return false;

        // Check if we can access the physical index via WitSqlEngine
        // Already checked above that it's WitSqlEngine
        var physicalIndex = ((Engine.WitSqlEngine)m_context.Database).GetPhysicalIndex(matchingIndex.Name);
        if (physicalIndex == null)
            return false;

        // Get first or last entry from index
        var entry = isMin ? physicalIndex.GetFirstEntry() : physicalIndex.GetLastEntry();
        
        if (entry == null)
        {
            // Index is empty - return NULL
            iterator = new IteratorConstantNull(alias ?? (isMin ? "MIN" : "MAX"));
            return true;
        }

        // Get table definition to find column type
        var table = m_context.Database.GetTable(tableName);
        if (table == null)
            return false;

        var column = table.Columns.FirstOrDefault(c => 
            c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (column == null)
            return false;

        // Deserialize the value from the index key
        var value = DeserializeIndexKey(entry.Value.IndexKey, column.Type);

        // Create a constant iterator returning the value
        iterator = new IteratorConstant(value, alias ?? (isMin ? "MIN" : "MAX"));

        return true;
    }

    /// <summary>
    /// Checks if SELECT list is exactly one MIN(col) or MAX(col) aggregate.
    /// </summary>
    private static bool TryGetSimpleMinMaxInfo(
        IReadOnlyList<ClauseSelectItem> selectList, 
        out string columnName, 
        out bool isMin,
        out string? alias)
    {
        columnName = string.Empty;
        isMin = false;
        alias = null;

        if (selectList.Count != 1)
            return false;

        var item = selectList[0];
        alias = item.Alias;

        if (item.Expression is not WitSqlExpressionFunctionCall func)
            return false;

        // Must be MIN or MAX function
        if (!func.FunctionName.Equals("MIN", StringComparison.OrdinalIgnoreCase) &&
            !func.FunctionName.Equals("MAX", StringComparison.OrdinalIgnoreCase))
            return false;

        isMin = func.FunctionName.Equals("MIN", StringComparison.OrdinalIgnoreCase);

        // Must not be a window function
        if (func.Over != null)
            return false;

        // Must have exactly one argument that is a simple column reference
        if (func.Arguments == null || func.Arguments.Count != 1)
            return false;

        if (func.Arguments[0] is not WitSqlExpressionColumnRef colRef)
            return false;

        // Must be a simple column (no table prefix required for this optimization)
        columnName = colRef.ColumnName;
        return true;
    }

    /// <summary>
    /// Deserializes an index key back to a WitSqlValue.
    /// </summary>
    private static WitSqlValue DeserializeIndexKey(byte[] indexKey, Types.WitDataType columnType)
    {
        if (indexKey.Length == 0)
            return WitSqlValue.Null;

        // Skip null marker byte if present
        int offset = 0;
        if (indexKey.Length > 0 && indexKey[0] == 0x01)
            offset = 1;

        var data = indexKey.AsSpan(offset);
        if (data.Length == 0)
            return WitSqlValue.Null;

        try
        {
            // Handle integer types
            if (columnType is Types.WitDataType.Int8 or Types.WitDataType.Int16 or 
                Types.WitDataType.Int32 or Types.WitDataType.Int64 or
                Types.WitDataType.UInt8 or Types.WitDataType.UInt16 or 
                Types.WitDataType.UInt32 or Types.WitDataType.UInt64)
            {
                if (data.Length >= 8)
                {
                    var unsignedVal = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(data);
                    var value = unchecked((long)(unsignedVal ^ 0x8000000000000000UL));
                    return WitSqlValue.FromInt(value);
                }
            }
            
            // Handle real/double types
            if (columnType is Types.WitDataType.Float16 or Types.WitDataType.Float32 or Types.WitDataType.Float64)
            {
                if (data.Length >= 8)
                {
                    var bits = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(data);
                    // Reverse the transformation done during serialization
                    if ((bits & long.MinValue) != 0)
                    {
                        // Was positive: flip sign bit back
                        bits ^= long.MinValue;
                    }
                    else
                    {
                        // Was negative: flip all bits back
                        bits = ~bits;
                    }
                    var value = BitConverter.Int64BitsToDouble(bits);
                    return WitSqlValue.FromReal(value);
                }
            }
            
            // Handle string types - stored as UTF8
            if (columnType is Types.WitDataType.StringFixed or Types.WitDataType.StringVariable)
            {
                var value = System.Text.Encoding.UTF8.GetString(data);
                return WitSqlValue.FromText(value);
            }
            
            // Handle date types
            if (columnType is Types.WitDataType.DateOnly)
            {
                if (data.Length >= 4)
                {
                    var dayNumber = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data);
                    var value = DateOnly.FromDayNumber(dayNumber);
                    return WitSqlValue.FromDateOnly(value);
                }
            }
            
            // Handle datetime types
            if (columnType is Types.WitDataType.DateTime)
            {
                if (data.Length >= 8)
                {
                    var ticks = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(data);
                    var value = new DateTime(ticks, DateTimeKind.Utc);
                    return WitSqlValue.FromDateTime(value);
                }
            }
            
            // Handle decimal
            if (columnType is Types.WitDataType.Decimal)
            {
                // Decimal is stored as string in index for correct ordering
                var strValue = System.Text.Encoding.UTF8.GetString(data);
                if (decimal.TryParse(strValue, System.Globalization.CultureInfo.InvariantCulture, out var dec))
                    return WitSqlValue.FromDecimal(dec);
            }

            // Handle boolean
            if (columnType is Types.WitDataType.Boolean)
            {
                if (data.Length >= 1)
                {
                    return WitSqlValue.FromBool(data[0] != 0);
                }
            }

            // Handle Guid
            if (columnType is Types.WitDataType.Guid)
            {
                if (data.Length >= 16)
                {
                    var guid = new Guid(data[..16]);
                    return WitSqlValue.FromGuid(guid);
                }
            }
            
            // Fallback - return as blob
            return WitSqlValue.FromBlob(data.ToArray());
        }
        catch
        {
            // If deserialization fails, return NULL
            return WitSqlValue.Null;
        }
    }

    #endregion

    #region Query Planning

    /// <param name="setOperationFollows">
    /// Whether this query is one arm of a set operation. When it is, the trailing <c>ORDER BY</c>,
    /// <c>LIMIT</c> and <c>OFFSET</c> belong to the combined result and are applied by
    /// <see cref="Plan"/> after the union - so this arm must not only skip them, it must not carry
    /// grouping keys for an <c>ORDER BY</c> that is not its own either, because that would widen the
    /// arm's schema and the set operation compares the two schemas.
    /// </param>
    private IResultIterator PlanAggregateQuery(
        IResultIterator iterator,
        WitSqlStatementSelect select,
        bool setOperationFollows = false)
    {
        // Not select.OrderByClause: when this query is one arm of a set operation, the trailing
        // ORDER BY is the union's and not this arm's - so this arm neither sorts by it nor carries
        // grouping keys for it.
        var orderByClause = setOperationFollows ? null : select.OrderByClause;

        // A `*` becomes the columns it stands for before anything else looks at the list, so the
        // check below and the group iterator both see a list they can evaluate rather than an item
        // with no expression - which is what used to answer NULL. KnownIssues 17.
        var selectList = ExpandStars(select.SelectList, iterator.Schema);

        // And then: a column no group can answer for is refused rather than answered from an
        // arbitrary row of the group. PostgreSQL's and SQL Server's rule, in its strict form.
        ValidateGroupedQuery(selectList, select, orderByClause);

        // Check if we can use streaming aggregation (more efficient)
        // Requirements: no GROUP BY, no HAVING, simple aggregates only
        bool canUseStreaming =
            (select.GroupByClause == null || select.GroupByClause.Count == 0) &&
            select.HavingClause == null &&
            IteratorStreamingAggregate.CanUseStreamingAggregation(selectList);

        if (canUseStreaming)
        {
            // Use streaming aggregation - O(1) memory
            iterator = new IteratorStreamingAggregate(iterator, selectList, m_context);

            // ORDER BY (after aggregation) - resolve aggregate expressions to result columns
            iterator = ApplyOrderByClauseForAggregate(
                iterator, orderByClause, selectList, selectList.Count);
        }
        else
        {
            // A grouped row is built out of the SELECT list, so a clause naming a column the query
            // GROUPS BY without selecting it was evaluated against a row that does not have it -
            // KnownIssues 15. The grouping expressions ride along on the grouped row instead, both
            // clauses resolve against that wider list, and the extra columns are dropped again as
            // soon as the sort has used them. Nothing is carried when the query has neither clause
            // to serve, so the commonest grouped query keeps exactly the plan it had.
            var groupedSelectList = orderByClause is { Count: > 0 } || select.HavingClause != null
                ? BuildGroupedSelectList(selectList, select.GroupByClause)
                : selectList;

            var carriesGroupingKeys = groupedSelectList.Count > selectList.Count;

            var having = carriesGroupingKeys && select.HavingClause != null
                ? ResolveCarriedGroupingKeys(select.HavingClause, groupedSelectList, selectList.Count)
                : select.HavingClause;

            // Use full GROUP BY aggregation - stores all rows
            iterator = new IteratorGroupBy(
                iterator,
                select.GroupByClause,
                groupedSelectList,
                m_context,
                having);

            // ORDER BY (after aggregation) - resolve aggregate expressions to result columns
            iterator = ApplyOrderByClauseForAggregate(
                iterator, orderByClause, groupedSelectList, selectList.Count);

            // The carried keys are the planner's business, not the caller's: they are dropped before
            // LIMIT and DISTINCT, so both count and compare the columns the query asked for.
            if (carriesGroupingKeys)
                iterator = new IteratorHideGroupingKeys(iterator, selectList.Count);
        }

        // LIMIT/OFFSET - the union's, when there is one, so Plan applies it after the arms are
        // combined rather than to this one alone.
        if (!setOperationFollows)
            iterator = ApplyLimitClause(iterator, select.LimitCount, select.LimitOffset);

        // DISTINCT (after aggregation). NOT deferred: SQL puts a SELECT DISTINCT on the arm it is
        // written in, not on the set expression.
        iterator = ApplyDistinct(iterator, select.IsDistinct);

        return iterator;
    }

    /// <inheritdoc cref="PlanAggregateQuery"/>
    private IResultIterator PlanNonAggregateQuery(
        IResultIterator iterator,
        WitSqlStatementSelect select,
        bool setOperationFollows = false)
    {
        // Not select.OrderByClause: when this query is one arm of a set operation, the trailing
        // ORDER BY and LIMIT belong to the combined result and Plan applies them after the union.
        var orderByClause = setOperationFollows ? null : select.OrderByClause;

        // Check if query contains window functions
        bool hasWindowFunctions = HasWindowFunctions(select.SelectList);

        if (hasWindowFunctions)
        {
            // Window functions need all data to be processed first
            // Apply ORDER BY before window processing if needed for window functions
            // Note: Window functions have their own ORDER BY in OVER clause
            
            // Window function iterator processes all rows and computes window values
            iterator = new IteratorWindow(iterator, select.SelectList, m_context);
            
            // ORDER BY - apply after window functions for final ordering. IteratorWindow's schema is
            // the select list, so the row here already IS the output and no select list is passed:
            // an ORDER BY position counts columns of it.
            iterator = ApplyOrderByClause(iterator, orderByClause);

            // DISTINCT - after window processing, and BEFORE the limit: SQL evaluates DISTINCT
            // first, so limiting ahead of it truncates the rows the duplicates are drawn from and
            // returns fewer than n distinct values. IteratorDistinct is streaming and yields first
            // occurrences, so the ordering above survives it.
            iterator = ApplyDistinct(iterator, select.IsDistinct);

            // LIMIT/OFFSET - last, over the distinct result. The union's, when there is one, so
            // Plan applies it after the arms are combined rather than to this one alone.
            if (!setOperationFollows)
                iterator = ApplyLimitClause(iterator, select.LimitCount, select.LimitOffset);
        }
        else
        {
            // Original non-window path.
            //
            // A `*` beside another item becomes the columns it stands for FIRST, and the same list
            // then serves the ordering and the projection - an ORDER BY position counts output
            // columns, and after an expansion there are more of them than there are select items.
            // The lone `SELECT *` is deliberately not expanded: ApplyProjection answers that one
            // with IteratorExcludeInternal, and expanding it would give the commonest query in the
            // language a different plan for nothing.
            var selectList = IsSelectStar(select.SelectList)
                ? select.SelectList
                : ExpandStars(select.SelectList, iterator.Schema);

            // ORDER BY - before projection to access original column names, which is why the select
            // list has to come with it: an ORDER BY position counts OUTPUT columns, and the row in
            // front of the sort here is the source's.
            iterator = ApplyOrderByClause(iterator, orderByClause, selectList);

            // Projection (SELECT columns)
            iterator = ApplyProjection(iterator, selectList);

            // DISTINCT - after projection so internal columns (like _rowid) are excluded
            iterator = ApplyDistinct(iterator, select.IsDistinct);

            // LIMIT/OFFSET - last. It used to sit before the projection "for efficiency", which put
            // it ahead of DISTINCT: the limit then truncated the rows the duplicates were drawn
            // from, so SELECT DISTINCT ... LIMIT n returned fewer than n distinct values. And the
            // union's, when there is one, is applied by Plan over the combined result.
            if (!setOperationFollows)
                iterator = ApplyLimitClause(iterator, select.LimitCount, select.LimitOffset);
        }

        return iterator;
    }

    #endregion
}
