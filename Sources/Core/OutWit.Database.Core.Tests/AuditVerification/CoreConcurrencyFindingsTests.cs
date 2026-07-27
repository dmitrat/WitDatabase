using System.Diagnostics;
using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Tree;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>core-concurrency</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// Concurrency claims are the ones a load test cannot settle: a passing stress run proves only that
/// the race did not happen this time. Every test here is therefore <b>deterministic</b> - it drives
/// the two threads into the exact interleaving the finding describes, or replaces the race with a
/// direct observation of the state the race would corrupt. Nothing sleeps waiting for a bug to
/// appear.
///
/// As in the engine fixtures, each test asserts the <b>correct</b> behaviour, so a failure confirms
/// the finding. See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class CoreConcurrencyFindingsTests
{
    private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    #region DeadlockDetector is never fed a wait edge

    [Test]
    [Ignore("CONFIRMED 2026-07-27: both transactions failed with TimeoutException after the full 2 s lock "
            + "timeout and neither raised DeadlockException. The detector is fetched into a local "
            + "that is never read, and the deadlock check is an empty `if` body carrying the comment "
            + "\"full implementation would track all holders\". "
            + "core-concurrency, Transactions/MvccTransaction.cs:228")]
    public void RowLockDeadlockIsDetectedRatherThanTimedOutTest()
    {
        // Finding: MvccTransaction.cs:228 - the detector is fetched into a local and never used;
        // the deadlock check is an empty `if` body. A classic AB/BA row-lock deadlock should be
        // detected and one victim aborted, not left for both sides to time out.
        using var store = CreateStore();
        store.Put(Key("a"), Value("1"));
        store.Put(Key("b"), Value("2"));

        using var tx1 = (MvccTransaction)store.BeginTransaction();
        using var tx2 = (MvccTransaction)store.BeginTransaction();

        tx1.GetForUpdate(Key("a"));
        tx2.GetForUpdate(Key("b"));

        var timeout = TimeSpan.FromSeconds(2);
        Exception? first = null;
        Exception? second = null;

        var t1 = Task.Run(() =>
        {
            try { tx1.GetForUpdate(Key("b"), RowLockWaitMode.Wait, timeout); }
            catch (Exception e) { first = e; }
        });
        var t2 = Task.Run(() =>
        {
            try { tx2.GetForUpdate(Key("a"), RowLockWaitMode.Wait, timeout); }
            catch (Exception e) { second = e; }
        });

        var sw = Stopwatch.StartNew();
        Task.WaitAll([t1, t2], TimeSpan.FromSeconds(30));
        sw.Stop();

        TestContext.Out.WriteLine(
            $"resolved after {sw.ElapsedMilliseconds} ms; " +
            $"tx1 -> {first?.GetType().Name ?? "no exception"}, " +
            $"tx2 -> {second?.GetType().Name ?? "no exception"}");

        Assert.That(new[] { first, second }, Has.Some.InstanceOf<DeadlockException>(),
            "a cycle in the wait-for graph must be reported as a deadlock, not as a lock timeout");
    }

    #endregion

    #region DatabaseLock leaks a reader count on cancellation

    [Test]
    public void CancelledReadLockDoesNotLeakAReaderCountTest()
    {
        // Finding: DatabaseLock.cs:153 - AcquireReadLockAsync increments m_readerCount before it
        // waits and does not undo it when the wait is cancelled, so the lock believes a reader is
        // present forever and reader/writer exclusion is permanently broken.
        using var databaseLock = new DatabaseLock(TimeSpan.FromSeconds(5));

        // Hold the write lock so the reader below has to queue rather than complete immediately.
        var writeHandle = databaseLock.AcquireWriteLock();

        using var cts = new CancellationTokenSource();
        var pending = databaseLock.AcquireReadLockAsync(cts.Token);

        // Deterministic hand-off: proceed only once the reader is genuinely queued.
        Assert.That(SpinUntil(() => databaseLock.WaitingReadCount > 0), Is.True,
            "the read lock should be queued behind the write lock");

        cts.Cancel();
        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());

        writeHandle.Dispose();

        Assert.That(databaseLock.CurrentReaderCount, Is.EqualTo(0),
            "the cancelled reader never entered the lock, so it must not be counted as holding it");
    }

    [Test]
    public void WriteLockIsStillExclusiveAfterAReadLockIsCancelledTest()
    {
        // The user-visible consequence of the same leak.
        using var databaseLock = new DatabaseLock(TimeSpan.FromSeconds(2));

        var writeHandle = databaseLock.AcquireWriteLock();
        using var cts = new CancellationTokenSource();
        var pending = databaseLock.AcquireReadLockAsync(cts.Token);

        Assert.That(SpinUntil(() => databaseLock.WaitingReadCount > 0), Is.True);
        cts.Cancel();
        try { pending.Wait(TimeSpan.FromSeconds(5)); } catch { /* expected */ }

        writeHandle.Dispose();

        Assert.That(() => databaseLock.AcquireWriteLock().Dispose(), Throws.Nothing,
            "with no live readers a writer must be able to take the lock again");
    }

    #endregion

    #region RowLockHandle.Dispose is an empty method

    [Test]
    [Ignore("CONFIRMED 2026-07-27: IsLocked still reports true after the handle is disposed. "
            + "core-concurrency, Concurrency/RowLockHandle.cs:40")]
    public void DisposingARowLockHandleReleasesTheLockTest()
    {
        // Finding: RowLockHandle.cs:40 - Dispose() has an empty body, so a lock survives the handle
        // that owns it and can be held forever by a transaction that has already finished.
        using var manager = new RowLockManager();

        var handle = manager.AcquireLock(
            new RowLockRequest(Key("k"), transactionId: 1, RowLockMode.Exclusive, RowLockWaitMode.NoWait));
        Assert.That(handle, Is.Not.Null);

        handle!.Dispose();

        Assert.That(manager.IsLocked(Key("k")), Is.False,
            "disposing the handle must release the row lock it represents");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the second transaction cannot take the row, because disposing the "
            + "first handle released nothing. Same defect, from the caller's side.")]
    public void AnotherTransactionCanLockTheRowAfterTheHandleIsDisposedTest()
    {
        using var manager = new RowLockManager();

        var first = manager.AcquireLock(
            new RowLockRequest(Key("k"), transactionId: 1, RowLockMode.Exclusive, RowLockWaitMode.NoWait));
        first!.Dispose();

        Assert.That(
            () => manager.AcquireLock(
                new RowLockRequest(Key("k"), transactionId: 2, RowLockMode.Exclusive, RowLockWaitMode.NoWait)),
            Throws.Nothing,
            "the row is no longer locked, so a second transaction must be able to take it");
    }

    #endregion

    #region RowLockManager completes waiters inline under its own lock

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and measured exactly: ReleaseAllLocks took 1007 ms for a waiter whose "
            + "continuation sleeps 1000 ms. The releasing thread runs foreign code to completion while "
            + "holding m_syncLock. core-concurrency, Concurrency/RowLockManager.cs:110")]
    public void ReleasingALockDoesNotRunTheWaitersContinuationInlineTest()
    {
        // Finding: RowLockManager.cs:110 - the TaskCompletionSource is created without
        // RunContinuationsAsynchronously and completed while m_syncLock is held, so the waiting
        // transaction's continuation executes on the releasing thread, inside the manager's lock.
        //
        // Deterministic probe: the woken waiter blocks for 1s. If its continuation runs inline,
        // that second is paid by the thread calling ReleaseAllLocks - which is measurable without
        // any race.
        using var manager = new RowLockManager();

        manager.AcquireLock(
            new RowLockRequest(Key("k"), transactionId: 1, RowLockMode.Exclusive, RowLockWaitMode.NoWait));

        var waiterEntered = new ManualResetEventSlim(false);
        var waiter = Task.Run(async () =>
        {
            waiterEntered.Set();
            await manager.AcquireLockAsync(
                new RowLockRequest(Key("k"), transactionId: 2, RowLockMode.Exclusive,
                    RowLockWaitMode.Wait, TimeSpan.FromSeconds(10)));

            // Stand-in for whatever the woken transaction does next.
            Thread.Sleep(1000);
        });

        waiterEntered.Wait();
        Assert.That(SpinUntil(() => manager.LockCount > 0), Is.True);
        Thread.Sleep(200); // let the waiter reach the await

        var sw = Stopwatch.StartNew();
        manager.ReleaseAllLocks(1);
        sw.Stop();

        waiter.Wait(TimeSpan.FromSeconds(15));

        TestContext.Out.WriteLine($"ReleaseAllLocks took {sw.ElapsedMilliseconds} ms");

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500),
            "the releasing thread must not execute the woken waiter's continuation inline");
    }

    #endregion

    #region LsmParallelStore does not read its own writes

    [Test]
    [Ignore("CONFIRMED 2026-07-27: Get returned null for a key written moments earlier on the same "
            + "thread. core-concurrency, Builder/LsmParallelStore.cs:83")]
    public void ParallelLsmStoreReadsItsOwnWriteTest()
    {
        // Finding: LsmParallelStore.cs:83 - Get/Scan query the underlying store without waiting for
        // the background merge, so a value that was just written is not visible to the writer.
        var directory = CreateTempDirectory();
        try
        {
            using var store = new LsmParallelStore(directory);

            store.Put(Key("k"), Value("v"));
            var read = store.Get(Key("k"));

            Assert.That(read, Is.Not.Null, "a write must be visible to the caller that made it");
            Assert.That(read, Is.EqualTo(Value("v")));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the scan returned 0 rows after a Put on the same thread.")]
    public void ParallelLsmStoreScanSeesItsOwnWriteTest()
    {
        var directory = CreateTempDirectory();
        try
        {
            using var store = new LsmParallelStore(directory);

            store.Put(Key("k"), Value("v"));
            var scanned = store.Scan(null, null).ToList();

            Assert.That(scanned, Has.Count.EqualTo(1),
                "a scan must see the write the same caller just made");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    #endregion

    #region PageLatchManager.Cleanup can drop a latch that is still held

    [Test]
    [Ignore("CONFIRMED 2026-07-27, in a worse form than stated. Both halves reproduce: the second "
            + "exclusive acquire IS granted while another thread holds the page, and the holder's "
            + "release then throws SynchronizationLockException \"The write lock is being released "
            + "without being held\" - it lands on the replacement latch, not the one it took. That "
            + "exception is raised on a background thread, so left unhandled it terminates the "
            + "process: this test crashed the test host before its Dispose was wrapped. "
            + "core-concurrency, Tree/PageLatchManager.cs:228")]
    public void CleanupDoesNotReleaseALatchHeldByAnotherThreadTest()
    {
        // Finding: PageLatchManager.cs:228 - Cleanup decides a latch is idle using
        // ReaderWriterLockSlim.IsWriteLockHeld, which is *thread-affine*: it reports whether the
        // calling thread holds the lock, not whether anyone does. Seen from the cleanup thread a
        // latch held exclusively by a different thread looks completely idle, so it is removed and
        // disposed underneath its owner.
        //
        // The thread hand-off below is what makes this deterministic - holding the latch on the
        // test's own thread would make IsWriteLockHeld true and hide the defect entirely.
        var manager = new PageLatchManager();
        var acquired = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        Exception? holderFailure = null;

        // The holder's Dispose is wrapped: without the catch, the exception it throws is unhandled
        // on a background thread and terminates the whole test host (observed 2026-07-27).
        var holder = new Thread(() =>
        {
            var handle = manager.AcquireExclusive(1);
            acquired.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            try { handle.Dispose(); }
            catch (Exception e) { holderFailure = e; }
        });
        holder.Start();
        acquired.Wait(TimeSpan.FromSeconds(10));

        bool granted;
        try
        {
            manager.Cleanup();

            granted = manager.TryAcquireExclusive(1, out var second);
            if (granted)
                second.Dispose();
        }
        finally
        {
            release.Set();
            holder.Join(TimeSpan.FromSeconds(10));
            try { manager.Dispose(); } catch (SynchronizationLockException) { /* part of the defect */ }
        }

        Assert.Multiple(() =>
        {
            Assert.That(granted, Is.False,
                "page 1 is exclusively latched by another thread, so a second exclusive acquire " +
                "must not be granted");
            Assert.That(holderFailure, Is.Null,
                "the holder must be able to release the latch it acquired");
        });
    }

    #endregion

    #region EnableFileLocking creates no file lock

    [Test]
    public void FileLockingActuallyExcludesASecondOpenerTest()
    {
        // Finding: WitDatabaseBuilder.cs:561 - EnableFileLocking defaults to true, but the builder
        // constructs `new LockManager(Options.LockTimeout)`, the overload that sets
        // m_fileLock = null and m_useFileLocking = false. The advertised ProviderFeatures.FileLocking
        // is therefore reported for a database that holds no file lock at all.
        var path = Path.Combine(CreateTempDirectory(), "locking.witdb");
        WitDatabase? second = null;
        try
        {
            using var first = WitDatabase.Create(path);

            Assert.That(() => second = WitDatabase.Open(path), Throws.Exception,
                "file locking is enabled by default, so a second opener must be refused");
        }
        finally
        {
            second?.Dispose();
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    #endregion

    #region Page cache returns a pooled buffer while a write of it is in flight

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and this one corrupts data outright: the page that reached storage was "
            + "filled with 0xFF - the content of the next borrower of the recycled pooled array - "
            + "instead of the 0xAB the caller wrote. Note the path matters: Evict() correctly refuses "
            + "with \"Cannot evict pinned page\"; Clear() disposes every CachedPage unconditionally. "
            + "core-concurrency, Cache/PageCacheShardedClock.cs:160")]
    public void ClearDoesNotRecycleABufferWhileItsWriteIsInFlightTest()
    {
        // Finding: PageCacheShardedClock.cs:160 - the cache disposes CachedPage, returning its
        // pooled buffer to the array pool, while an async write still holds that same memory. The
        // storage then writes whatever the next borrower put in the buffer.
        //
        // Fully deterministic: the storage double parks inside WritePageAsync until released, so
        // the eviction below is guaranteed to happen *during* the write rather than racing with it.
        using var storage = new BlockingStorage(pageSize: 256, pageCount: 8);
        using var cache = new PageCacheShardedClock(storage, maxPages: 4, shardCount: 1);

        var page = cache.CreatePage(1);
        page.Data.Fill(0xAB);
        cache.MarkDirty(1);

        var flush = cache.FlushAllAsync().AsTask();
        Assert.That(storage.WriteEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
            "the storage double should have been handed the page buffer");

        // The write is parked, holding the buffer. Clear() is the unguarded path: unlike Evict,
        // which refuses a pinned page with "Cannot evict pinned page", Clear disposes every
        // CachedPage unconditionally and hands its pooled array back.
        cache.Clear();

        // Simulate the next borrower of that pooled array scribbling over it.
        var stolen = System.Buffers.ArrayPool<byte>.Shared.Rent(256);
        stolen.AsSpan(0, 256).Fill(0xFF);
        System.Buffers.ArrayPool<byte>.Shared.Return(stolen);

        storage.ReleaseWrite.Set();
        flush.Wait(TimeSpan.FromSeconds(10));

        Assert.That(storage.GetPage(1), Is.All.EqualTo((byte)0xAB),
            "the page written to storage must be the one the caller dirtied");
    }

    #endregion

    #region LsmParallelWriter.FlushAllAsync and other threads' buffers

    [Test]
    public void FlushAllDoesNotDiscardAnotherThreadsBufferedWritesTest()
    {
        // Finding: LsmParallelWriter.cs:217 - FlushAllAsync drains and disposes thread-local
        // buffers belonging to threads that are still using them, so a writer thread can lose the
        // entries it had buffered, or keep writing into a disposed buffer.
        var directory = CreateTempDirectory();
        try
        {
            using var store = new StoreLsm(directory);
            using var writer = new LsmParallelWriter(store);

            var bufferedFirstHalf = new ManualResetEventSlim(false);
            var flushDone = new ManualResetEventSlim(false);
            Exception? writerFailure = null;

            var producer = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                        writer.Put(Key($"k{i:D2}"), Value($"v{i:D2}"));

                    bufferedFirstHalf.Set();
                    flushDone.Wait(TimeSpan.FromSeconds(10));

                    // Same thread, same thread-local buffer, after a foreign FlushAllAsync.
                    for (int i = 10; i < 20; i++)
                        writer.Put(Key($"k{i:D2}"), Value($"v{i:D2}"));

                    writer.FlushCurrentBuffer();
                }
                catch (Exception e) { writerFailure = e; }
            });

            producer.Start();
            bufferedFirstHalf.Wait(TimeSpan.FromSeconds(10));

            writer.FlushAllAsync().GetAwaiter().GetResult();
            flushDone.Set();
            producer.Join(TimeSpan.FromSeconds(20));

            writer.FlushAllAsync().GetAwaiter().GetResult();
            store.Flush();

            Assert.That(writerFailure, Is.Null,
                "the producer thread must not fail because another thread flushed");

            var missing = Enumerable.Range(0, 20)
                .Where(i => store.Get(Key($"k{i:D2}")) == null)
                .Select(i => $"k{i:D2}")
                .ToList();

            Assert.That(missing, Is.Empty, "no buffered write may be lost by a concurrent FlushAllAsync");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    #endregion

    #region StorageFile mixes locked buffered I/O with unlocked handle I/O

    // Finding: StorageFile.cs:199 - the synchronous path seeks and reads/writes through the
    // buffered FileStream under m_lock, while the asynchronous path calls RandomAccess on
    // m_stream.SafeFileHandle with no lock at all. The two do not see each other's buffering, and
    // nothing serialises the shared stream position against the handle-level access.
    //
    // No race is needed to show it: a value written through one path and read back through the
    // other is a single-threaded, fully deterministic observation.

    [Test]
    public void PageWrittenSynchronouslyIsVisibleToAnAsynchronousReadTest()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "storage.dat");
            using var storage = new StorageFile(path, pageSize: 4096);
            storage.SetSize(4);

            var page = new byte[4096];
            page.AsSpan().Fill(0xAB);
            storage.WritePage(1, page);

            var readBack = new byte[4096];
            storage.ReadPageAsync(1, readBack).AsTask().GetAwaiter().GetResult();

            Assert.That(readBack, Is.All.EqualTo((byte)0xAB),
                "an async read must see a page the same caller just wrote synchronously");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
    public void PageWrittenAsynchronouslyIsVisibleToASynchronousReadTest()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "storage.dat");
            using var storage = new StorageFile(path, pageSize: 4096);
            storage.SetSize(4);

            // Prime the buffered stream so it holds state for this region.
            var zeros = new byte[4096];
            storage.ReadPage(2, zeros);

            var page = new byte[4096];
            page.AsSpan().Fill(0xCD);
            storage.WritePageAsync(2, page).AsTask().GetAwaiter().GetResult();

            var readBack = new byte[4096];
            storage.ReadPage(2, readBack);

            Assert.That(readBack, Is.All.EqualTo((byte)0xCD),
                "a sync read must see a page the same caller just wrote asynchronously");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    #endregion

    #region Cancellation pin for the reader-count path

    [Test]
    public void RepeatedReadLockCancellationsNeverLeaveAPhantomReaderTest()
    {
        // Pin for the DatabaseLock.cs:153 claim. The dangerous window - cancelling after
        // m_readerCount++ but inside the m_writeSemaphore wait - is not reachable through the
        // public API, because every writer path takes m_readerGate *before* m_writeSemaphore, so a
        // reader can never find the semaphore held while the gate is open. This hammers the window
        // that IS reachable and asserts the count always returns to zero.
        using var databaseLock = new DatabaseLock(TimeSpan.FromSeconds(5));

        for (int i = 0; i < 200; i++)
        {
            var writeHandle = databaseLock.AcquireWriteLock();
            using var cts = new CancellationTokenSource();

            var pending = databaseLock.AcquireReadLockAsync(cts.Token);
            cts.Cancel();
            try { pending.Wait(TimeSpan.FromSeconds(5)); } catch { /* expected */ }

            writeHandle.Dispose();
        }

        Assert.That(databaseLock.CurrentReaderCount, Is.EqualTo(0),
            "200 cancelled read-lock acquisitions must leave no phantom reader behind");
        Assert.That(() => databaseLock.AcquireWriteLock().Dispose(), Throws.Nothing,
            "the lock must still be usable by a writer");
    }

    #endregion

    #region Helpers

    private static MvccTransactionalStore CreateStore() => new(new StoreInMemory());

    /// <summary>
    /// An <see cref="IStorage"/> whose asynchronous write parks until it is released, so a test can
    /// deterministically act while a write is genuinely in flight.
    /// </summary>
    private sealed class BlockingStorage : IStorage
    {
        private readonly byte[][] m_pages;

        public BlockingStorage(int pageSize, int pageCount)
        {
            PageSize = pageSize;
            m_pages = Enumerable.Range(0, pageCount).Select(_ => new byte[pageSize]).ToArray();
        }

        public ManualResetEventSlim WriteEntered { get; } = new(false);
        public ManualResetEventSlim ReleaseWrite { get; } = new(false);

        public byte[] GetPage(long pageNumber) => m_pages[pageNumber];

        public void ReadPage(long pageNumber, Span<byte> buffer) => m_pages[pageNumber].CopyTo(buffer);

        public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadPage(pageNumber, buffer.Span);
            return ValueTask.CompletedTask;
        }

        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer) =>
            buffer[..PageSize].CopyTo(m_pages[pageNumber]);

        public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteEntered.Set();
            await Task.Run(() => ReleaseWrite.Wait(cancellationToken), cancellationToken).ConfigureAwait(false);

            // The copy happens after the pause, exactly as a real slow write would.
            buffer.Span[..PageSize].CopyTo(m_pages[pageNumber]);
        }

        public void Flush() { }
        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void SetSize(long pageCount) { }

        public int PageSize { get; }
        public long PageCount => m_pages.Length;
        public bool IsReadOnly => false;
        public string ProviderKey => "blocking-test";

        public void Dispose()
        {
            WriteEntered.Dispose();
            ReleaseWrite.Dispose();
        }
    }

    private static bool SpinUntil(Func<bool> condition) =>
        SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5));

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "witdb-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion
}
