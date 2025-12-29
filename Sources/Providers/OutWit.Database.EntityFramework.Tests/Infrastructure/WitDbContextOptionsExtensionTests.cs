using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Infrastructure;

namespace OutWit.Database.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Tests for WitDbContextOptionsExtension.
/// </summary>
[TestFixture]
public class WitDbContextOptionsExtensionTests
{
    #region Constructor Tests

    [Test]
    public void DefaultConstructorCreatesEmptyExtensionTest()
    {
        var extension = new WitDbContextOptionsExtension();

        Assert.That(extension.ConnectionString, Is.Null);
        Assert.That(extension.Connection, Is.Null);
        Assert.That(extension.InMemory, Is.False);
    }

    #endregion

    #region WithConnectionString Tests

    [Test]
    public void WithConnectionStringReturnsNewInstanceTest()
    {
        var original = new WitDbContextOptionsExtension();
        var modified = original.WithConnectionString("Data Source=test.db");

        Assert.That(modified, Is.Not.SameAs(original));
        Assert.That(original.ConnectionString, Is.Null);
        Assert.That(modified.ConnectionString, Is.EqualTo("Data Source=test.db"));
    }

    [Test]
    public void WithConnectionStringPreservesOtherPropertiesTest()
    {
        var original = new WitDbContextOptionsExtension()
            .WithInMemory(true);

        var modified = original.WithConnectionString("Data Source=test.db");

        Assert.That(modified.ConnectionString, Is.EqualTo("Data Source=test.db"));
        Assert.That(modified.InMemory, Is.True);
    }

    #endregion

    #region WithInMemory Tests

    [Test]
    public void WithInMemoryReturnsNewInstanceTest()
    {
        var original = new WitDbContextOptionsExtension();
        var modified = original.WithInMemory(true);

        Assert.That(modified, Is.Not.SameAs(original));
        Assert.That(original.InMemory, Is.False);
        Assert.That(modified.InMemory, Is.True);
    }

    [Test]
    public void WithInMemoryDefaultsToTrueTest()
    {
        var extension = new WitDbContextOptionsExtension().WithInMemory();

        Assert.That(extension.InMemory, Is.True);
    }

    #endregion

    #region Info Tests

    [Test]
    public void InfoReturnsNonNullTest()
    {
        var extension = new WitDbContextOptionsExtension();

        Assert.That(extension.Info, Is.Not.Null);
    }

    [Test]
    public void InfoIsDatabaseProviderReturnsTrueTest()
    {
        var extension = new WitDbContextOptionsExtension();

        Assert.That(extension.Info.IsDatabaseProvider, Is.True);
    }

    [Test]
    public void InfoReturnsSameInstanceTest()
    {
        var extension = new WitDbContextOptionsExtension();
        var info1 = extension.Info;
        var info2 = extension.Info;

        Assert.That(info1, Is.SameAs(info2));
    }

    [Test]
    public void InfoLogFragmentContainsWitDatabaseTest()
    {
        var extension = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=test.db");

        Assert.That(extension.Info.LogFragment, Does.Contain("WitDatabase"));
        Assert.That(extension.Info.LogFragment, Does.Contain("test.db"));
    }

    [Test]
    public void InfoLogFragmentForInMemoryTest()
    {
        var extension = new WitDbContextOptionsExtension()
            .WithInMemory(true);

        Assert.That(extension.Info.LogFragment, Does.Contain("in-memory"));
    }

    #endregion

    #region Validate Tests

    [Test]
    public void ValidateThrowsWhenNoConnectionConfiguredTest()
    {
        var extension = new WitDbContextOptionsExtension();
        var options = new DbContextOptionsBuilder().Options;

        Assert.Throws<InvalidOperationException>(() => extension.Validate(options));
    }

    [Test]
    public void ValidateSucceedsWithConnectionStringTest()
    {
        var extension = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=:memory:");
        var options = new DbContextOptionsBuilder().Options;

        Assert.DoesNotThrow(() => extension.Validate(options));
    }

    [Test]
    public void ValidateSucceedsWithInMemoryModeTest()
    {
        var extension = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=:memory:")
            .WithInMemory(true);
        var options = new DbContextOptionsBuilder().Options;

        Assert.DoesNotThrow(() => extension.Validate(options));
    }

    #endregion

    #region GetServiceProviderHashCode Tests

    [Test]
    public void GetServiceProviderHashCodeIsDeterministicTest()
    {
        var extension = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=test.db");

        var hash1 = extension.Info.GetServiceProviderHashCode();
        var hash2 = extension.Info.GetServiceProviderHashCode();

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetServiceProviderHashCodeDiffersForDifferentInMemorySettingsTest()
    {
        var extension1 = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=test.db")
            .WithInMemory(false);
        var extension2 = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=test.db")
            .WithInMemory(true);

        Assert.That(extension1.Info.GetServiceProviderHashCode(),
            Is.Not.EqualTo(extension2.Info.GetServiceProviderHashCode()));
    }

    #endregion

    #region ShouldUseSameServiceProvider Tests

    [Test]
    public void ShouldUseSameServiceProviderReturnsTrueForSameConfigTest()
    {
        var extension1 = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=test.db");
        var extension2 = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=test.db");

        Assert.That(extension1.Info.ShouldUseSameServiceProvider(extension2.Info), Is.True);
    }

    [Test]
    public void ShouldUseSameServiceProviderReturnsFalseForDifferentInMemorySettingsTest()
    {
        var extension1 = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=test.db")
            .WithInMemory(false);
        var extension2 = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=test.db")
            .WithInMemory(true);

        Assert.That(extension1.Info.ShouldUseSameServiceProvider(extension2.Info), Is.False);
    }

    #endregion

    #region PopulateDebugInfo Tests

    [Test]
    public void PopulateDebugInfoAddsInMemoryTest()
    {
        var extension = new WitDbContextOptionsExtension()
            .WithConnectionString("Data Source=:memory:")
            .WithInMemory(true);

        var debugInfo = new Dictionary<string, string>();
        extension.Info.PopulateDebugInfo(debugInfo);

        Assert.That(debugInfo, Does.ContainKey("WitDb:InMemory"));
        Assert.That(debugInfo["WitDb:InMemory"], Is.EqualTo("True"));
    }

    #endregion
}
