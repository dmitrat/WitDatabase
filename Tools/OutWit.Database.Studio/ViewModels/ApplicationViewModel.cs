using OutWit.Database.Studio.Services;
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

    public ApplicationViewModel(
        IConnectionManager connections,
        ISettingsService settingsService,
        IExportService exportService,
        ILogger<ApplicationViewModel> logger,
        IConfirmationService? confirmations = null,
        IDialogService? dialogs = null,
        INotificationService? notifications = null)
    {
        Connections = connections;
        Settings = settingsService;
        Export = exportService;
        Logger = logger;
        Confirmations = confirmations ?? new KeepUnsavedChangesService();
        Dialogs = dialogs ?? new NoDialogService();
        Notifications = notifications ?? new NotificationService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationService>.Instance);

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

    #region Functions

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

    public IExportService Export { get; }

    /// <summary>
    /// Things that happened and did not need an answer (WS-7). Never null: a host that supplies none
    /// still gets a list, which keeps every call site free of a null check.
    /// </summary>
    public INotificationService Notifications { get; }

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
