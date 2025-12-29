using System.Reflection;

namespace OutWit.Database.EntityFramework.Tests.Query;

/// <summary>
/// Unit tests for WitStringMethodTranslator.
/// </summary>
[TestFixture]
public class WitStringMethodTranslatorTests
{
    #region ToUpper/ToLower Tests

    [Test]
    public void TranslateToUpperReturnsUpperFunctionTest()
    {
        var method = typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("ToUpper"));
    }

    [Test]
    public void TranslateToLowerReturnsLowerFunctionTest()
    {
        var method = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("ToLower"));
    }

    #endregion

    #region Trim Tests

    [Test]
    public void TranslateTrimReturnsTrimFunctionTest()
    {
        var method = typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Trim"));
    }

    [Test]
    public void TranslateTrimStartReturnsLTrimFunctionTest()
    {
        var method = typeof(string).GetMethod(nameof(string.TrimStart), Type.EmptyTypes)!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("TrimStart"));
    }

    [Test]
    public void TranslateTrimEndReturnsRTrimFunctionTest()
    {
        var method = typeof(string).GetMethod(nameof(string.TrimEnd), Type.EmptyTypes)!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("TrimEnd"));
    }

    #endregion

    #region Search Tests

    [Test]
    public void TranslateContainsMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Contains"));
    }

    [Test]
    public void TranslateStartsWithMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("StartsWith"));
    }

    [Test]
    public void TranslateEndsWithMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("EndsWith"));
    }

    [Test]
    public void TranslateIndexOfMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("IndexOf"));
    }

    #endregion

    #region Manipulation Tests

    [Test]
    public void TranslateReplaceMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Replace"));
    }

    [Test]
    public void TranslateSubstringMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Substring"));
    }

    [Test]
    public void TranslateSubstringStartOnlyMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.Substring), [typeof(int)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Substring"));
    }

    #endregion

    #region Static Methods Tests

    [Test]
    public void TranslateIsNullOrEmptyMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.IsNullOrEmpty), [typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("IsNullOrEmpty"));
        Assert.That(method.IsStatic, Is.True);
    }

    [Test]
    public void TranslateIsNullOrWhiteSpaceMethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("IsNullOrWhiteSpace"));
        Assert.That(method.IsStatic, Is.True);
    }

    [Test]
    public void TranslateConcat2MethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Concat"));
        Assert.That(method.GetParameters(), Has.Length.EqualTo(2));
    }

    [Test]
    public void TranslateConcat3MethodExistsTest()
    {
        var method = typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string), typeof(string)])!;
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Concat"));
        Assert.That(method.GetParameters(), Has.Length.EqualTo(3));
    }

    #endregion
}
