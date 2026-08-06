using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Views.Dialogs;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public MainWindowViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefault();
        InitEvents();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Title = "WitDatabase Studio";
        StatusText = "Ready";
        CurrentConnection = null;
        IsConnected = Connections.Active?.IsConnected == true;
        RecentFiles = new ObservableCollection<RecentFileItem>();
    }

    private void InitEvents()
    {
        ApplicationVm.Notifications.Changed += OnNotificationsChanged;

        // Three events about the collection instead of one about the application. IsConnected here
        // means "there is a connection to act on", which is a question about the active session now.
        Connections.SessionOpened += OnSessionOpened;
        Connections.SessionClosed += OnSessionClosed;
        Connections.ActiveChanged += OnActiveSessionChanged;
    }

    private void InitCommands()
    {
        NewDatabaseCommand = new RelayCommand(NewDatabaseAsync);
        OpenDatabaseCommand = new RelayCommand(OpenDatabaseAsync);
        CloseDatabaseCommand = new RelayCommand(CloseDatabaseAsync, CanCloseDatabase);
        RefreshCommand = new RelayCommand(RefreshAsync, () => IsConnected);
        ExportCommand = new RelayCommandAsync(ExportAsync, () => IsConnected);
        ImportCommand = new RelayCommandAsync(ImportAsync, () => IsConnected);
        OpenRecentCommand = new RelayCommandAsync<string>(OpenRecentAsync);
        ClearRecentFilesCommand = new RelayCommandAsync(ClearRecentFilesAsync);
        SettingsCommand = new RelayCommandAsync(ShowSettingsAsync);
        AboutCommand = new RelayCommandAsync(ShowAboutAsync);
        ExitCommand = new RelayCommandAsync(ExitAsync);
        ShowNotificationsCommand = new RelayCommand(ShowNotifications);
        HideNotificationsCommand = new RelayCommand(() => AreNotificationsVisible = false);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Initializes recent files from settings.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var settings = await Settings.LoadAsync();
            LoadRecentFiles(settings);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize settings");
        }
    }

    private void LoadRecentFiles(Models.Settings settings)
    {
        RecentFiles.Clear();

        foreach (var file in settings.RecentFiles)
        {
            if (File.Exists(file))
            {
                RecentFiles.Add(new RecentFileItem
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    Directory = Path.GetDirectoryName(file) ?? string.Empty
                });
            }
        }

        HasRecentFiles = RecentFiles.Count > 0;
    }

    /// <summary>
    /// Saves current window state to settings.
    /// </summary>
    public async Task SaveWindowStateAsync(double width, double height, string state)
    {
        try
        {
            var settings = await Settings.LoadAsync();
            settings.WindowWidth = width;
            settings.WindowHeight = height;
            settings.WindowState = state;
            await Settings.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save window state");
        }
    }

    #endregion

    #region Command Functions

    private async void NewDatabaseAsync()
    {
        Logger.LogInformation("NewDatabase command invoked");

        var result = await ApplicationVm.ConnectionVm.ShowCreateDialogAsync();
        Logger.LogInformation("ShowCreateDialogAsync returned: {Result}", result);

        if (!result || ApplicationVm.ConnectionVm.OpenedSession == null)
        {
            Logger.LogInformation("Dialog cancelled or no connection selected");
            return;
        }

        await LoadSchemaAfterConnectionAsync(ApplicationVm.ConnectionVm.OpenedSession);
    }

    private async void OpenDatabaseAsync()
    {
        var result = await ApplicationVm.ConnectionVm.ShowOpenDialogAsync();

        if (!result || ApplicationVm.ConnectionVm.OpenedSession == null)
            return;

        // Not a replacement: an open database stays open, with its tabs and its branch of the tree.
        await LoadSchemaAfterConnectionAsync(ApplicationVm.ConnectionVm.OpenedSession);
    }

    /// <summary>
    /// Closes ONE connection - the active one - and only its tabs (WS-13). This used to close the
    /// application's single connection, which took every tab of every database with it.
    /// </summary>
    private async void CloseDatabaseAsync()
    {
        var session = Connections.Active;

        if (session == null || !CanCloseDatabase())
            return;

        // Ask while there is still a connection to apply the edits to. Afterwards the only honest
        // offer left would be to discard them. Only the tabs of THIS connection are asked - the
        // others are not being closed.
        if (!await ApplicationVm.WorkspaceTabsVm.ConfirmCloseSessionAsync(session))
        {
            Logger.LogInformation("Disconnect cancelled: a tab has unapplied changes");
            return;
        }

        IsLoading = true;
        StatusText = $"Disconnecting from {session.DisplayName}...";

        try
        {
            await Connections.CloseAsync(session);

            CurrentConnection = Connections.Active?.Connection;
            StatusText = Connections.HasSessions
                ? $"Disconnected from {session.DisplayName}"
                : "Disconnected";

            Logger.LogInformation("Disconnected from {Name}, {Count} connections left",
                session.DisplayName, Connections.Sessions.Count);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Logger.LogError(ex, "Error disconnecting from database");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void RefreshAsync()
    {
        if (!IsConnected)
            return;

        await ApplicationVm.DatabaseExplorerVm.RefreshAsync();
    }

    private bool CanCloseDatabase()
    {
        return IsConnected && !IsLoading;
    }

    /// <summary>
    /// Whether a second, third, nth database can be opened. Always, now.
    /// </summary>
    public bool CanOpenDatabase => !IsLoading;

    private async Task ExportAsync()
    {
        if (!IsConnected)
            return;

        var exportVm = new ExportViewModel(ApplicationVm);
        await exportVm.InitializeAsync();

        await ApplicationVm.Dialogs.ShowExportAsync(exportVm);
    }

    private async Task ImportAsync()
    {
        if (!IsConnected)
            return;

        var importVm = new ImportViewModel(ApplicationVm);
        await importVm.InitializeAsync();

        await ApplicationVm.Dialogs.ShowImportAsync(importVm);
    }

    private async Task OpenRecentAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        if (!File.Exists(filePath))
        {
            await Settings.RemoveRecentFileAsync(filePath);
            var settings = await Settings.LoadAsync();
            LoadRecentFiles(settings);
            
            StatusText = $"File not found: {Path.GetFileName(filePath)}";
            return;
        }

        // Nothing is closed first. Opening a recent file used to call CloseDatabaseAsync - an
        // 'async void' method - without awaiting it, and then connect over the top of the close.
        var connection = new ConnectionInfo { FilePath = filePath };

        IsLoading = true;
        StatusText = $"Connecting to {Path.GetFileName(filePath)}...";

        try
        {
            var session = await Connections.OpenAsync(connection);

            if (session == null)
            {
                StatusText = $"Failed to open {Path.GetFileName(filePath)}";
                return;
            }

            await LoadSchemaAfterConnectionAsync(session);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Logger.LogError(ex, "Failed to open recent file: {FilePath}", filePath);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ClearRecentFilesAsync()
    {
        await Settings.ClearRecentFilesAsync();
        RecentFiles.Clear();
        HasRecentFiles = false;
    }

    private Task ShowSettingsAsync()
    {
        return ShowSettingsAsync(SettingsViewModel.SECTION_GENERAL);
    }

    /// <summary>
    /// About is a SECTION of the settings, not a window of its own (WS-53). A window whose only job is
    /// to state four version numbers is a window; the numbers belong next to the log folder and the
    /// file format version, which is what the person asking for them is actually collecting.
    /// </summary>
    private Task ShowAboutAsync()
    {
        return ShowSettingsAsync(SettingsViewModel.SECTION_ABOUT);
    }

    private Task ShowSettingsAsync(string section)
    {
        SettingsVm ??= new SettingsViewModel(ApplicationVm);

        SettingsVm.ShowSection(section);

        return ApplicationVm.Dialogs.ShowSettingsAsync(SettingsVm);
    }

    /// <summary>
    /// File &gt; Exit. Goes through the single shutdown path, which is the same one the window's close
    /// button takes: ask about unapplied work, save the window state in MainWindow.OnClosing, dispose
    /// the service provider - and with it the connection holding the database's exclusive file lock.
    ///
    /// It used to be Environment.Exit(0), which does none of that: the process simply stops.
    /// </summary>
    private async Task ExitAsync()
    {
        await ApplicationVm.RequestShutdownAsync();
    }

    #endregion

    #region Connection Flow

    private async Task LoadSchemaAfterConnectionAsync(IDatabaseSession session)
    {
        IsLoading = true;
        StatusText = "Loading database schema...";

        try
        {
            CurrentConnection = session.Connection;
            StatusText = $"Connected to {session.Connection.FilePath}";

            // Add to recent files
            if (!string.IsNullOrEmpty(session.Connection.FilePath))
            {
                await Settings.AddRecentFileAsync(session.Connection.FilePath);
                var settings = await Settings.LoadAsync();
                LoadRecentFiles(settings);
            }

            // This connection's branch only: the others are already loaded and reloading them would
            // throw away counts and expanded state nobody asked to lose.
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync(session);

            Logger.LogInformation("Database schema loaded for: {FilePath}", session.Connection.FilePath);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Logger.LogError(ex, "Error loading database schema");
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Event Handlers

    private void OnSessionOpened(object? sender, SessionEventArgs e)
    {
        UpdateConnectionState();
    }

    private void OnSessionClosed(object? sender, SessionEventArgs e)
    {
        UpdateConnectionState();
    }

    private void OnActiveSessionChanged(object? sender, SessionEventArgs? e)
    {
        UpdateConnectionState();
    }

    private void UpdateConnectionState()
    {
        var session = Connections.Active;

        IsConnected = session?.IsConnected == true;
        CurrentConnection = session?.Connection;
        OpenConnectionCount = Connections.Sessions.Count;

        // The left half of the status bar: which database, and what it is made of (1.5).
        ConnectionSummary = session == null
            ? string.Empty
            : $"{session.DisplayName} - {session.Connection.FilePath}";

        EngineSummary = session == null ? string.Empty : DescribeEngine(session);
    }

    /// <summary>
    /// What the status bar says about the engine. Only what the connection actually knows: the store
    /// it was opened with, whether it is encrypted, whether it is read-only. Page size and the
    /// transaction model are read from the file by the Open dialog and are not carried on the session,
    /// so they are not claimed here - a status bar that states something it has not been told is
    /// worse than one that says less.
    /// </summary>
    private static string DescribeEngine(IDatabaseSession session)
    {
        var parts = new List<string>
        {
            session.Connection.StorageEngine.ToUpperInvariant() == "LSM" ? "LSM" : "B-Tree"
        };

        if (session.Connection.IsEncrypted)
            parts.Add("encrypted");

        if (session.IsReadOnly)
            parts.Add("read-only");

        return string.Join(" - ", parts);
    }

    private void ShowNotifications()
    {
        AreNotificationsVisible = !AreNotificationsVisible;

        if (AreNotificationsVisible)
            ApplicationVm.Notifications.MarkAllRead();

        UpdateNotificationState();
    }

    private void UpdateNotificationState()
    {
        HasNotifications = ApplicationVm.Notifications.Notifications.Count > 0;
        HasUnreadNotifications = ApplicationVm.Notifications.UnreadCount > 0;
    }

    private void OnNotificationsChanged(object? sender, EventArgs e)
    {
        UpdateNotificationState();
    }

    #endregion

    #region Properties

    /// <summary>
    /// The settings window's ViewModel, kept rather than rebuilt: the window is not modal, so it can
    /// still be open when the menu item is used again, and a second ViewModel would put a second
    /// section selection on the same live settings.
    /// </summary>
    public SettingsViewModel? SettingsVm { get; private set; }

    [Notify]
    public string Title { get; set; } = null!;

    [Notify]
    public ConnectionInfo? CurrentConnection { get; set; }

    [Notify]
    public string StatusText { get; set; } = null!;

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public bool IsConnected { get; private set; }

    /// <summary>
    /// The connection shown in the status bar: its name and its path.
    /// </summary>
    [Notify]
    public string ConnectionSummary { get; private set; } = string.Empty;

    /// <summary>
    /// What that connection is made of, as far as the connection itself knows.
    /// </summary>
    [Notify]
    public string EngineSummary { get; private set; } = string.Empty;

    /// <summary>
    /// Where the cursor is in the selected query tab, the way a person counts: line and column from 1.
    /// </summary>
    public string CaretSummary => ApplicationVm.WorkspaceTabsVm.SelectedQueryTab is { } tab
        ? $"Ln {tab.CaretLine}, Col {tab.CaretColumn}"
        : string.Empty;

    /// <summary>
    /// Everything that has happened and did not need an answer (WS-7).
    /// </summary>
    public System.Collections.ObjectModel.ReadOnlyObservableCollection<Notification> Notifications =>
        ApplicationVm.Notifications.Notifications;

    [Notify]
    public bool HasNotifications { get; private set; }

    /// <summary>
    /// The dot on the bell.
    /// </summary>
    [Notify]
    public bool HasUnreadNotifications { get; private set; }

    [Notify]
    public bool AreNotificationsVisible { get; private set; }

    /// <summary>
    /// How many databases are open. One was the only possible answer until this stage.
    /// </summary>
    [Notify]
    public int OpenConnectionCount { get; private set; }

    [Notify]
    public ObservableCollection<RecentFileItem> RecentFiles { get; private set; } = null!;

    [Notify]
    public bool HasRecentFiles { get; private set; }

    #endregion

    #region Commands

    public ICommand NewDatabaseCommand { get; private set; } = null!;

    public ICommand OpenDatabaseCommand { get; private set; } = null!;

    public ICommand CloseDatabaseCommand { get; private set; } = null!;

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand ExportCommand { get; private set; } = null!;

    public ICommand ImportCommand { get; private set; } = null!;

    public ICommand OpenRecentCommand { get; private set; } = null!;

    public ICommand ClearRecentFilesCommand { get; private set; } = null!;

    public ICommand SettingsCommand { get; private set; } = null!;

    public ICommand AboutCommand { get; private set; } = null!;

    public ICommand ExitCommand { get; private set; } = null!;

    public ICommand ShowNotificationsCommand { get; private set; } = null!;

    public ICommand HideNotificationsCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IConnectionManager Connections => ApplicationVm.Connections;

    public ISettingsService Settings => ApplicationVm.Settings;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}

/// <summary>
/// Represents a recent file item for display.
/// </summary>
public sealed class RecentFileItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
}
