using NUnit.Framework;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The Open dialog says what it found, and asks only for what the file does not know (WS-46, WS-47).
///
/// <para>
/// Everything here is about the dialog telling the truth about an unknown path. The three states come
/// from the engine rather than from the design - see <c>StorageProbeTests</c> for the measurements -
/// and the case that matters most is the one where Studio cannot tell an encrypted database from a
/// file that is not a database at all.
/// </para>
/// </summary>
[TestFixture]
public class OpenDialogProbeTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync(connect: false);

        m_root = Path.Combine(m_studio.Root, "probe");
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region What it says

    [Test]
    public void AnUnencryptedDatabaseIsDescribedTest()
    {
        var path = Path.Combine(m_root, "plain.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        m_studio.Connection.ApplyAutoDetectedSettings(path);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.Probe.Kind, Is.EqualTo(StorageKind.Database));
            Assert.That(m_studio.Connection.ProbeMessage, Does.Contain("B-Tree"));
            Assert.That(m_studio.Connection.NeedsPassword, Is.False,
                "an unencrypted database must not be asked for a password");
        });
    }

    [Test]
    public void AnEncryptedDatabaseAsksForAPasswordAndClaimsNothingElseTest()
    {
        var path = Path.Combine(m_root, "secret.witdb");

        StudioFixture.CreateDatabaseOnDisk(path, "correct horse");

        m_studio.Connection.ApplyAutoDetectedSettings(path);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.NeedsPassword, Is.True);
            Assert.That(m_studio.Connection.ProbeMessage, Does.Not.Contain("B-Tree"),
                "the store cannot be read through the encryption, so it must not be printed");
            Assert.That(m_studio.Connection.ProbeMessage, Does.Not.Contain("MVCC"));
        });
    }

    /// <summary>
    /// The sentence that exists because the two cases are genuinely indistinguishable. Studio says
    /// both rather than picking the more confident-sounding one - which is how someone ends up typing
    /// a password at a text file and being told the password is wrong.
    /// </summary>
    [Test]
    public async Task AFileThatIsNotADatabaseSaysBothThingsAsync()
    {
        var path = Path.Combine(m_root, "notes.txt");

        await File.WriteAllTextAsync(path, new string('x', 4096));

        m_studio.Connection.ApplyAutoDetectedSettings(path);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.Probe.Kind, Is.EqualTo(StorageKind.Unreadable));
            Assert.That(m_studio.Connection.ProbeMessage, Does.Contain("not a database"));
        });
    }

    /// <summary>
    /// <b>This case is here because it went red as a control and changed the design.</b> It was written
    /// to prove that only a non-database gets the ambiguous wording - and a real encrypted database got
    /// it too, because encryption is exactly what makes the header unreadable. There is no reading that
    /// separates them, so there is one state and one sentence, and both files get it.
    ///
    /// The control that remains is the one that can be true: a READABLE database gets neither.
    /// </summary>
    [Test]
    public void AReadableDatabaseGetsNeitherTheWarningNorThePasswordBoxTest()
    {
        var path = Path.Combine(m_root, "plain.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        m_studio.Connection.ApplyAutoDetectedSettings(path);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ProbeMessage, Does.Not.Contain("not a database"));
            Assert.That(m_studio.Connection.NeedsPassword, Is.False);
        });
    }

    /// <summary>
    /// <b>Found by driving the shipping executable, and no ViewModel case could have seen it:</b> every
    /// one of them called <c>ApplyAutoDetectedSettings</c> itself, and the dialog only called it from
    /// the two Browse buttons. A path that was TYPED or pasted - which is the commonest way one arrives,
    /// and the only way a recent path does - produced no sentence at all.
    ///
    /// <para>
    /// This case says the path, and nothing else. It is the difference between the promise of 6.2 and
    /// an occasional version of it.
    /// </para>
    /// </summary>
    [Test]
    public void APathThatWasTypedIsRecognisedTest()
    {
        var path = Path.Combine(m_root, "typed.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        // Exactly what the text box does, and nothing more.
        m_studio.Connection.ConnectionInfo.FilePath = path;

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.Probe.Kind, Is.EqualTo(StorageKind.Database));
            Assert.That(m_studio.Connection.ProbeMessage, Does.Contain("B-Tree"));
        });
    }

    /// <summary>
    /// And an encrypted one typed in asks for the password without anything being pressed - which is
    /// the half of it a user notices.
    /// </summary>
    [Test]
    public void ATypedPathToAnEncryptedDatabaseAsksForThePasswordTest()
    {
        var path = Path.Combine(m_root, "typed-secret.witdb");

        StudioFixture.CreateDatabaseOnDisk(path, "correct horse");

        m_studio.Connection.ConnectionInfo.FilePath = path;

        Assert.That(m_studio.Connection.NeedsPassword, Is.True);
    }

    /// <summary>
    /// A computed property is read once when it binds unless it is told otherwise - the defect stage 8
    /// found in the section strip. Both of these are computed from the probe, so both are announced.
    /// </summary>
    [Test]
    public void ChangingThePathAnnouncesTheSentenceAndThePasswordBoxTest()
    {
        var announced = new List<string>();

        m_studio.Connection.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? string.Empty);

        var path = Path.Combine(m_root, "secret.witdb");

        StudioFixture.CreateDatabaseOnDisk(path, "correct horse");

        m_studio.Connection.ApplyAutoDetectedSettings(path);

        Assert.Multiple(() =>
        {
            Assert.That(announced, Does.Contain(nameof(m_studio.Connection.ProbeMessage)));
            Assert.That(announced, Does.Contain(nameof(m_studio.Connection.NeedsPassword)));
        });
    }

    #endregion

    #region What it refuses

    /// <summary>
    /// The engine CREATES a database it is asked to open and cannot find, which is right for a
    /// provider and wrong for a dialog called Open: a user whose file has moved would be shown an
    /// empty database and read it as their data being gone.
    /// </summary>
    [Test]
    public async Task OpeningAPathWithNothingAtItIsRefusedAsync()
    {
        await m_studio.Connection.ShowOpenDialogAsync();

        m_studio.Connection.ConnectionInfo.FilePath = Path.Combine(m_root, "gone.witdb");

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ErrorMessage, Is.Not.Null);
            Assert.That(m_studio.Connections.Sessions, Is.Empty, "and nothing was opened");
        });
    }

    /// <summary>
    /// A file too short to be a database is knowable - unlike the encrypted-or-not case - so it is
    /// refused rather than attempted.
    /// </summary>
    [Test]
    public async Task OpeningSomethingThatIsNotADatabaseIsRefusedAsync()
    {
        var path = Path.Combine(m_root, "tiny.txt");

        await File.WriteAllTextAsync(path, "hello");

        await m_studio.Connection.ShowOpenDialogAsync();

        m_studio.Connection.ConnectionInfo.FilePath = path;

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ErrorMessage, Does.Contain("not a WitDatabase"));
            Assert.That(m_studio.Connections.Sessions, Is.Empty);
        });
    }

    /// <summary>
    /// CONTROL for both refusals: a real database at a real path IS opened. Without it "nothing was
    /// opened" would pass for a dialog that refuses everything.
    /// </summary>
    [Test]
    public async Task ARealDatabaseIsStillOpenedAsync()
    {
        var path = Path.Combine(m_root, "plain.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await m_studio.Connection.ShowOpenDialogAsync();

        m_studio.Connection.ConnectionInfo.FilePath = path;

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ErrorMessage, Is.Null);
            Assert.That(m_studio.Connections.Sessions, Has.Count.EqualTo(1));
        });
    }

    #endregion
}
