using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Wal;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Wal;

/// <summary>
/// Unit tests for unified WriteAheadLog (transactional) component.
/// Tests CRC32 integrity, encryption, and transaction support.
/// </summary>
[TestFixture]
public class WriteAheadLogTests : IDisposable
{
    private string m_testDir = null!;

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"unified_wal_test_{Guid.NewGuid():N}");
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

    #region Basic Operations Tests

    [Test]
    public void AppendPutIncreasesSizeTest()
    {
        var walPath = Path.Combine(m_testDir, "basic.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        var initialSize = wal.Size;
        
        wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
        
        Assert.That(wal.Size, Is.GreaterThan(initialSize));
        Assert.That(wal.EntryCount, Is.EqualTo(1));
    }

    [Test]
    public void AppendDeleteIncreasesSizeTest()
    {
        var walPath = Path.Combine(m_testDir, "delete.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        var initialSize = wal.Size;
        
        wal.AppendDelete(ToBytes("key1"));
        
        Assert.That(wal.Size, Is.GreaterThan(initialSize));
    }

    [Test]
    public void SyncFlushesToDiskTest()
    {
        var walPath = Path.Combine(m_testDir, "sync.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
        wal.Sync();
        
        // File should exist and have content
        Assert.That(File.Exists(walPath), Is.True);
        Assert.That(new FileInfo(walPath).Length, Is.GreaterThan(0));
    }

    [Test]
    public void TruncateResetsSizeTest()
    {
        var walPath = Path.Combine(m_testDir, "truncate.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
        wal.AppendPut(ToBytes("key2"), ToBytes("value2"));
        wal.Sync();
        
        Assert.That(wal.Size, Is.GreaterThan(16)); // More than header
        
        wal.Truncate();
        
        Assert.That(wal.Size, Is.EqualTo(16)); // Just header (Magic + Version + EntryCounter)
        Assert.That(wal.EntryCount, Is.EqualTo(0));
    }

    #endregion

    #region Replay Tests

    [Test]
    public void ReplayReturnsAllEntriesTest()
    {
        var walPath = Path.Combine(m_testDir, "replay.wal");
        
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
            wal.AppendPut(ToBytes("key2"), ToBytes("value2"));
            wal.AppendDelete(ToBytes("key1"));
            wal.Sync();
        }

        var puts = new List<(byte[] Key, byte[] Value)>();
        var deletes = new List<byte[]>();
        
        using (var wal = new WriteAheadLog(walPath))
        {
            var visitor = new WalReplayVisitorSimple(
                (k, v) => puts.Add((k, v)),
                k => deletes.Add(k));
            
            var count = wal.Replay(visitor);
            
            Assert.That(count, Is.EqualTo(3));
        }

        Assert.That(puts.Count, Is.EqualTo(2));
        Assert.That(deletes.Count, Is.EqualTo(1));
    }

    [Test]
    public void ReplayPreservesOrderTest()
    {
        var walPath = Path.Combine(m_testDir, "order.wal");
        
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            for (int i = 0; i < 100; i++)
            {
                wal.AppendPut(ToBytes($"key{i:D3}"), ToBytes($"value{i}"));
            }
            wal.Sync();
        }

        var keys = new List<string>();
        
        using (var wal = new WriteAheadLog(walPath))
        {
            wal.Replay(new WalReplayVisitorSimple(
                (k, _) => keys.Add(TextEncoding.UTF8.GetString(k)),
                _ => { }));
        }

        Assert.That(keys.Count, Is.EqualTo(100));
        for (int i = 0; i < 100; i++)
        {
            Assert.That(keys[i], Is.EqualTo($"key{i:D3}"));
        }
    }

    #endregion

    #region Transaction Support Tests

    [Test]
    public void TransactionMarkersRecordedTest()
    {
        var walPath = Path.Combine(m_testDir, "tx_markers.wal");
        
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            wal.AppendBeginTransaction(1);
            wal.AppendPut(ToBytes("key1"), ToBytes("value1"), 1);
            wal.AppendCommitTransaction(1);
            wal.Sync();
        }

        var beginCount = 0;
        var commitCount = 0;
        var putCount = 0;
        
        using (var wal = new WriteAheadLog(walPath))
        {
            var visitor = new TestWalVisitor
            {
                OnBegin = _ => beginCount++,
                OnCommit = _ => commitCount++,
                OnPutAction = (_, _, _) => putCount++
            };
            
            wal.Replay(visitor);
        }

        Assert.That(beginCount, Is.EqualTo(1));
        Assert.That(commitCount, Is.EqualTo(1));
        Assert.That(putCount, Is.EqualTo(1));
    }

    [Test]
    public void TransactionalReplayOnlyCommittedTransactionsTest()
    {
        var walPath = Path.Combine(m_testDir, "tx_replay.wal");
        
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            // Committed transaction
            wal.AppendBeginTransaction(1);
            wal.AppendPut(ToBytes("committed_key"), ToBytes("value"), 1);
            wal.AppendCommitTransaction(1);
            
            // Uncommitted transaction
            wal.AppendBeginTransaction(2);
            wal.AppendPut(ToBytes("uncommitted_key"), ToBytes("value"), 2);
            // No commit
            
            // Rolled back transaction
            wal.AppendBeginTransaction(3);
            wal.AppendPut(ToBytes("rolledback_key"), ToBytes("value"), 3);
            wal.AppendRollbackTransaction(3);
            
            wal.Sync();
        }

        var appliedKeys = new List<string>();
        
        using (var wal = new WriteAheadLog(walPath))
        {
            var visitor = new WalReplayVisitorTransactional(
                (k, _) => appliedKeys.Add(TextEncoding.UTF8.GetString(k)),
                _ => { });
            
            wal.Replay(visitor);
        }

        Assert.That(appliedKeys, Has.Count.EqualTo(1));
        Assert.That(appliedKeys[0], Is.EqualTo("committed_key"));
    }

    [Test]
    public void MultipleTransactionsReplayedInOrderTest()
    {
        var walPath = Path.Combine(m_testDir, "multi_tx.wal");
        
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            for (int tx = 1; tx <= 5; tx++)
            {
                wal.AppendBeginTransaction(tx);
                wal.AppendPut(ToBytes($"key_tx{tx}"), ToBytes($"value{tx}"), tx);
                wal.AppendCommitTransaction(tx);
            }
            wal.Sync();
        }

        var keys = new List<string>();
        
        using (var wal = new WriteAheadLog(walPath))
        {
            var visitor = new WalReplayVisitorTransactional(
                (k, _) => keys.Add(TextEncoding.UTF8.GetString(k)),
                _ => { });
            
            wal.Replay(visitor);
        }

        Assert.That(keys.Count, Is.EqualTo(5));
        for (int i = 0; i < 5; i++)
        {
            Assert.That(keys[i], Is.EqualTo($"key_tx{i + 1}"));
        }
    }

    #endregion

    #region CRC32 Integrity Tests

    [Test]
    public void CorruptedEntryStopsReplayTest()
    {
        var walPath = Path.Combine(m_testDir, "corrupted.wal");
        
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
            wal.AppendPut(ToBytes("key2"), ToBytes("value2"));
            wal.Sync();
        }

        // Corrupt the file
        var bytes = File.ReadAllBytes(walPath);
        if (bytes.Length > 30)
        {
            bytes[25] ^= 0xFF; // Flip some bits in the middle
            File.WriteAllBytes(walPath, bytes);
        }

        var replayedCount = 0;
        
        using (var wal = new WriteAheadLog(walPath))
        {
            replayedCount = wal.Replay(new WalReplayVisitorSimple((_, _) => { }, _ => { }));
        }

        // Should stop at corrupted entry
        Assert.That(replayedCount, Is.LessThan(2));
    }

    #endregion

    #region Reopen Tests

    [Test]
    public void ReopenAndAppendPreservesDataTest()
    {
        var walPath = Path.Combine(m_testDir, "reopen.wal");
        
        // First session
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
            wal.Sync();
        }

        // Second session - append
        using (var wal = new WriteAheadLog(walPath))
        {
            wal.AppendPut(ToBytes("key2"), ToBytes("value2"));
            wal.Sync();
        }

        // Verify both entries
        var keys = new List<string>();
        
        using (var wal = new WriteAheadLog(walPath))
        {
            wal.Replay(new WalReplayVisitorSimple(
                (k, _) => keys.Add(TextEncoding.UTF8.GetString(k)),
                _ => { }));
        }

        Assert.That(keys, Is.EqualTo(new[] { "key1", "key2" }));
    }

    #endregion

    #region Properties Tests

    [Test]
    public void FilePathReturnsCorrectPathTest()
    {
        var walPath = Path.Combine(m_testDir, "path_test.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        
        Assert.That(wal.FilePath, Is.EqualTo(walPath));
    }

    [Test]
    public void IsEncryptedReturnsFalseWhenNoEncryptorTest()
    {
        var walPath = Path.Combine(m_testDir, "plain.wal");
        
        using var wal = new WriteAheadLog(walPath, encryptor: null, createNew: true);
        
        Assert.That(wal.IsEncrypted, Is.False);
    }

    [Test]
    public void EntryCountTracksEntriesTest()
    {
        var walPath = Path.Combine(m_testDir, "count.wal");
        
        using var wal = new WriteAheadLog(walPath, createNew: true);
        
        Assert.That(wal.EntryCount, Is.EqualTo(0));
        
        wal.AppendPut(ToBytes("key1"), ToBytes("value1"));
        Assert.That(wal.EntryCount, Is.EqualTo(1));
        
        wal.AppendPut(ToBytes("key2"), ToBytes("value2"));
        Assert.That(wal.EntryCount, Is.EqualTo(2));
        
        wal.AppendDelete(ToBytes("key1"));
        Assert.That(wal.EntryCount, Is.EqualTo(3));
    }

    #endregion

    #region Large Data Tests

    [Test]
    public void LargeValueRoundTripsTest()
    {
        var walPath = Path.Combine(m_testDir, "large.wal");
        var largeValue = new byte[100_000];
        new Random(42).NextBytes(largeValue);
        
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            wal.AppendPut(ToBytes("large"), largeValue);
            wal.Sync();
        }

        byte[]? recoveredValue = null;
        
        using (var wal = new WriteAheadLog(walPath))
        {
            wal.Replay(new WalReplayVisitorSimple(
                (_, v) => recoveredValue = v,
                _ => { }));
        }

        Assert.That(recoveredValue, Is.EqualTo(largeValue));
    }

    [Test]
    [Category("Stress")]
    public void ManyEntriesAllRecoveredTest()
    {
        var walPath = Path.Combine(m_testDir, "many.wal");
        const int entryCount = 10_000;
        
        using (var wal = new WriteAheadLog(walPath, createNew: true))
        {
            for (int i = 0; i < entryCount; i++)
            {
                wal.AppendPut(ToBytes($"key{i:D5}"), ToBytes($"value{i}"));
            }
            wal.Sync();
        }

        var count = 0;
        
        using (var wal = new WriteAheadLog(walPath))
        {
            count = wal.Replay(new WalReplayVisitorSimple((_, _) => { }, _ => { }));
        }

        Assert.That(count, Is.EqualTo(entryCount));
    }

    #endregion

    #region Helper Classes

    private class TestWalVisitor : IWalReplayVisitor
    {
        public Action<long>? OnBegin { get; set; }
        public Action<long>? OnCommit { get; set; }
        public Action<long>? OnRollback { get; set; }
        public Action<long, byte[], byte[]>? OnPutAction { get; set; }
        public Action<long, byte[]>? OnDeleteAction { get; set; }

        public void OnPut(long transactionId, byte[] key, byte[] value) 
            => OnPutAction?.Invoke(transactionId, key, value);
        
        public void OnDelete(long transactionId, byte[] key) 
            => OnDeleteAction?.Invoke(transactionId, key);
        
        public void OnBeginTransaction(long transactionId) 
            => OnBegin?.Invoke(transactionId);
        
        public void OnCommitTransaction(long transactionId) 
            => OnCommit?.Invoke(transactionId);
        
        public void OnRollbackTransaction(long transactionId) 
            => OnRollback?.Invoke(transactionId);
    }

    #endregion
}
