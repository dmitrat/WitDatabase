using System.ComponentModel;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// Base class for all workspace tabs.
/// </summary>
public abstract class WorkspaceTabViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    protected WorkspaceTabViewModel(ApplicationViewModel applicationVm, IDatabaseSession? session)
        : base(applicationVm)
    {
        InitEvents();

        if (session != null)
            Bind(session);
    }

    #endregion

    #region Initialization

    private void InitEvents()
    {
        PropertyChanged += OnBasePropertyChanged;
    }

    #endregion

    #region Session

    /// <summary>
    /// The connection this tab belongs to, and the only one it will ever run anything in (WS-3).
    /// Null in exactly two cases: a query tab that existed before any database was opened, and one
    /// whose connection has since been closed.
    /// </summary>
    public IDatabaseSession? Session { get; private set; }

    /// <summary>
    /// True once this tab has had a connection. A tab that has never had one adopts the first
    /// connection opened - which is what makes "start Studio, type a query, open a database, run it"
    /// still work. A tab whose connection was CLOSED does not adopt the next one: it would run against
    /// a database the user never chose for it, and it would do so silently.
    /// </summary>
    private bool m_hasEverBound;

    public bool CanBind => Session == null && !m_hasEverBound;

    /// <summary>
    /// Ties the tab to a connection for good.
    /// </summary>
    public void Bind(IDatabaseSession session)
    {
        if (Session != null)
            return;

        Session = session;
        m_hasEverBound = true;

        session.StatusChanged += OnSessionStatusChangedInternal;

        ConnectionName = session.DisplayName;
        IsConnectionOpen = true;
        ConnectionColorIndex = session.ColorIndex;
        IsReadOnly = session.IsReadOnly;

        OnSessionChanged();
    }

    /// <summary>
    /// Called when the tab's connection has been closed. The tab is not destroyed here - a query tab
    /// keeps its text, which is usually the only copy of it.
    /// </summary>
    public void Unbind()
    {
        if (Session == null)
            return;

        Session.StatusChanged -= OnSessionStatusChangedInternal;
        Session = null;
        IsConnectionOpen = false;

        OnSessionChanged();
    }

    private void OnSessionStatusChangedInternal(object? sender, bool isConnected)
    {
        OnSessionStatusChanged(isConnected);
    }

    /// <summary>
    /// Called when this tab's own connection opens or closes. Never fires for another connection - the
    /// event lives on the session, not on the application (WS-13).
    /// </summary>
    protected virtual void OnSessionStatusChanged(bool isConnected)
    {
    }

    /// <summary>
    /// Called when the tab gains or loses its connection.
    /// </summary>
    protected virtual void OnSessionChanged()
    {
    }

    #endregion

    #region Abstract

    /// <summary>
    /// Gets the type of this tab.
    /// </summary>
    public abstract WorkspaceTabType TabType { get; }

    /// <summary>
    /// Gets the icon path data for this tab type.
    /// </summary>
    public abstract string IconPath { get; }

    #endregion

    #region Virtual

    /// <summary>
    /// Called when the tab is activated (selected).
    /// </summary>
    public virtual void OnActivated()
    {
    }

    /// <summary>
    /// Called when the tab is deactivated (another tab selected).
    /// </summary>
    public virtual void OnDeactivated()
    {
    }

    /// <summary>
    /// Whether the tab would close without anyone having to be asked. Used to decide whether a close
    /// needs a question at all - never as the answer to one.
    /// </summary>
    public virtual bool CanClose() => true;

    /// <summary>
    /// Called before the tab is closed, and allowed to ask. Returns false to cancel the close.
    ///
    /// Separate from <see cref="CanClose"/> because a question needs a round trip to the user, and a
    /// bool-returning method has nowhere to put one - which is how the TODO above the old
    /// unconditional 'return true' stayed a TODO while edits were being discarded silently.
    /// </summary>
    public virtual Task<bool> ConfirmCloseAsync() => Task.FromResult(CanClose());

    /// <summary>
    /// Called when the tab is being closed.
    /// </summary>
    public virtual void OnClosed()
    {
    }

    #endregion

    #region Functions

    protected void UpdateDisplayTitle()
    {
        DisplayTitle = IsModified ? $"{Title} •" : Title;
    }

    #endregion

    #region Event Handlers

    private void OnBasePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((WorkspaceTabViewModel vm) => vm.Title))
            UpdateDisplayTitle();

        if (e.IsProperty((WorkspaceTabViewModel vm) => vm.IsModified))
            UpdateDisplayTitle();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Tab title.
    /// </summary>
    [Notify]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Display title with modification indicator.
    /// </summary>
    [Notify]
    public string DisplayTitle { get; protected set; } = string.Empty;

    /// <summary>
    /// Indicates if the tab has unsaved changes.
    /// </summary>
    [Notify]
    public bool IsModified { get; set; }

    /// <summary>
    /// Indicates if this tab is pinned (cannot be closed, stays at the beginning).
    /// </summary>
    [Notify]
    public bool IsPinned { get; set; }

    /// <summary>
    /// Unique identifier for this tab instance.
    /// Used to prevent duplicate tabs for the same object.
    /// </summary>
    public virtual string? UniqueId => null;

    /// <summary>
    /// The name of the connection this tab belongs to, kept after the connection is gone so that the
    /// tab can still say where its text came from.
    /// </summary>
    [Notify]
    public string? ConnectionName { get; private set; }

    /// <summary>
    /// Whether that connection is still open.
    /// </summary>
    /// <remarks>
    /// The name outlives the session on purpose; what must not outlive it is the impression that the
    /// tab is connected. The toolbar chip read <i>Sales</i> for a connection that had been closed,
    /// while the status bar had already moved on to another. A bool rather than a caption, because a
    /// caption composed here would be frozen in the language it was composed in.
    /// </remarks>
    [Notify]
    public bool IsConnectionOpen { get; private set; }

    /// <summary>
    /// The connection's colour (WS-3): a 2px stripe down the left edge of the tab, once the frame is
    /// redesigned. Carried now because it belongs to the connection, not to the drawing.
    /// </summary>
    [Notify]
    public int ConnectionColorIndex { get; private set; }

    /// <summary>
    /// Whether the connection this tab writes through refuses writes (WS-10).
    /// </summary>
    /// <remarks>
    /// Set beside <see cref="ConnectionName"/> and <see cref="ConnectionColorIndex"/> rather than
    /// computed from <c>Session</c>, for the same reason they are: a tab keeps what it knew after
    /// its connection is gone, and a computed property over a null session would quietly answer
    /// "writable" for a tab whose database was read-only. It is also <c>[Notify]</c>, because a
    /// property bound in markup that tells nobody is the exact defect this application has found
    /// three times.
    /// </remarks>
    [Notify]
    public bool IsReadOnly { get; private set; }

    #endregion
}
