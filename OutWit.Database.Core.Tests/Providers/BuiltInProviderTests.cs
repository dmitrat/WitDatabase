using NUnit.Framework;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Providers;

/// <summary>
/// Tests for BuiltInProviderRegistration and provider factory.
/// </summary>
[TestFixture]
public class BuiltInProviderTests
{
    #region Storage Provider Tests

    [Test]
    public void MemoryStorageProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<IStorage>("memory"), Is.True);
    }

    [Test]
    public void MemoryStorageCanBeCreatedViaFactoryTest()
    {
        var storage = ProviderRegistry.Instance.Create<IStorage>("memory",
            new ProviderParameters()
                .Set("pageSize", 4096)
                .Set("initialPageCount", 10));

        Assert.That(storage, Is.Not.Null);
        Assert.That(storage.PageSize, Is.EqualTo(4096));
        Assert.That(storage.ProviderKey, Is.EqualTo("memory"));
        
        storage.Dispose();
    }

    [Test]
    public void FileStorageProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<IStorage>("file"), Is.True);
    }

    [Test]
    public void EncryptedStorageProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<IStorage>("encrypted"), Is.True);
    }

    #endregion

    #region Store Provider Tests

    [Test]
    public void BTreeStoreProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<IKeyValueStore>("btree"), Is.True);
    }

    [Test]
    public void LsmStoreProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<IKeyValueStore>("lsm"), Is.True);
    }

    [Test]
    public void InMemoryStoreProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<IKeyValueStore>("inmemory"), Is.True);
    }

    [Test]
    public void InMemoryStoreCanBeCreatedViaFactoryTest()
    {
        var store = ProviderRegistry.Instance.Create<IKeyValueStore>("inmemory");

        Assert.That(store, Is.Not.Null);
        Assert.That(store.ProviderKey, Is.EqualTo("inmemory"));
        
        store.Put("key"u8.ToArray(), "value"u8.ToArray());
        Assert.That(store.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        
        store.Dispose();
    }

    #endregion

    #region Crypto Provider Tests

    [Test]
    public void AesGcmCryptoProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("aes-gcm"), Is.True);
    }

    [Test]
    public void AesGcmCryptoCanBeCreatedViaFactoryTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        var crypto = ProviderRegistry.Instance.Create<ICryptoProvider>("aes-gcm",
            new ProviderParameters().Set("key", key));

        Assert.That(crypto, Is.Not.Null);
        Assert.That(crypto.ProviderKey, Is.EqualTo("aes-gcm"));
        Assert.That(crypto.NonceSize, Is.EqualTo(12));
        Assert.That(crypto.TagSize, Is.EqualTo(16));
        
        crypto.Dispose();
    }

    #endregion

    #region Cache Provider Tests

    [Test]
    public void ClockCacheProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<IPageCache>("clock"), Is.True);
    }

    [Test]
    public void LruCacheProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<IPageCache>("lru"), Is.True);
    }

    [Test]
    public void CacheCanBeCreatedViaFactoryTest()
    {
        using var storage = new StorageMemory();

        var cache = ProviderRegistry.Instance.Create<IPageCache>("lru",
            new ProviderParameters()
                .Set("storage", storage)
                .Set("capacity", 100));

        Assert.That(cache, Is.Not.Null);
        Assert.That(cache.ProviderKey, Is.EqualTo("lru"));
        
        cache.Dispose();
    }

    #endregion

    #region Journal Provider Tests

    [Test]
    public void RollbackJournalProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<ITransactionJournal>("rollback"), Is.True);
    }

    [Test]
    public void WalJournalProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<ITransactionJournal>("wal"), Is.True);
    }

    #endregion

    #region All Providers Listed Tests

    [Test]
    public void AllStorageProvidersListedTest()
    {
        var keys = ProviderRegistry.Instance.GetRegisteredKeys<IStorage>();
        
        Assert.That(keys, Contains.Item("file"));
        Assert.That(keys, Contains.Item("memory"));
        Assert.That(keys, Contains.Item("encrypted"));
    }

    [Test]
    public void AllStoreProvidersListedTest()
    {
        var keys = ProviderRegistry.Instance.GetRegisteredKeys<IKeyValueStore>();
        
        Assert.That(keys, Contains.Item("btree"));
        Assert.That(keys, Contains.Item("lsm"));
        Assert.That(keys, Contains.Item("inmemory"));
    }

    [Test]
    public void AllCacheProvidersListedTest()
    {
        var keys = ProviderRegistry.Instance.GetRegisteredKeys<IPageCache>();
        
        Assert.That(keys, Contains.Item("clock"));
        Assert.That(keys, Contains.Item("lru"));
    }

    #endregion
}
