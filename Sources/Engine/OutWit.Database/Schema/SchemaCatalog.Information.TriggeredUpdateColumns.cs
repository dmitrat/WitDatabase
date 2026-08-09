using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.TRIGGERED_UPDATE_COLUMNS implementation.
/// </summary>
/// <remarks>
/// <para>
/// The standard's place for the <c>UPDATE OF</c> column list (ISO/IEC 9075-11), and the shape
/// PostgreSQL publishes: one row per watched column rather than a list inside
/// <c>INFORMATION_SCHEMA.TRIGGERS</c>, which keeps that view the shape every other database has.
/// </para>
/// <para>
/// It exists because the firing path honours the clause. Until 2026-08-09 the engine accepted
/// <c>UPDATE OF</c> and ignored it, and the catalogue had nowhere to publish it - so a
/// <c>CREATE TRIGGER</c> rebuilt from the catalogue (a dump, a table rebuild) came back watching
/// every column, and that cost nothing only because the engine ignored the clause on both sides.
/// One half of that pair cannot be fixed without the other.
/// </para>
/// </remarks>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] TRIGGERED_UPDATE_COLUMNS_COLUMNS = [
        "TRIGGER_CATALOG", "TRIGGER_SCHEMA", "TRIGGER_NAME",
        "EVENT_OBJECT_CATALOG", "EVENT_OBJECT_SCHEMA", "EVENT_OBJECT_TABLE", "EVENT_OBJECT_COLUMN"
    ];

    private static readonly WitSqlType[] TRIGGERED_UPDATE_COLUMNS_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text
    ];

    #endregion

    #region INFORMATION_SCHEMA.TRIGGERED_UPDATE_COLUMNS

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.TRIGGERED_UPDATE_COLUMNS view data: one row per column named in a
    /// trigger's <c>UPDATE OF</c> clause. A trigger without the clause watches every column and has no
    /// rows here, which is how the standard says "all of them".
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaTriggeredUpdateColumns()
    {
        m_lock.EnterReadLock();
        try
        {
            var results = new List<WitSqlRow>();

            foreach (var trigger in m_triggers.Values)
            {
                if (trigger.UpdateColumns is not { Count: > 0 } columns)
                    continue;

                foreach (var column in columns)
                {
                    results.Add(new WitSqlRow([
                        WitSqlValue.FromText("WitDB"),                  // TRIGGER_CATALOG
                        WitSqlValue.FromText("public"),                 // TRIGGER_SCHEMA
                        WitSqlValue.FromText(trigger.Name),             // TRIGGER_NAME
                        WitSqlValue.FromText("WitDB"),                  // EVENT_OBJECT_CATALOG
                        WitSqlValue.FromText("public"),                 // EVENT_OBJECT_SCHEMA
                        WitSqlValue.FromText(trigger.TableName),        // EVENT_OBJECT_TABLE
                        WitSqlValue.FromText(column),                   // EVENT_OBJECT_COLUMN
                    ], TRIGGERED_UPDATE_COLUMNS_COLUMNS));
                }
            }

            return results;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the column definitions for INFORMATION_SCHEMA.TRIGGERED_UPDATE_COLUMNS.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaTriggeredUpdateColumnsColumns() =>
        TRIGGERED_UPDATE_COLUMNS_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.TRIGGERED_UPDATE_COLUMNS.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaTriggeredUpdateColumnsColumnTypes() =>
        TRIGGERED_UPDATE_COLUMNS_TYPES;

    #endregion
}
