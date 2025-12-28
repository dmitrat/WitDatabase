using OutWit.Database.Definitions;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.COLUMNS implementation.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] COLUMNS_COLUMNS = [
        "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME",
        "ORDINAL_POSITION", "COLUMN_DEFAULT", "IS_NULLABLE", "DATA_TYPE",
        "CHARACTER_MAXIMUM_LENGTH", "NUMERIC_PRECISION", "NUMERIC_SCALE",
        "GENERATION_EXPRESSION", "IS_GENERATED"
    ];
    private static readonly WitSqlType[] COLUMNS_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Integer, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Integer, WitSqlType.Integer, WitSqlType.Integer,
        WitSqlType.Text, WitSqlType.Text
    ];

    #endregion

    #region INFORMATION_SCHEMA.COLUMNS

    /// <summary>
    /// Gets the INFORMATION_SCHEMA.COLUMNS view data.
    /// Returns information about all columns in all tables.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaColumns()
    {
        m_lock.EnterReadLock();
        try
        {
            foreach (var table in m_tables.Values)
            {
                foreach (var column in table.Columns)
                {
                    // PK columns are implicitly NOT NULL
                    var isNullable = column.Nullable && !column.IsPrimaryKey;

                    yield return new WitSqlRow([
                        WitSqlValue.FromText("WitDB"),                                    // TABLE_CATALOG
                        WitSqlValue.FromText("public"),                                   // TABLE_SCHEMA
                        WitSqlValue.FromText(table.Name),                                 // TABLE_NAME
                        WitSqlValue.FromText(column.Name),                                // COLUMN_NAME
                        WitSqlValue.FromInt(column.Ordinal + 1),                          // ORDINAL_POSITION (1-based)
                        column.DefaultValue != null
                            ? WitSqlValue.FromText(column.DefaultValue)
                            : WitSqlValue.Null,                                           // COLUMN_DEFAULT
                        WitSqlValue.FromText(isNullable ? "YES" : "NO"),                  // IS_NULLABLE
                        WitSqlValue.FromText(GetDataTypeName(column.Type)),               // DATA_TYPE
                        column.MaxLength.HasValue
                            ? WitSqlValue.FromInt(column.MaxLength.Value)
                            : WitSqlValue.Null,                                           // CHARACTER_MAXIMUM_LENGTH
                        column.Precision.HasValue
                            ? WitSqlValue.FromInt(column.Precision.Value)
                            : WitSqlValue.Null,                                           // NUMERIC_PRECISION
                        column.Scale.HasValue
                            ? WitSqlValue.FromInt(column.Scale.Value)
                            : WitSqlValue.Null,                                           // NUMERIC_SCALE
                        column.IsComputed
                            ? WitSqlValue.FromText(column.ComputedExpression!)
                            : WitSqlValue.Null,                                           // GENERATION_EXPRESSION
                        WitSqlValue.FromText(column.IsComputed
                            ? (column.IsStored ? "STORED" : "VIRTUAL")
                            : "NEVER"),                                                   // IS_GENERATED
                    ], COLUMNS_COLUMNS);
                }
            }
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the column definitions for INFORMATION_SCHEMA.COLUMNS.
    /// </summary>
    public static IReadOnlyList<string> GetInformationSchemaColumnsColumns() => COLUMNS_COLUMNS;

    /// <summary>
    /// Gets the column types for INFORMATION_SCHEMA.COLUMNS.
    /// </summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaColumnsColumnTypes() => COLUMNS_TYPES;

    #endregion

    #region Helpers

    private static string GetDataTypeName(WitDataType type)
    {
        return type switch
        {
            WitDataType.Boolean => "BOOLEAN",
            WitDataType.Int8 => "TINYINT",
            WitDataType.UInt8 => "UTINYINT",
            WitDataType.Int16 => "SMALLINT",
            WitDataType.UInt16 => "USMALLINT",
            WitDataType.Int32 => "INTEGER",
            WitDataType.UInt32 => "UINT",
            WitDataType.Int64 => "BIGINT",
            WitDataType.UInt64 => "UBIGINT",
            WitDataType.Float16 => "FLOAT16",
            WitDataType.Float32 => "FLOAT",
            WitDataType.Float64 => "DOUBLE",
            WitDataType.Decimal => "DECIMAL",
            WitDataType.DateTime => "DATETIME",
            WitDataType.DateOnly => "DATE",
            WitDataType.TimeOnly => "TIME",
            WitDataType.DateTimeOffset => "DATETIMEOFFSET",
            WitDataType.TimeSpan => "INTERVAL",
            WitDataType.Guid => "GUID",
            WitDataType.StringFixed => "CHAR",
            WitDataType.StringVariable => "VARCHAR",
            WitDataType.BinaryFixed => "BINARY",
            WitDataType.BinaryVariable => "VARBINARY",
            WitDataType.RowVersion => "ROWVERSION",
            WitDataType.Json => "JSON",
            _ => type.ToString().ToUpperInvariant()
        };
    }

    #endregion
}
