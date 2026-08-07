using System.Text.Json;
using NUnit.Framework;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The saved connections (WS-68).
///
/// <para>
/// Two of these cases are about what the window does NOT do, and both are the reason it exists in this
/// shape: "Remove" takes a row out of the list and leaves the database alone, and a database that is
/// not where it was is MARKED rather than dropped. A row that disappeared on its own is
/// indistinguishable from settings that were lost, and an unmounted disk is the commonest cause.
/// </para>
/// </summary>
[TestFixture]
public class ConnectionsWindowTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ConnectionsViewModel m_connections = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync(connect: false);

        m_connections = new ConnectionsViewModel(m_studio.App);
    }

    [TearDown]
    public async Task TearDown()
    {
        m_connections.Dispose();

        await m_studio.DisposeAsync();
    }

    #endregion

    #region What opening a database leaves behind

    /// <summary>
    /// The list fills itself: a connection that opened is a connection worth offering again, with the
    /// name and the colour it was given rather than the ones it would be given afresh.
    /// </summary>
    [Test]
    public async Task OpeningADatabaseSavesItAsAConnectionAsync()
    {
        var path = Path.Combine(m_studio.Root, "saved.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await OpenAsync(path);

        await m_connections.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_connections.Rows, Has.Count.EqualTo(1));
            Assert.That(m_connections.Rows[0].Path, Is.EqualTo(path));
            Assert.That(m_connections.Rows[0].ColorIndex, Is.GreaterThanOrEqualTo(0),
                "the colour is the one the session actually has, not 'none chosen'");
        });
    }

    /// <summary>Opening the same database twice does not make two rows.</summary>
    [Test]
    public async Task OpeningTheSameDatabaseTwiceKeepsOneRowAsync()
    {
        var path = Path.Combine(m_studio.Root, "saved.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await OpenAsync(path);
        await m_studio.Connections.CloseAllAsync();
        await OpenAsync(path);

        await m_connections.RefreshAsync();

        Assert.That(m_connections.Rows, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// <b>No password reaches the file, whatever anyone sets.</b> The list is ordinary JSON beside the
    /// settings; a password in it would be a password on disk in clear text, which is B1 with a longer
    /// life. This reads the file itself rather than the model - the model is not what leaks.
    /// </summary>
    [Test]
    public async Task NoPasswordIsEverWrittenToTheListAsync()
    {
        var path = Path.Combine(m_studio.Root, "secret.witdb");

        StudioFixture.CreateDatabaseOnDisk(path, "correct horse");

        m_studio.Connection.ResetForNewDialog();
        m_studio.Connection.ConnectionInfo.FilePath = path;
        m_studio.Connection.ConnectionInfo.IsEncrypted = true;
        m_studio.Connection.ConnectionInfo.Password = "correct horse";

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);

        Assume.That(m_studio.Connections.Sessions, Has.Count.EqualTo(1), "the fixture failed to open it");

        await m_studio.Profiles.FlushAsync();

        var json = await File.ReadAllTextAsync(ListPath());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("correct horse"), "the password itself");

            // And no field to put one in. The check is for a property called exactly "Password":
            // "PasswordIsStored" is a note that one is in the operating system's store, and it is the
            // whole point of the design - the list can say a password exists without holding it.
            Assert.That(JsonDocument.Parse(json).RootElement.EnumerateArray()
                    .SelectMany(entry => entry.EnumerateObject())
                    .Select(property => property.Name),
                Has.None.EqualTo("Password"));
        });
    }

    #endregion

    #region What it does not do

    /// <summary>
    /// "Remove" removes from the LIST. Deleting a database from the interface that manages databases is
    /// a function that will one day be pressed without looking, so it is not offered - and this is the
    /// case that says the button does what its label says.
    /// </summary>
    [Test]
    public async Task RemovingAConnectionLeavesTheDatabaseWhereItIsAsync()
    {
        var path = Path.Combine(m_studio.Root, "saved.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await OpenAsync(path);
        await m_studio.Connections.CloseAllAsync();

        await m_connections.RefreshAsync();
        m_connections.Selected = m_connections.Rows[0];

        await StudioFixture.PressAsync(m_connections.RemoveCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_connections.Rows, Is.Empty, "the row is gone");
            Assert.That(File.Exists(path), Is.True, "and the database is not");
        });
    }

    /// <summary>
    /// A database that is not where it was is MARKED and kept. The disk may not be mounted; a row that
    /// vanished on its own reads as lost settings, and the next thing the user does is create a new
    /// database over the top of a path they think is empty.
    /// </summary>
    [Test]
    public async Task AMissingDatabaseIsMarkedAndKeptAsync()
    {
        var path = Path.Combine(m_studio.Root, "will-move.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await OpenAsync(path);
        await m_studio.Connections.CloseAllAsync();

        File.Delete(path);

        await m_connections.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_connections.Rows, Has.Count.EqualTo(1), "it is still offered");
            Assert.That(m_connections.Rows[0].IsMissing, Is.True, "and it says it is not there");
            Assert.That(m_connections.MissingCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// CONTROL for the case above: a database that IS there is not marked missing, so the flag follows
    /// the file system rather than being on for everyone.
    /// </summary>
    [Test]
    public async Task ADatabaseThatIsThereIsNotMarkedMissingAsync()
    {
        var path = Path.Combine(m_studio.Root, "stays.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await OpenAsync(path);
        await m_studio.Connections.CloseAllAsync();

        await m_connections.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_connections.Rows[0].IsMissing, Is.False);
            Assert.That(m_connections.Rows[0].Storage, Does.Contain("B-Tree"),
                "and it says what is actually there now, not what the profile remembers");
        });
    }

    /// <summary>Connecting to a row whose file has gone says so rather than creating one.</summary>
    [Test]
    public async Task ConnectingToAMissingDatabaseIsRefusedAsync()
    {
        var path = Path.Combine(m_studio.Root, "gone.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await OpenAsync(path);
        await m_studio.Connections.CloseAllAsync();

        File.Delete(path);

        await m_connections.RefreshAsync();
        m_connections.Selected = m_connections.Rows[0];

        await StudioFixture.PressAsync(m_connections.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_connections.ErrorMessage, Is.Not.Null);
            Assert.That(File.Exists(path), Is.False, "and nothing was created at the path it refused");
        });
    }

    #endregion

    #region Connecting

    [Test]
    public async Task ConnectingOpensTheDatabaseAsync()
    {
        var path = Path.Combine(m_studio.Root, "reopen.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        await OpenAsync(path);
        await m_studio.Connections.CloseAllAsync();

        await m_connections.RefreshAsync();
        m_connections.Selected = m_connections.Rows[0];

        await StudioFixture.PressAsync(m_connections.ConnectCommand);

        Assert.That(m_studio.Connections.Sessions, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// The name and the colour come back with it - which is the whole point of saving them, and what
    /// tells a person which database a tab belongs to.
    /// </summary>
    [Test]
    public async Task TheNameAndColourComeBackWithTheConnectionAsync()
    {
        var path = Path.Combine(m_studio.Root, "coloured.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        m_studio.Connection.ResetForNewDialog();
        m_studio.Connection.ConnectionInfo.FilePath = path;
        m_studio.Connection.ConnectionInfo.DisplayName = "production";
        m_studio.Connection.ConnectionInfo.ColorIndex = 3;

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);
        await m_studio.Connections.CloseAllAsync();

        await m_connections.RefreshAsync();
        m_connections.Selected = m_connections.Rows[0];

        await StudioFixture.PressAsync(m_connections.ConnectCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.Connections.Sessions[0].DisplayName, Is.EqualTo("production"));
            Assert.That(m_studio.Connections.Sessions[0].ColorIndex, Is.EqualTo(3));
        });
    }

    #endregion

    #region Tools

    private async Task OpenAsync(string path)
    {
        m_studio.Connection.ResetForNewDialog();
        m_studio.Connection.ConnectionInfo.FilePath = path;

        await StudioFixture.PressAsync(m_studio.Connection.ConnectCommand);
    }

    private string ListPath()
    {
        return Path.Combine(m_studio.Root, "settings", "connections.json");
    }

    #endregion
}
