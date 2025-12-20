using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Tables management part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Tables

    /// <summary>
    /// Gets a table definition by name.
    /// </summary>
    public DefinitionTable? GetTable(string name)
    {
        m_tables.TryGetValue(name, out var table);
        return table;
    }

    /// <summary>
    /// Creates a new table.
    /// </summary>
    public void CreateTable(DefinitionTable table)
    {
        if (m_tables.ContainsKey(table.Name))
            throw new InvalidOperationException($"Table '{table.Name}' already exists");

        m_tables[table.Name] = table;
        SaveSchema();
    }

    /// <summary>
    /// Drops a table.
    /// </summary>
    public bool DropTable(string name)
    {
        if (!m_tables.Remove(name))
            return false;

        // Also remove associated indexes
        var tableIndexes = m_indexes.Values.Where(i => i.TableName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var index in tableIndexes)
        {
            m_indexes.Remove(index.Name);
        }

        SaveSchema();
        return true;
    }

    /// <summary>
    /// Renames a table.
    /// </summary>
    public void RenameTable(string oldName, string newName)
    {
        if (!m_tables.TryGetValue(oldName, out var table))
            throw new InvalidOperationException($"Table '{oldName}' not found");
        
        if (m_tables.ContainsKey(newName))
            throw new InvalidOperationException($"Table '{newName}' already exists");

        m_tables.Remove(oldName);
        m_tables[newName] = new DefinitionTable
        {
            Name = newName,
            Columns = table.Columns,
            PrimaryKey = table.PrimaryKey,
            RowIdColumn = table.RowIdColumn,
            AutoIncrementRowId = table.AutoIncrementRowId,
            CheckExpressions = table.CheckExpressions,
            ForeignKeys = table.ForeignKeys,
            UniqueConstraints = table.UniqueConstraints
        };
        
        // Update index references
        var tableIndexes = m_indexes.Values.Where(i => i.TableName.Equals(oldName, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var index in tableIndexes)
        {
            m_indexes[index.Name] = new DefinitionIndex
            {
                Name = index.Name,
                TableName = newName,
                Columns = index.Columns,
                IsUnique = index.IsUnique,
                IsPrimaryKey = index.IsPrimaryKey
            };
        }
        
        SaveSchema();
    }

    #endregion
}
