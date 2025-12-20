using System.Text;
using OutWit.Common.Json;
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
    public DefinitionTrigger? GetTrigger(string name) =>
        m_triggers.GetValueOrDefault(name);

    /// <summary>
    /// Gets all triggers for a specific table and event.
    /// </summary>
    public IEnumerable<DefinitionTrigger> GetTriggersForTable(string tableName, TriggerEvent? evt = null, TriggerTime? time = null)
    {
        return m_triggers.Values
            .Where(trigger => trigger.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            .Where(trigger => evt == null || trigger.Event == evt)
            .Where(trigger => time == null || trigger.Time == time)
            .OrderBy(trigger => trigger.Name);
    }

    /// <summary>
    /// Creates a new trigger.
    /// </summary>
    public void CreateTrigger(DefinitionTrigger trigger)
    {
        if (m_triggers.ContainsKey(trigger.Name))
            throw new InvalidOperationException($"Trigger '{trigger.Name}' already exists");

        if (!m_tables.ContainsKey(trigger.TableName) && !m_views.ContainsKey(trigger.TableName))
            throw new InvalidOperationException($"Table or view '{trigger.TableName}' not found");

        m_triggers[trigger.Name] = trigger;
        SaveTriggers();
    }

    /// <summary>
    /// Drops a trigger.
    /// </summary>
    public bool DropTrigger(string name)
    {
        if (!m_triggers.Remove(name))
            return false;

        SaveTriggers();
        return true;
    }

    private void SaveTriggers()
    {
        List<DefinitionTrigger> triggers = m_triggers.Values.ToList();
        m_store.Put(Encoding.UTF8.GetBytes(TRIGGERS_KEY).AsSpan(), triggers.ToJsonBytes());
    }

    private void LoadTriggers()
    {
        var triggersData = m_store.Get(Encoding.UTF8.GetBytes(TRIGGERS_KEY).AsSpan());
        if (triggersData == null || triggersData.Length == 0) 
            return;

        var triggers = triggersData.FromJsonBytes<List<DefinitionTrigger>>();
        if (triggers == null) 
            return;

        foreach (var trigger in triggers)
            m_triggers[trigger.Name] = trigger;
    }

    #endregion
}
