using OutWit.Common.Utils;
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
        m_lock.EnterReadLock();
        try
        {
            m_tables.TryGetValue(name, out var table);
            return table;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Creates a new table.
    /// </summary>
    public void CreateTable(DefinitionTable table)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (m_tables.ContainsKey(table.Name))
                throw new InvalidOperationException($"Table '{table.Name}' already exists");

            m_tables[table.Name] = table;
            m_tableRowCounts[table.Name] = 0; // Initialize row count to 0
            SaveTableRowCount(table.Name, 0, transaction: null); // Persist initial row count
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Drops a table.
    /// </summary>
    public bool DropTable(string name)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.Remove(name))
                return false;

            // Also remove associated indexes
            var tableIndexes = m_indexes.Values.Where(i => i.TableName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var index in tableIndexes)
            {
                m_indexes.Remove(index.Name);
            }

            // Remove associated triggers
            var tableTriggers = m_triggers.Values.Where(t => t.TableName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var trigger in tableTriggers)
            {
                m_triggers.Remove(trigger.Name);
            }

            // Remove row ID counter
            DeleteTableRowId(name);
            
            // Remove row count
            DeleteTableRowCount(name);

            SaveSchema();
            SaveTriggers();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Renames a table.
    /// </summary>
    public void RenameTable(string oldName, string newName)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(oldName, out var table))
                throw new InvalidOperationException($"Table '{oldName}' not found");

            if (m_tables.ContainsKey(newName))
                throw new InvalidOperationException($"Table '{newName}' already exists");

            m_tables.Remove(oldName);
            m_tables[newName] = table.With(x => x.Name, newName);

            // Update index references
            var tableIndexes = m_indexes.Values
                .Where(i => i.TableName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            foreach (var index in tableIndexes)
            {
                m_indexes[index.Name] = index.With(x => x.TableName, newName);
            }

            // Move row count to new name
            if (m_tableRowCounts.TryGetValue(oldName, out var count))
            {
                m_tableRowCounts.Remove(oldName);
                m_tableRowCounts[newName] = count;
            }

            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    #endregion
}
