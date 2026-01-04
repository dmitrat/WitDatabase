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

        // Not implemented in INFORMATION_SCHEMA yet
        return Array.Empty<string>();
    }

    public async Task<IReadOnlyList<string>> GetSequencesAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        // Not implemented in INFORMATION_SCHEMA yet
        return Array.Empty<string>();
    }

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string tableName, CancellationToken ct = default)
    {
        // Prefer INFORMATION_SCHEMA.COLUMNS (see WitSqlEngineInformationSchemaTests)
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
                // INFORMATION_SCHEMA.COLUMNS in engine currently provides generated columns metadata,
                // but UI ColumnInfo only needs PK which is resolved via separate query if needed.
                IsPrimaryKey = false
            });
        }

        await TryMarkPrimaryKeysAsync(tableName, columns, ct);

        return columns;
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

    // Legacy helper used by older UI code paths; keep but route to INFORMATION_SCHEMA version.
    public Task<IReadOnlyList<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken ct = default) =>
        GetColumnsAsync(tableName, ct);

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

            var table = new DataTable();
            table.Load(reader);

            result.ResultTable = table;
            result.RowsAffected = table.Rows.Count;
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;

            m_logger.LogInformation("Query executed successfully in {Time}ms, {Rows} rows",
                result.ExecutionTimeMs, result.RowsAffected);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
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
