using System.Reflection;

namespace OutWit.Database.EntityFramework.Tests.Query;

/// <summary>
/// Unit tests for WitDateTimeMethodTranslator.
/// </summary>
[TestFixture]
public class WitDateTimeMethodTranslatorTests
{
    #region Add Methods Tests

    [Test]
    public void DateTimeAddDaysMethodExistsTest()
    {
        var method = typeof(DateTime).GetMethod(nameof(DateTime.AddDays), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("AddDays"));
    }

    [Test]
    public void DateTimeAddMonthsMethodExistsTest()
    {
        var method = typeof(DateTime).GetMethod(nameof(DateTime.AddMonths), [typeof(int)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("AddMonths"));
    }

    [Test]
    public void DateTimeAddYearsMethodExistsTest()
    {
        var method = typeof(DateTime).GetMethod(nameof(DateTime.AddYears), [typeof(int)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("AddYears"));
    }

    [Test]
    public void DateTimeAddHoursMethodExistsTest()
    {
        var method = typeof(DateTime).GetMethod(nameof(DateTime.AddHours), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("AddHours"));
    }

    [Test]
    public void DateTimeAddMinutesMethodExistsTest()
    {
        var method = typeof(DateTime).GetMethod(nameof(DateTime.AddMinutes), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("AddMinutes"));
    }

    [Test]
    public void DateTimeAddSecondsMethodExistsTest()
    {
        var method = typeof(DateTime).GetMethod(nameof(DateTime.AddSeconds), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("AddSeconds"));
    }

    [Test]
    public void DateTimeAddMillisecondsMethodExistsTest()
    {
        var method = typeof(DateTime).GetMethod(nameof(DateTime.AddMilliseconds), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("AddMilliseconds"));
    }

    #endregion
}
