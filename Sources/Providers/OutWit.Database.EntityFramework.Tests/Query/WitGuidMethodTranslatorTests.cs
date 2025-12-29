using System.Reflection;

namespace OutWit.Database.EntityFramework.Tests.Query;

/// <summary>
/// Unit tests for WitGuidMethodTranslator.
/// </summary>
[TestFixture]
public class WitGuidMethodTranslatorTests
{
    #region NewGuid Tests

    [Test]
    public void GuidNewGuidMethodExistsTest()
    {
        var method = typeof(Guid).GetMethod(nameof(Guid.NewGuid), Type.EmptyTypes);
        
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("NewGuid"));
        Assert.That(method.IsStatic, Is.True);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(Guid)));
    }

    #endregion
}
