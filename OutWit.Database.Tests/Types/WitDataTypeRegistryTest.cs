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
    public void GetWitDataTypeForPrimitivesTest()
    {
        Assert.That(WitDataTypeRegistry.GetWitDataType<sbyte>(), Is.EqualTo(WitDataType.Int8));
        Assert.That(WitDataTypeRegistry.GetWitDataType<byte>(), Is.EqualTo(WitDataType.UInt8));
        Assert.That(WitDataTypeRegistry.GetWitDataType<short>(), Is.EqualTo(WitDataType.Int16));
        Assert.That(WitDataTypeRegistry.GetWitDataType<ushort>(), Is.EqualTo(WitDataType.UInt16));
        Assert.That(WitDataTypeRegistry.GetWitDataType<int>(), Is.EqualTo(WitDataType.Int32));
        Assert.That(WitDataTypeRegistry.GetWitDataType<uint>(), Is.EqualTo(WitDataType.UInt32));
        Assert.That(WitDataTypeRegistry.GetWitDataType<long>(), Is.EqualTo(WitDataType.Int64));
        Assert.That(WitDataTypeRegistry.GetWitDataType<ulong>(), Is.EqualTo(WitDataType.UInt64));
        Assert.That(WitDataTypeRegistry.GetWitDataType<Half>(), Is.EqualTo(WitDataType.Float16));
        Assert.That(WitDataTypeRegistry.GetWitDataType<float>(), Is.EqualTo(WitDataType.Float32));
        Assert.That(WitDataTypeRegistry.GetWitDataType<double>(), Is.EqualTo(WitDataType.Float64));
        Assert.That(WitDataTypeRegistry.GetWitDataType<bool>(), Is.EqualTo(WitDataType.Boolean));
        Assert.That(WitDataTypeRegistry.GetWitDataType<DateOnly>(), Is.EqualTo(WitDataType.DateOnly));
        Assert.That(WitDataTypeRegistry.GetWitDataType<TimeOnly>(), Is.EqualTo(WitDataType.TimeOnly));
        Assert.That(WitDataTypeRegistry.GetWitDataType<DateTime>(), Is.EqualTo(WitDataType.DateTime));
        Assert.That(WitDataTypeRegistry.GetWitDataType<DateTimeOffset>(), Is.EqualTo(WitDataType.DateTimeOffset));
        Assert.That(WitDataTypeRegistry.GetWitDataType<TimeSpan>(), Is.EqualTo(WitDataType.TimeSpan));
        Assert.That(WitDataTypeRegistry.GetWitDataType<Guid>(), Is.EqualTo(WitDataType.Guid));
        Assert.That(WitDataTypeRegistry.GetWitDataType<string>(), Is.EqualTo(WitDataType.StringVariable));
        Assert.That(WitDataTypeRegistry.GetWitDataType<byte[]>(), Is.EqualTo(WitDataType.BinaryVariable));
        Assert.That(WitDataTypeRegistry.GetWitDataType<decimal>(), Is.EqualTo(WitDataType.Decimal));
    }

    [Test]
    public void GetWitDataTypeForNullablesTest()
    {
        Assert.That(WitDataTypeRegistry.GetWitDataType<int?>(), Is.EqualTo(WitDataType.Int32));
        Assert.That(WitDataTypeRegistry.GetWitDataType<DateTime?>(), Is.EqualTo(WitDataType.DateTime));
        Assert.That(WitDataTypeRegistry.GetWitDataType<Guid?>(), Is.EqualTo(WitDataType.Guid));
        Assert.That(WitDataTypeRegistry.GetWitDataType<bool?>(), Is.EqualTo(WitDataType.Boolean));
        Assert.That(WitDataTypeRegistry.GetWitDataType<decimal?>(), Is.EqualTo(WitDataType.Decimal));
    }

    #endregion

    #region GetClrType Tests

    [Test]
    public void GetClrTypeTest()
    {
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Int8), Is.EqualTo(typeof(sbyte)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.UInt8), Is.EqualTo(typeof(byte)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Int16), Is.EqualTo(typeof(short)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.UInt16), Is.EqualTo(typeof(ushort)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Int32), Is.EqualTo(typeof(int)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.UInt32), Is.EqualTo(typeof(uint)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Int64), Is.EqualTo(typeof(long)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.UInt64), Is.EqualTo(typeof(ulong)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Float16), Is.EqualTo(typeof(Half)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Float32), Is.EqualTo(typeof(float)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Float64), Is.EqualTo(typeof(double)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Decimal), Is.EqualTo(typeof(decimal)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Boolean), Is.EqualTo(typeof(bool)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.DateOnly), Is.EqualTo(typeof(DateOnly)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.TimeOnly), Is.EqualTo(typeof(TimeOnly)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.DateTime), Is.EqualTo(typeof(DateTime)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.DateTimeOffset), Is.EqualTo(typeof(DateTimeOffset)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.TimeSpan), Is.EqualTo(typeof(TimeSpan)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Guid), Is.EqualTo(typeof(Guid)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.StringVariable), Is.EqualTo(typeof(string)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.StringFixed), Is.EqualTo(typeof(string)));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.BinaryVariable), Is.EqualTo(typeof(byte[])));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.BinaryFixed), Is.EqualTo(typeof(byte[])));
        Assert.That(WitDataTypeRegistry.GetClrType(WitDataType.Null), Is.EqualTo(typeof(DBNull)));
    }

    #endregion

    #region Enum Tests

    [Test]
    public void GetWitDataTypeForIntEnumTest()
    {
        // Int-based enum should map to Int32
        var witDataType = WitDataTypeRegistry.GetWitDataType<TestIntEnum>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.Int32));
    }

    [Test]
    public void GetWitDataTypeForByteEnumTest()
    {
        // Byte-based enum should map to UInt8
        var witDataType = WitDataTypeRegistry.GetWitDataType<TestByteEnum>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.UInt8));
    }

    [Test]
    public void GetWitDataTypeForLongEnumTest()
    {
        // Long-based enum should map to Int64
        var witDataType = WitDataTypeRegistry.GetWitDataType<TestLongEnum>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.Int64));
    }

    [Test]
    public void GetWitDataTypeForFlagsEnumTest()
    {
        // Flags enum should work the same as regular enum
        var witDataType = WitDataTypeRegistry.GetWitDataType<TestFlagsEnum>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.Int32));
    }

    [Test]
    public void GetWitDataTypeForNullableEnumTest()
    {
        var witDataType = WitDataTypeRegistry.GetWitDataType<TestIntEnum?>();
        Assert.That(witDataType, Is.EqualTo(WitDataType.Int32));
    }

    [Test]
    public void IsSupportedForEnumsTest()
    {
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(TestIntEnum)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(TestByteEnum)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(TestLongEnum)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(TestFlagsEnum)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(TestIntEnum?)), Is.True);
    }

    [Test]
    public void IsEnumTest()
    {
        Assert.That(WitDataTypeRegistry.IsEnum(typeof(TestIntEnum)), Is.True);
        Assert.That(WitDataTypeRegistry.IsEnum(typeof(TestIntEnum?)), Is.True);
        Assert.That(WitDataTypeRegistry.IsEnum(typeof(int)), Is.False);
        Assert.That(WitDataTypeRegistry.IsEnum(typeof(string)), Is.False);
    }

    [Test]
    public void GetEnumUnderlyingTypeTest()
    {
        Assert.That(WitDataTypeRegistry.GetEnumUnderlyingType(typeof(TestIntEnum)), Is.EqualTo(typeof(int)));
        Assert.That(WitDataTypeRegistry.GetEnumUnderlyingType(typeof(TestByteEnum)), Is.EqualTo(typeof(byte)));
        Assert.That(WitDataTypeRegistry.GetEnumUnderlyingType(typeof(TestLongEnum)), Is.EqualTo(typeof(long)));
        Assert.That(WitDataTypeRegistry.GetEnumUnderlyingType(typeof(int)), Is.EqualTo(typeof(int))); // Not an enum, returns itself
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
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Null), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Int8), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.UInt8), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Int16), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.UInt16), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Float16), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Float32), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Float64), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Decimal), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Boolean), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.DateOnly), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.TimeOnly), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.DateTime), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.DateTimeOffset), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.TimeSpan), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Guid), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.StringFixed), Is.True);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.BinaryFixed), Is.True);

        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Int32), Is.False); // VarInt
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.UInt32), Is.False);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.Int64), Is.False);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.UInt64), Is.False);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.StringVariable), Is.False);
        Assert.That(WitDataTypeRegistry.IsFixedSize(WitDataType.BinaryVariable), Is.False);
    }

    [Test]
    public void IsVariableSizeTest()
    {
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.Int32), Is.True);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.UInt32), Is.True);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.Int64), Is.True);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.UInt64), Is.True);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.StringVariable), Is.True);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.BinaryVariable), Is.True);

        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.Int8), Is.False);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.DateTime), Is.False);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.Guid), Is.False);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.StringFixed), Is.False);
        Assert.That(WitDataTypeRegistry.IsVariableSize(WitDataType.BinaryFixed), Is.False);
    }

    #endregion

    #region Nullable Tests

    [Test]
    public void IsNullableTest()
    {
        Assert.That(WitDataTypeRegistry.IsNullable(typeof(int?)), Is.True);
        Assert.That(WitDataTypeRegistry.IsNullable(typeof(DateTime?)), Is.True);
        Assert.That(WitDataTypeRegistry.IsNullable(typeof(string)), Is.True); // Reference type
        Assert.That(WitDataTypeRegistry.IsNullable(typeof(byte[])), Is.True); // Reference type

        Assert.That(WitDataTypeRegistry.IsNullable(typeof(int)), Is.False);
        Assert.That(WitDataTypeRegistry.IsNullable(typeof(DateTime)), Is.False);
        Assert.That(WitDataTypeRegistry.IsNullable(typeof(Guid)), Is.False);
    }

    #endregion

    #region Unsupported Type Test

    private class UnsupportedType { }

    [Test]
    public void UnsupportedTypeThrowsTest()
    {
        Assert.Throws<NotSupportedException>(() => WitDataTypeRegistry.GetWitDataType<UnsupportedType>());
    }

    [Test]
    public void IsSupportedForPrimitivesTest()
    {
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(int)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(string)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(DateTime)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(Guid)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(decimal)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(bool)), Is.True);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(byte[])), Is.True);

        Assert.That(WitDataTypeRegistry.IsSupported(typeof(UnsupportedType)), Is.False);
        Assert.That(WitDataTypeRegistry.IsSupported(typeof(object)), Is.False);
    }

    #endregion
}
