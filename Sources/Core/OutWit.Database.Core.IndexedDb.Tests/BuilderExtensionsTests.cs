using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.IndexedDb.Tests.Mocks;

namespace OutWit.Database.Core.IndexedDb.Tests;

/// <summary>
/// Tests for WitDatabaseBuilderIndexedDbExtensions.
/// </summary>
[TestFixture]
public class BuilderExtensionsTests
{
    #region Fields

    private MockJSRuntime m_jsRuntime = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_jsRuntime = new MockJSRuntime();
    }

    #endregion

    #region WithIndexedDbStorage Tests

    [Test]
    public void WithIndexedDbStorageSetsStorageTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithIndexedDbStorage("TestDb", m_jsRuntime);
        
        Assert.That(builder.Options.CustomStorage, Is.Not.Null);
        Assert.That(builder.Options.CustomStorage, Is.InstanceOf<StorageIndexedDb>());
    }

    [Test]
    public void WithIndexedDbStorageSetsDatabaseNameTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithIndexedDbStorage("MyDatabase", m_jsRuntime);
        
        var storage = (StorageIndexedDb)builder.Options.CustomStorage!;
        Assert.That(storage.DatabaseName, Is.EqualTo("MyDatabase"));
    }

    [Test]
    public void WithIndexedDbStorageDisablesFileLockingTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithFileLocking()  // Enable first
            .WithIndexedDbStorage("TestDb", m_jsRuntime);
        
        Assert.That(builder.Options.EnableFileLocking, Is.False);
    }

    [Test]
    public void WithIndexedDbStorageForcesBTreeIfNoEngineSpecifiedTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithIndexedDbStorage("TestDb", m_jsRuntime);
        
        Assert.That(builder.Options.UseBTree, Is.True);
        Assert.That(builder.Options.UseLsmTree, Is.False);
    }

    [Test]
    public void WithIndexedDbStorageClearsFilePathTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithFilePath("test.db")
            .WithIndexedDbStorage("TestDb", m_jsRuntime);
        
        Assert.That(builder.Options.FilePath, Is.Null);
    }

    [Test]
    public void WithIndexedDbStorageClearsMemoryStorageFlagTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithIndexedDbStorage("TestDb", m_jsRuntime);
        
        Assert.That(builder.Options.UseMemoryStorage, Is.False);
    }

    [Test]
    public void WithIndexedDbStorageWithCustomPageSizeTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithIndexedDbStorage("TestDb", m_jsRuntime, pageSize: 8192);
        
        var storage = (StorageIndexedDb)builder.Options.CustomStorage!;
        Assert.That(storage.PageSize, Is.EqualTo(8192));
    }

    #endregion

    #region Validation Tests

    [Test]
    public void WithIndexedDbStorageAfterLsmTreeThrowsTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithLsmTree("./lsm");
        
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.WithIndexedDbStorage("TestDb", m_jsRuntime);
        });
        
        Assert.That(ex!.Message, Does.Contain("LSM-Tree"));
        Assert.That(ex.Message, Does.Contain("not compatible"));
    }

    [Test]
    public void WithIndexedDbStorageWithNullJsRuntimeThrowsTest()
    {
        var builder = new WitDatabaseBuilder();
        
        Assert.Throws<ArgumentNullException>(() =>
        {
            builder.WithIndexedDbStorage("TestDb", null!);
        });
    }

    [Test]
    public void WithIndexedDbStorageWithEmptyNameThrowsTest()
    {
        var builder = new WitDatabaseBuilder();
        
        Assert.Throws<ArgumentException>(() =>
        {
            builder.WithIndexedDbStorage("", m_jsRuntime);
        });
    }

    [Test]
    public void WithIndexedDbStorageWithWhitespaceNameThrowsTest()
    {
        var builder = new WitDatabaseBuilder();
        
        Assert.Throws<ArgumentException>(() =>
        {
            builder.WithIndexedDbStorage("   ", m_jsRuntime);
        });
    }

    #endregion

    #region Chaining Tests

    [Test]
    public void BuilderChainingWorksTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithIndexedDbStorage("TestDb", m_jsRuntime)
            .WithBTree()
            .WithTransactions()
            .WithPageSize(8192)
            .WithCacheSize(100);
        
        Assert.That(builder.Options.CustomStorage, Is.Not.Null);
        Assert.That(builder.Options.UseBTree, Is.True);
        Assert.That(builder.Options.EnableTransactions, Is.True);
    }

    [Test]
    public void WithBTreeAfterIndexedDbStorageWorksTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithIndexedDbStorage("TestDb", m_jsRuntime)
            .WithBTree();
        
        Assert.That(builder.Options.UseBTree, Is.True);
    }

    [Test]
    public void WithEncryptionAfterIndexedDbStorageWorksTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithIndexedDbStorage("TestDb", m_jsRuntime)
            .WithEncryption("password");
        
        Assert.That(builder.Options.CustomCryptoProvider, Is.Not.Null);
    }

    [Test]
    public void WithMvccAfterIndexedDbStorageWorksTest()
    {
        var builder = new WitDatabaseBuilder()
            .WithIndexedDbStorage("TestDb", m_jsRuntime)
            .WithMvcc();
        
        Assert.That(builder.Options.EnableMvcc, Is.True);
    }

    #endregion
}
