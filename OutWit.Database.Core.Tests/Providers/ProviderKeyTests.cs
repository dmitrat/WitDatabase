using NUnit.Framework;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;
using OutWit.Database.Core.Wal;

namespace OutWit.Database.Core.Tests.Providers;

/// <summary>
/// Tests for ProviderKey properties across all implementations.
/// </summary>
[TestFixture]
public class ProviderKeyTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDB_ProviderKey_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        if (Directory.Exists(m_testDir))
        {
            try { Directory.Delete(m_testDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region Storage Provider Keys

    [Test]
    public void StorageMemoryHasCorrectProviderKeyTest()
    {
        using var storage = new StorageMemory();
        
        Assert.That(storage.ProviderKey, Is.EqualTo(StorageMemory.PROVIDER_KEY));
        Assert.That(storage.ProviderKey, Is.EqualTo("memory"));
    }

    [Test]
    public void StorageFileHasCorrectProviderKeyTest()
    {
        var path = Path.Combine(m_testDir, "test.db");
        using var storage = new StorageFile(path);
        
        Assert.That(storage.ProviderKey, Is.EqualTo(StorageFile.PROVIDER_KEY));
        Assert.That(storage.ProviderKey, Is.EqualTo("file"));
    }

    [Test]
    public void StorageEncryptedHasCorrectProviderKeyTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var provider = new EncryptorProviderAesGcm(key);
        var encryptor = new EncryptorPage(provider, new byte[16]);
        
        using var innerStorage = new StorageMemory(4096 + 28);
        using var storage = new StorageEncrypted(innerStorage, encryptor);
        
        Assert.That(storage.ProviderKey, Is.EqualTo(StorageEncrypted.PROVIDER_KEY));
        Assert.That(storage.ProviderKey, Is.EqualTo("encrypted"));
    }

    #endregion

    #region Store Provider Keys

    [Test]
    public void StoreBTreeHasCorrectProviderKeyTest()
    {
        using var storage = new StorageMemory();
        using var store = new StoreBTree(storage, ownsStorage: false);
        
        Assert.That(store.ProviderKey, Is.EqualTo(StoreBTree.PROVIDER_KEY));
        Assert.That(store.ProviderKey, Is.EqualTo("btree"));
    }

    [Test]
    public void StoreLsmHasCorrectProviderKeyTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm");
        using var store = new StoreLsm(lsmDir);
        
        Assert.That(store.ProviderKey, Is.EqualTo(StoreLsm.PROVIDER_KEY));
        Assert.That(store.ProviderKey, Is.EqualTo("lsm"));
    }

    [Test]
    public void StoreInMemoryHasCorrectProviderKeyTest()
    {
        using var store = new StoreInMemory();
        
        Assert.That(store.ProviderKey, Is.EqualTo(StoreInMemory.PROVIDER_KEY));
        Assert.That(store.ProviderKey, Is.EqualTo("inmemory"));
    }

    [Test]
    public void TransactionalStoreHasOwnProviderKeyTest()
    {
        using var storage = new StorageMemory();
        using var innerStore = new StoreBTree(storage, ownsStorage: false);
        using var store = new TransactionalStore(innerStore, ownsStore: false);
        
        Assert.That(store.ProviderKey, Is.EqualTo(TransactionalStore.PROVIDER_KEY));
        Assert.That(store.ProviderKey, Is.EqualTo("transactional"));
    }

    #endregion

    #region Crypto Provider Keys

    [Test]
    public void CryptoProviderAesGcmHasCorrectProviderKeyTest()
    {
        var key = new byte[32];
        using var provider = new EncryptorProviderAesGcm(key);
        
        Assert.That(provider.ProviderKey, Is.EqualTo(EncryptorProviderAesGcm.PROVIDER_KEY));
        Assert.That(provider.ProviderKey, Is.EqualTo("aes-gcm"));
    }

    #endregion

    #region Cache Provider Keys

    [Test]
    public void PageCacheLruHasCorrectProviderKeyTest()
    {
        using var storage = new StorageMemory();
        storage.SetSize(10);
        using var cache = new PageCacheLru(storage, maxPages: 10);
        
        Assert.That(cache.ProviderKey, Is.EqualTo(PageCacheLru.PROVIDER_KEY));
        Assert.That(cache.ProviderKey, Is.EqualTo("lru"));
    }

    [Test]
    public void PageCacheShardedClockHasCorrectProviderKeyTest()
    {
        using var storage = new StorageMemory();
        storage.SetSize(100);
        using var cache = new PageCacheShardedClock(storage, maxPages: 100);
        
        Assert.That(cache.ProviderKey, Is.EqualTo(PageCacheShardedClock.PROVIDER_KEY));
        Assert.That(cache.ProviderKey, Is.EqualTo("clock"));
    }

    #endregion

    #region Journal Provider Keys

    [Test]
    public void RollbackJournalHasCorrectProviderKeyTest()
    {
        var journalPath = Path.Combine(m_testDir, "rollback");
        using var journal = new RollbackJournal(journalPath);
        
        Assert.That(journal.ProviderKey, Is.EqualTo(RollbackJournal.PROVIDER_KEY));
        Assert.That(journal.ProviderKey, Is.EqualTo("rollback"));
    }

    [Test]
    public void WalTransactionJournalHasCorrectProviderKeyTest()
    {
        var walPath = Path.Combine(m_testDir, "wal.log");
        using var journal = new WalTransactionJournal(walPath, createNew: true);
        
        Assert.That(journal.ProviderKey, Is.EqualTo(WalTransactionJournal.PROVIDER_KEY));
        Assert.That(journal.ProviderKey, Is.EqualTo("wal"));
    }

    #endregion
}
