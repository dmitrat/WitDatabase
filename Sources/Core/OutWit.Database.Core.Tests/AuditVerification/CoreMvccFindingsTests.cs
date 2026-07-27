using NUnit.Framework;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Mvcc;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>core-mvcc</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// The isolation-level entry of this dimension is settled under <c>dropin-gaps</c>: the level
/// requested through ADO.NET is silently dropped rather than leaked, because WitSqlEngine.Execute
/// builds a fresh execution context per call.
///
/// The commit-cost claim is checked by <b>counting</b> rather than by timing - a counting inner
/// store makes "scans the whole database" a deterministic observation instead of a stopwatch
/// assertion, which is the mistake the suite's own Performance/ tests already make.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class CoreMvccFindingsTests
{
    private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static string Text(byte[]? v) => v == null ? "<null>" : System.Text.Encoding.UTF8.GetString(v);

    #region READ COMMITTED: point reads and range scans disagree

    [Test]
    public void ReadCommittedPointReadAndScanAgreeTest()
    {
        // Finding: MvccTransaction.cs:158 - point reads and range scans take their snapshots from
        // different places, so a single transaction can see two different versions of the same row
        // depending on how it asked. READ COMMITTED permits seeing another transaction's commit;
        // it does not permit two reads in the same transaction to disagree about the same key.
        using var store = CreateStore(WitIsolationLevel.ReadCommitted);
        store.Put(Key("a"), Value("1"));

        using var reader = (MvccTransaction)store.BeginTransaction();
        reader.Get(Key("a"));

        using (var writer = (MvccTransaction)store.BeginTransaction())
        {
            writer.Put(Key("a"), Value("2"));
            writer.Commit();
        }

        var byPoint = Text(reader.Get(Key("a")));
        var byScan = reader.Scan(null, null)
            .Where(e => Text(e.Key) == "a")
            .Select(e => Text(e.Value))
            .FirstOrDefault() ?? "<missing>";

        Assert.That(byScan, Is.EqualTo(byPoint),
            $"the same transaction read 'a' as '{byPoint}' by key and '{byScan}' by scan");
    }

    [Test]
    public void RepeatableReadScanStillReadsTheSnapshotTest()
    {
        // The other side of the fix. Making Scan follow the isolation level must not make every
        // scan read the latest committed state: under REPEATABLE READ a rescan has to keep showing
        // the snapshot, which is the guarantee that level exists to give.
        using var store = CreateStore(WitIsolationLevel.RepeatableRead);
        store.Put(Key("a"), Value("1"));

        using var reader = (MvccTransaction)store.BeginTransaction();
        Assert.That(Text(reader.Get(Key("a"))), Is.EqualTo("1"));

        using (var writer = (MvccTransaction)store.BeginTransaction())
        {
            writer.Put(Key("a"), Value("2"));
            writer.Commit();
        }

        var byScan = reader.Scan(null, null)
            .Where(e => Text(e.Key) == "a")
            .Select(e => Text(e.Value))
            .FirstOrDefault();

        Assert.Multiple(() =>
        {
            Assert.That(Text(reader.Get(Key("a"))), Is.EqualTo("1"), "the point read holds the snapshot");
            Assert.That(byScan, Is.EqualTo("1"), "and so does the scan");
        });
    }

    #endregion

    #region SERIALIZABLE: phantoms and write skew

    [Test]
    public void SerializableDoesNotSeePhantomsTest()
    {
        // PASSES - NOT REPRODUCED, and it narrows the finding. Range reads are indeed absent from
        // the read set, but the snapshot alone already hides a row inserted after the transaction
        // began, so no phantom appears. What is missing is write-skew detection, not phantom
        // protection - see SerializableRejectsWriteSkewTest.
        using var store = CreateStore(WitIsolationLevel.Serializable);
        store.Put(Key("k1"), Value("1"));
        store.Put(Key("k2"), Value("2"));

        using var reader = (MvccTransaction)store.BeginTransaction();
        var before = reader.Scan(null, null).Count();

        using (var writer = (MvccTransaction)store.BeginTransaction())
        {
            writer.Put(Key("k3"), Value("3"));
            writer.Commit();
        }

        var after = reader.Scan(null, null).Count();

        Assert.That(after, Is.EqualTo(before),
            $"a SERIALIZABLE transaction scanned {before} rows and then {after} - a phantom appeared");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: both transactions committed and nobody was left on call - textbook write "
            + "skew. But see SerializableDoesNotSeePhantomsTest, which PASSES: phantoms are prevented "
            + "by the snapshot. So the finding's \"does not prevent phantoms OR write skew\" is half "
            + "wrong - what this actually provides is snapshot isolation, which stops phantoms and "
            + "allows write skew, and that is precisely the difference SERIALIZABLE is supposed to "
            + "close. core-mvcc, Core/Transactions/MvccTransaction.cs:381")]
    public void SerializableRejectsWriteSkewTest()
    {
        // The classic write skew: two transactions each read a range, each decides based on what it
        // read, and each writes a row the other's read would have covered. Under SERIALIZABLE one of
        // them must fail; here both read a range that is never recorded, so nothing conflicts.
        using var store = CreateStore(WitIsolationLevel.Serializable);
        store.Put(Key("on-call-alice"), Value("yes"));
        store.Put(Key("on-call-bob"), Value("yes"));

        using var first = (MvccTransaction)store.BeginTransaction();
        using var second = (MvccTransaction)store.BeginTransaction();

        // Both check "is anyone else on call?" by scanning the range.
        var firstSees = first.Scan(Key("on-call-"), Key("on-call-~")).Count();
        var secondSees = second.Scan(Key("on-call-"), Key("on-call-~")).Count();
        Assert.That(firstSees, Is.EqualTo(2));
        Assert.That(secondSees, Is.EqualTo(2));

        // Each takes itself off call, believing the other is still on.
        first.Put(Key("on-call-alice"), Value("no"));
        second.Put(Key("on-call-bob"), Value("no"));

        first.Commit();
        var secondCommitted = TryCommit(second);

        var stillOnCall = store.Scan(Key("on-call-"), Key("on-call-~"))
            .Count(e => Text(e.Value) == "yes");

        Assert.That(secondCommitted && stillOnCall == 0, Is.False,
            "both transactions committed and nobody is left on call - that is write skew, which " +
            "SERIALIZABLE must prevent");
    }

    #endregion

    #region Garbage collection never reclaims deleted keys

    [Test]
    [Ignore("CONFIRMED 2026-07-27: 50 inner records before RunNow() and 50 after - 50 keys written and all "
            + "50 deleted, with no live transaction to protect any of them, and nothing was "
            + "reclaimed. core-mvcc, Core/Stores/MvccKeyValueStore.cs:546")]
    public void GarbageCollectionReclaimsDeletedKeysTest()
    {
        // Finding: MvccKeyValueStore.cs:546 - GC never reclaims deleted keys or metadata versions,
        // so a delete-heavy workload grows the file without bound. A tombstone whose deletion is
        // older than every live transaction has nothing left to protect.
        var timestampManager = new TransactionTimestampManager();
        using var inner = new StoreInMemory();
        using var store = new MvccKeyValueStore(inner, timestampManager, ownsStore: false);
        using var collector = new MvccGarbageCollector(store, timestampManager);

        for (int i = 0; i < 50; i++)
            store.Put(Key($"k{i:D2}"), Value("v"));
        for (int i = 0; i < 50; i++)
            store.Delete(Key($"k{i:D2}"));

        var before = inner.Scan(null, null).Count();
        collector.RunNow();
        var after = inner.Scan(null, null).Count();

        TestContext.Out.WriteLine($"inner records before GC: {before}, after: {after}");

        Assert.That(after, Is.LessThan(before),
            "a tombstone older than every live transaction must be reclaimable");
    }

    #endregion

    #region Commit scans the entire store

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and counted rather than timed: committing ONE row over a 500-row store "
            + "enumerated 502 entries across 7 scans. The cost of a commit follows the size of the "
            + "database, so bulk SaveChanges is quadratic. "
            + "core-mvcc, Core/Stores/MvccKeyValueStore.cs:400")]
    public void CommitDoesNotScanTheWholeStoreTest()
    {
        // Finding: MvccKeyValueStore.cs:400 - CommitTransaction scans every record in the store to
        // find the ones belonging to the committing transaction, so committing one row costs the
        // size of the database and bulk SaveChanges becomes quadratic.
        //
        // Counted, not timed: the inner store records how many entries commit enumerates.
        var timestampManager = new TransactionTimestampManager();
        using var inner = new CountingStore();
        using var store = new MvccTransactionalStore(
            new MvccKeyValueStore(inner, timestampManager, ownsStore: false), ownsStore: false);

        for (int i = 0; i < 500; i++)
            store.Put(Key($"seed{i:D4}"), Value("v"));

        inner.ResetCounters();

        using (var tx = store.BeginTransaction())
        {
            tx.Put(Key("one"), Value("v"));
            tx.Commit();
        }

        TestContext.Out.WriteLine(
            $"committing a single row over a 500-row store enumerated " +
            $"{inner.EntriesEnumerated} entries in {inner.ScanCount} scan(s)");

        Assert.That(inner.EntriesEnumerated, Is.LessThan(100),
            "the cost of a commit must follow the size of the transaction, not of the database");
    }

    #endregion

    #region Persisted max timestamp lags the data

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and total: 0 of 10 committed rows were visible after an unflushed "
            + "restart. The watermark is written only on Flush and Dispose, so recovery reads a "
            + "timestamp that hides every row committed since the last flush - the data is on the "
            + "media and unreachable. core-mvcc, Core/Stores/MvccKeyValueStore.cs:749")]
    public void CommittedRowsSurviveAnUnflushedRestartTest()
    {
        // Finding: MvccKeyValueStore.cs:749 - the max timestamp is kept in an in-memory cache and
        // written to the store only on Flush and Dispose ("Persists to store on Flush or periodic
        // intervals"). A crash between a commit and the next flush therefore leaves the persisted
        // watermark behind the data that is already durable, and recovery reads back a timestamp
        // that makes those committed rows invisible to every transactional read.
        //
        // No process kill is needed to show it: the inner store plays the part of the durable media
        // and simply outlives the MvccKeyValueStore that never got to flush.
        using var media = new StoreInMemory();

        var writerTimestamps = new TransactionTimestampManager();
        var writer = new MvccKeyValueStore(media, writerTimestamps, ownsStore: false);
        for (int i = 0; i < 10; i++)
            writer.Put(Key($"k{i:D2}"), Value($"v{i:D2}"));

        // Crash: no Flush, no Dispose. The data is in `media`; the watermark never got there.

        var recoveredTimestamps = new TransactionTimestampManager();
        using var recovered = new MvccKeyValueStore(media, recoveredTimestamps, ownsStore: false);
        using var store = new MvccTransactionalStore(recovered, ownsStore: false);

        using var tx = store.BeginTransaction();
        var visible = Enumerable.Range(0, 10).Count(i => tx.Get(Key($"k{i:D2}")) != null);

        TestContext.Out.WriteLine($"rows visible to a transaction after an unflushed restart: {visible}/10");

        Assert.That(visible, Is.EqualTo(10),
            "rows that were committed before the crash must still be readable after recovery");
    }

    #endregion

    #region Helpers

    private static MvccTransactionalStore CreateStore(WitIsolationLevel isolationLevel) =>
        new(new StoreInMemory(), lockManager: null, isolationLevel, ownsStore: true);

    private static bool TryCommit(ITransaction transaction)
    {
        try
        {
            transaction.Commit();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// An <see cref="IKeyValueStore"/> that records how much of itself gets enumerated, so
    /// "scans the whole database" becomes a count rather than a stopwatch reading.
    /// </summary>
    private sealed class CountingStore : IKeyValueStore
    {
        private readonly StoreInMemory m_inner = new();

        public long EntriesEnumerated { get; private set; }
        public int ScanCount { get; private set; }

        public void ResetCounters()
        {
            EntriesEnumerated = 0;
            ScanCount = 0;
        }

        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
        {
            ScanCount++;
            foreach (var entry in m_inner.Scan(startKey, endKey))
            {
                EntriesEnumerated++;
                yield return entry;
            }
        }

        public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
            byte[]? startKey,
            byte[]? endKey,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var entry in Scan(startKey, endKey))
            {
                await Task.CompletedTask;
                yield return entry;
            }
        }

        public byte[]? Get(ReadOnlySpan<byte> key) => m_inner.Get(key);
        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default) =>
            m_inner.GetAsync(key, cancellationToken);
        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => m_inner.Put(key, value);
        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default) =>
            m_inner.PutAsync(key, value, cancellationToken);
        public bool Delete(ReadOnlySpan<byte> key) => m_inner.Delete(key);
        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default) =>
            m_inner.DeleteAsync(key, cancellationToken);
        public void Flush() => m_inner.Flush();
        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => m_inner.FlushAsync(cancellationToken);
        public string ProviderKey => "counting-test";
        public void Dispose() => m_inner.Dispose();
    }

    #endregion
}
