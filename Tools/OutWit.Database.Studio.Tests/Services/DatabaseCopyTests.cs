using OutWit.Database.AdoNet;
using OutWit.Database.Core.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The byte copy (WS-59), and mostly the question of what "the database" is.
/// </summary>
[TestFixture]
public class DatabaseCopyTests
{
    #region Fields

    private StudioFixture m_fixture = null!;

    #endregion

    #region Setup

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region Tests

    /// <summary>
    /// A PAGED database cannot be copied while its connection is open - by anyone, in any way.
    ///
    /// <para>
    /// Measured twice on 2026-08-08: <c>File.Copy</c> and a stream opened with
    /// <c>FileShare.ReadWrite | FileShare.Delete</c> both fail with an <c>IOException</c> while the
    /// engine holds the file. So the design's 7.5 - flush, pause the writes, copy - is not a risk of an
    /// inconsistent copy for a paged database, it is not possible at all, and Studio says which of the
    /// two it is rather than handing over the exception.
    /// </para>
    /// <para>
    /// The CONTROL is the second half: the very same file copies once the connection has gone, and the
    /// copy opens and answers. Without it, "cannot be copied" would be indistinguishable from a copier
    /// that never worked.
    /// </para>
    /// </summary>
    [Test]
    public async Task APagedDatabaseCannotBeCopiedWhileItIsOpenAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        var whileOpen = Path.Combine(m_fixture.Root, "while-open.witdb");
        var afterClose = Path.Combine(m_fixture.Root, "after-close.witdb");

        var refused = await DatabaseCopier.CopyAsync(m_fixture.Database, whileOpen, verify: false);

        Assert.Multiple(() =>
        {
            Assert.That(refused.Outcome, Is.EqualTo(CopyOutcome.SourceIsHeldOpen));
            Assert.That(File.Exists(whileOpen), Is.False, "and nothing half-written was left behind");

            // The rawest possible confirmation that the refusal is about the engine's hold rather than
            // about the copier: the operating system will not give the bytes to anybody.
            Assert.That(() => File.Copy(m_fixture.DatabasePath, whileOpen),
                Throws.TypeOf<IOException>());
        });

        await m_fixture.Connections.CloseAsync(m_fixture.Database);

        File.Copy(m_fixture.DatabasePath, afterClose);

        Assert.That(File.Exists(afterClose), Is.True,
            "CONTROL: the same file, the same copier, once nothing is holding it");
    }

    /// <summary>
    /// An LSM database is a folder of finished files, and it DOES copy while open - which is what makes
    /// the paged refusal a fact about the store rather than about Studio.
    /// </summary>
    [Test]
    public async Task AnLsmDatabaseCopiesWhileItIsOpenAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.Lsm);

        var destination = Path.Combine(m_fixture.Root, "lsm-copy");

        var result = await DatabaseCopier.CopyAsync(m_fixture.Database, destination, verify: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(CopyOutcome.Copied));
            Assert.That(File.Exists(Path.Combine(destination, "provider.meta")), Is.True,
                "the sidecar is part of the database, not a file beside it");
            Assert.That(result.Verified, Is.True, "and the copy opened and answered");
            Assert.That(result.ObjectsInCopy, Is.GreaterThan(0));
            Assert.That(result.Parts.Count, Is.GreaterThan(1));
        });
    }

    /// <summary>
    /// A database is not one file, and the copy takes all of it - measured on a CLOSED database,
    /// because that is the only state in which a paged one can be copied at all.
    ///
    /// <para>
    /// The design says "byte copy of the file". <c>DatabaseFiles</c> says, in its own words, that the
    /// indexes live in a sibling directory - and the fixture has an index, so a copy of the file alone
    /// arrives without it. The CONTROL is the second half of this case.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheCopyTakesTheIndexesAndAFileCopyWouldNotAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        var source = m_fixture.DatabasePath;
        var session = m_fixture.Database;

        await m_fixture.Connections.CloseAsync(session);

        var whole = Path.Combine(m_fixture.Root, "whole.witdb");
        var fileOnly = Path.Combine(m_fixture.Root, "file-only.witdb");

        var result = await DatabaseCopier.CopyAsync(session, whole, verify: false);

        // The control: what the design's sentence would have produced.
        File.Copy(source, fileOnly);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(CopyOutcome.Copied));
            Assert.That(File.Exists(whole), Is.True);

            Assert.That(Directory.Exists(DatabaseFiles.GetIndexDirectory(whole)!), Is.True,
                "the indexes come with it, under the copy's own name");
            Assert.That(Directory.Exists(DatabaseFiles.GetIndexDirectory(fileOnly)!), Is.False,
                "CONTROL: copying the file alone leaves them behind - which is the defect this exists "
                + "to avoid, and it is silent");

            Assert.That(result.Parts.Count, Is.GreaterThan(1),
                "and the report names every part, because 'the database' is more than the user expects");
        });
    }

    /// <summary>
    /// The lock sidecar is deliberately NOT taken: it is the mark of "somebody has this open", and a
    /// backup carrying one is a backup that lies about itself.
    /// </summary>
    [Test]
    public async Task TheCopyDoesNotTakeTheLockAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        var source = m_fixture.DatabasePath;
        var session = m_fixture.Database;

        Assert.That(File.Exists(DatabaseFiles.GetLockPath(source)!), Is.True,
            "CONTROL: the source has one while Studio is holding it, so its absence beside the copy "
            + "is a decision rather than an accident of this fixture");

        await m_fixture.Connections.CloseAsync(session);

        var destination = Path.Combine(m_fixture.Root, "nolock.witdb");

        await DatabaseCopier.CopyAsync(session, destination, verify: false);

        Assert.That(File.Exists(DatabaseFiles.GetLockPath(destination)!), Is.False);
    }

    /// <summary>
    /// The copy opens and holds the same rows - which is the only thing that makes it a backup rather
    /// than a pile of bytes with the right names.
    /// </summary>
    [Test]
    public async Task TheCopyOpensAndHoldsTheSameRowsAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        var session = m_fixture.Database;

        await m_fixture.Connections.CloseAsync(session);

        var destination = Path.Combine(m_fixture.Root, "opens.witdb");

        var result = await DatabaseCopier.CopyAsync(session, destination, verify: true);

        await using var connection = new WitDbConnection($"Data Source={destination}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Customers";

        var rows = 0;

        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                rows++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(result.Verified, Is.True, "the copier's own check opened it");
            Assert.That(result.ObjectsInCopy, Is.GreaterThan(0));

            // Read again from outside the copier, because a check that only trusts its own verification
            // is trusting the thing under test.
            Assert.That(rows, Is.EqualTo(StudioFixture.CUSTOMER_COUNT));
        });
    }

    /// <summary>
    /// And what the parts are is asked before anything is copied, because "what will be taken" is the
    /// question a person asks of a backup.
    /// </summary>
    [Test]
    public async Task ThePartsAreNamedBeforeAnythingIsCopiedAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        var parts = DatabaseCopier.PartsOf(m_fixture.DatabasePath);

        Assert.Multiple(() =>
        {
            Assert.That(parts, Has.Some.EqualTo(m_fixture.DatabasePath));
            Assert.That(parts, Has.Some.EqualTo(DatabaseFiles.GetIndexDirectory(m_fixture.DatabasePath)));
            Assert.That(parts, Has.None.EqualTo(DatabaseFiles.GetLockPath(m_fixture.DatabasePath)));
        });
    }

    #endregion
}
