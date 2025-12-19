using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;
using OutWit.Database.Core.Wal;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Transactions;

/// <summary>
/// Stress tests for TransactionalStore.
/// Tests concurrent access patterns and durability under load.
/// Note: These tests use in-memory locking only (no file locks) for reliability.
/// </summary>
[TestFixture]
[Category("Stress")]
public class TransactionalStoreStressTests : IDisposable
{
    private string m_testDir = null!;

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"tx_stress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch { }
    }

    private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);

    /// <summary>
    /// Creates a TransactionalStore without file locking (for in-process tests).
    /// </summary>
    private TransactionalStore CreateStore(string name, TimeSpan? timeout = null)
    {
        var subDir = Path.Combine(m_testDir, name);
        Directory.CreateDirectory(subDir);

        var storage = new MemoryStorage();
        var btree = new BTreeStore(storage);
        var journal = new WalTransactionJournal(Path.Combine(subDir, "test.wal"));
        
        // Use in-memory locking only (no file lock) for reliable stress tests
        var lockManager = new LockManager(timeout ?? TimeSpan.FromSeconds(30));

        return new TransactionalStore(btree, journal, lockManager);
    }

    /// <summary>
    /// Creates a TransactionalStore with file locking (for cross-process scenarios).
    /// </summary>
    private TransactionalStore CreateStoreWithFileLock(string name, TimeSpan? timeout = null)
    {
        var subDir = Path.Combine(m_testDir, name);
        Directory.CreateDirectory(subDir);

        var storage = new MemoryStorage();
        var btree = new BTreeStore(storage);
        var journal = new WalTransactionJournal(Path.Combine(subDir, "test.wal"));
        var lockManager = new LockManager(Path.Combine(subDir, "test.db"), timeout ?? TimeSpan.FromSeconds(30));

        return new TransactionalStore(btree, journal, lockManager);
    }

    #region Sequential Transaction Stress

    [Test]
    public void SequentialTransactions_100Commits()
    {
        using var store = CreateStore("seq_100");

        for (int i = 0; i < 100; i++)
        {
            using var tx = store.BeginTransaction();
            tx.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));
            tx.Commit();
        }

        // Verify all data
        for (int i = 0; i < 100; i++)
        {
            var value = store.Get(ToBytes($"key{i}"));
            Assert.That(value, Is.Not.Null, $"Key {i} should exist");
        }
    }

    [Test]
    public void SequentialTransactions_AlternatingCommitRollback()
    {
        using var store = CreateStore("seq_alt");
        var committedKeys = new List<int>();

        for (int i = 0; i < 100; i++)
        {
            using var tx = store.BeginTransaction();
            tx.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));

            if (i % 2 == 0)
            {
                tx.Commit();
                committedKeys.Add(i);
            }
            else
            {
                tx.Rollback();
            }
        }

        // Verify only committed keys exist
        foreach (var key in committedKeys)
        {
            Assert.That(store.Get(ToBytes($"key{key}")), Is.Not.Null);
        }

        for (int i = 1; i < 100; i += 2)
        {
            Assert.That(store.Get(ToBytes($"key{i}")), Is.Null, $"Rolled back key {i} should not exist");
        }
    }

    [Test]
    public void LargeTransaction_1000Operations()
    {
        using var store = CreateStore("large_tx");

        using var tx = store.BeginTransaction();

        for (int i = 0; i < 1000; i++)
        {
            tx.Put(ToBytes($"key{i:D4}"), ToBytes($"value{i}"));
        }

        tx.Commit();

        // Verify
        Assert.That(store.Get(ToBytes("key0000")), Is.Not.Null);
        Assert.That(store.Get(ToBytes("key0999")), Is.Not.Null);
    }

    [Test]
    public void LargeTransaction_Rollback_1000Operations()
    {
        using var store = CreateStore("large_rb");

        // Pre-populate
        store.Put(ToBytes("existing"), ToBytes("original"));

        using var tx = store.BeginTransaction();

        for (int i = 0; i < 1000; i++)
        {
            tx.Put(ToBytes($"key{i:D4}"), ToBytes($"value{i}"));
        }
        tx.Put(ToBytes("existing"), ToBytes("modified"));

        tx.Rollback();

        // Verify nothing was written
        Assert.That(store.Get(ToBytes("key0000")), Is.Null);
        Assert.That(store.Get(ToBytes("existing")), Is.EqualTo(ToBytes("original")));
    }

    #endregion

    #region Concurrent Read/Write Stress

    [Test]
    [Timeout(30000)] // 30 second timeout
    public async Task ConcurrentReads_WhileWriting()
    {
        using var store = CreateStore("conc_rw", TimeSpan.FromSeconds(10));

        // Pre-populate data
        for (int i = 0; i < 100; i++)
        {
            store.Put(ToBytes($"key{i:D3}"), ToBytes($"value{i}"));
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var readCount = 0;
        var writeCount = 0;
        var errors = new List<Exception>();

        // Writer task - does short transactions
        var writerTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested && writeCount < 20)
                {
                    using var tx = store.BeginTransaction();
                    tx.Put(ToBytes($"new_key{writeCount}"), ToBytes($"new_value{writeCount}"));
                    tx.Commit();
                    Interlocked.Increment(ref writeCount);
                    await Task.Delay(50, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        }, cts.Token);

        // Reader tasks - read existing data
        var readerTasks = Enumerable.Range(0, 3).Select(readerIndex => Task.Run(async () =>
        {
            var random = new Random(readerIndex);
            try
            {
                while (!cts.Token.IsCancellationRequested && writeCount < 20)
                {
                    var key = $"key{random.Next(100):D3}";
                    var value = store.Get(ToBytes(key));
                    if (value != null)
                    {
                        Interlocked.Increment(ref readCount);
                    }
                    await Task.Delay(10, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { } // Expected if writer holds lock
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        }, cts.Token)).ToArray();

        await Task.WhenAll(new[] { writerTask }.Concat(readerTasks));

        // Check for errors
        if (errors.Count > 0)
        {
            throw new AggregateException("Errors during concurrent test", errors);
        }

        Assert.That(writeCount, Is.EqualTo(20), "All writes should complete");
        Assert.That(readCount, Is.GreaterThan(0), "Some reads should complete");

        TestContext.WriteLine($"Writes: {writeCount}, Reads: {readCount}");
    }

    [Test]
    [Timeout(60000)] // 60 second timeout
    public async Task ConcurrentWriters_Serialize()
    {
        using var store = CreateStore("conc_wr", TimeSpan.FromSeconds(30));

        var completedTransactions = 0;
        var concurrentCount = 0;
        var maxConcurrent = 0;

        // Use fully async transactions to avoid sync/async mixing issues
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            await Task.Delay(i * 50); // Stagger starts

            await using var tx = await store.BeginTransactionAsync();

            var current = Interlocked.Increment(ref concurrentCount);
            if (current > maxConcurrent)
                Interlocked.Exchange(ref maxConcurrent, current);

            await tx.PutAsync(ToBytes($"writer{i}"), ToBytes($"value{i}"));
            await Task.Delay(50); // Hold transaction briefly

            Interlocked.Decrement(ref concurrentCount);
            await tx.CommitAsync();
            Interlocked.Increment(ref completedTransactions);
        }).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(completedTransactions, Is.EqualTo(5));
        Assert.That(maxConcurrent, Is.EqualTo(1), "Only one transaction should be active at a time");

        // Verify all data written
        for (int i = 0; i < 5; i++)
        {
            Assert.That(store.Get(ToBytes($"writer{i}")), Is.EqualTo(ToBytes($"value{i}")));
        }
    }

    [Test]
    [Timeout(30000)]
    public async Task ManySequentialTransactions_NoDeadlock()
    {
        using var store = CreateStore("many_tx", TimeSpan.FromSeconds(10));

        var completedCount = 0;
        var tasks = new List<Task>();

        // Start 10 tasks, each doing 10 sequential transactions (fully async)
        for (int t = 0; t < 10; t++)
        {
            var taskId = t;
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await using var tx = await store.BeginTransactionAsync();
                    await tx.PutAsync(ToBytes($"task{taskId}_key{i}"), ToBytes($"value{i}"));
                    await tx.CommitAsync();
                    Interlocked.Increment(ref completedCount);
                    await Task.Yield(); // Give other tasks a chance
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.That(completedCount, Is.EqualTo(100), "All 100 transactions should complete");
        TestContext.WriteLine($"Completed {completedCount} transactions");
    }

    #endregion

    #region Durability Stress

    [Test]
    public void JournalGrowth_ManyTransactions()
    {
        var subDir = Path.Combine(m_testDir, "journal_growth");
        Directory.CreateDirectory(subDir);
        var walPath = Path.Combine(subDir, "test.wal");

        var storage = new MemoryStorage();
        var btree = new BTreeStore(storage);
        var journal = new WalTransactionJournal(walPath);
        var lockManager = new LockManager(); // No file lock

        using var store = new TransactionalStore(btree, journal, lockManager);

        // Many small transactions
        for (int i = 0; i < 100; i++)
        {
            using var tx = store.BeginTransaction();
            tx.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));
            tx.Commit();
        }

        var walSize = new FileInfo(walPath).Length;
        TestContext.WriteLine($"WAL size after 100 transactions: {walSize:N0} bytes");

        // Checkpoint should truncate
        store.Checkpoint();

        var walSizeAfterCheckpoint = new FileInfo(walPath).Length;
        TestContext.WriteLine($"WAL size after checkpoint: {walSizeAfterCheckpoint:N0} bytes");

        Assert.That(walSizeAfterCheckpoint, Is.LessThan(walSize), "Checkpoint should reduce WAL size");
    }

    #endregion

    #region Mixed Operations Stress

    [Test]
    public void MixedOperations_PutDeleteGet()
    {
        using var store = CreateStore("mixed_ops");
        var random = new Random(42);

        // Phase 1: Populate
        for (int i = 0; i < 200; i++)
        {
            store.Put(ToBytes($"key{i:D3}"), ToBytes($"value{i}"));
        }

        // Phase 2: Mixed operations in transactions
        for (int round = 0; round < 20; round++)
        {
            using var tx = store.BeginTransaction();

            // Random puts
            for (int j = 0; j < 5; j++)
            {
                var key = random.Next(200);
                tx.Put(ToBytes($"key{key:D3}"), ToBytes($"updated_{round}_{j}"));
            }

            // Random deletes
            for (int j = 0; j < 2; j++)
            {
                var key = random.Next(200);
                tx.Delete(ToBytes($"key{key:D3}"));
            }

            // Randomly commit or rollback
            if (random.Next(10) > 2) // 70% commit
            {
                tx.Commit();
            }
            else
            {
                tx.Rollback();
            }
        }

        // Verify we can still read
        var existingCount = 0;
        for (int i = 0; i < 200; i++)
        {
            if (store.Get(ToBytes($"key{i:D3}")) != null)
                existingCount++;
        }

        TestContext.WriteLine($"Remaining keys: {existingCount}/200");
        Assert.That(existingCount, Is.GreaterThan(0));
    }

    [Test]
    public void ReadYourOwnWrites_InTransaction()
    {
        using var store = CreateStore("read_own");

        using var tx = store.BeginTransaction();

        // Write and immediately read back
        for (int i = 0; i < 100; i++)
        {
            var key = ToBytes($"key{i}");
            var value = ToBytes($"value{i}");

            tx.Put(key, value);

            var readBack = tx.Get(key);
            Assert.That(readBack, Is.EqualTo(value), $"Should read own write for key {i}");
        }

        // Delete and verify
        for (int i = 0; i < 50; i++)
        {
            var key = ToBytes($"key{i}");
            tx.Delete(key);

            var readBack = tx.Get(key);
            Assert.That(readBack, Is.Null, $"Should see delete for key {i}");
        }

        tx.Commit();
    }

    #endregion

    #region Async Stress Tests

    [Test]
    public async Task AsyncTransactions_Sequential()
    {
        using var store = CreateStore("async_seq");

        for (int i = 0; i < 50; i++)
        {
            await using var tx = await store.BeginTransactionAsync();
            await tx.PutAsync(ToBytes($"key{i}"), ToBytes($"value{i}"));
            await tx.CommitAsync();
        }

        // Verify
        for (int i = 0; i < 50; i++)
        {
            Assert.That(store.Get(ToBytes($"key{i}")), Is.Not.Null);
        }
    }

    [Test]
    [Timeout(30000)]
    public async Task AsyncTransactions_Concurrent()
    {
        using var store = CreateStore("async_conc", TimeSpan.FromSeconds(30));

        var completedCount = 0;

        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await Task.Delay(i * 20); // Stagger starts
            
            await using var tx = await store.BeginTransactionAsync();
            await tx.PutAsync(ToBytes($"async_key{i}"), ToBytes($"async_value{i}"));
            await tx.CommitAsync();
            
            Interlocked.Increment(ref completedCount);
        }).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(completedCount, Is.EqualTo(10));

        // Verify all data
        for (int i = 0; i < 10; i++)
        {
            Assert.That(store.Get(ToBytes($"async_key{i}")), Is.Not.Null);
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    public void EmptyTransaction_CommitSucceeds()
    {
        using var store = CreateStore("empty_tx");

        using var tx = store.BeginTransaction();
        // No operations
        tx.Commit();

        Assert.That(tx.State, Is.EqualTo(TransactionState.Committed));
    }

    [Test]
    public void EmptyTransaction_RollbackSucceeds()
    {
        using var store = CreateStore("empty_rb");

        using var tx = store.BeginTransaction();
        // No operations
        tx.Rollback();

        Assert.That(tx.State, Is.EqualTo(TransactionState.RolledBack));
    }

    [Test]
    public async Task TransactionTimeout_ThrowsOnConflict()
    {
        using var store = CreateStore("timeout", TimeSpan.FromMilliseconds(100));

        using var tx1 = store.BeginTransaction();

        // Try to start second transaction from different thread - should timeout
        var task = Task.Run(() =>
        {
            Assert.Throws<TimeoutException>(() =>
            {
                using var tx2 = store.BeginTransaction();
            });
        });
        
        await task;
    }

    [Test]
    public void LargeValues_InTransaction()
    {
        using var store = CreateStore("large_val");
        var random = new Random(42);

        using var tx = store.BeginTransaction();

        // 10KB values
        for (int i = 0; i < 10; i++)
        {
            var value = new byte[10 * 1024];
            random.NextBytes(value);
            tx.Put(ToBytes($"large{i}"), value);
        }

        tx.Commit();

        // Verify
        for (int i = 0; i < 10; i++)
        {
            var value = store.Get(ToBytes($"large{i}"));
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.Length, Is.EqualTo(10 * 1024));
        }
    }

    #endregion
}
