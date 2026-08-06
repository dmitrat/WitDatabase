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

    /// <summary>
    /// True while F2 is open on this row: the name is replaced by a box holding
    /// <see cref="RenameText"/> until Enter or Escape.
    /// </summary>
    [Notify]
    public bool IsRenaming { get; set; }

    /// <summary>
    /// What is in the rename box. Kept on the node rather than in the ViewModel so the tree can bind
    /// it directly, and so a second node cannot pick up the first one's half-typed name.
    /// </summary>
    [Notify]
    public string? RenameText { get; set; }

    /// <summary>
    /// The right-hand side of the row: a column's type, a routine's return type, the number of
    /// objects in a folder. Whatever it is, it is read from the catalogue rather than guessed.
    /// </summary>
    [Notify]
    public string? Detail { get; set; }

    /// <summary>
    /// How many rows the table has, once it is known (WS-16). Null while unknown.
    /// </summary>
    [Notify]
    public long? RowCount { get; set; }

    /// <summary>
    /// Whether the count is still being waited for, arrived, or gave up. The tree never blocks on it:
    /// names are clickable immediately and numbers arrive as they come (2.2).
    /// </summary>
    [Notify]
    public RowCountState CountState { get; set; }

    /// <summary>
    /// For a column node: part of the primary key.
    /// </summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// For a column node: part of a foreign key.
    /// </summary>
    public bool IsForeignKey { get; set; }

    /// <summary>
    /// For a column node: declared NOT NULL.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// The table a column belongs to - a column node needs it to be acted on.
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// Whether the children have been loaded. A table's columns are read when it is first expanded:
    /// reading the columns of ninety tables to draw a tree nobody has opened is a query per table for
    /// nothing (2.2).
    /// </summary>
    public bool ChildrenLoaded { get; set; }

    #endregion
}

/// <summary>
/// Types of nodes in the database tree.
/// </summary>
/// <summary>
/// What is known about a table's row count.
/// </summary>
public enum RowCountState
{
    /// <summary>Nothing has been asked yet.</summary>
    Unknown,

    /// <summary>The count is running.</summary>
    Counting,

    /// <summary>The count came back.</summary>
    Counted,

    /// <summary>The count did not finish in time and was cancelled - the table is not blocked by it.</summary>
    TimedOut
}

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
    Sequence,

    /// <summary>A column of a table or a view - the tree shows them now (WS-15).</summary>
    Column,

    /// <summary>The sixth folder: functions and procedures (WS-21).</summary>
    RoutinesFolder,
    Routine
}
