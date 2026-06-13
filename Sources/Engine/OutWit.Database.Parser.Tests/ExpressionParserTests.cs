using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for expression parsing: operators, functions, literals.
/// </summary>
[TestFixture]
public class ExpressionParserTests
{
    #region Arithmetic

    [Test]
    public void ParseArithmeticPrecedenceTest()
    {
        var expr = WitSql.ParseExpression("1 + 2 * 3");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var add = (WitSqlExpressionBinary)expr;
        Assert.That(add.Operator, Is.EqualTo(BinaryOperatorType.Add));
        Assert.That(add.Right, Is.InstanceOf<WitSqlExpressionBinary>());
    }

    [Test]
    public void ParseParenthesizedExpressionTest()
    {
        var expr = WitSql.ParseExpression("(1 + 2) * 3");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var mul = (WitSqlExpressionBinary)expr;
        Assert.That(mul.Operator, Is.EqualTo(BinaryOperatorType.Multiply));
        Assert.That(mul.Left, Is.InstanceOf<WitSqlExpressionBinary>());
    }

    [Test]
    public void ParseComplexPrecedenceTest()
    {
        // (1 + 2) * 3 - 4 / 2 should be ((1+2)*3) - (4/2)
        var expr = WitSql.ParseExpression("(1 + 2) * 3 - 4 / 2");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var sub = (WitSqlExpressionBinary)expr;
        Assert.That(sub.Operator, Is.EqualTo(BinaryOperatorType.Subtract));
    }

    [Test]
    public void ParseUnaryMinusTest()
    {
        var expr = WitSql.ParseExpression("-5");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionUnary>());
        var unary = (WitSqlExpressionUnary)expr;
        Assert.That(unary.Operator, Is.EqualTo(UnaryOperatorType.Negate));
    }

    [Test]
    public void ParseUnaryPlusTest()
    {
        var expr = WitSql.ParseExpression("+5");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionUnary>());
        var unary = (WitSqlExpressionUnary)expr;
        Assert.That(unary.Operator, Is.EqualTo(UnaryOperatorType.Plus));
    }

    [Test]
    public void ParseModuloTest()
    {
        var expr = WitSql.ParseExpression("10 % 3");
        var bin = (WitSqlExpressionBinary)expr;
        Assert.That(bin.Operator, Is.EqualTo(BinaryOperatorType.Modulo));
    }

    #endregion

    #region Comparison

    [Test]
    public void ParseComparisonTest()
    {
        var expr = WitSql.ParseExpression("Age >= 18");
        var bin = (WitSqlExpressionBinary)expr;
        Assert.That(bin.Operator, Is.EqualTo(BinaryOperatorType.GreaterOrEqual));
    }

    [Test]
    public void ParseNotEqualTest()
    {
        var expr = WitSql.ParseExpression("Status <> 'deleted'");
        var bin = (WitSqlExpressionBinary)expr;
        Assert.That(bin.Operator, Is.EqualTo(BinaryOperatorType.NotEqual));
    }

    [Test]
    public void ParseNotEqualAltSyntaxTest()
    {
        var expr = WitSql.ParseExpression("Status != 'deleted'");
        var bin = (WitSqlExpressionBinary)expr;
        Assert.That(bin.Operator, Is.EqualTo(BinaryOperatorType.NotEqual));
    }

    [Test]
    public void ParseIsNullTest()
    {
        var expr = WitSql.ParseExpression("DeletedAt IS NULL");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIsNull>());
        Assert.That(((WitSqlExpressionIsNull)expr).IsNot, Is.False);
    }

    [Test]
    public void ParseIsNotNullTest()
    {
        var expr = WitSql.ParseExpression("Email IS NOT NULL");
        Assert.That(((WitSqlExpressionIsNull)expr).IsNot, Is.True);
    }

    #endregion

    #region Logical

    [Test]
    public void ParseAndOrTest()
    {
        var expr = WitSql.ParseExpression("A AND B OR C");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var or = (WitSqlExpressionBinary)expr;
        Assert.That(or.Operator, Is.EqualTo(BinaryOperatorType.Or));
    }

    [Test]
    public void ParseNotTest()
    {
        var expr = WitSql.ParseExpression("NOT IsDeleted");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionUnary>());
        var unary = (WitSqlExpressionUnary)expr;
        Assert.That(unary.Operator, Is.EqualTo(UnaryOperatorType.Not));
    }

    [Test]
    public void ParseComplexLogicalWithParenthesesTest()
    {
        var expr = WitSql.ParseExpression("(A OR B) AND (C OR D)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var and = (WitSqlExpressionBinary)expr;
        Assert.That(and.Operator, Is.EqualTo(BinaryOperatorType.And));
        Assert.That(and.Left, Is.InstanceOf<WitSqlExpressionBinary>());
        Assert.That(and.Right, Is.InstanceOf<WitSqlExpressionBinary>());
    }

    #endregion

    #region BETWEEN/IN/LIKE/GLOB

    [Test]
    public void ParseBetweenTest()
    {
        var expr = WitSql.ParseExpression("Age BETWEEN 18 AND 65");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBetween>());
        Assert.That(((WitSqlExpressionBetween)expr).IsNot, Is.False);
    }

    [Test]
    public void ParseNotBetweenTest()
    {
        var expr = WitSql.ParseExpression("Price NOT BETWEEN 10 AND 100");
        Assert.That(((WitSqlExpressionBetween)expr).IsNot, Is.True);
    }

    [Test]
    public void ParseInListTest()
    {
        var expr = WitSql.ParseExpression("Status IN ('active', 'pending', 'new')");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIn>());
        Assert.That(((WitSqlExpressionIn)expr).Values, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseNotInTest()
    {
        var expr = WitSql.ParseExpression("Id NOT IN (1, 2, 3)");
        Assert.That(((WitSqlExpressionIn)expr).IsNot, Is.True);
    }

    [Test]
    public void ParseLikeTest()
    {
        var expr = WitSql.ParseExpression("Name LIKE 'John%'");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionLike>());
    }

    [Test]
    public void ParseNotLikeTest()
    {
        var expr = WitSql.ParseExpression("Email NOT LIKE '%@spam.com'");
        Assert.That(((WitSqlExpressionLike)expr).IsNot, Is.True);
    }

    [Test]
    public void ParseLikeWithEscapeTest()
    {
        var expr = WitSql.ParseExpression("Name LIKE '100\\%%' ESCAPE '\\'");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionLike>());
        var like = (WitSqlExpressionLike)expr;
        Assert.That(like.Escape, Is.Not.Null);
    }

    [Test]
    public void ParseGlobTest()
    {
        var expr = WitSql.ParseExpression("Filename GLOB '*.txt'");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionGlob>());
    }

    [Test]
    public void ParseNotGlobTest()
    {
        var expr = WitSql.ParseExpression("Filename NOT GLOB '*.tmp'");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionGlob>());
        Assert.That(((WitSqlExpressionGlob)expr).IsNot, Is.True);
    }

    #endregion

    #region CASE/IIF

    [Test]
    public void ParseSearchedCaseTest()
    {
        var expr = WitSql.ParseExpression(
            "CASE WHEN Status = 1 THEN 'Active' WHEN Status = 2 THEN 'Pending' ELSE 'Unknown' END");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionCase>());
        var caseExpr = (WitSqlExpressionCase)expr;
        Assert.That(caseExpr.WhenClauses, Has.Count.EqualTo(2));
        Assert.That(caseExpr.ElseResult, Is.Not.Null);
    }

    [Test]
    public void ParseSimpleCaseTest()
    {
        var expr = WitSql.ParseExpression(
            "CASE Status WHEN 1 THEN 'Active' WHEN 2 THEN 'Pending' END");
        var caseExpr = (WitSqlExpressionCase)expr;
        Assert.That(caseExpr.Operand, Is.Not.Null);
    }

    [Test]
    public void ParseNestedCaseTest()
    {
        var expr = WitSql.ParseExpression(@"
            CASE 
                WHEN Category = 1 THEN 
                    CASE WHEN SubCategory = 'A' THEN 'Cat1-A' ELSE 'Cat1-Other' END
                ELSE 'Other' 
            END");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionCase>());
        var caseExpr = (WitSqlExpressionCase)expr;
        Assert.That(caseExpr.WhenClauses[0].Then, Is.InstanceOf<WitSqlExpressionCase>());
    }

    [Test]
    public void ParseIifTest()
    {
        var expr = WitSql.ParseExpression("IIF(IsActive, 'Yes', 'No')");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIif>());
    }

    #endregion

    #region Bitwise

    [Test]
    public void ParseBitwiseAndTest()
    {
        var expr = WitSql.ParseExpression("Flags & 15");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.BitwiseAnd));
    }

    [Test]
    public void ParseBitwiseOrTest()
    {
        var expr = WitSql.ParseExpression("Flags | 16");
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.BitwiseOr));
    }

    [Test]
    public void ParseBitwiseNotTest()
    {
        var expr = WitSql.ParseExpression("~Flags");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionUnary>());
        Assert.That(((WitSqlExpressionUnary)expr).Operator, Is.EqualTo(UnaryOperatorType.BitwiseNot));
    }

    [Test]
    public void ParseLeftShiftTest()
    {
        var expr = WitSql.ParseExpression("Value << 2");
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.LeftShift));
    }

    [Test]
    public void ParseRightShiftTest()
    {
        var expr = WitSql.ParseExpression("Value >> 2");
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.RightShift));
    }

    #endregion

    #region Aggregate Functions

    [Test]
    public void ParseFunctionCallTest()
    {
        var expr = WitSql.ParseExpression("UPPER(Name)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionFunctionCall>());
        Assert.That(((WitSqlExpressionFunctionCall)expr).FunctionName, Is.EqualTo("UPPER"));
    }

    [Test]
    public void ParseCountStarTest()
    {
        var expr = WitSql.ParseExpression("COUNT(*)");
        Assert.That(((WitSqlExpressionFunctionCall)expr).IsStar, Is.True);
    }

    [Test]
    public void ParseCountDistinctTest()
    {
        var expr = WitSql.ParseExpression("COUNT(DISTINCT Status)");
        Assert.That(((WitSqlExpressionFunctionCall)expr).IsDistinct, Is.True);
    }

    [Test]
    public void ParseSumFunctionTest()
    {
        var expr = WitSql.ParseExpression("SUM(Amount)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SUM"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseAvgFunctionTest()
    {
        var expr = WitSql.ParseExpression("AVG(Price)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("AVG"));
    }

    [Test]
    public void ParseMinFunctionTest()
    {
        var expr = WitSql.ParseExpression("MIN(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("MIN"));
    }

    [Test]
    public void ParseMaxFunctionTest()
    {
        var expr = WitSql.ParseExpression("MAX(Price)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("MAX"));
    }

    #endregion

    #region String Functions

    [Test]
    public void ParseLowerFunctionTest()
    {
        var expr = WitSql.ParseExpression("LOWER(Email)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LOWER"));
    }

    [Test]
    public void ParseLengthFunctionTest()
    {
        var expr = WitSql.ParseExpression("LENGTH(Name)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LENGTH"));
    }

    [Test]
    public void ParseSubstrFunctionTest()
    {
        var expr = WitSql.ParseExpression("SUBSTR(Name, 1, 10)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SUBSTR"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseTrimFunctionTest()
    {
        var expr = WitSql.ParseExpression("TRIM(Input)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("TRIM"));
    }

    [Test]
    public void ParseReplaceFunctionTest()
    {
        var expr = WitSql.ParseExpression("REPLACE(Content, 'old', 'new')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("REPLACE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseLeftFunctionTest()
    {
        var expr = WitSql.ParseExpression("LEFT(Title, 20)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LEFT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseRightFunctionTest()
    {
        var expr = WitSql.ParseExpression("RIGHT(Code, 4)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("RIGHT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseLeftFunctionInSelectTest()
    {
        var stmt = WitSql.ParseStatement("SELECT LEFT(Name, 10) AS ShortName FROM Users");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList[0].Expression, Is.InstanceOf<WitSqlExpressionFunctionCall>());
        var func = (WitSqlExpressionFunctionCall)select.SelectList[0].Expression!;
        Assert.That(func.FunctionName, Is.EqualTo("LEFT"));
    }

    [Test]
    public void ParseLeftJoinStillWorksTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users LEFT JOIN Orders ON Users.Id = Orders.UserId");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseRightJoinStillWorksTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users RIGHT JOIN Orders ON Users.Id = Orders.UserId");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseCoalesceTest()
    {
        var expr = WitSql.ParseExpression("COALESCE(Nickname, Username, 'Anonymous')");
        Assert.That(((WitSqlExpressionFunctionCall)expr).Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseNullifFunctionTest()
    {
        var expr = WitSql.ParseExpression("NULLIF(Value, 0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("NULLIF"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    #endregion

    #region Math Functions

    [Test]
    public void ParseAbsFunctionTest()
    {
        var expr = WitSql.ParseExpression("ABS(-5)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("ABS"));
    }

    [Test]
    public void ParseRoundFunctionTest()
    {
        var expr = WitSql.ParseExpression("ROUND(Price, 2)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("ROUND"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseFloorFunctionTest()
    {
        var expr = WitSql.ParseExpression("FLOOR(3.7)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("FLOOR"));
    }

    [Test]
    public void ParseCeilFunctionTest()
    {
        var expr = WitSql.ParseExpression("CEIL(3.2)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("CEIL"));
    }

    #endregion

    #region Date Functions

    [Test]
    public void ParseNowFunctionTest()
    {
        var expr = WitSql.ParseExpression("NOW()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("NOW"));
        Assert.That(func.Arguments, Is.Null.Or.Empty);
    }

    [Test]
    public void ParseNewGuidTest()
    {
        var expr = WitSql.ParseExpression("NEWGUID()");
        Assert.That(((WitSqlExpressionFunctionCall)expr).FunctionName, Is.EqualTo("NEWGUID"));
    }

    #endregion

    #region Window Functions

    [Test]
    public void ParseRowNumberWindowFunctionTest()
    {
        var expr = WitSql.ParseExpression("ROW_NUMBER() OVER (ORDER BY Id)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("ROW_NUMBER"));
        Assert.That(func.Over, Is.Not.Null);
        Assert.That(func.Over!.OrderBy, Is.Not.Null);
    }

    [Test]
    public void ParseRankWindowFunctionTest()
    {
        var expr = WitSql.ParseExpression("RANK() OVER (ORDER BY Score DESC)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("RANK"));
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseDenseRankWindowFunctionTest()
    {
        var expr = WitSql.ParseExpression("DENSE_RANK() OVER (PARTITION BY Department ORDER BY Salary DESC)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DENSE_RANK"));
        Assert.That(func.Over, Is.Not.Null);
        Assert.That(func.Over!.PartitionBy, Is.Not.Null);
        Assert.That(func.Over.OrderBy, Is.Not.Null);
    }

    [Test]
    public void ParseLagWindowFunctionTest()
    {
        var expr = WitSql.ParseExpression("LAG(Price, 1) OVER (PARTITION BY Category ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LAG"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseLeadWindowFunctionTest()
    {
        var expr = WitSql.ParseExpression("LEAD(Price) OVER (PARTITION BY Category ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LEAD"));
        Assert.That(func.Over, Is.Not.Null);
        Assert.That(func.Over!.PartitionBy, Is.Not.Null);
    }

    [Test]
    public void ParseSumOverPartitionTest()
    {
        var expr = WitSql.ParseExpression("SUM(Amount) OVER (PARTITION BY UserId)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SUM"));
        Assert.That(func.Over, Is.Not.Null);
        Assert.That(func.Over!.PartitionBy, Has.Count.EqualTo(1));
    }

    #endregion

    #region CAST

    [Test]
    public void ParseCastTest()
    {
        var expr = WitSql.ParseExpression("CAST('123' AS INT)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionCast>());
    }

    [Test]
    public void ParseCastToVarcharTest()
    {
        var expr = WitSql.ParseExpression("CAST(Price AS VARCHAR(20))");
        var cast = (WitSqlExpressionCast)expr;
        Assert.That(cast.TargetType.TypeName, Is.EqualTo("VARCHAR"));
        Assert.That(cast.TargetType.Length, Is.EqualTo(20));
    }

    #endregion

    #region String Concatenation

    [Test]
    public void ParseConcatTest()
    {
        var expr = WitSql.ParseExpression("FirstName || ' ' || LastName");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.Concat));
    }

    #endregion

    #region Literals

    [Test]
    public void ParseIntegerLiteralTest()
    {
        var expr = WitSql.ParseExpression("12345");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.Integer));
        Assert.That(lit.Value, Is.EqualTo(12345L));
    }

    [Test]
    public void ParseRealLiteralTest()
    {
        var expr = WitSql.ParseExpression("3.14159");
        Assert.That(((WitSqlExpressionLiteral)expr).Type, Is.EqualTo(LiteralType.Real));
    }

    [Test]
    public void ParseScientificNotationLiteralTest()
    {
        var expr = WitSql.ParseExpression("1.5e10");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.Real));
    }

    [Test]
    public void ParseNegativeScientificNotationTest()
    {
        var expr = WitSql.ParseExpression("2.5E-3");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.Real));
    }

    [Test]
    public void ParseStringLiteralTest()
    {
        var expr = WitSql.ParseExpression("'Hello, World!'");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.String));
        Assert.That(lit.Value, Is.EqualTo("Hello, World!"));
    }

    [Test]
    public void ParseEscapedQuotesInStringTest()
    {
        var expr = WitSql.ParseExpression("'It''s a test'");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.String));
        Assert.That(lit.Value, Is.EqualTo("It's a test"));
    }

    [Test]
    public void ParseBooleanLiteralsTest()
    {
        var trueExpr = (WitSqlExpressionLiteral)WitSql.ParseExpression("TRUE");
        var falseExpr = (WitSqlExpressionLiteral)WitSql.ParseExpression("FALSE");
        Assert.That(trueExpr.Value, Is.EqualTo(true));
        Assert.That(falseExpr.Value, Is.EqualTo(false));
    }

    [Test]
    public void ParseNullLiteralTest()
    {
        var expr = (WitSqlExpressionLiteral)WitSql.ParseExpression("NULL");
        Assert.That(expr.Type, Is.EqualTo(LiteralType.Null));
    }

    [Test]
    public void ParseBlobLiteralTest()
    {
        var expr = (WitSqlExpressionLiteral)WitSql.ParseExpression("X'48656C6C6F'");
        Assert.That(expr.Type, Is.EqualTo(LiteralType.Blob));
    }

    [Test]
    public void ParseCurrentTimestampLiteralTest()
    {
        var expr = WitSql.ParseExpression("CURRENT_TIMESTAMP");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.CurrentTimestamp));
    }

    [Test]
    public void ParseCurrentDateLiteralTest()
    {
        var expr = WitSql.ParseExpression("CURRENT_DATE");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.CurrentDate));
    }

    [Test]
    public void ParseCurrentTimeLiteralTest()
    {
        var expr = WitSql.ParseExpression("CURRENT_TIME");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.CurrentTime));
    }

    #endregion

    #region EXISTS Expression

    [Test]
    public void ParseExistsSubqueryTest()
    {
        var expr = WitSql.ParseExpression("EXISTS (SELECT 1 FROM Orders WHERE UserId = 1)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionExists>());
        var exists = (WitSqlExpressionExists)expr;
        Assert.That(exists.IsNot, Is.False);
        Assert.That(exists.Query, Is.Not.Null);
    }

    [Test]
    public void ParseNotExistsSubqueryTest()
    {
        var expr = WitSql.ParseExpression("NOT EXISTS (SELECT 1 FROM Bans WHERE UserId = Users.Id)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionExists>());
        var exists = (WitSqlExpressionExists)expr;
        Assert.That(exists.IsNot, Is.True);
    }

    #endregion

    #region Parameters

    [Test]
    public void ParseNamedParameterTest()
    {
        var expr = WitSql.ParseExpression("@userId");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionParameter>());
        var param = (WitSqlExpressionParameter)expr;
        Assert.That(param.ParameterType, Is.EqualTo(ParameterType.Named));
        Assert.That(param.Name, Is.EqualTo("userId"));
    }

    [Test]
    public void ParseColonParameterTest()
    {
        var expr = WitSql.ParseExpression(":name");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionParameter>());
        var param = (WitSqlExpressionParameter)expr;
        Assert.That(param.ParameterType, Is.EqualTo(ParameterType.Colon));
        Assert.That(param.Name, Is.EqualTo("name"));
    }

    [Test]
    public void ParsePositionalParameterTest()
    {
        var expr = WitSql.ParseExpression("?");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionParameter>());
        var param = (WitSqlExpressionParameter)expr;
        Assert.That(param.ParameterType, Is.EqualTo(ParameterType.Positional));
    }

    [Test]
    public void ParseDollarNamedParameterTest()
    {
        var expr = WitSql.ParseExpression("$id");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionParameter>());
        var param = (WitSqlExpressionParameter)expr;
        Assert.That(param.ParameterType, Is.EqualTo(ParameterType.DollarNamed));
        Assert.That(param.Name, Is.EqualTo("id"));
    }

    [Test]
    public void ParseNumberedParameterTest()
    {
        var expr = WitSql.ParseExpression("$1");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionParameter>());
        var param = (WitSqlExpressionParameter)expr;
        Assert.That(param.ParameterType, Is.EqualTo(ParameterType.Numbered));
        Assert.That(param.Position, Is.EqualTo(1));
    }

    [Test]
    public void ParseStatementWithDollarNamedParametersTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = $id");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseStatementWithNamedParametersTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Id = @id AND Status = @status");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseStatementWithPositionalParametersTest()
    {
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email) VALUES (?, ?)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementInsert>());
    }

    #endregion

    #region Quantified Expressions (ANY/SOME/ALL)

    [Test]
    public void ParseAnyWithEqualTest()
    {
        var expr = WitSql.ParseExpression("Price = ANY (SELECT Price FROM Products)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionQuantified>());
        var quantified = (WitSqlExpressionQuantified)expr;
        Assert.That(quantified.Operator, Is.EqualTo(BinaryOperatorType.Equal));
        Assert.That(quantified.QuantifierType, Is.EqualTo(QuantifierType.Any));
        Assert.That(quantified.Subquery, Is.Not.Null);
    }

    [Test]
    public void ParseSomeWithEqualTest()
    {
        var expr = WitSql.ParseExpression("Value = SOME (SELECT Value FROM Items)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionQuantified>());
        var quantified = (WitSqlExpressionQuantified)expr;
        Assert.That(quantified.QuantifierType, Is.EqualTo(QuantifierType.Some));
    }

    [Test]
    public void ParseAllWithGreaterThanTest()
    {
        var expr = WitSql.ParseExpression("Score > ALL (SELECT Score FROM Competitors)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionQuantified>());
        var quantified = (WitSqlExpressionQuantified)expr;
        Assert.That(quantified.Operator, Is.EqualTo(BinaryOperatorType.GreaterThan));
        Assert.That(quantified.QuantifierType, Is.EqualTo(QuantifierType.All));
    }

    [Test]
    public void ParseAnyWithLessThanOrEqualTest()
    {
        var expr = WitSql.ParseExpression("Amount <= ANY (SELECT Budget FROM Departments)");
        var quantified = (WitSqlExpressionQuantified)expr;
        Assert.That(quantified.Operator, Is.EqualTo(BinaryOperatorType.LessOrEqual));
        Assert.That(quantified.QuantifierType, Is.EqualTo(QuantifierType.Any));
    }

    [Test]
    public void ParseAllWithNotEqualTest()
    {
        var expr = WitSql.ParseExpression("Status <> ALL (SELECT Status FROM BlockedItems)");
        var quantified = (WitSqlExpressionQuantified)expr;
        Assert.That(quantified.Operator, Is.EqualTo(BinaryOperatorType.NotEqual));
        Assert.That(quantified.QuantifierType, Is.EqualTo(QuantifierType.All));
    }

    [Test]
    public void ParseAnyWithNotEqualAltSyntaxTest()
    {
        var expr = WitSql.ParseExpression("Id != ANY (SELECT BlockedId FROM Blocklist)");
        var quantified = (WitSqlExpressionQuantified)expr;
        Assert.That(quantified.Operator, Is.EqualTo(BinaryOperatorType.NotEqual));
    }

    [Test]
    public void ParseQuantifiedInWhereClauseTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Orders WHERE Total > ALL (SELECT Average FROM Statistics)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.WhereClause, Is.InstanceOf<WitSqlExpressionQuantified>());
    }

    #endregion

    #region JSON Functions

    [Test]
    public void ParseJsonValueFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_VALUE(Data, '$.name')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_VALUE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseJsonQueryFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_QUERY(Data, '$.items')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_QUERY"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseJsonExtractFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_EXTRACT(Payload, '$.user.id')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_EXTRACT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseJsonSetFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_SET(Data, '$.status', 'active')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_SET"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseJsonInsertFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_INSERT(Data, '$.newField', 123)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_INSERT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseJsonReplaceFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_REPLACE(Data, '$.name', 'NewName')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_REPLACE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseJsonRemoveFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_REMOVE(Data, '$.obsolete')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_REMOVE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseJsonTypeFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_TYPE(Data)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_TYPE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseJsonValidFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_VALID(RawText)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_VALID"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseJsonArrayFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_ARRAY(1, 2, 'three', 4.0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_ARRAY"));
        Assert.That(func.Arguments, Has.Count.EqualTo(4));
    }

    [Test]
    public void ParseJsonObjectFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_OBJECT('name', Name, 'id', Id)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_OBJECT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(4));
    }

    [Test]
    public void ParseJsonFunctionInSelectTest()
    {
        var stmt = WitSql.ParseStatement("SELECT JSON_VALUE(Profile, '$.email') AS Email FROM Users");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList[0].Expression, Is.InstanceOf<WitSqlExpressionFunctionCall>());
    }

    [Test]
    public void ParseJsonFunctionInWhereTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE JSON_VALUE(Settings, '$.theme') = 'dark'");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion

    #region Collation

    [Test]
    public void ParseCollateExpressionTest()
    {
        var expr = WitSql.ParseExpression("Name COLLATE NOCASE");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionCollate>());
        var collate = (WitSqlExpressionCollate)expr;
        Assert.That(collate.CollationName, Is.EqualTo("NOCASE"));
        Assert.That(collate.Operand, Is.InstanceOf<WitSqlExpressionColumnRef>());
    }

    [Test]
    public void ParseCollateBinaryTest()
    {
        var expr = WitSql.ParseExpression("Email COLLATE BINARY");
        var collate = (WitSqlExpressionCollate)expr;
        Assert.That(collate.CollationName, Is.EqualTo("BINARY"));
    }

    [Test]
    public void ParseCollateUnicodeTest()
    {
        var expr = WitSql.ParseExpression("Title COLLATE UNICODE");
        var collate = (WitSqlExpressionCollate)expr;
        Assert.That(collate.CollationName, Is.EqualTo("UNICODE"));
    }

    [Test]
    public void ParseCollateUnicodeCiTest()
    {
        var expr = WitSql.ParseExpression("Description COLLATE UNICODE_CI");
        var collate = (WitSqlExpressionCollate)expr;
        Assert.That(collate.CollationName, Is.EqualTo("UNICODE_CI"));
    }

    [Test]
    public void ParseCollateInComparisonTest()
    {
        var expr = WitSql.ParseExpression("Name COLLATE NOCASE = 'john'");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var binary = (WitSqlExpressionBinary)expr;
        Assert.That(binary.Left, Is.InstanceOf<WitSqlExpressionCollate>());
    }

    [Test]
    public void ParseCollateInSelectTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Name COLLATE NOCASE FROM Users");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList[0].Expression, Is.InstanceOf<WitSqlExpressionCollate>());
    }

    #endregion
}
