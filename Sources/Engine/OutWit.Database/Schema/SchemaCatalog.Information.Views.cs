using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.VIEWS implementation.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] VIEWS_COLUMNS = [
        "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME",
        "VIEW_DEFINITION", "CHECK_OPTION", "IS_UPDATABLE"
    ];
    private static readonly WitSqlType[] VIEWS_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text
    ];

    #endregion

    #region INFORMATION_SCHEMA.VIEWS

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.VIEWS view data.
    /// Returns information about all views.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaViews()
    {
        m_lock.EnterReadLock();
        try
        {
            foreach (var view in m_views.Values)
            {
                yield return new WitSqlRow([
                    WitSqlValue.FromText("WitDB"),                     // TABLE_CATALOG
                    WitSqlValue.FromText("public"),                    // TABLE_SCHEMA
                    WitSqlValue.FromText(view.Name),                   // TABLE_NAME
                    WitSqlValue.FromText(view.SelectSql),              // VIEW_DEFINITION
                    WitSqlValue.FromText("NONE"),                      // CHECK_OPTION
                    WitSqlValue.FromText("NO"),                        // IS_UPDATABLE
                ], VIEWS_COLUMNS);
            }
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the column definitions for INFORMATION_SCHEMA.VIEWS.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaViewsColumns() => VIEWS_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.VIEWS.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaViewsColumnTypes() => VIEWS_TYPES;

    #endregion
}
