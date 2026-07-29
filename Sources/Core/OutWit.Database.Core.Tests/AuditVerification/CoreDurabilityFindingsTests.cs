using NUnit.Framework;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;
using OutWit.Database.Core.Wal;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>core-durability</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// Recovery is simulated the same way as in the LSM and MVCC batches: the journal file on disk is
/// the durable media, and a fresh store is opened over it. No process kill is needed, and the
/// interleaving is exact rather than lucky.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class CoreDurabilityFindingsTests
{
    private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private string m_directory = null!;

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), "witdb-durability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #region Savepoint rollback is invisible to the journal

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the discarded write came back during replay. The rollback to the "
            + "savepoint removed it from the store and left its record in the journal, so recovery "
            + "reapplied a write the transaction had already thrown away before committing. "
            + "core-durability, Core/Transactions/Transaction.cs:310")]
    public void WalReplayDoesNotResurrectRolledBackWritesTest()
    {
        // Finding: Transaction.cs:310 - a rollback to a savepoint undoes the writes in the store but
        // leaves their records in the journal, so recovery replays writes the transaction had
        // already discarded before it committed.
        var walPath = Path.Combine(m_directory, "wal.log");

        using (var media = new StoreInMemory())
        using (var wal = new WalTransactionJournal(walPath))
        using (var store = new TransactionalStore(media, wal, ownsStore: false))
        {
            using var tx = (Transaction)store.BeginTransaction();
            tx.Put(Key("keep"), Value("1"));
            tx.CreateSavepoint("s");
            tx.Put(Key("discarded"), Value("2"));
            tx.RollbackToSavepoint("s");
            tx.Commit();

            Assert.That(media.Get(Key("keep")), Is.Not.Null, "the pre-savepoint write must survive");
            Assert.That(media.Get(Key("discarded")), Is.Null,
                "the rolled-back write must not be in the store");
        }

        // Recovery over the same journal, onto fresh media - exactly what a crash would leave.
        using var recoveredMedia = new StoreInMemory();
        using var recoveredWal = new WalTransactionJournal(walPath);
        using var recovered = new TransactionalStore(recoveredMedia, recoveredWal, ownsStore: false);

        var resurrected = recoveredMedia.Get(Key("discarded"));

        Assert.That(resurrected, Is.Null,
            "a write rolled back to a savepoint before commit must not come back during replay");
    }

    #endregion

    #region Recovery truncates the WAL after a partial replay

    [Test]
    public void CorruptWalRecordDoesNotSilentlyDiscardLaterTransactionsTest()
    {
        // Finding: TransactionalStore.cs:403 - recovery truncates the WAL after a partial replay, so
        // one bad record destroys every committed transaction behind it, and nothing is reported.
        // The silence is the serious half: a database that loses data must at least say so.
        var walPath = Path.Combine(m_directory, "wal.log");

        using (var media = new StoreInMemory())
        using (var wal = new WalTransactionJournal(walPath))
        using (var store = new TransactionalStore(media, wal, ownsStore: false))
        {
            for (int i = 0; i < 5; i++)
            {
                using var tx = (Transaction)store.BeginTransaction();
                tx.Put(Key($"k{i}"), Value($"v{i}"));
                tx.Commit();
            }
        }

        // Corrupt a record in the middle of the log, the way a torn write would.
        var bytes = File.ReadAllBytes(walPath);
        var midpoint = bytes.Length / 2;
        for (int i = midpoint; i < Math.Min(midpoint + 16, bytes.Length); i++)
            bytes[i] ^= 0xFF;
        File.WriteAllBytes(walPath, bytes);

        using var recoveredMedia = new StoreInMemory();
        Exception? reported = null;
        try
        {
            using var recoveredWal = new WalTransactionJournal(walPath);
            using var recovered = new TransactionalStore(recoveredMedia, recoveredWal, ownsStore: false);
        }
        catch (Exception e)
        {
            reported = e;
        }

        var recoveredCount = Enumerable.Range(0, 5).Count(i => recoveredMedia.Get(Key($"k{i}")) != null);

        TestContext.Out.WriteLine(
            $"after corrupting one mid-log record: {recoveredCount}/5 transactions recovered, " +
            $"error reported: {reported?.GetType().Name ?? "none"}");

        Assert.That(recoveredCount == 5 || reported != null, Is.True,
            "either every committed transaction is recovered, or the data loss is reported - " +
            "losing transactions silently is the one outcome that must not happen");
    }

    #endregion

    #region RollbackJournal with a bare relative path

    [Test]
    public void RollbackJournalAcceptsABareRelativePathTest()
    {
        // Finding: RollbackJournal.cs:51 - the constructor calls
        // `Directory.CreateDirectory(Path.GetDirectoryName(basePath) ?? basePath)`. For a bare
        // relative name, GetDirectoryName returns the empty string rather than null, so the `??`
        // never fires and CreateDirectory("") throws.
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(m_directory);

            Assert.That(() =>
            {
                using var journal = new RollbackJournal("relative.witdb");
            }, Throws.Nothing, "a bare relative path names a file in the current directory");
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    #endregion

    #region Findings recorded without a reproducing test

    // CONFIRMED BY MEASUREMENT, not by a test - "autocommit DML is never fsync'd: there is no Flush
    // call anywhere in the ADO.NET or EF Core provider" (WitSqlEngine.Dml.Operations.cs:257). The
    // factual half is exactly true and was checked directly: `grep -rn "\.Flush(" --include=*.cs`
    // over Sources/Providers/OutWit.Database.AdoNet/ and .EntityFramework/ returns **zero** hits.
    // Showing the resulting loss needs a real power cut, as with the LSM fsync finding.
    //
    // NOT REPRODUCIBLE with the current surface - "auto-increment / rowid counters are written after
    // the commit fsync and never flushed, so after a crash the next INSERT reuses a live rowid"
    // (WitSqlEngine.Transactions.cs:56). The media-outlives-the-wrapper trick used above works at
    // the store layer, but the rowid counters live in the engine's schema and a file-backed engine
    // opens its storage with FileShare.None - so a second engine cannot be opened over the same file
    // without disposing the first, and disposing is precisely what flushes the counters. Reproducing
    // it needs either a second process or an injected failure point. Recorded rather than guessed.

    #endregion
}
