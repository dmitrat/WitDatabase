using OutWit.Database.Expressions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Expressions;

/// <summary>
/// Tests for core expression evaluation: literals, column refs, parameters, operators.
/// </summary>
[TestFixture]
public class ExpressionEvaluatorCoreTests : ExpressionEvaluatorTestsBase
{
    #region Literal Tests

    [Test]
    public void EvaluateLiteralNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.Null, Value = null };

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        Assert.That(result.IsNull, Is.True);
    }

    [Test]
    public void EvaluateLiteralIntegerTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 42L };

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void EvaluateLiteralRealTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.Real, Value = 3.14 };

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(3.14).Within(0.001));
    }

    [Test]
    public void EvaluateLiteralStringTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.String, Value = "Hello" };

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Hello"));
    }

    [Test]
    public void EvaluateLiteralBooleanTrueTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = true };

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateLiteralBooleanFalseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = false };

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateLiteralBlobTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.Blob, Value = data };

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        Assert.That(result.AsBlob(), Is.EqualTo(data));
    }

    [Test]
    public void EvaluateLiteralCurrentTimestampTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.CurrentTimestamp };
        var before = DateTime.UtcNow;

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        var after = DateTime.UtcNow;
        Assert.That(result.AsDateTime(), Is.InRange(before, after));
    }

    [Test]
    public void EvaluateLiteralCurrentDateTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var literal = new WitSqlExpressionLiteral { Type = LiteralType.CurrentDate };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = evaluator.Evaluate(literal, CreateEmptyRow());

        Assert.That(result.AsDateOnly(), Is.EqualTo(today));
    }

    #endregion

    #region Column Reference Tests

    [Test]
    public void EvaluateColumnRefSimpleTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var colRef = new WitSqlExpressionColumnRef { ColumnName = "Name" };
        var row = CreateRowWithStrings(("Name", "John"));

        var result = evaluator.Evaluate(colRef, row);

        Assert.That(result.AsString(), Is.EqualTo("John"));
    }

    [Test]
    public void EvaluateColumnRefQualifiedTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var colRef = new WitSqlExpressionColumnRef { TableName = "Users", ColumnName = "Name" };
        var row = CreateRow(("Users.Name", WitSqlValue.FromText("Jane")));

        var result = evaluator.Evaluate(colRef, row);

        Assert.That(result.AsString(), Is.EqualTo("Jane"));
    }

    [Test]
    public void EvaluateColumnRefCaseInsensitiveTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var colRef = new WitSqlExpressionColumnRef { ColumnName = "NAME" };
        var row = CreateRowWithStrings(("name", "Test"));

        var result = evaluator.Evaluate(colRef, row);

        Assert.That(result.AsString(), Is.EqualTo("Test"));
    }

    [Test]
    public void EvaluateColumnRefNotFoundThrowsTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var colRef = new WitSqlExpressionColumnRef { ColumnName = "NonExistent" };
        var row = CreateRowWithStrings(("Name", "Value"));

        Assert.Throws<KeyNotFoundException>(() => evaluator.Evaluate(colRef, row));
    }

    #endregion

    #region Parameter Tests

    [Test]
    public void EvaluateParameterNamedTest()
    {
        m_context.Parameters["@userId"] = WitSqlValue.FromInt(123);
        var evaluator = new ExpressionEvaluator(m_context);
        var param = new WitSqlExpressionParameter
        {
            ParameterType = ParameterType.Named,
            Name = "userId"
        };

        var result = evaluator.Evaluate(param, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(123));
    }

    [Test]
    public void EvaluateParameterColonTest()
    {
        m_context.Parameters[":name"] = WitSqlValue.FromText("Alice");
        var evaluator = new ExpressionEvaluator(m_context);
        var param = new WitSqlExpressionParameter
        {
            ParameterType = ParameterType.Colon,
            Name = "name"
        };

        var result = evaluator.Evaluate(param, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void EvaluateParameterPositionalTest()
    {
        m_context.Parameters["?"] = WitSqlValue.FromReal(3.14);
        var evaluator = new ExpressionEvaluator(m_context);
        var param = new WitSqlExpressionParameter
        {
            ParameterType = ParameterType.Positional
        };

        var result = evaluator.Evaluate(param, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(3.14).Within(0.001));
    }

    [Test]
    public void EvaluateParameterNumberedTest()
    {
        m_context.Parameters["$1"] = WitSqlValue.FromText("first");
        var evaluator = new ExpressionEvaluator(m_context);
        var param = new WitSqlExpressionParameter
        {
            ParameterType = ParameterType.Numbered,
            Position = 1
        };

        var result = evaluator.Evaluate(param, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("first"));
    }

    [Test]
    public void EvaluateParameterNotFoundThrowsTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var param = new WitSqlExpressionParameter
        {
            ParameterType = ParameterType.Named,
            Name = "missing"
        };

        Assert.Throws<KeyNotFoundException>(() => evaluator.Evaluate(param, CreateEmptyRow()));
    }

    #endregion

    #region Binary Operator Tests - Arithmetic

    [Test]
    public void EvaluateBinaryAddTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(10L, BinaryOperatorType.Add, 5L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(15));
    }

    [Test]
    public void EvaluateBinarySubtractTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(10L, BinaryOperatorType.Subtract, 3L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(7));
    }

    [Test]
    public void EvaluateBinaryMultiplyTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(6L, BinaryOperatorType.Multiply, 7L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void EvaluateBinaryDivideTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(20L, BinaryOperatorType.Divide, 4L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(5));
    }

    [Test]
    public void EvaluateBinaryModuloTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(17L, BinaryOperatorType.Modulo, 5L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(2));
    }

    #endregion

    #region Binary Operator Tests - Comparison

    [Test]
    public void EvaluateBinaryEqualTrueTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(42L, BinaryOperatorType.Equal, 42L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateBinaryEqualFalseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(42L, BinaryOperatorType.Equal, 43L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateBinaryNotEqualTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(10L, BinaryOperatorType.NotEqual, 20L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateBinaryLessThanTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(5L, BinaryOperatorType.LessThan, 10L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateBinaryGreaterThanTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(20L, BinaryOperatorType.GreaterThan, 10L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    #endregion

    #region Binary Operator Tests - Logical

    [Test]
    public void EvaluateBinaryAndTrueTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryBoolExpr(true, BinaryOperatorType.And, true);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateBinaryAndFalseShortCircuitTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryBoolExpr(false, BinaryOperatorType.And, true);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateBinaryOrTrueShortCircuitTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryBoolExpr(true, BinaryOperatorType.Or, false);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateBinaryOrFalseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryBoolExpr(false, BinaryOperatorType.Or, false);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    #endregion

    #region Binary Operator Tests - Bitwise

    [Test]
    public void EvaluateBinaryBitwiseAndTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(0b1100L, BinaryOperatorType.BitwiseAnd, 0b1010L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(0b1000));
    }

    [Test]
    public void EvaluateBinaryBitwiseOrTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(0b1100L, BinaryOperatorType.BitwiseOr, 0b1010L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(0b1110));
    }

    [Test]
    public void EvaluateBinaryLeftShiftTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(1L, BinaryOperatorType.LeftShift, 4L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(16));
    }

    [Test]
    public void EvaluateBinaryRightShiftTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = CreateBinaryExpr(16L, BinaryOperatorType.RightShift, 2L);

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(4));
    }

    #endregion

    #region Binary Operator Tests - String

    [Test]
    public void EvaluateBinaryConcatTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionLiteral { Type = LiteralType.String, Value = "Hello" },
            Operator = BinaryOperatorType.Concat,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.String, Value = " World" }
        };

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Hello World"));
    }

    #endregion

    #region Unary Operator Tests

    [Test]
    public void EvaluateUnaryNegateTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = new WitSqlExpressionUnary
        {
            Operator = UnaryOperatorType.Negate,
            Operand = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 42L }
        };

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(-42));
    }

    [Test]
    public void EvaluateUnaryPlusTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = new WitSqlExpressionUnary
        {
            Operator = UnaryOperatorType.Plus,
            Operand = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 42L }
        };

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void EvaluateUnaryNotTrueTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = new WitSqlExpressionUnary
        {
            Operator = UnaryOperatorType.Not,
            Operand = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = true }
        };

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void EvaluateUnaryNotFalseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = new WitSqlExpressionUnary
        {
            Operator = UnaryOperatorType.Not,
            Operand = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = false }
        };

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void EvaluateUnaryBitwiseNotTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var expr = new WitSqlExpressionUnary
        {
            Operator = UnaryOperatorType.BitwiseNot,
            Operand = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 0L }
        };

        var result = evaluator.Evaluate(expr, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(-1));
    }

    #endregion

    #region Helper Methods

    private static WitSqlExpressionBinary CreateBinaryExpr(long left, BinaryOperatorType op, long right)
    {
        return new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = left },
            Operator = op,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = right }
        };
    }

    private static WitSqlExpressionBinary CreateBinaryBoolExpr(bool left, BinaryOperatorType op, bool right)
    {
        return new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = left },
            Operator = op,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = right }
        };
    }

    #endregion
}
