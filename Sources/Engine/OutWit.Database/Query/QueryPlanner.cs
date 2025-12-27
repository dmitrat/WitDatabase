using OutWit.Database.Context;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Values;

namespace OutWit.Database.Query;

/// <summary>
/// Plans SELECT queries by building iterator trees.
/// Implements the Volcano/Iterator model for query execution.
/// </summary>
public sealed class QueryPlanner
{
    #region Constants

    private static readonly HashSet<string> AGGREGATE_FUNCTIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX", "GROUP_CONCAT"
    };

    /// <summary>
    /// Window functions that assign ranking/position to rows.
    /// </summary>
    private static readonly HashSet<string> WINDOW_RANKING_FUNCTIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "PERCENT_RANK", "CUME_DIST"
    };

    /// <summary>
    /// Window functions that access values from other rows.
    /// </summary>
    private static readonly HashSet<string> WINDOW_VALUE_FUNCTIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "FIRST_VALUE", "LAST_VALUE", "NTH_VALUE", "LAG", "LEAD"
    };

    /// <summary>
    /// Maximum recursion depth for recursive CTEs to prevent infinite loops.
    /// </summary>
    private const int MAX_RECURSION_DEPTH = 1000;

    #endregion

    #region Fields

    private readonly ContextExecution m_context;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new query planner.
    /// </summary>
    /// <param name="context">The execution context.</param>
    public QueryPlanner(ContextExecution context)
    {
        m_context = context;
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

        // Build base iterator from FROM clause
        var iterator = CreateSourceIterator(select);

        // Apply WHERE filter
        iterator = ApplyWhereClause(iterator, select.WhereClause);

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

    #region CTE Handling

    /// <summary>
    /// Registers CTE definitions from the WITH clause into the execution context.
    /// </summary>
    private void RegisterCteDefinitions(WitSqlStatementSelect select)
    {
        if (select.CteDefinitions == null || select.CteDefinitions.Count == 0)
            return;

        foreach (var cte in select.CteDefinitions)
        {
            if (m_context.CteDefinitions.ContainsKey(cte.Name))
            {
                throw new InvalidOperationException($"Duplicate CTE name: '{cte.Name}'");
            }

            // For recursive CTEs, we need to mark them specially
            if (select.IsRecursive)
            {
                // Store with a marker that this is a recursive CTE
                m_context.CteDefinitions[cte.Name] = cte;
                m_context.State[$"CTE_RECURSIVE_{cte.Name}"] = true;
            }
            else
            {
                m_context.CteDefinitions[cte.Name] = cte;
            }
        }
    }

    /// <summary>
    /// Tries to resolve a table name as a CTE reference.
    /// </summary>
    /// <param name="tableName">The table name to resolve.</param>
    /// <returns>The CTE definition if found, null otherwise.</returns>
    private ClauseCteDefinition? TryGetCteDefinition(string tableName)
    {
        return m_context.CteDefinitions.TryGetValue(tableName, out var cteDef) 
            ? cteDef 
            : null;
    }

    /// <summary>
    /// Checks if a CTE is marked as recursive.
    /// </summary>
    private bool IsRecursiveCte(string cteName)
    {
        return m_context.State.TryGetValue($"CTE_RECURSIVE_{cteName}", out var value) 
               && value is true;
    }

    /// <summary>
    /// Gets the in-memory working table for recursive CTE execution.
    /// </summary>
    private List<WitSqlRow>? GetRecursiveWorkingTable(string cteName)
    {
        return m_context.State.TryGetValue($"CTE_WORKING_{cteName}", out var value)
            ? value as List<WitSqlRow>
            : null;
    }

    /// <summary>
    /// Gets the schema for the recursive CTE working table.
    /// </summary>
    private IReadOnlyList<WitSqlColumnInfo>? GetRecursiveWorkingTableSchema(string cteName)
    {
        return m_context.State.TryGetValue($"CTE_SCHEMA_{cteName}", out var value)
            ? value as IReadOnlyList<WitSqlColumnInfo>
            : null;
    }

    /// <summary>
    /// Sets the in-memory working table for recursive CTE execution.
    /// </summary>
    private void SetRecursiveWorkingTable(string cteName, List<WitSqlRow> rows, IReadOnlyList<WitSqlColumnInfo>? schema = null)
    {
        m_context.State[$"CTE_WORKING_{cteName}"] = rows;
        if (schema != null)
        {
            m_context.State[$"CTE_SCHEMA_{cteName}"] = schema;
        }
    }

    /// <summary>
    /// Tries to get cached CTE results.
    /// </summary>
    /// <param name="cteName">The CTE name.</param>
    /// <returns>The cached entry if found, null otherwise.</returns>
    private CteCacheEntry? TryGetCachedCte(string cteName)
    {
        return m_context.CteCache.TryGetValue(cteName, out var entry) ? entry : null;
    }

    /// <summary>
    /// Caches CTE results for reuse.
    /// </summary>
    /// <param name="cteName">The CTE name.</param>
    /// <param name="rows">The rows to cache.</param>
    /// <param name="schema">The schema of the cached rows.</param>
    private void CacheCteResults(string cteName, IReadOnlyList<WitSqlRow> rows, IReadOnlyList<WitSqlColumnInfo> schema)
    {
        m_context.CteCache[cteName] = new CteCacheEntry
        {
            Rows = rows,
            Schema = schema
        };
    }

    #endregion

    #region Query Planning

    private IResultIterator PlanAggregateQuery(IResultIterator iterator, WitSqlStatementSelect select)
    {
        // GROUP BY aggregation with integrated HAVING clause
        iterator = new IteratorGroupBy(
            iterator, 
            select.GroupByClause, 
            select.SelectList, 
            m_context,
            select.HavingClause);

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

    #region Clause Application

    private IResultIterator ApplyWhereClause(IResultIterator iterator, WitSqlExpression? whereClause)
    {
        if (whereClause == null)
            return iterator;

        return new IteratorFilter(iterator, whereClause, m_context);
    }

    private IResultIterator ApplyLockingClause(IResultIterator iterator, WitSqlStatementSelect select)
    {
        if (select.ForClause == null || select.ForClause.LockingType == LockingType.None)
            return iterator;

        // FOR UPDATE/SHARE requires an active MVCC transaction
        var transaction = m_context.Database.CurrentTransaction;
        if (transaction == null)
        {
            throw new InvalidOperationException(
                "FOR UPDATE/FOR SHARE requires an active transaction. " +
                "Start a transaction with BEGIN TRANSACTION first.");
        }

        if (transaction is not IMvccTransaction mvccTransaction)
        {
            throw new InvalidOperationException(
                "FOR UPDATE/FOR SHARE requires MVCC transaction support. " +
                "The current transaction type does not support row-level locking.");
        }

        // Get the table name from FROM clause
        var tableName = GetPrimaryTableName(select);
        if (tableName == null)
        {
            throw new InvalidOperationException(
                "FOR UPDATE/FOR SHARE requires a table in the FROM clause.");
        }

        return new IteratorLocking(iterator, select.ForClause, mvccTransaction, tableName);
    }

    private IResultIterator ApplyOrderByClause(IResultIterator iterator, IReadOnlyList<ClauseOrderByItem>? orderByClause)
    {
        if (orderByClause == null || orderByClause.Count == 0)
            return iterator;

        return new IteratorSort(iterator, orderByClause, m_context);
    }

    private IResultIterator ApplyLimitClause(IResultIterator iterator, WitSqlExpression? limitCount, WitSqlExpression? limitOffset)
    {
        if (limitCount == null)
            return iterator;

        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        var limit = evaluator.Evaluate(limitCount, dummyRow).AsInt64();
        var offset = limitOffset != null
            ? evaluator.Evaluate(limitOffset, dummyRow).AsInt64()
            : 0;

        return new IteratorLimit(iterator, limit, offset);
    }

    private IResultIterator ApplyProjection(IResultIterator iterator, IReadOnlyList<ClauseSelectItem> selectList)
    {
        // Skip projection for SELECT *
        if (IsSelectStar(selectList))
            return iterator;

        return new IteratorProject(iterator, selectList, m_context);
    }

    private static IResultIterator ApplyDistinct(IResultIterator iterator, bool isDistinct)
    {
        if (!isDistinct)
            return iterator;

        return new IteratorDistinct(iterator);
    }

    private IResultIterator ApplySetOperations(IResultIterator iterator, WitSqlStatementSelect select)
    {
        if (select.SetOperations == null || select.SetOperations.Count == 0)
            return iterator;

        foreach (var setOp in select.SetOperations)
        {
            var rightIterator = Plan(setOp.RightQuery);
            iterator = CreateSetOperationIterator(iterator, rightIterator, setOp);
        }

        return iterator;
    }

    private static IResultIterator CreateSetOperationIterator(
        IResultIterator left,
        IResultIterator right,
        ClauseSetOperation setOp)
    {
        return setOp.OperationType switch
        {
            SetOperationType.Union => new IteratorUnion(left, right, setOp.IsAll),
            SetOperationType.Intersect => new IteratorIntersect(left, right, setOp.IsAll),
            SetOperationType.Except => new IteratorExcept(left, right, setOp.IsAll),
            _ => throw new NotSupportedException($"Set operation {setOp.OperationType} not supported")
        };
    }

    #endregion

    #region Source Iterators

    private IResultIterator CreateSourceIterator(WitSqlStatementSelect select)
    {
        if (select.FromClause == null || select.FromClause.Count == 0)
        {
            // SELECT without FROM - returns single row
            return new IteratorSingleRow([], []);
        }

        // Start with the first table source
        var iterator = CreateTableSourceIterator(select.FromClause[0]);

        // Handle implicit cross joins (multiple tables in FROM without explicit JOIN)
        for (int i = 1; i < select.FromClause.Count; i++)
        {
            var rightIterator = CreateTableSourceIterator(select.FromClause[i]);
            iterator = new IteratorJoin(iterator, rightIterator, JoinType.Cross, null, m_context);
        }

        return iterator;
    }

    private IResultIterator CreateTableSourceIterator(TableSource source)
    {
        return source switch
        {
            TableSourceSimple simple => CreateSimpleTableIterator(simple),
            TableSourceJoin join => CreateJoinIterator(join),
            TableSourceSubquery subquery => CreateSubqueryIterator(subquery),
            _ => throw new NotSupportedException($"Table source type not supported: {source.GetType().Name}")
        };
    }

    private IResultIterator CreateSimpleTableIterator(TableSourceSimple simple)
    {
        // First check if it's a CTE reference
        var cteDef = TryGetCteDefinition(simple.TableName);
        if (cteDef != null)
        {
            // Check if this is a recursive CTE and we have a working table
            if (IsRecursiveCte(simple.TableName))
            {
                var workingTable = GetRecursiveWorkingTable(simple.TableName);
                if (workingTable != null)
                {
                    // Use the working table for recursive iteration with proper schema
                    var schema = GetRecursiveWorkingTableSchema(simple.TableName);
                    var inMemoryIterator = schema != null
                        ? new IteratorInMemory(workingTable, schema)
                        : new IteratorInMemory(workingTable);
                    return WrapWithAlias(inMemoryIterator, simple.Alias ?? simple.TableName);
                }
                
                // First access to recursive CTE - execute it fully
                return CreateRecursiveCteIterator(cteDef, simple.Alias ?? simple.TableName);
            }
            
            // For non-recursive CTEs, check if we have cached results
            var cached = TryGetCachedCte(simple.TableName);
            if (cached != null)
            {
                // Use cached results
                var cachedIterator = new IteratorInMemory(cached.Rows, cached.Schema);
                return WrapWithAlias(cachedIterator, simple.Alias ?? simple.TableName);
            }
            
            return CreateCteIterator(cteDef, simple.Alias ?? simple.TableName);
        }

        // Then check if it's a view
        var view = m_context.Database.GetView(simple.TableName);
        if (view != null)
        {
            return CreateViewIterator(view, simple.Alias ?? simple.TableName);
        }

        // Otherwise it's a regular table
        var tableIterator = m_context.Database.CreateTableScan(simple.TableName);
        return WrapWithAlias(tableIterator, simple.Alias ?? simple.TableName);
    }

    private IResultIterator CreateCteIterator(ClauseCteDefinition cteDef, string alias)
    {
        // Check if we already have cached results for this CTE
        var cached = TryGetCachedCte(cteDef.Name);
        if (cached != null)
        {
            // Use cached results
            var cachedIterator = new IteratorInMemory(cached.Rows, cached.Schema);
            return WrapWithAlias(cachedIterator, alias);
        }
        
        // Execute CTE and cache results
        var cteIterator = Plan(cteDef.Query);
        
        // If CTE has explicit column names, rename the columns
        if (cteDef.ColumnNames != null && cteDef.ColumnNames.Count > 0)
        {
            cteIterator = new IteratorColumnRename(cteIterator, cteDef.ColumnNames);
        }
        
        // Materialize the CTE results into memory and cache them
        var rows = new List<WitSqlRow>();
        cteIterator.Open();
        try
        {
            // Get schema after opening (schema might be built during Open)
            var schema = cteIterator.Schema;
            
            while (cteIterator.MoveNext())
            {
                rows.Add(cteIterator.Current);
            }
            
            // Cache the results
            CacheCteResults(cteDef.Name, rows, schema);
            
            // Return iterator over cached results
            var cachedIterator = new IteratorInMemory(rows, schema);
            return WrapWithAlias(cachedIterator, alias);
        }
        finally
        {
            cteIterator.Dispose();
        }
    }

    private IResultIterator CreateRecursiveCteIterator(ClauseCteDefinition cteDef, string alias)
    {
        var cteQuery = cteDef.Query;
        
        // Recursive CTE must have UNION ALL
        if (cteQuery.SetOperations == null || cteQuery.SetOperations.Count == 0)
        {
            throw new InvalidOperationException(
                $"Recursive CTE '{cteDef.Name}' must contain UNION ALL");
        }

        var setOp = cteQuery.SetOperations[0];
        if (!setOp.IsAll)
        {
            throw new InvalidOperationException(
                $"Recursive CTE '{cteDef.Name}' requires UNION ALL, not UNION");
        }

        // Step 1: Execute anchor member (the base SELECT before UNION ALL)
        var anchorQuery = new WitSqlStatementSelect
        {
            IsDistinct = cteQuery.IsDistinct,
            IsRecursive = false,
            CteDefinitions = null,
            SelectList = cteQuery.SelectList,
            FromClause = cteQuery.FromClause,
            WhereClause = cteQuery.WhereClause,
            GroupByClause = cteQuery.GroupByClause,
            HavingClause = cteQuery.HavingClause
        };

        var allRows = new List<WitSqlRow>();
        var workingTable = new List<WitSqlRow>();
        IReadOnlyList<WitSqlColumnInfo>? schema = null;
        
        // Execute anchor
        var anchorIterator = Plan(anchorQuery);
        anchorIterator.Open();
        try
        {
            schema = anchorIterator.Schema;
            
            while (anchorIterator.MoveNext())
            {
                var row = RenameRowColumns(anchorIterator.Current, cteDef.ColumnNames);
                workingTable.Add(row);
                allRows.Add(row);
            }
        }
        finally
        {
            anchorIterator.Dispose();
        }

        // Update schema with renamed columns if needed
        if (cteDef.ColumnNames != null && cteDef.ColumnNames.Count > 0 && schema != null)
        {
            schema = RenameSchemaColumns(schema, cteDef.ColumnNames);
        }

        // Step 2: Iteratively execute recursive member
        var recursiveQuery = setOp.RightQuery;
        int recursionDepth = 0;

        while (workingTable.Count > 0 && recursionDepth < MAX_RECURSION_DEPTH)
        {
            recursionDepth++;
            
            // Set working table for recursive reference with schema
            SetRecursiveWorkingTable(cteDef.Name, workingTable, schema);
            
            var newRows = new List<WitSqlRow>();
            
            // Execute recursive member
            var recursiveIterator = Plan(recursiveQuery);
            recursiveIterator.Open();
            try
            {
                while (recursiveIterator.MoveNext())
                {
                    var row = RenameRowColumns(recursiveIterator.Current, cteDef.ColumnNames);
                    newRows.Add(row);
                    allRows.Add(row);
                }
            }
            finally
            {
                recursiveIterator.Dispose();
            }

            // Swap working table for next iteration
            workingTable = newRows;
        }

        // Clear working table state
        m_context.State.Remove($"CTE_WORKING_{cteDef.Name}");
        m_context.State.Remove($"CTE_SCHEMA_{cteDef.Name}");

        if (recursionDepth >= MAX_RECURSION_DEPTH)
        {
            throw new InvalidOperationException(
                $"Recursive CTE '{cteDef.Name}' exceeded maximum recursion depth of {MAX_RECURSION_DEPTH}");
        }

        // Cache the results for potential reuse
        if (schema != null)
        {
            CacheCteResults(cteDef.Name, allRows, schema);
        }

        // Return in-memory iterator over all collected rows
        var resultIterator = new IteratorInMemory(allRows, schema ?? []);
        return WrapWithAlias(resultIterator, alias);
    }

    private static WitSqlRow RenameRowColumns(WitSqlRow row, IReadOnlyList<string>? newNames)
    {
        if (newNames == null || newNames.Count == 0)
            return row;

        var names = new string[row.ColumnCount];
        for (int i = 0; i < row.ColumnCount; i++)
        {
            names[i] = i < newNames.Count ? newNames[i] : row.ColumnNames[i];
        }

        return new WitSqlRow(row.Values.ToArray(), names);
    }

    private static IReadOnlyList<WitSqlColumnInfo> RenameSchemaColumns(
        IReadOnlyList<WitSqlColumnInfo> schema, 
        IReadOnlyList<string> newNames)
    {
        var result = new List<WitSqlColumnInfo>(schema.Count);
        
        for (int i = 0; i < schema.Count; i++)
        {
            var newName = i < newNames.Count ? newNames[i] : schema[i].Name;
            result.Add(new WitSqlColumnInfo
            {
                Name = newName,
                Type = schema[i].Type,
                IsNullable = schema[i].IsNullable,
                TableName = schema[i].TableName
            });
        }
        
        return result;
    }

    private IResultIterator CreateViewIterator(DefinitionView view, string alias)
    {
        // Parse and plan the view's SELECT statement
        var viewSelect = Parser.WitSql.ParseStatement(view.SelectSql) as WitSqlStatementSelect
            ?? throw new InvalidOperationException($"View '{view.Name}' contains invalid SELECT statement");

        // Recursively plan the view query
        var viewIterator = Plan(viewSelect);
        return WrapWithAlias(viewIterator, alias);
    }

    private IResultIterator CreateJoinIterator(TableSourceJoin join)
    {
        var left = CreateTableSourceIterator(join.Left);
        var right = CreateTableSourceIterator(join.Right);
        return new IteratorJoin(left, right, join.JoinType, join.OnCondition, m_context);
    }

    private IResultIterator CreateSubqueryIterator(TableSourceSubquery subquery)
    {
        var subqueryIterator = Plan(subquery.Subquery);
        var alias = subquery.Alias ?? throw new InvalidOperationException("Subquery in FROM must have an alias");
        return WrapWithAlias(subqueryIterator, alias);
    }

    private static IResultIterator WrapWithAlias(IResultIterator iterator, string alias)
    {
        return new IteratorAlias(iterator, alias);
    }

    #endregion

    #region Helper Methods

    private bool IsAggregateQuery(WitSqlStatementSelect select)
    {
        // Query is aggregate if it has GROUP BY or any aggregate function in SELECT (without OVER)
        if (select.GroupByClause != null && select.GroupByClause.Count > 0)
            return true;

        return HasNonWindowAggregates(select.SelectList);
    }

    private static bool HasNonWindowAggregates(IReadOnlyList<ClauseSelectItem> selectList)
    {
        foreach (var item in selectList)
        {
            if (ContainsNonWindowAggregateFunction(item.Expression))
                return true;
        }
        return false;
    }

    private static bool ContainsNonWindowAggregateFunction(WitSqlExpression? expression)
    {
        if (expression == null)
            return false;

        return expression switch
        {
            // Aggregate function WITHOUT OVER clause = regular aggregate
            WitSqlExpressionFunctionCall func => 
                AGGREGATE_FUNCTIONS.Contains(func.FunctionName) && func.Over == null,
            WitSqlExpressionBinary binary => 
                ContainsNonWindowAggregateFunction(binary.Left) || ContainsNonWindowAggregateFunction(binary.Right),
            WitSqlExpressionUnary unary => 
                ContainsNonWindowAggregateFunction(unary.Operand),
            WitSqlExpressionCase caseExpr => 
                ContainsNonWindowAggregateFunctionInCase(caseExpr),
            _ => false
        };
    }

    private static bool ContainsNonWindowAggregateFunctionInCase(WitSqlExpressionCase caseExpr)
    {
        if (ContainsNonWindowAggregateFunction(caseExpr.Operand))
            return true;

        foreach (var whenClause in caseExpr.WhenClauses)
        {
            if (ContainsNonWindowAggregateFunction(whenClause.When) || 
                ContainsNonWindowAggregateFunction(whenClause.Then))
                return true;
        }

        return ContainsNonWindowAggregateFunction(caseExpr.ElseResult);
    }

    /// <summary>
    /// Checks if the SELECT list contains any window functions.
    /// </summary>
    private static bool HasWindowFunctions(IReadOnlyList<ClauseSelectItem> selectList)
    {
        foreach (var item in selectList)
        {
            if (ContainsWindowFunction(item.Expression))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursively checks if an expression contains a window function.
    /// </summary>
    private static bool ContainsWindowFunction(WitSqlExpression? expression)
    {
        if (expression == null)
            return false;

        return expression switch
        {
            WitSqlExpressionFunctionCall func => IsWindowFunction(func),
            WitSqlExpressionBinary binary => 
                ContainsWindowFunction(binary.Left) || ContainsWindowFunction(binary.Right),
            WitSqlExpressionUnary unary => 
                ContainsWindowFunction(unary.Operand),
            WitSqlExpressionCase caseExpr => 
                ContainsWindowFunctionInCase(caseExpr),
            _ => false
        };
    }

    private static bool ContainsWindowFunctionInCase(WitSqlExpressionCase caseExpr)
    {
        if (ContainsWindowFunction(caseExpr.Operand))
            return true;

        foreach (var whenClause in caseExpr.WhenClauses)
        {
            if (ContainsWindowFunction(whenClause.When) || 
                ContainsWindowFunction(whenClause.Then))
                return true;
        }

        return ContainsWindowFunction(caseExpr.ElseResult);
    }

    /// <summary>
    /// Determines if a function call is a window function.
    /// A window function is either:
    /// 1. A ranking function (ROW_NUMBER, RANK, etc.) with OVER clause
    /// 2. A value function (LAG, LEAD, etc.) with OVER clause
    /// 3. An aggregate function with OVER clause
    /// </summary>
    private static bool IsWindowFunction(WitSqlExpressionFunctionCall func)
    {
        // Must have OVER clause to be a window function
        if (func.Over == null)
            return false;

        var funcName = func.FunctionName.ToUpperInvariant();

        // Check if it's a ranking or value window function
        if (WINDOW_RANKING_FUNCTIONS.Contains(funcName) || 
            WINDOW_VALUE_FUNCTIONS.Contains(funcName))
            return true;

        // Aggregate functions with OVER are also window functions
        if (AGGREGATE_FUNCTIONS.Contains(funcName))
            return true;

        return false;
    }

    private static bool HasAggregates(IReadOnlyList<ClauseSelectItem> selectList)
    {
        foreach (var item in selectList)
        {
            if (ContainsAggregateFunction(item.Expression))
                return true;
        }
        return false;
    }

    private static bool ContainsAggregateFunction(WitSqlExpression? expression)
    {
        if (expression == null)
            return false;

        return expression switch
        {
            WitSqlExpressionFunctionCall func => AGGREGATE_FUNCTIONS.Contains(func.FunctionName),
            WitSqlExpressionBinary binary => ContainsAggregateFunction(binary.Left) || ContainsAggregateFunction(binary.Right),
            WitSqlExpressionUnary unary => ContainsAggregateFunction(unary.Operand),
            WitSqlExpressionCase caseExpr => ContainsAggregateFunctionInCase(caseExpr),
            _ => false
        };
    }

    private static bool ContainsAggregateFunctionInCase(WitSqlExpressionCase caseExpr)
    {
        if (ContainsAggregateFunction(caseExpr.Operand))
            return true;

        foreach (var whenClause in caseExpr.WhenClauses)
        {
            if (ContainsAggregateFunction(whenClause.When) || ContainsAggregateFunction(whenClause.Then))
                return true;
        }

        return ContainsAggregateFunction(caseExpr.ElseResult);
    }

    private static bool IsSelectStar(IReadOnlyList<ClauseSelectItem> selectList)
    {
        return selectList.Count == 1 && selectList[0].IsStar;
    }

    /// <summary>
    /// Gets the primary table name from the FROM clause for locking purposes.
    /// For simple queries, returns the first table. For joins, returns the leftmost table.
    /// </summary>
    private static string? GetPrimaryTableName(WitSqlStatementSelect select)
    {
        if (select.FromClause == null || select.FromClause.Count == 0)
            return null;

        return GetTableNameFromSource(select.FromClause[0]);
    }

    private static string? GetTableNameFromSource(TableSource source)
    {
        return source switch
        {
            TableSourceSimple simple => simple.TableName,
            TableSourceJoin join => GetTableNameFromSource(join.Left),
            TableSourceSubquery => null, // Subqueries don't have a table to lock
            _ => null
        };
    }

    #endregion
}
