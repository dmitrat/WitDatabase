using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for expression parsing: operators, functions, literals.
/// </summary>
[TestFixture]
public class ExpressionParserTests
{
    #region Arithmetic

    [Test]
    public void ParseArithmeticPrecedence()
    {
        var expr = WitSql.ParseExpression("1 + 2 * 3");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var add = (WitSqlExpressionBinary)expr;
        Assert.That(add.Operator, Is.EqualTo(BinaryOperatorType.Add));
        Assert.That(add.Right, Is.InstanceOf<WitSqlExpressionBinary>());
    }

    [Test]
    public void ParseUnaryMinus()
    {
        var expr = WitSql.ParseExpression("-5");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionUnary>());
        var unary = (WitSqlExpressionUnary)expr;
        Assert.That(unary.Operator, Is.EqualTo(UnaryOperatorType.Negate));
    }

    [Test]
    public void ParseModulo()
    {
        var expr = WitSql.ParseExpression("10 % 3");
        var bin = (WitSqlExpressionBinary)expr;
        Assert.That(bin.Operator, Is.EqualTo(BinaryOperatorType.Modulo));
    }

    #endregion

    #region Comparison

    [Test]
    public void ParseComparison()
    {
        var expr = WitSql.ParseExpression("Age >= 18");
        var bin = (WitSqlExpressionBinary)expr;
        Assert.That(bin.Operator, Is.EqualTo(BinaryOperatorType.GreaterOrEqual));
    }

    [Test]
    public void ParseNotEqual()
    {
        var expr = WitSql.ParseExpression("Status <> 'deleted'");
        var bin = (WitSqlExpressionBinary)expr;
        Assert.That(bin.Operator, Is.EqualTo(BinaryOperatorType.NotEqual));
    }

    [Test]
    public void ParseIsNull()
    {
        var expr = WitSql.ParseExpression("DeletedAt IS NULL");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIsNull>());
        Assert.That(((WitSqlExpressionIsNull)expr).IsNot, Is.False);
    }

    [Test]
    public void ParseIsNotNull()
    {
        var expr = WitSql.ParseExpression("Email IS NOT NULL");
        Assert.That(((WitSqlExpressionIsNull)expr).IsNot, Is.True);
    }

    #endregion

    #region Logical

    [Test]
    public void ParseAndOr()
    {
        var expr = WitSql.ParseExpression("A AND B OR C");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var or = (WitSqlExpressionBinary)expr;
        Assert.That(or.Operator, Is.EqualTo(BinaryOperatorType.Or));
    }

    [Test]
    public void ParseNot()
    {
        var expr = WitSql.ParseExpression("NOT IsDeleted");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionUnary>());
        var unary = (WitSqlExpressionUnary)expr;
        Assert.That(unary.Operator, Is.EqualTo(UnaryOperatorType.Not));
    }

    #endregion

    #region BETWEEN/IN/LIKE/GLOB

    [Test]
    public void ParseBetween()
    {
        var expr = WitSql.ParseExpression("Age BETWEEN 18 AND 65");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBetween>());
        Assert.That(((WitSqlExpressionBetween)expr).IsNot, Is.False);
    }

    [Test]
    public void ParseNotBetween()
    {
        var expr = WitSql.ParseExpression("Price NOT BETWEEN 10 AND 100");
        Assert.That(((WitSqlExpressionBetween)expr).IsNot, Is.True);
    }

    [Test]
    public void ParseInList()
    {
        var expr = WitSql.ParseExpression("Status IN ('active', 'pending', 'new')");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIn>());
        Assert.That(((WitSqlExpressionIn)expr).Values, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseNotIn()
    {
        var expr = WitSql.ParseExpression("Id NOT IN (1, 2, 3)");
        Assert.That(((WitSqlExpressionIn)expr).IsNot, Is.True);
    }

    [Test]
    public void ParseLike()
    {
        var expr = WitSql.ParseExpression("Name LIKE 'John%'");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionLike>());
    }

    [Test]
    public void ParseNotLike()
    {
        var expr = WitSql.ParseExpression("Email NOT LIKE '%@spam.com'");
        Assert.That(((WitSqlExpressionLike)expr).IsNot, Is.True);
    }

    [Test]
    public void ParseGlob()
    {
        var expr = WitSql.ParseExpression("Filename GLOB '*.txt'");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionGlob>());
    }

    #endregion

    #region CASE/IIF

    [Test]
    public void ParseSearchedCase()
    {
        var expr = WitSql.ParseExpression(
            "CASE WHEN Status = 1 THEN 'Active' WHEN Status = 2 THEN 'Pending' ELSE 'Unknown' END");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionCase>());
        var caseExpr = (WitSqlExpressionCase)expr;
        Assert.That(caseExpr.WhenClauses, Has.Count.EqualTo(2));
        Assert.That(caseExpr.ElseResult, Is.Not.Null);
    }

    [Test]
    public void ParseSimpleCase()
    {
        var expr = WitSql.ParseExpression(
            "CASE Status WHEN 1 THEN 'Active' WHEN 2 THEN 'Pending' END");
        var caseExpr = (WitSqlExpressionCase)expr;
        Assert.That(caseExpr.Operand, Is.Not.Null);
    }

    [Test]
    public void ParseIif()
    {
        var expr = WitSql.ParseExpression("IIF(IsActive, 'Yes', 'No')");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIif>());
    }

    #endregion

    #region Bitwise

    [Test]
    public void ParseBitwiseAnd()
    {
        var expr = WitSql.ParseExpression("Flags & 15");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.BitwiseAnd));
    }

    [Test]
    public void ParseBitwiseOr()
    {
        var expr = WitSql.ParseExpression("Flags | 16");
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.BitwiseOr));
    }

    [Test]
    public void ParseBitwiseNot()
    {
        var expr = WitSql.ParseExpression("~Flags");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionUnary>());
        Assert.That(((WitSqlExpressionUnary)expr).Operator, Is.EqualTo(UnaryOperatorType.BitwiseNot));
    }

    [Test]
    public void ParseLeftShift()
    {
        var expr = WitSql.ParseExpression("Value << 2");
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.LeftShift));
    }

    [Test]
    public void ParseRightShift()
    {
        var expr = WitSql.ParseExpression("Value >> 2");
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.RightShift));
    }

    #endregion

    #region Functions

    [Test]
    public void ParseFunctionCall()
    {
        var expr = WitSql.ParseExpression("UPPER(Name)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionFunctionCall>());
        Assert.That(((WitSqlExpressionFunctionCall)expr).FunctionName, Is.EqualTo("UPPER"));
    }

    [Test]
    public void ParseCountStar()
    {
        var expr = WitSql.ParseExpression("COUNT(*)");
        Assert.That(((WitSqlExpressionFunctionCall)expr).IsStar, Is.True);
    }

    [Test]
    public void ParseCountDistinct()
    {
        var expr = WitSql.ParseExpression("COUNT(DISTINCT Status)");
        Assert.That(((WitSqlExpressionFunctionCall)expr).IsDistinct, Is.True);
    }

    [Test]
    public void ParseCoalesce()
    {
        var expr = WitSql.ParseExpression("COALESCE(Nickname, Username, 'Anonymous')");
        Assert.That(((WitSqlExpressionFunctionCall)expr).Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseNewGuid()
    {
        var expr = WitSql.ParseExpression("NEWGUID()");
        Assert.That(((WitSqlExpressionFunctionCall)expr).FunctionName, Is.EqualTo("NEWGUID"));
    }

    [Test]
    public void ParseCast()
    {
        var expr = WitSql.ParseExpression("CAST('123' AS INT)");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionCast>());
    }

    #endregion

    #region String Concatenation

    [Test]
    public void ParseConcat()
    {
        var expr = WitSql.ParseExpression("FirstName || ' ' || LastName");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        Assert.That(((WitSqlExpressionBinary)expr).Operator, Is.EqualTo(BinaryOperatorType.Concat));
    }

    #endregion

    #region Literals

    [Test]
    public void ParseIntegerLiteral()
    {
        var expr = WitSql.ParseExpression("12345");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.Integer));
        Assert.That(lit.Value, Is.EqualTo(12345L));
    }

    [Test]
    public void ParseRealLiteral()
    {
        var expr = WitSql.ParseExpression("3.14159");
        Assert.That(((WitSqlExpressionLiteral)expr).Type, Is.EqualTo(LiteralType.Real));
    }

    [Test]
    public void ParseStringLiteral()
    {
        var expr = WitSql.ParseExpression("'Hello, World!'");
        var lit = (WitSqlExpressionLiteral)expr;
        Assert.That(lit.Type, Is.EqualTo(LiteralType.String));
        Assert.That(lit.Value, Is.EqualTo("Hello, World!"));
    }

    [Test]
    public void ParseBooleanLiterals()
    {
        var trueExpr = (WitSqlExpressionLiteral)WitSql.ParseExpression("TRUE");
        var falseExpr = (WitSqlExpressionLiteral)WitSql.ParseExpression("FALSE");
        Assert.That(trueExpr.Value, Is.EqualTo(true));
        Assert.That(falseExpr.Value, Is.EqualTo(false));
    }

    [Test]
    public void ParseNullLiteral()
    {
        var expr = (WitSqlExpressionLiteral)WitSql.ParseExpression("NULL");
        Assert.That(expr.Type, Is.EqualTo(LiteralType.Null));
    }

    [Test]
    public void ParseBlobLiteral()
    {
        var expr = (WitSqlExpressionLiteral)WitSql.ParseExpression("X'48656C6C6F'");
        Assert.That(expr.Type, Is.EqualTo(LiteralType.Blob));
    }

    #endregion
}
