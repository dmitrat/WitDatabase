using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Visitor;

internal sealed partial class WitSqlVisitor
{
    #region Query Expression (SELECT with CTE/Set Operations)

    private new WitSqlStatementSelect VisitQueryExpression(WitSqlParser.QueryExpressionContext context)
    {
        // Parse CTE definitions if present
        List<ClauseCteDefinition>? cteDefinitions = null;
        var isRecursive = false;

        if (context.withClause() is { } withClause)
        {
            isRecursive = withClause.RECURSIVE() != null;
            cteDefinitions = withClause.cteDefinition()
                .Select(VisitCteDefinition)
                .ToList();
        }

        // Parse the first query term
        var queryTerm = context.queryTerm(0);
        WitSqlStatementSelect select;

        if (queryTerm.selectStatement() is { } selectCtx)
        {
            select = VisitSelectStatementWithCte(selectCtx, isRecursive, cteDefinitions);
        }
        else
        {
            // Nested queryExpression in parentheses
            select = VisitQueryExpression(queryTerm.queryExpression());
        }

        // Parse set operations (UNION, INTERSECT, EXCEPT)
        var setOperations = context.setOperation();
        if (setOperations.Length > 0)
        {
            var setOpList = new List<ClauseSetOperation>();

            for (int i = 0; i < setOperations.Length; i++)
            {
                var setOp = setOperations[i];
                var rightTerm = context.queryTerm(i + 1);

                WitSqlStatementSelect rightQuery;
                if (rightTerm.selectStatement() is { } rightSelectCtx)
                {
                    rightQuery = VisitSelectStatement(rightSelectCtx);
                }
                else
                {
                    rightQuery = VisitQueryExpression(rightTerm.queryExpression());
                }

                setOpList.Add(new ClauseSetOperation
                {
                    OperationType = ParseSetOperationType(setOp),
                    IsAll = setOp.ALL() != null,
                    RightQuery = rightQuery
                });
            }

            select.SetOperations = setOpList;
        }

        // ORDER BY and LIMIT are at queryExpression level
        if (context.orderByClause() is { } orderBy)
            select.OrderByClause = VisitOrderByClause(orderBy);
        if (context.limitClause() is { } limit)
        {
            select.LimitCount = limit.expression(0) is { } limitExpr ? VisitExpression(limitExpr) : null;
            select.LimitOffset = limit.expression(1) is { } offsetExpr ? VisitExpression(offsetExpr) : null;
        }

        return select;
    }

    private ClauseCteDefinition VisitCteDefinition(WitSqlParser.CteDefinitionContext context)
    {
        var columnNames = context.columnName()?.Select(c => c.GetText()).ToList();

        return new ClauseCteDefinition
        {
            Name = context.IDENTIFIER().GetText(),
            ColumnNames = columnNames,
            Query = VisitQueryExpression(context.queryExpression())
        };
    }

    private static SetOperationType ParseSetOperationType(WitSqlParser.SetOperationContext context)
    {
        if (context.UNION() != null) return SetOperationType.Union;
        if (context.INTERSECT() != null) return SetOperationType.Intersect;
        if (context.EXCEPT() != null) return SetOperationType.Except;
        throw new InvalidOperationException($"Unknown set operation type: {context.GetText()}");
    }

    #endregion

    #region SELECT Statement

    private WitSqlStatementSelect VisitSelectStatementWithCte(
        WitSqlParser.SelectStatementContext context,
        bool isRecursive,
        List<ClauseCteDefinition>? cteDefinitions)
    {
        return new WitSqlStatementSelect
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IsDistinct = context.DISTINCT() != null,
            IsRecursive = isRecursive,
            CteDefinitions = cteDefinitions,
            SelectList = VisitSelectList(context.selectList()),
            FromClause = context.fromClause() is { } from ? VisitFromClause(from) : null,
            WhereClause = context.whereClause() is { } where ? VisitExpression(where.expression()) : null,
            GroupByClause = context.groupByClause() is { } groupBy
                ? groupBy.expression().Select(VisitExpression).ToList<WitSqlExpression>()
                : null,
            HavingClause = context.havingClause() is { } having ? VisitExpression(having.expression()) : null,
        };
    }

    public override WitSqlStatementSelect VisitSelectStatement(WitSqlParser.SelectStatementContext context)
    {
        return VisitSelectStatementWithCte(context, false, null);
    }

    public override List<ClauseSelectItem> VisitSelectList(WitSqlParser.SelectListContext context)
    {
        return context.selectItem().Select(VisitSelectItem).ToList();
    }

    private ClauseSelectItem VisitSelectItem(WitSqlParser.SelectItemContext context)
    {
        return context switch
        {
            WitSqlParser.SelectAllContext => new ClauseSelectItem { IsStar = true },
            WitSqlParser.SelectTableAllContext tableAll => new ClauseSelectItem
            {
                IsStar = true,
                TableName = tableAll.tableName().GetText()
            },
            WitSqlParser.SelectExpressionContext expr => new ClauseSelectItem
            {
                Expression = VisitExpression(expr.expression()),
                Alias = expr.alias()?.GetText()
            },
            _ => throw new InvalidOperationException($"Unknown select item type: {context.GetType()}")
        };
    }

    public override List<TableSource> VisitFromClause(WitSqlParser.FromClauseContext context)
    {
        return context.tableSource().Select(VisitTableSource).ToList();
    }

    private TableSource VisitTableSource(WitSqlParser.TableSourceContext context)
    {
        return context switch
        {
            WitSqlParser.SimpleTableSourceContext simple => new TableSourceSimple
            {
                TableName = simple.tableName().GetText(),
                Alias = simple.alias()?.GetText()
            },
            WitSqlParser.JoinTableSourceContext join => new TableSourceJoin
            {
                Left = VisitTableSource(join.tableSource(0)),
                Right = VisitTableSource(join.tableSource(1)),
                JoinType = ParseJoinType(join.joinType()),
                OnCondition = join.expression() != null ? VisitExpression(join.expression()) : null,
                Alias = null
            },
            WitSqlParser.SubqueryTableSourceContext sub => new TableSourceSubquery
            {
                Subquery = VisitSelectStatement(sub.selectStatement()),
                Alias = sub.alias().GetText()
            },
            _ => throw new InvalidOperationException($"Unknown table source type: {context.GetType()}")
        };
    }

    private static JoinType ParseJoinType(WitSqlParser.JoinTypeContext context)
    {
        if (context.LEFT() != null) return JoinType.Left;
        if (context.RIGHT() != null) return JoinType.Right;
        if (context.FULL() != null) return JoinType.Full;
        if (context.CROSS() != null) return JoinType.Cross;
        return JoinType.Inner;
    }

    public override List<ClauseOrderByItem> VisitOrderByClause(WitSqlParser.OrderByClauseContext context)
    {
        return context.orderByItem().Select(item => new ClauseOrderByItem
        {
            Expression = VisitExpression(item.expression()),
            Descending = item.DESC() != null,
            NullsOrder = item.FIRST() != null ? NullsOrderType.First
                       : item.LAST() != null ? NullsOrderType.Last
                       : NullsOrderType.Default
        }).ToList();
    }

    #endregion

    #region INSERT Statement

    public override WitSqlStatementInsert VisitInsertStatement(WitSqlParser.InsertStatementContext context)
    {
        var columns = context.columnName()?.Select(c => c.GetText()).ToList();

        List<List<WitSqlExpression>>? values = null;
        if (context.valuesList() is { } valuesList)
        {
            values = new List<List<WitSqlExpression>>();
            // Parse each row of values
            foreach (var valueRow in valuesList.valueRow())
            {
                var row = valueRow.expression().Select(VisitExpression).ToList<WitSqlExpression>();
                values.Add(row);
            }
        }

        var conflictResolution = ConflictResolutionType.None;
        if (context.REPLACE() != null)
            conflictResolution = ConflictResolutionType.Replace;
        else if (context.IGNORE() != null)
            conflictResolution = ConflictResolutionType.Ignore;

        ClauseOnConflict? onConflict = null;
        if (context.onConflictClause() is { } onConflictCtx)
        {
            onConflict = VisitOnConflictClause(onConflictCtx);
        }

        return new WitSqlStatementInsert
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TableName = context.tableName().GetText(),
            ColumnNames = columns,
            Values = values,
            SelectSource = context.selectStatement() is { } select ? VisitSelectStatement(select) : null,
            ReturningClause = context.returningClause() is { } returning
                ? VisitSelectList(returning.selectList())
                : null,
            ConflictResolution = conflictResolution,
            OnConflict = onConflict
        };
    }

    private ClauseOnConflict VisitOnConflictClause(WitSqlParser.OnConflictClauseContext context)
    {
        var conflictColumns = context.columnName()?.Select(c => c.GetText()).ToList();
        var conflictAction = context.conflictAction();

        var actionType = conflictAction.NOTHING() != null
            ? ConflictActionType.Nothing
            : ConflictActionType.Update;

        List<ClauseSet>? updateClauses = null;
        WitSqlExpression? whereClause = null;

        if (actionType == ConflictActionType.Update)
        {
            updateClauses = conflictAction.setClause()
                .Select(s => new ClauseSet
                {
                    ColumnName = s.columnName().GetText(),
                    Value = VisitExpression(s.expression())
                })
                .ToList();

            if (conflictAction.expression() is { } whereExpr)
            {
                whereClause = VisitExpression(whereExpr);
            }
        }

        return new ClauseOnConflict
        {
            ConflictColumns = conflictColumns,
            ActionType = actionType,
            UpdateClauses = updateClauses,
            WhereClause = whereClause
        };
    }

    #endregion

    #region UPDATE Statement

    public override WitSqlStatementUpdate VisitUpdateStatement(WitSqlParser.UpdateStatementContext context)
    {
        return new WitSqlStatementUpdate
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TableName = context.tableName().GetText(),
            SetClauses = context.setClause().Select(s => new ClauseSet
            {
                ColumnName = s.columnName().GetText(),
                Value = VisitExpression(s.expression())
            }).ToList(),
            WhereClause = context.whereClause() is { } where ? VisitExpression(where.expression()) : null,
            ReturningClause = context.returningClause() is { } returning
                ? VisitSelectList(returning.selectList())
                : null
        };
    }

    #endregion

    #region DELETE Statement

    public override WitSqlStatementDelete VisitDeleteStatement(WitSqlParser.DeleteStatementContext context)
    {
        return new WitSqlStatementDelete
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TableName = context.tableName().GetText(),
            WhereClause = context.whereClause() is { } where ? VisitExpression(where.expression()) : null,
            ReturningClause = context.returningClause() is { } returning
                ? VisitSelectList(returning.selectList())
                : null
        };
    }

    #endregion
}
