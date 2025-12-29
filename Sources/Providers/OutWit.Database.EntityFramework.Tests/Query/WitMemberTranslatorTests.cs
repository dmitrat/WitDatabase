using System.Reflection;

namespace OutWit.Database.EntityFramework.Tests.Query;

/// <summary>
/// Unit tests for WitMemberTranslator.
/// </summary>
[TestFixture]
public class WitMemberTranslatorTests
{
    #region String Member Tests

    [Test]
    public void StringLengthPropertyExistsTest()
    {
        var property = typeof(string).GetProperty(nameof(string.Length));
        
        Assert.That(property, Is.Not.Null);
        Assert.That(property.Name, Is.EqualTo("Length"));
        Assert.That(property.PropertyType, Is.EqualTo(typeof(int)));
    }

    #endregion

    #region DateTime Instance Member Tests

    [Test]
    [TestCase(nameof(DateTime.Year), typeof(int))]
    [TestCase(nameof(DateTime.Month), typeof(int))]
    [TestCase(nameof(DateTime.Day), typeof(int))]
    [TestCase(nameof(DateTime.Hour), typeof(int))]
    [TestCase(nameof(DateTime.Minute), typeof(int))]
    [TestCase(nameof(DateTime.Second), typeof(int))]
    [TestCase(nameof(DateTime.Millisecond), typeof(int))]
    [TestCase(nameof(DateTime.DayOfWeek), typeof(DayOfWeek))]
    [TestCase(nameof(DateTime.DayOfYear), typeof(int))]
    [TestCase(nameof(DateTime.Date), typeof(DateTime))]
    [TestCase(nameof(DateTime.TimeOfDay), typeof(TimeSpan))]
    public void DateTimeInstancePropertyExistsTest(string propertyName, Type expectedType)
    {
        var property = typeof(DateTime).GetProperty(propertyName);
        
        Assert.That(property, Is.Not.Null);
        Assert.That(property.Name, Is.EqualTo(propertyName));
        Assert.That(property.PropertyType, Is.EqualTo(expectedType));
    }

    #endregion

    #region DateTime Static Member Tests

    [Test]
    [TestCase(nameof(DateTime.Now))]
    [TestCase(nameof(DateTime.UtcNow))]
    [TestCase(nameof(DateTime.Today))]
    public void DateTimeStaticPropertyExistsTest(string propertyName)
    {
        var property = typeof(DateTime).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        
        Assert.That(property, Is.Not.Null);
        Assert.That(property.Name, Is.EqualTo(propertyName));
    }

    #endregion

    #region DateOnly Member Tests

    [Test]
    [TestCase(nameof(DateOnly.Year), typeof(int))]
    [TestCase(nameof(DateOnly.Month), typeof(int))]
    [TestCase(nameof(DateOnly.Day), typeof(int))]
    [TestCase(nameof(DateOnly.DayOfWeek), typeof(DayOfWeek))]
    [TestCase(nameof(DateOnly.DayOfYear), typeof(int))]
    public void DateOnlyPropertyExistsTest(string propertyName, Type expectedType)
    {
        var property = typeof(DateOnly).GetProperty(propertyName);
        
        Assert.That(property, Is.Not.Null);
        Assert.That(property.Name, Is.EqualTo(propertyName));
        Assert.That(property.PropertyType, Is.EqualTo(expectedType));
    }

    #endregion

    #region TimeOnly Member Tests

    [Test]
    [TestCase(nameof(TimeOnly.Hour), typeof(int))]
    [TestCase(nameof(TimeOnly.Minute), typeof(int))]
    [TestCase(nameof(TimeOnly.Second), typeof(int))]
    [TestCase(nameof(TimeOnly.Millisecond), typeof(int))]
    public void TimeOnlyPropertyExistsTest(string propertyName, Type expectedType)
    {
        var property = typeof(TimeOnly).GetProperty(propertyName);
        
        Assert.That(property, Is.Not.Null);
        Assert.That(property.Name, Is.EqualTo(propertyName));
        Assert.That(property.PropertyType, Is.EqualTo(expectedType));
    }

    #endregion

    #region TimeSpan Member Tests

    [Test]
    [TestCase(nameof(TimeSpan.Hours), typeof(int))]
    [TestCase(nameof(TimeSpan.Minutes), typeof(int))]
    [TestCase(nameof(TimeSpan.Seconds), typeof(int))]
    [TestCase(nameof(TimeSpan.Milliseconds), typeof(int))]
    [TestCase(nameof(TimeSpan.TotalDays), typeof(double))]
    [TestCase(nameof(TimeSpan.TotalHours), typeof(double))]
    [TestCase(nameof(TimeSpan.TotalMinutes), typeof(double))]
    [TestCase(nameof(TimeSpan.TotalSeconds), typeof(double))]
    public void TimeSpanPropertyExistsTest(string propertyName, Type expectedType)
    {
        var property = typeof(TimeSpan).GetProperty(propertyName);
        
        Assert.That(property, Is.Not.Null);
        Assert.That(property.Name, Is.EqualTo(propertyName));
        Assert.That(property.PropertyType, Is.EqualTo(expectedType));
    }

    #endregion
}
