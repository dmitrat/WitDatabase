using System.Text.Json;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Types;

[TestFixture]
public class WitDataTypeRegistryTest
{
    // Test enum with default int underlying type
    private enum TestIntEnum
    {
        None = 0,
        First = 1,
        Second = 2,
        Large = 1000
    }

    // Test enum with byte underlying type
    private enum TestByteEnum : byte
    {
        Zero = 0,
        One = 1,
        Max = 255
    }

    // Test enum with long underlying type
    private enum TestLongEnum : long
    {
        Min = long.MinValue,
        Zero = 0,
        Max = long.MaxValue
    }

    // Flags enum
    [Flags]
    private enum TestFlagsEnum
    {
        None = 0,
        Read = 1,
        Write = 2,
        Execute = 4,
        All = Read | Write | Execute
    }

    #region Basic Type Mapping Tests

    [Test]
    public void GetDataTypeForPrimitivesTest()
    {
        Assert.That(WitTypeConverter.GetDataType<sbyte>(), Is.EqualTo(WitDataType.Int8));
        Assert.That(WitTypeConverter.GetDataType<byte>(), Is.EqualTo(WitDataType.UInt8));
        Assert.That(WitTypeConverter.GetDataType<short>(), Is.EqualTo(WitDataType.Int16));
        Assert.That(WitTypeConverter.GetDataType<ushort>(), Is.EqualTo(WitDataType.UInt16));
        Assert.That(WitTypeConverter.GetDataType<int>(), Is.EqualTo(WitDataType.Int32));
        Assert.That(WitTypeConverter.GetDataType<uint>(), Is.EqualTo(WitDataType.UInt32));
        Assert.That(WitTypeConverter.GetDataType<long>(), Is.EqualTo(WitDataType.Int64));
        Assert.That(WitTypeConverter.GetDataType<ulong>(), Is.EqualTo(WitDataType.UInt64));
        Assert.That(WitTypeConverter.GetDataType<Half>(), Is.EqualTo(WitDataType.Float16));
        Assert.That(WitTypeConverter.GetDataType<float>(), Is.EqualTo(WitDataType.Float32));
        Assert.That(WitTypeConverter.GetDataType<double>(), Is.EqualTo(WitDataType.Float64));
        Assert.That(WitTypeConverter.GetDataType<bool>(), Is.EqualTo(WitDataType.Boolean));
        Assert.That(WitTypeConverter.GetDataType<DateOnly>(), Is.EqualTo(WitDataType.DateOnly));
        Assert.That(WitTypeConverter.GetDataType<TimeOnly>(), Is.EqualTo(WitDataType.TimeOnly));
        Assert.That(WitTypeConverter.GetDataType<DateTime>(), Is.EqualTo(WitDataType.DateTime));
        Assert.That(WitTypeConverter.GetDataType<DateTimeOffset>(), Is.EqualTo(WitDataType.DateTimeOffset));
        Assert.That(WitTypeConverter.GetDataType<TimeSpan>(), Is.EqualTo(WitDataType.TimeSpan));
        Assert.That(WitTypeConverter.GetDataType<Guid>(), Is.EqualTo(WitDataType.Guid));
        Assert.That(WitTypeConverter.GetDataType<string>(), Is.EqualTo(WitDataType.StringVariable));
        Assert.That(WitTypeConverter.GetDataType<byte[]>(), Is.EqualTo(WitDataType.BinaryVariable));
        Assert.That(WitTypeConverter.GetDataType<decimal>(), Is.EqualTo(WitDataType.Decimal));
    }

    [Test]
    public void GetDataTypeForJsonTypesTest()
    {
        Assert.That(WitTypeConverter.GetDataType<JsonDocument>(), Is.EqualTo(WitDataType.Json));
        Assert.That(WitTypeConverter.GetDataType<JsonElement>(), Is.EqualTo(WitDataType.Json));
    }

    [Test]
    public void GetDataTypeForNullablesTest()
    {
        Assert.That(WitTypeConverter.GetDataType<int?>(), Is.EqualTo(WitDataType.Int32));
        Assert.That(WitTypeConverter.GetDataType<DateTime?>(), Is.EqualTo(WitDataType.DateTime));
        Assert.That(WitTypeConverter.GetDataType<Guid?>(), Is.EqualTo(WitDataType.Guid));
        Assert.That(WitTypeConverter.GetDataType<bool?>(), Is.EqualTo(WitDataType.Boolean));
        Assert.That(WitTypeConverter.GetDataType<decimal?>(), Is.EqualTo(WitDataType.Decimal));
    }

    #endregion

    #region GetClrType Tests

    [Test]
    public void GetClrTypeFromDataTypeTest()
    {
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Int8), Is.EqualTo(typeof(sbyte)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.UInt8), Is.EqualTo(typeof(byte)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Int16), Is.EqualTo(typeof(short)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.UInt16), Is.EqualTo(typeof(ushort)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Int32), Is.EqualTo(typeof(int)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.UInt32), Is.EqualTo(typeof(uint)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Int64), Is.EqualTo(typeof(long)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.UInt64), Is.EqualTo(typeof(ulong)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Float16), Is.EqualTo(typeof(Half)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Float32), Is.EqualTo(typeof(float)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Float64), Is.EqualTo(typeof(double)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Decimal), Is.EqualTo(typeof(decimal)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Boolean), Is.EqualTo(typeof(bool)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.DateOnly), Is.EqualTo(typeof(DateOnly)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.TimeOnly), Is.EqualTo(typeof(TimeOnly)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.DateTime), Is.EqualTo(typeof(DateTime)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.DateTimeOffset), Is.EqualTo(typeof(DateTimeOffset)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.TimeSpan), Is.EqualTo(typeof(TimeSpan)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Guid), Is.EqualTo(typeof(Guid)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.StringVariable), Is.EqualTo(typeof(string)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.StringFixed), Is.EqualTo(typeof(string)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.BinaryVariable), Is.EqualTo(typeof(byte[])));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.BinaryFixed), Is.EqualTo(typeof(byte[])));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Null), Is.EqualTo(typeof(DBNull)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.RowVersion), Is.EqualTo(typeof(ulong)));
        Assert.That(WitTypeConverter.GetClrType(WitDataType.Json), Is.EqualTo(typeof(JsonDocument)));
    }

    #endregion

    #region Enum Tests

    [Test]
    public void GetDataTypeForIntEnumTest()
    {
        // Int-based enum should map to Int32
        var witDataType = WitTypeConverter.GetDataType<TestIntEnum>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.Int32));
    }

    [Test]
    public void GetDataTypeForByteEnumTest()
    {
        // Byte-based enum should map to UInt8
        var witDataType = WitTypeConverter.GetDataType<TestByteEnum>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.UInt8));
    }

    [Test]
    public void GetDataTypeForLongEnumTest()
    {
        // Long-based enum should map to Int64
        var witDataType = WitTypeConverter.GetDataType<TestLongEnum>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.Int64));
    }

    [Test]
    public void GetDataTypeForFlagsEnumTest()
    {
        // Flags enum should work the same as regular enum
        var witDataType = WitTypeConverter.GetDataType<TestFlagsEnum>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.Int32));
    }

    [Test]
    public void GetDataTypeForNullableEnumTest()
    {
        var witDataType = WitTypeConverter.GetDataType<TestIntEnum?>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.Int32));
    }

    [Test]
    public void IsSupportedForEnumsTest()
    {
        Assert.That(WitTypeConverter.IsSupported(typeof(TestIntEnum)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(TestByteEnum)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(TestLongEnum)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(TestFlagsEnum)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(TestIntEnum?)), Is.True);
    }

    #endregion

    #region Enum Serialization Tests

    [Test]
    public void EnumSerializationRoundtripTest()
    {
        byte[] buffer = new byte[16];

        // Test int enum
        TestIntEnum intValue = TestIntEnum.Large;
        int written = WitDataTypeSerializer.Write(buffer, (int)intValue);
        var (readInt, _) = WitDataTypeSerializer.ReadInt32(buffer);
        TestIntEnum resultInt = (TestIntEnum)readInt;
        Assert.That(resultInt, Is.EqualTo(intValue));

        // Test byte enum
        TestByteEnum byteValue = TestByteEnum.Max;
        WitDataTypeSerializer.Write(buffer, (byte)byteValue);
        TestByteEnum resultByte = (TestByteEnum)WitDataTypeSerializer.ReadUInt8(buffer);
        Assert.That(resultByte, Is.EqualTo(byteValue));

        // Test long enum
        TestLongEnum longValue = TestLongEnum.Max;
        WitDataTypeSerializer.Write(buffer, (long)longValue);
        var (readLong, _) = WitDataTypeSerializer.ReadInt64(buffer);
        TestLongEnum resultLong = (TestLongEnum)readLong;
        Assert.That(resultLong, Is.EqualTo(longValue));
    }

    [Test]
    public void FlagsEnumSerializationTest()
    {
        byte[] buffer = new byte[16];

        TestFlagsEnum value = TestFlagsEnum.Read | TestFlagsEnum.Write;
        WitDataTypeSerializer.Write(buffer, (int)value);
        var (readValue, _) = WitDataTypeSerializer.ReadInt32(buffer);
        TestFlagsEnum result = (TestFlagsEnum)readValue;

        Assert.That(result, Is.EqualTo(value));
        Assert.That(result.HasFlag(TestFlagsEnum.Read), Is.True);
        Assert.That(result.HasFlag(TestFlagsEnum.Write), Is.True);
        Assert.That(result.HasFlag(TestFlagsEnum.Execute), Is.False);
    }

    #endregion

    #region Fixed/Variable Size Tests

    [Test]
    public void IsFixedSizeTest()
    {
        Assert.That(WitDataType.Null.IsFixedSize(), Is.True);
        Assert.That(WitDataType.Int8.IsFixedSize(), Is.True);
        Assert.That(WitDataType.UInt8.IsFixedSize(), Is.True);
        Assert.That(WitDataType.Int16.IsFixedSize(), Is.True);
        Assert.That(WitDataType.UInt16.IsFixedSize(), Is.True);
        Assert.That(WitDataType.Float16.IsFixedSize(), Is.True);
        Assert.That(WitDataType.Float32.IsFixedSize(), Is.True);
        Assert.That(WitDataType.Float64.IsFixedSize(), Is.True);
        Assert.That(WitDataType.Decimal.IsFixedSize(), Is.True);
        Assert.That(WitDataType.Boolean.IsFixedSize(), Is.True);
        Assert.That(WitDataType.DateOnly.IsFixedSize(), Is.True);
        Assert.That(WitDataType.TimeOnly.IsFixedSize(), Is.True);
        Assert.That(WitDataType.DateTime.IsFixedSize(), Is.True);
        Assert.That(WitDataType.DateTimeOffset.IsFixedSize(), Is.True);
        Assert.That(WitDataType.TimeSpan.IsFixedSize(), Is.True);
        Assert.That(WitDataType.Guid.IsFixedSize(), Is.True);
        Assert.That(WitDataType.StringFixed.IsFixedSize(), Is.True);
        Assert.That(WitDataType.BinaryFixed.IsFixedSize(), Is.True);
        Assert.That(WitDataType.RowVersion.IsFixedSize(), Is.True);

        Assert.That(WitDataType.Int32.IsFixedSize(), Is.False); // VarInt
        Assert.That(WitDataType.UInt32.IsFixedSize(), Is.False);
        Assert.That(WitDataType.Int64.IsFixedSize(), Is.False);
        Assert.That(WitDataType.UInt64.IsFixedSize(), Is.False);
        Assert.That(WitDataType.StringVariable.IsFixedSize(), Is.False);
        Assert.That(WitDataType.BinaryVariable.IsFixedSize(), Is.False);
        Assert.That(WitDataType.Json.IsFixedSize(), Is.False);
    }

    [Test]
    public void IsVariableSizeTest()
    {
        Assert.That(WitDataType.Int32.IsVariableSize(), Is.True);
        Assert.That(WitDataType.UInt32.IsVariableSize(), Is.True);
        Assert.That(WitDataType.Int64.IsVariableSize(), Is.True);
        Assert.That(WitDataType.UInt64.IsVariableSize(), Is.True);
        Assert.That(WitDataType.StringVariable.IsVariableSize(), Is.True);
        Assert.That(WitDataType.BinaryVariable.IsVariableSize(), Is.True);
        Assert.That(WitDataType.Json.IsVariableSize(), Is.True);

        Assert.That(WitDataType.Int8.IsVariableSize(), Is.False);
        Assert.That(WitDataType.DateTime.IsVariableSize(), Is.False);
        Assert.That(WitDataType.Guid.IsVariableSize(), Is.False);
        Assert.That(WitDataType.StringFixed.IsVariableSize(), Is.False);
        Assert.That(WitDataType.BinaryFixed.IsVariableSize(), Is.False);
        Assert.That(WitDataType.RowVersion.IsVariableSize(), Is.False);
    }

    #endregion

    #region Unsupported Type Test

    private class UnsupportedType { }

    [Test]
    public void UnsupportedTypeThrowsTest()
    {
        Assert.Throws<NotSupportedException>(() => WitTypeConverter.GetDataType<UnsupportedType>());
    }

    [Test]
    public void IsSupportedForPrimitivesTest()
    {
        Assert.That(WitTypeConverter.IsSupported(typeof(int)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(string)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(DateTime)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(Guid)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(decimal)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(bool)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(byte[])), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(JsonDocument)), Is.True);
        Assert.That(WitTypeConverter.IsSupported(typeof(JsonElement)), Is.True);

        Assert.That(WitTypeConverter.IsSupported(typeof(UnsupportedType)), Is.False);
        Assert.That(WitTypeConverter.IsSupported(typeof(object)), Is.False);
    }

    #endregion
}
