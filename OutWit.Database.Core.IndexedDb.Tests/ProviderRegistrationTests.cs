using NUnit.Framework;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.IndexedDb.Tests.Mocks;

namespace OutWit.Database.Core.IndexedDb.Tests;

/// <summary>
/// Tests for IndexedDbProviderRegistration.
/// </summary>
[TestFixture]
public class ProviderRegistrationTests
{
    #region Fields

    private MockJSRuntime m_jsRuntime = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_jsRuntime = new MockJSRuntime();
        
        // Ensure registration is complete
        IndexedDbProviderRegistration.EnsureRegistered();
    }

    #endregion

    #region Registration Tests

    [Test]
    public void IndexedDbStorageProviderIsRegisteredTest()
    {
        var isRegistered = ProviderRegistry.Instance.IsRegistered<IStorage>(StorageIndexedDb.PROVIDER_KEY);
        
        Assert.That(isRegistered, Is.True);
    }

    [Test]
    public void CreateIndexedDbStorageViaRegistryTest()
    {
        var parameters = new ProviderParameters()
            .Set("databaseName", "TestDb")
            .Set("jsRuntime", m_jsRuntime);
        
        var storage = ProviderRegistry.Instance.Create<IStorage>(StorageIndexedDb.PROVIDER_KEY, parameters);
        
        Assert.That(storage, Is.Not.Null);
        Assert.That(storage, Is.InstanceOf<StorageIndexedDb>());
        Assert.That(storage.ProviderKey, Is.EqualTo("indexeddb"));
        
        storage.Dispose();
    }

    [Test]
    public void CreateIndexedDbStorageWithCustomPageSizeViaRegistryTest()
    {
        var parameters = new ProviderParameters()
            .Set("databaseName", "TestDb")
            .Set("jsRuntime", m_jsRuntime)
            .Set("pageSize", 8192);
        
        var storage = ProviderRegistry.Instance.Create<IStorage>(StorageIndexedDb.PROVIDER_KEY, parameters);
        
        Assert.That(storage.PageSize, Is.EqualTo(8192));
        
        storage.Dispose();
    }

    [Test]
    public void CreateIndexedDbStorageWithDefaultPageSizeTest()
    {
        var parameters = new ProviderParameters()
            .Set("databaseName", "TestDb")
            .Set("jsRuntime", m_jsRuntime);
        
        var storage = ProviderRegistry.Instance.Create<IStorage>(StorageIndexedDb.PROVIDER_KEY, parameters);
        
        Assert.That(storage.PageSize, Is.EqualTo(DatabaseConstants.DEFAULT_PAGE_SIZE));
        
        storage.Dispose();
    }

    [Test]
    public void CreateIndexedDbStorageWithMissingDatabaseNameThrowsTest()
    {
        var parameters = new ProviderParameters()
            .Set("jsRuntime", m_jsRuntime);
        
        Assert.Throws<ArgumentException>(() =>
        {
            ProviderRegistry.Instance.Create<IStorage>(StorageIndexedDb.PROVIDER_KEY, parameters);
        });
    }

    [Test]
    public void CreateIndexedDbStorageWithMissingJsRuntimeThrowsTest()
    {
        var parameters = new ProviderParameters()
            .Set("databaseName", "TestDb");
        
        Assert.Throws<ArgumentException>(() =>
        {
            ProviderRegistry.Instance.Create<IStorage>(StorageIndexedDb.PROVIDER_KEY, parameters);
        });
    }

    [Test]
    public void EnsureRegisteredIsIdempotentTest()
    {
        // Call multiple times
        IndexedDbProviderRegistration.EnsureRegistered();
        IndexedDbProviderRegistration.EnsureRegistered();
        IndexedDbProviderRegistration.EnsureRegistered();
        
        // Should still work
        var isRegistered = ProviderRegistry.Instance.IsRegistered<IStorage>(StorageIndexedDb.PROVIDER_KEY);
        Assert.That(isRegistered, Is.True);
    }

    [Test]
    public void ProviderKeyIsCorrectTest()
    {
        Assert.That(StorageIndexedDb.PROVIDER_KEY, Is.EqualTo("indexeddb"));
    }

    #endregion
}
