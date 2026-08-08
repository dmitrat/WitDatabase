namespace OutWit.Database.Studio.Models;

/// <summary>
/// Type of workspace tab.
/// </summary>
public enum WorkspaceTabType
{
    /// <summary>
    /// SQL query editor tab.
    /// </summary>
    Query,

    /// <summary>
    /// Table data editor tab.
    /// </summary>
    TableEdit,

    /// <summary>
    /// Object structure viewer tab.
    /// </summary>
    Structure,

    /// <summary>
    /// The storage layer of one connection - the «База» tab (WS-54).
    /// </summary>
    Database
}
