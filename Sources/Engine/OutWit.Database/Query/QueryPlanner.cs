using OutWit.Database.Context;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

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
        // Build base iterator from FROM clause
        var iterator = CreateSourceIterator(select);

        // Apply WHERE filter
        iterator = ApplyWhereClause(iterator, select.WhereClause);

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
        // First check if it's a view
        var view = m_context.Database.GetView(simple.TableName);
        if (view != null)
        {
            return CreateViewIterator(view, simple.Alias ?? simple.TableName);
        }

        // Otherwise it's a regular table
        var tableIterator = m_context.Database.CreateTableScan(simple.TableName);
        return WrapWithAlias(tableIterator, simple.Alias ?? simple.TableName);
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

    #endregion
}
