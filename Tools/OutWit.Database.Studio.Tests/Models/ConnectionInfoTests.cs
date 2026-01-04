using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Tests.Models;

/// <summary>
/// Tests for <see cref="ConnectionInfo"/> model.
/// </summary>
[TestFixture]
public class ConnectionInfoTests
{
    #region Connection String Tests

    [Test]
    public void BuildConnectionStringReturnsBasicStringTest()
    {
        var connectionInfo = new ConnectionInfo
        {
            FilePath = "test.witdb"
        };

        var connectionString = connectionInfo.BuildConnectionString();

        Assert.That(connectionString, Is.EqualTo("Data Source=test.witdb"));
    }

    [Test]
    public void BuildConnectionStringIncludesReadOnlyModeTest()
    {
        var connectionInfo = new ConnectionInfo
        {
            FilePath = "test.witdb",
            IsReadOnly = true
        };

        var connectionString = connectionInfo.BuildConnectionString();

        Assert.That(connectionString, Does.Contain("Mode=ReadOnly"));
    }

    [Test]
    public void BuildConnectionStringIncludesEncryptionTest()
    {
        var connectionInfo = new ConnectionInfo
        {
            FilePath = "test.witdb",
            IsEncrypted = true,
            Password = "secret"
        };

        var connectionString = connectionInfo.BuildConnectionString();

        Assert.That(connectionString, Does.Contain("Encryption=aes-gcm"));
        Assert.That(connectionString, Does.Contain("Password=secret"));
    }

    [Test]
    public void BuildConnectionStringIncludesStorageEngineTest()
    {
        var connectionInfo = new ConnectionInfo
        {
            FilePath = "test.witdb",
            StorageEngine = "lsm"
        };

        var connectionString = connectionInfo.BuildConnectionString();

        Assert.That(connectionString, Does.Contain("Store=lsm"));
    }

    [Test]
    public void BuildConnectionStringDoesNotIncludeDefaultStorageEngineTest()
    {
        var connectionInfo = new ConnectionInfo
        {
            FilePath = "test.witdb",
            StorageEngine = "btree"
        };

        var connectionString = connectionInfo.BuildConnectionString();

        Assert.That(connectionString, Does.Not.Contain("Store="));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void CloneCreatesExactCopyTest()
    {
        var original = new ConnectionInfo
        {
            FilePath = "test.witdb",
            IsEncrypted = true,
            Password = "secret",
            IsReadOnly = true,
            StorageEngine = "lsm",
            DisplayName = "Test DB"
        };

        var clone = original.Clone();

        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone.FilePath, Is.EqualTo(original.FilePath));
        Assert.That(clone.IsEncrypted, Is.EqualTo(original.IsEncrypted));
        Assert.That(clone.Password, Is.EqualTo(original.Password));
        Assert.That(clone.IsReadOnly, Is.EqualTo(original.IsReadOnly));
        Assert.That(clone.StorageEngine, Is.EqualTo(original.StorageEngine));
        Assert.That(clone.DisplayName, Is.EqualTo(original.DisplayName));
    }

    #endregion

    #region Is Tests

    [Test]
    public void IsReturnsTrueForEqualInstancesTest()
    {
        var connectionInfo1 = new ConnectionInfo
        {
            FilePath = "test.witdb",
            IsEncrypted = true,
            Password = "secret",
            IsReadOnly = true,
            StorageEngine = "lsm"
        };

        var connectionInfo2 = new ConnectionInfo
        {
            FilePath = "test.witdb",
            IsEncrypted = true,
            Password = "secret",
            IsReadOnly = true,
            StorageEngine = "lsm"
        };

        Assert.That(connectionInfo1.Is(connectionInfo2), Is.True);
    }

    [Test]
    public void IsReturnsFalseForDifferentInstancesTest()
    {
        var connectionInfo1 = new ConnectionInfo
        {
            FilePath = "test1.witdb"
        };

        var connectionInfo2 = new ConnectionInfo
        {
            FilePath = "test2.witdb"
        };

        Assert.That(connectionInfo1.Is(connectionInfo2), Is.False);
    }

    #endregion
}
