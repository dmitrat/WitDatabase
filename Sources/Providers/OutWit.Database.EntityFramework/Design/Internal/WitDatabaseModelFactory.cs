using System.Globalization;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using OutWit.Database.AdoNet;

namespace OutWit.Database.EntityFramework.Design.Internal;

/// <summary>
/// Reverse engineers a WitDatabase database into a model.
/// Used by 'dotnet ef dbcontext scaffold' command.
/// </summary>
public class WitDatabaseModelFactory : IDatabaseModelFactory
{
    #region IDatabaseModelFactory

    /// <summary>
    /// Creates a model from the database.
    /// </summary>
    public DatabaseModel Create(string connectionString, DatabaseModelFactoryOptions options)
    {
        using var connection = new WitDbConnection(connectionString);
        return Create(connection, options);
    }

    /// <summary>
    /// Creates a model from an existing connection.
    /// </summary>
    public DatabaseModel Create(DbConnection connection, DatabaseModelFactoryOptions options)
    {
        var model = new DatabaseModel();
        
        var needsClose = connection.State != System.Data.ConnectionState.Open;
        if (needsClose)
        {
            connection.Open();
        }

        try
        {
            model.DatabaseName = GetDatabaseName(connection);
            
            // Get tables
            foreach (var table in GetTables(connection, options))
            {
                model.Tables.Add(table);
            }
        }
        finally
        {
            if (needsClose)
            {
                connection.Close();
            }
        }

        return model;
    }

    #endregion

    #region Helper Methods

    private static string? GetDatabaseName(DbConnection connection)
    {
        // Extract database name from connection string
        var connectionString = connection.ConnectionString ?? string.Empty;
        
        // Look for "Data Source=" in connection string
        var index = connectionString.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var start = index + "Data Source=".Length;
            var end = connectionString.IndexOf(';', start);
            var dataSource = end >= 0 
                ? connectionString.Substring(start, end - start) 
                : connectionString[start..];
            
            return Path.GetFileNameWithoutExtension(dataSource);
        }
        
        return null;
    }

    /// <summary>
    /// Reads the tables through <c>INFORMATION_SCHEMA</c>, which is what this engine implements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This factory used to issue SQLite's own catalog queries - <c>SELECT name FROM sqlite_master</c>
    /// and four <c>PRAGMA</c>s - which this engine has never had and whose grammar does not even contain
    /// the word <c>PRAGMA</c>. So <c>dotnet ef dbcontext scaffold</c> failed on its first query, and
    /// database-first was not incomplete but inoperative.
    /// </para>
    /// <para>
    /// <b>The answer is not to emulate SQLite's private catalog.</b> The engine implements the standard
    /// one, which is what PostgreSQL and SQL Server expose and what the drop-in target actually is;
    /// <c>sqlite_master</c> is an implementation detail of a different database that this provider had
    /// copied along with the query.
    /// </para>
    /// </remarks>
    private static IEnumerable<DatabaseTable> GetTables(DbConnection connection, DatabaseModelFactoryOptions options)
    {
        var tables = new List<DatabaseTable>();
        var tablesToFilter = options.Tables.ToList();
        var names = new List<string>();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                names.Add(reader.GetString(0));
        }

        foreach (var tableName in names)
        {
            if (tablesToFilter.Count > 0 && !tablesToFilter.Contains(tableName))
                continue;

            var table = new DatabaseTable { Name = tableName };

            GetColumns(connection, table);
            GetPrimaryKey(connection, table);
            GetIndexes(connection, table);
            GetForeignKeys(connection, table);

            tables.Add(table);
        }

        return tables;
    }

    private static void GetColumns(DbConnection connection, DatabaseTable table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT,
                   CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_AUTOINCREMENT
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = '{table.Name}'
            ORDER BY ORDINAL_POSITION
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var column = new DatabaseColumn
            {
                Table = table,
                Name = reader.GetString(0),

                // Composed rather than taken straight from DATA_TYPE. The standard catalog reports the
                // base type in one column and the size in others, so reading DATA_TYPE alone would drop
                // the declared length again - which is the defect this phase spent its length chasing
                // through four separate layers.
                StoreType = ComposeStoreType(
                    reader.GetString(1),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6)),

                IsNullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                DefaultValueSql = reader.IsDBNull(3) ? null : reader.GetString(3)
            };

            if (!reader.IsDBNull(7) && string.Equals(reader.GetString(7), "YES", StringComparison.OrdinalIgnoreCase))
                column.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd;

            table.Columns.Add(column);
        }
    }

    /// <summary>
    /// The primary key, read from the catalog rather than guessed.
    /// </summary>
    /// <remarks>
    /// This used to infer the key from which columns came back auto-generated, so a table whose key was
    /// not auto-generated scaffolded with no key at all.
    /// </remarks>
    private static void GetPrimaryKey(DbConnection connection, DatabaseTable table)
    {
        var keyColumns = new List<string>();
        string? constraintName = null;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT CONSTRAINT_NAME, COLUMN_NAME
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                WHERE TABLE_NAME = '{table.Name}' AND REFERENCED_TABLE_NAME IS NULL
                ORDER BY ORDINAL_POSITION
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                constraintName ??= reader.GetString(0);
                keyColumns.Add(reader.GetString(1));
            }
        }

        if (keyColumns.Count == 0)
            return;

        var pk = new DatabasePrimaryKey
        {
            Table = table,
            Name = constraintName ?? $"PK_{table.Name}"
        };

        foreach (var name in keyColumns)
        {
            var column = table.Columns.FirstOrDefault(c => c.Name == name);
            if (column != null)
                pk.Columns.Add(column);
        }

        if (pk.Columns.Count > 0)
            table.PrimaryKey = pk;
    }

    private static void GetIndexes(DbConnection connection, DatabaseTable table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT INDEX_NAME, COLUMN_NAME, IS_UNIQUE
            FROM INFORMATION_SCHEMA.INDEXES
            WHERE TABLE_NAME = '{table.Name}'
            ORDER BY INDEX_NAME, ORDINAL_POSITION
            """;

        var indexes = new Dictionary<string, DatabaseIndex>(StringComparer.OrdinalIgnoreCase);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var indexName = reader.GetString(0);
            var columnName = reader.GetString(1);
            var isUnique = !reader.IsDBNull(2)
                           && string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase);

            if (!indexes.TryGetValue(indexName, out var index))
            {
                index = new DatabaseIndex { Table = table, Name = indexName, IsUnique = isUnique };
                indexes[indexName] = index;
                table.Indexes.Add(index);
            }

            var column = table.Columns.FirstOrDefault(c => c.Name == columnName);
            if (column != null)
                index.Columns.Add(column);
        }
    }

    private static void GetForeignKeys(DbConnection connection, DatabaseTable table)
    {
        var deleteRules = ReadDeleteRules(connection);
        var foreignKeys = new Dictionary<string, DatabaseForeignKey>(StringComparer.OrdinalIgnoreCase);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT CONSTRAINT_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_NAME = '{table.Name}' AND REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY CONSTRAINT_NAME, ORDINAL_POSITION
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var constraintName = reader.GetString(0);
            var columnName = reader.GetString(1);
            var principalTable = reader.GetString(2);
            var principalColumn = reader.IsDBNull(3) ? null : reader.GetString(3);

            if (!foreignKeys.TryGetValue(constraintName, out var fk))
            {
                fk = new DatabaseForeignKey
                {
                    Table = table,
                    Name = constraintName,
                    PrincipalTable = new DatabaseTable { Name = principalTable },
                    OnDelete = deleteRules.TryGetValue(constraintName, out var rule)
                        ? ParseReferentialAction(rule)
                        : ReferentialAction.NoAction
                };

                foreignKeys[constraintName] = fk;
                table.ForeignKeys.Add(fk);
            }

            var column = table.Columns.FirstOrDefault(c => c.Name == columnName);
            if (column != null)
                fk.Columns.Add(column);

            if (principalColumn != null)
                fk.PrincipalColumns.Add(new DatabaseColumn { Name = principalColumn });
        }
    }

    private static Dictionary<string, string> ReadDeleteRules(DbConnection connection)
    {
        var rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CONSTRAINT_NAME, DELETE_RULE FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(1))
                rules[reader.GetString(0)] = reader.GetString(1);
        }

        return rules;
    }

    /// <summary>
    /// Puts the declared size back on the base type, which the standard catalog reports separately.
    /// </summary>
    private static string ComposeStoreType(string dataType, int? maxLength, int? precision, int? scale)
    {
        if (maxLength is > 0)
            return $"{dataType}({maxLength.Value.ToString(CultureInfo.InvariantCulture)})";

        if (precision is > 0)
        {
            return scale is > 0
                ? $"{dataType}({precision.Value.ToString(CultureInfo.InvariantCulture)},{scale.Value.ToString(CultureInfo.InvariantCulture)})"
                : $"{dataType}({precision.Value.ToString(CultureInfo.InvariantCulture)})";
        }

        return dataType;
    }

    private static ReferentialAction ParseReferentialAction(string action)
    {
        return action.ToUpperInvariant() switch
        {
            "CASCADE" => ReferentialAction.Cascade,
            "RESTRICT" => ReferentialAction.Restrict,
            "SET NULL" => ReferentialAction.SetNull,
            "SET DEFAULT" => ReferentialAction.SetDefault,
            _ => ReferentialAction.NoAction
        };
    }

    #endregion
}
