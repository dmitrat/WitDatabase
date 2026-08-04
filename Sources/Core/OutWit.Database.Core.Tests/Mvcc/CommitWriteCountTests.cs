using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Mvcc;

/// <summary>
/// How many writes to the underlying store does one transactional write cost?
/// </summary>
/// <remarks>
/// <para>
/// Phase 11 measured a transaction at 2.1x autocommit for the same rows and named the reason
/// structurally rather than by measurement: <b>a commit rewrites every version a second time</b>. The
/// timestamp is allocated first, each version is installed carrying the transaction id so it looks
/// uncommitted, and <c>CommitTransaction</c> then rewrites every one of them to clear the id and stamp
/// the timestamp. The second write changes a marker whose final value was known before the first write
/// was made.
/// </para>
/// <para>
/// <b>This counts rather than times.</b> A timing says the transaction is slower and leaves the reason
/// to inference; a count of the writes that reach the store is the claim itself, and it does not move
/// with the machine, the page cache or the load. Phase 4 learned the same thing about durability - a
/// store that never asks for a flush cannot have achieved one, and zero is unambiguous where a
/// millisecond is not.
/// </para>
/// <para>
/// <b>The control is autocommit</b>, in the same fixture and through the same counter: writing the same
/// rows without a transaction costs one store write each, so the transactional figure is compared with
/// a measured baseline rather than with the number of rows.
/// </para>
/// </remarks>
[TestFixture]
public class CommitWriteCountTests
{
    #region Types

    /// <summary>
    /// Counts what reaches the store underneath the MVCC layer. Everything else is passed straight
    /// through, so the store being measured behaves exactly as it does in a real database.
    /// </summary>
    private sealed class CountingStore(IKeyValueStore inner) : IKeyValueStore
    {
        public int Writes;
        public int Deletes;

        public string ProviderKey => inner.ProviderKey;

        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            Interlocked.Increment(ref Writes);
            inner.Put(key, value);
        }

        public bool Delete(ReadOnlySpan<byte> key)
        {
            Interlocked.Increment(ref Deletes);
            return inner.Delete(key);
        }

        public byte[]? Get(ReadOnlySpan<byte> key) => inner.Get(key);

        public bool ContainsKey(ReadOnlySpan<byte> key) => inner.ContainsKey(key);

        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey) =>
            inner.Scan(startKey, endKey);

        public IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(byte[]? startKey, byte[]? endKey,
            CancellationToken cancellationToken = default) =>
            inner.ScanAsync(startKey, endKey, cancellationToken);

        public void Flush() => inner.Flush();

        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default) =>
            inner.GetAsync(key, cancellationToken);

        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Writes);
            return inner.PutAsync(key, value, cancellationToken);
        }

        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Deletes);
            return inner.DeleteAsync(key, cancellationToken);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            inner.FlushAsync(cancellationToken);

        public void Dispose() => inner.Dispose();
    }

    #endregion

    #region Constants

    private const int ROWS = 50;

    #endregion

    #region Probes

    /// <summary>
    /// Probe: the store writes one transactional put costs, against the autocommit control.
    /// </summary>
    [Test]
    public void ProbeWhatATransactionalWriteCostsTest()
    {
        var (transactional, autocommit) = Measure();

        TestContext.Out.WriteLine($"{ROWS} rows, autocommit:        {autocommit} store writes");
        TestContext.Out.WriteLine($"{ROWS} rows, one transaction:   {transactional} store writes");
        TestContext.Out.WriteLine($"ratio: {(double)transactional / autocommit:F2}x");

        Assert.That(autocommit, Is.EqualTo(ROWS),
            "The control: an autocommitted write must reach the store exactly once. If this is not the " +
            "row count, the counter is measuring something other than the writes and the pin below " +
            "means nothing.");

        // PINS A DEFECT, NOT CORRECT BEHAVIOUR.
        //
        // A transactional write costs TWO store writes where an autocommitted one costs a single write.
        // The commit allocates its timestamp first, installs each version stamped with the transaction
        // id so it looks uncommitted, and then rewrites every one of them to clear the id - a second
        // write that changes a marker whose final value was known before the first write was made. The
        // extra one write is the max-timestamp record the commit persists.
        //
        // TO INVERT WHEN IT IS FIXED: this becomes ROWS + 1. See the fixture's remarks and
        // Docs/PHASE12-DATABASE-HEADER-PLAN.md § 6.2 for what the fix needs first - the blocker is
        // deeper than "pass the marker separately", because the per-transaction write set conflates the
        // versions a transaction CREATED with the earlier committed versions it MARKED DELETED, and a
        // rollback that worked from it undivided would delete the previous version outright.
        Assert.That(transactional, Is.EqualTo(2 * ROWS + 1),
            "The double write moved. If this is now ROWS + 1 the fix has landed and this assertion " +
            "should be inverted; any other number means something else changed in the commit path.");
    }

    /// <summary>
    /// A rolled-back overwrite leaves the original value, not a mixture and not a hole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written as the guard the double-write fix has to keep green, and it is the test that showed the
    /// fix needs more than the recorded blocker said. A rollback recognises its own records by the
    /// transaction id they carry; installing them already committed removes that marker, so the
    /// rollback would have to work from the per-transaction write set instead - <b>and that set is not
    /// only the versions the transaction created</b>. <c>MarkPreviousVersionDeleted</c> adds the
    /// EARLIER committed version's key to it as well, so that a transaction which deletes its own row
    /// can find it again at commit without a scan.
    /// </para>
    /// <para>
    /// Deleting everything in that set on rollback would therefore delete the previous committed
    /// version outright, which is this assertion failing as data loss rather than as a wrong marker.
    /// Today that version survives because the rollback's filter skips it and its delete stamp carries
    /// a timestamp that was never published.
    /// </para>
    /// </remarks>
    [Test]
    public void ARolledBackTransactionLeavesNothingTest()
    {
        using var inner = new StoreInMemory();
        using var store = new MvccTransactionalStore(inner, ownsStore: false);

        using (var transaction = store.BeginTransaction())
        {
            for (var i = 0; i < ROWS; i++)
                transaction.Put(Key(i), Value(i));

            transaction.Commit();
        }

        using (var transaction = store.BeginTransaction())
        {
            for (var i = 0; i < ROWS; i++)
                transaction.Put(Key(i), "rolled back"u8.ToArray());

            transaction.Rollback();
        }

        using var reader = store.BeginTransaction();

        for (var i = 0; i < ROWS; i++)
        {
            Assert.That(reader.Get(Key(i)), Is.EqualTo(Value(i)),
                $"row {i} carries the rolled-back value, so the rollback did not undo the write");
        }
    }

    /// <summary>
    /// An uncommitted transaction's writes are invisible to another transaction, which is what the
    /// commit timestamp - not the marker on the record - is supposed to guarantee.
    /// </summary>
    [Test]
    public void AnUncommittedWriteIsInvisibleToAnotherTransactionTest()
    {
        using var inner = new StoreInMemory();
        using var store = new MvccTransactionalStore(inner, ownsStore: false);

        using (var seed = store.BeginTransaction())
        {
            seed.Put(Key(0), Value(0));
            seed.Commit();
        }

        using var writer = store.BeginTransaction();
        writer.Put(Key(0), "uncommitted"u8.ToArray());

        using var reader = store.BeginTransaction();

        Assert.That(reader.Get(Key(0)), Is.EqualTo(Value(0)),
            "a second transaction saw a write the first has not committed");
    }

    #endregion

    #region Tools

    private static (int Transactional, int Autocommit) Measure()
    {
        int transactional;
        int autocommit;

        using (var inner = new StoreInMemory())
        {
            var counter = new CountingStore(inner);

            using var store = new MvccTransactionalStore(counter, ownsStore: false);
            using var transaction = store.BeginTransaction();

            for (var i = 0; i < ROWS; i++)
                transaction.Put(Key(i), Value(i));

            // Counted across the commit, because that is where an MVCC transaction's writes happen:
            // Put buffers into an in-memory change set and the store is not touched until Commit.
            var before = counter.Writes;
            transaction.Commit();
            transactional = counter.Writes - before;
        }

        using (var inner = new StoreInMemory())
        {
            var counter = new CountingStore(inner);

            using var store = new MvccKeyValueStore(counter, new TransactionTimestampManager(), ownsStore: false);

            var before = counter.Writes;

            for (var i = 0; i < ROWS; i++)
                store.Put(Key(i), Value(i));

            autocommit = counter.Writes - before;
        }

        return (transactional, autocommit);
    }

    private static byte[] Key(int index) => System.Text.Encoding.UTF8.GetBytes($"key{index:D4}");

    private static byte[] Value(int index) => System.Text.Encoding.UTF8.GetBytes($"value{index:D4}");

    #endregion
}
