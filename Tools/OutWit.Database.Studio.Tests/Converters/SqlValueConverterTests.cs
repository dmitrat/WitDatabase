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

    /// <summary>
    /// The BLOB default is its SIZE now, not truncated hex (the Data section of the settings, 6.6).
    /// Sixteen bytes of hex in a narrow column tell a person nothing they can act on; the cell viewer
    /// shows the bytes when they want them, and Hex and Base64 are still one setting away.
    /// </summary>
    [Test]
    public void ConvertSmallByteArrayReturnsItsSizeByDefaultTest()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var result = m_converter.Convert(bytes, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("(4 bytes)"));
    }

    [Test]
    public void ConvertLargeByteArrayReturnsItsSizeByDefaultTest()
    {
        var bytes = new byte[32];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)i;

        var result = m_converter.Convert(bytes, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("(32 bytes)"));
    }

    #endregion

    #region Dates - written by Studio, not by the DataGrid

    /// <summary>
    /// <b>These four cases used to assert the opposite, and they were pinning a defect.</b> They said
    /// the value came back unchanged, with a comment explaining that the DataGrid would format it
    /// "culture-aware" - which it did: on a ru-RU machine a DATETIME was drawn as
    /// <c>15.06.2025 14:30:45</c> and a DECIMAL as <c>123,45</c>, and neither can be pasted into a
    /// statement. WS-65 is that the format of a value is Studio's decision and not the locale's, so
    /// the converter now renders it and the grid formats nothing.
    ///
    /// They passed for four releases because the suite only ever ran under en-US.
    /// </summary>
    [Test]
    public void ConvertDateTimeIsWrittenInIsoTest()
    {
        var dateTime = new DateTime(2025, 6, 15, 14, 30, 45);

        var result = m_converter.Convert(dateTime, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("2025-06-15 14:30:45"));
    }

    [Test]
    public void ConvertDateOnlyIsWrittenInIsoTest()
    {
        var date = new DateOnly(2025, 6, 15);

        var result = m_converter.Convert(date, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("2025-06-15"));
    }

    [Test]
    public void ConvertTimeOnlyIsWrittenInIsoTest()
    {
        var time = new TimeOnly(14, 30, 45);

        var result = m_converter.Convert(time, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("14:30:45"));
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
    public void ConvertDateTimeOffsetIsWrittenInIsoTest()
    {
        var dto = new DateTimeOffset(2025, 6, 15, 14, 30, 45, TimeSpan.FromHours(2));

        var result = m_converter.Convert(dto, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("2025-06-15 14:30:45 +02:00"));
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

    /// <summary>
    /// Same story as the dates: this used to assert that the decimal came back untouched, which on a
    /// machine with a comma meant the grid drew a value that will not paste into a statement.
    /// </summary>
    [Test]
    public void ConvertDecimalIsWrittenWithADotTest()
    {
        var result = m_converter.Convert(123.45m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("123.45"));
    }

    [Test]
    public void ConvertDoubleIsWrittenWithADotTest()
    {
        var result = m_converter.Convert(3.14159d, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("3.14159"));
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
