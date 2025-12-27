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
    /// Sets the in-memory working table for recursive CTE execution.
    /// </summary>
    private void SetRecursiveWorkingTable(string cteName, List<WitSqlRow> rows)
    {
        m_context.State[$"CTE_WORKING_{cteName}"] = rows;
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
        // ORDER BY - before projection to access original column names
        iterator = ApplyOrderByClause(iterator, select.OrderByClause);

        // LIMIT/OFFSET - before projection for efficiency
        iterator = ApplyLimitClause(iterator, select.LimitCount, select.LimitOffset);

        // Projection (SELECT columns)
        iterator = ApplyProjection(iterator, select.SelectList);

        // DISTINCT - after projection so internal columns (like _rowid) are excluded
        iterator = ApplyDistinct(iterator, select.IsDistinct);

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
                    // Use the working table for recursive iteration
                    var inMemoryIterator = new IteratorInMemory(workingTable);
                    return WrapWithAlias(inMemoryIterator, simple.Alias ?? simple.TableName);
                }
                
                // First access to recursive CTE - execute it fully
                return CreateRecursiveCteIterator(cteDef, simple.Alias ?? simple.TableName);
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
        // Plan the CTE query (recursively handles nested CTEs)
        var cteIterator = Plan(cteDef.Query);
        
        // If CTE has explicit column names, rename the columns
        if (cteDef.ColumnNames != null && cteDef.ColumnNames.Count > 0)
        {
            cteIterator = new IteratorColumnRename(cteIterator, cteDef.ColumnNames);
        }
        
        return WrapWithAlias(cteIterator, alias);
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
        
        // Execute anchor
        var anchorIterator = Plan(anchorQuery);
        anchorIterator.Open();
        try
        {
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

        // Step 2: Iteratively execute recursive member
        var recursiveQuery = setOp.RightQuery;
        int recursionDepth = 0;

        while (workingTable.Count > 0 && recursionDepth < MAX_RECURSION_DEPTH)
        {
            recursionDepth++;
            
            // Set working table for recursive reference
            SetRecursiveWorkingTable(cteDef.Name, workingTable);
            
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

        if (recursionDepth >= MAX_RECURSION_DEPTH)
        {
            throw new InvalidOperationException(
                $"Recursive CTE '{cteDef.Name}' exceeded maximum recursion depth of {MAX_RECURSION_DEPTH}");
        }

        // Return in-memory iterator over all collected rows
        var resultIterator = new IteratorInMemory(allRows);
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
        // Query is aggregate if it has GROUP BY or any aggregate function in SELECT
        if (select.GroupByClause != null && select.GroupByClause.Count > 0)
            return true;

        return HasAggregates(select.SelectList);
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
