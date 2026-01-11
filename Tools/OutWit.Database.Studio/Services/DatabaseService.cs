using Microsoft.Extensions.Logging;
using OutWit.Database.AdoNet;
using OutWit.Database.Studio.Models;
using System.Data;
using System.Diagnostics;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Implementation of <see cref="IDatabaseService"/> using ADO.NET.
/// </summary>
public sealed class DatabaseService : IDatabaseService
{
    #region Events

    public event EventHandler<bool>? ConnectionStatusChanged;

    #endregion

    #region Fields

    private readonly ILogger<DatabaseService> m_logger;
    private WitDbConnection? m_connection;
    private ConnectionInfo? m_currentConnection;
    private bool m_disposed;

    #endregion

    #region Constructors

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        m_logger = logger;
    }

    #endregion

    #region Connection Management

    public async Task<bool> ConnectAsync(ConnectionInfo connection, CancellationToken ct = default)
    {
        var wasConnected = IsConnected;

        try
        {
            await DisconnectAsync();

            var connectionString = connection.BuildConnectionString();
            m_logger.LogInformation("Attempting to connect with connection string: {ConnectionString}", connectionString);

            m_connection = new WitDbConnection(connectionString);

            await m_connection.OpenAsync(ct);
            m_currentConnection = connection;

            m_logger.LogInformation("Successfully connected to database: {FilePath}", connection.FilePath);

            RaiseConnectionStatusChangedIfNeeded(wasConnected);
            return true;
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to connect to database: {FilePath}, ConnectionString: {ConnectionString}",
                connection.FilePath, connection.BuildConnectionString());
            m_connection?.Dispose();
            m_connection = null;
            m_currentConnection = null;

            RaiseConnectionStatusChangedIfNeeded(wasConnected);
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        var wasConnected = IsConnected;

        if (m_connection != null)
        {
            await m_connection.CloseAsync();
            m_connection.Dispose();
            m_connection = null;
            m_currentConnection = null;
            m_logger.LogInformation("Disconnected from database");
        }

        RaiseConnectionStatusChangedIfNeeded(wasConnected);
    }

    public bool IsConnected => m_connection?.State == ConnectionState.Open;

    public ConnectionInfo? CurrentConnection => m_currentConnection;

    private void RaiseConnectionStatusChangedIfNeeded(bool wasConnected)
    {
        var isConnected = IsConnected;
        if (isConnected == wasConnected)
            return;

        ConnectionStatusChanged?.Invoke(this, isConnected);
    }

    #endregion

    #region Schema Information

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
            var tableName = reader.GetString(0);
            tables.Add(new TableInfo { Name = tableName });
        }

        return tables;
    }

    public async Task<IReadOnlyList<string>> GetViewsAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        const string sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'VIEW'";

        return await ExecuteStringListQueryAsync(sql, ct);
    }

    public async Task<IReadOnlyList<string>> GetIndexesAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        const string sql = "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES";

        return await ExecuteStringListQueryAsync(sql, ct);
    }

    public async Task<IReadOnlyList<string>> GetTriggersAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            const string sql = "SELECT TRIGGER_NAME FROM INFORMATION_SCHEMA.TRIGGERS";
            return await ExecuteStringListQueryAsync(sql, ct);
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
            const string sql = "SELECT SEQUENCE_NAME FROM INFORMATION_SCHEMA.SEQUENCES";
            return await ExecuteStringListQueryAsync(sql, ct);
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "INFORMATION_SCHEMA.SEQUENCES not available, sequences list will be empty");
            return [];
        }
    }

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        const string sql = @"
            SELECT 
                COLUMN_NAME,
                ORDINAL_POSITION,
                DATA_TYPE,
                IS_NULLABLE,
                COLUMN_DEFAULT,
                IS_GENERATED
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
            var isNullableStr = reader.IsDBNull(3) ? "YES" : reader.GetString(3);

            columns.Add(new ColumnInfo
            {
                Name = reader.GetString(0),
                OrdinalPosition = reader.GetInt32(1),
                DataType = reader.GetString(2),
                IsNullable = isNullableStr.Equals("YES", StringComparison.OrdinalIgnoreCase),
                DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsPrimaryKey = false
            });
        }

        await TryMarkPrimaryKeysAsync(tableName, columns, ct);

        return columns;
    }

    public Task<IReadOnlyList<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken ct = default) =>
        GetColumnsAsync(tableName, ct);

    public async Task<string?> GetViewDefinitionAsync(string viewName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            const string sql = "SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = @viewName";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@viewName", viewName);

            var result = await command.ExecuteScalarAsync(ct);
            return result as string;
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Failed to get view definition for {ViewName}", viewName);
            return null;
        }
    }

    public async Task<string?> GetTriggerDefinitionAsync(string triggerName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            const string sql = "SELECT ACTION_STATEMENT FROM INFORMATION_SCHEMA.TRIGGERS WHERE TRIGGER_NAME = @triggerName";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@triggerName", triggerName);

            var result = await command.ExecuteScalarAsync(ct);
            return result as string;
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Failed to get trigger definition for {TriggerName}", triggerName);
            return null;
        }
    }

    public async Task<string?> GetIndexDefinitionAsync(string indexName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            // Build index definition from INFORMATION_SCHEMA.INDEXES
            const string sql = @"
                SELECT TABLE_NAME, COLUMN_NAME, IS_UNIQUE, FILTER_CONDITION
                FROM INFORMATION_SCHEMA.INDEXES 
                WHERE INDEX_NAME = @indexName
                ORDER BY ORDINAL_POSITION";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@indexName", indexName);

            string? tableName = null;
            var columns = new List<string>();
            var isUnique = false;
            string? filter = null;

            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tableName ??= reader.GetString(0);
                columns.Add(reader.GetString(1));
                isUnique = !reader.IsDBNull(2) && reader.GetBoolean(2);
                filter ??= reader.IsDBNull(3) ? null : reader.GetString(3);
            }

            if (tableName == null || columns.Count == 0)
                return null;

            var uniqueStr = isUnique ? "UNIQUE " : "";
            var filterStr = string.IsNullOrEmpty(filter) ? "" : $" WHERE {filter}";
            return $"CREATE {uniqueStr}INDEX {indexName} ON {tableName} ({string.Join(", ", columns)}){filterStr}";
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Failed to get index definition for {IndexName}", indexName);
            return null;
        }
    }

    public async Task<string?> GetTableDefinitionAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            // Get columns
            var columns = await GetColumnsAsync(tableName, ct);
            if (columns.Count == 0)
                return null;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"CREATE TABLE \"{tableName}\" (");

            var columnDefs = new List<string>();
            var pkColumns = new List<string>();

            foreach (var col in columns)
            {
                var colDef = $"    \"{col.Name}\" {col.DataType}";
                
                if (!col.IsNullable)
                    colDef += " NOT NULL";
                
                if (!string.IsNullOrEmpty(col.DefaultValue))
                    colDef += $" DEFAULT {col.DefaultValue}";

                columnDefs.Add(colDef);

                if (col.IsPrimaryKey)
                    pkColumns.Add($"\"{col.Name}\"");
            }

            sb.AppendLine(string.Join(",\n", columnDefs));

            if (pkColumns.Count > 0)
            {
                sb.AppendLine($"    ,PRIMARY KEY ({string.Join(", ", pkColumns)})");
            }

            sb.Append(");");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Failed to get table definition for {TableName}", tableName);
            return null;
        }
    }

    private async Task TryMarkPrimaryKeysAsync(string tableName, List<ColumnInfo> columns, CancellationToken ct)
    {
        // 1) Prefer INFORMATION_SCHEMA for PK metadata
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

            using (var command = m_connection!.CreateCommand())
            {
                command.CommandText = sql;
                command.Parameters.AddWithValue("@tableName", tableName);

                var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (!reader.IsDBNull(0))
                        pkColumns.Add(reader.GetString(0));
                }

                if (pkColumns.Count > 0)
                {
                    for (var i = 0; i < columns.Count; i++)
                    {
                        var c = columns[i];
                        if (pkColumns.Contains(c.Name))
                            c.IsPrimaryKey = true;
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Unable to read INFORMATION_SCHEMA primary key metadata");
        }

        // 2) Fallback: PRAGMA table_info exposes PK flag reliably
        try
        {
            const string pragmaSql = "PRAGMA table_info(@tableName)";

            using var command = m_connection!.CreateCommand();
            command.CommandText = pragmaSql;
            command.Parameters.AddWithValue("@tableName", tableName);

            var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                // PRAGMA table_info: (cid, name, type, notnull, dflt_value, pk)
                if (!reader.IsDBNull(1) && !reader.IsDBNull(5) && reader.GetInt32(5) > 0)
                    pkColumns.Add(reader.GetString(1));
            }

            if (pkColumns.Count == 0)
                return;

            for (var i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                if (pkColumns.Contains(c.Name))
                    c.IsPrimaryKey = true;
            }
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Unable to read PRAGMA table_info for PK metadata");
        }
    }

    #endregion

    #region Query Execution

    public async Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();

        var result = new QueryResult();
        var sw = Stopwatch.StartNew();

        try
        {
            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;

            using var reader = await command.ExecuteReaderAsync(ct);

            // Create DataTable with schema
            var dataTable = new DataTable("QueryResult");

            // Add columns with proper types
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var columnType = reader.GetFieldType(i);
                dataTable.Columns.Add(columnName, columnType);
            }

            // Read rows
            while (await reader.ReadAsync(ct))
            {
                var row = dataTable.NewRow();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                }
                dataTable.Rows.Add(row);
            }

            result.Data = dataTable;
            result.RowsAffected = dataTable.Rows.Count;
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;

            m_logger.LogInformation("Query executed successfully in {Time}ms, {Rows} rows, {Columns} columns",
                result.ExecutionTimeMs, result.RowsAffected, reader.FieldCount);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = FormatErrorMessage(ex);
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            m_logger.LogError(ex, "Query execution failed");
        }
        finally
        {
            sw.Stop();
        }

        return result;
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();

        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<object?> ExecuteScalarAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();

        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync(ct);
    }

    #endregion

    #region Helper Methods

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to a database");
    }

    private static string FormatErrorMessage(Exception ex)
    {
        if (ex.Message.StartsWith("Line ", StringComparison.Ordinal))
        {
            return $"SQL Syntax Error: {ex.Message}";
        }

        if (ex.InnerException != null)
        {
            var innerMessage = ex.InnerException.Message;
            if (innerMessage.StartsWith("Line ", StringComparison.Ordinal))
            {
                return $"SQL Syntax Error: {innerMessage}";
            }
        }

        return ex.Message;
    }

    private async Task<IReadOnlyList<string>> ExecuteStringListQueryAsync(string sql, CancellationToken ct)
    {
        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        var results = new List<string>();
        using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_connection?.Dispose();
    }

    #endregion
}
