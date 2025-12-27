using OutWit.Database.Types;
using OutWit.Database.Values;
using System.Text.Json;

namespace OutWit.Database.Tests.Types;

/// <summary>
/// Tests to ensure all WitSqlType values are properly handled across the codebase.
/// These tests serve as a safety net when adding new types.
/// </summary>
[TestFixture]
public class WitSqlTypeCompletenessTests
{
    #region Type Count Validation

    [Test]
    public void TypeCountMatchesEnumValues()
    {
        var enumValues = Enum.GetValues<WitSqlType>();
        
        Assert.That(enumValues.Length, Is.EqualTo(WitTypeConverter.SqlTypeCount),
            $"WitTypeConverter.SqlTypeCount ({WitTypeConverter.SqlTypeCount}) does not match actual enum count ({enumValues.Length}). " +
            "Update SqlTypeCount when adding new types!");
    }

    [Test]
    public void AllTypesListContainsAllEnumValues()
    {
        var enumValues = Enum.GetValues<WitSqlType>().ToHashSet();
        var listedTypes = WitTypeConverter.AllSqlTypes.ToHashSet();
        
        Assert.That(listedTypes, Is.EquivalentTo(enumValues),
            "WitTypeConverter.AllSqlTypes does not contain all enum values. Update it when adding new types!");
    }

    #endregion

    #region Factory Method Coverage

    [Test]
    public void AllTypesHaveFactoryMethod()
    {
        var missingFactoryMethods = new List<WitSqlType>();

        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            if (type == WitSqlType.Null)
                continue; // Null uses WitSqlValue.Null static property

            try
            {
                var value = CreateValueForType(type);
                Assert.That(value.Type, Is.EqualTo(type), 
                    $"Factory method for {type} returned wrong type {value.Type}");
            }
            catch (NotSupportedException)
            {
                missingFactoryMethods.Add(type);
            }
        }

        Assert.That(missingFactoryMethods, Is.Empty,
            $"Missing factory methods for: {string.Join(", ", missingFactoryMethods)}");
    }

    private static WitSqlValue CreateValueForType(WitSqlType type) => type switch
    {
        WitSqlType.Null => WitSqlValue.Null,
        WitSqlType.Integer => WitSqlValue.FromInt(42),
        WitSqlType.Real => WitSqlValue.FromReal(3.14),
        WitSqlType.Text => WitSqlValue.FromText("test"),
        WitSqlType.Blob => WitSqlValue.FromBlob([1, 2, 3]),
        WitSqlType.Boolean => WitSqlValue.True,
        WitSqlType.Decimal => WitSqlValue.FromDecimal(123.45m),
        WitSqlType.DateTime => WitSqlValue.FromDateTime(new DateTime(2025, 1, 1, 12, 0, 0)),
        WitSqlType.DateOnly => WitSqlValue.FromDateOnly(new DateOnly(2025, 1, 1)),
        WitSqlType.TimeOnly => WitSqlValue.FromTimeOnly(new TimeOnly(12, 0, 0)),
        WitSqlType.TimeSpan => WitSqlValue.FromTimeSpan(TimeSpan.FromHours(1)),
        WitSqlType.Guid => WitSqlValue.FromGuid(Guid.Parse("12345678-1234-1234-1234-123456789012")),
        WitSqlType.DateTimeOffset => WitSqlValue.FromDateTimeOffset(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(3))),
        WitSqlType.Json => WitSqlValue.FromJsonString("{\"test\": 1}"),
        WitSqlType.RowVersion => WitSqlValue.FromRowVersion(12345UL),
        _ => throw new NotSupportedException($"No factory method test for {type}")
    };

    #endregion

    #region AsString Coverage

    [Test]
    public void AllTypesCanBeConvertedToString()
    {
        var failures = new List<string>();

        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            try
            {
                var value = CreateValueForType(type);
                var str = value.AsString();
                Assert.That(str, Is.Not.Null, $"AsString for {type} returned null");
            }
            catch (Exception ex)
            {
                failures.Add($"{type}: {ex.Message}");
            }
        }

        Assert.That(failures, Is.Empty,
            $"AsString failures:\n{string.Join("\n", failures)}");
    }

    #endregion

    #region Comparison Coverage

    [Test]
    public void AllTypesCanBeSelfCompared()
    {
        var failures = new List<string>();

        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            try
            {
                var value = CreateValueForType(type);

                // Test comparison with itself (not a new instance)
                var comparison = value.CompareTo(value);
                var equality = value.Equals(value);
                var hashCode = value.GetHashCode();

                Assert.That(equality, Is.True, $"Self-comparison (same instance) failed for {type}");
                Assert.That(comparison, Is.EqualTo(0), $"CompareTo(self) != 0 for {type}");
            }
            catch (Exception ex)
            {
                failures.Add($"{type}: {ex.Message}");
            }
        }

        Assert.That(failures, Is.Empty,
            $"Comparison failures:\n{string.Join("\n", failures)}");
    }

    [Test]
    public void AllTypesCanBeComparedWithNull()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            var value = CreateValueForType(type);
            var nullValue = WitSqlValue.Null;

            // Non-null > null
            if (type != WitSqlType.Null)
            {
                Assert.That(value.CompareTo(nullValue), Is.GreaterThan(0),
                    $"{type} should be greater than NULL");
                Assert.That(nullValue.CompareTo(value), Is.LessThan(0),
                    $"NULL should be less than {type}");
            }
        }
    }

    #endregion

    #region Type Info Methods

    [Test]
    public void GetSqlTypeNameCoversAllTypes()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            var name = type.GetSqlTypeName();
            Assert.That(name, Is.Not.EqualTo("UNKNOWN"),
                $"GetSqlTypeName returned UNKNOWN for {type}");
        }
    }

    [Test]
    public void GetClrTypeCoversAllTypes()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            var clrType = WitTypeConverter.GetClrType(type);
            Assert.That(clrType, Is.Not.EqualTo(typeof(object)),
                $"GetClrType returned object for {type}");
        }
    }

    [Test]
    public void GetDefaultValueCoversAllTypes()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            var defaultValue = WitTypeConverter.GetDefaultValue(type);
            
            if (type == WitSqlType.Null || type == WitSqlType.Json)
            {
                Assert.That(defaultValue.IsNull, Is.True,
                    $"Default value for {type} should be NULL");
            }
            else
            {
                Assert.That(defaultValue.Type, Is.EqualTo(type),
                    $"Default value type mismatch for {type}");
            }
        }
    }

    #endregion

    #region Storage Category Coverage

    [Test]
    public void AllTypesHaveExactlyOneStorageCategory()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            if (type == WitSqlType.Null)
                continue;

            var usesInt = type.UsesIntStorage();
            var usesReal = type.UsesRealStorage();
            var usesUlong = type.UsesUlongStorage();
            var usesObject = type.UsesObjectStorage();

            var storageCount = (usesInt ? 1 : 0) + (usesReal ? 1 : 0) + (usesUlong ? 1 : 0) + (usesObject ? 1 : 0);
            
            Assert.That(storageCount, Is.EqualTo(1),
                $"Type {type} has {storageCount} storage categories (should be exactly 1). " +
                $"UsesInt={usesInt}, UsesReal={usesReal}, UsesUlong={usesUlong}, UsesObject={usesObject}");
        }
    }

    #endregion

    #region WitDataType Mapping

    [Test]
    public void AllSqlTypesHaveDataTypeMapping()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            if (type == WitSqlType.Null)
                continue;

            var dataType = type.ToDataType();
            
            Assert.That(dataType, Is.Not.EqualTo(WitDataType.Null),
                $"ToDataType returned Null for {type}");
        }
    }

    [Test]
    public void DataTypeToSqlTypeRoundTrip()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            if (type == WitSqlType.Null)
                continue;

            var dataType = type.ToDataType();
            var backToSql = dataType.ToSqlType();
            
            Assert.That(backToSql, Is.EqualTo(type),
                $"Round trip failed: {type} -> {dataType} -> {backToSql}");
        }
    }

    #endregion

    #region Conversion Matrix

    [Test]
    public void ConversionToTextAlwaysSucceeds()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            Assert.That(WitTypeConverter.CanConvert(type, WitSqlType.Text), Is.True,
                $"CanConvert({type}, Text) should be true");
        }
    }

    [Test]
    public void ConversionFromNullAlwaysSucceeds()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            Assert.That(WitTypeConverter.CanConvert(WitSqlType.Null, type), Is.True,
                $"CanConvert(Null, {type}) should be true");
        }
    }

    [Test]
    public void SelfConversionAlwaysSucceeds()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            Assert.That(WitTypeConverter.CanConvert(type, type), Is.True,
                $"CanConvert({type}, {type}) should be true");
        }
    }

    #endregion

    #region Getter Coverage Tests

    [Test]
    public void AllNumericTypesCanConvertToInt64()
    {
        var numericTypes = new[] { WitSqlType.Integer, WitSqlType.Real, WitSqlType.Decimal, WitSqlType.Boolean };
        
        foreach (var type in numericTypes)
        {
            var value = CreateValueForType(type);
            var result = value.AsInt64();
            // Should not throw
        }
    }

    [Test]
    public void AllNumericTypesCanConvertToDouble()
    {
        var numericTypes = new[] { WitSqlType.Integer, WitSqlType.Real, WitSqlType.Decimal, WitSqlType.Boolean };
        
        foreach (var type in numericTypes)
        {
            var value = CreateValueForType(type);
            var result = value.AsDouble();
            // Should not throw
        }
    }

    [Test]
    public void AllNumericTypesCanConvertToDecimal()
    {
        var numericTypes = new[] { WitSqlType.Integer, WitSqlType.Real, WitSqlType.Decimal };
        
        foreach (var type in numericTypes)
        {
            var value = CreateValueForType(type);
            var result = value.AsDecimal();
            // Should not throw
        }
    }

    [Test]
    public void AllDateTimeTypesCanConvertToDateTime()
    {
        var dateTypes = new[] { WitSqlType.DateTime, WitSqlType.DateTimeOffset };
        
        foreach (var type in dateTypes)
        {
            var value = CreateValueForType(type);
            var result = value.AsDateTime();
            // Should not throw
        }
    }

    #endregion

    #region Centralized Convert Tests

    [Test]
    public void ConvertPreservesValueForSameType()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            if (type == WitSqlType.Null)
                continue;

            var value = CreateValueForType(type);
            var converted = WitTypeConverter.Convert(value, type);
            
            Assert.That(converted.Type, Is.EqualTo(type),
                $"Convert should preserve type for {type}");
        }
    }

    [Test]
    public void ConvertNullAlwaysReturnsNull()
    {
        foreach (var type in WitTypeConverter.AllSqlTypes)
        {
            var converted = WitTypeConverter.Convert(WitSqlValue.Null, type);
            Assert.That(converted.IsNull, Is.True,
                $"Convert(Null, {type}) should return Null");
        }
    }

    [Test]
    public void ConvertToTextPreservesValue()
    {
        var testValues = new Dictionary<WitSqlType, (WitSqlValue value, string expected)>
        {
            [WitSqlType.Integer] = (WitSqlValue.FromInt(42), "42"),
            [WitSqlType.Real] = (WitSqlValue.FromReal(3.14), "3.14"),
            [WitSqlType.Boolean] = (WitSqlValue.True, "true"),
            [WitSqlType.Decimal] = (WitSqlValue.FromDecimal(123.45m), "123.45"),
        };

        foreach (var (type, (value, expected)) in testValues)
        {
            var converted = WitTypeConverter.Convert(value, WitSqlType.Text);
            Assert.That(converted.AsString(), Is.EqualTo(expected),
                $"Convert({type} -> Text) failed");
        }
    }

    [Test]
    public void ConvertNumericTypes()
    {
        // Integer -> Real
        var intValue = WitSqlValue.FromInt(42);
        var realResult = WitTypeConverter.Convert(intValue, WitSqlType.Real);
        Assert.That(realResult.Type, Is.EqualTo(WitSqlType.Real));
        Assert.That(realResult.AsDouble(), Is.EqualTo(42.0));

        // Real -> Integer
        var realValue = WitSqlValue.FromReal(3.7);
        var intResult = WitTypeConverter.Convert(realValue, WitSqlType.Integer);
        Assert.That(intResult.Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(intResult.AsInt64(), Is.EqualTo(3)); // Truncated

        // Integer -> Decimal
        var decResult = WitTypeConverter.Convert(intValue, WitSqlType.Decimal);
        Assert.That(decResult.Type, Is.EqualTo(WitSqlType.Decimal));
        Assert.That(decResult.AsDecimal(), Is.EqualTo(42m));
    }

    [Test]
    public void ConvertDateTimeTypes()
    {
        var dt = new DateTime(2025, 6, 15, 14, 30, 45);
        var dtValue = WitSqlValue.FromDateTime(dt);

        // DateTime -> DateOnly
        var dateResult = WitTypeConverter.Convert(dtValue, WitSqlType.DateOnly);
        Assert.That(dateResult.Type, Is.EqualTo(WitSqlType.DateOnly));
        Assert.That(dateResult.AsDateOnly(), Is.EqualTo(new DateOnly(2025, 6, 15)));

        // DateTime -> TimeOnly
        var timeResult = WitTypeConverter.Convert(dtValue, WitSqlType.TimeOnly);
        Assert.That(timeResult.Type, Is.EqualTo(WitSqlType.TimeOnly));
        Assert.That(timeResult.AsTimeOnly(), Is.EqualTo(new TimeOnly(14, 30, 45)));
    }

    #endregion

    #region ParseSqlTypeName Tests

    [Test]
    public void ParseSqlTypeNameHandlesIntegerVariants()
    {
        var integerNames = new[] { "INT", "INTEGER", "INT32", "BIGINT", "INT64", "LONG", "SMALLINT", "TINYINT" };
        
        foreach (var name in integerNames)
        {
            var type = WitTypeConverter.ParseSqlTypeName(name);
            Assert.That(type, Is.EqualTo(WitSqlType.Integer),
                $"ParseSqlTypeName('{name}') should return Integer");
        }
    }

    [Test]
    public void ParseSqlTypeNameHandlesRealVariants()
    {
        var realNames = new[] { "FLOAT", "DOUBLE", "REAL", "FLOAT32", "FLOAT64" };
        
        foreach (var name in realNames)
        {
            var type = WitTypeConverter.ParseSqlTypeName(name);
            Assert.That(type, Is.EqualTo(WitSqlType.Real),
                $"ParseSqlTypeName('{name}') should return Real");
        }
    }

    [Test]
    public void ParseSqlTypeNameHandlesStringVariants()
    {
        var stringNames = new[] { "VARCHAR", "TEXT", "STRING", "NVARCHAR", "CHAR", "NCHAR" };
        
        foreach (var name in stringNames)
        {
            var type = WitTypeConverter.ParseSqlTypeName(name);
            Assert.That(type, Is.EqualTo(WitSqlType.Text),
                $"ParseSqlTypeName('{name}') should return Text");
        }
    }

    [Test]
    public void ParseSqlTypeNameIsCaseInsensitive()
    {
        Assert.That(WitTypeConverter.ParseSqlTypeName("int"), Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitTypeConverter.ParseSqlTypeName("INT"), Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitTypeConverter.ParseSqlTypeName("Int"), Is.EqualTo(WitSqlType.Integer));
        Assert.That(WitTypeConverter.ParseSqlTypeName("InTeGeR"), Is.EqualTo(WitSqlType.Integer));
    }

    [Test]
    public void ParseSqlTypeNameHandlesDateTimeTypes()
    {
        Assert.That(WitTypeConverter.ParseSqlTypeName("DATE"), Is.EqualTo(WitSqlType.DateOnly));
        Assert.That(WitTypeConverter.ParseSqlTypeName("TIME"), Is.EqualTo(WitSqlType.TimeOnly));
        Assert.That(WitTypeConverter.ParseSqlTypeName("DATETIME"), Is.EqualTo(WitSqlType.DateTime));
        Assert.That(WitTypeConverter.ParseSqlTypeName("TIMESTAMP"), Is.EqualTo(WitSqlType.DateTime));
        Assert.That(WitTypeConverter.ParseSqlTypeName("DATETIMEOFFSET"), Is.EqualTo(WitSqlType.DateTimeOffset));
        Assert.That(WitTypeConverter.ParseSqlTypeName("INTERVAL"), Is.EqualTo(WitSqlType.TimeSpan));
    }

    #endregion
}
