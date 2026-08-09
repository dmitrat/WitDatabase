using OutWit.Database.Context;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Schema;
using OutWit.Database.Types;
using OutWit.Database.Utils;
using OutWit.Database.Values;

namespace OutWit.Database.Engine;

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
            
            // Build index from existing data in the table
            BuildIndexFromExistingData(index);
        }
        
        // Invalidate query plan cache (index may change optimal plan)
        InvalidatePlanCacheForTable(index.TableName);
    }

    /// <summary>
    /// Builds an index from existing data in the table.
    /// Supports partial indexes (WHERE clause), expression indexes, and covering indexes.
    /// </summary>
    /// <param name="indexDef">The index definition.</param>
    /// <remarks>
    /// <b>A build that fails must leave nothing behind.</b> The catalogue entry is written - and
    /// flushed - before the index holds anything, so an index whose build did not finish is one the
    /// planner will route queries through and the file cannot answer from: measured 2026-08-09, a
    /// build that exhausted the page cache left <c>WHERE V = 7</c> answering zero rows out of two,
    /// with the database opening and the query succeeding. Only the unique-violation case used to be
    /// cleaned up, and even that ran the physical drop first, so a drop that threw for the same
    /// reason the build did left the catalogue entry in place.
    /// </remarks>
    private void BuildIndexFromExistingData(DefinitionIndex indexDef)
    {
        var table = m_schema.GetTable(indexDef.TableName);
        if (table == null)
            return;

        var secondaryIndex = m_database.GetIndex(indexDef.Name);
        if (secondaryIndex == null)
            return;

        // Skip building if index already has data (e.g., restored from disk after restart)
        if (secondaryIndex.Count > 0)
            return;

        try
        {
            FillIndexFromExistingData(indexDef, table, secondaryIndex);
        }
        catch (UniqueIndexViolationException)
        {
            RemoveIndexAfterAFailedBuild(indexDef.Name);

            throw new InvalidOperationException(
                $"UNIQUE constraint failed: Cannot create unique index '{indexDef.Name}' " +
                $"on table '{indexDef.TableName}' - duplicate values exist");
        }
        catch
        {
            // Any other reason a build can end - and the one that was measured is not exotic: an
            // ordinary page cache runs out of unpinned slots on a table this size.
            RemoveIndexAfterAFailedBuild(indexDef.Name);
            throw;
        }
    }

    /// <summary>
    /// Takes both halves of an index whose build did not finish back out.
    /// </summary>
    /// <remarks>
    /// The catalogue is removed in a <c>finally</c> because it is the half that <b>persists</b>: the
    /// physical index lives in a file this process is holding, while the catalogue entry has already
    /// been made durable, so an entry left behind is what a query is planned through tomorrow. A
    /// physical drop that fails is not allowed to replace the build's own exception either - that is
    /// what the caller needs to be told.
    /// </remarks>
    private void RemoveIndexAfterAFailedBuild(string indexName)
    {
        try
        {
            m_database.DropIndex(indexName);
        }
        catch
        {
            // Reported by the exception the build is about to raise.
        }
        finally
        {
            m_schema.DropIndex(indexName);
        }
    }

    private void FillIndexFromExistingData(
        DefinitionIndex indexDef,
        DefinitionTable table,
        ISecondaryIndex secondaryIndex)
    {
        // Scan all rows in the table
        var tablePrefix = SchemaCatalog.GetTableDataPrefix(indexDef.TableName);
        var endPrefix = SchemaCatalog.GetTableDataEndPrefix(indexDef.TableName);

        foreach (var (key, value) in ScanStore(tablePrefix, endPrefix))
        {
            // Parse row ID from key
            var rowId = SchemaCatalog.ParseRowId(key, indexDef.TableName);
            
            // Deserialize row
            var row = table.DeserializeRow(value);
            
            // Check partial index WHERE condition
            if (!EvaluatePartialIndexCondition(indexDef, row))
                continue; // Skip rows that don't match the partial index condition

            // Build index key (supports expression indexes)
            var indexKey = BuildIndexKey(table, indexDef, row);
            if (indexKey == null)
                continue; // Skip rows with null values in indexed columns
            
            // Build primary key
            var primaryKey = BuildPrimaryKey(rowId);
            
            // Add to index. Nothing is caught here any more: every way this loop can end badly is
            // handled in one place by the caller, which is what stops a failure that is not a unique
            // violation from leaving a registered index with nothing in it.
            secondaryIndex.Add(indexKey, primaryKey);
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
    /// Gets the physical secondary index by name.
    /// Used for direct index operations like MIN/MAX optimization.
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <returns>The physical secondary index, or null if not found.</returns>
    public ISecondaryIndex? GetPhysicalIndex(string indexName)
    {
        if (!m_database.SupportsIndexes)
            return null;
        return m_database.GetIndex(indexName);
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
        // Get index definition to know the table name before dropping
        var indexDef = m_schema.GetIndex(indexName);
        var tableName = indexDef?.TableName;
        
        // Remove metadata from schema catalog
        m_schema.DropIndex(indexName);
        
        // Drop the physical secondary index from the database
        if (m_database.SupportsIndexes)
        {
            m_database.DropIndex(indexName);
        }
        
        // Invalidate query plan cache (index was used in optimal plans)
        if (tableName != null)
        {
            InvalidatePlanCacheForTable(tableName);
        }
    }

    #endregion
}
