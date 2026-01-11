using System.Globalization;
using NUnit.Framework;
using OutWit.Database.Studio.Converters;

namespace OutWit.Database.Studio.Tests.Converters;

/// <summary>
/// Tests for NullValueConverter.
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

    #region Convert Tests

    [Test]
    public void ConvertNullReturnsNullDisplayTextTest()
    {
        // Act
        var result = m_converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.That(result, Is.EqualTo(SqlValueConverter.NULL_DISPLAY_TEXT));
    }

    [Test]
    public void ConvertNonEmptyStringReturnsOriginalValueTest()
    {
        // Act
        var result = m_converter.Convert("Hello", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.That(result, Is.EqualTo("Hello"));
    }

    [Test]
    public void ConvertNumberReturnsStringRepresentationTest()
    {
        // Act
        var result = m_converter.Convert(42, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.That(result, Is.EqualTo(42));
    }

    #endregion

    #region ConvertBack Tests

    [Test]
    public void ConvertBackNullDisplayTextReturnsNullTest()
    {
        // Act
        var result = m_converter.ConvertBack(SqlValueConverter.NULL_DISPLAY_TEXT, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConvertBackNonNullValueReturnsValueTest()
    {
        // Act
        var result = m_converter.ConvertBack("Hello", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.That(result, Is.EqualTo("Hello"));
    }

    #endregion
}
