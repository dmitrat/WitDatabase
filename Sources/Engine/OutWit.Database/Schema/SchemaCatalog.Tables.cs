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

            // Number the columns as they were declared. Nothing did, so every column of every table
            // created by CREATE TABLE kept the default zero and INFORMATION_SCHEMA.COLUMNS published
            // ORDINAL_POSITION = 1 for all of them - the catalogue could not say what order a table's
            // columns are in. ADD COLUMN and DROP COLUMN have always numbered them; only the creation
            // path did not.
            m_tables[table.Name] = table.With(x => x.Columns,
                table.Columns.Select((column, ordinal) => column.With(x => x.Ordinal, ordinal)).ToList());
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

            // And the key generator with it. It is persisted under a key built from the table NAME, so
            // leaving it behind gives the renamed table a counter of zero - and the next generated
            // INSERT then lands on key 1 and OVERWRITES the row that is there, silently. It also
            // leaves the old name holding a counter that a later table created under that name would
            // inherit, which is the same defect pointing the other way.
            if (m_tableRowIds.TryGetValue(oldName, out var lastRowId))
            {
                DeleteTableRowId(oldName);

                m_tableRowIds[newName] = lastRowId;
                SaveTableRowId(newName, lastRowId, transaction: null);
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
