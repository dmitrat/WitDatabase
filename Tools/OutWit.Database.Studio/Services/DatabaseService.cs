using Microsoft.Extensions.Logging;
using OutWit.Database.AdoNet;
using OutWit.Database.Studio.Models;
using System.Data;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Implementation of <see cref="IDatabaseService"/> using ADO.NET.
/// </summary>
public sealed partial class DatabaseService : IDatabaseService
{
    #region Events

    public event EventHandler<bool>? ConnectionStatusChanged;

    #endregion

    #region Fields

    private readonly ILogger<DatabaseService> m_logger;
    private WitDbConnection? m_connection;
    private ConnectionInfo? m_currentConnection;
    private bool m_disposed;

    /// <summary>
    /// The last status actually delivered to subscribers. Compared against the live state rather
    /// than against a value captured by the caller: ConnectAsync calls DisconnectAsync, which raises
    /// its own event, so anything the caller captured beforehand is stale by the time it is used.
    /// </summary>
    private bool m_lastRaisedStatus;

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
        try
        {
            await DisconnectAsync();

            var connectionString = connection.BuildConnectionString();

            // ToLogString, never BuildConnectionString: the latter carries ";Password=..." and this
            // line reaches %AppData%\WitDatabase.Studio\logs\studio.log - the file users are asked to
            // attach to an issue.
            m_logger.LogInformation("Attempting to connect with connection string: {ConnectionString}",
                connection.ToLogString());

            m_connection = new WitDbConnection(connectionString);

            await m_connection.OpenAsync(ct);
            m_currentConnection = connection;

            m_logger.LogInformation("Successfully connected to database: {FilePath}", connection.FilePath);

            RaiseConnectionStatusChangedIfNeeded();
            return true;
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to connect to database: {FilePath}, ConnectionString: {ConnectionString}",
                connection.FilePath, connection.ToLogString());
            m_connection?.Dispose();
            m_connection = null;
            m_currentConnection = null;

            RaiseConnectionStatusChangedIfNeeded();
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (m_connection != null)
        {
            await m_connection.CloseAsync();
            m_connection.Dispose();
            m_connection = null;
            m_currentConnection = null;
            m_logger.LogInformation("Disconnected from database");
        }

        RaiseConnectionStatusChangedIfNeeded();
    }

    public bool IsConnected => m_connection?.State == ConnectionState.Open;

    public ConnectionInfo? CurrentConnection => m_currentConnection;

    /// <summary>
    /// Raises the status event when the live state differs from what subscribers were last told.
    ///
    /// It takes no 'wasConnected' argument on purpose. ConnectAsync begins by calling DisconnectAsync,
    /// which raises 'disconnected' on its own; a caller comparing against a value captured before that
    /// call would find connected == connected and stay silent, leaving every view believing the
    /// application is disconnected while the service is connected.
    /// </summary>
    private void RaiseConnectionStatusChangedIfNeeded()
    {
        var isConnected = IsConnected;
        if (isConnected == m_lastRaisedStatus)
            return;

        m_lastRaisedStatus = isConnected;
        ConnectionStatusChanged?.Invoke(this, isConnected);
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
