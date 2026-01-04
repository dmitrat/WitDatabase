using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MainWindowViewModel"/> menu commands.
/// </summary>
[TestFixture]
public class MainWindowViewModelTests
{
    #region Fields

    private ApplicationViewModel m_appVm = null!;
    private MainWindowViewModel m_mainWindowVm = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ApplicationViewModel>();

        var serviceProvider = services.BuildServiceProvider();

        m_appVm = serviceProvider.GetRequiredService<ApplicationViewModel>();
        m_mainWindowVm = m_appVm.MainWindowVm;
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
    public void CloseDatabaseCannotExecuteWhenNotConnectedTest()
    {
        m_mainWindowVm.CurrentConnection = null;

        var canExecute = m_mainWindowVm.CloseDatabaseCommand.CanExecute(null);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public void RefreshCannotExecuteWhenNotConnectedTest()
    {
        m_mainWindowVm.CurrentConnection = null;

        var canExecute = m_mainWindowVm.RefreshCommand.CanExecute(null);

        Assert.That(canExecute, Is.False);
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
    public void IsConnectedReturnsFalseWhenNoConnectionTest()
    {
        m_mainWindowVm.CurrentConnection = null;

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
