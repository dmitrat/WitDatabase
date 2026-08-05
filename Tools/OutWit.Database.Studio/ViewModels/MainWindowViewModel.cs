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
        IsConnected = Database.IsConnected;
        RecentFiles = new ObservableCollection<RecentFileItem>();
    }

    private void InitEvents()
    {
        Database.ConnectionStatusChanged += OnDatabaseServiceConnectionStatusChanged;
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

        if (!result || ApplicationVm.ConnectionVm.SelectedConnection == null)
        {
            Logger.LogInformation("Dialog cancelled or no connection selected");
            return;
        }

        await LoadSchemaAfterConnectionAsync(ApplicationVm.ConnectionVm.SelectedConnection);
    }

    private async void OpenDatabaseAsync()
    {
        var result = await ApplicationVm.ConnectionVm.ShowOpenDialogAsync();

        if (!result || ApplicationVm.ConnectionVm.SelectedConnection == null)
            return;

        await LoadSchemaAfterConnectionAsync(ApplicationVm.ConnectionVm.SelectedConnection);
    }

    private async void CloseDatabaseAsync()
    {
        if (!CanCloseDatabase())
            return;

        // Ask while there is still a connection to apply the edits to. Afterwards the only honest
        // offer left would be to discard them.
        if (!await ApplicationVm.WorkspaceTabsVm.ConfirmCloseAllAsync())
        {
            Logger.LogInformation("Disconnect cancelled: a tab has unapplied changes");
            return;
        }

        IsLoading = true;
        StatusText = "Disconnecting...";

        try
        {
            await Database.DisconnectAsync();
            CurrentConnection = null;
            StatusText = "Disconnected";

            ApplicationVm.DatabaseExplorerVm.Nodes.Clear();

            Logger.LogInformation("Disconnected from database");
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

    private async Task ExportAsync()
    {
        if (!IsConnected)
            return;

        var mainWindow = ApplicationVm.MainWindow;
        if (mainWindow == null)
            return;

        var exportVm = new ExportViewModel(ApplicationVm);
        await exportVm.InitializeAsync();

        await ExportDialog.ShowAsync(mainWindow, exportVm);
    }

    private async Task ImportAsync()
    {
        if (!IsConnected)
            return;

        var mainWindow = ApplicationVm.MainWindow;
        if (mainWindow == null)
            return;

        var importVm = new ImportViewModel(ApplicationVm);
        await importVm.InitializeAsync();

        await ImportDialog.ShowAsync(mainWindow, importVm);
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

        if (IsConnected)
        {
            CloseDatabaseAsync();
        }

        var connection = new ConnectionInfo { FilePath = filePath };
        
        IsLoading = true;
        StatusText = $"Connecting to {Path.GetFileName(filePath)}...";

        try
        {
            await Database.ConnectAsync(connection);
            await LoadSchemaAfterConnectionAsync(connection);
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

    private async Task ShowSettingsAsync()
    {
        var mainWindow = ApplicationVm.MainWindow;
        if (mainWindow == null)
            return;

        var settingsVm = new SettingsViewModel(ApplicationVm);
        await settingsVm.InitializeAsync();

        await SettingsDialog.ShowAsync(mainWindow, settingsVm);
    }

    private async Task ShowAboutAsync()
    {
        var mainWindow = ApplicationVm.MainWindow;
        if (mainWindow == null)
            return;

        var aboutVm = new AboutViewModel(ApplicationVm);
        await AboutDialog.ShowAsync(mainWindow, aboutVm);
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

    private async Task LoadSchemaAfterConnectionAsync(ConnectionInfo connection)
    {
        IsLoading = true;
        StatusText = "Loading database schema...";

        try
        {
            CurrentConnection = connection;
            StatusText = $"Connected to {CurrentConnection.FilePath}";

            // Add to recent files
            if (!string.IsNullOrEmpty(connection.FilePath))
            {
                await Settings.AddRecentFileAsync(connection.FilePath);
                var settings = await Settings.LoadAsync();
                LoadRecentFiles(settings);
            }

            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            Logger.LogInformation("Database schema loaded for: {FilePath}", CurrentConnection.FilePath);
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

    private void OnDatabaseServiceConnectionStatusChanged(object? sender, bool isConnected)
    {
        IsConnected = isConnected;
    }

    #endregion

    #region Properties

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

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

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
