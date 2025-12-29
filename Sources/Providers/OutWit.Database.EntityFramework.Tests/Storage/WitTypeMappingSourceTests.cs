using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OutWit.Database.EntityFramework.Storage;

namespace OutWit.Database.EntityFramework.Tests.Storage;

/// <summary>
/// Tests for WitTypeMappingSource.
/// </summary>
[TestFixture]
public class WitTypeMappingSourceTests
{
    #region Fields

    private WitTypeMappingSource m_typeMappingSource = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        // Create the type mapping source with minimal dependencies
        // The dependencies are not strictly needed for basic type lookup
        var dependencies = new TypeMappingSourceDependencies(
            new ValueConverterSelector(new ValueConverterSelectorDependencies()),
            new JsonValueReaderWriterSource(new JsonValueReaderWriterSourceDependencies()),
            []);
            
        var relationalDependencies = new RelationalTypeMappingSourceDependencies([]);
        
        m_typeMappingSource = new WitTypeMappingSource(dependencies, relationalDependencies);
    }

    #endregion

    #region Integer Type Mapping Tests

    [Test]
    public void FindMappingSByteReturnsTinyIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(sbyte));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("TINYINT"));
    }

    [Test]
    public void FindMappingByteReturnsUtinyIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(byte));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("UTINYINT"));
    }

    [Test]
    public void FindMappingShortReturnsSmallIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(short));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("SMALLINT"));
    }

    [Test]
    public void FindMappingUshortReturnsUsmallIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(ushort));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("USMALLINT"));
    }

    [Test]
    public void FindMappingIntReturnsIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(int));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("INT"));
    }

    [Test]
    public void FindMappingUintReturnsUintTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(uint));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("UINT"));
    }

    [Test]
    public void FindMappingLongReturnsBigIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(long));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("BIGINT"));
    }

    [Test]
    public void FindMappingUlongReturnsUbigIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(ulong));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("UBIGINT"));
    }

    #endregion

    #region Floating-Point Type Mapping Tests

    [Test]
    public void FindMappingFloatReturnsFloatTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(float));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("FLOAT"));
    }

    [Test]
    public void FindMappingDoubleReturnsDoubleTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(double));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("DOUBLE"));
    }

    [Test]
    public void FindMappingDecimalReturnsDecimalTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(decimal));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("DECIMAL"));
    }

    #endregion

    #region Boolean Type Mapping Tests

    [Test]
    public void FindMappingBoolReturnsBooleanTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(bool));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("BOOLEAN"));
    }

    #endregion

    #region Date/Time Type Mapping Tests

    [Test]
    public void FindMappingDateOnlyReturnsDateTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(DateOnly));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("DATE"));
    }

    [Test]
    public void FindMappingTimeOnlyReturnsTimeTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(TimeOnly));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("TIME"));
    }

    [Test]
    public void FindMappingDateTimeReturnsDateTimeTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(DateTime));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("DATETIME"));
    }

    [Test]
    public void FindMappingDateTimeOffsetReturnsDateTimeOffsetTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(DateTimeOffset));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("DATETIMEOFFSET"));
    }

    [Test]
    public void FindMappingTimeSpanReturnsIntervalTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(TimeSpan));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("INTERVAL"));
    }

    #endregion

    #region String Type Mapping Tests

    [Test]
    public void FindMappingStringReturnsTextTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(string));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("TEXT"));
    }

    #endregion

    #region Binary Type Mapping Tests

    [Test]
    public void FindMappingByteArrayReturnsBlobTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(byte[]));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("BLOB"));
    }

    #endregion

    #region GUID Type Mapping Tests

    [Test]
    public void FindMappingGuidReturnsGuidTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(Guid));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("GUID"));
    }

    #endregion

    #region Nullable Type Mapping Tests

    [Test]
    public void FindMappingNullableIntReturnsIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(int?));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("INT"));
    }

    [Test]
    public void FindMappingNullableBoolReturnsBooleanTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(bool?));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("BOOLEAN"));
    }

    [Test]
    public void FindMappingNullableDateTimeReturnsDateTimeTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(DateTime?));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("DATETIME"));
    }

    [Test]
    public void FindMappingNullableGuidReturnsGuidTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(Guid?));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("GUID"));
    }

    #endregion

    #region Enum Type Mapping Tests

    [Test]
    public void FindMappingEnumReturnsIntTest()
    {
        var mapping = m_typeMappingSource.FindMapping(typeof(DayOfWeek));

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.StoreType, Is.EqualTo("INT"));
    }

    #endregion

    #region Store Type Name Mapping Tests

    [Test]
    public void FindMappingByStoreTypeIntReturnsIntMappingTest()
    {
        var mapping = m_typeMappingSource.FindMapping("INT");

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.ClrType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void FindMappingByStoreTypeIntegerReturnsIntMappingTest()
    {
        var mapping = m_typeMappingSource.FindMapping("INTEGER");

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.ClrType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void FindMappingByStoreTypeTextReturnsStringMappingTest()
    {
        var mapping = m_typeMappingSource.FindMapping("TEXT");

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.ClrType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void FindMappingByStoreTypeVarcharReturnsStringMappingTest()
    {
        var mapping = m_typeMappingSource.FindMapping("VARCHAR");

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.ClrType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void FindMappingByStoreTypeBlobReturnsByteArrayMappingTest()
    {
        var mapping = m_typeMappingSource.FindMapping("BLOB");

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.ClrType, Is.EqualTo(typeof(byte[])));
    }

    [Test]
    public void FindMappingByStoreTypeUuidReturnsGuidMappingTest()
    {
        var mapping = m_typeMappingSource.FindMapping("UUID");

        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.ClrType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void FindMappingByStoreTypeCaseInsensitiveTest()
    {
        var mapping1 = m_typeMappingSource.FindMapping("int");
        var mapping2 = m_typeMappingSource.FindMapping("INT");
        var mapping3 = m_typeMappingSource.FindMapping("Int");

        Assert.That(mapping1, Is.Not.Null);
        Assert.That(mapping2, Is.Not.Null);
        Assert.That(mapping3, Is.Not.Null);
        Assert.That(mapping1!.ClrType, Is.EqualTo(mapping2!.ClrType));
        Assert.That(mapping2.ClrType, Is.EqualTo(mapping3!.ClrType));
    }

    #endregion
}
