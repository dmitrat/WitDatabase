using OutWit.Database.Definitions;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.TABLE_CONSTRAINTS implementation.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] TABLE_CONSTRAINTS_COLUMNS = [
        "CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME",
        "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "CONSTRAINT_TYPE",
        "IS_DEFERRABLE", "INITIALLY_DEFERRED"
    ];
    private static readonly WitSqlType[] TABLE_CONSTRAINTS_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text
    ];

    #endregion

    #region INFORMATION_SCHEMA.TABLE_CONSTRAINTS

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.TABLE_CONSTRAINTS view data.
    /// Returns information about table constraints.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaTableConstraints()
    {
        m_lock.EnterReadLock();
        try
        {
            foreach (var table in m_tables.Values)
            {
                // Primary key
                if (table.PrimaryKey != null)
                {
                    yield return new WitSqlRow([
                        WitSqlValue.FromText("WitDB"),                    // CONSTRAINT_CATALOG
                        WitSqlValue.FromText("public"),                   // CONSTRAINT_SCHEMA
                        WitSqlValue.FromText($"PK_{table.Name}"),         // CONSTRAINT_NAME
                        WitSqlValue.FromText("WitDB"),                    // TABLE_CATALOG
                        WitSqlValue.FromText("public"),                   // TABLE_SCHEMA
                        WitSqlValue.FromText(table.Name),                 // TABLE_NAME
                        WitSqlValue.FromText("PRIMARY KEY"),              // CONSTRAINT_TYPE
                        WitSqlValue.FromText("NO"),                       // IS_DEFERRABLE
                        WitSqlValue.FromText("NO"),                       // INITIALLY_DEFERRED
                    ], TABLE_CONSTRAINTS_COLUMNS);
                }

                // Table-level unique constraints
                if (table.UniqueConstraints != null)
                {
                    int constraintIndex = 1;
                    foreach (var _ in table.UniqueConstraints)
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText($"UQ_{table.Name}_{constraintIndex++}"),
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(table.Name),
                            WitSqlValue.FromText("UNIQUE"),
                            WitSqlValue.FromText("NO"),
                            WitSqlValue.FromText("NO"),
                        ], TABLE_CONSTRAINTS_COLUMNS);
                    }
                }

                // Column-level unique constraints
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
                            WitSqlValue.FromText("UNIQUE"),
                            WitSqlValue.FromText("NO"),
                            WitSqlValue.FromText("NO"),
                        ], TABLE_CONSTRAINTS_COLUMNS);
                    }
                }

                // Table-level foreign keys
                if (table.ForeignKeys != null)
                {
                    int fkIndex = 1;
                    foreach (var fk in table.ForeignKeys)
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText($"FK_{table.Name}_{fk.ForeignTable}_{fkIndex++}"),
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(table.Name),
                            WitSqlValue.FromText("FOREIGN KEY"),
                            WitSqlValue.FromText("NO"),
                            WitSqlValue.FromText("NO"),
                        ], TABLE_CONSTRAINTS_COLUMNS);
                    }
                }

                // Column-level foreign key constraints
                foreach (var column in table.Columns)
                {
                    if (column.ForeignKey != null)
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText($"FK_{table.Name}_{column.ForeignKey.ForeignTable}_{column.Name}"),
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(table.Name),
                            WitSqlValue.FromText("FOREIGN KEY"),
                            WitSqlValue.FromText("NO"),
                            WitSqlValue.FromText("NO"),
                        ], TABLE_CONSTRAINTS_COLUMNS);
                    }
                }

                // Table-level check constraints
                if (table.CheckExpressions != null)
                {
                    int checkIndex = 1;
                    foreach (var _ in table.CheckExpressions)
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText($"CK_{table.Name}_{checkIndex++}"),
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(table.Name),
                            WitSqlValue.FromText("CHECK"),
                            WitSqlValue.FromText("NO"),
                            WitSqlValue.FromText("NO"),
                        ], TABLE_CONSTRAINTS_COLUMNS);
                    }
                }

                // Column-level check constraints
                foreach (var column in table.Columns)
                {
                    if (!string.IsNullOrEmpty(column.CheckExpression))
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText($"CK_{table.Name}_{column.Name}"),
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(table.Name),
                            WitSqlValue.FromText("CHECK"),
                            WitSqlValue.FromText("NO"),
                            WitSqlValue.FromText("NO"),
                        ], TABLE_CONSTRAINTS_COLUMNS);
                    }
                }

                // Named constraints
                if (table.NamedConstraints != null)
                {
                    foreach (var constraint in table.NamedConstraints)
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(constraint.Name),
                            WitSqlValue.FromText("WitDB"),
                            WitSqlValue.FromText("public"),
                            WitSqlValue.FromText(table.Name),
                            WitSqlValue.FromText(GetConstraintTypeName(constraint.Type)),
                            WitSqlValue.FromText("NO"),
                            WitSqlValue.FromText("NO"),
                        ], TABLE_CONSTRAINTS_COLUMNS);
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
    /// Gets the column definitions for INFORMATION_SCHEMA.TABLE_CONSTRAINTS.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaTableConstraintsColumns() => TABLE_CONSTRAINTS_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.TABLE_CONSTRAINTS.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaTableConstraintsColumnTypes() => TABLE_CONSTRAINTS_TYPES;

    #endregion

    #region Helpers

    private static string GetConstraintTypeName(ConstraintType type)
    {
        return type switch
        {
            ConstraintType.Check => "CHECK",
            ConstraintType.Unique => "UNIQUE",
            ConstraintType.ForeignKey => "FOREIGN KEY",
            ConstraintType.PrimaryKey => "PRIMARY KEY",
            _ => "UNKNOWN"
        };
    }

    #endregion
}
