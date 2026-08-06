using OutWit.Common.Abstract;
using OutWit.Common.Aspects;
using OutWit.Common.Values;
using OutWit.Common.Collections;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Represents a node in the database explorer tree.
/// </summary>
public sealed partial class DatabaseNode : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not DatabaseNode other)
            return false;

        return Name.Is(other.Name)
            && NodeType.Is(other.NodeType)
            && ConnectionId.Equals(other.ConnectionId)
            && IsExpanded.Is(other.IsExpanded)
            && Children.Is(other.Children);
    }

    public override DatabaseNode Clone()
    {
        return new DatabaseNode
        {
            Name = Name,
            NodeType = NodeType,
            ConnectionId = ConnectionId,
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
    /// The connection this node came from. Every node in a branch carries it, including the root, so
    /// that "drop this table" goes to the database the user is pointing at rather than to whichever
    /// connection is active (WS-3).
    ///
    /// An id rather than a reference: a node left over from a closed connection must not be able to
    /// keep it alive, and <see cref="Guid.Empty"/> reads correctly as "no connection".
    /// </summary>
    public Guid ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets whether the node is expanded in the tree.
    /// </summary>
    [Notify]
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
