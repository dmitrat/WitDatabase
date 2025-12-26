using System.Text.Json;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Types;

[TestFixture]
public class WitDataTypeSerializerTest
{
    private byte[] m_buffer = null!;

    [SetUp]
    public void SetUp()
    {
        m_buffer = new byte[4096];
    }

    #region Integer Tests

    [Test]
    public void Int8RoundtripTest()
    {
        sbyte[] values = [sbyte.MinValue, -1, 0, 1, sbyte.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadInt8(m_buffer);
            
            Assert.That(written, Is.EqualTo(1));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void UInt8RoundtripTest()
    {
        byte[] values = [0, 1, 127, 255];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadUInt8(m_buffer);
            
            Assert.That(written, Is.EqualTo(1));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void Int16RoundtripTest()
    {
        short[] values = [short.MinValue, -1, 0, 1, short.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadInt16(m_buffer);
            
            Assert.That(written, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void UInt16RoundtripTest()
    {
        ushort[] values = [0, 1, 127, ushort.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadUInt16(m_buffer);
            
            Assert.That(written, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void Int32RoundtripTest()
    {
        int[] values = [int.MinValue, -1, 0, 1, 127, 128, 16383, 16384, int.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var (result, bytesRead) = WitDataTypeSerializer.ReadInt32(m_buffer);
            
            Assert.That(bytesRead, Is.EqualTo(written));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void UInt32RoundtripTest()
    {
        uint[] values = [0, 1, 127, 128, 16383, 16384, uint.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var (result, bytesRead) = WitDataTypeSerializer.ReadUInt32(m_buffer);
            
            Assert.That(bytesRead, Is.EqualTo(written));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void Int64RoundtripTest()
    {
        long[] values = [long.MinValue, -1, 0, 1, int.MaxValue, long.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var (result, bytesRead) = WitDataTypeSerializer.ReadInt64(m_buffer);
            
            Assert.That(bytesRead, Is.EqualTo(written));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void UInt64RoundtripTest()
    {
        ulong[] values = [0, 1, 127, uint.MaxValue, ulong.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var (result, bytesRead) = WitDataTypeSerializer.ReadUInt64(m_buffer);
            
            Assert.That(bytesRead, Is.EqualTo(written));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    #endregion

    #region Floating Point Tests

    [Test]
    public void Float16RoundtripTest()
    {
        Half[] values = [Half.MinValue, Half.NegativeOne, Half.Zero, Half.One, Half.MaxValue, Half.NaN, Half.PositiveInfinity];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadFloat16(m_buffer);
            
            Assert.That(written, Is.EqualTo(2));
            if (Half.IsNaN(value))
                Assert.That(Half.IsNaN(result), Is.True);
            else
                Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void Float32RoundtripTest()
    {
        float[] values = [float.MinValue, -1.5f, 0f, 1.5f, float.MaxValue, float.NaN, float.PositiveInfinity];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadFloat32(m_buffer);
            
            Assert.That(written, Is.EqualTo(4));
            if (float.IsNaN(value))
                Assert.That(float.IsNaN(result), Is.True);
            else
                Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void Float64RoundtripTest()
    {
        double[] values = [double.MinValue, -1.5, 0, 1.5, double.MaxValue, Math.PI, double.NaN];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadFloat64(m_buffer);
            
            Assert.That(written, Is.EqualTo(8));
            if (double.IsNaN(value))
                Assert.That(double.IsNaN(result), Is.True);
            else
                Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void DecimalRoundtripTest()
    {
        decimal[] values = [decimal.MinValue, -1.5m, 0m, 1.5m, decimal.MaxValue, 123456789.123456789m];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadDecimal(m_buffer);
            
            Assert.That(written, Is.EqualTo(16));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    #endregion

    #region Boolean Test

    [Test]
    public void BooleanRoundtripTest()
    {
        int written = WitDataTypeSerializer.Write(m_buffer, true);
        Assert.That(WitDataTypeSerializer.ReadBoolean(m_buffer), Is.True);
        Assert.That(written, Is.EqualTo(1));
        
        WitDataTypeSerializer.Write(m_buffer, false);
        Assert.That(WitDataTypeSerializer.ReadBoolean(m_buffer), Is.False);
    }

    #endregion

    #region Date/Time Tests

    [Test]
    public void DateOnlyRoundtripTest()
    {
        DateOnly[] values = [DateOnly.MinValue, new DateOnly(2024, 12, 10), DateOnly.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadDateOnly(m_buffer);
            
            Assert.That(written, Is.EqualTo(4));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void TimeOnlyRoundtripTest()
    {
        TimeOnly[] values = [TimeOnly.MinValue, new TimeOnly(12, 30, 45, 123), TimeOnly.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadTimeOnly(m_buffer);
            
            Assert.That(written, Is.EqualTo(8));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void DateTimeRoundtripTest()
    {
        DateTime[] values = [DateTime.MinValue, DateTime.Now, DateTime.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadDateTime(m_buffer);
            
            Assert.That(written, Is.EqualTo(8));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void DateTimeOffsetRoundtripTest()
    {
        DateTimeOffset[] values = 
        [
            DateTimeOffset.MinValue, 
            DateTimeOffset.Now,
            new DateTimeOffset(2024, 12, 10, 15, 30, 0, TimeSpan.FromHours(3)),
            DateTimeOffset.MaxValue
        ];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadDateTimeOffset(m_buffer);
            
            Assert.That(written, Is.EqualTo(10));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void TimeSpanRoundtripTest()
    {
        TimeSpan[] values = [TimeSpan.MinValue, TimeSpan.Zero, TimeSpan.FromDays(365), TimeSpan.MaxValue];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadTimeSpan(m_buffer);
            
            Assert.That(written, Is.EqualTo(8));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    #endregion

    #region Guid Test

    [Test]
    public void GuidRoundtripTest()
    {
        Guid[] values = [Guid.Empty, Guid.NewGuid(), Guid.Parse("12345678-1234-1234-1234-123456789abc")];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            var result = WitDataTypeSerializer.ReadGuid(m_buffer);
            
            Assert.That(written, Is.EqualTo(16));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    #endregion

    #region String Tests

    [Test]
    public void StringVariableRoundtripTest()
    {
        string[] values = ["", "Hello", "Привет мир! 你好世界", new string('x', 1000)];
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.WriteString(m_buffer, value);
            var (result, bytesRead) = WitDataTypeSerializer.ReadString(m_buffer);
            
            Assert.That(bytesRead, Is.EqualTo(written));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void StringFixedRoundtripTest()
    {
        const int fixedLength = 20;
        
        string short_value = "Hello";
        int written = WitDataTypeSerializer.WriteStringFixed(m_buffer, short_value, fixedLength);
        var result = WitDataTypeSerializer.ReadStringFixed(m_buffer, fixedLength);
        
        Assert.That(written, Is.EqualTo(fixedLength));
        Assert.That(result, Is.EqualTo(short_value));
    }

    [Test]
    public void StringNullRoundtripTest()
    {
        int written = WitDataTypeSerializer.WriteString(m_buffer, null!);
        var (result, bytesRead) = WitDataTypeSerializer.ReadString(m_buffer);
        
        Assert.That(bytesRead, Is.EqualTo(written));
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    #endregion

    #region Binary Tests

    [Test]
    public void BinaryVariableRoundtripTest()
    {
        byte[][] values = [[], [1, 2, 3], new byte[500]];
        Random.Shared.NextBytes(values[2]);
        
        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.WriteBinary(m_buffer, value);
            var (result, bytesRead) = WitDataTypeSerializer.ReadBinary(m_buffer);
            
            Assert.That(bytesRead, Is.EqualTo(written));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void BinaryFixedRoundtripTest()
    {
        const int fixedLength = 32;
        byte[] value = new byte[20];
        Random.Shared.NextBytes(value);
        
        int written = WitDataTypeSerializer.WriteBinaryFixed(m_buffer, value, fixedLength);
        var result = WitDataTypeSerializer.ReadBinaryFixed(m_buffer, fixedLength);
        
        Assert.That(written, Is.EqualTo(fixedLength));
        Assert.That(result[..20], Is.EqualTo(value));
        Assert.That(result[20..].All(b => b == 0), Is.True); // Padding is zeros
    }

    #endregion

    #region RowVersion Tests

    [Test]
    public void RowVersionRoundtripTest()
    {
        ulong[] values = [0, 1, 12345678901234567890UL, ulong.MaxValue];

        foreach (var value in values)
        {
            int written = WitDataTypeSerializer.WriteRowVersion(m_buffer, value);
            var result = WitDataTypeSerializer.ReadRowVersion(m_buffer);

            Assert.That(written, Is.EqualTo(8));
            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    public void RowVersionBytesRoundtripTest()
    {
        byte[] value = [1, 2, 3, 4, 5, 6, 7, 8];

        int written = WitDataTypeSerializer.WriteRowVersionBytes(m_buffer, value);
        var result = WitDataTypeSerializer.ReadRowVersionBytes(m_buffer);

        Assert.That(written, Is.EqualTo(8));
        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    public void RowVersionBytesThrowsOnInvalidLengthTest()
    {
        byte[] invalidValue = [1, 2, 3, 4, 5]; // Not 8 bytes

        Assert.Throws<ArgumentException>(() => WitDataTypeSerializer.WriteRowVersionBytes(m_buffer, invalidValue));
    }

    #endregion

    #region Json Tests

    [Test]
    public void JsonDocumentRoundtripTest()
    {
        string jsonString = """{"name":"John","age":30,"active":true}""";
        using var document = JsonDocument.Parse(jsonString);

        int written = WitDataTypeSerializer.WriteJson(m_buffer, document);
        var (result, bytesRead) = WitDataTypeSerializer.ReadJson(m_buffer);

        Assert.That(bytesRead, Is.EqualTo(written));
        Assert.That(result.RootElement.GetProperty("name").GetString(), Is.EqualTo("John"));
        Assert.That(result.RootElement.GetProperty("age").GetInt32(), Is.EqualTo(30));
        Assert.That(result.RootElement.GetProperty("active").GetBoolean(), Is.True);

        result.Dispose();
    }

    [Test]
    public void JsonStringRoundtripTest()
    {
        string jsonString = """{"items":[1,2,3],"nested":{"key":"value"}}""";

        int written = WitDataTypeSerializer.WriteJsonString(m_buffer, jsonString);
        var (result, bytesRead) = WitDataTypeSerializer.ReadJsonAsString(m_buffer);

        Assert.That(bytesRead, Is.EqualTo(written));
        Assert.That(result, Is.EqualTo(jsonString));
    }

    [Test]
    public void JsonEmptyObjectRoundtripTest()
    {
        using var document = JsonDocument.Parse("{}");

        int written = WitDataTypeSerializer.WriteJson(m_buffer, document);
        var (result, bytesRead) = WitDataTypeSerializer.ReadJson(m_buffer);

        Assert.That(bytesRead, Is.EqualTo(written));
        Assert.That(result.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));

        result.Dispose();
    }

    [Test]
    public void JsonArrayRoundtripTest()
    {
        string jsonString = """[1,"two",true,null]""";
        using var document = JsonDocument.Parse(jsonString);

        int written = WitDataTypeSerializer.WriteJson(m_buffer, document);
        var (result, bytesRead) = WitDataTypeSerializer.ReadJson(m_buffer);

        Assert.That(bytesRead, Is.EqualTo(written));
        Assert.That(result.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(result.RootElement.GetArrayLength(), Is.EqualTo(4));

        result.Dispose();
    }

    [Test]
    public void JsonComplexRoundtripTest()
    {
        string jsonString = """
            {
                "users": [
                    {"id": 1, "name": "Alice"},
                    {"id": 2, "name": "Bob"}
                ],
                "metadata": {
                    "version": "1.0",
                    "tags": ["test", "demo"]
                }
            }
            """;
        using var document = JsonDocument.Parse(jsonString);

        int written = WitDataTypeSerializer.WriteJson(m_buffer, document);
        var (result, bytesRead) = WitDataTypeSerializer.ReadJson(m_buffer);

        Assert.That(bytesRead, Is.EqualTo(written));
        Assert.That(result.RootElement.GetProperty("users").GetArrayLength(), Is.EqualTo(2));
        Assert.That(result.RootElement.GetProperty("metadata").GetProperty("version").GetString(), Is.EqualTo("1.0"));

        result.Dispose();
    }

    [Test]
    public void JsonStringThrowsOnInvalidJsonTest()
    {
        string invalidJson = "not valid json {";

        Assert.That(() => WitDataTypeSerializer.WriteJsonString(m_buffer, invalidJson), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void JsonEncodedSizeTest()
    {
        string jsonString = """{"key":"value"}""";
        using var document = JsonDocument.Parse(jsonString);

        int size = WitDataTypeSerializer.GetEncodedSize(document);
        int written = WitDataTypeSerializer.WriteJson(m_buffer, document);

        Assert.That(size, Is.EqualTo(written));
    }

    [Test]
    public void JsonStringEncodedSizeTest()
    {
        string jsonString = """{"key":"value"}""";

        int size = WitDataTypeSerializer.GetJsonStringEncodedSize(jsonString);
        int written = WitDataTypeSerializer.WriteJsonString(m_buffer, jsonString);

        Assert.That(size, Is.EqualTo(written));
    }

    #endregion

    #region Size Calculation Tests

    [Test]
    public void GetStringEncodedSizeTest()
    {
        string value = "Hello";
        int size = WitDataTypeSerializer.GetEncodedSize(value);
        int written = WitDataTypeSerializer.WriteString(m_buffer, value);
        
        Assert.That(size, Is.EqualTo(written));
    }

    [Test]
    public void GetInt32EncodedSizeTest()
    {
        int[] values = [0, 127, 128, 16383, 16384, int.MaxValue];
        
        foreach (var value in values)
        {
            int size = WitDataTypeSerializer.GetEncodedSize(value);
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            
            Assert.That(size, Is.EqualTo(written));
        }
    }

    [Test]
    public void GetUInt32EncodedSizeTest()
    {
        uint[] values = [0, 127, 128, 16383, 16384, uint.MaxValue];
        
        foreach (var value in values)
        {
            int size = WitDataTypeSerializer.GetEncodedSize(value);
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            
            Assert.That(size, Is.EqualTo(written));
        }
    }

    [Test]
    public void GetInt64EncodedSizeTest()
    {
        long[] values = [0, 127, 128, int.MaxValue, long.MaxValue];
        
        foreach (var value in values)
        {
            int size = WitDataTypeSerializer.GetEncodedSize(value);
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            
            Assert.That(size, Is.EqualTo(written));
        }
    }

    [Test]
    public void GetUInt64EncodedSizeTest()
    {
        ulong[] values = [0, 127, 128, uint.MaxValue, ulong.MaxValue];
        
        foreach (var value in values)
        {
            int size = WitDataTypeSerializer.GetEncodedSize(value);
            int written = WitDataTypeSerializer.Write(m_buffer, value);
            
            Assert.That(size, Is.EqualTo(written));
        }
    }

    [Test]
    public void GetBinaryEncodedSizeTest()
    {
        byte[] value = new byte[100];
        Random.Shared.NextBytes(value);
        
        int size = WitDataTypeSerializer.GetEncodedSize(value.AsSpan());
        int written = WitDataTypeSerializer.WriteBinary(m_buffer, value);
        
        Assert.That(size, Is.EqualTo(written));
    }

    #endregion

    #region GetFixedSize Tests

    [Test]
    public void GetFixedSizeTest()
    {
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Null), Is.EqualTo(0));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Int8), Is.EqualTo(1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.UInt8), Is.EqualTo(1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Boolean), Is.EqualTo(1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Int16), Is.EqualTo(2));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.UInt16), Is.EqualTo(2));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Float16), Is.EqualTo(2));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Float32), Is.EqualTo(4));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.DateOnly), Is.EqualTo(4));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Float64), Is.EqualTo(8));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.DateTime), Is.EqualTo(8));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.TimeOnly), Is.EqualTo(8));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.TimeSpan), Is.EqualTo(8));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.DateTimeOffset), Is.EqualTo(10));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Decimal), Is.EqualTo(16));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Guid), Is.EqualTo(16));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.RowVersion), Is.EqualTo(8));
        
        // Variable types return -1
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Int32), Is.EqualTo(-1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.UInt32), Is.EqualTo(-1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Int64), Is.EqualTo(-1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.UInt64), Is.EqualTo(-1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.StringFixed), Is.EqualTo(-1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.StringVariable), Is.EqualTo(-1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.BinaryFixed), Is.EqualTo(-1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.BinaryVariable), Is.EqualTo(-1));
        Assert.That(WitDataTypeSerializer.GetFixedSize(WitDataType.Json), Is.EqualTo(-1));
    }

    #endregion
}
