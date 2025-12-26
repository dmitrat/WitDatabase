using OutWit.Database.Expressions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Expressions;

/// <summary>
/// Tests for function expression evaluation: numeric, string, date, conversion functions.
/// </summary>
[TestFixture]
public class ExpressionEvaluatorFunctionsTests : ExpressionEvaluatorTestsBase
{
    #region Numeric Functions Tests

    [Test]
    public void EvaluateAbsPositiveTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("ABS", CreateIntLiteral(42));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void EvaluateAbsNegativeTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("ABS", CreateIntLiteral(-42));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void EvaluateRoundTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("ROUND", CreateRealLiteral(3.14159), CreateIntLiteral(2));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(3.14).Within(0.001));
    }

    [Test]
    public void EvaluateRoundNoDecimalsTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("ROUND", CreateRealLiteral(3.7));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(4.0).Within(0.001));
    }

    [Test]
    public void EvaluateFloorTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("FLOOR", CreateRealLiteral(3.7));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(3.0).Within(0.001));
    }

    [Test]
    public void EvaluateCeilTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("CEIL", CreateRealLiteral(3.2));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(4.0).Within(0.001));
    }

    [Test]
    public void EvaluateTruncTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("TRUNC", CreateRealLiteral(3.9));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(3.0).Within(0.001));
    }

    [Test]
    public void EvaluateSqrtTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SQRT", CreateRealLiteral(16.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(4.0).Within(0.001));
    }

    [Test]
    public void EvaluatePowerTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("POWER", CreateRealLiteral(2.0), CreateRealLiteral(10.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(1024.0).Within(0.001));
    }

    [Test]
    public void EvaluateSignPositiveTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SIGN", CreateRealLiteral(42.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void EvaluateSignNegativeTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SIGN", CreateRealLiteral(-42.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(-1));
    }

    [Test]
    public void EvaluateSignZeroTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SIGN", CreateRealLiteral(0.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void EvaluateModTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("MOD", CreateIntLiteral(17), CreateIntLiteral(5));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(2));
    }

    [Test]
    public void EvaluatePiTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCallNoArgs("PI");

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(Math.PI).Within(0.0001));
    }

    [Test]
    public void EvaluateDegreesTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("DEGREES", CreateRealLiteral(Math.PI));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(180.0).Within(0.001));
    }

    [Test]
    public void EvaluateRadiansTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("RADIANS", CreateRealLiteral(180.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(Math.PI).Within(0.001));
    }

    #endregion

    #region Trigonometric Functions Tests

    [Test]
    public void EvaluateSinTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SIN", CreateRealLiteral(0.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void EvaluateCosTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("COS", CreateRealLiteral(0.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void EvaluateTanTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("TAN", CreateRealLiteral(0.0));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.EqualTo(0.0).Within(0.001));
    }

    #endregion

    #region String Functions Tests

    [Test]
    public void EvaluateLengthTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("LENGTH", CreateStringLiteral("Hello"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(5));
    }

    [Test]
    public void EvaluateUpperTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("UPPER", CreateStringLiteral("hello"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("HELLO"));
    }

    [Test]
    public void EvaluateLowerTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("LOWER", CreateStringLiteral("HELLO"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("hello"));
    }

    [Test]
    public void EvaluateTrimTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("TRIM", CreateStringLiteral("  hello  "));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("hello"));
    }

    [Test]
    public void EvaluateLtrimTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("LTRIM", CreateStringLiteral("  hello"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("hello"));
    }

    [Test]
    public void EvaluateRtrimTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("RTRIM", CreateStringLiteral("hello  "));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("hello"));
    }

    [Test]
    public void EvaluateSubstringWithLengthTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SUBSTRING", CreateStringLiteral("Hello World"), CreateIntLiteral(1), CreateIntLiteral(5));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Hello"));
    }

    [Test]
    public void EvaluateSubstringFromMiddleTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SUBSTR", CreateStringLiteral("Hello World"), CreateIntLiteral(7));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("World"));
    }

    [Test]
    public void EvaluateLeftTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("LEFT", CreateStringLiteral("Hello World"), CreateIntLiteral(5));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Hello"));
    }

    [Test]
    public void EvaluateRightTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("RIGHT", CreateStringLiteral("Hello World"), CreateIntLiteral(5));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("World"));
    }

    [Test]
    public void EvaluateReplaceTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("REPLACE", CreateStringLiteral("Hello World"), CreateStringLiteral("World"), CreateStringLiteral("Universe"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Hello Universe"));
    }

    [Test]
    public void EvaluateInstrFoundTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("INSTR", CreateStringLiteral("Hello World"), CreateStringLiteral("World"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(7)); // 1-based
    }

    [Test]
    public void EvaluateInstrNotFoundTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("INSTR", CreateStringLiteral("Hello"), CreateStringLiteral("xyz"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void EvaluateConcatTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("CONCAT", CreateStringLiteral("Hello"), CreateStringLiteral(" "), CreateStringLiteral("World"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Hello World"));
    }

    [Test]
    public void EvaluateConcatWsTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("CONCAT_WS", CreateStringLiteral(", "), CreateStringLiteral("A"), CreateStringLiteral("B"), CreateStringLiteral("C"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("A, B, C"));
    }

    [Test]
    public void EvaluateReverseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("REVERSE", CreateStringLiteral("Hello"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("olleH"));
    }

    [Test]
    public void EvaluateRepeatTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("REPEAT", CreateStringLiteral("ab"), CreateIntLiteral(3));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("ababab"));
    }

    [Test]
    public void EvaluateSpaceTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SPACE", CreateIntLiteral(5));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("     "));
    }

    [Test]
    public void EvaluateLpadTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("LPAD", CreateStringLiteral("42"), CreateIntLiteral(5), CreateStringLiteral("0"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("00042"));
    }

    [Test]
    public void EvaluateRpadTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("RPAD", CreateStringLiteral("hi"), CreateIntLiteral(5), CreateStringLiteral("-"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("hi---"));
    }

    #endregion

    #region Date/Time Functions Tests

    [Test]
    public void EvaluateNowTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var before = DateTime.UtcNow;
        var func = CreateFunctionCallNoArgs("NOW");

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        var after = DateTime.UtcNow;
        Assert.That(result.AsDateTime(), Is.InRange(before, after));
    }

    [Test]
    public void EvaluateYearTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("YEAR", CreateDateTimeLiteral(new DateTime(2024, 6, 15)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(2024));
    }

    [Test]
    public void EvaluateMonthTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("MONTH", CreateDateTimeLiteral(new DateTime(2024, 6, 15)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(6));
    }

    [Test]
    public void EvaluateDayTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("DAY", CreateDateTimeLiteral(new DateTime(2024, 6, 15)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(15));
    }

    [Test]
    public void EvaluateHourTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("HOUR", CreateDateTimeLiteral(new DateTime(2024, 1, 1, 14, 30, 45)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(14));
    }

    [Test]
    public void EvaluateMinuteTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("MINUTE", CreateDateTimeLiteral(new DateTime(2024, 1, 1, 14, 30, 45)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(30));
    }

    [Test]
    public void EvaluateSecondTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("SECOND", CreateDateTimeLiteral(new DateTime(2024, 1, 1, 14, 30, 45)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(45));
    }

    [Test]
    public void EvaluateQuarterTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("QUARTER", CreateDateTimeLiteral(new DateTime(2024, 6, 15)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(2));
    }

    [Test]
    public void EvaluateDateAddDaysTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("DATEADD",
            CreateStringLiteral("DAY"),
            CreateIntLiteral(7),
            CreateDateTimeLiteral(new DateTime(2024, 1, 1)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDateTime(), Is.EqualTo(new DateTime(2024, 1, 8)));
    }

    [Test]
    public void EvaluateDateAddMonthsTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("DATEADD",
            CreateStringLiteral("MONTH"),
            CreateIntLiteral(3),
            CreateDateTimeLiteral(new DateTime(2024, 1, 15)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDateTime(), Is.EqualTo(new DateTime(2024, 4, 15)));
    }

    [Test]
    public void EvaluateDateDiffDaysTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("DATEDIFF",
            CreateStringLiteral("DAY"),
            CreateDateTimeLiteral(new DateTime(2024, 1, 1)),
            CreateDateTimeLiteral(new DateTime(2024, 1, 15)));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(14));
    }

    #endregion

    #region Null Handling Functions Tests

    [Test]
    public void EvaluateCoalesceFirstNonNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("COALESCE",
            new WitSqlExpressionLiteral { Type = LiteralType.Null },
            CreateStringLiteral("default"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("default"));
    }

    [Test]
    public void EvaluateCoalesceAllNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("COALESCE",
            new WitSqlExpressionLiteral { Type = LiteralType.Null },
            new WitSqlExpressionLiteral { Type = LiteralType.Null });

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.IsNull, Is.True);
    }

    [Test]
    public void EvaluateNullifEqualTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("NULLIF", CreateIntLiteral(5), CreateIntLiteral(5));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.IsNull, Is.True);
    }

    [Test]
    public void EvaluateNullifNotEqualTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("NULLIF", CreateIntLiteral(5), CreateIntLiteral(10));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(5));
    }

    [Test]
    public void EvaluateIfnullNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("IFNULL",
            new WitSqlExpressionLiteral { Type = LiteralType.Null },
            CreateStringLiteral("default"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("default"));
    }

    [Test]
    public void EvaluateIfnullNotNullTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("IFNULL",
            CreateStringLiteral("value"),
            CreateStringLiteral("default"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("value"));
    }

    #endregion

    #region ID Generation Functions Tests

    [Test]
    public void EvaluateNewGuidTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCallNoArgs("NEWGUID");

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsGuid(), Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void EvaluateRandomNoArgsTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCallNoArgs("RANDOM");

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsDouble(), Is.InRange(0.0, 1.0));
    }

    [Test]
    public void EvaluateRandomWithRangeTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("RANDOM", CreateIntLiteral(1), CreateIntLiteral(10));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.InRange(1, 10));
    }

    #endregion

    #region Type Conversion Functions Tests

    [Test]
    public void EvaluateTypeofIntegerTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("TYPEOF", CreateIntLiteral(42));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Integer"));
    }

    [Test]
    public void EvaluateTypeofTextTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("TYPEOF", CreateStringLiteral("hello"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("Text"));
    }

    [Test]
    public void EvaluateToStringTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("TOSTRING", CreateIntLiteral(42));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("42"));
    }

    [Test]
    public void EvaluateToIntTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("TOINT", CreateStringLiteral("123"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(123));
    }

    [Test]
    public void EvaluateHexTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("HEX", CreateBlobLiteral([0xDE, 0xAD, 0xBE, 0xEF]));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("DEADBEEF"));
    }

    [Test]
    public void EvaluateUnhexTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("UNHEX", CreateStringLiteral("DEADBEEF"));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsBlob(), Is.EqualTo(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));
    }

    [Test]
    public void EvaluateBase64Test()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("BASE64", CreateBlobLiteral([0x48, 0x65, 0x6c, 0x6c, 0x6f])); // "Hello"

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("SGVsbG8="));
    }

    [Test]
    public void EvaluateUnbase64Test()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCall("UNBASE64", CreateStringLiteral("SGVsbG8="));

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsBlob(), Is.EqualTo(new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f }));
    }

    #endregion

    #region System Functions Tests

    [Test]
    public void EvaluateDatabaseTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCallNoArgs("DATABASE");

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("WitDB"));
    }

    [Test]
    public void EvaluateVersionTest()
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCallNoArgs("VERSION");

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsString(), Is.EqualTo("1.0.0"));
    }

    [Test]
    public void EvaluateChangesTest()
    {
        m_context.LastChangesCount = 42;
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCallNoArgs("CHANGES");

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void EvaluateLastInsertRowIdTest()
    {
        m_context.LastInsertRowId = 12345;
        var evaluator = new ExpressionEvaluator(m_context);
        var func = CreateFunctionCallNoArgs("LAST_INSERT_ROWID");

        var result = evaluator.Evaluate(func, CreateEmptyRow());

        Assert.That(result.AsInt64(), Is.EqualTo(12345));
    }

    #endregion

    #region Helper Methods

    private static WitSqlExpressionFunctionCall CreateFunctionCall(string name, params WitSqlExpression[] args)
    {
        return new WitSqlExpressionFunctionCall
        {
            FunctionName = name,
            Arguments = args.ToList()
        };
    }

    private static WitSqlExpressionFunctionCall CreateFunctionCallNoArgs(string name)
    {
        return new WitSqlExpressionFunctionCall
        {
            FunctionName = name,
            Arguments = null
        };
    }

    private static WitSqlExpressionLiteral CreateIntLiteral(long value)
    {
        return new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = value };
    }

    private static WitSqlExpressionLiteral CreateRealLiteral(double value)
    {
        return new WitSqlExpressionLiteral { Type = LiteralType.Real, Value = value };
    }

    private static WitSqlExpressionLiteral CreateStringLiteral(string value)
    {
        return new WitSqlExpressionLiteral { Type = LiteralType.String, Value = value };
    }

    private static WitSqlExpressionLiteral CreateBlobLiteral(byte[] value)
    {
        return new WitSqlExpressionLiteral { Type = LiteralType.Blob, Value = value };
    }

    private static WitSqlExpressionLiteral CreateDateTimeLiteral(DateTime value)
    {
        // For testing, we'll use string conversion which WitSqlValue can parse
        return new WitSqlExpressionLiteral { Type = LiteralType.String, Value = value.ToString("O") };
    }

    #endregion
}
