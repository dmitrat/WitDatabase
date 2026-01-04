using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="DatabaseExplorerViewModel"/> context menu commands.
/// </summary>
[TestFixture]
public class DatabaseExplorerViewModelTests
{
    #region Fields

    private ApplicationViewModel m_appVm = null!;
    private DatabaseExplorerViewModel m_explorerVm = null!;

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
        m_explorerVm = m_appVm.DatabaseExplorerVm;
    }

    #endregion

    #region Command Tests

    [Test]
    public void BrowseDataCommandIsNotNullTest()
    {
        Assert.That(m_explorerVm.BrowseDataCommand, Is.Not.Null);
    }

    [Test]
    public void ViewDefinitionCommandIsNotNullTest()
    {
        Assert.That(m_explorerVm.ViewDefinitionCommand, Is.Not.Null);
    }

    [Test]
    public void DropObjectCommandIsNotNullTest()
    {
        Assert.That(m_explorerVm.DropObjectCommand, Is.Not.Null);
    }

    [Test]
    public void RefreshCommandIsNotNullTest()
    {
        Assert.That(m_explorerVm.RefreshCommand, Is.Not.Null);
    }

    #endregion

    #region CanExecute Tests

    [Test]
    public void BrowseDataCanExecuteWithTableNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table
        };

        var canExecute = m_explorerVm.BrowseDataCommand.CanExecute(null);

        Assert.That(canExecute, Is.True);
    }

    [Test]
    public void BrowseDataCanExecuteWithViewNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestView",
            NodeType = DatabaseNodeType.View
        };

        var canExecute = m_explorerVm.BrowseDataCommand.CanExecute(null);

        Assert.That(canExecute, Is.True);
    }

    [Test]
    public void BrowseDataCannotExecuteWithIndexNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestIndex",
            NodeType = DatabaseNodeType.Index
        };

        var canExecute = m_explorerVm.BrowseDataCommand.CanExecute(null);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public void ViewDefinitionCanExecuteWithViewNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestView",
            NodeType = DatabaseNodeType.View
        };

        var canExecute = m_explorerVm.ViewDefinitionCommand.CanExecute(null);

        Assert.That(canExecute, Is.True);
    }

    [Test]
    public void ViewDefinitionCanExecuteWithTriggerNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTrigger",
            NodeType = DatabaseNodeType.Trigger
        };

        var canExecute = m_explorerVm.ViewDefinitionCommand.CanExecute(null);

        Assert.That(canExecute, Is.True);
    }

    [Test]
    public void ViewDefinitionCannotExecuteWithTableNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table
        };

        var canExecute = m_explorerVm.ViewDefinitionCommand.CanExecute(null);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public void DropObjectCanExecuteWithTableNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table
        };

        var canExecute = m_explorerVm.DropObjectCommand.CanExecute(null);

        Assert.That(canExecute, Is.True);
    }

    [Test]
    public void DropObjectCannotExecuteWithDatabaseNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestDatabase",
            NodeType = DatabaseNodeType.Database
        };

        var canExecute = m_explorerVm.DropObjectCommand.CanExecute(null);

        Assert.That(canExecute, Is.False);
    }

    #endregion

    #region Node Selection Tests

    [Test]
    public void SelectedNodeCanBeSetTest()
    {
        var node = new DatabaseNode
        {
            Name = "TestNode",
            NodeType = DatabaseNodeType.Table
        };

        m_explorerVm.SelectedNode = node;

        Assert.That(m_explorerVm.SelectedNode, Is.EqualTo(node));
    }

    [Test]
    public void SelectedNodeCanBeNullTest()
    {
        m_explorerVm.SelectedNode = null;

        Assert.That(m_explorerVm.SelectedNode, Is.Null);
    }

    #endregion
}
