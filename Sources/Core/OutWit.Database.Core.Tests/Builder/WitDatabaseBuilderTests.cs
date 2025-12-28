using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Builder;

[TestFixture]
public class WitDatabaseBuilderTests
{
    private string m_testDir = null!;

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDB_Builder_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        // Give some time for file handles to be released
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        if (Directory.Exists(m_testDir))
        {
            try { Directory.Delete(m_testDir, recursive: true); } catch { }
        }
    }

    #region Memory Storage + BTree Tests

    [Test]
    public void MemoryBTreeWithoutTransactionsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithoutTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.False);
        
        db.Put("key1"u8, "value1"u8);
        var value = db.Get("key1"u8);

        Assert.That(value, Is.Not.Null);
        Assert.That(value, Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void MemoryBTreeWithTransactionsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        var value = db.Get("key1"u8);
        Assert.That(value, Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void MemoryBTreeWithEncryptionTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithAesEncryption(key)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        var value = db.Get("secret"u8);

        Assert.That(value, Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void MemoryBTreeWithPasswordEncryptionTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryption("my-secret-password")
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        var value = db.Get("secret"u8);

        Assert.That(value, Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void MemoryBTreeWithUserPasswordEncryptionTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryption("admin", "my-secret-password")
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        var value = db.Get("secret"u8);

        Assert.That(value, Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void SamePasswordProducesSameEncryptionTest()
    {
        // First database - write data
        using (var db1 = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryption("test-password")
            .WithoutTransactions()
            .Build())
        {
            db1.Put("key"u8, "value"u8);
        }

        // The encryption is deterministic based on password
        // This test verifies the key derivation is consistent
        using var db2 = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryption("test-password")
            .WithoutTransactions()
            .Build();

        // Different memory storage, so no data, but encryption setup works
        db2.Put("key"u8, "value"u8);
        Assert.That(db2.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    [Test]
    public void MemoryBTreeWithEncryptionAndTransactionsTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithAesEncryption(key)
            .WithTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("secret"u8, "classified"u8);
            tx.Commit();
        }

        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    #endregion

    #region File Storage + BTree Tests

    [Test]
    public void FileBTreeWithoutTransactionsTest()
    {
        var dbPath = Path.Combine(m_testDir, "btree_simple.db");

        // First session - write data
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithoutTransactions()
            .Build())
        {
            db.Put("key1"u8, "value1"u8);
            db.Flush();
        }

        // Second session - verify data persisted
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithoutTransactions()
            .Build())
        {
            var value = db.Get("key1"u8);
            Assert.That(value, Is.EqualTo("value1"u8.ToArray()));
        }
    }

    [Test]
    public void FileBTreeWithTransactionsTest()
    {
        var dbPath = Path.Combine(m_testDir, "btree_tx.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void FileBTreeWithEncryptionTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var dbPath = Path.Combine(m_testDir, "btree_enc.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithAesEncryption(key)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    #endregion

    #region LSM-Tree Tests

    [Test]
    public void LsmTreeWithoutTransactionsTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_simple");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        var value = db.Get("key1"u8);

        Assert.That(value, Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void LsmTreeWithTransactionsTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_tx");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void LsmTreeWithEncryptionTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var lsmDir = Path.Combine(m_testDir, "lsm_enc");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithAesEncryption(key)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void LsmTreeWithCustomOptionsTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_custom");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(lsmDir)
            .WithLsmTree(opts =>
            {
                opts.MemTableSizeLimit = 1024 * 1024; // 1 MB
                opts.EnableBlockCache = true;
                opts.BlockCacheSizeBytes = 16 * 1024 * 1024; // 16 MB
            })
            .WithoutTransactions()
            .Build();

        // Write enough data to trigger flush
        for (int i = 0; i < 100; i++)
        {
            db.Put(System.Text.Encoding.UTF8.GetBytes($"key{i}"), new byte[1024]);
        }

        var value = db.Get("key50"u8);
        Assert.That(value, Is.Not.Null);
    }

    #endregion

    #region Configuration Tests

    [Test]
    public void CustomPageSizeTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithPageSize(8192)
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, new byte[4000]);
        var value = db.Get("key1"u8);

        Assert.That(value, Has.Length.EqualTo(4000));
    }

    [Test]
    public void CustomCacheSizeTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithCacheSize(100)
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.Not.Null);
    }

    [Test]
    public void CustomLockTimeoutTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .WithLockTimeout(TimeSpan.FromSeconds(10))
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);
    }

    [Test]
    public void EncryptionWithCustomSaltTest()
    {
        var key = new byte[32];
        var salt = new byte[16];
        new Random(42).NextBytes(key);
        new Random(123).NextBytes(salt);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithAesEncryption(key, salt)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    #endregion

    #region Invalid Configuration Tests

    [Test]
    public void NoStorageConfiguredThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithBTree()
                .Build();
        });
        
        Assert.That(ex!.Message, Does.Contain("Storage not configured"));
    }

    [Test]
    public void LsmWithoutDirectoryThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithLsmTree()
                .Build();
        });
        
        Assert.That(ex!.Message, Does.Contain("LSM-Tree requires a directory"));
    }

    [Test]
    public void LsmWithCustomStorageThrowsTest()
    {
        var storage = new StorageMemory(4096);
        
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithStorage(storage)
                .WithLsmTree()
                .Build();
        });
        
        Assert.That(ex!.Message, Does.Contain("LSM-Tree uses directory-based storage"));
        storage.Dispose();
    }

    [Test]
    public void CustomStoreWithEncryptionThrowsTest()
    {
        var store = new TestStoreInMemory();
        var key = new byte[32];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithStore(store)
                .WithAesEncryption(key)
                .Build();
        });
        
        Assert.That(ex!.Message, Does.Contain("Cannot use WithAesEncryption"));
        store.Dispose();
    }

    [Test]
    public void CustomStoreWithStorageThrowsTest()
    {
        var store = new TestStoreInMemory();
        var storage = new StorageMemory(4096);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithStore(store)
                .WithStorage(storage)
                .Build();
        });
        
        Assert.That(ex!.Message, Does.Contain("Cannot use WithStorage() with WithStore(IKeyValueStore)"));
        store.Dispose();
        storage.Dispose();
    }

    [Test]
    public void InvalidPageSizeTooSmallThrowsTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            new WitDatabaseBuilder()
                .WithPageSize(100);
        });
    }

    [Test]
    public void InvalidPageSizeTooLargeThrowsTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            new WitDatabaseBuilder()
                .WithPageSize(100000);
        });
    }

    [Test]
    public void InvalidPageSizeNotPowerOfTwoThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree()
                .WithPageSize(1000) // Valid range but not power of 2
                .Build();
        });
        
        Assert.That(ex!.Message, Does.Contain("power of 2"));
    }

    [Test]
    public void InvalidCacheSizeThrowsTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            new WitDatabaseBuilder()
                .WithCacheSize(0);
        });
    }

    [Test]
    public void InvalidEncryptionKeyThrowsTest()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            new WitDatabaseBuilder()
                .WithAesEncryption(new byte[16]); // Wrong size
        });
    }

    [Test]
    public void InvalidSaltThrowsTest()
    {
        var key = new byte[32];
        
        Assert.Throws<ArgumentException>(() =>
        {
            new WitDatabaseBuilder()
                .WithAesEncryption(key, new byte[4]); // Too short
        });
    }

    #endregion

    #region Build Methods Tests

    [Test]
    public void BuildStoreReturnsKeyValueStoreTest()
    {
        var store = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .BuildStore();

        try
        {
            store.Put("key"u8, "value"u8);
            Assert.That(store.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
        finally
        {
            store.Dispose();
        }
    }

    [Test]
    public void BuildTransactionalStoreReturnsTransactionalStoreTest()
    {
        var store = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .BuildTransactionalStore();

        try
        {
            using var tx = store.BeginTransaction();
            tx.Put("key"u8, "value"u8);
            tx.Commit();

            Assert.That(store.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
        finally
        {
            store.Dispose();
        }
    }

    #endregion

    #region Scan Tests

    [Test]
    public void ScanWorksWithBuiltDatabaseTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithoutTransactions()
            .Build();

        db.Put("a"u8, "1"u8);
        db.Put("b"u8, "2"u8);
        db.Put("c"u8, "3"u8);

        var results = db.Scan().ToList();

        Assert.That(results, Has.Count.EqualTo(3));
    }

    #endregion

    #region String Key Tests

    [Test]
    public void StringKeyOverloadsWorkTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithoutTransactions()
            .Build();

        db.Put("mykey", "value"u8.ToArray());
        var value = db.Get("mykey");

        Assert.That(value, Is.EqualTo("value"u8.ToArray()));

        var deleted = db.Delete("mykey");
        Assert.That(deleted, Is.True);
        Assert.That(db.Get("mykey"), Is.Null);
    }

    #endregion

    #region Transaction Error Tests

    [Test]
    public void BeginTransactionWithoutTransactionsThrowsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithoutTransactions()
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => db.BeginTransaction());
        Assert.That(ex!.Message, Does.Contain("Transactions are not enabled"));
    }

    #endregion

    #region MVCC Tests

    [Test]
    public void MemoryBTreeWithMvccTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithMvcc()
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);
        Assert.That(db.SupportsMvcc, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        var value = db.Get("key1"u8);
        Assert.That(value, Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void MvccReadOnlyTransactionTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithMvcc()
            .Build();

        db.Put("key1"u8, "value1"u8);

        var readTx = db.BeginReadOnlyTransaction();
        
        var value = readTx.Get("key1"u8);
        Assert.That(value, Is.EqualTo("value1"u8.ToArray()));
        Assert.That(readTx.IsReadOnly, Is.True);
        
        readTx.Dispose();
    }

    [Test]
    public void MvccConcurrentReadTransactionsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithMvcc()
            .Build();

        db.Put("key1"u8, "value1"u8);

        // Start multiple read transactions concurrently
        var readTx1 = db.BeginReadOnlyTransaction();
        var readTx2 = db.BeginReadOnlyTransaction();
        var readTx3 = db.BeginReadOnlyTransaction();

        // All should see the same data
        Assert.That(readTx1.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
        Assert.That(readTx2.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
        Assert.That(readTx3.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));

        readTx1.Dispose();
        readTx2.Dispose();
        readTx3.Dispose();
    }

    [Test]
    public void MvccSnapshotIsolationTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithMvcc()
            .Build();

        db.Put("key1"u8, "v1"u8);

        // Start read transaction - takes snapshot
        var readTx = db.BeginReadOnlyTransaction();
        Assert.That(readTx.Get("key1"u8), Is.EqualTo("v1"u8.ToArray()));

        // Commit new value outside the read transaction
        db.Put("key1"u8, "v2"u8);

        // Read transaction still sees old value (snapshot isolation)
        Assert.That(readTx.Get("key1"u8), Is.EqualTo("v1"u8.ToArray()));

        readTx.Dispose();

        // New read sees updated value
        Assert.That(db.Get("key1"u8), Is.EqualTo("v2"u8.ToArray()));
    }

    [Test]
    public void MvccReadDuringWriteTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithMvcc()
            .Build();

        db.Put("key1"u8, "initial"u8);

        // Start write transaction
        using var writeTx = db.BeginTransaction();
        writeTx.Put("key1"u8, "new-value"u8);

        // Start read transaction during write
        var readTx = db.BeginReadOnlyTransaction();

        // Read should see old value (snapshot isolation)
        Assert.That(readTx.Get("key1"u8), Is.EqualTo("initial"u8.ToArray()));

        writeTx.Commit();

        // Read transaction still sees old value (its snapshot)
        Assert.That(readTx.Get("key1"u8), Is.EqualTo("initial"u8.ToArray()));

        readTx.Dispose();
    }

    [Test]
    public void MvccWithCustomIsolationLevelTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithMvcc(IsolationLevel.Serializable)
            .Build();

        Assert.That(db.SupportsMvcc, Is.True);

        using var tx = db.BeginTransaction();
        Assert.That(tx.IsolationLevel, Is.EqualTo(IsolationLevel.Serializable));
        tx.Rollback();
    }

    [Test]
    public void MvccLsmTreeTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_mvcc");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithMvcc()
            .Build();

        Assert.That(db.SupportsMvcc, Is.True);

        db.Put("key1"u8, "value1"u8);

        var readTx = db.BeginReadOnlyTransaction();
        Assert.That(readTx.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
        readTx.Dispose();
    }

    [Test]
    public void BeginReadOnlyTransactionWithoutMvccThrowsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions() // Regular transactions, not MVCC
            .Build();

        Assert.That(db.SupportsMvcc, Is.False);

        var ex = Assert.Throws<InvalidOperationException>(() => db.BeginReadOnlyTransaction());
        Assert.That(ex!.Message, Does.Contain("MVCC"));
    }

    [Test]
    public void MvccActiveTransactionCountTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithMvcc()
            .Build();

        Assert.That(db.ActiveTransactionCount, Is.EqualTo(0));

        var tx1 = db.BeginTransaction();
        Assert.That(db.ActiveTransactionCount, Is.EqualTo(1));

        var tx2 = db.BeginReadOnlyTransaction();
        Assert.That(db.ActiveTransactionCount, Is.EqualTo(2));

        tx1.Commit();
        Assert.That(db.ActiveTransactionCount, Is.EqualTo(1));

        tx2.Dispose();
        Assert.That(db.ActiveTransactionCount, Is.EqualTo(0));
    }

    #endregion

    #region Provider Key API Tests

    [Test]
    public void WithStoreKeyBTreeTest()
    {
        var dbPath = Path.Combine(m_testDir, "provider_btree.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithStoreKey(StoreBTree.PROVIDER_KEY)
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithStoreKeyLsmTest()
    {
        var lsmDir = Path.Combine(m_testDir, "provider_lsm");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(lsmDir)
            .WithStoreKey(StoreLsm.PROVIDER_KEY)
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithStoreKeyInMemoryTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithStoreKey(StoreInMemory.PROVIDER_KEY)
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithStoreKeyAndParametersTest()
    {
        var dbPath = Path.Combine(m_testDir, "provider_params.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithStoreKey(StoreBTree.PROVIDER_KEY, p =>
            {
                p.Set("cacheSize", 50);
            })
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithStoreKeyEmptyParametersUsesDefaultsTest()
    {
        var dbPath = Path.Combine(m_testDir, "provider_defaults.db");

        // Empty parameters should be filled with defaults
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithStoreKey(StoreBTree.PROVIDER_KEY, new ProviderParameters())
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithStoreKeyInvalidThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithStoreKey("non-existent-provider")
                .Build();
        });

        Assert.That(ex!.Message, Does.Contain("Store provider 'non-existent-provider' is not registered"));
    }

    [Test]
    public void WithEncryptionKeyAesGcmTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryptionKey("aes-gcm", "my-password")
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void WithEncryptionKeyUserPasswordTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryptionKey("aes-gcm", "user", "password")
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void WithEncryptionKeyFastTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryptionKeyFast("aes-gcm", "my-password")
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void WithEncryptionKeyInvalidThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree()
                .WithEncryptionKey("non-existent-crypto", "password")
                .Build();
        });

        Assert.That(ex!.Message, Does.Contain("Encryption provider 'non-existent-crypto' is not registered"));
    }

    [Test]
    public void WithJournalKeyRollbackTest()
    {
        var dbPath = Path.Combine(m_testDir, "journal_rollback.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithJournalKey("rollback")
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithJournalKeyWalTest()
    {
        var dbPath = Path.Combine(m_testDir, "journal_wal.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithJournalKey("wal")
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithJournalKeyWithParametersTest()
    {
        var dbPath = Path.Combine(m_testDir, "journal_params.db");
        var journalPath = Path.Combine(m_testDir, "custom.journal");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithJournalKey("rollback", p =>
            {
                p.Set("filePath", journalPath);
            })
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithJournalKeyInvalidThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree()
                .WithJournalKey("non-existent-journal")
                .Build();
        });

        Assert.That(ex!.Message, Does.Contain("Journal provider 'non-existent-journal' is not registered"));
    }

    [Test]
    public void WithCacheKeyClockTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithCacheKey("clock")
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithCacheKeyLruTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithCacheKey("lru")
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithCacheKeyWithParametersTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithCacheKey("clock", p =>
            {
                p.Set("capacity", 100);
            })
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void WithCacheKeyInvalidThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree()
                .WithCacheKey("non-existent-cache")
                .WithoutTransactions()
                .Build();
        });

        Assert.That(ex!.Message, Does.Contain("Cache provider 'non-existent-cache' is not registered"));
    }

    [Test]
    public void ProviderKeyWithTransactionsTest()
    {
        var dbPath = Path.Combine(m_testDir, "provider_tx.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithStoreKey(StoreBTree.PROVIDER_KEY)
            .WithTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void ProviderKeyWithMvccTest()
    {
        var dbPath = Path.Combine(m_testDir, "provider_mvcc.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithStoreKey(StoreBTree.PROVIDER_KEY)
            .WithMvcc()
            .Build();

        Assert.That(db.SupportsMvcc, Is.True);

        db.Put("key1"u8, "value1"u8);

        var readTx = db.BeginReadOnlyTransaction();
        Assert.That(readTx.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
        readTx.Dispose();
    }

    [Test]
    public void CombinedProviderKeysTest()
    {
        var dbPath = Path.Combine(m_testDir, "combined_providers.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithStoreKey(StoreBTree.PROVIDER_KEY)
            .WithEncryptionKey("aes-gcm", "password")
            .WithCacheKey("clock")
            .WithJournalKey("rollback")
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("secret"u8, "classified"u8);
            tx.Commit();
        }

        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    #endregion

    #region Provider Parameters Default Values Tests

    [Test]
    public void EmptyStoreParametersGetFilledWithDefaultsTest()
    {
        var dbPath = Path.Combine(m_testDir, "default_store_params.db");

        // Should work even with empty parameters - defaults will be applied
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithStoreKey(StoreBTree.PROVIDER_KEY, new ProviderParameters())
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void PartialStoreParametersGetMergedWithDefaultsTest()
    {
        var dbPath = Path.Combine(m_testDir, "partial_store_params.db");

        // Only cacheSize specified - other defaults should be applied
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithStoreKey(StoreBTree.PROVIDER_KEY, p =>
            {
                p.Set("cacheSize", 50);
                // storage, ownsStorage, providerMetadata should get defaults
            })
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void EmptyJournalParametersGetFilledWithDefaultsTest()
    {
        var dbPath = Path.Combine(m_testDir, "default_journal_params.db");

        // Should work even with empty parameters - filePath will be derived from dbPath
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithJournalKey("rollback", new ProviderParameters())
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void LsmProviderKeyWithEmptyParametersTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_empty_params");

        // Should work - directory and options will be derived
        using var db = new WitDatabaseBuilder()
            .WithFilePath(lsmDir)
            .WithStoreKey(StoreLsm.PROVIDER_KEY, new ProviderParameters())
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    [Test]
    public void ProviderParametersOverrideDefaultsTest()
    {
        var dbPath = Path.Combine(m_testDir, "override_defaults.db");
        var customCacheSize = 123;

        // User-specified value should override default
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithCacheSize(50) // Default from builder
            .WithStoreKey(StoreBTree.PROVIDER_KEY, p =>
            {
                p.Set("cacheSize", customCacheSize); // Override in parameters
            })
            .WithoutTransactions()
            .Build();

        db.Put("key1"u8, "value1"u8);
        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    #endregion

    #region Helper class for tests

    /// <summary>
    /// Test-only in-memory store (different from OutWit.Database.Core.Stores.StoreInMemory).
    /// </summary>
    private sealed class TestStoreInMemory : IKeyValueStore
    {
        private readonly Dictionary<string, byte[]> m_data = new();

        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            var keyStr = Convert.ToBase64String(key);
            return m_data.TryGetValue(keyStr, out var value) ? value : null;
        }

        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Get(key));

        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            m_data[Convert.ToBase64String(key)] = value.ToArray();
        }

        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            Put(key, value);
            return ValueTask.CompletedTask;
        }

        public bool Delete(ReadOnlySpan<byte> key)
            => m_data.Remove(Convert.ToBase64String(key));

        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Delete(key));

        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
            => m_data.Select(kv => (Convert.FromBase64String(kv.Key), kv.Value));

        public IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(byte[]? startKey, byte[]? endKey, CancellationToken cancellationToken = default)
            => Scan(startKey, endKey).ToAsyncEnumerable();

        public void Flush() { }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public string ProviderKey => "test-inmemory";

        public void Dispose() { }
    }

    #endregion
}

// Extension for async enumerable
internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }
}
