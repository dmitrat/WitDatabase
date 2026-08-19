using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Services.Localization;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// Main application view model that contains all other view models.
/// Acts as a container and communication hub for all ViewModels.
/// Singleton pattern for easy access throughout the application.
/// </summary>
public sealed class ApplicationViewModel
{
    #region Singleton

    private static readonly Lock LOCK = new();

    public static ApplicationViewModel Instance
    {
        get
        {
            if (field != null) 
                return field;

            lock (LOCK)
            {
                field ??= Program.GetService<ApplicationViewModel>();
            }
            return field;
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// <paramref name="profiles"/> is REQUIRED, and it is the only service here that is.
    /// </summary>
    /// <remarks>
    /// It used to be optional like the rest, and where the others fall back to a null object it fell
    /// back to a real store over the user's own
    /// <c>%AppData%\WitDatabase.Studio\connections.json</c>. Every test that built this ViewModel
    /// without one wrote into the developer's saved connections - 2644 of them by the time anybody
    /// looked. The container has the store registered, so nothing in the application is worse off for
    /// having to say so.
    /// </remarks>
    public ApplicationViewModel(
        IConnectionManager connections,
        ISettingsService settingsService,
        IExportService exportService,
        IConnectionProfileStore profiles,
        ILogger<ApplicationViewModel> logger,
        IConfirmationService? confirmations = null,
        IDialogService? dialogs = null,
        INotificationService? notifications = null,
        IQueryHistoryService? history = null,
        ILocalizationService? localization = null)
    {
        Profiles = profiles;

        Connections = connections;
        Settings = settingsService;
        Export = exportService;
        Logger = logger;
        Localization = localization ?? new LocalizationService(settingsService.Current.Language);
        History = history ?? new NoQueryHistoryService();
        Confirmations = confirmations ?? new KeepUnsavedChangesService();
        Dialogs = dialogs ?? new NoDialogService();
        Notifications = notifications ?? new NotificationService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationService>.Instance);

        // The language is a setting, so it is changed by changing the setting - there is no second way
        // to do it and nothing to keep in step. This is the whole of "applied immediately" for WS-63.
        //
        // The value FORMAT is a different setting and is followed separately, which is WS-65 in the
        // wiring: choosing Russian does not make a decimal Russian, and neither does the machine's
        // locale. A value in the grid is something a person pastes into a statement.
        ApplyValueFormat();

        Settings.Changed += (_, e) =>
        {
            if (e.PropertyName == nameof(Models.Settings.Language))
            {
                Localization.SetLanguage(Settings.Current.Language);

                // The status bar is written into from 57 places, each of them formatting a
                // sentence at the moment of its event, and nothing re-reads them - so a message
                // stayed in the language it was written in. Rather than teach every call site to
                // keep its key and its arguments, the bar returns to its idle sentence: a stale
                // message in the language somebody has just left is worse than none.
                MainWindowVm.StatusText = Localization["Status.Ready"];
            }

            if (e.PropertyName is nameof(Models.Settings.DateTimeFormat)
                or nameof(Models.Settings.NumberFormat)
                or nameof(Models.Settings.BinaryDisplay))
                ApplyValueFormat();
        };

        InitViewModels();
    }

    #endregion

    #region Initialization

    private void InitViewModels()
    {
        MainWindowVm = new MainWindowViewModel(this);
        ConnectionVm = new ConnectionViewModel(this);
        DatabaseExplorerVm = new DatabaseExplorerViewModel(this);

        WorkspaceTabsVm = new WorkspaceTabsViewModel(this);

        // Both read the other view models, so they come last.
        PaletteVm = new CommandPaletteViewModel(this);
        InspectorVm = new ObjectInspectorViewModel(this);
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the application has been asked to close itself from inside the ViewModel layer -
    /// File &gt; Exit, and nothing else.
    ///
    /// It exists so that there is exactly ONE way out of Studio. Exit used to call
    /// <c>Environment.Exit(0)</c>, which ends the process where it stands: MainWindow.OnClosing never
    /// runs, so the window size is not saved and nothing is asked about unapplied edits, and the
    /// service provider is never disposed - which matters since 12.2.0, because the database is held
    /// under an exclusive file lock that is released by closing the connection.
    /// </summary>
    public event EventHandler? ShutdownRequested;

    #endregion

    #region Opening a database

    /// <summary>
    /// Opens a database and leaves behind everything the application keeps beside an open session:
    /// the branch in the tree, the entry in Recent Databases and the saved connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the only way to open one</b>, and <c>EveryRouteThatOpensADatabaseTests</c> holds it
    /// to that. Before it existed, each route did its own version: the Open dialog did all three, the
    /// recent list did two, the copy dialog remembered the tree after an August defect and nothing
    /// else, and the Connections window did none - so connecting from it left the tree empty until
    /// Refresh was pressed, and the database never reached the recent list.
    /// </para>
    /// <para>
    /// <b>Why not have the explorer listen to <c>SessionOpened</c>.</b> An event handler is
    /// <c>async void</c>: it has nowhere to put a failure, and it would build a branch that the caller
    /// has already awaited. The explorer takes the CLOSING half for exactly that reason - closing has
    /// nothing to await and no reader to disappoint. Opening keeps its caller.
    /// </para>
    /// </remarks>
    public async Task<IDatabaseSession?> OpenDatabaseAsync(ConnectionInfo connection,
        CancellationToken ct = default)
    {
        var session = await Connections.OpenAsync(connection, ct);

        if (session == null)
            return null;

        await RememberAsync(session);

        // This connection's branch only: the others are already there, and rebuilding them would throw
        // away counts and expanded state nobody asked to lose.
        await DatabaseExplorerVm.RefreshAsync(session);

        return session;
    }

    /// <summary>
    /// The recent list and the saved connection, for a database that has a path.
    /// </summary>
    /// <remarks>
    /// <b>A name a person chose is not overwritten by one the application derived.</b> The session's
    /// DisplayName falls back to the file name when the dialog's name box was empty, and saving that
    /// over a stored profile is how <i>Sales</i> became <i>sales</i> - one database under two names in
    /// two screenshots. The colour goes with it, for the same reason.
    /// </remarks>
    private async Task RememberAsync(IDatabaseSession session)
    {
        var path = session.Connection.FilePath;

        if (string.IsNullOrWhiteSpace(path) || path == ":memory:")
            return;

        await Settings.AddRecentFileAsync(path);

        // The welcome screen reads its own list, and it is the one place that knows the entry is new.
        await MainWindowVm.ReloadRecentFilesAsync();

        var profile = ConnectionProfile.From(session.Connection);

        profile.ColorIndex = session.ColorIndex;
        profile.Name = session.DisplayName;

        var saved = (await Profiles.LoadAsync())
            .FirstOrDefault(other => string.Equals(other.Path, path, StringComparison.OrdinalIgnoreCase));

        if (saved != null && string.IsNullOrWhiteSpace(session.Connection.DisplayName))
        {
            profile.Name = saved.Name;
            profile.ColorIndex = saved.ColorIndex;
        }

        await Profiles.SaveAsync(profile);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Hands the three format settings to the one place a converter can read them from. Everything
    /// else takes the format as an argument; an Avalonia value converter has nowhere to be handed one.
    /// </summary>
    private void ApplyValueFormat()
    {
        Converters.ValueFormat.Current = new Converters.ValueFormat(
            Settings.Current.DateTimeFormat,
            Settings.Current.NumberFormat,
            Settings.Current.BinaryDisplay);
    }

    public ApplicationViewModel ResetOwnerWindow(Avalonia.Controls.Window? window)
    {
        MainWindow = window;
        return this;
    }

    /// <summary>
    /// Asks every tab that holds unapplied work what to do, and requests shutdown only if all of them
    /// agree. Returns false when the user chose to stay.
    /// </summary>
    public async Task<bool> RequestShutdownAsync()
    {
        if (!await WorkspaceTabsVm.ConfirmCloseAllAsync())
        {
            Logger.LogInformation("Shutdown cancelled: a tab has unapplied changes");
            return false;
        }

        ShutdownRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Closes every database Studio holds, synchronously, in the order that flushes them.
    ///
    /// <para>
    /// <b>This is issue 10's fix and it is deliberately not left to the container.</b> Disposing a
    /// connection is what writes the file header - <c>PageManager.Dispose</c> flushes - and the
    /// container's disposal was measured never to reach the connection manager at all. A database
    /// that is not flushed keeps a header older than its own pages, which loses everything since the
    /// last flush and, once the page cache has evicted anything, cannot be opened again.
    /// </para>
    /// <para>
    /// The query history is a database too, and it goes the same way for the same reason.
    /// </para>
    /// </summary>
    public void CloseDatabases()
    {
        try
        {
            Connections.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Closing the open databases failed");
        }

        try
        {
            // On a pool thread rather than inline: blocking the UI thread on a task that may want it
            // back is a deadlock, and this runs while the window is still closing.
            Task.Run(async () => await History.DisposeAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Closing the query history failed");
        }

        Logger.LogInformation("Databases closed");
    }

    #endregion

    #region View Models

    public MainWindowViewModel MainWindowVm { get; private set; } = null!;
    public ConnectionViewModel ConnectionVm { get; private set; } = null!;
    public DatabaseExplorerViewModel DatabaseExplorerVm { get; private set; } = null!;
    
    /// <summary>
    /// The tabs of the workspace - query, data, structure.
    /// </summary>
    public WorkspaceTabsViewModel WorkspaceTabsVm { get; private set; } = null!;

    /// <summary>
    /// Ctrl+K: objects and commands in one list (WS-9).
    /// </summary>
    public CommandPaletteViewModel PaletteVm { get; private set; } = null!;

    /// <summary>
    /// The right panel: what the selected object is (WS-18).
    /// </summary>
    public ObjectInspectorViewModel InspectorVm { get; private set; } = null!;

    #endregion

    #region Properties

    /// <summary>
    /// Every open connection. There used to be a single <c>Database</c> service here, which is why a
    /// tab could only run against whatever Studio was connected to last.
    /// </summary>
    public IConnectionManager Connections { get; }

    /// <summary>
    /// The connection the user is looking at: where a new tab is opened, where the object dialogs
    /// create their objects, where export and import read and write. An open tab does NOT use this -
    /// it runs in the session it belongs to (WS-3).
    /// </summary>
    public IDatabaseSession? ActiveSession => Connections.Active;

    public ISettingsService Settings { get; }

    /// <summary>
    /// The interface language (WS-63). Never null - a host that supplies none still gets English, so no
    /// call site needs a null check to ask for a string.
    /// </summary>
    public ILocalizationService Localization { get; }

    /// <summary>
    /// The saved connections (WS-68) - names, colours and read-only flags that survive a session, and
    /// never a password.
    /// </summary>
    public IConnectionProfileStore Profiles { get; }

    public IExportService Export { get; }

    /// <summary>
    /// Things that happened and did not need an answer (WS-7). Never null: a host that supplies none
    /// still gets a list, which keeps every call site free of a null check.
    /// </summary>
    public INotificationService Notifications { get; }

    /// <summary>
    /// What has been run, kept between sessions (WS-29). Never null, and never load-bearing: a host
    /// that supplies none - or a store that would not open - leaves every query working.
    /// </summary>
    public IQueryHistoryService History { get; }

    public ILogger<ApplicationViewModel> Logger { get; }

    /// <summary>
    /// Asks the user about work that would otherwise be lost. Never null: without a host-supplied
    /// implementation this keeps the work and refuses the close.
    /// </summary>
    public IConfirmationService Confirmations { get; set; }

    /// <summary>
    /// Pickers and dialogs. Never null: without a host-supplied implementation nothing is shown and
    /// every answer is "nothing was chosen", which is what a headless run should see.
    ///
    /// This and <see cref="Confirmations"/> stay separate on purpose. One shows a window, the other
    /// asks a person a question; a test that scripts an answer about unapplied work should not have
    /// to stub nine picker methods to do it.
    /// </summary>
    public IDialogService Dialogs { get; set; }
    
    public Avalonia.Controls.Window? MainWindow { get; private set; }

    #endregion
}
