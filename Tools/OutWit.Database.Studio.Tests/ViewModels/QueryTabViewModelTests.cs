using NUnit.Framework;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.ViewModels.Tabs;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for QueryTabViewModel functionality.
/// </summary>
[TestFixture]
public class QueryTabViewModelTests
{
    #region Fields

    private ApplicationViewModel m_applicationVm = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        var databaseService = new FakeDatabaseService();
        m_applicationVm = new ApplicationViewModel(
            databaseService,
            new FakeSettingsService(),
            new FakeExportService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplicationViewModel>.Instance);
    }

    #endregion

    #region Initialization Tests

    [Test]
    public void InitialStateHasDefaultValuesTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.TotalRowCount, Is.EqualTo(0));
        Assert.That(viewModel.HasResults, Is.False);
        Assert.That(viewModel.SelectedRows, Is.Null);
        Assert.That(viewModel.ResultData, Is.Null);
        Assert.That(viewModel.CurrentView, Is.Null);
    }

    [Test]
    public void TitleCanBeSetTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        
        // Act
        viewModel.Title = "Query 1";

        // Assert
        Assert.That(viewModel.Title, Is.EqualTo("Query 1"));
    }

    [Test]
    public void SqlTextDefaultsToEmptyTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.SqlText, Is.EqualTo(string.Empty));
    }

    #endregion

    #region Null Result Tests

    [Test]
    public void SetResultDataWithNullClearsResultsTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Act
        viewModel.SetResultData(null);

        // Assert
        Assert.That(viewModel.TotalRowCount, Is.EqualTo(0));
        Assert.That(viewModel.HasResults, Is.False);
        Assert.That(viewModel.CurrentView, Is.Null);
        Assert.That(viewModel.ResultData, Is.Null);
    }

    #endregion

    #region Clear Results Tests

    [Test]
    public void ClearResultsClearsAllDataTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        viewModel.ErrorMessage = "Some error";
        viewModel.RowsAffected = 10;
        viewModel.ExecutionTimeMs = 100;

        // Act
        viewModel.ClearResults();

        // Assert
        Assert.That(viewModel.ResultData, Is.Null);
        Assert.That(viewModel.CurrentView, Is.Null);
        Assert.That(viewModel.TotalRowCount, Is.EqualTo(0));
        Assert.That(viewModel.ErrorMessage, Is.Null);
        Assert.That(viewModel.RowsAffected, Is.EqualTo(0));
        Assert.That(viewModel.ExecutionTimeMs, Is.EqualTo(0));
        Assert.That(viewModel.SelectedRows, Is.Null);
    }

    #endregion

    #region Display Title Tests

    [Test]
    public void DisplayTitleShowsModificationIndicatorTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        viewModel.Title = "Query 1";
        viewModel.IsModified = false;

        // Assert - No indicator when not modified
        Assert.That(viewModel.DisplayTitle, Is.EqualTo("Query 1"));

        // Act
        viewModel.IsModified = true;

        // Assert - Shows • when modified
        Assert.That(viewModel.DisplayTitle, Is.EqualTo("Query 1 •"));
    }

    #endregion

    #region CanCopyRows Tests

    [Test]
    public void CanCopyRowsIsFalseWhenNoResultsTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.CanCopyRows, Is.False);
    }

    #endregion

    #region Command Tests

    [Test]
    public void CopyRowsCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.CopyRowsCommand, Is.Not.Null);
    }

    [Test]
    public void CopyRowsAsInsertCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.CopyRowsAsInsertCommand, Is.Not.Null);
    }

    [Test]
    public void CopyRowsAsCsvCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.CopyRowsAsCsvCommand, Is.Not.Null);
    }

    [Test]
    public void CopyAllRowsCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.CopyAllRowsCommand, Is.Not.Null);
    }

    [Test]
    public void CopyAllRowsAsInsertCommandIsNotNullTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);

        // Assert
        Assert.That(viewModel.CopyAllRowsAsInsertCommand, Is.Not.Null);
    }

    #endregion

    #region SetResultData Tests

    [Test]
    public void SetResultDataWithEmptyTableSetsZeroRowCountTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        var dataTable = new System.Data.DataTable();
        dataTable.Columns.Add("Id", typeof(int));

        // Act
        viewModel.SetResultData(dataTable);

        // Assert
        Assert.That(viewModel.TotalRowCount, Is.EqualTo(0));
        Assert.That(viewModel.HasResults, Is.False);
        Assert.That(viewModel.CurrentView, Is.Null);
    }

    [Test]
    public void SetResultDataWithRowsSetsCorrectCountTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        var dataTable = new System.Data.DataTable();
        dataTable.Columns.Add("Id", typeof(int));
        dataTable.Rows.Add(1);
        dataTable.Rows.Add(2);
        dataTable.Rows.Add(3);

        // Act
        viewModel.SetResultData(dataTable);

        // Assert
        Assert.That(viewModel.TotalRowCount, Is.EqualTo(3));
        Assert.That(viewModel.HasResults, Is.True);
        Assert.That(viewModel.CurrentView, Is.Not.Null);
        Assert.That(viewModel.ResultData, Is.SameAs(dataTable));
    }

    #endregion

    #region Status Tests

    [Test]
    public void IsSuccessIsTrueWhenNoErrorMessageTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        viewModel.ErrorMessage = null;

        // Assert
        Assert.That(viewModel.IsSuccess, Is.True);
    }

    [Test]
    public void IsSuccessIsFalseWhenErrorMessageExistsTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        viewModel.ErrorMessage = "Error occurred";

        // Assert
        Assert.That(viewModel.IsSuccess, Is.False);
    }

    [Test]
    public void HasMessagesIsTrueWhenRowsAffectedGreaterThanZeroTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        viewModel.RowsAffected = 5;

        // Assert
        Assert.That(viewModel.HasMessages, Is.True);
    }

    [Test]
    public void HasMessagesIsTrueWhenErrorMessageExistsTest()
    {
        // Arrange
        var viewModel = new QueryTabViewModel(m_applicationVm);
        viewModel.ErrorMessage = "Error";

        // Assert
        Assert.That(viewModel.HasMessages, Is.True);
    }

    #endregion
}
