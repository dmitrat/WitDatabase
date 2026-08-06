using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="WorkspaceTabsViewModel"/>.
/// </summary>
[TestFixture]
public class WorkspaceTabsViewModelTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ApplicationViewModel m_appVm = null!;
    private WorkspaceTabsViewModel m_workspaceVm = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task Setup()
    {
        m_studio = await StudioFixture.CreateAsync();

        m_appVm = m_studio.App;
        m_workspaceVm = m_appVm.WorkspaceTabsVm;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Initial State Tests

    [Test]
    public void InitialStateHasOneQueryTabTest()
    {
        Assert.That(m_workspaceVm.Tabs, Has.Count.EqualTo(1));
        Assert.That(m_workspaceVm.SelectedTab, Is.InstanceOf<QueryTabViewModel>());
    }

    [Test]
    public void SelectedTabIsNotNullTest()
    {
        Assert.That(m_workspaceVm.SelectedTab, Is.Not.Null);
    }

    [Test]
    public void CanExecuteQueryIsFalseInitiallyTest()
    {
        // Not connected and no SQL text
        Assert.That(m_workspaceVm.CanExecuteQuery, Is.False);
    }

    /// <summary>
    /// Closing became asynchronous when it started asking about unapplied work, and RelayCommandAsync
    /// is 'async void'. A clean tab answers without suspending, so Execute happens to run to
    /// completion inline - but relying on that is relying on an implementation detail of the thing
    /// under test. Waiting is explicit here instead.
    /// </summary>
    private async Task CloseTabAsync(WorkspaceTabViewModel? tab)
    {
        var command = (RelayCommandAsync<WorkspaceTabViewModel>)m_workspaceVm.CloseTabCommand;

        command.Execute(tab);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (command.IsExecuting)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The close command did not complete within 30 seconds.");

            await Task.Delay(5);
        }
    }

    #endregion

    #region Tab Management Tests

    [Test]
    public void NewQueryTabCommandCreatesNewTabTest()
    {
        var initialCount = m_workspaceVm.Tabs.Count;

        m_workspaceVm.NewQueryTabCommand.Execute(null);

        Assert.That(m_workspaceVm.Tabs, Has.Count.EqualTo(initialCount + 1));
    }

    [Test]
    public void OpenQueryTabReturnsNewTabTest()
    {
        var tab = m_workspaceVm.OpenQueryTab("SELECT 1", "Test Query");

        Assert.That(tab, Is.Not.Null);
        Assert.That(tab.SqlText, Is.EqualTo("SELECT 1"));
        Assert.That(tab.Title, Is.EqualTo("Test Query"));
        Assert.That(m_workspaceVm.SelectedTab, Is.EqualTo(tab));
    }

    [Test]
    public async Task CloseTabRemovesTabTest()
    {
        // Add a second tab
        m_workspaceVm.NewQueryTabCommand.Execute(null);
        var initialCount = m_workspaceVm.Tabs.Count;
        var tabToClose = m_workspaceVm.SelectedTab;

        await CloseTabAsync(tabToClose);

        Assert.That(m_workspaceVm.Tabs, Has.Count.EqualTo(initialCount - 1));
        Assert.That(m_workspaceVm.Tabs, Does.Not.Contain(tabToClose));
    }

    [Test]
    public async Task CannotCloseLastTabTest()
    {
        // Ensure only one tab
        while (m_workspaceVm.Tabs.Count > 1)
        {
            await CloseTabAsync(m_workspaceVm.Tabs.Last());
        }

        var lastTab = m_workspaceVm.SelectedTab;
        await CloseTabAsync(lastTab);

        // Tab should still be there
        Assert.That(m_workspaceVm.Tabs, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Redirected from QueryTabsViewModelTests when the legacy view model was deleted. Its two other
    /// unique cases - CurrentSqlText and MarkCurrentTabAsModified - were not redirected: those members
    /// exist only on the legacy type, nothing in the application calls them, and giving the surviving
    /// view model an API purely to keep a test alive is how legacy grows back.
    /// </summary>
    [Test]
    public void ClearResultsCommandClearsTheResultOfTheSelectedTabTest()
    {
        var tab = (QueryTabViewModel)m_workspaceVm.SelectedTab!;

        tab.ErrorMessage = "Some error";
        tab.RowsAffected = 10;
        tab.ExecutionTimeMs = 100;

        m_workspaceVm.ClearResultsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(tab.ErrorMessage, Is.Null);
            Assert.That(tab.RowsAffected, Is.Zero);
            Assert.That(tab.ExecutionTimeMs, Is.Zero);
        });
    }

    [Test]
    public void CanExecuteQueryIsFalseWithoutSqlTextTest()
    {
        ((QueryTabViewModel)m_workspaceVm.SelectedTab!).SqlText = string.Empty;

        Assert.That(m_workspaceVm.CanExecuteQuery, Is.False);
    }

    [Test]
    public void PinTabMovesToPinnedSectionTest()
    {
        // Create two tabs
        m_workspaceVm.NewQueryTabCommand.Execute(null);
        var tabToPin = m_workspaceVm.Tabs.Last();

        m_workspaceVm.PinTabCommand.Execute(tabToPin);

        Assert.That(tabToPin.IsPinned, Is.True);
        Assert.That(m_workspaceVm.Tabs.IndexOf(tabToPin), Is.EqualTo(0), "Pinned tab should be at the beginning");
    }

    [Test]
    public void UnpinTabMovesAfterPinnedTabsTest()
    {
        // Create and pin a tab
        m_workspaceVm.NewQueryTabCommand.Execute(null);
        var tabToPin = m_workspaceVm.Tabs.Last();
        m_workspaceVm.PinTabCommand.Execute(tabToPin);

        // Now unpin
        m_workspaceVm.UnpinTabCommand.Execute(tabToPin);

        Assert.That(tabToPin.IsPinned, Is.False);
    }

    #endregion
}
