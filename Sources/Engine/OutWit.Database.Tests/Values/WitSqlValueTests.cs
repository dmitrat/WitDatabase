using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Values;

/// <summary>
/// Tests for WitSqlValue - core functionality, static instances, and ToString.
/// </summary>
[TestFixture]
public class WitSqlValueTests
{
    #region Static Instances Tests

    [Test]
    public void NullIsNullTypeTest()
    {
        Assert.That(WitSqlValue.Null.Type, Is.EqualTo(WitSqlType.Null));
        Assert.That(WitSqlValue.Null.IsNull, Is.True);
    }

    [Test]
    public void TrueIsBooleanTrueTest()
    {
        Assert.That(WitSqlValue.True.Type, Is.EqualTo(WitSqlType.Boolean));
        Assert.That(WitSqlValue.True.AsBool(), Is.True);
    }

    [Test]
    public void FalseIsBooleanFalseTest()
    {
        Assert.That(WitSqlValue.False.Type, Is.EqualTo(WitSqlType.Boolean));
        Assert.That(WitSqlValue.False.AsBool(), Is.False);
    }

    #endregion

    #region ToString Tests

    [Test]
    public void ToStringFormatsCorrectlyTest()
    {
        Assert.That(WitSqlValue.Null.ToString(), Is.EqualTo("NULL"));
        Assert.That(WitSqlValue.FromInt(42).ToString(), Is.EqualTo("Integer:42"));
        Assert.That(WitSqlValue.FromReal(3.14).ToString(), Is.EqualTo("Real:3.14"));
        Assert.That(WitSqlValue.FromText("hello").ToString(), Is.EqualTo("Text:hello"));
        Assert.That(WitSqlValue.True.ToString(), Is.EqualTo("Boolean:true"));
    }

    #endregion

    #region ToObject Tests

    [Test]
    public void ToObjectReturnsCorrectTypesTest()
    {
        Assert.That(WitSqlValue.Null.ToObject(), Is.Null);
        Assert.That(WitSqlValue.FromInt(42).ToObject(), Is.EqualTo(42L));
        Assert.That(WitSqlValue.FromReal(3.14).ToObject(), Is.EqualTo(3.14));
        Assert.That(WitSqlValue.FromText("hello").ToObject(), Is.EqualTo("hello"));
        Assert.That(WitSqlValue.True.ToObject(), Is.EqualTo(true));
        Assert.That(WitSqlValue.FromDecimal(123m).ToObject(), Is.EqualTo(123m));
    }

    #endregion
}
