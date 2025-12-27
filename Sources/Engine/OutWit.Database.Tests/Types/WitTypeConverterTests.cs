using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Types;

/// <summary>
/// Tests for WitTypeConverter - centralized type conversion logic.
/// </summary>
[TestFixture]
public class WitTypeConverterTests
{
    #region ToSqlType Tests

    [Test]
    public void ToSqlTypeNullTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.Null), Is.EqualTo(WitSqlType.Null));
    }

    [Test]
    [TestCase(WitDataType.Int8)]
    [TestCase(WitDataType.UInt8)]
    [TestCase(WitDataType.Int16)]
    [TestCase(WitDataType.UInt16)]
    [TestCase(WitDataType.Int32)]
    [TestCase(WitDataType.UInt32)]
    [TestCase(WitDataType.Int64)]
    [TestCase(WitDataType.UInt64)]
    public void ToSqlTypeIntegerTypesTest(WitDataType dataType)
    {
        Assert.That(WitTypeConverter.ToSqlType(dataType), Is.EqualTo(WitSqlType.Integer));
    }

    [Test]
    [TestCase(WitDataType.Float16)]
    [TestCase(WitDataType.Float32)]
    [TestCase(WitDataType.Float64)]
    public void ToSqlTypeRealTypesTest(WitDataType dataType)
    {
        Assert.That(WitTypeConverter.ToSqlType(dataType), Is.EqualTo(WitSqlType.Real));
    }

    [Test]
    public void ToSqlTypeDecimalTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.Decimal), Is.EqualTo(WitSqlType.Decimal));
    }

    [Test]
    public void ToSqlTypeBooleanTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.Boolean), Is.EqualTo(WitSqlType.Boolean));
    }

    [Test]
    public void ToSqlTypeDateOnlyTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.DateOnly), Is.EqualTo(WitSqlType.DateOnly));
    }

    [Test]
    public void ToSqlTypeTimeOnlyTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.TimeOnly), Is.EqualTo(WitSqlType.TimeOnly));
    }

    [Test]
    public void ToSqlTypeDateTimeTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.DateTime), Is.EqualTo(WitSqlType.DateTime));
    }

    [Test]
    public void ToSqlTypeDateTimeOffsetTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.DateTimeOffset), Is.EqualTo(WitSqlType.DateTimeOffset));
    }

    [Test]
    public void ToSqlTypeTimeSpanTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.TimeSpan), Is.EqualTo(WitSqlType.TimeSpan));
    }

    [Test]
    public void ToSqlTypeGuidTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.Guid), Is.EqualTo(WitSqlType.Guid));
    }

    [Test]
    [TestCase(WitDataType.StringFixed)]
    [TestCase(WitDataType.StringVariable)]
    public void ToSqlTypeStringTypesTest(WitDataType dataType)
    {
        Assert.That(WitTypeConverter.ToSqlType(dataType), Is.EqualTo(WitSqlType.Text));
    }

    [Test]
    [TestCase(WitDataType.BinaryFixed)]
    [TestCase(WitDataType.BinaryVariable)]
    public void ToSqlTypeBinaryTypesTest(WitDataType dataType)
    {
        Assert.That(WitTypeConverter.ToSqlType(dataType), Is.EqualTo(WitSqlType.Blob));
    }

    [Test]
    public void ToSqlTypeRowVersionTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.RowVersion), Is.EqualTo(WitSqlType.RowVersion));
    }

    [Test]
    public void ToSqlTypeJsonTest()
    {
        Assert.That(WitTypeConverter.ToSqlType(WitDataType.Json), Is.EqualTo(WitSqlType.Json));
    }

    [Test]
    public void ToSqlTypeCoversAllWitDataTypesTest()
    {
        // Ensure all WitDataType values are handled (no exceptions thrown)
        foreach (WitDataType dataType in Enum.GetValues<WitDataType>())
        {
            Assert.DoesNotThrow(() => WitTypeConverter.ToSqlType(dataType),
                $"ToSqlType should handle {dataType}");
        }
    }

    #endregion

    #region Index Key Serialization Tests

    [Test]
    public void SerializeIndexKeyNullValueTest()
    {
        var types = new[] { WitDataType.Int64 };
        var values = new[] { WitSqlValue.Null };

        var bytes = WitTypeConverter.SerializeIndexKey(values, types);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes.Length, Is.EqualTo(1));
        Assert.That(bytes[0], Is.EqualTo(0x00)); // Null marker
    }

    [Test]
    public void SerializeIndexKeyIntegerValueTest()
    {
        var types = new[] { WitDataType.Int64 };
        var values = new[] { WitSqlValue.FromInt(42) };

        var bytes = WitTypeConverter.SerializeIndexKey(values, types);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes.Length, Is.EqualTo(9)); // 1 byte non-null marker + 8 bytes for long
        Assert.That(bytes[0], Is.EqualTo(0x01)); // Non-null marker
    }

    [Test]
    public void SerializeIndexKeyIntegerOrderingTest()
    {
        var types = new[] { WitDataType.Int64 };

        // Test that serialized bytes preserve ordering
        var bytesNeg = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromInt(-100)], types);
        var bytesZero = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromInt(0)], types);
        var bytesPos = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromInt(100)], types);

        // Byte comparison should give same order as integer comparison
        Assert.That(CompareBytes(bytesNeg, bytesZero), Is.LessThan(0));
        Assert.That(CompareBytes(bytesZero, bytesPos), Is.LessThan(0));
        Assert.That(CompareBytes(bytesNeg, bytesPos), Is.LessThan(0));
    }

    [Test]
    public void SerializeIndexKeyStringValueTest()
    {
        var types = new[] { WitDataType.StringVariable };
        var values = new[] { WitSqlValue.FromText("hello") };

        var bytes = WitTypeConverter.SerializeIndexKey(values, types);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes[0], Is.EqualTo(0x01)); // Non-null marker
    }

    [Test]
    public void SerializeIndexKeyBooleanValueTest()
    {
        var types = new[] { WitDataType.Boolean };

        var bytesFalse = WitTypeConverter.SerializeIndexKey([WitSqlValue.False], types);
        var bytesTrue = WitTypeConverter.SerializeIndexKey([WitSqlValue.True], types);

        Assert.That(bytesFalse[0], Is.EqualTo(0x01)); // Non-null marker
        Assert.That(bytesTrue[0], Is.EqualTo(0x01)); // Non-null marker

        // False should sort before true
        Assert.That(CompareBytes(bytesFalse, bytesTrue), Is.LessThan(0));
    }

    [Test]
    public void SerializeIndexKeyDateTimeOrderingTest()
    {
        var types = new[] { WitDataType.DateTime };

        var dt1 = new DateTime(2020, 1, 1);
        var dt2 = new DateTime(2025, 6, 15);

        var bytes1 = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromDateTime(dt1)], types);
        var bytes2 = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromDateTime(dt2)], types);

        // Earlier date should sort before later date
        Assert.That(CompareBytes(bytes1, bytes2), Is.LessThan(0));
    }

    [Test]
    public void SerializeIndexKeyFloatOrderingTest()
    {
        var types = new[] { WitDataType.Float64 };

        var bytesNeg = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromReal(-1.5)], types);
        var bytesZero = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromReal(0.0)], types);
        var bytesPos = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromReal(1.5)], types);

        // Byte comparison should give same order as float comparison
        Assert.That(CompareBytes(bytesNeg, bytesZero), Is.LessThan(0));
        Assert.That(CompareBytes(bytesZero, bytesPos), Is.LessThan(0));
    }

    [Test]
    public void SerializeIndexKeyGuidValueTest()
    {
        var types = new[] { WitDataType.Guid };
        var guid = Guid.NewGuid();

        var bytes = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromGuid(guid)], types);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes[0], Is.EqualTo(0x01)); // Non-null marker
        Assert.That(bytes.Length, Is.EqualTo(17)); // 1 + 16 for GUID
    }

    [Test]
    public void SerializeIndexKeyCompositeKeyTest()
    {
        var types = new[] { WitDataType.StringVariable, WitDataType.Int64 };
        var values = new[] { WitSqlValue.FromText("test"), WitSqlValue.FromInt(42) };

        var bytes = WitTypeConverter.SerializeIndexKey(values, types);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes.Length, Is.GreaterThan(0));
    }

    [Test]
    public void SerializeIndexKeyPartialKeyTest()
    {
        // Partial key (fewer values than columns) is valid for range scans
        var types = new[] { WitDataType.StringVariable, WitDataType.Int64 };
        var values = new[] { WitSqlValue.FromText("test") };

        var bytes = WitTypeConverter.SerializeIndexKey(values, types);

        Assert.That(bytes, Is.Not.Null);
    }

    [Test]
    public void SerializeIndexKeyTooManyValuesThrowsTest()
    {
        var types = new[] { WitDataType.Int64 };
        var values = new[] { WitSqlValue.FromInt(1), WitSqlValue.FromInt(2) };

        Assert.Throws<ArgumentException>(() => WitTypeConverter.SerializeIndexKey(values, types));
    }

    [Test]
    public void SerializeIndexKeyNullSortsFirstTest()
    {
        var types = new[] { WitDataType.Int64 };

        var bytesNull = WitTypeConverter.SerializeIndexKey([WitSqlValue.Null], types);
        var bytesValue = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromInt(0)], types);

        // Null should sort before any value
        Assert.That(CompareBytes(bytesNull, bytesValue), Is.LessThan(0));
    }

    [Test]
    public void SerializeIndexKeyDecimalValueTest()
    {
        var types = new[] { WitDataType.Decimal };
        var values = new[] { WitSqlValue.FromDecimal(123.45m) };

        var bytes = WitTypeConverter.SerializeIndexKey(values, types);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes[0], Is.EqualTo(0x01)); // Non-null marker
    }

    [Test]
    public void SerializeIndexKeyDateOnlyOrderingTest()
    {
        var types = new[] { WitDataType.DateOnly };

        var d1 = new DateOnly(2020, 1, 1);
        var d2 = new DateOnly(2025, 6, 15);

        var bytes1 = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromDateOnly(d1)], types);
        var bytes2 = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromDateOnly(d2)], types);

        Assert.That(CompareBytes(bytes1, bytes2), Is.LessThan(0));
    }

    [Test]
    public void SerializeIndexKeyTimeOnlyOrderingTest()
    {
        var types = new[] { WitDataType.TimeOnly };

        var t1 = new TimeOnly(8, 0, 0);
        var t2 = new TimeOnly(17, 30, 0);

        var bytes1 = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromTimeOnly(t1)], types);
        var bytes2 = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromTimeOnly(t2)], types);

        Assert.That(CompareBytes(bytes1, bytes2), Is.LessThan(0));
    }

    [Test]
    public void SerializeIndexKeyTimeSpanOrderingTest()
    {
        var types = new[] { WitDataType.TimeSpan };

        var tsNeg = TimeSpan.FromHours(-1);
        var tsZero = TimeSpan.Zero;
        var tsPos = TimeSpan.FromHours(1);

        var bytesNeg = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromTimeSpan(tsNeg)], types);
        var bytesZero = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromTimeSpan(tsZero)], types);
        var bytesPos = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromTimeSpan(tsPos)], types);

        Assert.That(CompareBytes(bytesNeg, bytesZero), Is.LessThan(0));
        Assert.That(CompareBytes(bytesZero, bytesPos), Is.LessThan(0));
    }

    [Test]
    public void SerializeIndexKeyBlobValueTest()
    {
        var types = new[] { WitDataType.BinaryVariable };
        var blob = new byte[] { 1, 2, 3, 4, 5 };

        var bytes = WitTypeConverter.SerializeIndexKey([WitSqlValue.FromBlob(blob)], types);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes[0], Is.EqualTo(0x01)); // Non-null marker
    }

    [Test]
    public void SerializeIndexKeyCoversAllDataTypesTest()
    {
        // Test that all data types can be serialized without exceptions
        var testCases = new (WitDataType type, WitSqlValue value)[]
        {
            (WitDataType.Int8, WitSqlValue.FromInt(42)),
            (WitDataType.UInt8, WitSqlValue.FromInt(42)),
            (WitDataType.Int16, WitSqlValue.FromInt(42)),
            (WitDataType.UInt16, WitSqlValue.FromInt(42)),
            (WitDataType.Int32, WitSqlValue.FromInt(42)),
            (WitDataType.UInt32, WitSqlValue.FromInt(42)),
            (WitDataType.Int64, WitSqlValue.FromInt(42)),
            (WitDataType.UInt64, WitSqlValue.FromInt(42)),
            (WitDataType.Float16, WitSqlValue.FromReal(3.14)),
            (WitDataType.Float32, WitSqlValue.FromReal(3.14)),
            (WitDataType.Float64, WitSqlValue.FromReal(3.14)),
            (WitDataType.Decimal, WitSqlValue.FromDecimal(123.45m)),
            (WitDataType.Boolean, WitSqlValue.True),
            (WitDataType.DateOnly, WitSqlValue.FromDateOnly(DateOnly.FromDateTime(DateTime.Today))),
            (WitDataType.TimeOnly, WitSqlValue.FromTimeOnly(TimeOnly.FromDateTime(DateTime.Now))),
            (WitDataType.DateTime, WitSqlValue.FromDateTime(DateTime.Now)),
            (WitDataType.DateTimeOffset, WitSqlValue.FromDateTimeOffset(DateTimeOffset.Now)),
            (WitDataType.TimeSpan, WitSqlValue.FromTimeSpan(TimeSpan.FromHours(1))),
            (WitDataType.Guid, WitSqlValue.FromGuid(Guid.NewGuid())),
            (WitDataType.StringFixed, WitSqlValue.FromText("test")),
            (WitDataType.StringVariable, WitSqlValue.FromText("test")),
            (WitDataType.BinaryFixed, WitSqlValue.FromBlob([1, 2, 3])),
            (WitDataType.BinaryVariable, WitSqlValue.FromBlob([1, 2, 3])),
            (WitDataType.Json, WitSqlValue.FromText("{}")),
        };

        foreach (var (type, value) in testCases)
        {
            Assert.DoesNotThrow(() => 
            {
                var bytes = WitTypeConverter.SerializeIndexKey([value], [type]);
                Assert.That(bytes.Length, Is.GreaterThan(0), $"Type {type} should produce bytes");
            }, $"SerializeIndexKey should handle {type}");
        }
    }

    #endregion

    #region Helpers

    private static int CompareBytes(byte[] a, byte[] b)
    {
        var minLen = Math.Min(a.Length, b.Length);
        for (int i = 0; i < minLen; i++)
        {
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        }
        return a.Length.CompareTo(b.Length);
    }

    #endregion
}
