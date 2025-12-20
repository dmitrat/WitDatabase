using NUnit.Framework;
using OutWit.Database.Core.Builder;

namespace OutWit.Database.Core.Tests.Builder;

/// <summary>
/// Tests for opening databases with complex configurations and verifying settings are restored.
/// </summary>
[TestFixture]
public class DatabaseConfigurationPersistenceTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDB_ConfigPersist_{Guid.NewGuid():N}");
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

    #region Encryption Persistence Tests

    [Test]
    public void EncryptedDatabaseRequiresPasswordToOpenTest()
    {
        var path = Path.Combine(m_testDir, "encrypted.db");
        
        using (var db = WitDatabase.Create(path, "secret123"))
        {
            db.Put("key"u8, "value"u8);
        }
        
        // Open without password should fail with InvalidDataException
        // (because Open() detects encryption and throws)
        Assert.Catch<InvalidDataException>(() => WitDatabase.Open(path));
    }

    [Test]
    public void EncryptedDatabaseWrongPasswordFailsTest()
    {
        var path = Path.Combine(m_testDir, "encrypted_wrong.db");
        
        using (var db = WitDatabase.Create(path, "correct"))
        {
            db.Put("key"u8, "value"u8);
        }
        
        // Open with wrong password should fail with CryptographicException
        // (because decryption authentication fails)
        Assert.Catch<Exception>(() => WitDatabase.Open(path, "wrong"));
    }

    [Test]
    public void EncryptedDatabaseCorrectPasswordSucceedsTest()
    {
        var path = Path.Combine(m_testDir, "encrypted_correct.db");
        
        using (var db = WitDatabase.Create(path, "password"))
        {
            db.Put("key"u8, "value"u8);
        }
        
        using (var db = WitDatabase.Open(path, "password"))
        {
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
    }

    #endregion

    #region Transaction State Persistence Tests

    [Test]
    public void DatabaseWithTransactionsPreservesTransactionSupportTest()
    {
        var path = Path.Combine(m_testDir, "with_tx.db");
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithTransactions()
            .Build())
        {
            Assert.That(db.SupportsTransactions, Is.True);
            
            using var tx = db.BeginTransaction();
            tx.Put("key"u8, "value"u8);
            tx.Commit();
        }
        
        // Reopen - should still support transactions with same config
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithTransactions()
            .Build())
        {
            Assert.That(db.SupportsTransactions, Is.True);
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
    }

    [Test]
    public void DatabaseWithoutTransactionsReopensWithoutTransactionsTest()
    {
        var path = Path.Combine(m_testDir, "no_tx.db");
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithoutTransactions()
            .Build())
        {
            Assert.That(db.SupportsTransactions, Is.False);
            db.Put("key"u8, "value"u8);
        }
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithoutTransactions()
            .Build())
        {
            Assert.That(db.SupportsTransactions, Is.False);
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
    }

    /// <summary>
    /// Open() now auto-detects transaction settings from header.
    /// </summary>
    [Test]
    public void OpenAutoDetectsTransactionSettingsTest()
    {
        var path = Path.Combine(m_testDir, "open_autodetect.db");
        
        // Create without transactions
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithoutTransactions()
            .Build())
        {
            db.Put("key"u8, "value"u8);
        }
        
        // Open() should auto-detect that transactions were disabled
        using (var db = WitDatabase.Open(path))
        {
            Assert.That(db.SupportsTransactions, Is.False, 
                "Open() should auto-detect transaction settings from header");
            
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
    }

    [Test]
    public void OpenAutoDetectsTransactionsEnabledTest()
    {
        var path = Path.Combine(m_testDir, "open_with_tx.db");
        
        // Create with transactions
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithTransactions()
            .Build())
        {
            using var tx = db.BeginTransaction();
            tx.Put("key"u8, "value"u8);
            tx.Commit();
        }
        
        // Open() should auto-detect that transactions were enabled
        using (var db = WitDatabase.Open(path))
        {
            Assert.That(db.SupportsTransactions, Is.True);
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
    }

    #endregion

    #region Page Size Persistence Tests
    
    /// <summary>
    /// CRITICAL: Page size mismatch should throw, not silently corrupt data.
    /// </summary>
    [Test]
    public void ReopenWithDifferentPageSizeThrowsTest()
    {
        var path = Path.Combine(m_testDir, "pagesize.db");
        
        // Create with page size 8192
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithPageSize(8192)
            .Build())
        {
            db.Put("key"u8, "value"u8);
        }
        
        // Try to reopen with different page size - should throw
        Assert.Catch<Exception>(() =>
        {
            using var db = new WitDatabaseBuilder()
                .WithFilePath(path)
                .WithBTree()
                .WithPageSize(4096) // Different!
                .Build();
        }, "Opening database with mismatched page size should throw");
    }

    #endregion

    #region GetDatabaseInfo Tests

    [Test]
    public void GetDatabaseInfoReturnsCorrectHintsTest()
    {
        var path = Path.Combine(m_testDir, "info.db");
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithTransactions()
            .WithFileLocking()
            .Build())
        {
            db.Put("key"u8, "value"u8);
        }
        
        var info = WitDatabase.GetDatabaseInfo(path);
        
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.StoreProvider, Is.EqualTo("btree"));
        Assert.That(info.HasTransactions, Is.True);
        Assert.That(info.HasFileLocking, Is.True);
        Assert.That(info.RequiresEncryption, Is.False);
    }

    [Test]
    public void GetDatabaseInfoDetectsEncryptionTest()
    {
        var path = Path.Combine(m_testDir, "encrypted_info.db");
        
        using (var db = WitDatabase.Create(path, "password"))
        {
            db.Put("key"u8, "value"u8);
        }
        
        var info = WitDatabase.GetDatabaseInfo(path);
        
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.RequiresEncryption, Is.True);
    }

    [Test]
    public void GetDatabaseInfoReturnsNullForNonExistentFileTest()
    {
        var path = Path.Combine(m_testDir, "nonexistent.db");
        
        var info = WitDatabase.GetDatabaseInfo(path);
        
        Assert.That(info, Is.Null);
    }

    #endregion

    #region Full Configuration Tests

    [Test]
    public void FullConfigurationBTreeAllOptionsWorksTest()
    {
        var path = Path.Combine(m_testDir, "full_config.db");
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithEncryption("password")
            .WithTransactions()
            .WithPageSize(8192)
            .WithCacheSize(500)
            .WithFileLocking()
            .Build())
        {
            Assert.That(db.SupportsTransactions, Is.True);

            using var tx = db.BeginTransaction();
            tx.Put("key1"u8, "value1"u8);
            tx.Put("key2"u8, "value2"u8);
            tx.Commit();
        }
        
        // Reopen with same config
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithEncryption("password")
            .WithTransactions()
            .WithPageSize(8192)
            .WithCacheSize(500)
            .Build())
        {
            Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
            Assert.That(db.Get("key2"u8), Is.EqualTo("value2"u8.ToArray()));
        }
    }

    [Test]
    public void FullConfigurationLsmAllOptionsWorksTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_full");
        
        using (var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir, opts =>
            {
                opts.EnableWal = true;
                opts.EnableBlockCache = true;
                opts.BlockCacheSizeBytes = 4 * 1024 * 1024;
                opts.MemTableSizeLimit = 512 * 1024;
                opts.SyncWrites = false;
                opts.BackgroundCompaction = false;
            })
            .WithEncryption("password")
            .WithTransactions()
            .Build())
        {
            Assert.That(db.SupportsTransactions, Is.True);

            for (int i = 0; i < 100; i++)
            {
                db.Put(System.Text.Encoding.UTF8.GetBytes($"key{i}"), new byte[100]);
            }
        }
        
        // Reopen
        using (var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir, opts =>
            {
                opts.EnableWal = true;
            })
            .WithEncryption("password")
            .Build())
        {
            Assert.That(db.Get("key50"u8), Is.Not.Null);
        }
    }

    #endregion

    #region Storage + Engine Combinations

    [Test]
    public void MemoryStorageBTreeWorksTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .Build();

        db.Put("key"u8, "value"u8);
        Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    [Test]
    public void FileStorageBTreeWorksTest()
    {
        var path = Path.Combine(m_testDir, "file_btree.db");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .Build();

        db.Put("key"u8, "value"u8);
        Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    [Test]
    public void DirectoryLsmTreeWorksTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm");
        
        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .Build();

        db.Put("key"u8, "value"u8);
        Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    #endregion

    #region Cache and PageSize Tests

    [Test]
    public void CustomCacheSizeIsRespectedTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithCacheSize(50)
            .Build();

        for (int i = 0; i < 100; i++)
        {
            db.Put(System.Text.Encoding.UTF8.GetBytes($"key{i}"), new byte[100]);
        }

        for (int i = 0; i < 100; i++)
        {
            var value = db.Get(System.Text.Encoding.UTF8.GetBytes($"key{i}"));
            Assert.That(value, Is.Not.Null);
        }
    }

    [Test]
    public void CustomPageSizeWorksTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithPageSize(16384)
            .Build();

        for (int i = 0; i < 10; i++)
        {
            db.Put(System.Text.Encoding.UTF8.GetBytes($"key{i}"), new byte[8000]);
        }

        for (int i = 0; i < 10; i++)
        {
            var value = db.Get(System.Text.Encoding.UTF8.GetBytes($"key{i}"));
            Assert.That(value, Has.Length.EqualTo(8000));
        }
    }

    #endregion

    #region Encryption Combinations

    [Test]
    public void MemoryStorageBTreeWithEncryptionWorksTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryption("password")
            .Build();

        db.Put("secret"u8, "data"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("data"u8.ToArray()));
    }

    [Test]
    public void FileStorageBTreeWithEncryptionWorksTest()
    {
        var path = Path.Combine(m_testDir, "encrypted.db");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithEncryption("password")
            .Build();

        db.Put("secret"u8, "data"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("data"u8.ToArray()));
    }

    [Test]
    public void LsmTreeWithEncryptionWorksTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_encrypted");
        var key = new byte[32];
        new Random(42).NextBytes(key);
        
        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithAesEncryption(key)
            .Build();

        db.Put("secret"u8, "data"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("data"u8.ToArray()));
    }

    #endregion
}
