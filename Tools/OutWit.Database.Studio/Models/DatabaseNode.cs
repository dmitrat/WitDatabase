using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Common.Collections;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Represents a node in the database explorer tree.
/// </summary>
public sealed class DatabaseNode : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not DatabaseNode other)
            return false;

        return Name.Is(other.Name)
            && NodeType.Is(other.NodeType)
            && IsExpanded.Is(other.IsExpanded)
            && Children.Is(other.Children);
    }

    public override DatabaseNode Clone()
    {
        return new DatabaseNode
        {
            Name = Name,
            NodeType = NodeType,
            IsExpanded = IsExpanded,
            Children = Children.Select(node => node.Clone()).ToList()
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the node name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the node type.
    /// </summary>
    public DatabaseNodeType NodeType { get; set; }

    /// <summary>
    /// Gets or sets whether the node is expanded in the tree.
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// Gets or sets the child nodes.
    /// </summary>
    public List<DatabaseNode> Children { get; set; } = [];

    #endregion
}

/// <summary>
/// Types of nodes in the database tree.
/// </summary>
public enum DatabaseNodeType
{
    Database,
    TablesFolder,
    Table,
    ViewsFolder,
    View,
    IndexesFolder,
    Index,
    TriggersFolder,
    Trigger,
    SequencesFolder,
    Sequence
}
