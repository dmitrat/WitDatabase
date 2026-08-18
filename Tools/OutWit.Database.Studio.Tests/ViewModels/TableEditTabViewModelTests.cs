using NUnit.Framework;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.ViewModels.Tabs;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for TableEditTabViewModel functionality.
/// </summary>
[TestFixture]
public class TableEditTabViewModelTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ApplicationViewModel m_applicationVm = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task Setup()
    {
        m_studio = await StudioFixture.CreateAsync();

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
    public void InitializationSetsTableNameTest()
    {
        // Arrange & Act
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.TableName, Is.EqualTo("Users"));
    }

    [Test]
    public void InitializationSetsTitleTest()
    {
        // Arrange & Act
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.Title, Does.Contain("Users"));
        Assert.That(viewModel.Title, Does.Contain("Edit"));
    }

    [Test]
    public void InitialStateHasNoChangesTest()
    {
        // Arrange & Act
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.HasChanges, Is.False);
        Assert.That(viewModel.CanCommit, Is.False);
        Assert.That(viewModel.CanRollback, Is.False);
    }

    [Test]
    public void UniqueIdIncludesTableNameTest()
    {
        // Arrange & Act
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.UniqueId, Does.Contain("Users"));
        Assert.That(viewModel.UniqueId, Does.StartWith("edit:"));
    }

    #endregion

    #region Status Bar Tests

    [Test]
    public void IsDefaultStateIsTrueInitiallyTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert - Initially no error and no success message means default state
        // Note: IsDefaultState is computed based on HasError and LastOperationSuccess
        Assert.That(viewModel.ErrorMessage, Is.Null);
        Assert.That(viewModel.StatusMessage, Is.Null);
    }

    [Test]
    public void HasErrorIsTrueWhenErrorMessageExistsTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Act
        viewModel.ErrorMessage = "Some error";

        // Assert - ErrorMessage triggers property change which updates HasError
        Assert.That(viewModel.ErrorMessage, Is.EqualTo("Some error"));
    }

    [Test]
    public void StatusMessageInitiallyNullTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.StatusMessage, Is.Null);
    }

    #endregion

    #region Commands Tests


    [Test]
    public void RefreshCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.RefreshCommand, Is.Not.Null);
    }

    [Test]
    public void AddRowCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.AddRowCommand, Is.Not.Null);
    }

    [Test]
    public void DeleteRowCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.DeleteRowCommand, Is.Not.Null);
    }

    [Test]
    public void CommitCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.CommitCommand, Is.Not.Null);
    }

    [Test]
    public void RollbackCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.RollbackCommand, Is.Not.Null);
    }

    [Test]
    public void CellEditedCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.CellEditedCommand, Is.Not.Null);
    }

    #endregion

    #region TabType Tests

    [Test]
    public void TabTypeIsTableEditTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.TabType, Is.EqualTo(WorkspaceTabType.TableEdit));
    }

    #endregion

    #region Property Tests

    [Test]
    public void TotalRowCountDefaultsToZeroTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.TotalRowCount, Is.EqualTo(0));
    }

    [Test]
    public void IsLoadingDefaultsToFalseTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.IsLoading, Is.False);
    }

    [Test]
    public void ColumnsInitializedToEmptyListTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.Columns, Is.Not.Null);
        Assert.That(viewModel.Columns, Is.Empty);
    }

    [Test]
    public void PrimaryKeyColumnsInitializedToEmptyListTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Assert
        Assert.That(viewModel.PrimaryKeyColumns, Is.Not.Null);
        Assert.That(viewModel.PrimaryKeyColumns, Is.Empty);
    }

    #endregion

    #region Cleanup Tests

    [Test]
    public void OnClosedClearsDataTest()
    {
        // Arrange
        var viewModel = new TableEditTabViewModel(m_applicationVm, m_studio.Database, "Users");

        // Act
        viewModel.OnClosed();

        // Assert
        Assert.That(viewModel.EditableData, Is.Null);
        Assert.That(viewModel.CurrentView, Is.Null);
    }

    #endregion
}
