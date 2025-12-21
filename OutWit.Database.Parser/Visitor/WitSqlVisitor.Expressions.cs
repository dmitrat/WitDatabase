using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Specs;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Visitor;

internal sealed partial class WitSqlVisitor
{
    #region Expressions

    public WitSqlExpression VisitExpression(WitSqlParser.ExpressionContext context)
    {
        return context switch
        {
            WitSqlParser.LiteralExprContext lit => VisitLiteral(lit.literal()),
            WitSqlParser.ColumnRefExprContext col => VisitColumnRef(col.columnRef()),
            WitSqlParser.FunctionCallExprContext func => VisitFunctionCall(func.functionCall()),
            WitSqlParser.ParenExprContext paren => VisitExpression(paren.expression()),
            WitSqlParser.SubqueryExprContext sub => new WitSqlExpressionSubquery
            {
                Line = sub.Start.Line,
                Column = sub.Start.Column,
                Query = VisitSelectStatement(sub.selectStatement())
            },
            WitSqlParser.UnaryExprContext unary => new WitSqlExpressionUnary
            {
                Line = unary.Start.Line,
                Column = unary.Start.Column,
                Operator = WitSqlVisitor.ParseUnaryOperator(unary),
                Operand = VisitExpression(unary.expression())
            },
            WitSqlParser.MulDivExprContext mulDiv => new WitSqlExpressionBinary
            {
                Line = mulDiv.Start.Line,
                Column = mulDiv.Start.Column,
                Left = VisitExpression(mulDiv.expression(0)),
                Operator = WitSqlVisitor.ParseMulDivOperator(mulDiv),
                Right = VisitExpression(mulDiv.expression(1))
            },
            WitSqlParser.AddSubExprContext addSub => new WitSqlExpressionBinary
            {
                Line = addSub.Start.Line,
                Column = addSub.Start.Column,
                Left = VisitExpression(addSub.expression(0)),
                Operator = addSub.PLUS() != null ? BinaryOperatorType.Add : BinaryOperatorType.Subtract,
                Right = VisitExpression(addSub.expression(1))
            },
            WitSqlParser.ConcatExprContext concat => new WitSqlExpressionBinary
            {
                Line = concat.Start.Line,
                Column = concat.Start.Column,
                Left = VisitExpression(concat.expression(0)),
                Operator = BinaryOperatorType.Concat,
                Right = VisitExpression(concat.expression(1))
            },
            WitSqlParser.CompareExprContext comp => new WitSqlExpressionBinary
            {
                Line = comp.Start.Line,
                Column = comp.Start.Column,
                Left = VisitExpression(comp.expression(0)),
                Operator = WitSqlVisitor.ParseCompareOperator(comp),
                Right = VisitExpression(comp.expression(1))
            },
            WitSqlParser.EqualityExprContext eq => new WitSqlExpressionBinary
            {
                Line = eq.Start.Line,
                Column = eq.Start.Column,
                Left = VisitExpression(eq.expression(0)),
                Operator = eq.EQ() != null ? BinaryOperatorType.Equal : BinaryOperatorType.NotEqual,
                Right = VisitExpression(eq.expression(1))
            },
            WitSqlParser.IsNullExprContext isNull => new WitSqlExpressionIsNull
            {
                Line = isNull.Start.Line,
                Column = isNull.Start.Column,
                Expression = VisitExpression(isNull.expression()),
                IsNot = isNull.NOT() != null
            },
            WitSqlParser.BetweenExprContext between => new WitSqlExpressionBetween
            {
                Line = between.Start.Line,
                Column = between.Start.Column,
                Expression = VisitExpression(between.expression(0)),
                Low = VisitExpression(between.expression(1)),
                High = VisitExpression(between.expression(2)),
                IsNot = between.NOT() != null
            },
            WitSqlParser.InExprContext inExpr => new WitSqlExpressionIn
            {
                Line = inExpr.Start.Line,
                Column = inExpr.Start.Column,
                Expression = VisitExpression(inExpr.expression(0)),
                Values = inExpr.selectStatement() == null
                    ? inExpr.expression().Skip(1).Select(VisitExpression).ToList()
                    : null,
                Subquery = inExpr.selectStatement() is { } inSelect ? VisitSelectStatement(inSelect) : null,
                IsNot = inExpr.NOT() != null
            },
            WitSqlParser.LikeExprContext like => new WitSqlExpressionLike
            {
                Line = like.Start.Line,
                Column = like.Start.Column,
                Expression = VisitExpression(like.expression(0)),
                Pattern = VisitExpression(like.expression(1)),
                Escape = like.expression().Length > 2 ? VisitExpression(like.expression(2)) : null,
                IsNot = like.NOT() != null
            },
            WitSqlParser.AndExprContext and => new WitSqlExpressionBinary
            {
                Line = and.Start.Line,
                Column = and.Start.Column,
                Left = VisitExpression(and.expression(0)),
                Operator = BinaryOperatorType.And,
                Right = VisitExpression(and.expression(1))
            },
            WitSqlParser.OrExprContext or => new WitSqlExpressionBinary
            {
                Line = or.Start.Line,
                Column = or.Start.Column,
                Left = VisitExpression(or.expression(0)),
                Operator = BinaryOperatorType.Or,
                Right = VisitExpression(or.expression(1))
            },
            WitSqlParser.CaseExprContext caseExpr => VisitCaseExpression(caseExpr),
            WitSqlParser.CastExprContext cast => new WitSqlExpressionCast
            {
                Line = cast.Start.Line,
                Column = cast.Start.Column,
                Expression = VisitExpression(cast.expression()),
                TargetType = VisitDataType(cast.dataType())
            },
            WitSqlParser.GlobExprContext glob => new WitSqlExpressionGlob
            {
                Line = glob.Start.Line,
                Column = glob.Start.Column,
                Expression = VisitExpression(glob.expression(0)),
                Pattern = VisitExpression(glob.expression(1)),
                IsNot = glob.NOT() != null
            },
            WitSqlParser.IifExprContext iif => new WitSqlExpressionIif
            {
                Line = iif.Start.Line,
                Column = iif.Start.Column,
                Condition = VisitExpression(iif.expression(0)),
                TrueValue = VisitExpression(iif.expression(1)),
                FalseValue = VisitExpression(iif.expression(2))
            },
            WitSqlParser.BitwiseExprContext bitwise => VisitBitwiseExpression(bitwise),
            _ => throw new InvalidOperationException($"Unknown expression type: {context.GetType()}")
        };
    }

    private WitSqlExpressionBinary VisitBitwiseExpression(WitSqlParser.BitwiseExprContext context)
    {
        var op = context.AMP() != null ? BinaryOperatorType.BitwiseAnd :
                 context.PIPE() != null ? BinaryOperatorType.BitwiseOr :
                 context.LSHIFT() != null ? BinaryOperatorType.LeftShift :
                 BinaryOperatorType.RightShift;

        return new WitSqlExpressionBinary
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Left = VisitExpression(context.expression(0)),
            Operator = op,
            Right = VisitExpression(context.expression(1))
        };
    }

    private WitSqlExpressionLiteral VisitLiteral(WitSqlParser.LiteralContext context)
    {
        var line = context.Start.Line;
        var col = context.Start.Column;

        return context switch
        {
            WitSqlParser.IntLiteralContext intLit => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Integer,
                Value = long.Parse(intLit.GetText())
            },
            WitSqlParser.RealLiteralContext realLit => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Real,
                Value = double.Parse(realLit.GetText(), System.Globalization.CultureInfo.InvariantCulture)
            },
            WitSqlParser.StringLiteralContext strLit => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.String,
                Value = WitSqlVisitor.ParseStringLiteral(strLit.GetText())
            },
            WitSqlParser.BlobLiteralContext blobLit => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Blob,
                Value = WitSqlVisitor.ParseBlobLiteral(blobLit.GetText())
            },
            WitSqlParser.TrueLiteralContext => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Boolean,
                Value = true
            },
            WitSqlParser.FalseLiteralContext => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Boolean,
                Value = false
            },
            WitSqlParser.NullLiteralContext => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Null,
                Value = null
            },
            WitSqlParser.CurrentTimestampLiteralContext => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.CurrentTimestamp
            },
            WitSqlParser.CurrentDateLiteralContext => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.CurrentDate
            },
            WitSqlParser.CurrentTimeLiteralContext => new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.CurrentTime
            },
            _ => throw new InvalidOperationException($"Unknown literal type: {context.GetType()}")
        };
    }

    public override WitSqlExpressionColumnRef VisitColumnRef(WitSqlParser.ColumnRefContext context)
    {
        return new WitSqlExpressionColumnRef
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TableName = context.tableName()?.GetText(),
            ColumnName = context.columnName().GetText()
        };
    }

    public override WitSqlExpressionFunctionCall VisitFunctionCall(WitSqlParser.FunctionCallContext context)
    {
        var args = context.expression()?.Select(VisitExpression).ToList();

        return new WitSqlExpressionFunctionCall
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            FunctionName = context.functionName().GetText().ToUpperInvariant(),
            Arguments = args,
            IsDistinct = context.DISTINCT() != null,
            IsStar = context.STAR() != null,
            Over = context.windowSpec() is { } ws ? VisitWindowSpec(ws) : null
        };
    }

    public override SpecWindow VisitWindowSpec(WitSqlParser.WindowSpecContext context)
    {
        return new SpecWindow
        {
            PartitionBy = context.expression()?.Select(VisitExpression).ToList(),
            OrderBy = context.orderByClause() is { } orderBy ? VisitOrderByClause(orderBy) : null
        };
    }

    private WitSqlExpressionCase VisitCaseExpression(WitSqlParser.CaseExprContext context)
    {
        var expressions = context.expression();
        var whenTokens = context.WHEN();

        WitSqlExpression? operand = null;
        int exprOffset = 0;

        // Check if this is simple CASE expr WHEN... or searched CASE WHEN...
        // In simple CASE, there's one more expression than (WHEN count * 2) + optional ELSE
        // For searched CASE: expressions = (WHEN count * 2) + optional ELSE
        // For simple CASE: expressions = 1 + (WHEN count * 2) + optional ELSE
        int whenCount = whenTokens.Length;
        bool hasElse = context.ELSE() != null;
        int searchedExprCount = whenCount * 2 + (hasElse ? 1 : 0);

        if (expressions.Length > searchedExprCount)
        {
            // This is simple CASE - first expression is the operand
            operand = VisitExpression(expressions[0]);
            exprOffset = 1;
        }

        var whenClauses = new List<ClauseWhen>();

        // Parse WHEN/THEN pairs
        for (int i = 0; i < whenCount; i++)
        {
            int whenIdx = exprOffset + (i * 2);
            int thenIdx = whenIdx + 1;
            whenClauses.Add(new ClauseWhen
            {
                When = VisitExpression(expressions[whenIdx]),
                Then = VisitExpression(expressions[thenIdx])
            });
        }

        // ELSE clause
        WitSqlExpression? elseResult = null;
        if (hasElse)
        {
            elseResult = VisitExpression(expressions[^1]);
        }

        return new WitSqlExpressionCase
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Operand = operand,
            WhenClauses = whenClauses,
            ElseResult = elseResult
        };
    }

    #endregion
}
