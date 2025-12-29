using Microsoft.EntityFrameworkCore;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Infrastructure;

namespace OutWit.Database.EntityFramework.Tests.Extensions;

/// <summary>
/// Tests for WitDbContextOptionsBuilderExtensions.
/// </summary>
[TestFixture]
public class WitDbContextOptionsBuilderExtensionsTests
{
    #region UseWitDb with Connection String Tests

    [Test]
    public void UseWitDbWithConnectionStringConfiguresExtensionTest()
    {
        var builder = new DbContextOptionsBuilder();

        builder.UseWitDb("Data Source=test.db");

        var extension = builder.Options.FindExtension<WitDbContextOptionsExtension>();
        Assert.That(extension, Is.Not.Null);
        Assert.That(extension!.ConnectionString, Is.EqualTo("Data Source=test.db"));
    }

    [Test]
    public void UseWitDbWithConnectionStringReturnsSameBuilderTest()
    {
        var builder = new DbContextOptionsBuilder();

        var result = builder.UseWitDb("Data Source=test.db");

        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void UseWitDbWithNullConnectionStringThrowsTest()
    {
        var builder = new DbContextOptionsBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.UseWitDb((string)null!));
    }

    [Test]
    public void UseWitDbWithEmptyConnectionStringThrowsTest()
    {
        var builder = new DbContextOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.UseWitDb(string.Empty));
    }

    #endregion

    #region UseWitDb with Connection Tests

    [Test]
    public void UseWitDbWithConnectionConfiguresExtensionTest()
    {
        var builder = new DbContextOptionsBuilder();
        using var connection = new WitDbConnection("Data Source=:memory:");

        builder.UseWitDb(connection);

        var extension = builder.Options.FindExtension<WitDbContextOptionsExtension>();
        Assert.That(extension, Is.Not.Null);
        Assert.That(extension!.Connection, Is.SameAs(connection));
    }

    [Test]
    public void UseWitDbWithConnectionReturnsSameBuilderTest()
    {
        var builder = new DbContextOptionsBuilder();
        using var connection = new WitDbConnection("Data Source=:memory:");

        var result = builder.UseWitDb(connection);

        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void UseWitDbWithNullConnectionThrowsTest()
    {
        var builder = new DbContextOptionsBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.UseWitDb((WitDbConnection)null!));
    }

    #endregion

    #region UseWitDbInMemory Tests

    [Test]
    public void UseWitDbInMemoryConfiguresExtensionTest()
    {
        var builder = new DbContextOptionsBuilder();

        builder.UseWitDbInMemory();

        var extension = builder.Options.FindExtension<WitDbContextOptionsExtension>();
        Assert.That(extension, Is.Not.Null);
        Assert.That(extension!.InMemory, Is.True);
        Assert.That(extension.ConnectionString, Is.EqualTo("Data Source=:memory:"));
    }

    [Test]
    public void UseWitDbInMemoryReturnsSameBuilderTest()
    {
        var builder = new DbContextOptionsBuilder();

        var result = builder.UseWitDbInMemory();

        Assert.That(result, Is.SameAs(builder));
    }

    #endregion

    #region Generic DbContextOptionsBuilder Tests

    [Test]
    public void GenericUseWitDbConfiguresExtensionTest()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>();

        builder.UseWitDb("Data Source=test.db");

        var extension = builder.Options.FindExtension<WitDbContextOptionsExtension>();
        Assert.That(extension, Is.Not.Null);
        Assert.That(extension!.ConnectionString, Is.EqualTo("Data Source=test.db"));
    }

    [Test]
    public void GenericUseWitDbInMemoryConfiguresExtensionTest()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>();

        builder.UseWitDbInMemory();

        var extension = builder.Options.FindExtension<WitDbContextOptionsExtension>();
        Assert.That(extension, Is.Not.Null);
        Assert.That(extension!.InMemory, Is.True);
    }

    #endregion

    #region Options Action Tests

    [Test]
    public void UseWitDbWithOptionsActionInvokesActionTest()
    {
        var builder = new DbContextOptionsBuilder();
        var actionInvoked = false;

        builder.UseWitDb("Data Source=test.db", options =>
        {
            actionInvoked = true;
        });

        Assert.That(actionInvoked, Is.True);
    }

    [Test]
    public void UseWitDbInMemoryWithOptionsActionInvokesActionTest()
    {
        var builder = new DbContextOptionsBuilder();
        var actionInvoked = false;

        builder.UseWitDbInMemory(options =>
        {
            actionInvoked = true;
        });

        Assert.That(actionInvoked, Is.True);
    }

    #endregion

    #region Multiple UseWitDb Calls Tests

    [Test]
    public void MultipleUseWitDbCallsUpdatesExtensionTest()
    {
        var builder = new DbContextOptionsBuilder();

        builder.UseWitDb("Data Source=first.db");
        builder.UseWitDb("Data Source=second.db");

        var extension = builder.Options.FindExtension<WitDbContextOptionsExtension>();
        Assert.That(extension, Is.Not.Null);
        Assert.That(extension!.ConnectionString, Is.EqualTo("Data Source=second.db"));
    }

    #endregion

    #region Test Helpers

    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    }

    #endregion
}
