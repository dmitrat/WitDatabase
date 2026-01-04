using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ISettingsService m_settingsService;
    private readonly ILogger<ApplicationViewModel> m_logger;

    #endregion

    #region Constructors

    public MainWindowViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService,
        ISettingsService settingsService,
        ILogger<ApplicationViewModel> logger)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_settingsService = settingsService;
        m_logger = logger;

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
        IsConnected = m_databaseService.IsConnected;
    }

    private void InitEvents()
    {
        m_databaseService.ConnectionStatusChanged += OnDatabaseServiceConnectionStatusChanged;
    }

    private void InitCommands()
    {
        NewDatabaseCommand = new DelegateCommand<object>(_ => NewDatabaseAsync());
        OpenDatabaseCommand = new DelegateCommand<object>(_ => OpenDatabaseAsync());
        CloseDatabaseCommand = new DelegateCommand<object>(_ => CloseDatabaseAsync(), _ => CanCloseDatabase());
        RefreshCommand = new DelegateCommand<object>(_ => RefreshAsync(), _ => IsConnected);
        ExitCommand = new DelegateCommand<object>(_ => Exit());
    }

    #endregion

    #region Commands

    public DelegateCommand<object> NewDatabaseCommand { get; private set; } = null!;
    public DelegateCommand<object> OpenDatabaseCommand { get; private set; } = null!;
    public DelegateCommand<object> CloseDatabaseCommand { get; private set; } = null!;
    public DelegateCommand<object> RefreshCommand { get; private set; } = null!;
    public DelegateCommand<object> ExitCommand { get; private set; } = null!;

    private async void NewDatabaseAsync()
    {
        m_logger.LogInformation("NewDatabase command invoked");

        var result = await ApplicationVm.ConnectionVm.ShowCreateDialogAsync();
        m_logger.LogInformation("ShowCreateDialogAsync returned: {Result}", result);

        if (!result || ApplicationVm.ConnectionVm.SelectedConnection == null)
        {
            m_logger.LogInformation("Dialog cancelled or no connection selected");
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

        IsLoading = true;
        StatusText = "Disconnecting...";

        try
        {
            await m_databaseService.DisconnectAsync();
            CurrentConnection = null;
            StatusText = "Disconnected";

            ApplicationVm.DatabaseExplorerVm.Nodes.Clear();

            m_logger.LogInformation("Disconnected from database");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            m_logger.LogError(ex, "Error disconnecting from database");
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

    private void Exit()
    {
        Environment.Exit(0);
    }

    #endregion

    #region Connection Flow

    private async Task LoadSchemaAfterConnectionAsync(ConnectionInfo connection)
    {
        IsLoading = true;
        StatusText = "Loading database schema...";

        try
        {
            // Connection is established inside ConnectionViewModel.
            CurrentConnection = connection;
            StatusText = $"Connected to {CurrentConnection.FilePath}";

            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            m_logger.LogInformation("Database schema loaded for: {FilePath}", CurrentConnection.FilePath);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            m_logger.LogError(ex, "Error loading database schema");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnDatabaseServiceConnectionStatusChanged(object? sender, bool isConnected)
    {
        IsConnected = isConnected;
    }

    #endregion

    #region Properties

    [Notify]
    public string Title { get; set; } = "WitDatabase Studio";

    [Notify]
    public ConnectionInfo? CurrentConnection { get; set; }

    [Notify]
    public string StatusText { get; set; } = "Ready";

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public bool IsConnected { get; private set; }

    #endregion
}
