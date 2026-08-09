using NUnit.Framework;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// What Studio can say about a path before it opens it (WS-47).
///
/// <para>
/// The design's Open dialog shows a line like "found a B-Tree database, 84 MB, encrypted AES-GCM,
/// MVCC" as soon as a path is chosen. Most of that turned out to be unobtainable for the case that
/// needs it most, and these cases are why the dialog has three states rather than one:
/// </para>
/// <list type="bullet">
/// <item>an ENCRYPTED database can be recognised as encrypted and as nothing else - the header lives
/// inside the encrypted page, so the store, the algorithm, MVCC and the journal are all unreadable
/// until the password is supplied;</item>
/// <item>and a file that is not a database at all is indistinguishable from one, because both fail
/// the same magic-byte check. Studio would have asked for the password to a text file.</item>
/// </list>
/// </summary>
[TestFixture]
public class StorageProbeTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // a file still held open is not a test failure
        }
    }

    #endregion

    #region What a real database looks like

    [Test]
    public void AnUnencryptedDatabaseIsDescribedInFullTest()
    {
        var path = Path.Combine(m_root, "plain.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        var probe = StorageProbe.Look(path);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Kind, Is.EqualTo(StorageKind.Database));
            Assert.That(probe.StoreType, Is.EqualTo("btree"));
            Assert.That(probe.RequiresPassword, Is.False);
            Assert.That(probe.SizeInBytes, Is.GreaterThan(0));
        });
    }

    [Test]
    public void APathThatDoesNotExistIsNotFoundTest()
    {
        var probe = StorageProbe.Look(Path.Combine(m_root, "nothing.witdb"));

        Assert.Multiple(() =>
        {
            Assert.That(probe.Kind, Is.EqualTo(StorageKind.NotFound));
            Assert.That(probe.RequiresPassword, Is.False);
        });
    }

    /// <summary>
    /// A folder with no SSTable and no manifest is not an LSM database, and the dialog says exactly
    /// that rather than offering to open it.
    /// </summary>
    [Test]
    public void AFolderWithNoDatabaseInItSaysSoTest()
    {
        var folder = Path.Combine(m_root, "exports");
        Directory.CreateDirectory(folder);

        Assert.That(StorageProbe.Look(folder).Kind, Is.EqualTo(StorageKind.NotADatabase));
    }

    #endregion

    #region The two the design could not have known about

    /// <summary>
    /// MEASURED, and it is why the banner has an "encrypted" state that says nothing else.
    /// <c>StorageDetector</c> answers an encrypted file with <c>StoreType = "btree"</c> and
    /// <c>EncryptionProvider = "unknown"</c> - the first is an assumption and the second is honest.
    /// Neither MVCC nor the journal can be read either. So the dialog must not claim to know them.
    /// </summary>
    [Test]
    public async Task AnEncryptedDatabaseCanOnlyBeRecognisedAsEncryptedAsync()
    {
        var path = Path.Combine(m_root, "secret.witdb");

        StudioFixture.CreateDatabaseOnDisk(path, "correct horse");

        var probe = StorageProbe.Look(path);

        await Task.CompletedTask;

        Assert.Multiple(() =>
        {
            Assert.That(probe.Kind, Is.EqualTo(StorageKind.Unreadable));
            Assert.That(probe.RequiresPassword, Is.True);
            Assert.That(probe.StoreType, Is.Null,
                "the store cannot be read through the encryption, so Studio must not print one");
            Assert.That(probe.EncryptionProvider, Is.Null,
                "and neither can the algorithm - 'unknown' is what the detector honestly answers");
            Assert.That(probe.SizeInBytes, Is.GreaterThan(0), "the size is the one thing the file system knows");
        });
    }

    /// <summary>
    /// And the case where the ambiguity above does NOT apply, which is the whole reason it is a
    /// separate sentence in the dialog.
    ///
    /// <para>
    /// An LSM database is a FOLDER, and its sidecar has to be readable in the clear - it is what says
    /// which encryption provider to build. So there is nothing ambiguous about an encrypted LSM
    /// database: Studio knows it is a database, knows the store, and knows the transaction model,
    /// while still not being able to read a single row without the password.
    /// </para>
    /// <para>
    /// It went the other way until 2026-08-08: detection never read the sidecar, so an encrypted LSM
    /// database was reported as needing no password at all and the failure arrived from the engine as
    /// a wrong-password error on an open nobody had been asked to authorise.
    /// </para>
    /// </summary>
    [Test]
    public void AnEncryptedLsmFolderIsKnownToBeADatabaseTest()
    {
        var plain = Path.Combine(m_root, "lsm_plain");
        var secret = Path.Combine(m_root, "lsm_secret");

        StudioFixture.CreateLsmDatabaseOnDisk(plain, password: null);
        StudioFixture.CreateLsmDatabaseOnDisk(secret, password: "correct horse");

        var open = StorageProbe.Look(plain);
        var locked = StorageProbe.Look(secret);

        Assert.Multiple(() =>
        {
            // The control: encryption is what is being measured, so the folder WITHOUT it must come
            // back openable. A probe that asked for a password everywhere would pass the half below.
            Assert.That(open.Kind, Is.EqualTo(StorageKind.Database));
            Assert.That(open.RequiresPassword, Is.False);
            Assert.That(open.HasMvcc, Is.True, "and the transaction model is read from the sidecar");

            Assert.That(locked.Kind, Is.EqualTo(StorageKind.Unreadable));
            Assert.That(locked.RequiresPassword, Is.True);
            Assert.That(locked.StoreType, Is.EqualTo("lsm"),
                "the folder said what it is before anything was decrypted, so Studio may print it");
            Assert.That(locked.EncryptionProvider, Is.Not.Empty);
        });
    }

    /// <summary>
    /// MEASURED, and this one is a defect rather than a limit: a file that is not a database at all
    /// fails the same magic-byte check an encrypted one does, so the detector reports it as an
    /// encrypted B-Tree - and Studio would have asked for the password to a text file, then blamed the
    /// password when the open failed.
    ///
    /// <para>
    /// Studio cannot distinguish them either. What it CAN do is stop claiming to: a path that carries
    /// no magic bytes is reported as "encrypted, or not a database", and the dialog says both. That is
    /// the honest sentence, and it is the one the design's "wrong password" wording was already
    /// reaching for - "wrong password, or the file is damaged".
    /// </para>
    /// </summary>
    [Test]
    public async Task AFileThatIsNotADatabaseCannotBeToldFromAnEncryptedOneAsync()
    {
        var path = Path.Combine(m_root, "notes.txt");

        await File.WriteAllTextAsync(path, new string('x', 4096));

        var probe = StorageProbe.Look(path);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Kind, Is.EqualTo(StorageKind.Unreadable),
                "the SAME answer a real encrypted database gets - the two are one state, not two");
            Assert.That(probe.RequiresPassword, Is.True);
        });
    }

    /// <summary>
    /// CONTROL, and it earned its place by going RED and changing the design. It was first written to
    /// prove that only a non-database is reported ambiguously - and a real encrypted database was too,
    /// because encryption is exactly what makes the header unreadable. There is no reading that
    /// separates them, so the separate state and its flag were deleted and the dialog says both.
    ///
    /// What is left is the half that can be true: a READABLE database is not reported as unreadable.
    /// </summary>
    [Test]
    public void AReadableDatabaseIsNotReportedAsUnreadableTest()
    {
        var path = Path.Combine(m_root, "plain.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        Assert.That(StorageProbe.Look(path).Kind, Is.EqualTo(StorageKind.Database));
    }

    /// <summary>
    /// A file too short to hold a header is not a database and is not ambiguous either - there is
    /// nothing to have been encrypted.
    /// </summary>
    [Test]
    public async Task AFileTooShortToHoldAHeaderIsNotADatabaseAsync()
    {
        var path = Path.Combine(m_root, "tiny.witdb");

        await File.WriteAllTextAsync(path, "hello");

        Assert.That(StorageProbe.Look(path).Kind, Is.EqualTo(StorageKind.NotADatabase));
    }

    /// <summary>
    /// PINS A DEFECT, NOT CORRECT BEHAVIOUR. **A database that is OPEN is reported as not a database
    /// at all** - so the Open dialog, handed the path of a database this very application has open,
    /// says there is nothing there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured 2026-08-09, and it had been derived from the «База» tab's finding without being
    /// reproduced: while a connection holds the file, <c>StorageDetector.Detect</c> answers with a
    /// null store type - the same answer it gives for a text file - and everything downstream reads
    /// that as "not a database". The lock is exclusive, so this is true even inside the process that
    /// holds it.
    /// </para>
    /// <para>
    /// <b>The proper fix is engine-side and is the phase-10 remainder's first item:</b> an open
    /// database should be able to describe itself through its connection rather than by re-reading
    /// its own file behind its own lock. When that lands, this case goes RED and should be replaced
    /// by: an open database is reported as a database.
    /// </para>
    /// <para>
    /// The control is the second half - the same path, the same file, after the connection has gone -
    /// which is what makes this a statement about the LOCK rather than about the file.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AnOpenDatabaseIsReportedAsNotADatabaseTest()
    {
        var fixture = await StudioFixture.CreateAsync();

        try
        {
            var path = fixture.Database.Connection?.FilePath;

            Assert.That(path, Is.Not.Null.And.Not.Empty);

            Assert.That(StorageProbe.Look(path).Kind, Is.EqualTo(StorageKind.NotADatabase),
                "PINS A DEFECT: an open database reads as if there were no database at the path");

            await fixture.Connections.CloseAllAsync();

            Assert.That(StorageProbe.Look(path).Kind, Is.EqualTo(StorageKind.Database),
                "and the same file answers correctly once nothing holds it - so this is the lock, "
                + "not the file");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    #endregion
}
