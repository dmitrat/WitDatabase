using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Values;

/// <summary>
/// Tests for WitSqlValue arithmetic operators and implicit conversions.
/// </summary>
[TestFixture]
public class WitSqlValueOperatorsTests
{
    #region Arithmetic Operators Tests

    [Test]
    public void ArithmeticOperatorsTest()
    {
        WitSqlValue a = 10;
        WitSqlValue b = 3;

        Assert.That((a + b).AsInt64(), Is.EqualTo(13));
        Assert.That((a - b).AsInt64(), Is.EqualTo(7));
        Assert.That((a * b).AsInt64(), Is.EqualTo(30));
        Assert.That((a % b).AsInt64(), Is.EqualTo(1));
        Assert.That((-a).AsInt64(), Is.EqualTo(-10));
    }

    [Test]
    public void DivisionOperatorTest()
    {
        WitSqlValue a = 10;
        WitSqlValue b = 2;

        Assert.That((a / b).AsInt64(), Is.EqualTo(5));
    }

    [Test]
    public void DivisionOperatorNotExactTest()
    {
        WitSqlValue a = 10;
        WitSqlValue b = 3;

        Assert.That((a / b).AsDouble(), Is.EqualTo(10.0 / 3.0).Within(0.0001));
    }

    #endregion

    #region Implicit Conversions Tests

    [Test]
    public void ImplicitConversionsTest()
    {
        WitSqlValue intValue = 42;
        WitSqlValue longValue = 42L;
        WitSqlValue doubleValue = 3.14;
        WitSqlValue stringValue = "hello";
        WitSqlValue boolValue = true;
        WitSqlValue decimalValue = 123.45m;
        WitSqlValue dateTimeValue = new DateTime(2024, 6, 15);
        WitSqlValue dateOnlyValue = new DateOnly(2024, 6, 15);
        WitSqlValue timeOnlyValue = new TimeOnly(10, 30);
        WitSqlValue timeSpanValue = TimeSpan.FromHours(2);
        WitSqlValue guidValue = Guid.Empty;

        Assert.That(intValue.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(longValue.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(doubleValue.Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(stringValue.Type, Is.EqualTo(WitSqlType.Text));
        Assert.That(boolValue.Type, Is.EqualTo(WitSqlType.Boolean));
        Assert.That(decimalValue.Type, Is.EqualTo(WitSqlType.Decimal));
        Assert.That(dateTimeValue.Type, Is.EqualTo(WitSqlType.DateTime));
        Assert.That(dateOnlyValue.Type, Is.EqualTo(WitSqlType.DateOnly));
        Assert.That(timeOnlyValue.Type, Is.EqualTo(WitSqlType.TimeOnly));
        Assert.That(timeSpanValue.Type, Is.EqualTo(WitSqlType.TimeSpan));
        Assert.That(guidValue.Type, Is.EqualTo(WitSqlType.Guid));
    }

    [Test]
    public void ImplicitConversionPreservesValueTest()
    {
        WitSqlValue intValue = 42;
        WitSqlValue doubleValue = 3.14;
        WitSqlValue stringValue = "hello";
        WitSqlValue boolValue = true;

        Assert.That(intValue.AsInt64(), Is.EqualTo(42));
        Assert.That(doubleValue.AsDouble(), Is.EqualTo(3.14));
        Assert.That(stringValue.AsString(), Is.EqualTo("hello"));
        Assert.That(boolValue.AsBool(), Is.True);
    }

    [Test]
    public void ImplicitConversionInExpressionsTest()
    {
        WitSqlValue result = WitSqlValue.FromInt(10) + 5;
        Assert.That(result.AsInt64(), Is.EqualTo(15));

        result = WitSqlValue.FromReal(10.0) + 2.5;
        Assert.That(result.AsDouble(), Is.EqualTo(12.5));
    }

    #endregion
}
