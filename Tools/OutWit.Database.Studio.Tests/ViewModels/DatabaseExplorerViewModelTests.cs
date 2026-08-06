using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="DatabaseExplorerViewModel"/> context menu commands.
/// </summary>
[TestFixture]
public class DatabaseExplorerViewModelTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ApplicationViewModel m_appVm = null!;
    private DatabaseExplorerViewModel m_explorerVm = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task Setup()
    {
        m_studio = await StudioFixture.CreateAsync();

        m_appVm = m_studio.App;

        m_explorerVm = m_appVm.DatabaseExplorerVm;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Command Tests

    [Test]
    public void SelectTop100CommandIsNotNullTest()
    {
        Assert.That(m_explorerVm.SelectTop100Command, Is.Not.Null);
    }

    [Test]
    public void SelectTop1000CommandIsNotNullTest()
    {
        Assert.That(m_explorerVm.SelectTop1000Command, Is.Not.Null);
    }

    [Test]
    public void ViewDefinitionCommandIsNotNullTest()
    {
        Assert.That(m_explorerVm.ViewDefinitionCommand, Is.Not.Null);
    }

    [Test]
    public void ViewStructureCommandIsNotNullTest()
    {
        Assert.That(m_explorerVm.ViewStructureCommand, Is.Not.Null);
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

    #region CanBrowseData Tests

    [Test]
    public void CanBrowseDataWithTableNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanBrowseData, Is.True);
    }

    [Test]
    public void CanBrowseDataWithViewNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestView",
            NodeType = DatabaseNodeType.View,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanBrowseData, Is.True);
    }

    [Test]
    public void CannotBrowseDataWithIndexNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestIndex",
            NodeType = DatabaseNodeType.Index,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanBrowseData, Is.False);
    }

    [Test]
    public void CannotBrowseDataWithNullNodeTest()
    {
        m_explorerVm.SelectedNode = null;

        Assert.That(m_explorerVm.CanBrowseData, Is.False);
    }

    #endregion

    #region CanViewStructure Tests

    [Test]
    public void CanViewStructureWithTableNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewStructure, Is.True);
    }

    [Test]
    public void CanViewStructureWithViewNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestView",
            NodeType = DatabaseNodeType.View,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewStructure, Is.True);
    }

    [Test]
    public void CanViewStructureWithIndexNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestIndex",
            NodeType = DatabaseNodeType.Index,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewStructure, Is.True);
    }

    [Test]
    public void CannotViewStructureWithTriggerNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTrigger",
            NodeType = DatabaseNodeType.Trigger,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewStructure, Is.False);
    }

    #endregion

    #region CanViewDefinition Tests

    [Test]
    public void CanViewDefinitionWithTableNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewDefinition, Is.True);
    }

    [Test]
    public void CanViewDefinitionWithViewNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestView",
            NodeType = DatabaseNodeType.View,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewDefinition, Is.True);
    }

    [Test]
    public void CanViewDefinitionWithTriggerNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTrigger",
            NodeType = DatabaseNodeType.Trigger,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewDefinition, Is.True);
    }

    [Test]
    public void CanViewDefinitionWithIndexNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestIndex",
            NodeType = DatabaseNodeType.Index,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewDefinition, Is.True);
    }

    [Test]
    public void CannotViewDefinitionWithSequenceNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestSequence",
            NodeType = DatabaseNodeType.Sequence,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanViewDefinition, Is.False);
    }

    [Test]
    public void CannotViewDefinitionWithNullNodeTest()
    {
        m_explorerVm.SelectedNode = null;

        Assert.That(m_explorerVm.CanViewDefinition, Is.False);
    }

    #endregion

    #region CanDropObject Tests

    [Test]
    public void CanDropObjectWithTableNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanDropObject, Is.True);
    }

    [Test]
    public void CanDropObjectWithViewNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestView",
            NodeType = DatabaseNodeType.View,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanDropObject, Is.True);
    }

    [Test]
    public void CanDropObjectWithIndexNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestIndex",
            NodeType = DatabaseNodeType.Index,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanDropObject, Is.True);
    }

    [Test]
    public void CannotDropObjectWithDatabaseNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "TestDatabase",
            NodeType = DatabaseNodeType.Database,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanDropObject, Is.False);
    }

    [Test]
    public void CannotDropObjectWithFolderNodeTest()
    {
        m_explorerVm.SelectedNode = new DatabaseNode
        {
            Name = "Tables",
            NodeType = DatabaseNodeType.TablesFolder,
            ConnectionId = m_studio.Database.Id
        };

        Assert.That(m_explorerVm.CanDropObject, Is.False);
    }

    [Test]
    public void CannotDropObjectWithNullNodeTest()
    {
        m_explorerVm.SelectedNode = null;

        Assert.That(m_explorerVm.CanDropObject, Is.False);
    }

    #endregion

    #region Node Selection Tests

    [Test]
    public void SelectedNodeCanBeSetTest()
    {
        var node = new DatabaseNode
        {
            Name = "TestNode",
            NodeType = DatabaseNodeType.Table,
            ConnectionId = m_studio.Database.Id
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

    /// <summary>
    /// CONTROL for every case above. They all attach the node to an open connection; without this one,
    /// "the command is offered" would pass for a tree that offers commands on nodes belonging to a
    /// connection that no longer exists - which is exactly what a node left over from a closed
    /// database is.
    /// </summary>
    [Test]
    public void ANodeWhoseConnectionIsGoneOffersNothingTest()
    {
        var orphan = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table,
            ConnectionId = Guid.NewGuid()
        };

        m_explorerVm.SelectedNode = orphan;

        Assert.Multiple(() =>
        {
            Assert.That(m_explorerVm.SessionFor(orphan), Is.Null, "the connection cannot be resolved");
            Assert.That(m_explorerVm.CanBrowseData, Is.False);
            Assert.That(m_explorerVm.CanEditData, Is.False);
            Assert.That(m_explorerVm.CanViewStructure, Is.False);
            Assert.That(m_explorerVm.CanViewDefinition, Is.False);
            Assert.That(m_explorerVm.CanDropObject, Is.False);
        });
    }

    #endregion
}
