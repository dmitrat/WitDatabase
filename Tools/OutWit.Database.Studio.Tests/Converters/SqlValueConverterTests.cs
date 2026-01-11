using System.Globalization;
using NUnit.Framework;
using OutWit.Database.Studio.Converters;

namespace OutWit.Database.Studio.Tests.Converters;

/// <summary>
/// Tests for SqlValueConverter.
/// </summary>
[TestFixture]
public class SqlValueConverterTests
{
    #region Fields

    private SqlValueConverter m_converter = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_converter = new SqlValueConverter();
    }

    #endregion

    #region Null/DBNull Tests

    [Test]
    public void ConvertNullReturnsNullDisplayTextTest()
    {
        var result = m_converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo(SqlValueConverter.NULL_DISPLAY_TEXT));
    }

    [Test]
    public void ConvertDbNullReturnsNullDisplayTextTest()
    {
        var result = m_converter.Convert(DBNull.Value, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo(SqlValueConverter.NULL_DISPLAY_TEXT));
    }

    #endregion

    #region Byte Array Tests

    [Test]
    public void ConvertEmptyByteArrayReturnsEmptyIndicatorTest()
    {
        var result = m_converter.Convert(Array.Empty<byte>(), typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("(empty)"));
    }

    [Test]
    public void ConvertSmallByteArrayReturnsHexStringTest()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var result = m_converter.Convert(bytes, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("0xDEADBEEF"));
    }

    [Test]
    public void ConvertLargeByteArrayReturnsTruncatedHexWithSizeTest()
    {
        var bytes = new byte[32];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)i;

        var result = m_converter.Convert(bytes, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Does.StartWith("0x"));
        Assert.That(result, Does.EndWith("(32 bytes)"));
        Assert.That(result, Does.Contain("..."));
    }

    #endregion

    #region DateTime Tests - Return original values for DataGrid culture-aware formatting

    [Test]
    public void ConvertDateTimeReturnsOriginalValueTest()
    {
        var dateTime = new DateTime(2025, 6, 15, 14, 30, 45);

        var result = m_converter.Convert(dateTime, typeof(string), null, CultureInfo.InvariantCulture);

        // DateTime returned as-is - DataGrid handles culture-aware formatting
        Assert.That(result, Is.EqualTo(dateTime));
    }

    [Test]
    public void ConvertDateOnlyReturnsOriginalValueTest()
    {
        var date = new DateOnly(2025, 6, 15);

        var result = m_converter.Convert(date, typeof(string), null, CultureInfo.InvariantCulture);

        // DateOnly returned as-is - DataGrid handles culture-aware formatting
        Assert.That(result, Is.EqualTo(date));
    }

    [Test]
    public void ConvertTimeOnlyReturnsOriginalValueTest()
    {
        var time = new TimeOnly(14, 30, 45);

        var result = m_converter.Convert(time, typeof(string), null, CultureInfo.InvariantCulture);

        // TimeOnly returned as-is - DataGrid handles culture-aware formatting
        Assert.That(result, Is.EqualTo(time));
    }

    [Test]
    public void ConvertTimeSpanReturnsOriginalValueTest()
    {
        var timeSpan = new TimeSpan(2, 30, 45);

        var result = m_converter.Convert(timeSpan, typeof(string), null, CultureInfo.InvariantCulture);

        // TimeSpan returned as-is - DataGrid handles culture-aware formatting
        Assert.That(result, Is.EqualTo(timeSpan));
    }

    [Test]
    public void ConvertDateTimeOffsetReturnsOriginalValueTest()
    {
        var dto = new DateTimeOffset(2025, 6, 15, 14, 30, 45, TimeSpan.FromHours(2));

        var result = m_converter.Convert(dto, typeof(string), null, CultureInfo.InvariantCulture);

        // DateTimeOffset returned as-is - DataGrid handles culture-aware formatting
        Assert.That(result, Is.EqualTo(dto));
    }

    #endregion

    #region Boolean Tests

    [Test]
    public void ConvertTrueReturnsTrueStringTest()
    {
        var result = m_converter.Convert(true, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("true"));
    }

    [Test]
    public void ConvertFalseReturnsFalseStringTest()
    {
        var result = m_converter.Convert(false, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("false"));
    }

    #endregion

    #region Numeric Types Tests - Return original values

    [Test]
    public void ConvertStringReturnsOriginalValueTest()
    {
        var result = m_converter.Convert("Hello World", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("Hello World"));
    }

    [Test]
    public void ConvertIntegerReturnsOriginalValueTest()
    {
        var result = m_converter.Convert(42, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void ConvertLongReturnsOriginalValueTest()
    {
        var result = m_converter.Convert(123456789L, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo(123456789L));
    }

    [Test]
    public void ConvertDecimalReturnsOriginalValueTest()
    {
        var result = m_converter.Convert(123.45m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo(123.45m));
    }

    [Test]
    public void ConvertDoubleReturnsOriginalValueTest()
    {
        var result = m_converter.Convert(3.14159d, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo(3.14159d));
    }

    [Test]
    public void ConvertGuidReturnsOriginalValueTest()
    {
        var guid = Guid.NewGuid();

        var result = m_converter.Convert(guid, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo(guid));
    }

    #endregion

    #region ConvertBack Tests

    [Test]
    public void ConvertBackNullDisplayTextReturnsNullTest()
    {
        var result = m_converter.ConvertBack(SqlValueConverter.NULL_DISPLAY_TEXT, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConvertBackNonNullValueReturnsValueTest()
    {
        var result = m_converter.ConvertBack("Hello", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("Hello"));
    }

    #endregion
}
