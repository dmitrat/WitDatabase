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
            WitSqlParser.ParameterExprContext param => VisitParameter(param.parameter()),
            WitSqlParser.ParenExprContext paren => VisitExpression(paren.expression()),
            WitSqlParser.SubqueryExprContext sub => new WitSqlExpressionSubquery
            {
                Line = sub.Start.Line,
                Column = sub.Start.Column,
                Query = VisitQueryExpression(sub.queryExpression())
            },
            WitSqlParser.ExistsExprContext exists => new WitSqlExpressionExists
            {
                Line = exists.Start.Line,
                Column = exists.Start.Column,
                Query = VisitQueryExpression(exists.queryExpression()),
                IsNot = exists.NOT() != null
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
                Values = inExpr.queryExpression() == null
                    ? inExpr.expression().Skip(1).Select(VisitExpression).ToList()
                    : null,
                Subquery = inExpr.queryExpression() is { } inQuery ? VisitQueryExpression(inQuery) : null,
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
            WitSqlParser.ConvertExprContext convert => new WitSqlExpressionCast
            {
                Line = convert.Start.Line,
                Column = convert.Start.Column,
                Expression = VisitExpression(convert.expression()),
                TargetType = VisitDataType(convert.dataType())
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
            WitSqlParser.QuantifiedExprContext quantified => VisitQuantifiedExpression(quantified),
            WitSqlParser.CollateExprContext collate => new WitSqlExpressionCollate
            {
                Line = collate.Start.Line,
                Column = collate.Start.Column,
                Operand = VisitExpression(collate.expression()),
                CollationName = collate.collationName().GetText().ToUpperInvariant()
            },
            _ => throw new InvalidOperationException($"Unknown expression type: {context.GetType()}")
        };
    }

    private WitSqlExpressionQuantified VisitQuantifiedExpression(WitSqlParser.QuantifiedExprContext context)
    {
        var compOp = context.comparisonOp();
        var op = compOp.EQ() != null ? BinaryOperatorType.Equal :
                 compOp.NE() != null || compOp.NE2() != null ? BinaryOperatorType.NotEqual :
                 compOp.LT() != null ? BinaryOperatorType.LessThan :
                 compOp.LE() != null ? BinaryOperatorType.LessOrEqual :
                 compOp.GT() != null ? BinaryOperatorType.GreaterThan :
                 BinaryOperatorType.GreaterOrEqual;

        var quantifierType = context.ANY() != null ? QuantifierType.Any :
                             context.SOME() != null ? QuantifierType.Some :
                             QuantifierType.All;

        return new WitSqlExpressionQuantified
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Expression = VisitExpression(context.expression()),
            Operator = op,
            QuantifierType = quantifierType,
            Subquery = VisitQueryExpression(context.queryExpression())
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

    private WitSqlExpressionParameter VisitParameter(WitSqlParser.ParameterContext context)
    {
        var line = context.Start.Line;
        var col = context.Start.Column;

        return context switch
        {
            WitSqlParser.NamedParameterContext named => new WitSqlExpressionParameter
            {
                Line = line,
                Column = col,
                ParameterType = ParameterType.Named,
                Name = named.GetText()[1..] // Remove @ prefix
            },
            WitSqlParser.ColonParameterContext colon => new WitSqlExpressionParameter
            {
                Line = line,
                Column = col,
                ParameterType = ParameterType.Colon,
                Name = colon.GetText()[1..] // Remove : prefix
            },
            WitSqlParser.DollarNamedParameterContext dollarNamed => new WitSqlExpressionParameter
            {
                Line = line,
                Column = col,
                ParameterType = ParameterType.DollarNamed,
                Name = dollarNamed.GetText()[1..] // Remove $ prefix
            },
            WitSqlParser.PositionalParameterContext => new WitSqlExpressionParameter
            {
                Line = line,
                Column = col,
                ParameterType = ParameterType.Positional
            },
            WitSqlParser.NumberedParameterContext numbered => new WitSqlExpressionParameter
            {
                Line = line,
                Column = col,
                ParameterType = ParameterType.Numbered,
                Position = int.Parse(numbered.GetText()[1..]) // Remove $ prefix
            },
            _ => throw new InvalidOperationException($"Unknown parameter type: {context.GetType()}")
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

    public WitSqlExpressionColumnRef VisitColumnRef(WitSqlParser.ColumnRefContext context)
    {
        return context switch
        {
            WitSqlParser.SimpleColumnRefContext simple => new WitSqlExpressionColumnRef
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                TableName = simple.tableName() != null ? GetTableName(simple.tableName()) : null,
                ColumnName = GetColumnName(simple.columnName()),
                IsExcluded = false
            },
            WitSqlParser.ExcludedColumnRefContext excluded => new WitSqlExpressionColumnRef
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                TableName = null,
                ColumnName = GetColumnName(excluded.columnName()),
                IsExcluded = true
            },
            _ => throw new InvalidOperationException($"Unknown column ref type: {context.GetType()}")
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
            OrderBy = context.orderByClause() is { } orderBy ? VisitOrderByClause(orderBy) : null,
            Frame = context.frameClause() is { } frame ? VisitFrameClause(frame) : null
        };
    }

    private SpecFrame VisitFrameClause(WitSqlParser.FrameClauseContext context)
    {
        var frameType = context.ROWS() != null ? FrameType.Rows : FrameType.Range;

        var frameBounds = context.frameBound();

        if (frameBounds.Length == 1)
        {
            // Single bound: ROWS/RANGE frameBound (end is implicitly CURRENT ROW)
            return new SpecFrame
            {
                FrameType = frameType,
                Start = VisitFrameBound(frameBounds[0]),
                End = new SpecFrameBound { BoundType = FrameBoundType.CurrentRow }
            };
        }
        else
        {
            // BETWEEN ... AND ...
            return new SpecFrame
            {
                FrameType = frameType,
                Start = VisitFrameBound(frameBounds[0]),
                End = VisitFrameBound(frameBounds[1])
            };
        }
    }

    private SpecFrameBound VisitFrameBound(WitSqlParser.FrameBoundContext context)
    {
        if (context.UNBOUNDED() != null)
        {
            if (context.PRECEDING() != null)
            {
                return new SpecFrameBound { BoundType = FrameBoundType.UnboundedPreceding };
            }
            else
            {
                return new SpecFrameBound { BoundType = FrameBoundType.UnboundedFollowing };
            }
        }

        if (context.CURRENT() != null)
        {
            return new SpecFrameBound { BoundType = FrameBoundType.CurrentRow };
        }

        // n PRECEDING or n FOLLOWING
        var intLiteral = context.INTEGER_LITERAL();
        var offset = intLiteral != null ? int.Parse(intLiteral.GetText()) : 1;

        if (context.PRECEDING() != null)
        {
            return new SpecFrameBound { BoundType = FrameBoundType.Preceding, Offset = offset };
        }
        else
        {
            return new SpecFrameBound { BoundType = FrameBoundType.Following, Offset = offset };
        }
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
