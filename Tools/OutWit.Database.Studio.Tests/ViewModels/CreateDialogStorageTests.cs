using NUnit.Framework;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The Create dialog is built from the storage choice (WS-48, WS-49).
///
/// <para>
/// The three defects of 2.0 - LSM leaving a second abandoned database beside the real one, in-memory
/// writing to disk, and in-memory connecting to a different database than the one it built - were all
/// the same mistake: the storage was two independent questions, and the path was asked for before
/// either had been answered. Stage 0 refused the impossible pair; this makes it unrepresentable, which
/// is what the cases below are about.
/// </para>
/// </summary>
[TestFixture]
public class CreateDialogStorageTests
{
    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync(connect: false);

        await m_studio.Connection.ShowCreateDialogAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region One choice of three

    /// <summary>
    /// The case the removed refusal is replaced by, and the one the ViewModel's comment points at:
    /// there is no pair to refuse because there is no pair.
    /// </summary>
    [Test]
    public void ChoosingInMemoryDropsTheLsmChoiceTest()
    {
        m_studio.Connection.ChooseStorageCommand.Execute("lsm");

        Assume.That(m_studio.Connection.SelectedStorageEngine, Is.EqualTo("lsm"));

        m_studio.Connection.ChooseStorageCommand.Execute("memory");

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.IsInMemory, Is.True);
            Assert.That(m_studio.Connection.SelectedStorageEngine, Is.Not.EqualTo("lsm"),
                "an in-memory database has no store to choose, so it cannot be an LSM one");
        });
    }

    /// <summary>
    /// And the other direction, which is the one that used to be able to argue: naming a store while
    /// the database is in memory must not quietly put it back on disk.
    /// </summary>
    [Test]
    public void NamingAStoreWhileInMemoryDoesNotPutItOnDiskTest()
    {
        m_studio.Connection.ChooseStorageCommand.Execute("memory");

        m_studio.Connection.SelectedStorageEngine = "lsm";

        Assert.That(m_studio.Connection.IsInMemory, Is.True);
    }

    /// <summary>
    /// The storage decides what the NEXT question is - which is the whole reason it is asked first.
    /// </summary>
    [TestCase("btree", true, false, false)]
    [TestCase("lsm", false, true, false)]
    [TestCase("memory", false, false, true)]
    public void TheStorageDecidesWhatIsAskedForNextTest(string storage, bool file, bool folder, bool memory)
    {
        m_studio.Connection.ChooseStorageCommand.Execute(storage);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.NeedsFile, Is.EqualTo(file));
            Assert.That(m_studio.Connection.NeedsFolder, Is.EqualTo(folder));
            Assert.That(m_studio.Connection.IsInMemory, Is.EqualTo(memory));
            Assert.That(m_studio.Connection.IsFileBased, Is.EqualTo(!memory),
                "an in-memory database is asked for no path at all");
        });
    }

    /// <summary>
    /// The label and the sentence under the box follow the choice, and both are computed - so both are
    /// announced. A computed property is read once when it binds unless it is told.
    /// </summary>
    [Test]
    public void TheLabelUnderThePathBoxFollowsTheStorageTest()
    {
        var announced = new List<string>();

        m_studio.Connection.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? string.Empty);

        m_studio.Connection.ChooseStorageCommand.Execute("btree");
        var forFile = m_studio.Connection.PathLabel;

        m_studio.Connection.ChooseStorageCommand.Execute("lsm");
        var forFolder = m_studio.Connection.PathLabel;

        Assert.Multiple(() =>
        {
            Assert.That(forFolder, Is.Not.EqualTo(forFile), "a file and a folder are not the same question");
            Assert.That(m_studio.Connection.PathHint, Does.Contain("LSM"));
            Assert.That(announced, Does.Contain(nameof(ConnectionViewModel.PathLabel)));
            Assert.That(announced, Does.Contain(nameof(ConnectionViewModel.PathHint)));
        });
    }

    #endregion

    #region Encryption

    [Test]
    public void ChoosingAnAlgorithmTurnsEncryptionOnTest()
    {
        m_studio.Connection.ChooseEncryptionCommand.Execute(ConnectionInfo.CHACHA20);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ConnectionInfo.IsEncrypted, Is.True);
            Assert.That(m_studio.Connection.IsChaCha20, Is.True);
            Assert.That(m_studio.Connection.IsAesGcm, Is.False);
            Assert.That(m_studio.Connection.IsNotEncrypted, Is.False);
        });
    }

    /// <summary>
    /// Turning encryption off clears the password rather than remembering it in a field nobody can
    /// see. That is the shape of B1 - the defect that put a password into the log file - one level in.
    /// </summary>
    [Test]
    public void TurningEncryptionOffForgetsThePasswordTest()
    {
        m_studio.Connection.ChooseEncryptionCommand.Execute(ConnectionInfo.DEFAULT_ENCRYPTION);
        m_studio.Connection.ConnectionInfo.Password = "correct horse";
        m_studio.Connection.PasswordAgain = "correct horse";

        m_studio.Connection.ChooseEncryptionCommand.Execute(string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ConnectionInfo.IsEncrypted, Is.False);
            Assert.That(m_studio.Connection.ConnectionInfo.Password, Is.Null);
            Assert.That(m_studio.Connection.PasswordAgain, Is.Null);
        });
    }

    /// <summary>
    /// The key is derived from the password and no copy is kept anywhere, so a typing mistake here is
    /// unrecoverable in a way almost nothing else in Studio is. It is the only confirm-it-twice field
    /// in either dialog, and it earns that.
    /// </summary>
    [Test]
    public async Task TwoDifferentPasswordsAreRefusedBeforeAnythingIsCreatedAsync()
    {
        var path = Path.Combine(m_studio.Root, "mistyped.witdb");

        m_studio.Connection.ChooseStorageCommand.Execute("btree");
        m_studio.Connection.ConnectionInfo.FilePath = path;
        m_studio.Connection.ChooseEncryptionCommand.Execute(ConnectionInfo.DEFAULT_ENCRYPTION);
        m_studio.Connection.ConnectionInfo.Password = "correct horse";
        m_studio.Connection.PasswordAgain = "correct hose";

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ErrorMessage, Is.Not.Null);
            Assert.That(File.Exists(path), Is.False, "and nothing was written before the refusal");
        });
    }

    /// <summary>
    /// CONTROL: the same passwords create the database. Without it the case above would pass for a
    /// dialog that refuses every encrypted database.
    /// </summary>
    [Test]
    public async Task TheSamePasswordTwiceCreatesTheDatabaseAsync()
    {
        var path = Path.Combine(m_studio.Root, "typed-right.witdb");

        m_studio.Connection.ChooseStorageCommand.Execute("btree");
        m_studio.Connection.ConnectionInfo.FilePath = path;
        m_studio.Connection.ChooseEncryptionCommand.Execute(ConnectionInfo.DEFAULT_ENCRYPTION);
        m_studio.Connection.ConnectionInfo.Password = "correct horse";
        m_studio.Connection.PasswordAgain = "correct horse";

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ErrorMessage, Is.Null);
            Assert.That(File.Exists(path), Is.True);
        });
    }

    #endregion

    #region The connection string (WS-49)

    /// <summary>
    /// Nothing about the provider is hidden: the string that will reach <c>WitDbConnection</c> can be
    /// read, and edited, from both dialogs. Not asking about the other fifteen properties is a choice;
    /// hiding them is not.
    /// </summary>
    [Test]
    public void TheConnectionStringIsShownAndEditedTest()
    {
        m_studio.Connection.ChooseStorageCommand.Execute("btree");
        m_studio.Connection.ConnectionInfo.FilePath = "D:/data/sales.witdb";

        Assume.That(m_studio.Connection.ConnectionString, Does.Contain("Data Source=D:/data/sales.witdb"));

        m_studio.Connection.ConnectionString =
            "Data Source=D:/data/other.witdb;Encryption=chacha20-poly1305;Password=correct horse;Store=lsm";

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connection.ConnectionInfo.FilePath, Is.EqualTo("D:/data/other.witdb"));
            Assert.That(m_studio.Connection.ConnectionInfo.IsEncrypted, Is.True);
            Assert.That(m_studio.Connection.ConnectionInfo.Password, Is.EqualTo("correct horse"));
            Assert.That(m_studio.Connection.ConnectionInfo.EncryptionProvider, Is.EqualTo(ConnectionInfo.CHACHA20));
            Assert.That(m_studio.Connection.SelectedStorageEngine, Is.EqualTo("lsm"));
        });
    }

    /// <summary>
    /// It is read back by the PROVIDER's own builder rather than by a parser of Studio's own: a client
    /// that disagrees with the engine about what a connection string means is worse than one that
    /// cannot show it. A string it will not accept is the user's typing and is reported, not thrown.
    /// </summary>
    [Test]
    public void AConnectionStringThatWillNotParseIsReportedTest()
    {
        m_studio.Connection.ConnectionString = "this is not a connection string";

        Assert.That(m_studio.Connection.ErrorMessage, Is.Not.Null);
    }

    #endregion
}
