using System.Data;
using OutWit.Database.Engine;

namespace OutWit.Database.AdoNet.Schema;

/// <summary>
/// Provides schema information for WitDatabase connections.
/// </summary>
internal sealed class SchemaProvider
{
    #region Constants

    /// <summary>
    /// Collection name for metadata collections.
    /// </summary>
    public const string METADATA_COLLECTIONS = "MetaDataCollections";

    /// <summary>
    /// Collection name for data source information.
    /// </summary>
    public const string DATA_SOURCE_INFORMATION = "DataSourceInformation";

    /// <summary>
    /// Collection name for data types.
    /// </summary>
    public const string DATA_TYPES = "DataTypes";

    /// <summary>
    /// Collection name for restrictions.
    /// </summary>
    public const string RESTRICTIONS = "Restrictions";

    /// <summary>
    /// Collection name for reserved words.
    /// </summary>
    public const string RESERVED_WORDS = "ReservedWords";

    /// <summary>
    /// Collection name for tables.
    /// </summary>
    public const string TABLES = "Tables";

    /// <summary>
    /// Collection name for columns.
    /// </summary>
    public const string COLUMNS = "Columns";

    /// <summary>
    /// Collection name for indexes.
    /// </summary>
    public const string INDEXES = "Indexes";

    /// <summary>
    /// Collection name for index columns.
    /// </summary>
    public const string INDEX_COLUMNS = "IndexColumns";

    /// <summary>
    /// Collection name for views.
    /// </summary>
    public const string VIEWS = "Views";

    /// <summary>
    /// Collection name for foreign keys.
    /// </summary>
    public const string FOREIGN_KEYS = "ForeignKeys";

    #endregion

    #region Fields

    private readonly WitSqlEngine m_engine;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new schema provider.
    /// </summary>
    /// <param name="engine">The SQL engine to query for schema information.</param>
    public SchemaProvider(WitSqlEngine engine)
    {
        m_engine = engine;
    }

    #endregion

    #region GetSchema

    /// <summary>
    /// Gets schema information for the specified collection.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="restrictions">Optional restrictions to filter the results.</param>
    /// <returns>A DataTable containing the schema information.</returns>
    public DataTable GetSchema(string? collectionName, string?[]? restrictions)
    {
        collectionName ??= METADATA_COLLECTIONS;

        return collectionName.ToUpperInvariant() switch
        {
            "METADATACOLLECTIONS" => GetMetaDataCollections(),
            "DATASOURCEINFORMATION" => GetDataSourceInformation(),
            "DATATYPES" => GetDataTypes(),
            "RESTRICTIONS" => GetRestrictions(),
            "RESERVEDWORDS" => GetReservedWords(),
            "TABLES" => GetTables(restrictions),
            "COLUMNS" => GetColumns(restrictions),
            "INDEXES" => GetIndexes(restrictions),
            "INDEXCOLUMNS" => GetIndexColumns(restrictions),
            "VIEWS" => GetViews(restrictions),
            "FOREIGNKEYS" => GetForeignKeys(restrictions),
            _ => throw new ArgumentException($"Unknown schema collection: {collectionName}", nameof(collectionName))
        };
    }

    #endregion

    #region MetaDataCollections

    private static DataTable GetMetaDataCollections()
    {
        var table = new DataTable(METADATA_COLLECTIONS);
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("NumberOfRestrictions", typeof(int));
        table.Columns.Add("NumberOfIdentifierParts", typeof(int));

        table.Rows.Add(METADATA_COLLECTIONS, 0, 0);
        table.Rows.Add(DATA_SOURCE_INFORMATION, 0, 0);
        table.Rows.Add(DATA_TYPES, 0, 0);
        table.Rows.Add(RESTRICTIONS, 0, 0);
        table.Rows.Add(RESERVED_WORDS, 0, 0);
        table.Rows.Add(TABLES, 2, 2);
        table.Rows.Add(COLUMNS, 3, 3);
        table.Rows.Add(INDEXES, 2, 2);
        table.Rows.Add(INDEX_COLUMNS, 3, 3);
        table.Rows.Add(VIEWS, 1, 1);
        table.Rows.Add(FOREIGN_KEYS, 2, 2);

        return table;
    }

    #endregion

    #region DataSourceInformation

    private static DataTable GetDataSourceInformation()
    {
        var table = new DataTable(DATA_SOURCE_INFORMATION);

        table.Columns.Add("CompositeIdentifierSeparatorPattern", typeof(string));
        table.Columns.Add("DataSourceProductName", typeof(string));
        table.Columns.Add("DataSourceProductVersion", typeof(string));
        table.Columns.Add("DataSourceProductVersionNormalized", typeof(string));
        table.Columns.Add("GroupByBehavior", typeof(int));
        table.Columns.Add("IdentifierPattern", typeof(string));
        table.Columns.Add("IdentifierCase", typeof(int));
        table.Columns.Add("OrderByColumnsInSelect", typeof(bool));
        table.Columns.Add("ParameterMarkerFormat", typeof(string));
        table.Columns.Add("ParameterMarkerPattern", typeof(string));
        table.Columns.Add("ParameterNameMaxLength", typeof(int));
        table.Columns.Add("ParameterNamePattern", typeof(string));
        table.Columns.Add("QuotedIdentifierPattern", typeof(string));
        table.Columns.Add("QuotedIdentifierCase", typeof(int));
        table.Columns.Add("StatementSeparatorPattern", typeof(string));
        table.Columns.Add("StringLiteralPattern", typeof(string));
        table.Columns.Add("SupportedJoinOperators", typeof(int));

        var row = table.NewRow();
        row["CompositeIdentifierSeparatorPattern"] = @"\.";
        row["DataSourceProductName"] = "WitDatabase";
        row["DataSourceProductVersion"] = WitDatabaseVersion.Text;
        row["DataSourceProductVersionNormalized"] = WitDatabaseVersion.Normalized;
        row["GroupByBehavior"] = 1; // GroupByBehavior.Unrelated
        row["IdentifierPattern"] = @"(^\[\p{Lo}\p{Lu}\p{Ll}_@#][\p{Lo}\p{Lu}\p{Ll}\p{Nd}@$#_]*$)|(^\[([^\]\0]|\]\])+\]$)|(^\"".+\""$)";
        row["IdentifierCase"] = 1; // IdentifierCase.Insensitive
        row["OrderByColumnsInSelect"] = false;
        row["ParameterMarkerFormat"] = "@{0}";
        row["ParameterMarkerPattern"] = @"@[\p{Lo}\p{Lu}\p{Ll}\p{Lm}_@#][\p{Lo}\p{Lu}\p{Ll}\p{Lm}\p{Nd}\uff3f_@#\$]*(?=\s+|$)";
        row["ParameterNameMaxLength"] = 128;
        row["ParameterNamePattern"] = @"^[\p{Lo}\p{Lu}\p{Ll}\p{Lm}_@#][\p{Lo}\p{Lu}\p{Ll}\p{Lm}\p{Nd}\uff3f_@#\$]*(?=\s+|$)";
        row["QuotedIdentifierPattern"] = @"(([^\]]|\]\])*)";
        row["QuotedIdentifierCase"] = 1; // IdentifierCase.Insensitive
        row["StatementSeparatorPattern"] = @";";
        row["StringLiteralPattern"] = @"'(([^']|'')*)'";
        row["SupportedJoinOperators"] = 15; // All join types

        table.Rows.Add(row);
        return table;
    }

    #endregion

    #region DataTypes

    private static DataTable GetDataTypes()
    {
        var table = new DataTable(DATA_TYPES);

        table.Columns.Add("TypeName", typeof(string));
        table.Columns.Add("ProviderDbType", typeof(int));
        table.Columns.Add("ColumnSize", typeof(long));
        table.Columns.Add("CreateFormat", typeof(string));
        table.Columns.Add("CreateParameters", typeof(string));
        table.Columns.Add("DataType", typeof(string));
        table.Columns.Add("IsAutoIncrementable", typeof(bool));
        table.Columns.Add("IsBestMatch", typeof(bool));
        table.Columns.Add("IsCaseSensitive", typeof(bool));
        table.Columns.Add("IsFixedLength", typeof(bool));
        table.Columns.Add("IsFixedPrecisionScale", typeof(bool));
        table.Columns.Add("IsLong", typeof(bool));
        table.Columns.Add("IsNullable", typeof(bool));
        table.Columns.Add("IsSearchable", typeof(bool));
        table.Columns.Add("IsSearchableWithLike", typeof(bool));
        table.Columns.Add("IsUnsigned", typeof(bool));
        table.Columns.Add("MaximumScale", typeof(short));
        table.Columns.Add("MinimumScale", typeof(short));
        table.Columns.Add("IsConcurrencyType", typeof(bool));
        table.Columns.Add("IsLiteralSupported", typeof(bool));
        table.Columns.Add("LiteralPrefix", typeof(string));
        table.Columns.Add("LiteralSuffix", typeof(string));

        AddDataType(table, "TINYINT", DbType.SByte, 3, "TINYINT", null, "System.SByte", true, true);
        AddDataType(table, "UTINYINT", DbType.Byte, 3, "UTINYINT", null, "System.Byte", true, true, isUnsigned: true);
        AddDataType(table, "SMALLINT", DbType.Int16, 5, "SMALLINT", null, "System.Int16", true, true);
        AddDataType(table, "USMALLINT", DbType.UInt16, 5, "USMALLINT", null, "System.UInt16", true, true, isUnsigned: true);
        AddDataType(table, "INT", DbType.Int32, 10, "INT", null, "System.Int32", true, true);
        AddDataType(table, "UINT", DbType.UInt32, 10, "UINT", null, "System.UInt32", true, true, isUnsigned: true);
        AddDataType(table, "BIGINT", DbType.Int64, 19, "BIGINT", null, "System.Int64", true, true);
        AddDataType(table, "UBIGINT", DbType.UInt64, 20, "UBIGINT", null, "System.UInt64", true, true, isUnsigned: true);
        AddDataType(table, "FLOAT", DbType.Single, 7, "FLOAT", null, "System.Single", false, true);
        AddDataType(table, "DOUBLE", DbType.Double, 15, "DOUBLE", null, "System.Double", false, true);
        AddDataType(table, "DECIMAL", DbType.Decimal, 38, "DECIMAL({0},{1})", "precision,scale", "System.Decimal", false, true);
        AddDataType(table, "BOOLEAN", DbType.Boolean, 1, "BOOLEAN", null, "System.Boolean", false, true);
        AddDataType(table, "DATE", DbType.Date, 10, "DATE", null, "System.DateOnly", false, true);
        AddDataType(table, "TIME", DbType.Time, 16, "TIME", null, "System.TimeOnly", false, true);
        AddDataType(table, "DATETIME", DbType.DateTime, 23, "DATETIME", null, "System.DateTime", false, true);
        AddDataType(table, "DATETIMEOFFSET", DbType.DateTimeOffset, 34, "DATETIMEOFFSET", null, "System.DateTimeOffset", false, true);
        AddDataType(table, "GUID", DbType.Guid, 36, "GUID", null, "System.Guid", false, true);
        AddDataType(table, "VARCHAR", DbType.String, 65535, "VARCHAR({0})", "max length", "System.String", false, true, isLong: false, searchableWithLike: true);
        AddDataType(table, "TEXT", DbType.String, int.MaxValue, "TEXT", null, "System.String", false, true, isLong: true, searchableWithLike: true);
        AddDataType(table, "VARBINARY", DbType.Binary, 65535, "VARBINARY({0})", "max length", "System.Byte[]", false, true, isLong: false);
        AddDataType(table, "BLOB", DbType.Binary, int.MaxValue, "BLOB", null, "System.Byte[]", false, true, isLong: true);
        AddDataType(table, "JSON", DbType.String, int.MaxValue, "JSON", null, "System.String", false, true, isLong: true);
        AddDataType(table, "ROWVERSION", DbType.Binary, 8, "ROWVERSION", null, "System.Byte[]", false, false, isConcurrency: true);

        return table;
    }

    private static void AddDataType(DataTable table, string typeName, DbType dbType, long columnSize,
        string createFormat, string? createParams, string dataType, bool isAutoIncrementable, bool isBestMatch,
        bool isLong = false, bool searchableWithLike = false, bool isUnsigned = false, bool isConcurrency = false)
    {
        var row = table.NewRow();
        row["TypeName"] = typeName;
        row["ProviderDbType"] = (int)dbType;
        row["ColumnSize"] = columnSize;
        row["CreateFormat"] = createFormat;
        row["CreateParameters"] = (object?)createParams ?? DBNull.Value;
        row["DataType"] = dataType;
        row["IsAutoIncrementable"] = isAutoIncrementable;
        row["IsBestMatch"] = isBestMatch;
        row["IsCaseSensitive"] = false;
        row["IsFixedLength"] = false;
        row["IsFixedPrecisionScale"] = dbType == DbType.Decimal;
        row["IsLong"] = isLong;
        row["IsNullable"] = true;
        row["IsSearchable"] = true;
        row["IsSearchableWithLike"] = searchableWithLike;
        row["IsUnsigned"] = isUnsigned;
        row["MaximumScale"] = (short)(dbType == DbType.Decimal ? 38 : 0);
        row["MinimumScale"] = (short)0;
        row["IsConcurrencyType"] = isConcurrency;
        row["IsLiteralSupported"] = true;
        row["LiteralPrefix"] = dbType == DbType.String ? "'" : DBNull.Value;
        row["LiteralSuffix"] = dbType == DbType.String ? "'" : DBNull.Value;

        table.Rows.Add(row);
    }

    #endregion

    #region Restrictions

    private static DataTable GetRestrictions()
    {
        var table = new DataTable(RESTRICTIONS);

        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("RestrictionName", typeof(string));
        table.Columns.Add("RestrictionDefault", typeof(string));
        table.Columns.Add("RestrictionNumber", typeof(int));

        // Tables restrictions
        table.Rows.Add(TABLES, "Catalog", "", 1);
        table.Rows.Add(TABLES, "Table", "", 2);

        // Columns restrictions
        table.Rows.Add(COLUMNS, "Catalog", "", 1);
        table.Rows.Add(COLUMNS, "Table", "", 2);
        table.Rows.Add(COLUMNS, "Column", "", 3);

        // Indexes restrictions
        table.Rows.Add(INDEXES, "Catalog", "", 1);
        table.Rows.Add(INDEXES, "Index", "", 2);

        // IndexColumns restrictions
        table.Rows.Add(INDEX_COLUMNS, "Catalog", "", 1);
        table.Rows.Add(INDEX_COLUMNS, "Index", "", 2);
        table.Rows.Add(INDEX_COLUMNS, "Column", "", 3);

        // Views restrictions
        table.Rows.Add(VIEWS, "View", "", 1);

        // ForeignKeys restrictions
        table.Rows.Add(FOREIGN_KEYS, "Catalog", "", 1);
        table.Rows.Add(FOREIGN_KEYS, "Table", "", 2);

        return table;
    }

    #endregion

    #region ReservedWords

    private static DataTable GetReservedWords()
    {
        var table = new DataTable(RESERVED_WORDS);
        table.Columns.Add("ReservedWord", typeof(string));

        var reservedWords = new[]
        {
            "ADD", "ALL", "ALTER", "AND", "AS", "ASC", "BETWEEN", "BY", "CASE", "CHECK",
            "COLUMN", "CONSTRAINT", "CREATE", "CROSS", "DEFAULT", "DELETE", "DESC", "DISTINCT",
            "DROP", "ELSE", "END", "EXISTS", "FALSE", "FOREIGN", "FROM", "FULL", "GROUP",
            "HAVING", "IF", "IN", "INDEX", "INNER", "INSERT", "INTO", "IS", "JOIN", "KEY",
            "LEFT", "LIKE", "LIMIT", "NOT", "NULL", "OFFSET", "ON", "OR", "ORDER", "OUTER",
            "PRIMARY", "REFERENCES", "RIGHT", "SELECT", "SET", "TABLE", "THEN", "TRUE",
            "UNION", "UNIQUE", "UPDATE", "VALUES", "WHEN", "WHERE"
        };

        foreach (var word in reservedWords)
        {
            table.Rows.Add(word);
        }

        return table;
    }

    #endregion

    #region Tables

    private DataTable GetTables(string?[]? restrictions)
    {
        var table = new DataTable(TABLES);

        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("TABLE_TYPE", typeof(string));

        var tableNameFilter = restrictions?.Length > 1 ? restrictions[1] : null;

        foreach (var tableName in m_engine.GetAllTableNames())
        {
            if (tableNameFilter != null && !tableName.Equals(tableNameFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var row = table.NewRow();
            row["TABLE_CATALOG"] = "main";
            row["TABLE_SCHEMA"] = DBNull.Value;
            row["TABLE_NAME"] = tableName;
            row["TABLE_TYPE"] = "TABLE";
            table.Rows.Add(row);
        }

        return table;
    }

    #endregion

    #region Columns

    private DataTable GetColumns(string?[]? restrictions)
    {
        var table = new DataTable(COLUMNS);

        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("COLUMN_NAME", typeof(string));
        table.Columns.Add("ORDINAL_POSITION", typeof(int));
        table.Columns.Add("COLUMN_DEFAULT", typeof(string));
        table.Columns.Add("IS_NULLABLE", typeof(string));
        table.Columns.Add("DATA_TYPE", typeof(string));
        table.Columns.Add("CHARACTER_MAXIMUM_LENGTH", typeof(int));
        table.Columns.Add("NUMERIC_PRECISION", typeof(int));
        table.Columns.Add("NUMERIC_SCALE", typeof(int));
        table.Columns.Add("IS_AUTOINCREMENT", typeof(string));

        var tableNameFilter = restrictions?.Length > 1 ? restrictions[1] : null;
        var columnNameFilter = restrictions?.Length > 2 ? restrictions[2] : null;

        foreach (var tableName in m_engine.GetAllTableNames())
        {
            if (tableNameFilter != null && !tableName.Equals(tableNameFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var tableDef = m_engine.GetTable(tableName);
            if (tableDef == null) continue;

            for (int i = 0; i < tableDef.Columns.Count; i++)
            {
                var col = tableDef.Columns[i];

                if (columnNameFilter != null && !col.Name.Equals(columnNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var row = table.NewRow();
                row["TABLE_CATALOG"] = "main";
                row["TABLE_SCHEMA"] = DBNull.Value;
                row["TABLE_NAME"] = tableName;
                row["COLUMN_NAME"] = col.Name;
                row["ORDINAL_POSITION"] = i + 1;
                row["COLUMN_DEFAULT"] = col.DisplayDefault() ?? (object)DBNull.Value;
                row["IS_NULLABLE"] = col.Nullable ? "YES" : "NO";
                row["DATA_TYPE"] = col.Type.ToString();
                row["CHARACTER_MAXIMUM_LENGTH"] = col.MaxLength.HasValue ? col.MaxLength.Value : (object)DBNull.Value;
                row["NUMERIC_PRECISION"] = col.Precision.HasValue ? col.Precision.Value : (object)DBNull.Value;
                row["NUMERIC_SCALE"] = col.Scale.HasValue ? col.Scale.Value : (object)DBNull.Value;
                row["IS_AUTOINCREMENT"] = col.IsAutoIncrement ? "YES" : "NO";

                table.Rows.Add(row);
            }
        }

        return table;
    }

    #endregion

    #region Indexes

    private DataTable GetIndexes(string?[]? restrictions)
    {
        var table = new DataTable(INDEXES);

        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("INDEX_NAME", typeof(string));
        table.Columns.Add("IS_UNIQUE", typeof(bool));
        table.Columns.Add("IS_PRIMARY", typeof(bool));

        var indexNameFilter = restrictions?.Length > 1 ? restrictions[1] : null;

        foreach (var tableName in m_engine.GetAllTableNames())
        {
            foreach (var indexDef in m_engine.GetTableIndexes(tableName))
            {
                if (indexNameFilter != null && !indexDef.Name.Equals(indexNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var row = table.NewRow();
                row["TABLE_CATALOG"] = "main";
                row["TABLE_SCHEMA"] = DBNull.Value;
                row["TABLE_NAME"] = tableName;
                row["INDEX_NAME"] = indexDef.Name;
                row["IS_UNIQUE"] = indexDef.IsUnique;
                row["IS_PRIMARY"] = false; // Primary key is implicit in WitDatabase

                table.Rows.Add(row);
            }
        }

        return table;
    }

    #endregion

    #region IndexColumns

    private DataTable GetIndexColumns(string?[]? restrictions)
    {
        var table = new DataTable(INDEX_COLUMNS);

        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("INDEX_NAME", typeof(string));
        table.Columns.Add("COLUMN_NAME", typeof(string));
        table.Columns.Add("ORDINAL_POSITION", typeof(int));
        table.Columns.Add("SORT_ORDER", typeof(string));

        var indexNameFilter = restrictions?.Length > 1 ? restrictions[1] : null;
        var columnNameFilter = restrictions?.Length > 2 ? restrictions[2] : null;

        foreach (var tableName in m_engine.GetAllTableNames())
        {
            foreach (var indexDef in m_engine.GetTableIndexes(tableName))
            {
                if (indexNameFilter != null && !indexDef.Name.Equals(indexNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                for (int i = 0; i < indexDef.Columns.Count; i++)
                {
                    var colName = indexDef.Columns[i];

                    if (columnNameFilter != null && !colName.Equals(columnNameFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var row = table.NewRow();
                    row["TABLE_CATALOG"] = "main";
                    row["TABLE_SCHEMA"] = DBNull.Value;
                    row["TABLE_NAME"] = tableName;
                    row["INDEX_NAME"] = indexDef.Name;
                    row["COLUMN_NAME"] = colName;
                    row["ORDINAL_POSITION"] = i + 1;
                    row["SORT_ORDER"] = "ASC"; // Default to ASC

                    table.Rows.Add(row);
                }
            }
        }

        return table;
    }

    #endregion

    #region Views

    private DataTable GetViews(string?[]? restrictions)
    {
        var table = new DataTable(VIEWS);

        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("VIEW_DEFINITION", typeof(string));

        // Note: Views would need to be exposed through the engine
        // For now, return an empty table

        return table;
    }

    #endregion

    #region ForeignKeys

    private DataTable GetForeignKeys(string?[]? restrictions)
    {
        var table = new DataTable(FOREIGN_KEYS);

        table.Columns.Add("CONSTRAINT_CATALOG", typeof(string));
        table.Columns.Add("CONSTRAINT_SCHEMA", typeof(string));
        table.Columns.Add("CONSTRAINT_NAME", typeof(string));
        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("CONSTRAINT_TYPE", typeof(string));
        table.Columns.Add("IS_DEFERRABLE", typeof(string));
        table.Columns.Add("INITIALLY_DEFERRED", typeof(string));

        var tableNameFilter = restrictions?.Length > 1 ? restrictions[1] : null;

        foreach (var tableName in m_engine.GetAllTableNames())
        {
            if (tableNameFilter != null && !tableName.Equals(tableNameFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var tableDef = m_engine.GetTable(tableName);
            if (tableDef == null) continue;

            // Check NamedConstraints
            if (tableDef.NamedConstraints != null)
            {
                foreach (var constraint in tableDef.NamedConstraints)
                {
                    if (constraint.Type != Database.Definitions.ConstraintType.ForeignKey)
                        continue;

                    var row = table.NewRow();
                    row["CONSTRAINT_CATALOG"] = "main";
                    row["CONSTRAINT_SCHEMA"] = DBNull.Value;
                    row["CONSTRAINT_NAME"] = constraint.Name;
                    row["TABLE_CATALOG"] = "main";
                    row["TABLE_SCHEMA"] = DBNull.Value;
                    row["TABLE_NAME"] = tableName;
                    row["CONSTRAINT_TYPE"] = "FOREIGN KEY";
                    row["IS_DEFERRABLE"] = "NO";
                    row["INITIALLY_DEFERRED"] = "NO";

                    table.Rows.Add(row);
                }
            }
        }

        return table;
    }

    #endregion
}
