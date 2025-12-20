using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Indexes management part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Indexes

    /// <summary>
    /// Gets an index definition by name.
    /// </summary>
    public DefinitionIndex? GetIndex(string name)
    {
        m_indexes.TryGetValue(name, out var index);
        return index;
    }

    /// <summary>
    /// Gets all indexes for a table.
    /// </summary>
    public IEnumerable<DefinitionIndex> GetTableIndexes(string tableName)
    {
        return m_indexes.Values.Where(i => i.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a new index.
    /// </summary>
    public void CreateIndex(DefinitionIndex index)
    {
        if (m_indexes.ContainsKey(index.Name))
            throw new InvalidOperationException($"Index '{index.Name}' already exists");

        if (!m_tables.ContainsKey(index.TableName))
            throw new InvalidOperationException($"Table '{index.TableName}' does not exist");

        m_indexes[index.Name] = index;
        SaveSchema();
    }

    /// <summary>
    /// Drops an index.
    /// </summary>
    public bool DropIndex(string name)
    {
        if (!m_indexes.Remove(name))
            return false;

        SaveSchema();
        return true;
    }

    #endregion
}
