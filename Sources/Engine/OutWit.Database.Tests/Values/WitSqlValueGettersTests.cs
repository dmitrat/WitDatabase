using OutWit.Database.Types;
using System.Text.Json;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Values;

/// <summary>
/// Tests for WitSqlValue getters (AsInt64, AsString, AsDateTime, etc.).
/// </summary>
[TestFixture]
public class WitSqlValueGettersTests
{
    #region Type Conversion Tests

    [Test]
    public void IntegerToDoubleConversionTest()
    {
        var value = WitSqlValue.FromInt(42);
        Assert.That(value.AsDouble(), Is.EqualTo(42.0));
    }

    [Test]
    public void IntegerToDecimalConversionTest()
    {
        var value = WitSqlValue.FromInt(42);
        Assert.That(value.AsDecimal(), Is.EqualTo(42m));
    }

    [Test]
    public void IntegerToStringConversionTest()
    {
        var value = WitSqlValue.FromInt(42);
        Assert.That(value.AsString(), Is.EqualTo("42"));
    }

    [Test]
    public void IntegerToBoolConversionTest()
    {
        Assert.That(WitSqlValue.FromInt(0).AsBool(), Is.False);
        Assert.That(WitSqlValue.FromInt(1).AsBool(), Is.True);
        Assert.That(WitSqlValue.FromInt(-1).AsBool(), Is.True);
    }

    [Test]
    public void RealToIntegerConversionTest()
    {
        var value = WitSqlValue.FromReal(42.9);
        Assert.That(value.AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void TextToIntegerConversionTest()
    {
        Assert.That(WitSqlValue.FromText("42").AsInt64(), Is.EqualTo(42));
        Assert.That(WitSqlValue.FromText("invalid").AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void TextToDoubleConversionTest()
    {
        Assert.That(WitSqlValue.FromText("3.14").AsDouble(), Is.EqualTo(3.14));
        Assert.That(WitSqlValue.FromText("invalid").AsDouble(), Is.EqualTo(0));
    }

    [Test]
    public void TextToBoolConversionTest()
    {
        Assert.That(WitSqlValue.FromText("true").AsBool(), Is.True);
        Assert.That(WitSqlValue.FromText("false").AsBool(), Is.False);
        Assert.That(WitSqlValue.FromText("1").AsBool(), Is.True);
        Assert.That(WitSqlValue.FromText("0").AsBool(), Is.False);
        Assert.That(WitSqlValue.FromText("").AsBool(), Is.False);
        Assert.That(WitSqlValue.FromText("hello").AsBool(), Is.True);
    }

    [Test]
    public void BooleanToIntegerConversionTest()
    {
        Assert.That(WitSqlValue.True.AsInt64(), Is.EqualTo(1));
        Assert.That(WitSqlValue.False.AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void BooleanToStringConversionTest()
    {
        Assert.That(WitSqlValue.True.AsString(), Is.EqualTo("true"));
        Assert.That(WitSqlValue.False.AsString(), Is.EqualTo("false"));
    }

    [Test]
    public void NullConversionsReturnDefaultsTest()
    {
        var nullValue = WitSqlValue.Null;

        Assert.That(nullValue.AsInt64(), Is.EqualTo(0));
        Assert.That(nullValue.AsDouble(), Is.EqualTo(0));
        Assert.That(nullValue.AsString(), Is.EqualTo(string.Empty));
        Assert.That(nullValue.AsBlob(), Is.Empty);
        Assert.That(nullValue.AsBool(), Is.False);
        Assert.That(nullValue.AsDecimal(), Is.EqualTo(0m));
        Assert.That(nullValue.AsDateTime(), Is.EqualTo(DateTime.MinValue));
        Assert.That(nullValue.AsDateOnly(), Is.EqualTo(DateOnly.MinValue));
        Assert.That(nullValue.AsTimeOnly(), Is.EqualTo(TimeOnly.MinValue));
        Assert.That(nullValue.AsTimeSpan(), Is.EqualTo(TimeSpan.Zero));
        Assert.That(nullValue.AsGuid(), Is.EqualTo(Guid.Empty));
        Assert.That(nullValue.AsDateTimeOffset(), Is.EqualTo(DateTimeOffset.MinValue));
    }

    [Test]
    public void InvalidConversionThrowsInvalidCastExceptionTest()
    {
        var blob = WitSqlValue.FromBlob([1, 2, 3]);

        Assert.Throws<InvalidCastException>(() => blob.AsInt64());
        Assert.Throws<InvalidCastException>(() => blob.AsDouble());
        Assert.Throws<InvalidCastException>(() => blob.AsBool());
        Assert.Throws<InvalidCastException>(() => blob.AsDecimal());
        Assert.Throws<InvalidCastException>(() => blob.AsDateTime());
        Assert.Throws<InvalidCastException>(() => blob.AsDateOnly());
        Assert.Throws<InvalidCastException>(() => blob.AsTimeOnly());
        Assert.Throws<InvalidCastException>(() => blob.AsTimeSpan());
        Assert.Throws<InvalidCastException>(() => blob.AsDateTimeOffset());
    }

    [Test]
    public void GuidFromBlobConversionTest()
    {
        var guid = Guid.NewGuid();
        var blob = WitSqlValue.FromBlob(guid.ToByteArray());

        Assert.That(blob.AsGuid(), Is.EqualTo(guid));
    }

    [Test]
    public void GuidFromTextConversionTest()
    {
        var guid = Guid.NewGuid();
        var text = WitSqlValue.FromText(guid.ToString());

        Assert.That(text.AsGuid(), Is.EqualTo(guid));
    }

    [Test]
    public void BlobFromTextBase64ConversionTest()
    {
        byte[] original = [1, 2, 3, 4, 5];
        var text = WitSqlValue.FromText(Convert.ToBase64String(original));

        Assert.That(text.AsBlob(), Is.EqualTo(original));
    }

    #endregion

    #region DateTime Conversions Tests

    [Test]
    public void DateTimeFromTextConversionTest()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var text = WitSqlValue.FromText(dt.ToString("o"));

        Assert.That(text.AsDateTime(), Is.EqualTo(dt));
    }

    [Test]
    public void DateOnlyFromDateTimeConversionTest()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0);
        var value = WitSqlValue.FromDateTime(dt);

        Assert.That(value.AsDateOnly(), Is.EqualTo(new DateOnly(2024, 6, 15)));
    }

    [Test]
    public void TimeOnlyFromDateTimeConversionTest()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 45);
        var value = WitSqlValue.FromDateTime(dt);

        Assert.That(value.AsTimeOnly(), Is.EqualTo(new TimeOnly(10, 30, 45)));
    }

    [Test]
    public void TimeOnlyFromTimeSpanConversionTest()
    {
        var ts = new TimeSpan(10, 30, 45);
        var value = WitSqlValue.FromTimeSpan(ts);

        Assert.That(value.AsTimeOnly(), Is.EqualTo(new TimeOnly(10, 30, 45)));
    }

    [Test]
    public void DateTimeOffsetFromDateTimeConversionTest()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var value = WitSqlValue.FromDateTime(dt);

        Assert.That(value.AsDateTimeOffset().DateTime, Is.EqualTo(dt));
    }

    [Test]
    public void DateTimeFromDateTimeOffsetConversionTest()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(3));
        var value = WitSqlValue.FromDateTimeOffset(dto);

        Assert.That(value.AsDateTime(), Is.EqualTo(dto.DateTime));
    }

    #endregion

    #region Json Getters Tests

    [Test]
    public void AsJsonElementReturnsElementTest()
    {
        var value = WitSqlValue.FromJsonString("""{"name":"John","age":30}""");
        var element = value.AsJsonElement();

        Assert.That(element.GetProperty("name").GetString(), Is.EqualTo("John"));
        Assert.That(element.GetProperty("age").GetInt32(), Is.EqualTo(30));
    }

    [Test]
    public void AsJsonDocumentReturnsDocumentTest()
    {
        var value = WitSqlValue.FromJsonString("""[1,2,3]""");
        using var doc = value.AsJsonDocument();

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.RootElement.GetArrayLength(), Is.EqualTo(3));
    }

    [Test]
    public void AsJsonElementFromTextParsesJsonTest()
    {
        var value = WitSqlValue.FromText("""{"key":"value"}""");
        var element = value.AsJsonElement();

        Assert.That(element.GetProperty("key").GetString(), Is.EqualTo("value"));
    }

    [Test]
    public void AsJsonElementFromNullReturnsDefaultTest()
    {
        var element = WitSqlValue.Null.AsJsonElement();

        Assert.That(element.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
    }

    [Test]
    public void AsJsonDocumentFromNullReturnsNullTest()
    {
        var doc = WitSqlValue.Null.AsJsonDocument();

        Assert.That(doc, Is.Null);
    }

    [Test]
    public void AsStringFromJsonReturnsRawJsonTest()
    {
        var value = WitSqlValue.FromJsonString("""{"name":"John"}""");
        var str = value.AsString();

        Assert.That(str, Does.Contain("name"));
        Assert.That(str, Does.Contain("John"));
    }

    #endregion
}
