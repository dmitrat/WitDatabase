using NUnit.Framework;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for SettingsViewModel functionality.
/// </summary>
[TestFixture]
public class SettingsViewModelTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ApplicationViewModel m_applicationVm = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task Setup()
    {
        // The real SettingsService, pointed at the fixture's own folder: these cases now round-trip
        // through JSON on disk, which is where the settings a user loses actually live.
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
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel, Is.Not.Null);
    }

    [Test]
    public async Task InitializeAsyncLoadsSettingsTest()
    {
        // Arrange
        await m_studio.Settings.SaveAsync(new Settings
        {
            Theme = "Dark",
            EditorFontSize = 16,
            AutoSaveQueries = false,
            MaxRecentFiles = 5
        });
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Act
        await viewModel.InitializeAsync();

        // Assert
        Assert.That(viewModel.SelectedTheme, Is.EqualTo("Dark"));
        Assert.That(viewModel.EditorFontSize, Is.EqualTo(16));
        Assert.That(viewModel.AutoSaveQueries, Is.False);
        Assert.That(viewModel.MaxRecentFiles, Is.EqualTo(5));
    }

    #endregion

    #region Theme Tests

    [Test]
    public async Task SelectedThemeDefaultsToLightTest()
    {
        // Arrange
        await m_studio.Settings.SaveAsync(new Settings { Theme = "Light" });
        var viewModel = new SettingsViewModel(m_applicationVm);
        await viewModel.InitializeAsync();

        // Assert
        Assert.That(viewModel.SelectedTheme, Is.EqualTo("Light"));
    }

    [Test]
    public void AvailableThemesContainsLightAndDarkTest()
    {
        // Arrange
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.AvailableThemes, Contains.Item("Light"));
        Assert.That(viewModel.AvailableThemes, Contains.Item("Dark"));
    }

    #endregion

    #region Commands Tests

    [Test]
    public void SaveCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.SaveCommand, Is.Not.Null);
    }

    [Test]
    public void CancelCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.CancelCommand, Is.Not.Null);
    }

    #endregion

    #region DialogClosed Event Tests

    [Test]
    public void CancelRaisesDialogClosedEventWithFalseTest()
    {
        // Arrange
        var viewModel = new SettingsViewModel(m_applicationVm);
        bool? eventValue = null;
        viewModel.DialogClosed += (result) => eventValue = result;

        // Act
        viewModel.CancelCommand.Execute(null);

        // Assert
        Assert.That(eventValue, Is.False);
    }

    #endregion

    #region Property Tests

    [Test]
    public void EditorFontSizeCanBeSetTest()
    {
        // Arrange
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Act
        viewModel.EditorFontSize = 16;

        // Assert
        Assert.That(viewModel.EditorFontSize, Is.EqualTo(16));
    }

    [Test]
    public void AutoSaveQueriesCanBeSetTest()
    {
        // Arrange
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Act
        viewModel.AutoSaveQueries = false;

        // Assert
        Assert.That(viewModel.AutoSaveQueries, Is.False);
    }

    [Test]
    public void MaxRecentFilesCanBeSetTest()
    {
        // Arrange
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Act
        viewModel.MaxRecentFiles = 20;

        // Assert
        Assert.That(viewModel.MaxRecentFiles, Is.EqualTo(20));
    }

    [Test]
    public void AvailableFontSizesIsNotEmptyTest()
    {
        // Arrange
        var viewModel = new SettingsViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.AvailableFontSizes, Is.Not.Empty);
        Assert.That(viewModel.AvailableFontSizes, Does.Contain(14));
    }

    #endregion
}
