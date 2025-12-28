using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.KEY_COLUMN_USAGE implementation.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] KEY_COLUMN_USAGE_COLUMNS = [
        "CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME",
        "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME",
        "ORDINAL_POSITION", "POSITION_IN_UNIQUE_CONSTRAINT",
        "REFERENCED_TABLE_SCHEMA", "REFERENCED_TABLE_NAME", "REFERENCED_COLUMN_NAME"
    ];
    private static readonly WitSqlType[] KEY_COLUMN_USAGE_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Integer, WitSqlType.Integer,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text
    ];

    #endregion

    #region INFORMATION_SCHEMA.KEY_COLUMN_USAGE

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.KEY_COLUMN_USAGE view data.
    /// Returns information about columns that are constrained as keys.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaKeyColumnUsage()
    {
        m_lock.EnterReadLock();
        try
        {
            foreach (var table in m_tables.Values)
            {
                // Primary key columns
                if (table.PrimaryKey != null)
                {
                    int position = 1;
                    foreach (var columnName in table.PrimaryKey)
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),                               // CONSTRAINT_CATALOG
                            WitSqlValue.FromText("public"),                              // CONSTRAINT_SCHEMA
                            WitSqlValue.FromText($"PK_{table.Name}"),                    // CONSTRAINT_NAME
                            WitSqlValue.FromText("WitDB"),                               // TABLE_CATALOG
                            WitSqlValue.FromText("public"),                              // TABLE_SCHEMA
                            WitSqlValue.FromText(table.Name),                            // TABLE_NAME
                            WitSqlValue.FromText(columnName),                            // COLUMN_NAME
                            WitSqlValue.FromInt(position++),                             // ORDINAL_POSITION
                            WitSqlValue.Null,                                            // POSITION_IN_UNIQUE_CONSTRAINT
                            WitSqlValue.Null,                                            // REFERENCED_TABLE_SCHEMA
                            WitSqlValue.Null,                                            // REFERENCED_TABLE_NAME
                            WitSqlValue.Null,                                            // REFERENCED_COLUMN_NAME
                        ], KEY_COLUMN_USAGE_COLUMNS);
                    }
                }

                // Table-level unique constraints
                if (table.UniqueConstraints != null)
                {
                    int constraintIndex = 1;
                    foreach (var uniqueColumns in table.UniqueConstraints)
                    {
                        int position = 1;
                        foreach (var columnName in uniqueColumns)
                        {
                            yield return new WitSqlRow([
                                WitSqlValue.FromText("WitDB"),
                                WitSqlValue.FromText("public"),
                                WitSqlValue.FromText($"UQ_{table.Name}_{constraintIndex}"),
                                WitSqlValue.FromText("WitDB"),
                                WitSqlValue.FromText("public"),
                                WitSqlValue.FromText(table.Name),
                                WitSqlValue.FromText(columnName),
                                WitSqlValue.FromInt(position++),
                                WitSqlValue.Null,
                                WitSqlValue.Null,
                                WitSqlValue.Null,
                                WitSqlValue.Null,
                            ], KEY_COLUMN_USAGE_COLUMNS);
                        }
                        constraintIndex++;
                    }
                }

                // Column-level unique constraints (from IsUnique property)
                foreach (var column in table.Columns)
                {
                    if (column.IsUnique && !column.IsPrimaryKey)
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText($"UQ_{table.Name}_{column.Name}"),
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(table.Name),
                            WitSqlValue.FromText(column.Name),
                            WitSqlValue.FromInt(1),
                            WitSqlValue.Null,
                            WitSqlValue.Null,
                            WitSqlValue.Null,
                            WitSqlValue.Null,
                        ], KEY_COLUMN_USAGE_COLUMNS);
                    }
                }

                // Table-level foreign key columns
                if (table.ForeignKeys != null)
                {
                    int fkIndex = 1;
                    foreach (var fk in table.ForeignKeys)
                    {
                        int position = 1;
                        for (int i = 0; i < fk.Columns.Count; i++)
                        {
                            var localColumn = fk.Columns[i];
                            var foreignColumn = fk.ForeignColumns != null && i < fk.ForeignColumns.Count
                                ? fk.ForeignColumns[i]
                                : localColumn;

                            yield return new WitSqlRow([
                                WitSqlValue.FromText("WitDB"),
                                WitSqlValue.FromText("public"),
                                WitSqlValue.FromText($"FK_{table.Name}_{fk.ForeignTable}_{fkIndex}"),
                                WitSqlValue.FromText("WitDB"),
                                WitSqlValue.FromText("public"),
                                WitSqlValue.FromText(table.Name),
                                WitSqlValue.FromText(localColumn),
                                WitSqlValue.FromInt(position++),
                                WitSqlValue.FromInt(position - 1),
                                WitSqlValue.FromText("public"),
                                WitSqlValue.FromText(fk.ForeignTable),
                                WitSqlValue.FromText(foreignColumn),
                            ], KEY_COLUMN_USAGE_COLUMNS);
                        }
                        fkIndex++;
                    }
                }

                // Column-level foreign key constraints (from ForeignKey property)
                foreach (var column in table.Columns)
                {
                    if (column.ForeignKey != null)
                    {
                        var fk = column.ForeignKey;
                        var foreignColumn = fk.ForeignColumns != null && fk.ForeignColumns.Count > 0
                            ? fk.ForeignColumns[0]
                            : column.Name;

                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText($"FK_{table.Name}_{fk.ForeignTable}_{column.Name}"),
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(table.Name),
                            WitSqlValue.FromText(column.Name),
                            WitSqlValue.FromInt(1),
                            WitSqlValue.FromInt(1),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(fk.ForeignTable),
                            WitSqlValue.FromText(foreignColumn),
                        ], KEY_COLUMN_USAGE_COLUMNS);
                    }
                }
            }
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the column definitions for INFORMATION_SCHEMA.KEY_COLUMN_USAGE.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaKeyColumnUsageColumns() => KEY_COLUMN_USAGE_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.KEY_COLUMN_USAGE.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaKeyColumnUsageColumnTypes() => KEY_COLUMN_USAGE_TYPES;

    #endregion
}
