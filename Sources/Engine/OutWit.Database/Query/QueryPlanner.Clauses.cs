using OutWit.Database.Core.Interfaces;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Query;

/// <summary>
/// SQL clause application for QueryPlanner (WHERE, ORDER BY, LIMIT, etc.).
/// </summary>
public sealed partial class QueryPlanner
{
    #region WHERE Clause

    private IResultIterator ApplyWhereClause(IResultIterator iterator, WitSqlExpression? whereClause, WitSqlStatementSelect? select = null)
    {
        if (whereClause == null)
            return iterator;

        // Check if we already used an index - in that case, we might still need
        // residual filtering for predicates not covered by the index
        // The index iterator handles the indexed predicate, but other predicates need filtering
        
        return new IteratorFilter(iterator, whereClause, m_context);
    }

    #endregion

    #region FOR UPDATE/SHARE Locking

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

    #endregion

    #region ORDER BY Clause

    private IResultIterator ApplyOrderByClause(IResultIterator iterator, IReadOnlyList<ClauseOrderByItem>? orderByClause)
    {
        if (orderByClause == null || orderByClause.Count == 0)
            return iterator;

        return new IteratorSort(iterator, orderByClause, m_context);
    }

    /// <summary>
    /// Applies ORDER BY for aggregate queries, resolving aggregate expressions to result columns.
    /// </summary>
    /// <param name="iterator">The source iterator (after GROUP BY).</param>
    /// <param name="orderByClause">The ORDER BY clause.</param>
    /// <param name="selectList">The SELECT list (contains computed aggregate columns).</param>
    /// <returns>Iterator with ORDER BY applied.</returns>
    private IResultIterator ApplyOrderByClauseForAggregate(
        IResultIterator iterator, 
        IReadOnlyList<ClauseOrderByItem>? orderByClause,
        IReadOnlyList<ClauseSelectItem> selectList)
    {
        if (orderByClause == null || orderByClause.Count == 0)
            return iterator;

        // Transform ORDER BY expressions: replace aggregate functions with column references
        // to the result columns from GROUP BY
        var resolvedOrderBy = ResolveAggregateOrderBy(orderByClause, selectList);
        
        return new IteratorSort(iterator, resolvedOrderBy, m_context);
    }

    /// <summary>
    /// Resolves aggregate expressions in ORDER BY to column references from the SELECT list.
    /// For example: ORDER BY SUM(Amount) DESC -> ORDER BY column_index_2 DESC
    /// </summary>
    private static List<ClauseOrderByItem> ResolveAggregateOrderBy(
        IReadOnlyList<ClauseOrderByItem> orderByClause,
        IReadOnlyList<ClauseSelectItem> selectList)
    {
        var resolved = new List<ClauseOrderByItem>(orderByClause.Count);

        foreach (var orderItem in orderByClause)
        {
            var resolvedExpr = ResolveAggregateExpression(orderItem.Expression, selectList);
            
            resolved.Add(new ClauseOrderByItem
            {
                Expression = resolvedExpr,
                Descending = orderItem.Descending,
                NullsOrder = orderItem.NullsOrder
            });
        }

        return resolved;
    }

    /// <summary>
    /// Resolves an expression that may contain aggregates to use result column references.
    /// </summary>
    private static WitSqlExpression ResolveAggregateExpression(
        WitSqlExpression expr,
        IReadOnlyList<ClauseSelectItem> selectList)
    {
        // If expression is aggregate function, find matching column in SELECT
        if (expr is WitSqlExpressionFunctionCall func && IsAggregateFunction(func))
        {
            // Find matching aggregate in SELECT list by index
            for (int i = 0; i < selectList.Count; i++)
            {
                var selectItem = selectList[i];
                if (selectItem.Expression is WitSqlExpressionFunctionCall selectFunc &&
                    AggregateExpressionsMatch(func, selectFunc))
                {
                    // Return column reference by position (use 0-based index marker)
                    return new WitSqlExpressionOrderByColumnIndex { ColumnIndex = i };
                }
            }
            
            // Aggregate not found in SELECT - return the original expression
            // This will cause a runtime error, but allows for debugging
            return expr;
        }

        // If expression is column reference, check if it matches a SELECT column
        if (expr is WitSqlExpressionColumnRef colRef)
        {
            // Try to find by alias or column name
            for (int i = 0; i < selectList.Count; i++)
            {
                var selectItem = selectList[i];
                
                // Match by alias
                if (selectItem.Alias != null &&
                    string.Equals(selectItem.Alias, colRef.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    return new WitSqlExpressionOrderByColumnIndex { ColumnIndex = i };
                }
                
                // Match by column expression
                if (selectItem.Expression is WitSqlExpressionColumnRef selectCol &&
                    string.Equals(selectCol.ColumnName, colRef.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    return new WitSqlExpressionOrderByColumnIndex { ColumnIndex = i };
                }
            }
            
            // Column might be a GROUP BY key that's in the result - check schema names
            return expr;
        }

        // For binary/unary expressions, recursively resolve
        if (expr is WitSqlExpressionBinary binary)
        {
            return new WitSqlExpressionBinary
            {
                Left = ResolveAggregateExpression(binary.Left, selectList),
                Operator = binary.Operator,
                Right = ResolveAggregateExpression(binary.Right, selectList)
            };
        }

        if (expr is WitSqlExpressionUnary unary)
        {
            return new WitSqlExpressionUnary
            {
                Operand = ResolveAggregateExpression(unary.Operand, selectList),
                Operator = unary.Operator
            };
        }

        // Other expressions (literals, etc.) pass through
        return expr;
    }

    /// <summary>
    /// Checks if two aggregate function calls are equivalent.
    /// </summary>
    private static bool AggregateExpressionsMatch(WitSqlExpressionFunctionCall a, WitSqlExpressionFunctionCall b)
    {
        // Must have same function name
        if (!string.Equals(a.FunctionName, b.FunctionName, StringComparison.OrdinalIgnoreCase))
            return false;

        // Both must be star or both not
        if (a.IsStar != b.IsStar)
            return false;

        // Check DISTINCT modifier
        if (a.IsDistinct != b.IsDistinct)
            return false;

        // COUNT(*) matches COUNT(*)
        if (a.IsStar)
            return true;

        // Compare arguments
        var argsA = a.Arguments ?? [];
        var argsB = b.Arguments ?? [];

        if (argsA.Count != argsB.Count)
            return false;

        for (int i = 0; i < argsA.Count; i++)
        {
            if (!ExpressionsMatch(argsA[i], argsB[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if two expressions are structurally equivalent.
    /// </summary>
    private static bool ExpressionsMatch(WitSqlExpression a, WitSqlExpression b)
    {
        if (a.GetType() != b.GetType())
            return false;

        return (a, b) switch
        {
            (WitSqlExpressionColumnRef colA, WitSqlExpressionColumnRef colB) =>
                string.Equals(colA.ColumnName, colB.ColumnName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(colA.TableName ?? "", colB.TableName ?? "", StringComparison.OrdinalIgnoreCase),

            (WitSqlExpressionLiteral litA, WitSqlExpressionLiteral litB) =>
                litA.Type == litB.Type && Equals(litA.Value, litB.Value),

            (WitSqlExpressionFunctionCall funcA, WitSqlExpressionFunctionCall funcB) =>
                AggregateExpressionsMatch(funcA, funcB),

            _ => false
        };
    }

    /// <summary>
    /// Gets the generated column name for an aggregate function.
    /// </summary>
    private static string GetAggregateColumnName(WitSqlExpressionFunctionCall func, int index)
    {
        // This should match the naming in IteratorGroupBy.BuildSchema()
        return func.FunctionName;
    }

    /// <summary>
    /// Checks if a function call is an aggregate function.
    /// </summary>
    private static bool IsAggregateFunction(WitSqlExpressionFunctionCall func)
    {
        var name = func.FunctionName.ToUpperInvariant();
        return name is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" or "GROUP_CONCAT" or "STRING_AGG" or "ARRAY_AGG";
    }

    #endregion

    #region LIMIT/OFFSET Clause

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

    #endregion

    #region Projection

    private IResultIterator ApplyProjection(IResultIterator iterator, IReadOnlyList<ClauseSelectItem> selectList)
    {
        // For SELECT *, we need to exclude internal columns like _rowid
        if (IsSelectStar(selectList))
        {
            // Only wrap if the source contains internal columns
            if (IteratorExcludeInternal.NeedsFiltering(iterator.Schema))
            {
                return new IteratorExcludeInternal(iterator);
            }
            return iterator;
        }

        return new IteratorProject(iterator, selectList, m_context);
    }

    #endregion

    #region DISTINCT

    private static IResultIterator ApplyDistinct(IResultIterator iterator, bool isDistinct)
    {
        if (!isDistinct)
            return iterator;

        return new IteratorDistinct(iterator);
    }

    #endregion

    #region Set Operations (UNION, INTERSECT, EXCEPT)

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
}
