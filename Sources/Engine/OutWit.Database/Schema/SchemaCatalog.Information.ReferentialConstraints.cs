using OutWit.Database.Definitions;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS implementation.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] REFERENTIAL_CONSTRAINTS_COLUMNS = [
        "CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME",
        "UNIQUE_CONSTRAINT_CATALOG", "UNIQUE_CONSTRAINT_SCHEMA", "UNIQUE_CONSTRAINT_NAME",
        "MATCH_OPTION", "UPDATE_RULE", "DELETE_RULE"
    ];
    private static readonly WitSqlType[] REFERENTIAL_CONSTRAINTS_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text
    ];

    #endregion

    #region INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS view data.
    /// Returns information about foreign key constraints.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaReferentialConstraints()
    {
        m_lock.EnterReadLock();
        try
        {
            foreach (var table in m_tables.Values)
            {
                // Table-level foreign keys
                if (table.ForeignKeys != null)
                {
                    int fkIndex = 1;
                    foreach (var fk in table.ForeignKeys)
                    {
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),                                              // CONSTRAINT_CATALOG
                            WitSqlValue.FromText("public"),                                             // CONSTRAINT_SCHEMA
                            WitSqlValue.FromText($"FK_{table.Name}_{fk.ForeignTable}_{fkIndex++}"),     // CONSTRAINT_NAME
                            WitSqlValue.FromText("WitDB"),                                              // UNIQUE_CONSTRAINT_CATALOG
                            WitSqlValue.FromText("public"),                                             // UNIQUE_CONSTRAINT_SCHEMA
                            WitSqlValue.FromText($"PK_{fk.ForeignTable}"),                              // UNIQUE_CONSTRAINT_NAME
                            WitSqlValue.FromText("NONE"),                                               // MATCH_OPTION
                            WitSqlValue.FromText(GetReferenceActionName(fk.OnUpdate)),                  // UPDATE_RULE
                            WitSqlValue.FromText(GetReferenceActionName(fk.OnDelete)),                  // DELETE_RULE
                        ], REFERENTIAL_CONSTRAINTS_COLUMNS);
                    }
                }

                // Column-level foreign key constraints
                foreach (var column in table.Columns)
                {
                    if (column.ForeignKey != null)
                    {
                        var fk = column.ForeignKey;
                        yield return new WitSqlRow([
                            WitSqlValue.FromText("WitDB"),                                              // CONSTRAINT_CATALOG
                            WitSqlValue.FromText("public"),                                             // CONSTRAINT_SCHEMA
                            WitSqlValue.FromText($"FK_{table.Name}_{fk.ForeignTable}_{column.Name}"),   // CONSTRAINT_NAME
                            WitSqlValue.FromText("WitDB"),                                              // UNIQUE_CONSTRAINT_CATALOG
                            WitSqlValue.FromText("public"),                                             // UNIQUE_CONSTRAINT_SCHEMA
                            WitSqlValue.FromText($"PK_{fk.ForeignTable}"),                              // UNIQUE_CONSTRAINT_NAME
                            WitSqlValue.FromText("NONE"),                                               // MATCH_OPTION
                            WitSqlValue.FromText(GetReferenceActionName(fk.OnUpdate)),                  // UPDATE_RULE
                            WitSqlValue.FromText(GetReferenceActionName(fk.OnDelete)),                  // DELETE_RULE
                        ], REFERENTIAL_CONSTRAINTS_COLUMNS);
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
    /// Gets the column definitions for INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaReferentialConstraintsColumns() => REFERENTIAL_CONSTRAINTS_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaReferentialConstraintsColumnTypes() => REFERENTIAL_CONSTRAINTS_TYPES;

    #endregion

    #region Helpers

    private static string GetReferenceActionName(ReferenceAction action)
    {
        return action switch
        {
            ReferenceAction.NoAction => "NO ACTION",
            ReferenceAction.Restrict => "RESTRICT",
            ReferenceAction.Cascade => "CASCADE",
            ReferenceAction.SetNull => "SET NULL",
            ReferenceAction.SetDefault => "SET DEFAULT",
            _ => "NO ACTION"
        };
    }

    #endregion
}
