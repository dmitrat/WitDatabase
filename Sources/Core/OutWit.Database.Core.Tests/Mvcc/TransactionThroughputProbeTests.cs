using System.Diagnostics;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Mvcc;

/// <summary>
/// The number phase 11 measured in passing and did not chase: four writers in transactions against the
/// same rows on autocommit.
/// </summary>
/// <remarks>
/// <para>
/// Phase 11 recorded <b>181 s in transactions against 61 s on autocommit</b> for 100,000 rows written
/// by four writers - a transaction three times slower than no transaction, which is the wrong way
/// round. <c>CommitCostProbeTests</c> found the mechanism with a single writer and no contention at
/// all: the commit scanned the whole store to find the versions it had just written, so a hundred
/// commits over a growing database were quadratic.
/// </para>
/// <para>
/// This measures the consequence rather than the mechanism, because a fix for a mechanism is only a fix
/// if the number it was blamed for moves. <b>Category=Performance</b>: it writes 100,000 rows twice and
/// belongs in a deliberate run, not in every CI build.
/// </para>
/// <para>
/// Both shapes are run in one pass and reported together, so the comparison is between two numbers from
/// the same machine in the same minute rather than between one measured now and one written down in a
/// document.
/// </para>
/// </remarks>
[TestFixture]
[Category("Performance")]
public class TransactionThroughputProbeTests
{
    #region Constants

    private const int WRITERS = 4;
    private const int ROWS_PER_WRITER = 25000;
    private const int BATCH = 1000;

    #endregion

    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_txthroughput_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region The probe

    [Test]
    public void BatchedTransactionsAgainstAutocommitTest()
    {
        var autocommit = Measure(useTransactions: false, synchronousCommit: true);
        var durable = Measure(useTransactions: true, synchronousCommit: true);
        var buffered = Measure(useTransactions: true, synchronousCommit: false);

        TestContext.Out.WriteLine(
            $"THROUGHPUT {WRITERS} writers x {ROWS_PER_WRITER} rows: " +
            $"autocommit {autocommit:0.0} s | batches of {BATCH}, durable commit {durable:0.0} s " +
            $"({durable / Math.Max(0.001, autocommit):0.00}x) | batches, commit not flushed " +
            $"{buffered:0.0} s ({buffered / Math.Max(0.001, autocommit):0.00}x)");

        // Like for like: a durable commit pays a flush per batch and a Put on the store pays none, so
        // comparing those two measures durability rather than the commit path - which is how the
        // headline this probe exists for came to be quoted as "transactions are three times slower".
        // Measured here, the flush is 0.5 s of a 3.8 s difference; it is not the story.
        //
        // ATTRIBUTED, both sides on this machine in the same minutes. With the commit scanning the
        // whole store, this workload took 50.8 s against autocommit's 3.1 s - 16.3x. With the store
        // remembering what each transaction wrote: 7.2 s against 3.4 s - 2.1x, and 6.7 s unflushed.
        //
        // PINS WHAT IS LEFT, WHICH IS A DIFFERENT DEFECT. Two times is still the wrong way round for a
        // batch of a thousand rows, and the reason is structural: committing REWRITES every version a
        // second time, so a transactional write costs two writes to the store where an autocommitted
        // one costs a single write. Removing that means marking the transaction committed once and
        // resolving visibility through the transaction table on read, which is a design change and not
        // a patch. When it lands this ratio should fall towards 1 and the bound below goes red.
        // The bound is 3x rather than the measured 2.0-2.4 because this is a timing ratio on a shared
        // machine - four runs gave 1.99, 2.10, 2.24 and 2.37 - and it still catches the 16x that the
        // scan produced by an order of magnitude. A tight bound on a timing test is a flaky test.
        Assert.That(buffered, Is.LessThan(autocommit * 3.0),
            $"batched transactions took {buffered:0.0} s against {autocommit:0.0} s for the same writes " +
            "with no transaction, and neither flushes - the commit path has got worse than the double " +
            "write it is known to pay");
    }

    #endregion

    #region Tools

    /// <summary>
    /// Builds the database the way a consumer gets it, which for four writers is not optional.
    /// </summary>
    /// <remarks>
    /// The first version of this probe handed a bare <see cref="StoreBTree"/> to the transactional
    /// store, and four threads tore it apart inside a leaf split - <c>StoreBTree</c> has no locking of
    /// its own, and since 12.0.0 the builder wraps every one of them. The instrument was wrong before
    /// its subject, and the crash was the correct answer to the question it was actually asking.
    /// </remarks>
    private double Measure(bool useTransactions, bool synchronousCommit)
    {
        var path = Path.Combine(m_root,
            $"throughput_{(useTransactions ? "tx" : "auto")}_{(synchronousCommit ? "durable" : "buffered")}.witdb");

        using var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithMvcc().Build();

        var transactional = (ITransactionalStore)database.Store;

        if (transactional is MvccTransactionalStore mvcc)
            mvcc.SynchronousCommit = synchronousCommit;

        var stopwatch = Stopwatch.StartNew();

        var writers = new Thread[WRITERS];

        for (var w = 0; w < WRITERS; w++)
        {
            var writer = w;

            writers[w] = new Thread(() =>
            {
                for (var i = 0; i < ROWS_PER_WRITER; i += BATCH)
                {
                    if (useTransactions)
                    {
                        using var transaction = transactional.BeginTransaction();

                        for (var j = 0; j < BATCH; j++)
                            transaction.Put(Key(writer, i + j), Value(i + j));

                        transaction.Commit();
                    }
                    else
                    {
                        for (var j = 0; j < BATCH; j++)
                            transactional.Put(Key(writer, i + j), Value(i + j));
                    }
                }
            });

            writers[w].Start();
        }

        foreach (var thread in writers)
            thread.Join();

        stopwatch.Stop();

        return stopwatch.Elapsed.TotalSeconds;
    }

    private static byte[] Key(int writer, int i) => System.Text.Encoding.UTF8.GetBytes($"w{writer}-{i:D7}");

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"value-{i}");

    #endregion
}
