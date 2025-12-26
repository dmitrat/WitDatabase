using OutWit.Database.Expressions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Expressions;

/// <summary>
/// Tests for conditional expression evaluation: CASE, IIF, IS NULL, BETWEEN, IN, LIKE, GLOB, CAST.
/// </summary>
[TestFixture]
public class ExpressionEvaluatorConditionalTests : ExpressionEvaluatorTestsBase
{
    #region CASE Expression Tests

    [Test]
    public void EvaluateSimpleCaseMatchTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var caseExpr = new WitSqlExpressionCase
        {
            Operand = CreateIntLiteral(2),
            WhenClauses =
            [
                new ClauseWhen
                {
                    When = CreateIntLiteral(1),
                    Then = CreateStringLiteral("one")
                },
                new ClauseWhen
                {
                    When = CreateIntLiteral(2),
                    Then = CreateStringLiteral("two")
                }
            ],
            ElseResult = CreateStringLiteral("other")
        };

        var result = evaluator.Evaluate(caseExpr, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("two"));
    }

    [Test]
    public void EvaluateSimpleCaseElseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var caseExpr = new WitSqlExpressionCase
        {
            Operand = CreateIntLiteral(99),
            WhenClauses =
            [
                new ClauseWhen
                {
                    When = CreateIntLiteral(1),
                    Then = CreateStringLiteral("one")
                }
            ],
            ElseResult = CreateStringLiteral("other")
        };

        var result = evaluator.Evaluate(caseExpr, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("other"));
    }

    [Test]
    public void EvaluateSearchedCaseMatchTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var row = CreateRowWithInts(("Age", 25));
        var caseExpr = new WitSqlExpressionCase
        {
            WhenClauses =
            [
                new ClauseWhen
                {
                    When = new WitSqlExpressionBinary
                    {
                        Left = new WitSqlExpressionColumnRef { ColumnName = "Age" },
                        Operator = BinaryOperatorType.LessThan,
                        Right = CreateIntLiteral(18)
                    },
                    Then = CreateStringLiteral("minor")
                },
                new ClauseWhen
                {
                    When = new WitSqlExpressionBinary
                    {
                        Left = new WitSqlExpressionColumnRef { ColumnName = "Age" },
                        Operator = BinaryOperatorType.GreaterOrEqual,
                        Right = CreateIntLiteral(18)
                    },
                    Then = CreateStringLiteral("adult")
                }
            ]
        };

        var result = evaluator.Evaluate(caseExpr, row);

        Assert.That(result.AsString(), Is.EqualTo("adult"));
    }

    [Test]
    public void EvaluateCaseNoMatchNoElseReturnsNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var caseExpr = new WitSqlExpressionCase
        {
            Operand = CreateIntLiteral(99),
            WhenClauses =
            [
                new ClauseWhen
                {
                    When = CreateIntLiteral(1),
                    Then = CreateStringLiteral("one")
                }
            ]
        };

        var result = evaluator.Evaluate(caseExpr, CreateEmptyRow());

        Assert.That(result.IsNull, Is.True);
    }

    #endregion

    #region IIF Expression Tests

    [Test]
    public void EvaluateIifTrueTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var iif = new WitSqlExpressionIif
        {
            Condition = CreateBoolLiteral(true),
            TrueValue = CreateStringLiteral("yes"),
            FalseValue = CreateStringLiteral("no")
        };

        var result = evaluator.Evaluate(iif, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("yes"));
    }

    [Test]
    public void EvaluateIifFalseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var iif = new WitSqlExpressionIif
        {
            Condition = CreateBoolLiteral(false),
            TrueValue = CreateStringLiteral("yes"),
            FalseValue = CreateStringLiteral("no")
        };

        var result = evaluator.Evaluate(iif, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("no"));
    }

    [Test]
    public void EvaluateIifNullConditionReturnsFalseValueTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var iif = new WitSqlExpressionIif
        {
            Condition = new WitSqlExpressionLiteral { Type = LiteralType.Null },
            TrueValue = CreateStringLiteral("yes"),
            FalseValue = CreateStringLiteral("no")
        };

        var result = evaluator.Evaluate(iif, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("no"));
    }

    #endregion

    #region IS NULL Tests

    [Test]
    public void EvaluateIsNullTrueTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var isNull = new WitSqlExpressionIsNull
        {
            Expression = new WitSqlExpressionLiteral { Type = LiteralType.Null },
            IsNot = false
        };

        var result = evaluator.Evaluate(isNull, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateIsNullFalseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var isNull = new WitSqlExpressionIsNull
        {
            Expression = CreateIntLiteral(42),
            IsNot = false
        };

        var result = evaluator.Evaluate(isNull, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateIsNotNullTrueTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var isNull = new WitSqlExpressionIsNull
        {
            Expression = CreateIntLiteral(42),
            IsNot = true
        };

        var result = evaluator.Evaluate(isNull, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateIsNotNullFalseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var isNull = new WitSqlExpressionIsNull
        {
            Expression = new WitSqlExpressionLiteral { Type = LiteralType.Null },
            IsNot = true
        };

        var result = evaluator.Evaluate(isNull, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    #endregion

    #region BETWEEN Tests

    [Test]
    public void EvaluateBetweenInRangeTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var between = new WitSqlExpressionBetween
        {
            Expression = CreateIntLiteral(5),
            Low = CreateIntLiteral(1),
            High = CreateIntLiteral(10),
            IsNot = false
        };

        var result = evaluator.Evaluate(between, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateBetweenOutOfRangeTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var between = new WitSqlExpressionBetween
        {
            Expression = CreateIntLiteral(15),
            Low = CreateIntLiteral(1),
            High = CreateIntLiteral(10),
            IsNot = false
        };

        var result = evaluator.Evaluate(between, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateBetweenInclusiveLowTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var between = new WitSqlExpressionBetween
        {
            Expression = CreateIntLiteral(1),
            Low = CreateIntLiteral(1),
            High = CreateIntLiteral(10),
            IsNot = false
        };

        var result = evaluator.Evaluate(between, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateBetweenInclusiveHighTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var between = new WitSqlExpressionBetween
        {
            Expression = CreateIntLiteral(10),
            Low = CreateIntLiteral(1),
            High = CreateIntLiteral(10),
            IsNot = false
        };

        var result = evaluator.Evaluate(between, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateNotBetweenTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var between = new WitSqlExpressionBetween
        {
            Expression = CreateIntLiteral(15),
            Low = CreateIntLiteral(1),
            High = CreateIntLiteral(10),
            IsNot = true
        };

        var result = evaluator.Evaluate(between, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateBetweenWithNullReturnsNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var between = new WitSqlExpressionBetween
        {
            Expression = new WitSqlExpressionLiteral { Type = LiteralType.Null },
            Low = CreateIntLiteral(1),
            High = CreateIntLiteral(10),
            IsNot = false
        };

        var result = evaluator.Evaluate(between, CreateEmptyRow());

        Assert.That(result.IsNull, Is.True);
    }

    #endregion

    #region IN Tests

    [Test]
    public void EvaluateInFoundTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var inExpr = new WitSqlExpressionIn
        {
            Expression = CreateIntLiteral(2),
            Values = [CreateIntLiteral(1), CreateIntLiteral(2), CreateIntLiteral(3)],
            IsNot = false
        };

        var result = evaluator.Evaluate(inExpr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateInNotFoundTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var inExpr = new WitSqlExpressionIn
        {
            Expression = CreateIntLiteral(5),
            Values = [CreateIntLiteral(1), CreateIntLiteral(2), CreateIntLiteral(3)],
            IsNot = false
        };

        var result = evaluator.Evaluate(inExpr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateNotInTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var inExpr = new WitSqlExpressionIn
        {
            Expression = CreateIntLiteral(5),
            Values = [CreateIntLiteral(1), CreateIntLiteral(2), CreateIntLiteral(3)],
            IsNot = true
        };

        var result = evaluator.Evaluate(inExpr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateInWithStringsTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var inExpr = new WitSqlExpressionIn
        {
            Expression = CreateStringLiteral("B"),
            Values = [CreateStringLiteral("A"), CreateStringLiteral("B"), CreateStringLiteral("C")],
            IsNot = false
        };

        var result = evaluator.Evaluate(inExpr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateInNullExpressionReturnsNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var inExpr = new WitSqlExpressionIn
        {
            Expression = new WitSqlExpressionLiteral { Type = LiteralType.Null },
            Values = [CreateIntLiteral(1), CreateIntLiteral(2)],
            IsNot = false
        };

        var result = evaluator.Evaluate(inExpr, CreateEmptyRow());

        Assert.That(result.IsNull, Is.True);
    }

    #endregion

    #region LIKE Tests

    [Test]
    public void EvaluateLikePercentWildcardTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var like = new WitSqlExpressionLike
        {
            Expression = CreateStringLiteral("Hello World"),
            Pattern = CreateStringLiteral("Hello%"),
            IsNot = false
        };

        var result = evaluator.Evaluate(like, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateLikeUnderscoreWildcardTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var like = new WitSqlExpressionLike
        {
            Expression = CreateStringLiteral("cat"),
            Pattern = CreateStringLiteral("c_t"),
            IsNot = false
        };

        var result = evaluator.Evaluate(like, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateLikeNoMatchTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var like = new WitSqlExpressionLike
        {
            Expression = CreateStringLiteral("Hello"),
            Pattern = CreateStringLiteral("Bye%"),
            IsNot = false
        };

        var result = evaluator.Evaluate(like, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateNotLikeTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var like = new WitSqlExpressionLike
        {
            Expression = CreateStringLiteral("Hello"),
            Pattern = CreateStringLiteral("Bye%"),
            IsNot = true
        };

        var result = evaluator.Evaluate(like, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateLikeWithEscapeTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var like = new WitSqlExpressionLike
        {
            Expression = CreateStringLiteral("10%"),
            Pattern = CreateStringLiteral("10!%"),
            Escape = CreateStringLiteral("!"),
            IsNot = false
        };

        var result = evaluator.Evaluate(like, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateLikeCaseInsensitiveTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var like = new WitSqlExpressionLike
        {
            Expression = CreateStringLiteral("HELLO"),
            Pattern = CreateStringLiteral("hello"),
            IsNot = false
        };

        var result = evaluator.Evaluate(like, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateLikeNullReturnsNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var like = new WitSqlExpressionLike
        {
            Expression = new WitSqlExpressionLiteral { Type = LiteralType.Null },
            Pattern = CreateStringLiteral("%"),
            IsNot = false
        };

        var result = evaluator.Evaluate(like, CreateEmptyRow());

        Assert.That(result.IsNull, Is.True);
    }

    #endregion

    #region GLOB Tests

    [Test]
    public void EvaluateGlobStarWildcardTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var glob = new WitSqlExpressionGlob
        {
            Expression = CreateStringLiteral("file.txt"),
            Pattern = CreateStringLiteral("*.txt"),
            IsNot = false
        };

        var result = evaluator.Evaluate(glob, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateGlobQuestionMarkWildcardTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var glob = new WitSqlExpressionGlob
        {
            Expression = CreateStringLiteral("cat"),
            Pattern = CreateStringLiteral("c?t"),
            IsNot = false
        };

        var result = evaluator.Evaluate(glob, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateGlobNoMatchTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var glob = new WitSqlExpressionGlob
        {
            Expression = CreateStringLiteral("file.doc"),
            Pattern = CreateStringLiteral("*.txt"),
            IsNot = false
        };

        var result = evaluator.Evaluate(glob, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateNotGlobTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var glob = new WitSqlExpressionGlob
        {
            Expression = CreateStringLiteral("file.doc"),
            Pattern = CreateStringLiteral("*.txt"),
            IsNot = true
        };

        var result = evaluator.Evaluate(glob, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    #endregion

    #region CAST Tests

    [Test]
    public void EvaluateCastIntToStringTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var cast = new WitSqlExpressionCast
        {
            Expression = CreateIntLiteral(42),
            TargetType = new WitSqlDataType { TypeName = "TEXT" }
        };

        var result = evaluator.Evaluate(cast, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("42"));
    }

    [Test]
    public void EvaluateCastStringToIntTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var cast = new WitSqlExpressionCast
        {
            Expression = CreateStringLiteral("123"),
            TargetType = new WitSqlDataType { TypeName = "INTEGER" }
        };

        var result = evaluator.Evaluate(cast, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(123));
    }

    [Test]
    public void EvaluateCastIntToRealTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var cast = new WitSqlExpressionCast
        {
            Expression = CreateIntLiteral(42),
            TargetType = new WitSqlDataType { TypeName = "REAL" }
        };

        var result = evaluator.Evaluate(cast, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(42.0));
    }

    [Test]
    public void EvaluateCastStringToBoolTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var cast = new WitSqlExpressionCast
        {
            Expression = CreateStringLiteral("true"),
            TargetType = new WitSqlDataType { TypeName = "BOOL" }
        };

        var result = evaluator.Evaluate(cast, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateCastNullReturnsNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var cast = new WitSqlExpressionCast
        {
            Expression = new WitSqlExpressionLiteral { Type = LiteralType.Null },
            TargetType = new WitSqlDataType { TypeName = "INTEGER" }
        };

        var result = evaluator.Evaluate(cast, CreateEmptyRow());

        Assert.That(result.IsNull, Is.True);
    }

    #endregion

    #region Helper Methods

    private static WitSqlExpressionLiteral CreateIntLiteral(long value)
    {
        return new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = value };
    }

    private static WitSqlExpressionLiteral CreateStringLiteral(string value)
    {
        return new WitSqlExpressionLiteral { Type = LiteralType.String, Value = value };
    }

    private static WitSqlExpressionLiteral CreateBoolLiteral(bool value)
    {
        return new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = value };
    }

    #endregion
}
