using NUnit.Framework;
using OutWit.Database.Core.BouncyCastle;
using OutWit.Database.Core.Builder;

namespace OutWit.Database.Core.Tests.Builder;

[TestFixture]
public class WitDatabaseBuilderBouncyCastleTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDB_BC_{Guid.NewGuid():N}");
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

    #region Memory Storage + BouncyCastle Tests

    [Test]
    public void MemoryBTreeWithBouncyCastleEncryptionTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithBouncyCastleEncryption(key)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        var value = db.Get("secret"u8);

        Assert.That(value, Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void MemoryBTreeWithBouncyCastleEncryptionAndTransactionsTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithBouncyCastleEncryption(key)
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

    [Test]
    public void MemoryBTreeWithBouncyCastleEncryptionAndCustomSaltTest()
    {
        var key = new byte[32];
        var salt = new byte[16];
        new Random(42).NextBytes(key);
        new Random(123).NextBytes(salt);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithBouncyCastleEncryption(key, salt)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void MemoryBTreeWithBouncyCastlePasswordEncryptionTest()
    {
        var salt = new byte[16];
        new Random(123).NextBytes(salt);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithBouncyCastleEncryption("my-secure-password", salt)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    #endregion

    #region File Storage + BouncyCastle Tests

    [Test]
    public void FileBTreeWithBouncyCastleEncryptionTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var dbPath = Path.Combine(m_testDir, "bc_enc.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithBouncyCastleEncryption(key)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void FileBTreeWithBouncyCastleEncryptionAndTransactionsTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var dbPath = Path.Combine(m_testDir, "bc_enc_tx.db");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithBouncyCastleEncryption(key)
            .WithTransactions()
            .Build();

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Put("key2"u8, "value2"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
        Assert.That(db.Get("key2"u8), Is.EqualTo("value2"u8.ToArray()));
    }

    #endregion

    #region LSM-Tree + BouncyCastle Tests

    [Test]
    public void LsmTreeWithBouncyCastleEncryptionTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var lsmDir = Path.Combine(m_testDir, "lsm_bc");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithBouncyCastleEncryption(key)
            .WithoutTransactions()
            .Build();

        db.Put("secret"u8, "classified"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("classified"u8.ToArray()));
    }

    [Test]
    public void LsmTreeWithBouncyCastleEncryptionAndTransactionsTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var lsmDir = Path.Combine(m_testDir, "lsm_bc_tx");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithBouncyCastleEncryption(key)
            .WithTransactions()
            .Build();

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key1"u8, "value1"u8);
            tx.Commit();
        }

        Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()));
    }

    #endregion

    #region Validation Tests

    [Test]
    public void WithBouncyCastleEncryption_InvalidKeySize_ThrowsTest()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            new WitDatabaseBuilder()
                .WithBouncyCastleEncryption(new byte[16]); // Wrong size
        });
    }

    [Test]
    public void WithBouncyCastleEncryption_InvalidSaltSize_ThrowsTest()
    {
        var key = new byte[32];
        
        Assert.Throws<ArgumentException>(() =>
        {
            new WitDatabaseBuilder()
                .WithBouncyCastleEncryption(key, new byte[4]); // Too short
        });
    }

    [Test]
    public void WithBouncyCastleEncryption_EmptyPassword_ThrowsTest()
    {
        var salt = new byte[16];
        
        Assert.Throws<ArgumentException>(() =>
        {
            new WitDatabaseBuilder()
                .WithBouncyCastleEncryption("", salt);
        });
    }

    [Test]
    public void WithBouncyCastleEncryption_TooFewIterations_ThrowsTest()
    {
        var salt = new byte[16];
        
        Assert.Throws<ArgumentException>(() =>
        {
            new WitDatabaseBuilder()
                .WithBouncyCastleEncryption("password", salt, 100); // Too few
        });
    }

    #endregion

    #region Interoperability Tests

    [Test]
    public void MultipleOperations_WithBouncyCastleEncryption_WorksTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithBouncyCastleEncryption(key)
            .WithoutTransactions()
            .Build();

        // Put multiple values
        for (int i = 0; i < 100; i++)
        {
            db.Put(System.Text.Encoding.UTF8.GetBytes($"key{i}"), 
                   System.Text.Encoding.UTF8.GetBytes($"value{i}"));
        }

        // Verify all values
        for (int i = 0; i < 100; i++)
        {
            var value = db.Get(System.Text.Encoding.UTF8.GetBytes($"key{i}"));
            Assert.That(value, Is.EqualTo(System.Text.Encoding.UTF8.GetBytes($"value{i}")));
        }

        // Delete some values
        for (int i = 0; i < 50; i++)
        {
            db.Delete(System.Text.Encoding.UTF8.GetBytes($"key{i}"));
        }

        // Verify deletions
        for (int i = 0; i < 50; i++)
        {
            Assert.That(db.Get(System.Text.Encoding.UTF8.GetBytes($"key{i}")), Is.Null);
        }

        // Remaining values still exist
        for (int i = 50; i < 100; i++)
        {
            var value = db.Get(System.Text.Encoding.UTF8.GetBytes($"key{i}"));
            Assert.That(value, Is.Not.Null);
        }
    }

    [Test]
    public void Scan_WithBouncyCastleEncryption_WorksTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithBouncyCastleEncryption(key)
            .WithoutTransactions()
            .Build();

        db.Put("a"u8, "1"u8);
        db.Put("b"u8, "2"u8);
        db.Put("c"u8, "3"u8);

        var results = db.Scan().ToList();

        Assert.That(results, Has.Count.EqualTo(3));
    }

    #endregion
}
