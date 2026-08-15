using System.Xml.Linq;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Studio meets a database in the encryption format that preceded the crypto preamble, and offers the
/// way out instead of saying "failed to connect".
/// </summary>
/// <remarks>
/// <para>
/// <b>The engine refuses such a database since 14.0.0</b> - its salt is derived from its password and
/// stored in the clear, and its nonce counter restarts on every open - and the refusal's own message
/// says to convert it by changing its password. <b>Studio is the tool that does that</b>, so a Studio
/// that could not open one at all would leave the instruction unfollowable.
/// </para>
/// <para>
/// The fixture is the one 13.0.0 committed: written by the code at 12.8.0, the last version before
/// the format change. It must not be regenerated.
/// </para>
/// </remarks>
[TestFixture]
public class LegacyEncryptionInStudioTests
{
    #region Constants

    private const string FIXTURE = "12.8.0-encrypted.witdb";

    private const string FIXTURE_PASSWORD = "phase18-fixture";

    private const string INDEX_SUFFIX = "_indexes";

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"StudioLegacy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region The refusal, and what Studio does with it

    /// <summary>
    /// The first attempt is refused, and Studio says WHICH refusal it was rather than "failed to
    /// connect" - then offers the box that opens it.
    /// </summary>
    [Test]
    public async Task ARefusedLegacyDatabaseIsExplainedAndOfferedTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var path = CopyFixture();

        studio.Connection.ConnectionInfo.FilePath = path;
        studio.Connection.ConnectionInfo.IsEncrypted = true;
        studio.Connection.ConnectionInfo.Password = FIXTURE_PASSWORD;

        await StudioFixture.PressAsync(studio.Connection.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(studio.Connection.OpenedSession, Is.Null, "the database is refused");

            Assert.That(studio.Connections.LastOpenError, Is.InstanceOf<LegacyEncryptionException>(),
                "and the reason survives the trip back - the manager used to answer null and nothing "
                + "else, so every refusal read the same");

            Assert.That(studio.Connection.IsLegacyEncryptionOffered, Is.True,
                "so the box that opens it appears");

            Assert.That(studio.Connection.ErrorMessage, Does.Contain("13.1.0"),
                "and the sentence names the version rather than saying 'failed to connect'");
            Assert.That(studio.Connection.ErrorMessage, Does.Contain("password"),
                "and points at the conversion, which is a password change");
        });
    }

    /// <summary>
    /// With the box ticked the same database opens and answers with its rows, which is the whole
    /// point of offering it.
    /// </summary>
    [Test]
    public async Task WithTheBoxTickedTheDatabaseOpensAndAnswersTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var path = CopyFixture();

        studio.Connection.ConnectionInfo.FilePath = path;
        studio.Connection.ConnectionInfo.IsEncrypted = true;
        studio.Connection.ConnectionInfo.Password = FIXTURE_PASSWORD;
        studio.Connection.ConnectionInfo.IsLegacyEncryption = true;

        await StudioFixture.PressAsync(studio.Connection.ConnectCommand);

        var session = studio.Connection.OpenedSession;

        Assert.That(session, Is.Not.Null, "the database opens");

        var result = await session!.ExecuteQueryAsync("SELECT Name FROM Customers WHERE Id = 7");

        Assert.Multiple(() =>
        {
            Assert.That(result.Data?.Rows[0][0]?.ToString(), Is.EqualTo("Customer 7"),
                "and answers with the rows 12.8.0 wrote");

            // The conversion is the point of opening it, so it is said once, where it will be read.
            Assert.That(studio.App.Notifications.Notifications.Any(n =>
                    n.Detail != null && n.Detail.Contains("password", StringComparison.OrdinalIgnoreCase)),
                Is.True,
                "and Studio says what to do about it rather than leaving the old format as a "
                + "settled state");
        });
    }

    /// <summary>
    /// The flag reaches the engine the only way it can: through the connection string.
    /// </summary>
    [Test]
    public void TheConnectionStringCarriesTheKeywordTest()
    {
        var connection = new ConnectionInfo
        {
            FilePath = "db.witdb",
            IsEncrypted = true,
            Password = "secret",
            IsLegacyEncryption = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(connection.BuildConnectionString(), Does.Contain("Legacy Encryption=true"));

            // CONTROL: it is not written for a database that did not ask for it, and never without a
            // password - it selects an encryption scheme, so it means nothing on its own.
            connection.IsLegacyEncryption = false;
            Assert.That(connection.BuildConnectionString(), Does.Not.Contain("Legacy Encryption"));

            connection.IsLegacyEncryption = true;
            connection.IsEncrypted = false;
            Assert.That(connection.BuildConnectionString(), Does.Not.Contain("Legacy Encryption"),
                "an unencrypted database has no encryption scheme to choose");
        });
    }

    /// <summary>
    /// CONTROL: an ordinary database is not offered the box. The offer follows the engine's verdict,
    /// so a refusal for any other reason must not produce it.
    /// </summary>
    [Test]
    public async Task ControlAnOrdinaryDatabaseIsNotOfferedTheBoxTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var path = Path.Combine(m_directory, "not-a-database.witdb");

        await File.WriteAllTextAsync(path, "this is a text file");

        studio.Connection.ConnectionInfo.FilePath = path;

        await StudioFixture.PressAsync(studio.Connection.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(studio.Connection.OpenedSession, Is.Null);
            Assert.That(studio.Connection.IsLegacyEncryptionOffered, Is.False,
                "a file that is not a database is a different refusal and gets a different answer");
        });
    }

    /// <summary>
    /// And the box is on the dialog rather than only in the ViewModel - the shape this project has
    /// found three times, most recently a whole dialog no command opened.
    /// </summary>
    [Test]
    public void TheBoxIsOnTheDialogTest()
    {
        var markup = XDocument.Load(Path.Combine(StudioFolder(),
            "Views", "Dialogs", "OpenDatabaseDialog.axaml"));

        var box = markup.Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "CheckBox"
                && element.Attribute("AutomationProperties.AutomationId")?.Value
                    == "OpenDatabaseLegacyEncryption");

        Assert.That(box, Is.Not.Null, "the dialog has to draw the box the ViewModel offers");

        Assert.Multiple(() =>
        {
            Assert.That(box!.Attribute("IsVisible")?.Value,
                Does.Contain("IsLegacyEncryptionOffered"),
                "and show it only after the engine has refused such a database");
            Assert.That(box.Attribute("IsChecked")?.Value,
                Does.Contain("IsLegacyEncryption"),
                "and bind it to the flag that reaches the connection string");
        });
    }

    #endregion

    #region Tools

    private string CopyFixture()
    {
        var source = Path.Combine(FixturesFolder(), FIXTURE);
        var target = Path.Combine(m_directory, FIXTURE);

        File.Copy(source, target);

        var indexes = source + INDEX_SUFFIX;

        if (Directory.Exists(indexes))
        {
            Directory.CreateDirectory(target + INDEX_SUFFIX);

            foreach (var file in Directory.EnumerateFiles(indexes))
                File.Copy(file, Path.Combine(target + INDEX_SUFFIX, Path.GetFileName(file)));
        }

        return target;
    }

    /// <summary>
    /// The 12.8.0 fixtures live with the ADO.NET tests, which is where they were committed. Copied
    /// rather than duplicated: two copies of a fixture that must not be regenerated is two things to
    /// keep in step.
    /// </summary>
    private static string FixturesFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Sources", "Providers",
                "OutWit.Database.AdoNet.Tests", "Fixtures");

            if (File.Exists(Path.Combine(candidate, FIXTURE)))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the 12.8.0 fixtures were not found from " + AppContext.BaseDirectory);
    }

    private static string StudioFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
