using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Transactions;
using OutWit.Database.Core.Wal;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Transactions;

/// <summary>
/// Unit tests for TransactionalStore component.
/// Tests transaction management and ACID properties.
/// </summary>
[TestFixture]
public class TransactionalStoreTests : IDisposable
{
    private string m_testDir = null!;
    private IKeyValueStore m_underlyingStore = null!;
    private TransactionalStore m_store = null!;

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"tx_store_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);

        var dbPath = Path.Combine(m_testDir, "test.db");
        var storage = new MemoryStorage();
        m_underlyingStore = new BTreeStore(storage);
        
        var journal = new WalTransactionJournal(Path.Combine(m_testDir, "test.wal"));
        var lockManager = new LockManager(dbPath);
        
        m_store = new TransactionalStore(m_underlyingStore, journal, lockManager);
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        m_store?.Dispose();
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch { }
    }

    private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);

    #region Basic Operations Tests

    [Test]
    public void Put_WithoutTransaction_Succeeds()
    {
        m_store.Put(ToBytes("key1"), ToBytes("value1"));
        
        var result = m_store.Get(ToBytes("key1"));
        Assert.That(result, Is.EqualTo(ToBytes("value1")));
    }

    [Test]
    public void Get_NonExistentKey_ReturnsNull()
    {
        var result = m_store.Get(ToBytes("nonexistent"));
        
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Delete_ExistingKey_Succeeds()
    {
        m_store.Put(ToBytes("key1"), ToBytes("value1"));
        
        var deleted = m_store.Delete(ToBytes("key1"));
        
        Assert.That(deleted, Is.True);
        Assert.That(m_store.Get(ToBytes("key1")), Is.Null);
    }

    [Test]
    public void Flush_PersistsData()
    {
        m_store.Put(ToBytes("key1"), ToBytes("value1"));
        m_store.Flush();
        
        Assert.That(m_store.Get(ToBytes("key1")), Is.EqualTo(ToBytes("value1")));
    }

    #endregion

    #region Transaction Lifecycle Tests

    [Test]
    public void BeginTransaction_ReturnsActiveTransaction()
    {
        using var tx = m_store.BeginTransaction();
        
        Assert.That(tx, Is.Not.Null);
        Assert.That(tx.State, Is.EqualTo(TransactionState.Active));
    }

    [Test]
    public async Task BeginTransactionAsync_ReturnsActiveTransaction()
    {
        await using var tx = await m_store.BeginTransactionAsync();
        
        Assert.That(tx, Is.Not.Null);
        Assert.That(tx.State, Is.EqualTo(TransactionState.Active));
    }

    [Test]
    public void Commit_SetsStateToCommitted()
    {
        using var tx = m_store.BeginTransaction();
        tx.Put(ToBytes("key"), ToBytes("value"));
        tx.Commit();
        
        Assert.That(tx.State, Is.EqualTo(TransactionState.Committed));
    }

    [Test]
    public void Rollback_SetsStateToRolledBack()
    {
        using var tx = m_store.BeginTransaction();
        tx.Put(ToBytes("key"), ToBytes("value"));
        tx.Rollback();
        
        Assert.That(tx.State, Is.EqualTo(TransactionState.RolledBack));
    }

    [Test]
    public void Dispose_RollsBackActiveTransaction()
    {
        ITransaction tx = m_store.BeginTransaction();
        tx.Put(ToBytes("key"), ToBytes("value"));
        tx.Dispose();
        
        Assert.That(tx.State, Is.EqualTo(TransactionState.RolledBack));
        Assert.That(m_store.Get(ToBytes("key")), Is.Null);
    }

    #endregion

    #region Transaction Isolation Tests

    [Test]
    public void Transaction_SeesOwnChanges()
    {
        using var tx = m_store.BeginTransaction();
        
        tx.Put(ToBytes("key"), ToBytes("value"));
        
        var result = tx.Get(ToBytes("key"));
        Assert.That(result, Is.EqualTo(ToBytes("value")));
    }

    [Test]
    public void Transaction_CommitPersistsChanges()
    {
        using var tx = m_store.BeginTransaction();
        tx.Put(ToBytes("key1"), ToBytes("value1"));
        tx.Put(ToBytes("key2"), ToBytes("value2"));
        tx.Commit();
        
        Assert.That(m_store.Get(ToBytes("key1")), Is.EqualTo(ToBytes("value1")));
        Assert.That(m_store.Get(ToBytes("key2")), Is.EqualTo(ToBytes("value2")));
    }

    [Test]
    public void Transaction_RollbackDiscardsChanges()
    {
        m_store.Put(ToBytes("existing"), ToBytes("original"));
        
        using var tx = m_store.BeginTransaction();
        tx.Put(ToBytes("new_key"), ToBytes("new_value"));
        tx.Put(ToBytes("existing"), ToBytes("modified"));
        tx.Rollback();
        
        Assert.That(m_store.Get(ToBytes("new_key")), Is.Null, "New key should not exist");
        Assert.That(m_store.Get(ToBytes("existing")), Is.EqualTo(ToBytes("original")), "Existing key should have original value");
    }

    [Test]
    public void Transaction_DeleteIsRolledBack()
    {
        m_store.Put(ToBytes("key"), ToBytes("value"));
        
        using var tx = m_store.BeginTransaction();
        tx.Delete(ToBytes("key"));
        Assert.That(tx.Get(ToBytes("key")), Is.Null, "Key should be deleted in transaction");
        tx.Rollback();
        
        Assert.That(m_store.Get(ToBytes("key")), Is.EqualTo(ToBytes("value")), "Key should be restored after rollback");
    }

    #endregion

    #region Multiple Operations Tests

    [Test]
    public void Transaction_MultipleOperationsAtomic()
    {
        using var tx = m_store.BeginTransaction();
        
        for (int i = 0; i < 100; i++)
        {
            tx.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));
        }
        
        tx.Commit();
        
        for (int i = 0; i < 100; i++)
        {
            Assert.That(m_store.Get(ToBytes($"key{i}")), Is.Not.Null, $"Key {i} should exist");
        }
    }

    [Test]
    public void Transaction_OverwritesSameKey()
    {
        using var tx = m_store.BeginTransaction();
        
        tx.Put(ToBytes("key"), ToBytes("value1"));
        tx.Put(ToBytes("key"), ToBytes("value2"));
        tx.Put(ToBytes("key"), ToBytes("value3"));
        
        var result = tx.Get(ToBytes("key"));
        Assert.That(result, Is.EqualTo(ToBytes("value3")));
        
        tx.Commit();
        
        Assert.That(m_store.Get(ToBytes("key")), Is.EqualTo(ToBytes("value3")));
    }

    [Test]
    public void Transaction_DeleteThenPut()
    {
        m_store.Put(ToBytes("key"), ToBytes("original"));
        
        using var tx = m_store.BeginTransaction();
        tx.Delete(ToBytes("key"));
        tx.Put(ToBytes("key"), ToBytes("new_value"));
        tx.Commit();
        
        Assert.That(m_store.Get(ToBytes("key")), Is.EqualTo(ToBytes("new_value")));
    }

    [Test]
    public void Transaction_PutThenDelete()
    {
        using var tx = m_store.BeginTransaction();
        tx.Put(ToBytes("key"), ToBytes("value"));
        tx.Delete(ToBytes("key"));
        tx.Commit();
        
        Assert.That(m_store.Get(ToBytes("key")), Is.Null);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public void CommitAfterCommit_ThrowsInvalidOperation()
    {
        using var tx = m_store.BeginTransaction();
        tx.Put(ToBytes("key"), ToBytes("value"));
        tx.Commit();
        
        Assert.Throws<InvalidOperationException>(() => tx.Commit());
    }

    [Test]
    public void RollbackAfterCommit_ThrowsInvalidOperation()
    {
        using var tx = m_store.BeginTransaction();
        tx.Put(ToBytes("key"), ToBytes("value"));
        tx.Commit();
        
        Assert.Throws<InvalidOperationException>(() => tx.Rollback());
    }

    [Test]
    public void PutAfterCommit_ThrowsInvalidOperation()
    {
        using var tx = m_store.BeginTransaction();
        tx.Commit();
        
        Assert.Throws<InvalidOperationException>(() => tx.Put(ToBytes("key"), ToBytes("value")));
    }

    [Test]
    public void GetAfterCommit_ThrowsInvalidOperation()
    {
        using var tx = m_store.BeginTransaction();
        tx.Commit();
        
        Assert.Throws<InvalidOperationException>(() => tx.Get(ToBytes("key")));
    }

    [Test]
    public void DeleteAfterRollback_ThrowsInvalidOperation()
    {
        using var tx = m_store.BeginTransaction();
        tx.Rollback();
        
        Assert.Throws<InvalidOperationException>(() => tx.Delete(ToBytes("key")));
    }

    #endregion

    #region Async Tests

    [Test]
    public async Task CommitAsync_PersistsChanges()
    {
        await using var tx = await m_store.BeginTransactionAsync();
        await tx.PutAsync(ToBytes("key"), ToBytes("value"));
        await tx.CommitAsync();
        
        Assert.That(tx.State, Is.EqualTo(TransactionState.Committed));
        Assert.That(m_store.Get(ToBytes("key")), Is.EqualTo(ToBytes("value")));
    }

    [Test]
    public async Task RollbackAsync_DiscardsChanges()
    {
        await using var tx = await m_store.BeginTransactionAsync();
        await tx.PutAsync(ToBytes("key"), ToBytes("value"));
        await tx.RollbackAsync();
        
        Assert.That(tx.State, Is.EqualTo(TransactionState.RolledBack));
        Assert.That(m_store.Get(ToBytes("key")), Is.Null);
    }

    [Test]
    public async Task DisposeAsync_RollsBackActiveTransaction()
    {
        ITransaction tx = await m_store.BeginTransactionAsync();
        await tx.PutAsync(ToBytes("key"), ToBytes("value"));
        await tx.DisposeAsync();
        
        Assert.That(tx.State, Is.EqualTo(TransactionState.RolledBack));
    }

    #endregion

    #region Concurrent Transaction Tests

    [Test]
    public async Task ConcurrentTransactionStart_Blocks()
    {
        var shortTimeoutStore = CreateStoreWithTimeout(TimeSpan.FromMilliseconds(200));
        
        using var tx1 = shortTimeoutStore.BeginTransaction();
        
        // Try to start second transaction from different thread - should timeout
        var task = Task.Run(() =>
        {
            Assert.Throws<TimeoutException>(() =>
            {
                using var tx2 = shortTimeoutStore.BeginTransaction();
            });
        });
        
        await task;
        
        shortTimeoutStore.Dispose();
    }

    [Test]
    public void TransactionCompleted_AllowsNext()
    {
        using var tx1 = m_store.BeginTransaction();
        tx1.Put(ToBytes("key1"), ToBytes("value1"));
        tx1.Commit();
        
        using var tx2 = m_store.BeginTransaction();
        tx2.Put(ToBytes("key2"), ToBytes("value2"));
        tx2.Commit();
        
        Assert.That(m_store.Get(ToBytes("key1")), Is.EqualTo(ToBytes("value1")));
        Assert.That(m_store.Get(ToBytes("key2")), Is.EqualTo(ToBytes("value2")));
    }

    #endregion

    #region Scan Tests

    [Test]
    public void Scan_ReturnsAllKeys()
    {
        for (int i = 0; i < 10; i++)
        {
            m_store.Put(ToBytes($"key{i:D2}"), ToBytes($"value{i}"));
        }
        
        var results = m_store.Scan(null, null).ToList();
        
        Assert.That(results.Count, Is.EqualTo(10));
    }

    [Test]
    public void Scan_WithRange_ReturnsFilteredKeys()
    {
        for (int i = 0; i < 10; i++)
        {
            m_store.Put(ToBytes($"key{i:D2}"), ToBytes($"value{i}"));
        }
        
        var results = m_store.Scan(ToBytes("key03"), ToBytes("key07")).ToList();
        
        Assert.That(results.Count, Is.GreaterThanOrEqualTo(4));
    }

    #endregion

    #region Helper Methods

    private TransactionalStore CreateStoreWithTimeout(TimeSpan timeout)
    {
        var subDir = Path.Combine(m_testDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(subDir);
        
        var storage = new MemoryStorage();
        var store = new BTreeStore(storage);
        var journal = new WalTransactionJournal(Path.Combine(subDir, "test.wal"));
        var lockManager = new LockManager(Path.Combine(subDir, "test.db"), timeout);
        
        return new TransactionalStore(store, journal, lockManager);
    }

    #endregion
}
