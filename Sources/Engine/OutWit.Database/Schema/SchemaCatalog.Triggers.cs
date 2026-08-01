using System.Text;
using OutWit.Common.MemoryPack;
using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Triggers management part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Triggers

    /// <summary>
    /// Gets a trigger by name.
    /// </summary>
    public DefinitionTrigger? GetTrigger(string name)
    {
        m_lock.EnterReadLock();
        try
        {
            return m_triggers.GetValueOrDefault(name);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets all triggers for a specific table and event.
    /// </summary>
    public IEnumerable<DefinitionTrigger> GetTriggersForTable(string tableName, TriggerEvent? evt = null, TriggerTime? time = null)
    {
        m_lock.EnterReadLock();
        try
        {
            return m_triggers.Values
                .Where(trigger => trigger.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                .Where(trigger => evt == null || trigger.Event == evt)
                .Where(trigger => time == null || trigger.Time == time)
                .OrderBy(trigger => trigger.Name)
                .ToList();
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Creates a new trigger.
    /// </summary>
    public void CreateTrigger(DefinitionTrigger trigger)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (m_triggers.ContainsKey(trigger.Name))
                throw new InvalidOperationException($"Trigger '{trigger.Name}' already exists");

            if (!m_tables.ContainsKey(trigger.TableName) && !m_views.ContainsKey(trigger.TableName))
                throw new InvalidOperationException($"Table or view '{trigger.TableName}' not found");

            m_triggers[trigger.Name] = trigger;
            SaveTriggers();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Drops a trigger.
    /// </summary>
    public bool DropTrigger(string name)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_triggers.Remove(name))
                return false;

            SaveTriggers();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    private void SaveTriggers()
    {
        List<DefinitionTrigger> triggers = m_triggers.Values.ToList();
        PutSchemaRecord(TRIGGERS_KEY_BYTES.AsSpan(), triggers.ToMemoryPackBytes());
    }

    private void LoadTriggers()
    {
        var triggersData = GetSchemaRecord(TRIGGERS_KEY_BYTES.AsSpan());
        if (triggersData == null || triggersData.Length == 0)
            return;

        var triggers = ReadSchemaRecord<List<DefinitionTrigger>>(triggersData, "triggers");

        foreach (var trigger in triggers)
            m_triggers[trigger.Name] = trigger;
    }

    #endregion
}
