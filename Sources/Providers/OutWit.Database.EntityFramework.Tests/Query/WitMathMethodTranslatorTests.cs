using System.Reflection;

namespace OutWit.Database.EntityFramework.Tests.Query;

/// <summary>
/// Unit tests for WitMathMethodTranslator.
/// </summary>
[TestFixture]
public class WitMathMethodTranslatorTests
{
    #region Abs Tests

    [Test]
    [TestCase(typeof(decimal))]
    [TestCase(typeof(double))]
    [TestCase(typeof(float))]
    [TestCase(typeof(int))]
    [TestCase(typeof(long))]
    [TestCase(typeof(short))]
    [TestCase(typeof(sbyte))]
    public void MathAbsMethodExistsForTypeTest(Type paramType)
    {
        var method = typeof(Math).GetMethod(nameof(Math.Abs), [paramType]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Abs"));
    }

    #endregion

    #region Rounding Tests

    [Test]
    [TestCase(typeof(decimal))]
    [TestCase(typeof(double))]
    public void MathCeilingMethodExistsForTypeTest(Type paramType)
    {
        var method = typeof(Math).GetMethod(nameof(Math.Ceiling), [paramType]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Ceiling"));
    }

    [Test]
    [TestCase(typeof(decimal))]
    [TestCase(typeof(double))]
    public void MathFloorMethodExistsForTypeTest(Type paramType)
    {
        var method = typeof(Math).GetMethod(nameof(Math.Floor), [paramType]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Floor"));
    }

    [Test]
    [TestCase(typeof(decimal))]
    [TestCase(typeof(double))]
    public void MathRoundMethodExistsForTypeTest(Type paramType)
    {
        var method = typeof(Math).GetMethod(nameof(Math.Round), [paramType]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Round"));
    }

    [Test]
    [TestCase(typeof(decimal))]
    [TestCase(typeof(double))]
    public void MathTruncateMethodExistsForTypeTest(Type paramType)
    {
        var method = typeof(Math).GetMethod(nameof(Math.Truncate), [paramType]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Truncate"));
    }

    #endregion

    #region Power/Root Tests

    [Test]
    public void MathPowMethodExistsTest()
    {
        var method = typeof(Math).GetMethod(nameof(Math.Pow), [typeof(double), typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Pow"));
    }

    [Test]
    public void MathSqrtMethodExistsTest()
    {
        var method = typeof(Math).GetMethod(nameof(Math.Sqrt), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Sqrt"));
    }

    #endregion

    #region Logarithm Tests

    [Test]
    public void MathLogMethodExistsTest()
    {
        var method = typeof(Math).GetMethod(nameof(Math.Log), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Log"));
    }

    [Test]
    public void MathLog10MethodExistsTest()
    {
        var method = typeof(Math).GetMethod(nameof(Math.Log10), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Log10"));
    }

    [Test]
    public void MathExpMethodExistsTest()
    {
        var method = typeof(Math).GetMethod(nameof(Math.Exp), [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Exp"));
    }

    #endregion

    #region Trigonometric Tests

    [Test]
    [TestCase(nameof(Math.Sin))]
    [TestCase(nameof(Math.Cos))]
    [TestCase(nameof(Math.Tan))]
    [TestCase(nameof(Math.Asin))]
    [TestCase(nameof(Math.Acos))]
    [TestCase(nameof(Math.Atan))]
    public void MathTrigMethodExistsTest(string methodName)
    {
        var method = typeof(Math).GetMethod(methodName, [typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo(methodName));
    }

    [Test]
    public void MathAtan2MethodExistsTest()
    {
        var method = typeof(Math).GetMethod(nameof(Math.Atan2), [typeof(double), typeof(double)]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Atan2"));
    }

    #endregion

    #region Min/Max Tests

    [Test]
    [TestCase(typeof(int))]
    [TestCase(typeof(long))]
    [TestCase(typeof(double))]
    [TestCase(typeof(decimal))]
    public void MathMaxMethodExistsForTypeTest(Type paramType)
    {
        var method = typeof(Math).GetMethod(nameof(Math.Max), [paramType, paramType]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Max"));
    }

    [Test]
    [TestCase(typeof(int))]
    [TestCase(typeof(long))]
    [TestCase(typeof(double))]
    [TestCase(typeof(decimal))]
    public void MathMinMethodExistsForTypeTest(Type paramType)
    {
        var method = typeof(Math).GetMethod(nameof(Math.Min), [paramType, paramType]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Min"));
    }

    #endregion

    #region Sign Tests

    [Test]
    [TestCase(typeof(int))]
    [TestCase(typeof(long))]
    [TestCase(typeof(double))]
    [TestCase(typeof(decimal))]
    public void MathSignMethodExistsForTypeTest(Type paramType)
    {
        var method = typeof(Math).GetMethod(nameof(Math.Sign), [paramType]);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Sign"));
    }

    #endregion
}
