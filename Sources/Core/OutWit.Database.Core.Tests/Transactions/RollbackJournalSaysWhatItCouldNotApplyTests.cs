using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Transactions;

/// <summary>
/// A rollback journal that cannot be applied says so, and its file is kept.
/// </summary>
/// <remarks>
/// <para>
/// <c>RollbackJournal.Recover</c> wrapped every journal in <c>try { … } catch { }</c> under the
/// comment <i>"Skip corrupted journals"</i>, and then deleted the file it had just failed on. So a
/// journal that could not be applied left the database carrying half a transaction, took the evidence
/// with it, and told nobody. The intent was sound - one bad file must not stop a database opening -
/// and it is kept: what changes is that the failure is REPORTED and the file survives to be looked at.
/// </para>
/// <para>
/// <b>There is nowhere to log it.</b> <c>OutWit.Database.Core</c> has no <c>ILogger</c> anywhere and
/// taking a dependency for this would be a decision of a different size, so the channel is a property
/// on the journal and on the store. That is written down rather than papered over: a caller who never
/// reads it is no better off than before.
/// </para>
/// <para>
/// The WAL's half of the same question was answered in 12.x and is the model - it throws
/// <c>WalReplayException</c> rather than truncating in silence. The rollback journal is the one that
/// stayed quiet.
/// </para>
/// </remarks>
[TestFixture]
public class RollbackJournalSaysWhatItCouldNotApplyTests
{
    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb_journal_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region Tests

    /// <summary>
    /// A journal file that cannot be read is reported, kept, and does not stop the database opening.
    /// </summary>
    [Test]
    public void AJournalThatCannotBeAppliedIsReportedAndKeptTest()
    {
        var basePath = Path.Combine(m_directory, "data.witdb");
        var journalPath = basePath + "_777.rollback";

        // Not a journal at all - which is what a truncated write, a bad restore or a disk returning
        // garbage looks like from here.
        File.WriteAllBytes(journalPath, "this is not a journal"u8.ToArray());

        using var store = new StoreBTree(new StorageMemory(4096), 64, ownsStorage: true);
        using var journal = new RollbackJournal(basePath);

        var recovered = journal.Recover(store);

        Assert.Multiple(() =>
        {
            Assert.That(recovered, Is.Zero, "nothing in that file could be applied");

            Assert.That(journal.RecoveryFailures, Has.Count.EqualTo(1),
                "the failure has to reach somebody - it used to reach nobody at all");
            Assert.That(journal.RecoveryFailures[0].Path, Is.EqualTo(journalPath));
            Assert.That(journal.RecoveryFailures[0].Reason, Is.Not.Empty,
                "and say what went wrong, not merely that something did");

            Assert.That(File.Exists(journalPath), Is.True,
                "the file it could not apply is KEPT - it used to be deleted, which took the "
                + "evidence with it");
        });
    }

    /// <summary>
    /// CONTROL: a journal that CAN be applied is applied, reported as no failure, and deleted. The
    /// case above must not be passing because recovery stopped working.
    /// </summary>
    [Test]
    public void ControlAJournalThatCanBeAppliedIsAppliedAndRemovedTest()
    {
        var basePath = Path.Combine(m_directory, "control.witdb");

        using var store = new StoreBTree(new StorageMemory(4096), 64, ownsStorage: true);

        store.Put("k"u8.ToArray(), "new"u8.ToArray());

        // A journal written the way a transaction writes one: the value as it was BEFORE the change,
        // which is what a rollback restores.
        using (var writing = new RollbackJournal(basePath))
        {
            writing.BeginTransaction(1);
            writing.LogPut(1, "k"u8.ToArray(), "new"u8.ToArray(), "old"u8.ToArray());
            writing.Sync();
        }

        using var journal = new RollbackJournal(basePath);

        var recovered = journal.Recover(store);

        Assert.Multiple(() =>
        {
            Assert.That(recovered, Is.EqualTo(1), "the entry is applied");
            Assert.That(System.Text.Encoding.UTF8.GetString(store.Get("k"u8.ToArray()) ?? []),
                Is.EqualTo("old"), "and the value it restores is the one from before the change");

            Assert.That(journal.RecoveryFailures, Is.Empty, "nothing failed");
            Assert.That(Directory.GetFiles(m_directory, "control.witdb_*.rollback"), Is.Empty,
                "a journal that WAS applied is still removed - keeping it is for the ones that "
                + "could not be");
        });
    }

    /// <summary>
    /// A journal whose tail is damaged applies the prefix it can and reports the rest, rather than
    /// stopping at the damage and calling the result a success.
    /// </summary>
    /// <remarks>
    /// The entry reader's own <c>catch { break; }</c> is what made this silent: it stopped at the
    /// first unreadable entry and returned what it had, and the caller could not tell that from a
    /// journal that simply ended there.
    /// </remarks>
    [Test]
    public void AJournalWithADamagedTailAppliesThePrefixAndReportsTheRestTest()
    {
        var basePath = Path.Combine(m_directory, "tail.witdb");

        using (var writing = new RollbackJournal(basePath))
        {
            writing.BeginTransaction(2);
            writing.LogPut(2, "a"u8.ToArray(), "new-a"u8.ToArray(), "old-a"u8.ToArray());
            writing.LogPut(2, "b"u8.ToArray(), "new-b"u8.ToArray(), "old-b"u8.ToArray());
            writing.Sync();
        }

        var journalPath = Directory.GetFiles(m_directory, "tail.witdb_*.rollback").Single();
        var bytes = File.ReadAllBytes(journalPath);

        // Cut the last entry in half: a torn tail, which is what an interrupted write leaves.
        File.WriteAllBytes(journalPath, bytes[..(bytes.Length - 12)]);

        using var store = new StoreBTree(new StorageMemory(4096), 64, ownsStorage: true);
        using var journal = new RollbackJournal(basePath);

        var recovered = journal.Recover(store);

        Assert.Multiple(() =>
        {
            Assert.That(recovered, Is.GreaterThan(0), "the prefix that is intact is applied");

            Assert.That(journal.RecoveryFailures, Has.Count.EqualTo(1),
                "and the damaged tail is reported rather than looking like the end of the file");
            Assert.That(File.Exists(journalPath), Is.True,
                "and the file is kept, because what was not applied is still in it");
        });
    }

    /// <summary>
    /// A journal that cannot even be OPENED is reported too - which is a different path from a
    /// journal that opens and reads badly, and it needed its own case.
    /// </summary>
    /// <remarks>
    /// <b>Found by sabotage, not by design.</b> Restoring the old empty <c>catch</c> left every other
    /// case in this fixture green: a file that is not a journal never throws - it fails the magic
    /// check and comes back as damage - so nothing here reached the exception path, and a part whose
    /// red set is empty is a part nothing measures. A file held open by somebody else does reach it,
    /// and it is what a backup agent or an antivirus scanner looks like from here.
    /// </remarks>
    [Test]
    public void AJournalThatCannotBeOpenedIsReportedTest()
    {
        var basePath = Path.Combine(m_directory, "held.witdb");
        var journalPath = basePath + "_555.rollback";

        File.WriteAllBytes(journalPath, "anything"u8.ToArray());

        using var store = new StoreBTree(new StorageMemory(4096), 64, ownsStorage: true);
        using var journal = new RollbackJournal(basePath);

        // Held with no sharing at all, so opening it for reading throws rather than returning bytes.
        using (var held = new FileStream(journalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            journal.Recover(store);

            Assert.Multiple(() =>
            {
                Assert.That(journal.RecoveryFailures, Has.Count.EqualTo(1),
                    "a journal nothing could open is still a journal that was not applied");
                Assert.That(journal.RecoveryFailures[0].Reason, Does.Contain("IOException"),
                    "and the reason says what refused it");
            });
        }

        Assert.That(File.Exists(journalPath), Is.True, "and the file is still there");
    }

    /// <summary>
    /// Opening the database does not fail because a journal could not be applied. One bad file must
    /// not stop a database opening - that intent is the one thing worth keeping from the old code.
    /// </summary>
    [Test]
    public void ADatabaseWithAnUnrecoverableJournalStillOpensTest()
    {
        var path = Path.Combine(m_directory, "opens.witdb");

        using (var database = new WitDatabaseBuilder()
                   .WithFilePath(path).WithBTree().WithTransactions().Build())
        {
            database.Put("k"u8.ToArray(), "v"u8.ToArray());
        }

        File.WriteAllBytes(path + "_999.rollback", "not a journal"u8.ToArray());

        using var reopened = new WitDatabaseBuilder()
            .WithFilePath(path).WithBTree().WithTransactions().Build();

        Assert.That(System.Text.Encoding.UTF8.GetString(reopened.Get("k"u8.ToArray()) ?? []),
            Is.EqualTo("v"),
            "the database opens and answers; the journal it could not apply is a report, not a wall");
    }

    #endregion
}
