using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MainWindowViewModel"/> menu commands.
/// </summary>
[TestFixture]
public class MainWindowViewModelTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ApplicationViewModel m_appVm = null!;
    private MainWindowViewModel m_mainWindowVm = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task Setup()
    {
        m_studio = await StudioFixture.CreateAsync();

        m_appVm = m_studio.App;

        m_mainWindowVm = m_appVm.MainWindowVm;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Command Tests

    [Test]
    public void NewDatabaseCommandIsNotNullTest()
    {
        Assert.That(m_mainWindowVm.NewDatabaseCommand, Is.Not.Null);
    }

    [Test]
    public void OpenDatabaseCommandIsNotNullTest()
    {
        Assert.That(m_mainWindowVm.OpenDatabaseCommand, Is.Not.Null);
    }

    [Test]
    public void CloseDatabaseCommandIsNotNullTest()
    {
        Assert.That(m_mainWindowVm.CloseDatabaseCommand, Is.Not.Null);
    }

    [Test]
    public void RefreshCommandIsNotNullTest()
    {
        Assert.That(m_mainWindowVm.RefreshCommand, Is.Not.Null);
    }

    [Test]
    public void ExitCommandIsNotNullTest()
    {
        Assert.That(m_mainWindowVm.ExitCommand, Is.Not.Null);
    }

    #endregion

    #region CanExecute Tests

    [Test]
    public async Task CloseDatabaseFollowsTheConnectionTest()
    {
        // Both directions in one case. This used to set CurrentConnection = null and call that
        // "not connected" - a property of the view, not of the service, and against a double that was
        // never connected either way the difference could not show.
        Assert.That(m_mainWindowVm.CloseDatabaseCommand.CanExecute(null), Is.True,
            "connected: closing the database is offered");

        await m_studio.Connections.CloseAsync(m_studio.Database);

        Assert.That(m_mainWindowVm.CloseDatabaseCommand.CanExecute(null), Is.False,
            "disconnected: there is nothing left to close");
    }

    [Test]
    public async Task RefreshFollowsTheConnectionTest()
    {
        Assert.That(m_mainWindowVm.RefreshCommand.CanExecute(null), Is.True,
            "connected: there is a schema to refresh");

        await m_studio.Connections.CloseAsync(m_studio.Database);

        Assert.That(m_mainWindowVm.RefreshCommand.CanExecute(null), Is.False);
    }

    #endregion

    #region Properties Tests

    [Test]
    public void TitleIsNotNullTest()
    {
        Assert.That(m_mainWindowVm.Title, Is.Not.Null);
        Assert.That(m_mainWindowVm.Title, Is.Not.Empty);
    }

    [Test]
    public void StatusTextIsNotNullTest()
    {
        Assert.That(m_mainWindowVm.StatusText, Is.Not.Null);
    }

    [Test]
    public async Task IsConnectedFollowsTheConnectionNotTheDisplayedOneTest()
    {
        Assert.That(m_mainWindowVm.IsConnected, Is.True);

        // Clearing the displayed connection is not a disconnect, and must not read as one.
        m_mainWindowVm.CurrentConnection = null;

        Assert.That(m_mainWindowVm.IsConnected, Is.True,
            "the flag the whole interface binds to describes the service, not a display property");

        await m_studio.Connections.CloseAsync(m_studio.Database);

        Assert.That(m_mainWindowVm.IsConnected, Is.False);
    }

    [Test]
    public void IsLoadingDefaultsToFalseTest()
    {
        Assert.That(m_mainWindowVm.IsLoading, Is.False);
    }

    #endregion

    #region Integration Tests

    [Test]
    public void AllCommandsAreInitializedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_mainWindowVm.NewDatabaseCommand, Is.Not.Null);
            Assert.That(m_mainWindowVm.OpenDatabaseCommand, Is.Not.Null);
            Assert.That(m_mainWindowVm.CloseDatabaseCommand, Is.Not.Null);
            Assert.That(m_mainWindowVm.RefreshCommand, Is.Not.Null);
            Assert.That(m_mainWindowVm.ExitCommand, Is.Not.Null);
        });
    }

    #endregion
}
