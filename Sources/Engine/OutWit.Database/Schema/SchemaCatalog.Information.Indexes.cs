using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.INDEXES implementation.
/// </summary>
public sealed partial class SchemaCatalog
{

    #region Constants

    private static readonly string[] INDEXES_COLUMNS = [
        "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "INDEX_NAME",
        "COLUMN_NAME", "ORDINAL_POSITION", "IS_UNIQUE", "INDEX_TYPE", "FILTER_CONDITION"
    ];
    private static readonly WitSqlType[] INDEXES_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Integer, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text
    ];

    #endregion

    #region INFORMATION_SCHEMA.INDEXES

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.INDEXES view data.
    /// Returns information about all indexes (non-standard extension).
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaIndexes()
    {
        m_lock.EnterReadLock();
        try
        {
            foreach (var index in m_indexes.Values)
            {
                int position = 1;
                foreach (var columnName in index.Columns)
                {
                    yield return new WitSqlRow([
                        WitSqlValue.FromText("WitDB"),                                     // TABLE_CATALOG
                        WitSqlValue.FromText("public"),                                    // TABLE_SCHEMA
                        WitSqlValue.FromText(index.TableName),                             // TABLE_NAME
                        WitSqlValue.FromText(index.Name),                                  // INDEX_NAME
                        WitSqlValue.FromText(columnName),                                  // COLUMN_NAME
                        WitSqlValue.FromInt(position++),                                   // ORDINAL_POSITION
                        WitSqlValue.FromText(index.IsUnique ? "YES" : "NO"),               // IS_UNIQUE
                        WitSqlValue.Null,                                                  // INDEX_TYPE (B-tree, etc.)
                        index.WhereExpression != null
                            ? WitSqlValue.FromText(index.WhereExpression)
                            : WitSqlValue.Null,                                            // FILTER_CONDITION
                    ], INDEXES_COLUMNS);
                }
            }
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the column definitions for INFORMATION_SCHEMA.INDEXES.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaIndexesColumns() => INDEXES_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.INDEXES.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaIndexesColumnTypes() => INDEXES_TYPES;

    #endregion
}
