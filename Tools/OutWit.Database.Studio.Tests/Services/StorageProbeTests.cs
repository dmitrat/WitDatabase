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
            Assert.That(probe.Kind, Is.EqualTo(StorageKind.Encrypted));
            Assert.That(probe.RequiresPassword, Is.True);
            Assert.That(probe.StoreType, Is.Null,
                "the store cannot be read through the encryption, so Studio must not print one");
            Assert.That(probe.EncryptionProvider, Is.Null,
                "and neither can the algorithm - 'unknown' is what the detector honestly answers");
            Assert.That(probe.SizeInBytes, Is.GreaterThan(0), "the size is the one thing the file system knows");
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
            Assert.That(probe.Kind, Is.EqualTo(StorageKind.Encrypted));
            Assert.That(probe.RequiresPassword, Is.True);
            Assert.That(probe.CouldAlsoBeSomethingElse, Is.True,
                "the dialog has to say 'encrypted, or not a database' - it cannot know which");
        });
    }

    /// <summary>
    /// CONTROL for the case above: a real unencrypted database is NOT reported ambiguously, so the
    /// flag is measuring the missing magic bytes and not simply always true.
    /// </summary>
    [Test]
    public void ARealDatabaseIsNotReportedAsAmbiguousTest()
    {
        var path = Path.Combine(m_root, "plain.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        Assert.That(StorageProbe.Look(path).CouldAlsoBeSomethingElse, Is.False);
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

    #endregion
}
