using OutWit.Database.Types;
using System.Text.Json;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Values;

/// <summary>
/// Tests for WitSqlValue factory methods (FromInt, FromText, FromObject, etc.).
/// </summary>
[TestFixture]
public class WitSqlValueFactoryTests
{
    #region Factory Methods Tests

    [Test]
    public void FromIntCreatesIntegerTypeTest()
    {
        var value = WitSqlValue.FromInt(42);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(value.AsInt64(), Is.EqualTo(42));
        Assert.That(value.IsNull, Is.False);
    }

    [Test]
    public void FromRealCreatesRealTypeTest()
    {
        var value = WitSqlValue.FromReal(3.14);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(value.AsDouble(), Is.EqualTo(3.14));
    }

    [Test]
    public void FromTextCreatesTextTypeTest()
    {
        var value = WitSqlValue.FromText("hello");

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Text));
        Assert.That(value.AsString(), Is.EqualTo("hello"));
    }

    [Test]
    public void FromTextThrowsOnNullTest()
    {
        Assert.Throws<ArgumentNullException>(() => WitSqlValue.FromText(null!));
    }

    [Test]
    public void FromBlobCreatesBlobTypeTest()
    {
        byte[] data = [1, 2, 3, 4, 5];
        var value = WitSqlValue.FromBlob(data);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Blob));
        Assert.That(value.AsBlob(), Is.EqualTo(data));
    }

    [Test]
    public void FromBlobThrowsOnNullTest()
    {
        Assert.Throws<ArgumentNullException>(() => WitSqlValue.FromBlob(null!));
    }

    [Test]
    public void FromBoolCreatesBooleanTypeTest()
    {
        var trueValue = WitSqlValue.FromBool(true);
        var falseValue = WitSqlValue.FromBool(false);

        Assert.That(trueValue.Type, Is.EqualTo(WitSqlType.Boolean));
        Assert.That(trueValue.AsBool(), Is.True);
        Assert.That(falseValue.AsBool(), Is.False);
    }

    [Test]
    public void FromDecimalCreatesDecimalTypeTest()
    {
        var value = WitSqlValue.FromDecimal(123.456m);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Decimal));
        Assert.That(value.AsDecimal(), Is.EqualTo(123.456m));
    }

    [Test]
    public void FromDateTimeCreatesDateTimeTypeTest()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0);
        var value = WitSqlValue.FromDateTime(dt);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.DateTime));
        Assert.That(value.AsDateTime(), Is.EqualTo(dt));
    }

    [Test]
    public void FromDateOnlyCreatesDateOnlyTypeTest()
    {
        var date = new DateOnly(2024, 6, 15);
        var value = WitSqlValue.FromDateOnly(date);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.DateOnly));
        Assert.That(value.AsDateOnly(), Is.EqualTo(date));
    }

    [Test]
    public void FromTimeOnlyCreatesTimeOnlyTypeTest()
    {
        var time = new TimeOnly(10, 30, 45);
        var value = WitSqlValue.FromTimeOnly(time);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.TimeOnly));
        Assert.That(value.AsTimeOnly(), Is.EqualTo(time));
    }

    [Test]
    public void FromTimeSpanCreatesTimeSpanTypeTest()
    {
        var ts = TimeSpan.FromHours(2.5);
        var value = WitSqlValue.FromTimeSpan(ts);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.TimeSpan));
        Assert.That(value.AsTimeSpan(), Is.EqualTo(ts));
    }

    [Test]
    public void FromGuidCreatesGuidTypeTest()
    {
        var guid = Guid.NewGuid();
        var value = WitSqlValue.FromGuid(guid);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Guid));
        Assert.That(value.AsGuid(), Is.EqualTo(guid));
    }

    [Test]
    public void FromDateTimeOffsetCreatesDateTimeOffsetTypeTest()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(3));
        var value = WitSqlValue.FromDateTimeOffset(dto);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.DateTimeOffset));
        Assert.That(value.AsDateTimeOffset(), Is.EqualTo(dto));
    }

    [Test]
    public void FromJsonDocumentCreatesJsonTypeTest()
    {
        using var doc = JsonDocument.Parse("""{"name":"John","age":30}""");
        var value = WitSqlValue.FromJson(doc);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Json));
        Assert.That(value.IsNull, Is.False);
    }

    [Test]
    public void FromJsonElementCreatesJsonTypeTest()
    {
        using var doc = JsonDocument.Parse("""{"name":"John"}""");
        var value = WitSqlValue.FromJson(doc.RootElement);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Json));
    }

    [Test]
    public void FromJsonStringCreatesJsonTypeTest()
    {
        var value = WitSqlValue.FromJsonString("""[1,2,3]""");

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Json));
    }

    [Test]
    public void FromJsonStringThrowsOnInvalidJsonTest()
    {
        Assert.That(() => WitSqlValue.FromJsonString("{invalid}"), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void FromJsonThrowsOnNullTest()
    {
        Assert.Throws<ArgumentNullException>(() => WitSqlValue.FromJson((JsonDocument)null!));
        Assert.Throws<ArgumentNullException>(() => WitSqlValue.FromJsonString(null!));
    }

    [Test]
    public void TryFromJsonStringSucceedsOnValidJsonTest()
    {
        Assert.That(WitSqlValue.TryFromJsonString("""{"key":"value"}""", out var result), Is.True);
        Assert.That(result.Type, Is.EqualTo(WitSqlType.Json));
    }

    [Test]
    public void TryFromJsonStringFailsOnInvalidJsonTest()
    {
        Assert.That(WitSqlValue.TryFromJsonString("not json", out var result), Is.False);
        Assert.That(result.IsNull, Is.True);
    }

    [Test]
    public void TryFromJsonStringFailsOnNullOrEmptyTest()
    {
        Assert.That(WitSqlValue.TryFromJsonString(null, out _), Is.False);
        Assert.That(WitSqlValue.TryFromJsonString("", out _), Is.False);
    }

    #endregion

    #region FromObject Tests

    [Test]
    public void FromObjectHandlesAllSupportedTypesTest()
    {
        Assert.That(WitSqlValue.FromObject(null).IsNull, Is.True);
        Assert.That(WitSqlValue.FromObject(DBNull.Value).IsNull, Is.True);
        Assert.That(WitSqlValue.FromObject(true).Type, Is.EqualTo(WitSqlType.Boolean));
        Assert.That(WitSqlValue.FromObject((sbyte)1).Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitSqlValue.FromObject((byte)1).Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitSqlValue.FromObject((short)1).Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitSqlValue.FromObject((ushort)1).Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitSqlValue.FromObject(1).Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitSqlValue.FromObject(1u).Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitSqlValue.FromObject(1L).Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitSqlValue.FromObject(1uL).Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitSqlValue.FromObject(1.0f).Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(WitSqlValue.FromObject(1.0).Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(WitSqlValue.FromObject(1.0m).Type, Is.EqualTo(WitSqlType.Decimal));
        Assert.That(WitSqlValue.FromObject("test").Type, Is.EqualTo(WitSqlType.Text));
        Assert.That(WitSqlValue.FromObject(new byte[] { 1, 2, 3 }).Type, Is.EqualTo(WitSqlType.Blob));
        Assert.That(WitSqlValue.FromObject(DateTime.Now).Type, Is.EqualTo(WitSqlType.DateTime));
        Assert.That(WitSqlValue.FromObject(DateOnly.FromDateTime(DateTime.Now)).Type, Is.EqualTo(WitSqlType.DateOnly));
        Assert.That(WitSqlValue.FromObject(TimeOnly.FromDateTime(DateTime.Now)).Type, Is.EqualTo(WitSqlType.TimeOnly));
        Assert.That(WitSqlValue.FromObject(TimeSpan.FromHours(1)).Type, Is.EqualTo(WitSqlType.TimeSpan));
        Assert.That(WitSqlValue.FromObject(Guid.NewGuid()).Type, Is.EqualTo(WitSqlType.Guid));
        Assert.That(WitSqlValue.FromObject(DateTimeOffset.Now).Type, Is.EqualTo(WitSqlType.DateTimeOffset));
    }

    [Test]
    public void FromObjectReturnsSqlValueAsIsTest()
    {
        var original = WitSqlValue.FromInt(42);
        var result = WitSqlValue.FromObject(original);

        Assert.That(result, Is.EqualTo(original));
    }

    [Test]
    public void FromObjectHandlesJsonDocumentTest()
    {
        using var doc = JsonDocument.Parse("{}");
        var value = WitSqlValue.FromObject(doc);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Json));
    }

    [Test]
    public void FromObjectHandlesJsonElementTest()
    {
        using var doc = JsonDocument.Parse("123");
        var value = WitSqlValue.FromObject(doc.RootElement);

        Assert.That(value.Type, Is.EqualTo(WitSqlType.Json));
    }

    [Test]
    public void FromObjectThrowsOnUnsupportedTypeTest()
    {
        Assert.Throws<ArgumentException>(() => WitSqlValue.FromObject(new object()));
    }

    #endregion
}
