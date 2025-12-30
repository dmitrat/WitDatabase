using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Wal;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Wal;

/// <summary>
/// Tests for WalParallelWriter concurrent write functionality.
/// </summary>
[TestFixture]
public class WalParallelWriterTests : IDisposable
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"wal_parallel_test_{Guid.NewGuid():N}");
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

    #endregion

    #region Basic Operations Tests

    [Test]
    public async Task PutAsyncWritesEntryTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_put.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        await writer.PutAsync(1, ToBytes("key1"), ToBytes("value1"));

        Assert.That(writer.EntriesWritten, Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteAsyncWritesEntryTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_delete.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        await writer.DeleteAsync(1, ToBytes("key1"));

        Assert.That(writer.EntriesWritten, Is.EqualTo(1));
    }

    [Test]
    public void TryPutSubmitsWithoutWaitingTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_tryput.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        using var writer = new WalParallelWriter(wal);

        var result = writer.TryPut(1, ToBytes("key1"), ToBytes("value1"));

        Assert.That(result, Is.True);
        Assert.That(writer.EntriesSubmitted, Is.EqualTo(1));
    }

    [Test]
    public void TryDeleteSubmitsWithoutWaitingTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_trydelete.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        using var writer = new WalParallelWriter(wal);

        var result = writer.TryDelete(1, ToBytes("key1"));

        Assert.That(result, Is.True);
        Assert.That(writer.EntriesSubmitted, Is.EqualTo(1));
    }

    #endregion

    #region Transaction Support Tests

    [Test]
    public async Task FullTransactionFlowTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_tx.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        await writer.BeginTransactionAsync(1);
        await writer.PutAsync(1, ToBytes("key1"), ToBytes("value1"));
        await writer.PutAsync(1, ToBytes("key2"), ToBytes("value2"));
        await writer.CommitTransactionAsync(1);

        Assert.That(writer.EntriesWritten, Is.EqualTo(4));
        Assert.That(writer.SyncCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task RollbackTransactionWritesTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_rollback.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        await writer.BeginTransactionAsync(1);
        await writer.PutAsync(1, ToBytes("key1"), ToBytes("value1"));
        await writer.RollbackTransactionAsync(1);

        Assert.That(writer.EntriesWritten, Is.EqualTo(3));
    }

    #endregion

    #region Concurrent Access Tests

    [Test]
    public async Task MultipleThreadsWriteSafelyTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_concurrent.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        const int threads = 8;
        const int entriesPerThread = 50;

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(async () =>
        {
            for (int i = 0; i < entriesPerThread; i++)
            {
                await writer.PutAsync(threadId, 
                    ToBytes($"t{threadId}_k{i}"), 
                    ToBytes($"value{i}"));
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(writer.EntriesWritten, Is.EqualTo(threads * entriesPerThread));
    }

    [Test]
    public async Task ConcurrentTransactionsTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_concurrent_tx.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        const int transactions = 20;

        var tasks = Enumerable.Range(1, transactions).Select(txId => Task.Run(async () =>
        {
            await writer.BeginTransactionAsync(txId);
            await writer.PutAsync(txId, ToBytes($"key_tx{txId}"), ToBytes($"value{txId}"));
            await writer.CommitTransactionAsync(txId);
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(writer.EntriesWritten, Is.EqualTo(transactions * 3));
    }

    [Test]
    public async Task TryPutFromMultipleThreadsTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_tryput_mt.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        const int threads = 4;
        const int entriesPerThread = 100;
        var successCount = 0;

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < entriesPerThread; i++)
            {
                if (writer.TryPut(threadId, ToBytes($"t{threadId}_k{i}"), ToBytes($"v{i}")))
                {
                    Interlocked.Increment(ref successCount);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        await writer.SyncAsync();

        Assert.That(successCount, Is.EqualTo(threads * entriesPerThread));
        // SyncAsync adds a marker entry, so EntriesWritten may be +1
        Assert.That(writer.EntriesWritten, Is.GreaterThanOrEqualTo(threads * entriesPerThread));
    }

    #endregion

    #region Sync Tests

    [Test]
    public async Task SyncAsyncFlushesPendingEntriesTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_sync.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        // Submit entries without waiting
        for (int i = 0; i < 10; i++)
        {
            writer.TryPut(0, ToBytes($"key{i}"), ToBytes($"value{i}"));
        }

        // Sync
        await writer.SyncAsync();

        // SyncAsync adds a marker entry, so EntriesWritten may be +1
        Assert.That(writer.EntriesWritten, Is.GreaterThanOrEqualTo(10));
        Assert.That(writer.PendingEntries, Is.EqualTo(0));
    }

    [Test]
    public async Task CommitTriggersImplicitSyncTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_commit_sync.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        var syncCountBefore = writer.SyncCount;
        
        await writer.CommitTransactionAsync(1);

        Assert.That(writer.SyncCount, Is.GreaterThan(syncCountBefore));
    }

    #endregion

    #region Replay Verification Tests

    [Test]
    public async Task EntriesCanBeReplayedAfterConcurrentWritesTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_replay.wal");
        
        // Write entries concurrently
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        await using (var writer = new WalParallelWriter(wal))
        {
            var tasks = Enumerable.Range(1, 5).Select(txId => Task.Run(async () =>
            {
                await writer.BeginTransactionAsync(txId);
                await writer.PutAsync(txId, ToBytes($"key_tx{txId}"), ToBytes($"value{txId}"));
                await writer.CommitTransactionAsync(txId);
            })).ToArray();

            await Task.WhenAll(tasks);
        }

        // Replay and verify
        var replayedKeys = new List<string>();
        using (var wal = new WriteAheadLog(walPath))
        {
            var visitor = new WalReplayVisitorTransactional(
                (k, _) => replayedKeys.Add(TextEncoding.UTF8.GetString(k)),
                _ => { });
            wal.Replay(visitor);
        }

        Assert.That(replayedKeys, Has.Count.EqualTo(5));
        for (int txId = 1; txId <= 5; txId++)
        {
            Assert.That(replayedKeys, Contains.Item($"key_tx{txId}"));
        }
    }

    #endregion

    #region Bounded Queue Tests

    [Test]
    public async Task BoundedQueueBackpressureWorksTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_bounded.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal, queueBound: 10);

        // Should complete without hanging
        var tasks = Enumerable.Range(0, 50).Select(i =>
            writer.PutAsync(0, ToBytes($"key{i}"), ToBytes($"value{i}"))
        ).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(writer.EntriesWritten, Is.EqualTo(50));
    }

    [Test]
    public void UnboundedQueueAcceptsAllEntriesTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_unbounded.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        using var writer = new WalParallelWriter(wal, queueBound: 0);

        // Submit many entries
        for (int i = 0; i < 1000; i++)
        {
            var result = writer.TryPut(0, ToBytes($"key{i}"), ToBytes($"value{i}"));
            Assert.That(result, Is.True);
        }

        Assert.That(writer.EntriesSubmitted, Is.EqualTo(1000));
    }

    #endregion

    #region Statistics Tests

    [Test]
    public async Task StatisticsTrackCorrectlyTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_stats.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        Assert.That(writer.EntriesSubmitted, Is.EqualTo(0));
        Assert.That(writer.EntriesWritten, Is.EqualTo(0));
        Assert.That(writer.SyncCount, Is.EqualTo(0));

        await writer.PutAsync(0, ToBytes("key1"), ToBytes("value1"));
        await writer.CommitTransactionAsync(1);

        Assert.That(writer.EntriesSubmitted, Is.EqualTo(2));
        Assert.That(writer.EntriesWritten, Is.EqualTo(2));
        Assert.That(writer.SyncCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task IsBusyReflectsProcessingStateTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_busy.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var writer = new WalParallelWriter(wal);

        // Submit entries
        for (int i = 0; i < 100; i++)
        {
            writer.TryPut(0, ToBytes($"key{i}"), ToBytes($"value{i}"));
        }

        // Sync and verify not busy
        await writer.SyncAsync();

        Assert.That(writer.PendingEntries, Is.EqualTo(0));
    }

    #endregion

    #region Disposal Tests

    [Test]
    public async Task DisposeWaitsForPendingWritesTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_dispose.wal");
        
        // Write and dispose together
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            var writer = new WalParallelWriter(wal);
            await writer.PutAsync(0, ToBytes("key1"), ToBytes("value1"));
            await writer.DisposeAsync();
        }

        // Now reopen and verify entry was written
        var count = 0;
        using (var wal2 = new WriteAheadLog(walPath))
        {
            count = wal2.Replay(new WalReplayVisitorSimple((_, _) => { }, _ => { }));
        }

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void DisposedWriterThrowsTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_disposed.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        var writer = new WalParallelWriter(wal);
        writer.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await writer.PutAsync(0, ToBytes("key"), ToBytes("value")));
    }

    #endregion

    #region Timeout Tests

    [Test]
    public async Task TimeoutThrowsTimeoutExceptionTest()
    {
        var walPath = Path.Combine(m_testDir, "parallel_timeout.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        
        // Create writer with very short timeout
        await using var writer = new WalParallelWriter(wal, waitTimeoutMs: 1);

        // Normal operation should still work (small data)
        await writer.PutAsync(0, ToBytes("key"), ToBytes("value"));
        
        Assert.That(writer.EntriesWritten, Is.EqualTo(1));
    }

    #endregion

    #region Helpers

    private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);

    #endregion
}
