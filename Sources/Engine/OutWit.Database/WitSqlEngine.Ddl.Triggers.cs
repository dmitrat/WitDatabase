using OutWit.Database.Definitions;

namespace OutWit.Database;

/// <summary>
/// DDL (Data Definition Language) operations for triggers in WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Get Trigger

    /// <summary>
    /// Get a trigger by name.
    /// </summary>
    /// <param name="triggerName">The trigger name.</param>
    /// <returns>The trigger definition or null if not found.</returns>
    public DefinitionTrigger? GetTrigger(string triggerName)
    {
        return m_schema.GetTrigger(triggerName);
    }

    /// <summary>
    /// Get all triggers for a table and optionally filter by event and timing.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="evt">Optional trigger event filter (INSERT, UPDATE, DELETE).</param>
    /// <param name="time">Optional trigger timing filter (BEFORE, AFTER, INSTEAD OF).</param>
    /// <returns>Matching trigger definitions.</returns>
    public IEnumerable<DefinitionTrigger> GetTriggersForTable(string tableName, TriggerEvent? evt = null, TriggerTime? time = null)
    {
        return m_schema.GetTriggersForTable(tableName, evt, time);
    }

    #endregion

    #region Create Trigger

    /// <summary>
    /// Create a trigger.
    /// </summary>
    /// <param name="trigger">The trigger definition.</param>
    public void CreateTrigger(DefinitionTrigger trigger)
    {
        m_schema.CreateTrigger(trigger);
    }

    #endregion

    #region Drop Trigger

    /// <summary>
    /// Drop a trigger.
    /// </summary>
    /// <param name="name">The trigger name to drop.</param>
    public void DropTrigger(string name)
    {
        m_schema.DropTrigger(name);
    }

    #endregion
}
