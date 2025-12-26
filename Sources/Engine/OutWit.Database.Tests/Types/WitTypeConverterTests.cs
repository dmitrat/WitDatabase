using OutWit.Database.Types;

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
    [TestCase(WitDataType.RowVersion)]
    public void ToSqlTypeBinaryTypesTest(WitDataType dataType)
    {
        Assert.That(WitTypeConverter.ToSqlType(dataType), Is.EqualTo(WitSqlType.Blob));
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
}
