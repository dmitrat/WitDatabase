using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Wal;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Wal;

/// <summary>
/// Tests for WalBatchCommitter group commit functionality.
/// </summary>
[TestFixture]
public class WalBatchCommitterTests : IDisposable
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"wal_batch_test_{Guid.NewGuid():N}");
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
    public async Task SubmitPutWritesEntryTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_put.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal);

        var result = await committer.SubmitPutAsync(1, ToBytes("key1"), ToBytes("value1"));
        await committer.FlushAsync();

        Assert.That(result, Is.True);
        Assert.That(committer.EntriesWritten, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task SubmitDeleteWritesEntryTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_delete.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal);

        await committer.SubmitDeleteAsync(1, ToBytes("key1"));
        await committer.FlushAsync();

        Assert.That(committer.EntriesWritten, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task SubmitTransactionMarkersWritesTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_tx.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal);

        await committer.SubmitBeginTransactionAsync(1);
        await committer.SubmitPutAsync(1, ToBytes("key1"), ToBytes("value1"));
        await committer.SubmitCommitTransactionAsync(1);

        // Wait for completion
        await Task.Delay(100);

        Assert.That(committer.EntriesWritten, Is.EqualTo(3));
    }

    #endregion

    #region Batching Tests

    [Test]
    public async Task MultiplePutsAreBatchedTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_multiple.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal, maxBatchSize: 10, batchTimeoutMs: 100);

        // Submit multiple entries quickly
        var tasks = new List<ValueTask<bool>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(committer.SubmitPutAsync(0, ToBytes($"key{i}"), ToBytes($"value{i}")));
        }

        // Wait for all
        foreach (var task in tasks)
        {
            await task;
        }

        // Should have batched some entries together
        Assert.That(committer.EntriesWritten, Is.EqualTo(10));
        Assert.That(committer.BatchesCommitted, Is.LessThanOrEqualTo(10));
    }

    [Test]
    public async Task AverageEntriesPerBatchCalculatesCorrectlyTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_avg.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal, maxBatchSize: 50, batchTimeoutMs: 50);

        // Submit entries in bursts
        for (int batch = 0; batch < 3; batch++)
        {
            var tasks = new List<ValueTask<bool>>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(committer.SubmitPutAsync(0, ToBytes($"b{batch}_k{i}"), ToBytes($"v{i}")));
            }
            foreach (var task in tasks)
            {
                await task;
            }
            await Task.Delay(100); // Let batch complete
        }

        Assert.That(committer.EntriesWritten, Is.EqualTo(60));
        Assert.That(committer.AverageEntriesPerBatch, Is.GreaterThan(1));
    }

    #endregion

    #region Concurrent Access Tests

    [Test]
    public async Task ConcurrentSubmitsSafeTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_concurrent.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal);

        const int threads = 4;
        const int entriesPerThread = 100;

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(async () =>
        {
            for (int i = 0; i < entriesPerThread; i++)
            {
                await committer.SubmitPutAsync(threadId, 
                    ToBytes($"t{threadId}_k{i}"), 
                    ToBytes($"value{i}"));
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        await committer.FlushAsync();

        // FlushAsync may add a marker entry
        Assert.That(committer.EntriesWritten, Is.GreaterThanOrEqualTo(threads * entriesPerThread));
    }

    [Test]
    public async Task ConcurrentTransactionsSafeTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_concurrent_tx.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal);

        const int transactions = 10;

        var tasks = Enumerable.Range(1, transactions).Select(txId => Task.Run(async () =>
        {
            await committer.SubmitBeginTransactionAsync(txId);
            await committer.SubmitPutAsync(txId, ToBytes($"key_tx{txId}"), ToBytes($"value{txId}"));
            await committer.SubmitCommitTransactionAsync(txId);
        })).ToArray();

        await Task.WhenAll(tasks);

        // Each transaction has 3 entries: begin, put, commit
        Assert.That(committer.EntriesWritten, Is.EqualTo(transactions * 3));
    }

    #endregion

    #region Replay Verification Tests

    [Test]
    public async Task BatchedEntriesCanBeReplayedTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_replay.wal");
        
        // Write entries
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        await using (var committer = new WalBatchCommitter(wal))
        {
            await committer.SubmitBeginTransactionAsync(1);
            await committer.SubmitPutAsync(1, ToBytes("key1"), ToBytes("value1"));
            await committer.SubmitPutAsync(1, ToBytes("key2"), ToBytes("value2"));
            await committer.SubmitCommitTransactionAsync(1);
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

        Assert.That(replayedKeys, Has.Count.EqualTo(2));
        Assert.That(replayedKeys, Contains.Item("key1"));
        Assert.That(replayedKeys, Contains.Item("key2"));
    }

    #endregion

    #region Statistics Tests

    [Test]
    public async Task StatisticsTrackCorrectlyTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_stats.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal);

        Assert.That(committer.EntriesWritten, Is.EqualTo(0));
        Assert.That(committer.BatchesCommitted, Is.EqualTo(0));

        await committer.SubmitPutAsync(0, ToBytes("key1"), ToBytes("value1"));
        await committer.FlushAsync();

        Assert.That(committer.EntriesWritten, Is.GreaterThanOrEqualTo(1));
        Assert.That(committer.BatchesCommitted, Is.GreaterThanOrEqualTo(1));
        Assert.That(committer.AverageBatchLatencyMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task PendingEntriesTracksQueueDepthTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_pending.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        await using var committer = new WalBatchCommitter(wal, batchTimeoutMs: 1000); // Long timeout

        // Submit without waiting
        for (int i = 0; i < 5; i++)
        {
            _ = committer.SubmitPutAsync(0, ToBytes($"key{i}"), ToBytes($"value{i}"));
        }

        // Wait for processing
        await committer.FlushAsync();

        // After flush, pending should be 0 (or -1 if not supported)
        var pending = committer.PendingEntries;
        Assert.That(pending, Is.LessThanOrEqualTo(0));
    }

    #endregion

    #region Disposal Tests

    [Test]
    public async Task DisposeFlushesRemainingEntriesTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_dispose.wal");
        
        // Write entry and dispose committer and WAL together
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            var committer = new WalBatchCommitter(wal);
            await committer.SubmitPutAsync(0, ToBytes("key1"), ToBytes("value1"));
            await committer.DisposeAsync();
        }

        // Now reopen and verify entry was written
        var count = 0;
        using (var wal2 = new WriteAheadLog(walPath))
        {
            count = wal2.Replay(new WalReplayVisitorSimple((_, _) => { }, _ => { }));
        }

        Assert.That(count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void DisposedCommitterThrowsTest()
    {
        var walPath = Path.Combine(m_testDir, "batch_disposed.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        var committer = new WalBatchCommitter(wal);
        committer.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await committer.SubmitPutAsync(0, ToBytes("key"), ToBytes("value")));
    }

    #endregion

    #region Helpers

    private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);

    #endregion
}
