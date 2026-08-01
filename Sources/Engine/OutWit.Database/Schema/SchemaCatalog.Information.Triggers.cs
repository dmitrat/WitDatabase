using OutWit.Database.Definitions;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.TRIGGERS implementation.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] TRIGGERS_COLUMNS = [
        "TRIGGER_CATALOG", "TRIGGER_SCHEMA", "TRIGGER_NAME",
        "EVENT_MANIPULATION", "EVENT_OBJECT_CATALOG", "EVENT_OBJECT_SCHEMA", "EVENT_OBJECT_TABLE",
        "ACTION_ORDER", "ACTION_CONDITION", "ACTION_STATEMENT",
        "ACTION_ORIENTATION", "ACTION_TIMING", "ACTION_REFERENCE_OLD_TABLE", "ACTION_REFERENCE_NEW_TABLE"
    ];

    private static readonly WitSqlType[] TRIGGERS_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Integer, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text
    ];

    #endregion

    #region INFORMATION_SCHEMA.TRIGGERS

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.TRIGGERS view data.
    /// Returns information about all triggers.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaTriggers()
    {
        m_lock.EnterReadLock();
        try
        {
            var results = new List<WitSqlRow>();
            var order = 1;
            
            foreach (var trigger in m_triggers.Values)
            {
                results.Add(new WitSqlRow([
                    WitSqlValue.FromText("WitDB"),                                      // TRIGGER_CATALOG
                    WitSqlValue.FromText("public"),                                     // TRIGGER_SCHEMA
                    WitSqlValue.FromText(trigger.Name),                                 // TRIGGER_NAME
                    WitSqlValue.FromText(GetEventManipulation(trigger.Event)),          // EVENT_MANIPULATION
                    WitSqlValue.FromText("WitDB"),                                      // EVENT_OBJECT_CATALOG
                    WitSqlValue.FromText("public"),                                     // EVENT_OBJECT_SCHEMA
                    WitSqlValue.FromText(trigger.TableName),                            // EVENT_OBJECT_TABLE
                    WitSqlValue.FromInt(order++),                                       // ACTION_ORDER
                    trigger.DisplayWhen() is { } when
                        ? WitSqlValue.FromText(when)
                        : WitSqlValue.Null,                                             // ACTION_CONDITION
                    trigger.DisplayBody() is { } body
                        ? WitSqlValue.FromText(body)
                        : WitSqlValue.Null,                                            // ACTION_STATEMENT
                    WitSqlValue.FromText(trigger.ForEachRow ? "ROW" : "STATEMENT"),     // ACTION_ORIENTATION
                    WitSqlValue.FromText(GetActionTiming(trigger.Time)),                // ACTION_TIMING
                    WitSqlValue.Null,                                                   // ACTION_REFERENCE_OLD_TABLE
                    WitSqlValue.Null,                                                   // ACTION_REFERENCE_NEW_TABLE
                ], TRIGGERS_COLUMNS));
            }
            
            return results;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the column definitions for INFORMATION_SCHEMA.TRIGGERS.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaTriggersColumns() => TRIGGERS_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.TRIGGERS.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaTriggersColumnTypes() => TRIGGERS_TYPES;

    #endregion

    #region Helpers

    private static string GetEventManipulation(TriggerEvent evt)
    {
        return evt switch
        {
            TriggerEvent.Insert => "INSERT",
            TriggerEvent.Update => "UPDATE",
            TriggerEvent.Delete => "DELETE",
            _ => evt.ToString().ToUpperInvariant()
        };
    }

    private static string GetActionTiming(TriggerTime time)
    {
        return time switch
        {
            TriggerTime.Before => "BEFORE",
            TriggerTime.After => "AFTER",
            TriggerTime.InsteadOf => "INSTEAD OF",
            _ => time.ToString().ToUpperInvariant()
        };
    }

    #endregion
}
