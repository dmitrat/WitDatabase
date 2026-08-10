using OutWit.Database.Core.Interfaces;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Serializers;
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

    /// <summary>
    /// Applies ORDER BY over rows that have NOT been projected yet, which is where this clause runs
    /// for an ordinary query - the row is still the source's, under the source's column names.
    /// </summary>
    private IResultIterator ApplyOrderByClause(
        IResultIterator iterator,
        IReadOnlyList<ClauseOrderByItem>? orderByClause,
        IReadOnlyList<ClauseSelectItem>? selectList = null)
    {
        if (orderByClause == null || orderByClause.Count == 0)
            return iterator;

        var resolved = selectList == null
            ? ResolveOrdinalPositions(orderByClause, position =>
                ResolveAgainstProjectedRow(position, iterator.Schema.Count))
            : ResolveOrdinalPositions(orderByClause, position =>
                ResolveAgainstSelectList(position, selectList, iterator.Schema));

        return new IteratorSort(iterator, resolved, m_context);
    }

    /// <summary>
    /// Applies ORDER BY for aggregate queries, resolving aggregate expressions to result columns.
    /// </summary>
    /// <param name="iterator">The source iterator (after GROUP BY).</param>
    /// <param name="orderByClause">The ORDER BY clause.</param>
    /// <param name="selectList">The list the grouped row is built from - the query's own, plus any
    /// grouping keys carried for this clause's benefit.</param>
    /// <param name="visibleColumnCount">How many of those columns the query actually returns, which
    /// is what an <c>ORDER BY &lt;position&gt;</c> counts.</param>
    /// <returns>Iterator with ORDER BY applied.</returns>
    private IResultIterator ApplyOrderByClauseForAggregate(
        IResultIterator iterator,
        IReadOnlyList<ClauseOrderByItem>? orderByClause,
        IReadOnlyList<ClauseSelectItem> selectList,
        int visibleColumnCount)
    {
        if (orderByClause == null || orderByClause.Count == 0)
            return iterator;

        // A grouped row IS the projection, so a position is simply a column of it. Done first, so
        // that what the resolution below sees is the same shape whether the user wrote the position
        // or the expression.
        var positioned = ResolveOrdinalPositions(orderByClause, position =>
            ResolveAgainstProjectedRow(position, visibleColumnCount));

        // Transform ORDER BY expressions: replace aggregate functions with column references
        // to the result columns from GROUP BY
        var resolvedOrderBy = ResolveAggregateOrderBy(positioned, selectList);

        return new IteratorSort(iterator, resolvedOrderBy, m_context);
    }

    #endregion

    #region ORDER BY &lt;position&gt;

    /// <summary>
    /// Rewrites every bare integer in an <c>ORDER BY</c> - a 1-based OUTPUT COLUMN POSITION - into
    /// something that names the column it stands for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>Docs/KnownIssues.md</c> 16.</b> The parser makes the integer an ordinary literal and
    /// nothing turned it into a position, so <see cref="IteratorSort"/> evaluated it once per row:
    /// every row answered the same number, every comparison was equal, and the sort was a no-op.
    /// <c>ORDER BY 1</c> answered exactly what the query without any <c>ORDER BY</c> answers, and
    /// <c>ORDER BY 2 DESC</c> was not a descending sort either. PostgreSQL, SQL Server and SQLite all
    /// implement the form, so a query written for any of them was quietly answered in the wrong
    /// order. Measured 2026-08-10.
    /// </para>
    /// <para>
    /// <b>There are two resolutions because the clause runs in two different places.</b> Over a
    /// grouped, windowed or <c>VALUES</c> result the row already IS the output, so a position is a
    /// column of it. For an ordinary query the sort runs BEFORE the projection - deliberately, so it
    /// can reach the source's own column names - and there a position has to become the N-th select
    /// item's own expression instead.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ClauseOrderByItem> ResolveOrdinalPositions(
        IReadOnlyList<ClauseOrderByItem> orderByClause,
        Func<long, WitSqlExpression> resolve)
    {
        List<ClauseOrderByItem>? rewritten = null;

        for (var i = 0; i < orderByClause.Count; i++)
        {
            if (ReadOrdinalPosition(orderByClause[i].Expression) is not { } position)
                continue;

            rewritten ??= [.. orderByClause];
            rewritten[i] = new ClauseOrderByItem
            {
                Expression = resolve(position),
                Descending = orderByClause[i].Descending,
                NullsOrder = orderByClause[i].NullsOrder
            };
        }

        return rewritten ?? orderByClause;
    }

    /// <summary>
    /// The position an <c>ORDER BY</c> item names, or null when it names something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a BARE integer literal is a position, and a leading sign belongs to it. <c>ORDER BY 1 +
    /// 1</c> is an expression and <c>ORDER BY '1'</c> is a string constant; both keep the behaviour
    /// they had, which is a constant that sorts nothing. <b>Measured against SQLite</b> rather than
    /// assumed: it answers those two in insertion order and refuses <c>-1</c>, <c>0</c> and <c>99</c>
    /// with "1st ORDER BY term out of range".
    /// </para>
    /// <para>
    /// Which is why the sign is read here at all: the parser gives <c>-1</c> as a unary negation over
    /// a literal, so without this it would fall through as an expression and be the same silent
    /// no-op this issue is about. A zero or a negative IS a position, and is refused as one.
    /// </para>
    /// </remarks>
    private static long? ReadOrdinalPosition(WitSqlExpression? expression)
    {
        var sign = 1;

        if (expression is WitSqlExpressionUnary { Operator: UnaryOperatorType.Negate or UnaryOperatorType.Plus } unary)
        {
            sign = unary.Operator == UnaryOperatorType.Negate ? -1 : 1;
            expression = unary.Operand;
        }

        if (expression is not WitSqlExpressionLiteral { Type: LiteralType.Integer } literal)
            return null;

        return literal.Value switch
        {
            long value => sign * value,
            int value => sign * value,
            _ => null
        };
    }

    /// <summary>
    /// A position over a row that is already the query's output.
    /// </summary>
    private static WitSqlExpression ResolveAgainstProjectedRow(long position, int columnCount)
    {
        if (position < 1 || position > columnCount)
            throw PositionIsNotInTheSelectList(position, columnCount);

        return new WitSqlExpressionOrderByColumnIndex { ColumnIndex = (int)(position - 1) };
    }

    /// <summary>
    /// A position over a row that has not been projected yet: it becomes the N-th select item's own
    /// expression, which is what the user would have had to write instead.
    /// </summary>
    private static WitSqlExpression ResolveAgainstSelectList(
        long position,
        IReadOnlyList<ClauseSelectItem> selectList,
        IReadOnlyList<WitSqlColumnInfo> sourceSchema)
    {
        // SELECT * is the one shape whose output columns are not its select list: they are the
        // source's own, minus the internal ones. The sort has the source row in front of it here, so
        // naming that column is enough.
        if (IsSelectStar(selectList))
        {
            var visible = sourceSchema
                .Where(column => !IteratorExcludeInternal.IsInternalColumn(column.Name))
                .ToArray();

            if (position < 1 || position > visible.Length)
                throw PositionIsNotInTheSelectList(position, visible.Length);

            return new WitSqlExpressionColumnRef { ColumnName = visible[(int)(position - 1)].Name };
        }

        if (position < 1 || position > selectList.Count)
            throw PositionIsNotInTheSelectList(position, selectList.Count);

        var item = selectList[(int)(position - 1)];

        if (item.Expression != null)
            return item.Expression;

        // A star sharing its select list with other items. The engine does not expand one there at
        // all - it writes a single NULL for it, `Docs/KnownIssues.md` 17 - so there is no column for
        // the position to name, and saying so is better than sorting by the NULL.
        throw new InvalidOperationException(
            $"ORDER BY position {position} refers to a `*` in a select list that has other items "
            + "beside it, and this engine does not expand a star there. Name the column instead.");
    }

    private static InvalidOperationException PositionIsNotInTheSelectList(long position, int columnCount) =>
        new($"ORDER BY position {position} is not in the select list - the query returns "
            + $"{columnCount} column(s).");

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
        // THE SAME EXPRESSION AS A SELECT ITEM, whatever shape it is. A grouped row carries only the
        // SELECT list, so an ORDER BY that names anything else is evaluated against a row that does
        // not have it - "Column 'DeviceType' not found". The cases below know two shapes, an
        // aggregate call and a bare column; everything else fell through unchanged, and
        // ORDER BY CAST(x AS TEXT) over GROUP BY x is what EF Core emits for
        // `GroupBy(x => x.Type).Select(g => g.Key.ToString())` - the shape of KnownIssues 3. Found
        // 2026-08-09 by running that query rather than by reading this method.
        var matched = MatchSelectItem(expr, selectList);

        if (matched >= 0)
            return new WitSqlExpressionOrderByColumnIndex { ColumnIndex = matched };

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
    /// The SELECT list a grouped row is built from: the query's own, with every GROUP BY expression
    /// that is not selected already appended to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what makes a grouping column reachable from <c>ORDER BY</c> and <c>HAVING</c></b> -
    /// <c>Docs/KnownIssues.md</c> 15. A grouped row used to be exactly the SELECT list, so
    /// <c>SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind</c> sorted rows that have no
    /// <c>Kind</c>; the user saw .NET's own "Failed to compare two elements in the array". All three
    /// target databases accept both shapes.
    /// </para>
    /// <para>
    /// The carried columns keep their natural names, which is what lets an expression OVER a
    /// grouping column - <c>ORDER BY UPPER(Kind)</c> - resolve by ordinary evaluation without the
    /// planner having to recurse into every node type. <see cref="IteratorHideGroupingKeys"/> drops
    /// them again after the sort.
    /// </para>
    /// <para>
    /// Only expressions the serializer can identify are carried: <see cref="Render"/> renders a
    /// subquery as the literal <c>SELECT ...</c>, so two different ones cannot be told apart, and a
    /// grouping key carried under a name that matches the wrong expression would order by the wrong
    /// column. Those keep the old behaviour rather than gain a wrong answer.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ClauseSelectItem> BuildGroupedSelectList(
        IReadOnlyList<ClauseSelectItem> selectList,
        IReadOnlyList<WitSqlExpression>? groupByClause)
    {
        if (groupByClause == null || groupByClause.Count == 0)
            return selectList;

        List<ClauseSelectItem>? carried = null;

        foreach (var expression in groupByClause)
        {
            if (expression == null || Render(expression) == null)
                continue;

            if (MatchSelectItem(expression, carried ?? selectList) >= 0)
                continue;

            carried ??= [.. selectList];
            carried.Add(new ClauseSelectItem { Expression = expression });
        }

        return carried ?? selectList;
    }

    /// <summary>
    /// Rewrites every occurrence of a CARRIED grouping expression to a reference to the column it is
    /// carried in, so that <c>HAVING</c> reaches it the same way <c>ORDER BY</c> does.
    /// </summary>
    /// <remarks>
    /// Only the carried items are matched - the ones appended by
    /// <see cref="BuildGroupedSelectList"/>, which did not exist until this query was planned. So the
    /// rewrite is additive by construction: every shape that resolved before resolves the same way,
    /// and the only expressions it can change are the ones that used to throw.
    /// </remarks>
    private static WitSqlExpression ResolveCarriedGroupingKeys(
        WitSqlExpression expression,
        IReadOnlyList<ClauseSelectItem> groupedSelectList,
        int firstCarried)
    {
        var text = Render(expression);

        if (text != null)
        {
            for (var i = firstCarried; i < groupedSelectList.Count; i++)
            {
                if (groupedSelectList[i].Expression is { } key && Render(key) == text)
                    return new WitSqlExpressionOrderByColumnIndex { ColumnIndex = i };
            }
        }

        if (expression is WitSqlExpressionBinary binary)
        {
            return new WitSqlExpressionBinary
            {
                Left = ResolveCarriedGroupingKeys(binary.Left, groupedSelectList, firstCarried),
                Operator = binary.Operator,
                Right = ResolveCarriedGroupingKeys(binary.Right, groupedSelectList, firstCarried)
            };
        }

        if (expression is WitSqlExpressionUnary unary)
        {
            return new WitSqlExpressionUnary
            {
                Operand = ResolveCarriedGroupingKeys(unary.Operand, groupedSelectList, firstCarried),
                Operator = unary.Operator
            };
        }

        return expression;
    }

    /// <summary>
    /// The index of the SELECT item that is the same expression as <paramref name="expr"/>, or -1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compared as the CANONICAL TEXT the serializer writes, because the AST's own <c>Is</c> includes
    /// the line and column a node was parsed at - and the two occurrences of one expression are, by
    /// definition, in two different places in the statement. Comparing renderings is what makes
    /// "the same expression" a question about the expression rather than about where it was written.
    /// </para>
    /// <para>
    /// <b>Not attempted for anything carrying a subquery.</b> The serializer renders one as the
    /// literal text <c>SELECT ...</c>, so two different subqueries render identically - and a false
    /// match here would silently order by the wrong column, which is worse than the refusal this
    /// method exists to remove.
    /// </para>
    /// </remarks>
    private static int MatchSelectItem(WitSqlExpression expr, IReadOnlyList<ClauseSelectItem> selectList)
    {
        var text = Render(expr);

        if (text == null)
            return -1;

        for (var i = 0; i < selectList.Count; i++)
        {
            if (selectList[i].Expression is { } selected && Render(selected) == text)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// An expression as text, or null when the rendering cannot be trusted to identify it.
    /// </summary>
    private static string? Render(WitSqlExpression expression)
    {
        try
        {
            var text = WitSqlExpressionSerializer.Serialize(expression);

            // The serializer's one lossy case - see MatchSelectItem.
            return text.Contains("SELECT", StringComparison.OrdinalIgnoreCase) ? null : text;
        }
        catch (NotSupportedException)
        {
            // An expression the serializer has no form for cannot be identified this way, and a
            // planner must not fail over a comparison it was only trying.
            return null;
        }
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
        if (limitCount == null && limitOffset == null)
            return iterator;

        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        // A missing or negative count means "no upper bound", so OFFSET alone still applies. Before,
        // a null count returned the iterator untouched and the offset was silently dropped.
        var limit = limitCount != null
            ? evaluator.Evaluate(limitCount, dummyRow).AsInt64()
            : -1;
        var offset = limitOffset != null
            ? evaluator.Evaluate(limitOffset, dummyRow).AsInt64()
            : 0;

        return new IteratorLimit(iterator, limit, offset);
    }

    #endregion

    #region Projection

    private IResultIterator ApplyProjection(IResultIterator iterator, IReadOnlyList<ClauseSelectItem> selectList)
    {
        // SELECT * emits exactly the source schema: its columns, once, under their own names.
        //
        // This used to wrap only when the source carried an internal column such as _rowid, and pass
        // the raw rows through otherwise - which was wrong for a derived table. IteratorAlias
        // deliberately puts every column into the row TWICE, qualified and bare, so that both X.Id
        // and Id resolve; its schema is correct but its rows are not a result. With no _rowid to
        // trigger the wrapper, that doubling reached the caller:
        //
        //     SELECT * FROM (SELECT Id, TId FROM S) AS X  ->  X.Id, X.TId, Id, TId
        //
        // A row twice as wide as asked for, with duplicate names - which an ordinal reader misreads
        // silently rather than failing on. The wrapper maps by schema ordinal, so applying it always
        // is both the fix and the simpler rule. Every table scan already carried _rowid and was
        // wrapped anyway, so the only newly wrapped shape is the derived table.
        if (IsSelectStar(selectList))
            return new IteratorExcludeInternal(iterator);

        // Any `*` beside another item has already become the columns it stands for by now - see
        // PlanNonAggregateQuery, where the same expanded list serves the ordering. KnownIssues 17.
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
