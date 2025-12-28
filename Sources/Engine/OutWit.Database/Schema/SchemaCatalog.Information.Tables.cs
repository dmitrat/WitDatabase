using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.TABLES implementation.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] TABLES_COLUMNS = ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "TABLE_TYPE", "TABLE_COMMENT"];

    private static readonly WitSqlType[] TABLES_TYPES = [WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text];

    #endregion

    #region INFORMATION_SCHEMA.TABLES

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.TABLES view data.
    /// Returns information about all tables and views in the database.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaTables()
    {
        m_lock.EnterReadLock();
        try
        {
            // Add tables
            foreach (var table in m_tables.Values)
            {
                yield return new WitSqlRow([
                    WitSqlValue.FromText("WitDB"),          // TABLE_CATALOG
                    WitSqlValue.FromText("public"),         // TABLE_SCHEMA
                    WitSqlValue.FromText(table.Name),       // TABLE_NAME
                    WitSqlValue.FromText("BASE TABLE"),     // TABLE_TYPE
                    WitSqlValue.Null,                       // TABLE_COMMENT
                ], TABLES_COLUMNS);
            }

            // Add views
            foreach (var view in m_views.Values)
            {
                yield return new WitSqlRow([
                    WitSqlValue.FromText("WitDB"),          // TABLE_CATALOG
                    WitSqlValue.FromText("public"),         // TABLE_SCHEMA
                    WitSqlValue.FromText(view.Name),        // TABLE_NAME
                    WitSqlValue.FromText("VIEW"),           // TABLE_TYPE
                    WitSqlValue.Null,                       // TABLE_COMMENT
                ], TABLES_COLUMNS);
            }
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the column definitions for INFORMATION_SCHEMA.TABLES.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaTablesColumns() => TABLES_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.TABLES.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaTablesColumnTypes() => TABLES_TYPES;

    #endregion
}
