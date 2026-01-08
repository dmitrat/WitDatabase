using System.Windows.Input;
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
        ExitCommand = new RelayCommand(Exit);
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

    private void OnDatabaseServiceConnectionStatusChanged(object? sender, bool isConnected)
    {
        IsConnected = isConnected;
    }

    #endregion

    #region Commands

    public ICommand NewDatabaseCommand { get; private set; } = null!;

    public ICommand OpenDatabaseCommand { get; private set; } = null!;

    public ICommand CloseDatabaseCommand { get; private set; } = null!;

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand ExitCommand { get; private set; } = null!;

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

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ISettingsService Settings => ApplicationVm.Settings;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
