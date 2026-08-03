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

        // The victim is whichever transaction closed the cycle - see RegisterWaitEdges for why the
        // detector's victim strategy cannot be honoured on a path where the others are blocked.
        var deadlock = (DeadlockException)new[] { first, second }.First(e => e is DeadlockException);
        Assert.That(deadlock.VictimTransactionId, Is.EqualTo(deadlock == first ? tx1.TransactionId : tx2.TransactionId),
            "the transaction that is told about the deadlock must be the one named as victim");
    }

    [Test]
    public void ADeadlockIsReportedBeforeTheTimeoutRatherThanAfterItTest()
    {
        // A control on the fix, not on the finding. The assertion above would also pass if the report
        // arrived after the full wait, which would leave the user-visible cost unchanged. Give the wait
        // a 30 s timeout and require the deadlock to come back in a small fraction of it: a detector
        // fed at the right moment answers immediately, and no interleaving has to be caught.
        using var store = CreateStore();
        store.Put(Key("a"), Value("1"));
        store.Put(Key("b"), Value("2"));

        using var tx1 = (MvccTransaction)store.BeginTransaction();
        using var tx2 = (MvccTransaction)store.BeginTransaction();

        tx1.GetForUpdate(Key("a"));
        tx2.GetForUpdate(Key("b"));

        var generous = TimeSpan.FromSeconds(30);

        // tx1 waits behind tx2 first, and is genuinely parked - it stays blocked for the whole test.
        var parked = new ManualResetEventSlim(false);
        var waiter = Task.Run(() =>
        {
            parked.Set();
            try { tx1.GetForUpdate(Key("b"), RowLockWaitMode.Wait, generous); } catch { /* left waiting */ }
        });

        parked.Wait();
        Thread.Sleep(300); // let tx1 register its edge and enter the wait

        var sw = Stopwatch.StartNew();
        var thrown = Assert.Throws<DeadlockException>(
            () => tx2.GetForUpdate(Key("a"), RowLockWaitMode.Wait, generous),
            "tx2 closes the cycle, so tx2 is refused");
        sw.Stop();

        TestContext.Out.WriteLine($"deadlock reported after {sw.ElapsedMilliseconds} ms of a 30000 ms timeout");

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000),
            "the cycle is known when the wait is registered, so it must not cost the timeout");
        Assert.That(thrown!.CycleParticipants, Is.Not.Null.And.Contains(tx1.TransactionId),
            "the report must name the other participant, which is what makes it actionable");

        // Release tx1 so the parked task does not outlive the test.
        tx2.Rollback();
        waiter.Wait(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void AnOrdinaryLockWaitIsNotReportedAsADeadlockTest()
    {
        // The attribution control: a wait-for edge is added on every waiting acquire now, so the cheap
        // way to pass the two tests above would be to report a deadlock whenever anyone waits. This
        // fails if the fix over-claims.
        using var store = CreateStore();
        store.Put(Key("a"), Value("1"));

        using var tx1 = (MvccTransaction)store.BeginTransaction();
        using var tx2 = (MvccTransaction)store.BeginTransaction();

        tx1.GetForUpdate(Key("a"));

        // tx2 waits behind tx1, and tx1 waits for nothing - there is no cycle.
        var waiter = Task.Run(() =>
        {
            Thread.Sleep(300);
            tx1.Rollback(); // releases 'a'
        });

        Assert.That(() => tx2.GetForUpdate(Key("a"), RowLockWaitMode.Wait, TimeSpan.FromSeconds(10)),
            Throws.Nothing, "a wait that is not part of a cycle must simply be satisfied");

        waiter.Wait(TimeSpan.FromSeconds(10));
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

    [Test]
    public void DisposingOneSharedHandleLeavesTheOtherHoldersLockTest()
    {
        // A control on the fix rather than on the finding: releasing per-handle must release ONE
        // holder, not the entry. A fix that dropped the whole LockEntry would pass the two tests
        // above and silently unlock a row another transaction still holds.
        using var manager = new RowLockManager();

        var first = manager.AcquireLock(
            new RowLockRequest(Key("k"), transactionId: 1, RowLockMode.Shared, RowLockWaitMode.NoWait));
        manager.AcquireLock(
            new RowLockRequest(Key("k"), transactionId: 2, RowLockMode.Shared, RowLockWaitMode.NoWait));

        first!.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(manager.IsLockedByTransaction(Key("k"), 1), Is.False,
                "the disposed handle's transaction must no longer hold the row");
            Assert.That(manager.IsLockedByTransaction(Key("k"), 2), Is.True,
                "the other shared holder never disposed anything and must keep its lock");
            Assert.That(manager.IsLocked(Key("k")), Is.True,
                "the row is still locked, because a second transaction holds it");
        });
    }

    [Test]
    public void DisposingAHandleGrantsTheRowToAQueuedWaiterTest()
    {
        // The consequence for a caller that is already waiting: releasing through the handle has to
        // run the same grant path ReleaseAllLocks does, or the waiter sits there until it times out.
        using var manager = new RowLockManager();

        var held = manager.AcquireLock(
            new RowLockRequest(Key("k"), transactionId: 1, RowLockMode.Exclusive, RowLockWaitMode.NoWait));

        var waiterEntered = new ManualResetEventSlim(false);
        RowLockHandle? granted = null;
        var waiter = Task.Run(async () =>
        {
            waiterEntered.Set();
            granted = await manager.AcquireLockAsync(
                new RowLockRequest(Key("k"), transactionId: 2, RowLockMode.Exclusive,
                    RowLockWaitMode.Wait, TimeSpan.FromSeconds(10)));
        });

        waiterEntered.Wait();
        Thread.Sleep(200); // let the waiter reach the await

        held!.Dispose();

        Assert.That(waiter.Wait(TimeSpan.FromSeconds(5)), Is.True,
            "disposing the holder's handle must wake the queued waiter");
        Assert.That(granted, Is.Not.Null);
        Assert.That(manager.IsLockedByTransaction(Key("k"), 2), Is.True,
            "the woken waiter now holds the row");
    }

    [Test]
    public void DisposingAHandleTwiceDoesNotStealTheWaitersLockTest()
    {
        // Guards the nastier half of a per-handle release: once the row has been granted onward, a
        // second Dispose of the same handle must not release the NEW holder's lock.
        using var manager = new RowLockManager();

        var first = manager.AcquireLock(
            new RowLockRequest(Key("k"), transactionId: 1, RowLockMode.Exclusive, RowLockWaitMode.NoWait));
        first!.Dispose();

        var second = manager.AcquireLock(
            new RowLockRequest(Key("k"), transactionId: 2, RowLockMode.Exclusive, RowLockWaitMode.NoWait));
        Assert.That(second, Is.Not.Null);

        first.Dispose(); // second time, on a handle that no longer owns anything

        Assert.That(manager.IsLockedByTransaction(Key("k"), 2), Is.True,
            "the second transaction's lock must survive a repeat Dispose of the first handle");
    }

    #endregion

    #region RowLockManager completes waiters inline under its own lock

    [Test]
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

    [Test]
    public void CancellingAQueuedTransactionDoesNotRunItsContinuationInlineTest()
    {
        // NOT AN AUDIT FINDING. Found 2026-07-30 by grepping for the SHAPE of the RowLockManager
        // defect rather than the site the finding names: of the eight TaskCompletionSource
        // constructions in Sources/, six already pass RunContinuationsAsynchronously. The two that
        // did not were RowLockManager (above) and TransactionWaitQueue.
        //
        // Measured the same way - time the wrong thread. The queue's completion runs on whichever
        // thread calls Cancel, because CancellationToken.Register callbacks are synchronous, so a
        // caller cancelling one waiting transaction pays for whatever that transaction does next.
        using var store = CreateStore();

        var waiterEntered = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        var waiter = Task.Run(async () =>
        {
            waiterEntered.Set();
            await store.WaitInQueueAsync(
                transactionId: 1, isWriter: true, timeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token);

            // Stand-in for whatever the cancelled transaction does on its way out.
            Thread.Sleep(1000);
        });

        waiterEntered.Wait();
        Assert.That(SpinUntil(() => store.WaitQueue.WaitingCount > 0), Is.True,
            "the transaction should be queued before it is cancelled");
        Thread.Sleep(200); // let the waiter reach the await

        var sw = Stopwatch.StartNew();
        cts.Cancel();
        sw.Stop();

        waiter.Wait(TimeSpan.FromSeconds(15));

        TestContext.Out.WriteLine($"Cancel took {sw.ElapsedMilliseconds} ms");

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500),
            "the cancelling thread must not execute the cancelled waiter's continuation inline");
    }

    #endregion

    #region LsmParallelStore does not read its own writes

    [Test]
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
    public void ParallelLsmStoreKeepsEveryWriteItAcknowledgedTest()
    {
        // NOT AN AUDIT FINDING, and this is the CONTROL for something larger than the two markers
        // around it. The SQL-level probe
        // (AdoNet ConcurrencyModelProbeTests.ProbeLsmParallelModeSeesItsOwnCommittedRows) showed ten
        // successful INSERTs over Store=lsm + Parallel Mode=Buffered leaving 0 or 1 rows - lost, not
        // hidden, because a clean close and reopen recovers nothing.
        //
        // This walks the same shape one layer down and PASSES: driven directly, with a Flush after each
        // write, the store keeps all ten. So the loss is not "LsmParallelStore drops writes" on its own -
        // it needs whatever the engine does differently, and this test is what stops the eventual fix
        // from being aimed at the wrong layer.
        var directory = CreateTempDirectory();
        try
        {
            using var store = new LsmParallelStore(directory);

            for (var i = 0; i < 10; i++)
            {
                store.Put(Key($"k{i}"), Value($"v{i}"));
                store.Checkpoint();
            }

            var scanned = store.Scan(null, null).ToList();
            var missing = Enumerable.Range(0, 10)
                .Where(i => store.Get(Key($"k{i}")) == null)
                .ToList();

            TestContext.Out.WriteLine(
                $"scan returned {scanned.Count} of 10; keys not readable: [{string.Join(",", missing)}]");

            Assert.Multiple(() =>
            {
                Assert.That(missing, Is.Empty, "every key flushed by its writer must be readable");
                Assert.That(scanned, Has.Count.EqualTo(10), "and a scan must return all ten");
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
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

    [Test]
    public void MvccOverAParallelLsmStoreCommitsWhatItAcknowledgedTest()
    {
        // THE DEFECT THE TWO MARKERS ABOVE ACTUALLY CAUSE, and it is far larger than their wording.
        //
        // MvccKeyValueStore.CommitTransaction SCANS the store to find the versions it has just
        // installed and rewrite them as committed. Over LsmParallelStore that scan read a store the
        // queued merge had not reached, so the loop found nothing to commit: the version stayed
        // uncommitted for ever - written to the SSTable, invisible to every reader, in every
        // configuration, and unrecoverable by reopening without the parallel wrapper.
        //
        // One Put in one transaction was enough. The reopen below is the half that makes this a
        // durability test rather than a visibility one.
        var directory = CreateTempDirectory();
        try
        {
            using (var database = new WitDatabaseBuilder()
                       .WithLsmTree(directory)
                       .WithParallelWrites(ParallelMode.Buffered)
                       .WithTransactions()
                       .WithMvcc()
                       .Build())
            {
                using var transaction = database.BeginTransaction();
                transaction.Put(Key("k"), Value("v"));
                transaction.Commit();

                Assert.That(database.Get(Key("k")), Is.EqualTo(Value("v")),
                    "a committed row must be readable by the session that committed it");
            }

            using var reopened = new WitDatabaseBuilder()
                .WithLsmTree(directory)
                .WithParallelWrites(ParallelMode.Buffered)
                .WithTransactions()
                .WithMvcc()
                .Build();

            Assert.That(reopened.Get(Key("k")), Is.EqualTo(Value("v")),
                "and it must still be there after a clean close - otherwise the commit never happened");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
    public void ParallelLsmStoreAppliesBufferedOperationsInOrderTest()
    {
        // NOT AN AUDIT FINDING, and NOT the cause of the commit loss above - it was the first
        // hypothesis for it and was refuted by execution, which is the only reason it was found.
        //
        // MergeBuffersBatch split each batch into puts and deletes "for better locality" and applied
        // every put before every delete, so a caller that wrote a key, deleted it and wrote it again
        // within one batch got the delete applied last. The buffer holds the operations in order; the
        // merge must keep it.
        var directory = CreateTempDirectory();
        try
        {
            using var store = new LsmParallelStore(directory);

            // All three land in one buffer, so they are merged as one batch.
            store.Put(Key("k"), Value("first"));
            store.Delete(Key("k"));
            store.Put(Key("k"), Value("second"));

            store.Checkpoint();

            Assert.That(store.Get(Key("k")), Is.EqualTo(Value("second")),
                "the last operation on the key was a Put, so the key must exist with its value");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    #endregion

    #region PageLatchManager.Cleanup could drop a latch that was still held - REMOVED WITH THE SUBSYSTEM

    // The marker that lived here is gone because its subject is gone. `PageLatch` and
    // `PageLatchManager` were deleted 2026-07-30; the defect was real and confirmed - Cleanup decided a
    // latch was idle using the thread-affine ReaderWriterLockSlim.IsWriteLockHeld, so a second exclusive
    // acquire WAS granted while another thread held the page, and the holder's release then threw
    // SynchronizationLockException on a background thread, terminating the test host.
    //
    // It was fixed by deletion rather than by repair, because NOTHING could enter it. Re-verified
    // exhaustively before removal: across Sources/, Tools/, Samples/ and Benchmarks/ the only
    // references to either type were their own declarations and their own tests. `BTreeConcurrentStore`
    // serialises with one store-wide ReaderWriterLockSlim, not per-page latches, which is consistent
    // with the decided model - one writer at a time - and leaves finer-grained page latching as an
    // optimisation nothing had wired in.
    //
    // Kept as a comment rather than dropped silently so that "the count went down" is not mistaken for
    // "the defect was repaired". See Docs/PHASE5-CONCURRENCY-PLAN.md 8b.4.

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
    public void ClearDoesNotRecycleABufferWhileItsWriteIsInFlightTest()
    {
        // Finding: PageCacheShardedClock.cs:160 - the cache disposes CachedPage, returning its
        // pooled buffer to the array pool, while an async write still holds that same memory. The
        // storage then writes whatever the next borrower put in the buffer.
        //
        // Fully deterministic: the storage double parks inside WritePageAsync until released, so
        // the Clear below is guaranteed to happen *during* the write rather than racing with it.
        using var storage = new BlockingStorage(pageSize: 256, pageCount: 8);
        using var cache = new PageCacheShardedClock(storage, maxPages: 4, shardCount: 1);

        var page = cache.CreatePage(1);
        page.Data.Fill(0xAB);
        cache.MarkDirty(1);

        var flush = cache.FlushAllAsync().AsTask();
        Assert.That(storage.WriteEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
            "the storage double should have been handed the page buffer");

        // The write is parked, holding the buffer, and FlushAllAsync pinned the page before starting
        // it. Clear was the unguarded path: Evict refuses a pinned page with "Cannot evict pinned
        // page", while Clear disposed every CachedPage unconditionally and handed its pooled array
        // back. It must now refuse on the same condition.
        Assert.That(() => cache.Clear(), Throws.InstanceOf<InvalidOperationException>(),
            "Clear must refuse a pinned page exactly as Evict does");

        // Simulate the next borrower of that pooled array scribbling over it. If Clear had returned
        // the buffer to the pool, this is the content that would reach storage.
        var stolen = System.Buffers.ArrayPool<byte>.Shared.Rent(256);
        stolen.AsSpan(0, 256).Fill(0xFF);
        System.Buffers.ArrayPool<byte>.Shared.Return(stolen);

        storage.ReleaseWrite.Set();
        flush.Wait(TimeSpan.FromSeconds(10));

        Assert.That(storage.GetPage(1), Is.All.EqualTo((byte)0xAB),
            "the page written to storage must be the one the caller dirtied");
    }

    [Test]
    public void ClearIsAllOrNothingRatherThanPartlyDestructiveTest()
    {
        // A control on the fix: refusing is only useful if nothing was thrown away before the refusal.
        // A pin check written inside the disposal loop would pass the test above and still have
        // recycled every page that came before the pinned one.
        using var storage = new BlockingStorage(pageSize: 256, pageCount: 8);
        using var cache = new PageCacheShardedClock(storage, maxPages: 4, shardCount: 1);

        // Two pages released back to the cache, so the parked write below is the ONLY pin. Without the
        // releases every page would be pinned by CreatePage and the refusal would prove nothing about
        // which page caused it.
        cache.CreatePage(1).Data.Fill(0x11);
        cache.ReleasePage(1);
        cache.CreatePage(2).Data.Fill(0x22);
        cache.ReleasePage(2);

        var pinned = cache.CreatePage(3);
        pinned.Data.Fill(0xAB);
        cache.MarkDirty(3);

        var flush = cache.FlushAllAsync().AsTask();
        Assert.That(storage.WriteEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        var countBefore = cache.Count;
        Assert.That(() => cache.Clear(), Throws.InstanceOf<InvalidOperationException>());

        Assert.That(cache.Count, Is.EqualTo(countBefore),
            "a refused Clear must leave the cache exactly as it was, not half emptied");

        storage.ReleaseWrite.Set();
        flush.Wait(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void LruCacheClearDoesNotRecycleABufferWhileItsWriteIsInFlightTest()
    {
        // NOT AN AUDIT FINDING. The finding names PageCacheShardedClock.cs:160 only, but PageCacheLru
        // has the identical structure: Evict refuses a pinned page, FlushAllAsync pins each page for the
        // duration of its write, and Clear disposed every page unconditionally. Found by checking the
        // other IPageCache implementation rather than the site the finding names.
        //
        // Reachable: PageCacheLru is registered as a provider (ProviderRegistration.cs), so
        // `WithCacheKey("lru")` selects it - a supported configuration, not dead code.
        using var storage = new BlockingStorage(pageSize: 256, pageCount: 8);
        using var cache = new PageCacheLru(storage, maxPages: 4);

        var page = cache.CreatePage(1);
        page.Data.Fill(0xAB);
        cache.MarkDirty(1);

        var flush = cache.FlushAllAsync().AsTask();
        Assert.That(storage.WriteEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        Assert.That(() => cache.Clear(), Throws.InstanceOf<InvalidOperationException>(),
            "Clear must refuse a pinned page exactly as Evict does");

        var stolen = System.Buffers.ArrayPool<byte>.Shared.Rent(256);
        stolen.AsSpan(0, 256).Fill(0xFF);
        System.Buffers.ArrayPool<byte>.Shared.Return(stolen);

        storage.ReleaseWrite.Set();
        flush.Wait(TimeSpan.FromSeconds(10));

        Assert.That(storage.GetPage(1), Is.All.EqualTo((byte)0xAB),
            "the page written to storage must be the one the caller dirtied");
    }

    [Test]
    public void DisposingTheCacheDoesNotThrowAwayAnUnfinishedWriteTest()
    {
        // Dispose is the ONLY production caller of Clear - established by the reachability pass, which
        // is what makes this marker durability-adjacent rather than write-path. So Dispose cannot
        // simply inherit Clear's refusal: it has to let the write finish, then clear.
        using var storage = new BlockingStorage(pageSize: 256, pageCount: 8);
        var cache = new PageCacheShardedClock(storage, maxPages: 4, shardCount: 1);

        var page = cache.CreatePage(1);
        page.Data.Fill(0xAB);
        cache.MarkDirty(1);

        var flush = cache.FlushAllAsync().AsTask();
        Assert.That(storage.WriteEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        // Dispose on another thread, so the parked write is genuinely in flight while it runs.
        var disposed = new ManualResetEventSlim(false);
        Exception? disposeFailure = null;
        var disposer = Task.Run(() =>
        {
            try { cache.Dispose(); }
            catch (Exception e) { disposeFailure = e; }
            finally { disposed.Set(); }
        });

        Assert.That(disposed.Wait(TimeSpan.FromMilliseconds(500)), Is.False,
            "Dispose must still be waiting for the in-flight write rather than discarding it");

        var stolen = System.Buffers.ArrayPool<byte>.Shared.Rent(256);
        stolen.AsSpan(0, 256).Fill(0xFF);
        System.Buffers.ArrayPool<byte>.Shared.Return(stolen);

        storage.ReleaseWrite.Set();
        flush.Wait(TimeSpan.FromSeconds(10));
        Assert.That(disposer.Wait(TimeSpan.FromSeconds(10)), Is.True, "Dispose must complete once the write has");

        Assert.Multiple(() =>
        {
            Assert.That(disposeFailure, Is.Null, "Dispose must not throw because a write was in flight");
            Assert.That(storage.GetPage(1), Is.All.EqualTo((byte)0xAB),
                "the write that was in flight when Dispose ran must still have landed intact");
        });
    }

    #endregion

    #region LsmParallelWriter.FlushAllAsync and other threads' buffers

    [Test]
    public void FlushAllDoesNotDiscardAnotherThreadsBufferedWritesTest()
    {
        // FIXED 2026-07-30, and this marker is un-ignored with the fix. FlushAllAsync used to hand
        // the merge loop a buffer whose owner was still appending to it, and reset only the CALLING
        // thread's slot, leaving every other owner holding a buffer that had been drained and
        // disposed. Each buffer is now taken under the same gate its owner appends under and
        // replaced with a fresh one in the same operation.
        //
        // Kept active but not treated as the proof: this scenario PARKS the producer while the
        // foreign flush runs, so the writes never overlap it - measured, it failed 0 times in 20
        // rounds here before the fix, and it took CI to fail it at all. The evidence is
        // LsmParallelWriterFlushProbeTests, which overlaps them and was red on this machine.
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
            store.Checkpoint();

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
