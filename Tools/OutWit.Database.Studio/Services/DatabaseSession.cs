using Microsoft.Extensions.Logging;
using OutWit.Database.AdoNet;
using OutWit.Database.Core.Providers;
using OutWit.Database.Studio.Models;
using System.Data;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Implementation of <see cref="IDatabaseSession"/> using ADO.NET: one WitDbConnection, opened once,
/// closed once.
///
/// This was <c>DatabaseService</c>, a singleton whose one connection was replaced on every open. The
/// type is the same work; what changed is that it no longer stands for the application. Opening a
/// second database now makes a second one of these, and <see cref="ConnectionManager"/> keeps them.
/// </summary>
public sealed partial class DatabaseSession : IDatabaseSession, IDisposable
{
    #region Events

    public event EventHandler<bool>? StatusChanged;

    #endregion

    #region Fields

    private readonly ILogger<DatabaseSession> m_logger;
    private WitDbConnection? m_connection;
    private bool m_disposed;

    /// <summary>
    /// The last status actually delivered to subscribers, so that a close that closes nothing - or a
    /// second open - says nothing.
    ///
    /// It compares against the live state rather than against a value a caller captured: that is the
    /// shape of phase 13's S1, where ConnectAsync captured 'wasConnected' before an inner disconnect
    /// raised its own event, found true == true and stayed silent. One connection per session removes
    /// the nesting that made it possible; the comparison stays honest anyway.
    /// </summary>
    private bool m_lastRaisedStatus;

    #endregion

    #region Constructors

    public DatabaseSession(ConnectionInfo connection, ILogger<DatabaseSession> logger)
    {
        // A copy: the Create and Open dialogs keep editing their own ConnectionInfo after connecting,
        // and a session that shared it would silently change data source underneath its tabs.
        Connection = connection.Clone();

        m_logger = logger;

        // The name the user gave it in the dialog wins; the file name is the FALLBACK (WS-46). It was
        // the other way round until stage 9, which meant the Open dialog offered a name box whose value
        // reached nothing - the session, the tab stripe and the saved connection all showed the file
        // name. Caught by a case asking for the name back after a reopen.
        DisplayName = string.IsNullOrWhiteSpace(Connection.DisplayName)
            ? DefaultDisplayName(Connection.FilePath)
            : Connection.DisplayName;

        Catalog = new SchemaCatalog(this);
    }

    #endregion

    #region Connection Management

    /// <summary>
    /// Opens the connection this session was built for. Called by <see cref="ConnectionManager"/>,
    /// which is what adds the session to the application's list - a session opened behind its back
    /// belongs to nobody.
    /// </summary>
    public async Task<bool> OpenAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return true;

        try
        {
            var connectionString = Connection.BuildConnectionString();

            // ToLogString, never BuildConnectionString: the latter carries ";Password=..." and this
            // line reaches %AppData%\WitDatabase.Studio\logs\studio.log - the file users are asked to
            // attach to an issue.
            m_logger.LogInformation("Attempting to connect with connection string: {ConnectionString}",
                Connection.ToLogString());

            m_connection = new WitDbConnection(connectionString);

            await m_connection.OpenAsync(ct);

            // AFTER the open, and asked of the CONNECTION. This used to read the file a moment before
            // connecting, because an open paged database holds an exclusive lock and re-reading its
            // own header answers nothing - so the «База» tab's Configuration block was blank without
            // the trick. An open database describes itself now (the paged store keeps its header in
            // memory), which is the honest version of the same answer and the only one available when
            // something else already has the database open.
            StoredConfiguration = m_connection.StoredConfiguration
                                  ?? StorageDetector.ReadStoredConfiguration(Connection.FilePath ?? string.Empty);

            m_logger.LogInformation("Successfully connected to database: {FilePath}", Connection.FilePath);

            RaiseStatusChangedIfNeeded();
            return true;
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to connect to database: {FilePath}, ConnectionString: {ConnectionString}",
                Connection.FilePath, Connection.ToLogString());

            m_connection?.Dispose();
            m_connection = null;

            RaiseStatusChangedIfNeeded();
            return false;
        }
    }

    /// <summary>
    /// Closes the connection and releases the database's exclusive file lock. Also called by the
    /// manager, for the same reason.
    /// </summary>
    public async Task CloseAsync()
    {
        if (m_connection != null)
        {
            // Before the connection goes: an open transaction has to be decided by somebody, and the
            // only honest answer here is the one that changes nothing (WS-26).
            await DiscardTransactionAsync();

            await m_connection.CloseAsync();
            m_connection.Dispose();
            m_connection = null;

            m_logger.LogInformation("Disconnected from database: {FilePath}", Connection.FilePath);
        }

        RaiseStatusChangedIfNeeded();
    }

    public bool IsConnected => m_connection?.State == ConnectionState.Open;

    public bool IsReadOnly => Connection.IsReadOnly;

    private void RaiseStatusChangedIfNeeded()
    {
        var isConnected = IsConnected;
        if (isConnected == m_lastRaisedStatus)
            return;

        m_lastRaisedStatus = isConnected;
        StatusChanged?.Invoke(this, isConnected);
    }

    /// <summary>
    /// What to call this connection before anyone has looked at the others: the file's name, the
    /// folder's name for an LSM database, and something readable for in-memory.
    /// </summary>
    public static string DefaultDisplayName(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "database";

        if (filePath == ":memory:")
            return "in-memory";

        var name = Path.GetFileNameWithoutExtension(filePath.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        return string.IsNullOrWhiteSpace(name) ? filePath : name;
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

    #region Properties

    public Guid Id { get; } = Guid.NewGuid();

    public ConnectionInfo Connection { get; }

    /// <summary>
    /// What the database recorded when it was created, read just before this session opened it.
    /// </summary>
    /// <remarks>
    /// Null when there was nothing to read - a database being created, or a path that is not one.
    /// </remarks>
    public StoredConfiguration? StoredConfiguration { get; private set; }

    public ISchemaCatalog Catalog { get; }

    /// <summary>
    /// Set by <see cref="ConnectionManager"/> when the default collides with a session that is already
    /// open - two databases called "sales" in two folders are the ordinary case, not the odd one.
    /// </summary>
    public string DisplayName { get; set; }

    public int ColorIndex { get; set; }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        m_connection?.Dispose();
        m_connection = null;
    }

    #endregion
}
