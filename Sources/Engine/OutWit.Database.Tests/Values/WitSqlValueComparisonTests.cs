using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Values;

/// <summary>
/// Tests for WitSqlValue comparison (IEquatable, IComparable, comparison operators).
/// </summary>
[TestFixture]
public class WitSqlValueComparisonTests
{
    #region Comparison Tests

    [Test]
    public void CompareToSameTypeTest()
    {
        Assert.That(WitSqlValue.FromInt(10).CompareTo(WitSqlValue.FromInt(5)), Is.GreaterThan(0));
        Assert.That(WitSqlValue.FromInt(5).CompareTo(WitSqlValue.FromInt(10)), Is.LessThan(0));
        Assert.That(WitSqlValue.FromInt(5).CompareTo(WitSqlValue.FromInt(5)), Is.EqualTo(0));
    }

    [Test]
    public void CompareToNullTest()
    {
        var value = WitSqlValue.FromInt(10);

        Assert.That(WitSqlValue.Null.CompareTo(WitSqlValue.Null), Is.EqualTo(0));
        Assert.That(WitSqlValue.Null.CompareTo(value), Is.LessThan(0));
        Assert.That(value.CompareTo(WitSqlValue.Null), Is.GreaterThan(0));
    }

    [Test]
    public void CompareToCrossTypeNumericTest()
    {
        var intValue = WitSqlValue.FromInt(10);
        var realValue = WitSqlValue.FromReal(10.0);
        var decimalValue = WitSqlValue.FromDecimal(10m);

        Assert.That(intValue.CompareTo(realValue), Is.EqualTo(0));
        Assert.That(intValue.CompareTo(decimalValue), Is.EqualTo(0));
        Assert.That(realValue.CompareTo(decimalValue), Is.EqualTo(0));
    }

    [Test]
    public void CompareBlobsTest()
    {
        var a = WitSqlValue.FromBlob([1, 2, 3]);
        var b = WitSqlValue.FromBlob([1, 2, 4]);
        var c = WitSqlValue.FromBlob([1, 2, 3]);

        Assert.That(a.CompareTo(b), Is.LessThan(0));
        Assert.That(b.CompareTo(a), Is.GreaterThan(0));
        Assert.That(a.CompareTo(c), Is.EqualTo(0));
    }

    [Test]
    public void CompareTextsTest()
    {
        var a = WitSqlValue.FromText("apple");
        var b = WitSqlValue.FromText("banana");
        var c = WitSqlValue.FromText("apple");

        Assert.That(a.CompareTo(b), Is.LessThan(0));
        Assert.That(b.CompareTo(a), Is.GreaterThan(0));
        Assert.That(a.CompareTo(c), Is.EqualTo(0));
    }

    [Test]
    public void CompareDateTimesTest()
    {
        var earlier = WitSqlValue.FromDateTime(new DateTime(2024, 1, 1));
        var later = WitSqlValue.FromDateTime(new DateTime(2024, 12, 31));

        Assert.That(earlier.CompareTo(later), Is.LessThan(0));
        Assert.That(later.CompareTo(earlier), Is.GreaterThan(0));
    }

    #endregion

    #region Comparison Operators Tests

    [Test]
    public void ComparisonOperatorsTest()
    {
        WitSqlValue a = 10;
        WitSqlValue b = 5;

        Assert.That(a == WitSqlValue.FromInt(10), Is.True);
        Assert.That(a != b, Is.True);
        Assert.That(a > b, Is.True);
        Assert.That(a >= b, Is.True);
        Assert.That(b < a, Is.True);
        Assert.That(b <= a, Is.True);
    }

    #endregion

    #region Equality Tests

    [Test]
    public void EqualsNullsTest()
    {
        Assert.That(WitSqlValue.Null.Equals(WitSqlValue.Null), Is.True);
        Assert.That(WitSqlValue.FromInt(0).Equals(WitSqlValue.Null), Is.False);
        Assert.That(WitSqlValue.Null.Equals(WitSqlValue.FromInt(0)), Is.False);
    }

    [Test]
    public void EqualsSameValuesTest()
    {
        Assert.That(WitSqlValue.FromInt(42).Equals(WitSqlValue.FromInt(42)), Is.True);
        Assert.That(WitSqlValue.FromText("hello").Equals(WitSqlValue.FromText("hello")), Is.True);
        Assert.That(WitSqlValue.True.Equals(WitSqlValue.True), Is.True);
    }

    [Test]
    public void EqualsCrossTypeNumericTest()
    {
        Assert.That(WitSqlValue.FromInt(42).Equals(WitSqlValue.FromReal(42.0)), Is.True);
        Assert.That(WitSqlValue.FromInt(42).Equals(WitSqlValue.FromDecimal(42m)), Is.True);
    }

    [Test]
    public void EqualsObjectTest()
    {
        var value = WitSqlValue.FromInt(42);

        Assert.That(value.Equals((object)WitSqlValue.FromInt(42)), Is.True);
        Assert.That(value.Equals("not a SqlValue"), Is.False);
        Assert.That(value.Equals((object?)null), Is.False);
    }

    #endregion

    #region GetHashCode Tests

    [Test]
    public void GetHashCodeConsistentTest()
    {
        var value = WitSqlValue.FromInt(42);

        Assert.That(value.GetHashCode(), Is.EqualTo(value.GetHashCode()));
    }

    [Test]
    public void GetHashCodeEqualValuesHaveSameHashTest()
    {
        var a = WitSqlValue.FromInt(42);
        var b = WitSqlValue.FromInt(42);

        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void GetHashCodeNullIsZeroTest()
    {
        Assert.That(WitSqlValue.Null.GetHashCode(), Is.EqualTo(0));
    }

    #endregion
}
