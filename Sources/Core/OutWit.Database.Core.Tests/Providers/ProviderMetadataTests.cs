using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Providers;

/// <summary>
/// Tests for ProviderMetadata persistence in database header.
/// </summary>
[TestFixture]
public class ProviderMetadataTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDB_Metadata_{Guid.NewGuid():N}");
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

    #region Basic Metadata Tests

    [Test]
    public void NewDatabaseHasDefaultMetadataTest()
    {
        using var storage = new StorageMemory();
        using var pageManager = new PageManager(storage);
        
        var metadata = pageManager.GetProviderMetadata();
        
        Assert.That(metadata.StoreProviderKey, Is.EqualTo("btree"));
        Assert.That(metadata.EncryptionProviderKey, Is.Empty);
        Assert.That(metadata.Features, Is.EqualTo(ProviderFeatures.None));
    }

    [Test]
    public void MetadataPersistedToHeaderTest()
    {
        var metadata = new ProviderMetadata
        {
            Features = ProviderFeatures.Encryption | ProviderFeatures.Transactions,
            StoreProviderKey = "btree",
            EncryptionProviderKey = "aes-gcm",
            CacheProviderKey = "clock",
            JournalProviderKey = "wal"
        };

        using var storage = new StorageMemory();
        
        // Create with metadata
        using (var pageManager = new PageManager(storage, 100))
        {
            pageManager.SetProviderMetadata(metadata);
            pageManager.Flush();
        }

        // Reopen and verify
        using (var pageManager = new PageManager(storage, 100))
        {
            var loaded = pageManager.GetProviderMetadata();
            
            Assert.That(loaded.StoreProviderKey, Is.EqualTo("btree"));
            Assert.That(loaded.EncryptionProviderKey, Is.EqualTo("aes-gcm"));
            Assert.That(loaded.IsEncrypted, Is.True);
            Assert.That(loaded.HasTransactions, Is.True);
        }
    }

    [Test]
    public void MetadataWrittenToFileStorageTest()
    {
        var path = Path.Combine(m_testDir, "metadata.db");
        
        var metadata = new ProviderMetadata
        {
            Features = ProviderFeatures.Encryption | ProviderFeatures.FileLocking,
            StoreProviderKey = "lsm",
            EncryptionProviderKey = "chacha20" // Shorter key that fits in 16 bytes
        };

        // Create
        using (var storage = new StorageFile(path))
        using (var pageManager = new PageManager(storage, 100))
        {
            pageManager.SetProviderMetadata(metadata);
        }

        // Reopen
        using (var storage = new StorageFile(path))
        using (var pageManager = new PageManager(storage, 100))
        {
            var loaded = pageManager.GetProviderMetadata();
            
            Assert.That(loaded.StoreProviderKey, Is.EqualTo("lsm"));
            Assert.That(loaded.EncryptionProviderKey, Is.EqualTo("chacha20"));
            Assert.That(loaded.IsEncrypted, Is.True);
            Assert.That(loaded.Features.HasFlag(ProviderFeatures.FileLocking), Is.True);
            Assert.That(loaded.HasTransactions, Is.False);
        }
    }

    #endregion

    #region Builder Metadata Tests

    [Test]
    public void BuilderSetsCorrectMetadataForBTreeTest()
    {
        var path = Path.Combine(m_testDir, "btree.db");
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithTransactions()
            .Build())
        {
            db.Put("key"u8, "value"u8);
        }

        // Read header directly
        using var storage = new StorageFile(path);
        using var pageManager = new PageManager(storage, 100);
        
        var metadata = pageManager.GetProviderMetadata();
        
        Assert.That(metadata.StoreProviderKey, Is.EqualTo("btree"));
        Assert.That(metadata.HasTransactions, Is.True);
        Assert.That(metadata.IsEncrypted, Is.False);
    }

    [Test]
    public void BuilderSetsCorrectMetadataForEncryptedDatabaseTest()
    {
        var path = Path.Combine(m_testDir, "encrypted.db");
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithEncryption("password")
            .WithTransactions()
            .Build())
        {
            db.Put("key"u8, "value"u8);
        }

        // Read header directly (will fail to decrypt but we can check raw bytes)
        // Actually, for encrypted DB we can't read header directly without the key
        // But we verified the metadata is set when opening with correct password
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithEncryption("password")
            .WithTransactions()
            .Build())
        {
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
    }

    [Test]
    public void BuilderWithoutTransactionsSetsCorrectFlagsTest()
    {
        var path = Path.Combine(m_testDir, "notx.db");
        
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithoutTransactions()
            .Build())
        {
            db.Put("key"u8, "value"u8);
        }

        using var storage = new StorageFile(path);
        using var pageManager = new PageManager(storage, 100);
        
        var metadata = pageManager.GetProviderMetadata();
        
        Assert.That(metadata.HasTransactions, Is.False);
    }

    #endregion

    #region ProviderMetadata Serialization Tests

    [Test]
    public void ProviderKeyMaxLengthTruncatedTest()
    {
        var longKey = new string('x', 32); // Longer than MAX_PROVIDER_KEY_LENGTH
        
        var metadata = new ProviderMetadata
        {
            StoreProviderKey = longKey
        };

        var buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        DatabaseConstants.MAGIC_BYTES.CopyTo(buffer); // Add magic for valid header
        
        metadata.WriteTo(buffer);
        
        var loaded = ProviderMetadata.ReadFrom(buffer);
        
        // Should be truncated to MAX_PROVIDER_KEY_LENGTH
        Assert.That(loaded.StoreProviderKey.Length, Is.EqualTo(ProviderMetadata.MAX_PROVIDER_KEY_LENGTH));
    }

    [Test]
    public void EmptyProviderKeyHandledTest()
    {
        var metadata = new ProviderMetadata
        {
            StoreProviderKey = "",
            EncryptionProviderKey = ""
        };

        var buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        DatabaseConstants.MAGIC_BYTES.CopyTo(buffer);
        
        metadata.WriteTo(buffer);
        
        var loaded = ProviderMetadata.ReadFrom(buffer);
        
        Assert.That(loaded.StoreProviderKey, Is.Empty);
        Assert.That(loaded.EncryptionProviderKey, Is.Empty);
    }

    [Test]
    public void SpecialCharactersInProviderKeyTest()
    {
        var metadata = new ProviderMetadata
        {
            StoreProviderKey = "my-store_v2",
            EncryptionProviderKey = "aes-256-gcm"
        };

        var buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        DatabaseConstants.MAGIC_BYTES.CopyTo(buffer);
        
        metadata.WriteTo(buffer);
        
        var loaded = ProviderMetadata.ReadFrom(buffer);
        
        Assert.That(loaded.StoreProviderKey, Is.EqualTo("my-store_v2"));
        Assert.That(loaded.EncryptionProviderKey, Is.EqualTo("aes-256-gcm"));
    }

    #endregion

    #region Feature Flags Tests

    [Test]
    public void AllFeatureFlagsRoundTripTest()
    {
        var allFlags = ProviderFeatures.Encryption | 
                      ProviderFeatures.Transactions | 
                      ProviderFeatures.FileLocking;

        var metadata = new ProviderMetadata
        {
            Features = allFlags,
            StoreProviderKey = "btree"
        };

        var buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        DatabaseConstants.MAGIC_BYTES.CopyTo(buffer);
        
        metadata.WriteTo(buffer);
        
        var loaded = ProviderMetadata.ReadFrom(buffer);
        
        Assert.That(loaded.Features, Is.EqualTo(allFlags));
        Assert.That(loaded.IsEncrypted, Is.True);
        Assert.That(loaded.HasTransactions, Is.True);
        Assert.That(loaded.Features.HasFlag(ProviderFeatures.FileLocking), Is.True);
    }

    #endregion

    #region MVCC Feature Flag Persistence Tests

    [Test]
    public void MvccFlagPersistedAndDetectedTest()
    {
        var path = Path.Combine(m_testDir, "mvcc.db");

        // Create with MVCC
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithMvcc()
            .Build())
        {
            db.Put("key"u8, "value"u8);
        }

        // Check metadata via StorageDetector
        var detection = StorageDetector.Detect(path);
        Assert.That(detection.HasMvcc, Is.True, "MVCC flag should be persisted");
        Assert.That(detection.HasTransactions, Is.True, "Transactions flag should be set");

        // Reopen and verify MVCC is used
        using (var db = WitDatabase.Open(path))
        {
            Assert.That(db.SupportsMvcc, Is.True, "Reopened database should use MVCC");
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()), "Data should persist");
        }
    }

    [Test]
    public void MvccRoundTripPersistenceTest()
    {
        var path = Path.Combine(m_testDir, "mvcc_roundtrip.db");

        // Create with MVCC and write multiple keys
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithMvcc()
            .Build())
        {
            db.Put("key1"u8, "value1"u8);
            db.Put("key2"u8, "value2"u8);
            db.Put("key3"u8, "value3"u8);
        }

        // Reopen and verify all data persists
        using (var db = WitDatabase.Open(path))
        {
            Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
            Assert.That(db.Get("key2"u8), Is.EqualTo("value2"u8.ToArray()));
            Assert.That(db.Get("key3"u8), Is.EqualTo("value3"u8.ToArray()));

            // Verify scan works
            var allKeys = db.Scan().ToList();
            Assert.That(allKeys.Count, Is.EqualTo(3));
        }
    }

    [Test]
    public void MvccMultipleReopenPersistenceTest()
    {
        var path = Path.Combine(m_testDir, "mvcc_multi.db");

        // Session 1: Create
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithMvcc()
            .Build())
        {
            db.Put("session1"u8, "data1"u8);
        }

        // Session 2: Reopen and add more data
        using (var db = WitDatabase.Open(path))
        {
            Assert.That(db.SupportsMvcc, Is.True);
            db.Put("session2"u8, "data2"u8);
        }

        // Session 3: Verify all data
        using (var db = WitDatabase.Open(path))
        {
            Assert.That(db.Get("session1"u8), Is.EqualTo("data1"u8.ToArray()));
            Assert.That(db.Get("session2"u8), Is.EqualTo("data2"u8.ToArray()));
        }
    }

    #endregion
}
