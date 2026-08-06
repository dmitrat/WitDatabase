using NUnit.Framework;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for AboutViewModel functionality.
/// </summary>
[TestFixture]
public class AboutViewModelTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ApplicationViewModel m_applicationVm = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task Setup()
    {
        m_studio = await StudioFixture.CreateAsync(connect: false);

        m_applicationVm = m_studio.App;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Initialization Tests

    [Test]
    public void InitializationCreatesViewModelTest()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel, Is.Not.Null);
    }

    [Test]
    public void ProductNameIsSetTest()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.ProductName, Is.EqualTo("WitDatabase Studio"));
    }

    [Test]
    public void VersionIsNotEmptyTest()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.Version, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void CopyrightContainsCurrentYearTest()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.Copyright, Does.Contain(DateTime.Now.Year.ToString()));
        Assert.That(viewModel.Copyright, Does.Contain("Dmitry Ratner"));
    }

    [Test]
    public void DescriptionIsNotEmptyTest()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.Description, Is.Not.Null.And.Not.Empty);
    }

    #endregion

    #region URL Tests

    [Test]
    public void WebsiteUrlIsCorrectTest()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.WebsiteUrl, Is.EqualTo("https://witdatabase.io"));
    }

    [Test]
    public void AuthorUrlIsCorrectTest()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.AuthorUrl, Is.EqualTo("https://ratner.io"));
    }

    [Test]
    public void GitHubUrlIsCorrectTest()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.GitHubUrl, Does.Contain("github.com"));
        Assert.That(viewModel.GitHubUrl, Does.Contain("WitDatabase"));
    }

    #endregion

    #region Commands Tests

    [Test]
    public void OpenWebsiteCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.OpenWebsiteCommand, Is.Not.Null);
    }

    [Test]
    public void OpenAuthorCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.OpenAuthorCommand, Is.Not.Null);
    }

    [Test]
    public void OpenGitHubCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.OpenGitHubCommand, Is.Not.Null);
    }

    [Test]
    public void CloseCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new AboutViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.CloseCommand, Is.Not.Null);
    }

    #endregion

    #region DialogClosed Event Tests

    [Test]
    public void CloseCommandRaisesDialogClosedEventTest()
    {
        // Arrange
        var viewModel = new AboutViewModel(m_applicationVm);
        var eventRaised = false;
        viewModel.DialogClosed += () => eventRaised = true;

        // Act
        viewModel.CloseCommand.Execute(null);

        // Assert
        Assert.That(eventRaised, Is.True);
    }

    #endregion
}
