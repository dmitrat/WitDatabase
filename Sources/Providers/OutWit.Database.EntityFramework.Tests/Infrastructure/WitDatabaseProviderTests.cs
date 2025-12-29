using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Infrastructure;

namespace OutWit.Database.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Tests for WitDatabaseProvider.
/// </summary>
[TestFixture]
public class WitDatabaseProviderTests
{
    #region Name Tests

    [Test]
    public void NameReturnsCorrectProviderNameTest()
    {
        var provider = new WitDatabaseProvider();

        Assert.That(provider.Name, Is.EqualTo(WitDatabaseProvider.PROVIDER_NAME));
        Assert.That(provider.Name, Is.EqualTo("OutWit.Database.EntityFramework"));
    }

    #endregion

    #region IsConfigured Tests

    [Test]
    public void IsConfiguredReturnsFalseWhenNotConfiguredTest()
    {
        var provider = new WitDatabaseProvider();
        var options = new DbContextOptionsBuilder().Options;

        Assert.That(provider.IsConfigured(options), Is.False);
    }

    [Test]
    public void IsConfiguredReturnsTrueWhenConfiguredTest()
    {
        var provider = new WitDatabaseProvider();
        var options = new DbContextOptionsBuilder()
            .UseWitDb("Data Source=:memory:")
            .Options;

        Assert.That(provider.IsConfigured(options), Is.True);
    }

    [Test]
    public void IsConfiguredReturnsTrueForInMemoryTest()
    {
        var provider = new WitDatabaseProvider();
        var options = new DbContextOptionsBuilder()
            .UseWitDbInMemory()
            .Options;

        Assert.That(provider.IsConfigured(options), Is.True);
    }

    #endregion
}
