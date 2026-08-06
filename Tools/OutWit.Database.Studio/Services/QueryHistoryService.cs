using Microsoft.Extensions.Logging;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// One remembered query.
/// </summary>
public sealed record QueryHistoryEntry(
    long Id,
    string Text,
    string? Connection,
    DateTime ExecutedAt,
    double DurationMs,
    int Rows,
    string Status,
    int Uses);

/// <summary>
/// The query history (WS-29). A closed tab with an unsaved query stops being lost work.
/// </summary>
public interface IQueryHistoryService : IAsyncDisposable
{
    /// <summary>
    /// Whether the history store opened. False is a normal state, not a failure of Studio: the panel
    /// says so and everything else goes on working.
    /// </summary>
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Remembers a query that has just run. Repeating the same text raises the existing entry and
    /// counts it rather than adding a second one.
    /// </summary>
    Task RecordAsync(string text, string? connection, double durationMs, int rows, string status,
        CancellationToken ct = default);

    /// <summary>
    /// The most recent entries, newest first, optionally narrowed to those containing a piece of text.
    /// </summary>
    Task<IReadOnlyList<QueryHistoryEntry>> SearchAsync(string? term = null, int limit = 200,
        CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// No history at all: what a host that supplies none gets, so that no call site needs a null check.
/// It reports itself unavailable, which is exactly what the panel then says.
/// </summary>
public sealed class NoQueryHistoryService : IQueryHistoryService
{
    public bool IsAvailable => false;

    public string? UnavailableReason => "the query history was not started";

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task RecordAsync(string text, string? connection, double durationMs, int rows, string status,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<QueryHistoryEntry>> SearchAsync(string? term = null, int limit = 200,
        CancellationToken ct = default) => Task.FromResult<IReadOnlyList<QueryHistoryEntry>>([]);

    public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// The history, kept in a WitDatabase of Studio's own.
///
/// This is the one place in the product where Studio is an ordinary consumer of the engine it ships
/// with - a local store with an index on time and a text search over the query. If keeping a history
/// in WitDatabase turns out to be awkward, that is a signal worth hearing before a user sends it.
///
/// The other side of that is honest and is why <see cref="IsAvailable"/> exists: a defect in the
/// engine would break the history too, and the history must never be able to stop somebody running a
/// query. Every method here fails quietly and says so through that flag.
///
/// <b>What is never written:</b> the connection string, and the values of any parameters. What goes in
/// is the SQL text as typed, the connection's display NAME, when, how long, how many rows and how it
/// ended. A password reached the log file once already (stage 0, B1); the same mistake in a store that
/// survives restarts would be worse.
/// </summary>
public sealed class QueryHistoryService : IQueryHistoryService
{
    #region Constants

    /// <summary>
    /// Reserved words cannot be column names on this engine unless they are quoted - measured: a
    /// column called <c>Text</c> or <c>Rows</c> is refused outright. Names here avoid the question.
    /// </summary>
    private const string CREATE_TABLE = """
        CREATE TABLE QueryHistory (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            QueryText VARCHAR(8000) NOT NULL,
            ConnectionName VARCHAR(200),
            ExecutedAt DATETIME NOT NULL,
            DurationMs DECIMAL(18,3) NOT NULL,
            RowsReturned INTEGER NOT NULL,
            Status VARCHAR(32) NOT NULL,
            Uses INTEGER NOT NULL)
        """;

    private const string CREATE_INDEX =
        "CREATE INDEX IX_QueryHistory_ExecutedAt ON QueryHistory (ExecutedAt)";

    /// <summary>
    /// The design's bounds: thirty days or five thousand entries, whichever is reached first.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    public const int MAX_ENTRIES = 5000;

    #endregion

    #region Fields

    private readonly string m_path;
    private readonly ILogger<QueryHistoryService> m_logger;
    private WitDbConnection? m_connection;
    private readonly SemaphoreSlim m_gate = new(1, 1);

    #endregion

    #region Constructors

    /// <summary>
    /// The path is a parameter for the same reason <c>SettingsService</c>'s is: a test with the real
    /// service must not be able to write into the developer's own history.
    /// </summary>
    public QueryHistoryService(string path, ILogger<QueryHistoryService> logger)
    {
        m_path = path;
        m_logger = logger;
    }

    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WitDatabase.Studio", "history.witdb");
    }

    #endregion

    #region Properties

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    #endregion

    #region Functions

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var folder = Path.GetDirectoryName(m_path);

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            m_connection = new WitDbConnection($"Data Source={m_path}");
            await m_connection.OpenAsync(ct);

            if (!await HasTableAsync(ct))
            {
                await ExecuteAsync(CREATE_TABLE, ct);
                await ExecuteAsync(CREATE_INDEX, ct);
            }

            IsAvailable = true;
            UnavailableReason = null;

            m_logger.LogInformation("Query history opened at {Path}", m_path);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;

            m_connection?.Dispose();
            m_connection = null;

            m_logger.LogWarning(ex, "Query history is unavailable; queries will run as usual");
        }
    }

    public async Task RecordAsync(string text, string? connection, double durationMs, int rows, string status,
        CancellationToken ct = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(text))
            return;

        await m_gate.WaitAsync(ct);

        try
        {
            var trimmed = text.Trim();
            var now = DateTime.UtcNow;

            var existing = await ScalarAsync(
                "SELECT Id FROM QueryHistory WHERE QueryText = @text", ct, ("@text", trimmed));

            if (existing != null)
            {
                // The same query run again is the same entry, raised. A history that grows a row per
                // press of F5 is a log, and nobody searches a log for their own query.
                await ExecuteAsync(
                    "UPDATE QueryHistory SET Uses = Uses + 1, ExecutedAt = @at, DurationMs = @duration, " +
                    "RowsReturned = @rows, Status = @status WHERE Id = @id", ct,
                    ("@at", now), ("@duration", durationMs), ("@rows", rows), ("@status", status),
                    ("@id", Convert.ToInt64(existing)));
            }
            else
            {
                await ExecuteAsync(
                    "INSERT INTO QueryHistory (QueryText, ConnectionName, ExecutedAt, DurationMs, " +
                    "RowsReturned, Status, Uses) VALUES (@text, @connection, @at, @duration, @rows, @status, 1)",
                    ct,
                    ("@text", trimmed), ("@connection", (object?)connection ?? DBNull.Value), ("@at", now),
                    ("@duration", durationMs), ("@rows", rows), ("@status", status));
            }

            await TrimAsync(now, ct);
        }
        catch (Exception ex)
        {
            m_logger.LogWarning(ex, "A query could not be written to the history");
        }
        finally
        {
            m_gate.Release();
        }
    }

    public async Task<IReadOnlyList<QueryHistoryEntry>> SearchAsync(string? term = null, int limit = 200,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
            return [];

        await m_gate.WaitAsync(ct);

        try
        {
            var entries = new List<QueryHistoryEntry>();

            using var command = m_connection!.CreateCommand();

            command.CommandText = string.IsNullOrWhiteSpace(term)
                ? "SELECT Id, QueryText, ConnectionName, ExecutedAt, DurationMs, RowsReturned, Status, Uses " +
                  $"FROM QueryHistory ORDER BY ExecutedAt DESC LIMIT {limit}"
                : "SELECT Id, QueryText, ConnectionName, ExecutedAt, DurationMs, RowsReturned, Status, Uses " +
                  $"FROM QueryHistory WHERE QueryText LIKE @term ORDER BY ExecutedAt DESC LIMIT {limit}";

            if (!string.IsNullOrWhiteSpace(term))
                command.Parameters.Add(new WitDbParameter("@term", $"%{term.Trim()}%"));

            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                entries.Add(new QueryHistoryEntry(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetDateTime(3),
                    Convert.ToDouble(reader.GetValue(4)),
                    Convert.ToInt32(reader.GetValue(5)),
                    reader.GetString(6),
                    Convert.ToInt32(reader.GetValue(7))));
            }

            return entries;
        }
        catch (Exception ex)
        {
            m_logger.LogWarning(ex, "The history could not be read");
            return [];
        }
        finally
        {
            m_gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
            return;

        await m_gate.WaitAsync(ct);

        try
        {
            await ExecuteAsync("DELETE FROM QueryHistory", ct);
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Thirty days, then five thousand entries. The count is taken by reading the ids rather than
    /// through COUNT(*): on this engine a count is separate state and can disagree with the rows.
    /// </summary>
    private async Task TrimAsync(DateTime now, CancellationToken ct)
    {
        await ExecuteAsync("DELETE FROM QueryHistory WHERE ExecutedAt < @cutoff", ct,
            ("@cutoff", now - MaxAge));

        var kept = new List<DateTime>();

        using (var command = m_connection!.CreateCommand())
        {
            command.CommandText = $"SELECT ExecutedAt FROM QueryHistory ORDER BY ExecutedAt DESC LIMIT {MAX_ENTRIES}";

            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
                kept.Add(reader.GetDateTime(0));
        }

        if (kept.Count < MAX_ENTRIES)
            return;

        await ExecuteAsync("DELETE FROM QueryHistory WHERE ExecutedAt < @oldest", ct, ("@oldest", kept[^1]));
    }

    private async Task<bool> HasTableAsync(CancellationToken ct)
    {
        try
        {
            using var command = m_connection!.CreateCommand();
            command.CommandText = "SELECT Id FROM QueryHistory LIMIT 1";

            using var reader = await command.ExecuteReaderAsync(ct);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
            command.Parameters.Add(new WitDbParameter(name, value ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<object?> ScalarAsync(string sql, CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
            command.Parameters.Add(new WitDbParameter(name, value ?? DBNull.Value));

        var value1 = await command.ExecuteScalarAsync(ct);

        return value1 is DBNull ? null : value1;
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (m_connection == null)
            return;

        try
        {
            await m_connection.CloseAsync();
        }
        catch (Exception)
        {
            // shutting down
        }

        m_connection.Dispose();
        m_connection = null;
        IsAvailable = false;
    }

    #endregion
}
