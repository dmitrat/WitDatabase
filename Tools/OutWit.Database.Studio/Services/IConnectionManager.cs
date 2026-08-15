using System.Collections.ObjectModel;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The open connections of the application, and the only place allowed to open or close one.
///
/// Studio used to hold a single <c>DatabaseService</c> with a single connection and a GLOBAL
/// ConnectionStatusChanged event. Four ViewModels listened to it, so disconnecting one database closed
/// every tab of every kind (WS-13), and a tab could only ever run against whatever the application was
/// connected to last (WS-3). This holds a collection instead; the status event moved onto
/// <see cref="IDatabaseSession"/>.
/// </summary>
public interface IConnectionManager : IDisposable
{
    #region Events

    /// <summary>
    /// Raised after a session has been opened and added to <see cref="Sessions"/>.
    /// </summary>
    event EventHandler<SessionEventArgs>? SessionOpened;

    /// <summary>
    /// Raised after a session has been closed and removed from <see cref="Sessions"/>. Its argument is
    /// the session that went away - what tells a tab whether the news is about IT (WS-13).
    /// </summary>
    event EventHandler<SessionEventArgs>? SessionClosed;

    /// <summary>
    /// Raised when <see cref="Active"/> changes. The active session is where the tree, the object
    /// dialogs and export/import work; it is NOT where an open tab runs its query (WS-3).
    /// </summary>
    event EventHandler<SessionEventArgs?>? ActiveChanged;

    #endregion

    #region Sessions

    /// <summary>
    /// Every open connection, in the order they were opened.
    /// </summary>
    ObservableCollection<IDatabaseSession> Sessions { get; }

    /// <summary>
    /// Why the last <c>OpenAsync</c> returned null, or null when the last one succeeded.
    /// </summary>
    /// <remarks>
    /// Opening answers a session or nothing, and one refusal is not another: a database in the
    /// encryption format that preceded the crypto preamble can be offered a way forward, and a file
    /// that is not a database cannot. Without this every refusal reached the dialog as the same
    /// sentence.
    /// </remarks>
    Exception? LastOpenError { get; }

    /// <summary>
    /// The connection the user is looking at - the one a new tab and the object dialogs belong to.
    /// Null when nothing is open.
    /// </summary>
    IDatabaseSession? Active { get; set; }

    /// <summary>
    /// Whether anything at all is open. The old IsConnected, and worth spelling differently: with more
    /// than one connection, "connected" is a question about a session, not about Studio.
    /// </summary>
    bool HasSessions { get; }

    #endregion

    #region Functions

    /// <summary>
    /// Opens a new connection and makes it active. Returns null if the connection failed - a failed
    /// attempt leaves no session behind, so the tree and the tabs never see one they cannot use.
    /// </summary>
    Task<IDatabaseSession?> OpenAsync(ConnectionInfo connection, CancellationToken ct = default);

    /// <summary>
    /// Closes one session. Everything that belongs to it goes; nothing that belongs to another does.
    /// </summary>
    Task CloseAsync(IDatabaseSession session);

    /// <summary>
    /// Closes every session - shutting down, and nothing else.
    /// </summary>
    Task CloseAllAsync();

    /// <summary>
    /// Finds an open session by id, or null. Tree nodes hold ids rather than references.
    /// </summary>
    IDatabaseSession? Find(Guid id);

    #endregion
}

/// <summary>
/// Which session the news is about.
/// </summary>
public sealed class SessionEventArgs(IDatabaseSession session) : EventArgs
{
    public IDatabaseSession Session { get; } = session;
}
