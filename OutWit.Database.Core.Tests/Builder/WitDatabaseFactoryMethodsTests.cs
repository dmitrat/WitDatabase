using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Tests.Builder;

/// <summary>
/// Tests for WitDatabase static factory methods (Create, Open, CreateOrOpen, CreateInMemory).
/// </summary>
[TestFixture]
public class WitDatabaseFactoryMethodsTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDB_Factory_{Guid.NewGuid():N}");
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

    #region Create Tests

    [Test]
    public void CreateNewDatabaseWorksTest()
    {
        var path = Path.Combine(m_testDir, "create.db");
        
        using var db = WitDatabase.Create(path);
        
        Assert.That(db.SupportsTransactions, Is.True);
        db.Put("key"u8, "value"u8);
        Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    [Test]
    public void CreateExistingFileThrowsTest()
    {
        var path = Path.Combine(m_testDir, "existing.db");
        File.WriteAllBytes(path, [1, 2, 3]);
        
        Assert.Throws<IOException>(() => WitDatabase.Create(path));
    }

    [Test]
    public void CreateWithPasswordWorksTest()
    {
        var path = Path.Combine(m_testDir, "encrypted_create.db");
        
        using (var db = WitDatabase.Create(path, "password"))
        {
            db.Put("secret"u8, "data"u8);
        }
        
        using (var db = WitDatabase.Open(path, "password"))
        {
            Assert.That(db.Get("secret"u8), Is.EqualTo("data"u8.ToArray()));
        }
    }

    #endregion

    #region Open Tests

    [Test]
    public void OpenExistingDatabaseWorksTest()
    {
        var path = Path.Combine(m_testDir, "open.db");
        
        using (var db = WitDatabase.Create(path))
        {
            db.Put("key"u8, "value"u8);
        }
        
        using (var db = WitDatabase.Open(path))
        {
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
    }

    [Test]
    public void OpenNonExistentFileThrowsTest()
    {
        var path = Path.Combine(m_testDir, "nonexistent.db");
        
        Assert.Throws<FileNotFoundException>(() => WitDatabase.Open(path));
    }

    #endregion

    #region CreateOrOpen Tests

    [Test]
    public void CreateOrOpenCreatesNewTest()
    {
        var path = Path.Combine(m_testDir, "createoropen.db");
        
        using var db = WitDatabase.CreateOrOpen(path);
        
        db.Put("key"u8, "value"u8);
        Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    [Test]
    public void CreateOrOpenOpensExistingTest()
    {
        var path = Path.Combine(m_testDir, "createoropen2.db");
        
        using (var db = WitDatabase.Create(path))
        {
            db.Put("key"u8, "value"u8);
        }
        
        using (var db = WitDatabase.CreateOrOpen(path))
        {
            Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
        }
    }

    #endregion

    #region CreateInMemory Tests

    [Test]
    public void CreateInMemoryWorksTest()
    {
        using var db = WitDatabase.CreateInMemory();
        
        Assert.That(db.SupportsTransactions, Is.True);
        db.Put("key"u8, "value"u8);
        Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    [Test]
    public void CreateInMemoryWithPasswordWorksTest()
    {
        using var db = WitDatabase.CreateInMemory("password");
        
        Assert.That(db.SupportsTransactions, Is.True);
        db.Put("secret"u8, "data"u8);
        Assert.That(db.Get("secret"u8), Is.EqualTo("data"u8.ToArray()));
    }

    #endregion

    #region BeginTransaction with IsolationLevel Tests

    [Test]
    public void BeginTransactionWithDefaultIsolationLevelTest()
    {
        using var db = WitDatabase.CreateInMemory();
        
        using var tx = db.BeginTransaction();
        
        Assert.That(tx.IsolationLevel, Is.EqualTo(IsolationLevel.ReadCommitted));
        tx.Put("key"u8, "value"u8);
        tx.Commit();
    }

    [Test]
    public void BeginTransactionWithSerializableIsolationLevelTest()
    {
        using var db = WitDatabase.CreateInMemory();
        
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        
        Assert.That(tx.IsolationLevel, Is.EqualTo(IsolationLevel.Serializable));
        tx.Put("key"u8, "value"u8);
        tx.Commit();
        
        Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    [Test]
    public void BeginTransactionWithSnapshotIsolationLevelTest()
    {
        using var db = WitDatabase.CreateInMemory();
        
        using var tx = db.BeginTransaction(IsolationLevel.Snapshot);
        
        Assert.That(tx.IsolationLevel, Is.EqualTo(IsolationLevel.Snapshot));
        tx.Rollback();
    }

    [Test]
    public void BeginTransactionWithReadUncommittedIsolationLevelTest()
    {
        using var db = WitDatabase.CreateInMemory();
        
        using var tx = db.BeginTransaction(IsolationLevel.ReadUncommitted);
        
        Assert.That(tx.IsolationLevel, Is.EqualTo(IsolationLevel.ReadUncommitted));
        tx.Rollback();
    }

    [Test]
    public async Task BeginTransactionAsyncWithIsolationLevelTest()
    {
        using var db = WitDatabase.CreateInMemory();
        
        using var tx = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        
        Assert.That(tx.IsolationLevel, Is.EqualTo(IsolationLevel.RepeatableRead));
        await tx.RollbackAsync();
    }

    [Test]
    public void BeginTransactionWithIsolationLevelWhenDisabledThrowsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithoutTransactions()
            .Build();
        
        Assert.Throws<InvalidOperationException>(() => 
            db.BeginTransaction(IsolationLevel.Serializable));
    }

    [Test]
    public async Task BeginTransactionAsyncWithIsolationLevelWhenDisabledThrowsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithoutTransactions()
            .Build();
        
        Assert.ThrowsAsync<InvalidOperationException>(async () => 
            await db.BeginTransactionAsync(IsolationLevel.Serializable));
    }

    #endregion
}
