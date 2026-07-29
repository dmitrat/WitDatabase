using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;
using OutWit.Database.Core.Wal;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// What recovery makes of a transaction that rolled back to a savepoint before committing.
/// </summary>
/// <remarks>
/// <c>Put</c> and <c>Delete</c> write to the journal the moment they are called, while the store
/// itself is not touched until commit. Rolling back to a savepoint used to restore only the in-memory
/// change set, so the journal kept its account of writes the transaction had thrown away and replay
/// brought them back.
///
/// The fix logs a compensating record for every key whose logged value no longer matches where the
/// transaction stands. <b>The dangerous case is not the obvious one.</b> A key created after the
/// savepoint compensates to a delete, which is easy; a key that already existed in the store and was
/// only <i>modified</i> after the savepoint must compensate to a <i>put of its original value</i> - if
/// it compensated to a delete, the rollback would destroy data the transaction never owned. Each case
/// below exists because it distinguishes a correct compensation from a plausible wrong one.
///
/// Recovery is simulated as everywhere else in this suite: the journal file is the durable media and
/// a fresh store is opened over it. No process kill, and the interleaving is exact.
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class SavepointReplayTests
{
    #region Fields

    private string m_directory = null!;
    private string m_walPath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb-savepoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
        m_walPath = Path.Combine(m_directory, "wal.log");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region Tests

    /// <summary>
    /// The case a wrong fix gets wrong: the key was already in the store, and the transaction only
    /// changed it after the savepoint. Rolling back must leave the original value, not remove it.
    /// </summary>
    [Test]
    public void RollbackRestoresAPreExistingValueRatherThanDeletingItTest()
    {
        using (var media = new StoreInMemory())
        {
            media.Put(Key("existing"), Value("original"));

            using var wal = new WalTransactionJournal(m_walPath);
            using var store = new TransactionalStore(media, wal, ownsStore: false);

            using var tx = (Transaction)store.BeginTransaction();
            tx.CreateSavepoint("s");
            tx.Put(Key("existing"), Value("changed"));
            tx.RollbackToSavepoint("s");
            tx.Commit();
        }

        // The media that survived is a fresh one: the pre-existing row has to come from the journal,
        // so this asks the recovery path rather than the store that was already holding it.
        using var recoveredMedia = new StoreInMemory();
        recoveredMedia.Put(Key("existing"), Value("original"));

        Replay(recoveredMedia);

        Assert.That(recoveredMedia.Get(Key("existing")), Is.EqualTo(Value("original")),
            "the transaction rolled its change away before committing, so the value the store held "
            + "all along must survive recovery - compensating with a delete here would destroy a row "
            + "the transaction never owned");
    }

    /// <summary>
    /// A key deleted after the savepoint has to come back.
    /// </summary>
    [Test]
    public void RollbackUndoesADeleteMadeAfterTheSavepointTest()
    {
        using (var media = new StoreInMemory())
        {
            media.Put(Key("doomed"), Value("alive"));

            using var wal = new WalTransactionJournal(m_walPath);
            using var store = new TransactionalStore(media, wal, ownsStore: false);

            using var tx = (Transaction)store.BeginTransaction();
            tx.CreateSavepoint("s");
            tx.Delete(Key("doomed"));
            tx.RollbackToSavepoint("s");
            tx.Commit();
        }

        using var recoveredMedia = new StoreInMemory();
        recoveredMedia.Put(Key("doomed"), Value("alive"));

        Replay(recoveredMedia);

        Assert.That(recoveredMedia.Get(Key("doomed")), Is.EqualTo(Value("alive")),
            "the delete was rolled back before the commit, so replay must not carry it out");
    }

    /// <summary>
    /// The control: everything written <i>before</i> the savepoint still has to survive. Without it,
    /// a compensation that simply discarded the transaction would pass every other test here.
    /// </summary>
    [Test]
    public void ControlWritesBeforeTheSavepointSurviveTest()
    {
        using (var media = new StoreInMemory())
        {
            using var wal = new WalTransactionJournal(m_walPath);
            using var store = new TransactionalStore(media, wal, ownsStore: false);

            using var tx = (Transaction)store.BeginTransaction();
            tx.Put(Key("kept"), Value("1"));
            tx.CreateSavepoint("s");
            tx.Put(Key("discarded"), Value("2"));
            tx.RollbackToSavepoint("s");
            tx.Put(Key("after"), Value("3"));
            tx.Commit();
        }

        using var recoveredMedia = new StoreInMemory();
        Replay(recoveredMedia);

        Assert.Multiple(() =>
        {
            Assert.That(recoveredMedia.Get(Key("kept")), Is.EqualTo(Value("1")),
                "a write made before the savepoint is not affected by rolling back to it");

            Assert.That(recoveredMedia.Get(Key("after")), Is.EqualTo(Value("3")),
                "and a write made after the rollback is part of the committed transaction");

            Assert.That(recoveredMedia.Get(Key("discarded")), Is.Null,
                "only the discarded write is gone");
        });
    }

    /// <summary>
    /// A key written after the savepoint, rolled back, and then written again - the compensation must
    /// not outlive the value that replaced it.
    /// </summary>
    [Test]
    public void RewritingAfterTheRollbackWinsOverTheCompensationTest()
    {
        using (var media = new StoreInMemory())
        {
            using var wal = new WalTransactionJournal(m_walPath);
            using var store = new TransactionalStore(media, wal, ownsStore: false);

            using var tx = (Transaction)store.BeginTransaction();
            tx.CreateSavepoint("s");
            tx.Put(Key("k"), Value("first"));
            tx.RollbackToSavepoint("s");
            tx.Put(Key("k"), Value("second"));
            tx.Commit();
        }

        using var recoveredMedia = new StoreInMemory();
        Replay(recoveredMedia);

        Assert.That(recoveredMedia.Get(Key("k")), Is.EqualTo(Value("second")),
            "the compensating delete is logged before the second write, and the replay applies a "
            + "transaction's records in order - so the value written after the rollback must stand");
    }

    #endregion

    #region Tools

    private void Replay(StoreInMemory media)
    {
        using var wal = new WalTransactionJournal(m_walPath);
        using var store = new TransactionalStore(media, wal, ownsStore: false);
    }

    private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    #endregion
}
