using NUnit.Framework;
using OutWit.Database.Core.Builder;

namespace OutWit.Database.Core.Tests.Builder;

/// <summary>
/// Tests for database transaction configuration options.
/// </summary>
[TestFixture]
public class TransactionConfigurationTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDB_TxConfig_{Guid.NewGuid():N}");
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

    #region Transaction Enable/Disable Tests

    [Test]
    public void BTreeWithTransactionsSupportsTransactionsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);

        using var tx = db.BeginTransaction();
        tx.Put("key"u8, "value"u8);
        tx.Commit();

        Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
    }

    [Test]
    public void BTreeWithoutTransactionsDoesNotSupportTransactionsTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithoutTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.False);
        Assert.Throws<InvalidOperationException>(() => db.BeginTransaction());
    }

    [Test]
    public void LsmWithTransactionsSupportsTransactionsTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_tx");
        
        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.True);
    }

    [Test]
    public void LsmWithoutTransactionsDoesNotSupportTransactionsTest()
    {
        var lsmDir = Path.Combine(m_testDir, "lsm_no_tx");
        
        using var db = new WitDatabaseBuilder()
            .WithLsmTree(lsmDir)
            .WithoutTransactions()
            .Build();

        Assert.That(db.SupportsTransactions, Is.False);
    }

    #endregion

    #region Transaction Rollback Tests

    [Test]
    public void TransactionRollbackDiscardsChangesTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();

        db.Put("original"u8, "value"u8);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("new"u8, "data"u8);
            tx.Delete("original"u8);
            tx.Rollback();
        }

        Assert.That(db.Get("original"u8), Is.EqualTo("value"u8.ToArray()));
        Assert.That(db.Get("new"u8), Is.Null);
    }

    [Test]
    public void TransactionDisposeWithoutCommitRollsBackTest()
    {
        using var db = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();

        db.Put("key"u8, "original"u8);

        using (var tx = db.BeginTransaction())
        {
            tx.Put("key"u8, "modified"u8);
            // No commit or rollback - dispose should rollback
        }

        Assert.That(db.Get("key"u8), Is.EqualTo("original"u8.ToArray()));
    }

    #endregion
}
