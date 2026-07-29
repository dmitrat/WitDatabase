using System.Globalization;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Exceptions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Specs;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Visitor;

internal sealed partial class WitSqlVisitor
{
    #region Expressions

    /// <summary>
    /// The entry point for every expression position in the grammar.
    /// </summary>
    /// <remarks>
    /// <c>expression</c> is now a one-alternative rule over <c>searchCondition</c>, which is what
    /// keeps all 29 <c>VisitExpression(ctx.expression())</c> call sites in the DML and DDL visitors
    /// compiling unchanged while every one of them gains the full boolean layer.
    /// </remarks>
    public WitSqlExpression VisitExpression(WitSqlParser.ExpressionContext context) =>
        VisitSearchCondition(context.searchCondition());

    /// <summary>
    /// The boolean layer: <c>OR</c>, <c>AND</c> and prefix <c>NOT</c>.
    /// </summary>
    /// <remarks>
    /// Splitting the flat rule into three changes the parse tree everywhere, but <b>not</b> the AST:
    /// <see cref="WitSqlExpression"/> has no boolean/value distinction, so all three dispatchers
    /// return the same type and everything downstream of the parser is unaffected.
    /// </remarks>
    public WitSqlExpression VisitSearchCondition(WitSqlParser.SearchConditionContext context)
    {
        return context switch
        {
            WitSqlParser.PredicateExprContext pred => VisitPredicate(pred.predicate()),
            WitSqlParser.AndExprContext and => new WitSqlExpressionBinary
            {
                Line = and.Start.Line,
                Column = and.Start.Column,
                Left = VisitSearchCondition(and.searchCondition(0)),
                Operator = BinaryOperatorType.And,
                Right = VisitSearchCondition(and.searchCondition(1))
            },
            WitSqlParser.OrExprContext or => new WitSqlExpressionBinary
            {
                Line = or.Start.Line,
                Column = or.Start.Column,
                Left = VisitSearchCondition(or.searchCondition(0)),
                Operator = BinaryOperatorType.Or,
                Right = VisitSearchCondition(or.searchCondition(1))
            },
            WitSqlParser.NotExprContext not => NegateSearchCondition(not),
            _ => throw new InvalidOperationException($"Unknown search condition type: {context.GetType()}")
        };
    }

    /// <summary>
    /// Applies prefix <c>NOT</c>, folding <c>NOT EXISTS (…)</c> back into a single
    /// <see cref="WitSqlExpressionExists"/> with <c>IsNot</c> set.
    /// </summary>
    /// <remarks>
    /// The grammar's <c>existsExpr</c> no longer carries its own optional <c>NOT</c>. Carrying it in
    /// both places made <c>NOT EXISTS (…)</c> derivable two ways, which ANTLR resolved silently by
    /// alternative order — one of the seven ambiguities the corpus harness found on the old grammar.
    /// Folding it here keeps the emitted AST byte-identical to the old one, which matters because
    /// <c>ExpressionEvaluator.Subquery</c> reads <c>exists.IsNot</c> directly.
    /// </remarks>
    private WitSqlExpression NegateSearchCondition(WitSqlParser.NotExprContext context)
    {
        var operand = VisitSearchCondition(context.searchCondition());

        if (operand is WitSqlExpressionExists { IsNot: false } exists)
        {
            return new WitSqlExpressionExists
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                Query = exists.Query,
                IsNot = true
            };
        }

        return new WitSqlExpressionUnary
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Operator = UnaryOperatorType.Not,
            Operand = operand
        };
    }

    /// <summary>
    /// The comparison and pattern layer.
    /// </summary>
    /// <remarks>
    /// The <b>left</b> operand of each alternative is a recursive <c>predicate</c> reference, so
    /// comparisons still chain — <c>a = 1 = 1</c> parses as <c>(a = 1) = 1</c>, which is what SQLite
    /// does. Every <b>other</b> operand is a <c>valueExpression</c>, a different rule, and therefore
    /// cannot derive <c>AND</c>: that is what stops <c>BETWEEN</c> reaching past its upper bound to
    /// swallow the following conjunct.
    /// </remarks>
    public WitSqlExpression VisitPredicate(WitSqlParser.PredicateContext context)
    {
        return context switch
        {
            WitSqlParser.ValuePredicateContext value => VisitValueExpression(value.valueExpression()),
            WitSqlParser.CompareExprContext comp => new WitSqlExpressionBinary
            {
                Line = comp.Start.Line,
                Column = comp.Start.Column,
                Left = VisitPredicate(comp.predicate()),
                Operator = WitSqlVisitor.ParseCompareOperator(comp),
                Right = VisitValueExpression(comp.valueExpression())
            },
            WitSqlParser.EqualityExprContext eq => new WitSqlExpressionBinary
            {
                Line = eq.Start.Line,
                Column = eq.Start.Column,
                Left = VisitPredicate(eq.predicate()),
                Operator = eq.EQ() != null ? BinaryOperatorType.Equal : BinaryOperatorType.NotEqual,
                Right = VisitValueExpression(eq.valueExpression())
            },
            WitSqlParser.IsNullExprContext isNull => new WitSqlExpressionIsNull
            {
                Line = isNull.Start.Line,
                Column = isNull.Start.Column,
                Expression = VisitPredicate(isNull.predicate()),
                IsNot = isNull.NOT() != null
            },
            WitSqlParser.BetweenExprContext between => new WitSqlExpressionBetween
            {
                Line = between.Start.Line,
                Column = between.Start.Column,
                Expression = VisitPredicate(between.predicate()),
                Low = VisitValueExpression(between.valueExpression(0)),
                High = VisitValueExpression(between.valueExpression(1)),
                IsNot = between.NOT() != null
            },
            WitSqlParser.InExprContext inExpr => new WitSqlExpressionIn
            {
                Line = inExpr.Start.Line,
                Column = inExpr.Start.Column,
                Expression = VisitPredicate(inExpr.predicate()),
                Values = inExpr.queryExpression() == null
                    ? inExpr.expression().Select(VisitExpression).ToList()
                    : null,
                Subquery = inExpr.queryExpression() is { } inQuery ? VisitQueryExpression(inQuery) : null,
                IsNot = inExpr.NOT() != null
            },
            WitSqlParser.LikeExprContext like => new WitSqlExpressionLike
            {
                Line = like.Start.Line,
                Column = like.Start.Column,
                Expression = VisitPredicate(like.predicate()),
                Pattern = VisitValueExpression(like.valueExpression(0)),
                // The ESCAPE operand is optional again. It only needed its own alternative while the
                // pattern was an interior reference of the flat rule; inside `predicate` it is not.
                Escape = like.ESCAPE() != null ? VisitValueExpression(like.valueExpression(1)) : null,
                IsNot = like.NOT() != null
            },
            WitSqlParser.GlobExprContext glob => new WitSqlExpressionGlob
            {
                Line = glob.Start.Line,
                Column = glob.Start.Column,
                Expression = VisitPredicate(glob.predicate()),
                Pattern = VisitValueExpression(glob.valueExpression()),
                IsNot = glob.NOT() != null
            },
            WitSqlParser.QuantifiedExprContext quantified => VisitQuantifiedExpression(quantified),
            WitSqlParser.ExistsExprContext exists => new WitSqlExpressionExists
            {
                Line = exists.Start.Line,
                Column = exists.Start.Column,
                Query = VisitQueryExpression(exists.queryExpression()),
                IsNot = false
            },
            _ => throw new InvalidOperationException($"Unknown predicate type: {context.GetType()}")
        };
    }

    /// <summary>
    /// The value layer: literals, references, arithmetic, and everything that produces a value.
    /// </summary>
    public WitSqlExpression VisitValueExpression(WitSqlParser.ValueExpressionContext context)
    {
        return context switch
        {
            WitSqlParser.LiteralExprContext lit => VisitLiteral(lit.literal()),
            WitSqlParser.ColumnRefExprContext col => VisitColumnRef(col.columnRef()),
            WitSqlParser.FunctionCallExprContext func => VisitFunctionCall(func.functionCall()),
            WitSqlParser.ParameterExprContext param => VisitParameter(param.parameter()),
            // Parentheses carry no meaning of their own once the tree is built; the serializer adds
            // its own back on the way out.
            WitSqlParser.ParenExprContext paren => VisitExpression(paren.expression()),
            WitSqlParser.SubqueryExprContext sub => new WitSqlExpressionSubquery
            {
                Line = sub.Start.Line,
                Column = sub.Start.Column,
                Query = VisitQueryExpression(sub.queryExpression())
            },
            WitSqlParser.UnaryExprContext unary => new WitSqlExpressionUnary
            {
                Line = unary.Start.Line,
                Column = unary.Start.Column,
                Operator = WitSqlVisitor.ParseUnaryOperator(unary),
                Operand = VisitValueExpression(unary.valueExpression())
            },
            WitSqlParser.MulDivExprContext mulDiv => new WitSqlExpressionBinary
            {
                Line = mulDiv.Start.Line,
                Column = mulDiv.Start.Column,
                Left = VisitValueExpression(mulDiv.valueExpression(0)),
                Operator = WitSqlVisitor.ParseMulDivOperator(mulDiv),
                Right = VisitValueExpression(mulDiv.valueExpression(1))
            },
            WitSqlParser.AddSubExprContext addSub => new WitSqlExpressionBinary
            {
                Line = addSub.Start.Line,
                Column = addSub.Start.Column,
                Left = VisitValueExpression(addSub.valueExpression(0)),
                Operator = addSub.PLUS() != null ? BinaryOperatorType.Add : BinaryOperatorType.Subtract,
                Right = VisitValueExpression(addSub.valueExpression(1))
            },
            WitSqlParser.BitwiseExprContext bitwise => VisitBitwiseExpression(bitwise),
            WitSqlParser.ConcatExprContext concat => new WitSqlExpressionBinary
            {
                Line = concat.Start.Line,
                Column = concat.Start.Column,
                Left = VisitValueExpression(concat.valueExpression(0)),
                Operator = BinaryOperatorType.Concat,
                Right = VisitValueExpression(concat.valueExpression(1))
            },
            WitSqlParser.CollateExprContext collate => new WitSqlExpressionCollate
            {
                Line = collate.Start.Line,
                Column = collate.Start.Column,
                Operand = VisitValueExpression(collate.valueExpression()),
                CollationName = collate.collationName().GetText().ToUpperInvariant()
            },
            WitSqlParser.SimpleCaseExprContext simpleCase => VisitSimpleCase(simpleCase),
            WitSqlParser.SearchedCaseExprContext searchedCase => VisitSearchedCase(searchedCase),
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
            WitSqlParser.IifExprContext iif => new WitSqlExpressionIif
            {
                Line = iif.Start.Line,
                Column = iif.Start.Column,
                Condition = VisitExpression(iif.expression(0)),
                TrueValue = VisitExpression(iif.expression(1)),
                FalseValue = VisitExpression(iif.expression(2))
            },
            _ => throw new InvalidOperationException($"Unknown value expression type: {context.GetType()}")
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
            Expression = VisitPredicate(context.predicate()),
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
            Left = VisitValueExpression(context.valueExpression(0)),
            Operator = op,
            Right = VisitValueExpression(context.valueExpression(1))
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
            WitSqlParser.IntLiteralContext intLit => ParseIntegerLiteral(intLit.GetText(), line, col),
            WitSqlParser.HexLiteralContext hexLit => ParseHexLiteral(hexLit.GetText(), line, col),
            WitSqlParser.RealLiteralContext realLit => ParseNumericLiteral(realLit.GetText(), line, col),
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

    /// <summary>
    /// Parses an integer literal, widening to an exact decimal when it does not fit
    /// <see cref="long"/>.
    /// </summary>
    /// <remarks>
    /// <c>long.Parse</c> used to throw a raw <see cref="OverflowException"/> out of the parser for any
    /// value above <see cref="long.MaxValue"/> — which made <c>UBIGINT</c>'s upper half unreachable by
    /// literal — and for <c>-9223372036854775808</c>, because the sign is a separate unary operator so
    /// the magnitude <c>9223372036854775808</c> is parsed on its own first.
    /// </remarks>
    private static WitSqlExpressionLiteral ParseIntegerLiteral(string text, int line, int col)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Integer,
                Value = longValue
            };
        }

        if (decimal.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Decimal,
                Value = decimalValue
            };
        }

        throw new WitSqlParsingException(new[]
        {
            new WitSqlParsingError
            {
                Line = line,
                Column = col,
                Message = $"Integer literal '{text}' is out of range"
            }
        });
    }

    /// <summary>
    /// Parses a <c>0x…</c> hexadecimal literal as a 64-bit two's-complement integer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hex does not behave like the decimal integer literal above, and the difference was
    /// measured rather than assumed.</b> An oversized decimal literal is widened to
    /// <see cref="decimal"/> to keep its value; an oversized hex literal is not. SQLite answers
    /// <c>SELECT 0xFFFFFFFFFFFFFFFF</c> with <b>-1</b> — the 64 bits are reinterpreted as signed,
    /// which is the whole point of writing a bit pattern in hex. Widening it to
    /// 18446744073709551615 instead would have been the natural-looking choice and would have
    /// disagreed with the oracle.
    /// </para>
    /// <para>
    /// Past 64 bits SQLite raises <c>hex literal too big</c> rather than truncating, so this does
    /// too. Leading zeros do not count against the limit.
    /// </para>
    /// </remarks>
    private static WitSqlExpressionLiteral ParseHexLiteral(string text, int line, int col)
    {
        // text is "0x…" or "0X…"; both the prefix and the digits are case-insensitive.
        var digits = text[2..].TrimStart('0');

        if (digits.Length > 16)
        {
            throw new WitSqlParsingException(new[]
            {
                new WitSqlParsingError
                {
                    Line = line,
                    Column = col,
                    Message = $"Hex literal '{text}' is too big"
                }
            });
        }

        var magnitude = digits.Length == 0
            ? 0UL
            : Convert.ToUInt64(digits, 16);

        return new WitSqlExpressionLiteral
        {
            Line = line,
            Column = col,
            Type = LiteralType.Integer,
            Value = unchecked((long)magnitude)
        };
    }

    /// <summary>
    /// Parses a numeric literal that carries a decimal point or an exponent.
    /// </summary>
    /// <remarks>
    /// SQL treats a literal with a decimal point and no exponent as **exact** numeric
    /// (DECIMAL/NUMERIC); only the exponent form is approximate. Parsing everything as
    /// <see cref="double"/> silently changed values — <c>12345678901234.5678</c> inserted into a
    /// <c>DECIMAL(28,10)</c> column read back as <c>12345678901234.6</c>.
    /// </remarks>
    private static WitSqlExpressionLiteral ParseNumericLiteral(string text, int line, int col)
    {
        var isApproximate = text.Contains('e') || text.Contains('E');

        if (!isApproximate &&
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return new WitSqlExpressionLiteral
            {
                Line = line,
                Column = col,
                Type = LiteralType.Decimal,
                Value = decimalValue
            };
        }

        // Exponent form, or a magnitude/precision decimal cannot hold: fall back to approximate.
        return new WitSqlExpressionLiteral
        {
            Line = line,
            Column = col,
            Type = LiteralType.Real,
            Value = double.Parse(text, CultureInfo.InvariantCulture)
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

    /// <summary>
    /// <c>CASE operand WHEN value THEN … END</c> — the simple form, where each <c>WHEN</c> holds a
    /// value compared against the operand.
    /// </summary>
    /// <remarks>
    /// The two <c>CASE</c> forms used to share one grammar alternative, and the visitor told them
    /// apart by <b>counting</b> the context's expressions against
    /// <c>whenCount * 2 + (hasElse ? 1 : 0)</c>. They are now structurally distinct, so the arithmetic
    /// is gone along with whatever it got wrong at the edges.
    /// </remarks>
    private WitSqlExpressionCase VisitSimpleCase(WitSqlParser.SimpleCaseExprContext context)
    {
        var results = context.expression();
        var whenValues = context.valueExpression();
        var hasElse = context.ELSE() != null;

        // valueExpression(0) is the operand; the rest are the WHEN values, one per WHEN.
        var whenClauses = new List<ClauseWhen>();

        for (var i = 0; i < context.WHEN().Length; i++)
        {
            whenClauses.Add(new ClauseWhen
            {
                When = VisitValueExpression(whenValues[i + 1]),
                Then = VisitExpression(results[i])
            });
        }

        return new WitSqlExpressionCase
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Operand = VisitValueExpression(whenValues[0]),
            WhenClauses = whenClauses,
            ElseResult = hasElse ? VisitExpression(results[^1]) : null
        };
    }

    /// <summary>
    /// <c>CASE WHEN condition THEN … END</c> — the searched form, where each <c>WHEN</c> holds a
    /// full boolean condition.
    /// </summary>
    private WitSqlExpressionCase VisitSearchedCase(WitSqlParser.SearchedCaseExprContext context)
    {
        var results = context.expression();
        var conditions = context.searchCondition();
        var hasElse = context.ELSE() != null;

        var whenClauses = new List<ClauseWhen>();

        for (var i = 0; i < conditions.Length; i++)
        {
            whenClauses.Add(new ClauseWhen
            {
                When = VisitSearchCondition(conditions[i]),
                Then = VisitExpression(results[i])
            });
        }

        return new WitSqlExpressionCase
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Operand = null,
            WhenClauses = whenClauses,
            ElseResult = hasElse ? VisitExpression(results[^1]) : null
        };
    }

    #endregion
}
