using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="ApplicationViewModel"/>.
/// </summary>
[TestFixture]
public class ApplicationViewModelTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ApplicationViewModel m_appVm = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task Setup()
    {
        m_studio = await StudioFixture.CreateAsync(connect: false);

        m_appVm = m_studio.App;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
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
    public void WorkspaceTabsVmIsNotNullTest()
    {
        Assert.That(m_appVm.WorkspaceTabsVm, Is.Not.Null);
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
            Assert.That(m_appVm.WorkspaceTabsVm, Is.Not.Null);
        });
    }

    #endregion
}
