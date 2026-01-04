using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Tests.Models;

/// <summary>
/// Tests for <see cref="DatabaseNode"/> model.
/// </summary>
[TestFixture]
public class DatabaseNodeTests
{
    #region Clone Tests

    [Test]
    public void CloneCreatesExactCopyTest()
    {
        var original = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table,
            IsExpanded = true,
            Children = new List<DatabaseNode>
            {
                new DatabaseNode { Name = "Column1", NodeType = DatabaseNodeType.Table },
                new DatabaseNode { Name = "Column2", NodeType = DatabaseNodeType.Table }
            }
        };

        var clone = original.Clone();

        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone.Name, Is.EqualTo(original.Name));
        Assert.That(clone.NodeType, Is.EqualTo(original.NodeType));
        Assert.That(clone.IsExpanded, Is.EqualTo(original.IsExpanded));
        Assert.That(clone.Children.Count, Is.EqualTo(original.Children.Count));
        Assert.That(clone.Children[0], Is.Not.SameAs(original.Children[0]));
    }

    #endregion

    #region Is Tests

    [Test]
    public void IsReturnsTrueForEqualNodesTest()
    {
        var node1 = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table,
            IsExpanded = true
        };

        var node2 = new DatabaseNode
        {
            Name = "TestTable",
            NodeType = DatabaseNodeType.Table,
            IsExpanded = true
        };

        Assert.That(node1.Is(node2), Is.True);
    }

    [Test]
    public void IsReturnsFalseForDifferentNodesTest()
    {
        var node1 = new DatabaseNode
        {
            Name = "Table1",
            NodeType = DatabaseNodeType.Table
        };

        var node2 = new DatabaseNode
        {
            Name = "Table2",
            NodeType = DatabaseNodeType.Table
        };

        Assert.That(node1.Is(node2), Is.False);
    }

    [Test]
    public void IsReturnsFalseForDifferentNodeTypesTest()
    {
        var node1 = new DatabaseNode
        {
            Name = "Test",
            NodeType = DatabaseNodeType.Table
        };

        var node2 = new DatabaseNode
        {
            Name = "Test",
            NodeType = DatabaseNodeType.View
        };

        Assert.That(node1.Is(node2), Is.False);
    }

    #endregion

    #region NodeType Tests

    [Test]
    public void NodeTypeEnumContainsAllExpectedValuesTest()
    {
        var types = Enum.GetValues<DatabaseNodeType>();

        Assert.That(types, Contains.Item(DatabaseNodeType.Database));
        Assert.That(types, Contains.Item(DatabaseNodeType.TablesFolder));
        Assert.That(types, Contains.Item(DatabaseNodeType.Table));
        Assert.That(types, Contains.Item(DatabaseNodeType.ViewsFolder));
        Assert.That(types, Contains.Item(DatabaseNodeType.View));
        Assert.That(types, Contains.Item(DatabaseNodeType.IndexesFolder));
        Assert.That(types, Contains.Item(DatabaseNodeType.Index));
        Assert.That(types, Contains.Item(DatabaseNodeType.TriggersFolder));
        Assert.That(types, Contains.Item(DatabaseNodeType.Trigger));
        Assert.That(types, Contains.Item(DatabaseNodeType.SequencesFolder));
        Assert.That(types, Contains.Item(DatabaseNodeType.Sequence));
    }

    #endregion

    #region Children Tests

    [Test]
    public void ChildrenListIsEmptyByDefaultTest()
    {
        var node = new DatabaseNode
        {
            Name = "Test",
            NodeType = DatabaseNodeType.Table
        };

        Assert.That(node.Children, Is.Not.Null);
        Assert.That(node.Children, Is.Empty);
    }

    [Test]
    public void CanAddChildrenTest()
    {
        var parent = new DatabaseNode
        {
            Name = "Tables",
            NodeType = DatabaseNodeType.TablesFolder
        };

        parent.Children.Add(new DatabaseNode 
        { 
            Name = "Users", 
            NodeType = DatabaseNodeType.Table 
        });

        parent.Children.Add(new DatabaseNode 
        { 
            Name = "Orders", 
            NodeType = DatabaseNodeType.Table 
        });

        Assert.That(parent.Children.Count, Is.EqualTo(2));
        Assert.That(parent.Children[0].Name, Is.EqualTo("Users"));
        Assert.That(parent.Children[1].Name, Is.EqualTo("Orders"));
    }

    #endregion
}
