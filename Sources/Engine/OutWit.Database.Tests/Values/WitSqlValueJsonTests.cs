using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Values;

/// <summary>
/// Tests for WitSqlValue JSON operations (JsonExtract, JsonType, JsonArrayLength).
/// </summary>
[TestFixture]
public class WitSqlValueJsonTests
{
    #region Json Extract Tests

    [Test]
    public void JsonExtractSimplePropertyTest()
    {
        var json = WitSqlValue.FromJsonString("""{"name":"John","age":30}""");

        Assert.That(json.JsonExtract("name").AsString(), Is.EqualTo("John"));
        Assert.That(json.JsonExtract("age").AsInt64(), Is.EqualTo(30));
    }

    [Test]
    public void JsonExtractWithDollarPrefixTest()
    {
        var json = WitSqlValue.FromJsonString("""{"name":"John"}""");

        Assert.That(json.JsonExtract("$.name").AsString(), Is.EqualTo("John"));
        Assert.That(json.JsonExtract("$name").AsString(), Is.EqualTo("John"));
    }

    [Test]
    public void JsonExtractNestedPropertyTest()
    {
        var json = WitSqlValue.FromJsonString("""{"user":{"name":"John","address":{"city":"NYC"}}}""");

        Assert.That(json.JsonExtract("user.name").AsString(), Is.EqualTo("John"));
        Assert.That(json.JsonExtract("user.address.city").AsString(), Is.EqualTo("NYC"));
    }

    [Test]
    public void JsonExtractArrayElementTest()
    {
        var json = WitSqlValue.FromJsonString("""{"items":[10,20,30]}""");

        Assert.That(json.JsonExtract("items[0]").AsInt64(), Is.EqualTo(10));
        Assert.That(json.JsonExtract("items[1]").AsInt64(), Is.EqualTo(20));
        Assert.That(json.JsonExtract("items[2]").AsInt64(), Is.EqualTo(30));
    }

    [Test]
    public void JsonExtractArrayOfObjectsTest()
    {
        var json = WitSqlValue.FromJsonString("""{"users":[{"name":"Alice"},{"name":"Bob"}]}""");

        Assert.That(json.JsonExtract("users[0].name").AsString(), Is.EqualTo("Alice"));
        Assert.That(json.JsonExtract("users[1].name").AsString(), Is.EqualTo("Bob"));
    }

    [Test]
    public void JsonExtractMissingPropertyReturnsNullTest()
    {
        var json = WitSqlValue.FromJsonString("""{"name":"John"}""");

        Assert.That(json.JsonExtract("missing").IsNull, Is.True);
        Assert.That(json.JsonExtract("name.nested").IsNull, Is.True);
    }

    [Test]
    public void JsonExtractOutOfBoundsArrayReturnsNullTest()
    {
        var json = WitSqlValue.FromJsonString("""{"items":[1,2,3]}""");

        Assert.That(json.JsonExtract("items[10]").IsNull, Is.True);
        Assert.That(json.JsonExtract("items[-1]").IsNull, Is.True);
    }

    [Test]
    public void JsonExtractFromNonJsonReturnsNullTest()
    {
        Assert.That(WitSqlValue.FromInt(42).JsonExtract("key").IsNull, Is.True);
        Assert.That(WitSqlValue.Null.JsonExtract("key").IsNull, Is.True);
    }

    [Test]
    public void JsonExtractReturnsCorrectTypesTest()
    {
        var json = WitSqlValue.FromJsonString("""
            {
                "string": "hello",
                "number": 42,
                "float": 3.14,
                "bool": true,
                "null": null,
                "array": [1,2],
                "object": {"nested": true}
            }
            """);

        Assert.That(json.JsonExtract("string").Type, Is.EqualTo(WitSqlType.Text));
        Assert.That(json.JsonExtract("number").Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(json.JsonExtract("float").Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(json.JsonExtract("bool").Type, Is.EqualTo(WitSqlType.Boolean));
        Assert.That(json.JsonExtract("null").IsNull, Is.True);
        Assert.That(json.JsonExtract("array").Type, Is.EqualTo(WitSqlType.Json));
        Assert.That(json.JsonExtract("object").Type, Is.EqualTo(WitSqlType.Json));
    }

    #endregion

    #region Json Type Tests

    [Test]
    public void JsonTypeReturnsCorrectTypeNamesTest()
    {
        Assert.That(WitSqlValue.FromJsonString("null").JsonType(), Is.EqualTo("null"));
        Assert.That(WitSqlValue.FromJsonString("true").JsonType(), Is.EqualTo("boolean"));
        Assert.That(WitSqlValue.FromJsonString("false").JsonType(), Is.EqualTo("boolean"));
        Assert.That(WitSqlValue.FromJsonString("42").JsonType(), Is.EqualTo("number"));
        Assert.That(WitSqlValue.FromJsonString("3.14").JsonType(), Is.EqualTo("number"));
        Assert.That(WitSqlValue.FromJsonString("\"hello\"").JsonType(), Is.EqualTo("string"));
        Assert.That(WitSqlValue.FromJsonString("[1,2,3]").JsonType(), Is.EqualTo("array"));
        Assert.That(WitSqlValue.FromJsonString("{}").JsonType(), Is.EqualTo("object"));
    }

    [Test]
    public void JsonTypeOnNonJsonReturnsNullTest()
    {
        Assert.That(WitSqlValue.FromInt(42).JsonType(), Is.EqualTo("null"));
        Assert.That(WitSqlValue.Null.JsonType(), Is.EqualTo("null"));
    }

    #endregion

    #region Json Array Length Tests

    [Test]
    public void JsonArrayLengthReturnsCorrectLengthTest()
    {
        Assert.That(WitSqlValue.FromJsonString("[]").JsonArrayLength().AsInt64(), Is.EqualTo(0));
        Assert.That(WitSqlValue.FromJsonString("[1]").JsonArrayLength().AsInt64(), Is.EqualTo(1));
        Assert.That(WitSqlValue.FromJsonString("[1,2,3]").JsonArrayLength().AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void JsonArrayLengthOnNonArrayReturnsNullTest()
    {
        Assert.That(WitSqlValue.FromJsonString("{}").JsonArrayLength().IsNull, Is.True);
        Assert.That(WitSqlValue.FromJsonString("42").JsonArrayLength().IsNull, Is.True);
        Assert.That(WitSqlValue.FromJsonString("\"text\"").JsonArrayLength().IsNull, Is.True);
    }

    [Test]
    public void JsonArrayLengthOnNonJsonReturnsNullTest()
    {
        Assert.That(WitSqlValue.FromInt(42).JsonArrayLength().IsNull, Is.True);
        Assert.That(WitSqlValue.Null.JsonArrayLength().IsNull, Is.True);
    }

    #endregion

    #region Json Comparison Tests

    [Test]
    public void JsonValuesCompareByStringTest()
    {
        var a = WitSqlValue.FromJsonString("""{"a":1}""");
        var b = WitSqlValue.FromJsonString("""{"a":1}""");
        var c = WitSqlValue.FromJsonString("""{"b":2}""");

        Assert.That(a.CompareTo(b), Is.EqualTo(0));
        Assert.That(a.CompareTo(c), Is.Not.EqualTo(0));
    }

    [Test]
    public void JsonValuesEqualityTest()
    {
        var a = WitSqlValue.FromJsonString("""{"key":"value"}""");
        var b = WitSqlValue.FromJsonString("""{"key":"value"}""");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    #endregion
}
