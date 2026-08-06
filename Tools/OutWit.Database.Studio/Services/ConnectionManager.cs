using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Implementation of <see cref="IConnectionManager"/>. One of these per application; as many
/// <see cref="DatabaseSession"/> instances as the user has databases open.
/// </summary>
public sealed class ConnectionManager : IConnectionManager
{
    #region Constants

    /// <summary>
    /// Six colours, repetition allowed (WS-3). The colour marks which connection a tab belongs to; it
    /// is not an identity, so a seventh connection reusing the first colour is fine.
    /// </summary>
    public const int COLOR_COUNT = 6;

    #endregion

    #region Events

    public event EventHandler<SessionEventArgs>? SessionOpened;

    public event EventHandler<SessionEventArgs>? SessionClosed;

    public event EventHandler<SessionEventArgs?>? ActiveChanged;

    #endregion

    #region Fields

    private readonly ILoggerFactory m_loggerFactory;
    private readonly ILogger<ConnectionManager> m_logger;

    private int m_nextColorIndex;
    private bool m_disposed;

    #endregion

    #region Constructors

    public ConnectionManager(ILoggerFactory loggerFactory, ILogger<ConnectionManager> logger)
    {
        m_loggerFactory = loggerFactory;
        m_logger = logger;

        Sessions = [];
    }

    #endregion

    #region Functions

    public async Task<IDatabaseSession?> OpenAsync(ConnectionInfo connection, CancellationToken ct = default)
    {
        var session = new DatabaseSession(connection, m_loggerFactory.CreateLogger<DatabaseSession>())
        {
            ColorIndex = m_nextColorIndex % COLOR_COUNT
        };

        session.DisplayName = UniqueDisplayName(session.DisplayName);

        if (!await session.OpenAsync(ct))
        {
            // Nothing is added and nothing is announced. A session in the list that never opened would
            // show up as a root in the tree that answers every question with "not connected".
            session.Dispose();
            return null;
        }

        m_nextColorIndex++;

        Sessions.Add(session);
        m_logger.LogInformation("Connection opened: {Name} ({FilePath}), {Count} open",
            session.DisplayName, session.Connection.FilePath, Sessions.Count);

        SessionOpened?.Invoke(this, new SessionEventArgs(session));

        Active = session;

        return session;
    }

    public async Task CloseAsync(IDatabaseSession session)
    {
        if (session is not DatabaseSession owned || !Sessions.Contains(session))
        {
            m_logger.LogWarning("Asked to close a session this manager does not own");
            return;
        }

        await owned.CloseAsync();

        var index = Sessions.IndexOf(session);
        Sessions.Remove(session);

        // Announced BEFORE disposing: the tabs that belong to it are still closing themselves, and a
        // disposed connection would turn their last act into an exception.
        SessionClosed?.Invoke(this, new SessionEventArgs(session));

        owned.Dispose();

        m_logger.LogInformation("Connection closed: {Name}, {Count} left", session.DisplayName, Sessions.Count);

        if (Active == session)
        {
            // The neighbour, not "nothing": closing the second of three connections should not leave
            // the tree with no selection.
            var next = index < Sessions.Count ? Sessions[index] : Sessions.LastOrDefault();
            Active = next;
        }
    }

    public async Task CloseAllAsync()
    {
        foreach (var session in Sessions.ToList())
            await CloseAsync(session);
    }

    public IDatabaseSession? Find(Guid id)
    {
        return Sessions.FirstOrDefault(session => session.Id == id);
    }

    /// <summary>
    /// Two databases called "sales" in two folders is the ordinary case. The second one becomes
    /// "sales (2)" - a name, not an identity: the id is what anything durable holds on to.
    /// </summary>
    private string UniqueDisplayName(string preferred)
    {
        if (Sessions.All(session => session.DisplayName != preferred))
            return preferred;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{preferred} ({suffix})";

            if (Sessions.All(session => session.DisplayName != candidate))
                return candidate;
        }
    }

    #endregion

    #region Properties

    public ObservableCollection<IDatabaseSession> Sessions { get; }

    public IDatabaseSession? Active
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            ActiveChanged?.Invoke(this, value == null ? null : new SessionEventArgs(value));
        }
    }

    public bool HasSessions => Sessions.Count > 0;

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        // Synchronous, and on purpose: this runs while the process is shutting down, and since 12.2.0
        // an undisposed connection leaves the database under an exclusive file lock.
        foreach (var session in Sessions.ToList())
        {
            if (session is DatabaseSession owned)
                owned.Dispose();
        }

        Sessions.Clear();
        Active = null;
    }

    #endregion
}
