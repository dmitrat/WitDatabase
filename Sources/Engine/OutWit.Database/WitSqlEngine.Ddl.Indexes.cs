using OutWit.Database.Definitions;

namespace OutWit.Database;

/// <summary>
/// DDL (Data Definition Language) operations for indexes in WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Create Index

    /// <summary>
    /// Create an index.
    /// </summary>
    /// <param name="index">The index definition.</param>
    public void CreateIndex(DefinitionIndex index)
    {
        m_schema.CreateIndex(index);
    }

    #endregion

    #region Drop Index

    /// <summary>
    /// Drop an index.
    /// </summary>
    /// <param name="indexName">The index name to drop.</param>
    public void DropIndex(string indexName)
    {
        m_schema.DropIndex(indexName);
    }

    #endregion
}
