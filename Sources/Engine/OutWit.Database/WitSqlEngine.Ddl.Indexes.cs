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
        // Store metadata in schema catalog
        m_schema.CreateIndex(index);
        
        // Create the physical secondary index in the database
        // This enables index lookups via m_database.GetIndex()
        if (m_database.SupportsIndexes)
        {
            m_database.CreateIndex(index.Name, index.IsUnique);
        }
    }

    #endregion

    #region Get Index

    /// <summary>
    /// Get an index definition by name.
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <returns>The index definition, or null if not found.</returns>
    public DefinitionIndex? GetIndex(string indexName)
    {
        return m_schema.GetIndex(indexName);
    }

    /// <summary>
    /// Get all indexes for a table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>Collection of index definitions.</returns>
    public IEnumerable<DefinitionIndex> GetTableIndexes(string tableName)
    {
        return m_schema.GetTableIndexes(tableName);
    }

    /// <summary>
    /// Explicit interface implementation for IDatabase.GetIndex.
    /// </summary>
    DefinitionIndex? Interfaces.IDatabase.GetIndex(string indexName) => GetIndex(indexName);

    /// <summary>
    /// Explicit interface implementation for IDatabase.GetTableIndexes.
    /// </summary>
    IEnumerable<DefinitionIndex> Interfaces.IDatabase.GetTableIndexes(string tableName) => GetTableIndexes(tableName);

    #endregion

    #region Drop Index

    /// <summary>
    /// Drop an index.
    /// </summary>
    /// <param name="indexName">The index name to drop.</param>
    public void DropIndex(string indexName)
    {
        // Remove metadata from schema catalog
        m_schema.DropIndex(indexName);
        
        // Drop the physical secondary index from the database
        if (m_database.SupportsIndexes)
        {
            m_database.DropIndex(indexName);
        }
    }

    #endregion
}
