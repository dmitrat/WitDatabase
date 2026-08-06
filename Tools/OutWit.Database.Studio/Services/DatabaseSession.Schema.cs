using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Schema information methods for DatabaseSession.
/// </summary>
public sealed partial class DatabaseSession
{
    #region Tables and Views

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        const string sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";

        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        var tables = new List<TableInfo>();
        using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableInfo { Name = reader.GetString(0) });
        }

        return tables;
    }

    public async Task<IReadOnlyList<string>> GetViewsAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return await ExecuteStringListQueryAsync(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'VIEW'", ct);
    }

    #endregion

    #region Indexes, Triggers, Sequences

    public async Task<IReadOnlyList<string>> GetIndexesAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return await ExecuteStringListQueryAsync(
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES", ct);
    }

    public async Task<IReadOnlyList<string>> GetTriggersAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            return await ExecuteStringListQueryAsync(
                "SELECT TRIGGER_NAME FROM INFORMATION_SCHEMA.TRIGGERS", ct);
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "INFORMATION_SCHEMA.TRIGGERS not available, triggers list will be empty");
            return [];
        }
    }

    public async Task<IReadOnlyList<string>> GetSequencesAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            return await ExecuteStringListQueryAsync(
                "SELECT SEQUENCE_NAME FROM INFORMATION_SCHEMA.SEQUENCES", ct);
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "INFORMATION_SCHEMA.SEQUENCES not available, sequences list will be empty");
            return [];
        }
    }

    #endregion

    #region Columns

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        var columns = await ReadColumnsFromSchemaAsync(tableName, ct);
        await TryMarkPrimaryKeysAsync(tableName, columns, ct);

        return columns;
    }

    public Task<IReadOnlyList<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken ct = default) =>
        GetColumnsAsync(tableName, ct);

    private async Task<List<ColumnInfo>> ReadColumnsFromSchemaAsync(string tableName, CancellationToken ct)
    {
        const string sql = @"
            SELECT 
                COLUMN_NAME,
                ORDINAL_POSITION,
                DATA_TYPE,
                IS_NULLABLE,
                COLUMN_DEFAULT,
                CHARACTER_MAXIMUM_LENGTH,
                NUMERIC_PRECISION,
                NUMERIC_SCALE,
                IS_GENERATED,
                GENERATION_EXPRESSION,
                IS_AUTOINCREMENT,
                IS_UNIQUE,
                CHECK_EXPRESSION,
                COLLATION_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName
            ORDER BY ORDINAL_POSITION";

        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@tableName", tableName);

        var columns = new List<ColumnInfo>();
        using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            columns.Add(MapColumnFromReader(reader));
        }

        return columns;
    }

    private static ColumnInfo MapColumnFromReader(System.Data.Common.DbDataReader reader)
    {
        var isNullableStr = reader.IsDBNull(3) ? "YES" : reader.GetString(3);
        var isAutoIncrementStr = reader.IsDBNull(10) ? "NO" : reader.GetString(10);
        var isUniqueStr = reader.IsDBNull(11) ? "NO" : reader.GetString(11);

        return new ColumnInfo
        {
            Name = reader.GetString(0),
            OrdinalPosition = reader.GetInt32(1),
            DataType = reader.GetString(2),
            IsNullable = isNullableStr.Equals("YES", StringComparison.OrdinalIgnoreCase),
            DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4),
            MaxLength = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            NumericPrecision = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            NumericScale = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            IsGenerated = reader.IsDBNull(8) ? "NEVER" : reader.GetString(8),
            GenerationExpression = reader.IsDBNull(9) ? null : reader.GetString(9),
            IsAutoIncrement = isAutoIncrementStr.Equals("YES", StringComparison.OrdinalIgnoreCase),
            IsUnique = isUniqueStr.Equals("YES", StringComparison.OrdinalIgnoreCase),
            CheckExpression = reader.IsDBNull(12) ? null : reader.GetString(12),
            Collation = reader.IsDBNull(13) ? null : reader.GetString(13),
            IsPrimaryKey = false // Will be set in TryMarkPrimaryKeysAsync
        };
    }

    private async Task TryMarkPrimaryKeysAsync(string tableName, List<ColumnInfo> columns, CancellationToken ct)
    {
        try
        {
            const string sql = @"
                SELECT kcu.COLUMN_NAME
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                   AND tc.TABLE_NAME = kcu.TABLE_NAME
                WHERE kcu.TABLE_NAME = @tableName
                  AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@tableName", tableName);

            var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = await command.ExecuteReaderAsync(ct);
            
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                    pkColumns.Add(reader.GetString(0));
            }

            foreach (var col in columns)
            {
                if (pkColumns.Contains(col.Name))
                    col.IsPrimaryKey = true;
            }
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Unable to read INFORMATION_SCHEMA primary key metadata");
        }
    }

    #endregion
}
