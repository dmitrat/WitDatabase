using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="ApplicationViewModel"/> singleton.
/// </summary>
[TestFixture]
public class ApplicationViewModelTests
{
    #region Fields

    private ApplicationViewModel m_appVm = null!;

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
    }

    #endregion

    #region ViewModels Tests

    [Test]
    public void ApplicationViewModelIsNotNullTest()
    {
        Assert.That(m_appVm, Is.Not.Null);
    }

    [Test]
    public void MainWindowVmIsNotNullTest()
    {
        Assert.That(m_appVm.MainWindowVm, Is.Not.Null);
    }

    [Test]
    public void ConnectionVmIsNotNullTest()
    {
        Assert.That(m_appVm.ConnectionVm, Is.Not.Null);
    }

    [Test]
    public void DatabaseExplorerVmIsNotNullTest()
    {
        Assert.That(m_appVm.DatabaseExplorerVm, Is.Not.Null);
    }

    [Test]
    public void QueryEditorVmIsNotNullTest()
    {
        Assert.That(m_appVm.QueryEditorVm, Is.Not.Null);
    }

    [Test]
    public void TableStructureVmIsNotNullTest()
    {
        Assert.That(m_appVm.TableStructureVm, Is.Not.Null);
    }

    #endregion

    #region Child ViewModels Tests

    [Test]
    public void AllChildViewModelsAreInitializedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_appVm.MainWindowVm, Is.Not.Null);
            Assert.That(m_appVm.ConnectionVm, Is.Not.Null);
            Assert.That(m_appVm.DatabaseExplorerVm, Is.Not.Null);
            Assert.That(m_appVm.QueryEditorVm, Is.Not.Null);
            Assert.That(m_appVm.TableStructureVm, Is.Not.Null);
        });
    }

    #endregion
}
