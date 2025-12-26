using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Values;

/// <summary>
/// Tests for WitSqlValue arithmetic, logical, and string operations.
/// </summary>
[TestFixture]
public class WitSqlValueOperationsTests
{
    #region Arithmetic Operations Tests

    [Test]
    public void AddIntegersTest()
    {
        var a = WitSqlValue.FromInt(10);
        var b = WitSqlValue.FromInt(5);

        var result = a.Add(b);

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(result.AsInt64(), Is.EqualTo(15));
    }

    [Test]
    public void AddRealsTest()
    {
        var a = WitSqlValue.FromReal(1.5);
        var b = WitSqlValue.FromReal(2.5);

        var result = a.Add(b);

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(result.AsDouble(), Is.EqualTo(4.0));
    }

    [Test]
    public void AddIntegerAndRealPromotesToRealTest()
    {
        var a = WitSqlValue.FromInt(10);
        var b = WitSqlValue.FromReal(2.5);

        var result = a.Add(b);

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(result.AsDouble(), Is.EqualTo(12.5));
    }

    [Test]
    public void AddDecimalsTest()
    {
        var a = WitSqlValue.FromDecimal(10.5m);
        var b = WitSqlValue.FromDecimal(5.3m);

        var result = a.Add(b);

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Decimal));
        Assert.That(result.AsDecimal(), Is.EqualTo(15.8m));
    }

    [Test]
    public void AddStringsTest()
    {
        var a = WitSqlValue.FromText("Hello, ");
        var b = WitSqlValue.FromText("World!");

        var result = a.Add(b);

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Text));
        Assert.That(result.AsString(), Is.EqualTo("Hello, World!"));
    }

    [Test]
    public void AddWithNullReturnsNullTest()
    {
        var a = WitSqlValue.FromInt(10);

        Assert.That(a.Add(WitSqlValue.Null).IsNull, Is.True);
        Assert.That(WitSqlValue.Null.Add(a).IsNull, Is.True);
    }

    [Test]
    public void SubtractIntegersTest()
    {
        var result = WitSqlValue.FromInt(10).Subtract(WitSqlValue.FromInt(3));

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(result.AsInt64(), Is.EqualTo(7));
    }

    [Test]
    public void MultiplyIntegersTest()
    {
        var result = WitSqlValue.FromInt(6).Multiply(WitSqlValue.FromInt(7));

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(result.AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void DivideIntegersExactTest()
    {
        var result = WitSqlValue.FromInt(10).Divide(WitSqlValue.FromInt(2));

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(result.AsInt64(), Is.EqualTo(5));
    }

    [Test]
    public void DivideIntegersNotExactReturnsRealTest()
    {
        var result = WitSqlValue.FromInt(10).Divide(WitSqlValue.FromInt(3));

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(result.AsDouble(), Is.EqualTo(10.0 / 3.0).Within(0.0001));
    }

    [Test]
    public void DivideByZeroReturnsNullTest()
    {
        var result = WitSqlValue.FromInt(10).Divide(WitSqlValue.FromInt(0));
        Assert.That(result.IsNull, Is.True);

        result = WitSqlValue.FromReal(10.0).Divide(WitSqlValue.FromReal(0.0));
        Assert.That(result.IsNull, Is.True);
    }

    [Test]
    public void ModuloTest()
    {
        var result = WitSqlValue.FromInt(10).Modulo(WitSqlValue.FromInt(3));

        Assert.That(result.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(result.AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void ModuloByZeroReturnsNullTest()
    {
        var result = WitSqlValue.FromInt(10).Modulo(WitSqlValue.FromInt(0));
        Assert.That(result.IsNull, Is.True);
    }

    [Test]
    public void NegateTest()
    {
        Assert.That(WitSqlValue.FromInt(42).Negate().AsInt64(), Is.EqualTo(-42));
        Assert.That(WitSqlValue.FromReal(3.14).Negate().AsDouble(), Is.EqualTo(-3.14));
        Assert.That(WitSqlValue.FromDecimal(100m).Negate().AsDecimal(), Is.EqualTo(-100m));
        Assert.That(WitSqlValue.Null.Negate().IsNull, Is.True);
    }

    #endregion

    #region Logical Operations Tests

    [Test]
    public void LogicalAndTest()
    {
        Assert.That(WitSqlValue.And(WitSqlValue.True, WitSqlValue.True).AsBool(), Is.True);
        Assert.That(WitSqlValue.And(WitSqlValue.True, WitSqlValue.False).AsBool(), Is.False);
        Assert.That(WitSqlValue.And(WitSqlValue.False, WitSqlValue.True).AsBool(), Is.False);
        Assert.That(WitSqlValue.And(WitSqlValue.False, WitSqlValue.False).AsBool(), Is.False);
    }

    [Test]
    public void LogicalOrTest()
    {
        Assert.That(WitSqlValue.Or(WitSqlValue.True, WitSqlValue.True).AsBool(), Is.True);
        Assert.That(WitSqlValue.Or(WitSqlValue.True, WitSqlValue.False).AsBool(), Is.True);
        Assert.That(WitSqlValue.Or(WitSqlValue.False, WitSqlValue.True).AsBool(), Is.True);
        Assert.That(WitSqlValue.Or(WitSqlValue.False, WitSqlValue.False).AsBool(), Is.False);
    }

    [Test]
    public void LogicalNotTest()
    {
        Assert.That(WitSqlValue.Not(WitSqlValue.True).AsBool(), Is.False);
        Assert.That(WitSqlValue.Not(WitSqlValue.False).AsBool(), Is.True);
    }

    [Test]
    public void LogicalOperationsWithNullReturnNullTest()
    {
        Assert.That(WitSqlValue.And(WitSqlValue.True, WitSqlValue.Null).IsNull, Is.True);
        Assert.That(WitSqlValue.Or(WitSqlValue.Null, WitSqlValue.False).IsNull, Is.True);
        Assert.That(WitSqlValue.Not(WitSqlValue.Null).IsNull, Is.True);
    }

    #endregion

    #region String Operations Tests

    [Test]
    public void ConcatStringsTest()
    {
        var a = WitSqlValue.FromText("Hello");
        var b = WitSqlValue.FromText(" World");

        Assert.That(a.Concat(b).AsString(), Is.EqualTo("Hello World"));
    }

    [Test]
    public void ConcatMixedTypesTest()
    {
        var str = WitSqlValue.FromText("Value: ");
        var num = WitSqlValue.FromInt(42);

        Assert.That(str.Concat(num).AsString(), Is.EqualTo("Value: 42"));
    }

    [Test]
    public void ConcatWithNullReturnsNullTest()
    {
        var str = WitSqlValue.FromText("Hello");

        Assert.That(str.Concat(WitSqlValue.Null).IsNull, Is.True);
    }

    #endregion
}
