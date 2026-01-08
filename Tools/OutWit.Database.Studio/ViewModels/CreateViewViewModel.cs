using System.ComponentModel;
using System.Windows.Input;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.Logging;
using OutWit.Common.Utils;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for creating a new view.
/// </summary>
public class CreateViewViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool> ShouldCloseDialog = delegate { };

    #endregion

    #region Constructors

    public CreateViewViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefault();
        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        ViewName = string.Empty;
        SelectStatement = "SELECT \n    \nFROM \nWHERE ";
    }

    private void InitCommands()
    {
        CreateViewCommand = new RelayCommandAsync(CreateViewAsync);
        CancelCommand = new RelayCommand(Cancel);
    }

    private void InitEvents()
    {
        this.PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Functions

    private async Task CreateViewAsync()
    {
        if (!CanCreateView)
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var sql = $"CREATE VIEW {ViewName} AS\n{SelectStatement}";
            await Database.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = $"Created view: {ViewName}";
            Logger.LogInformation("Created view: {ViewName}", ViewName);

            // Refresh explorer
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            ShouldCloseDialog(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create view: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error creating view";
            Logger.LogError(ex, "Failed to create view {ViewName}", ViewName);
        }
        finally
        {
            IsCreating = false;
        }
    }

    private void Cancel()
    {
        ShouldCloseDialog(false);
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        CanCreateView = !string.IsNullOrWhiteSpace(ViewName)
                               && !string.IsNullOrWhiteSpace(SelectStatement)
                               && !IsCreating
                               && Database.IsConnected;
    }

    #endregion
    
    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.IsProperty((CreateViewViewModel vm)=>vm.ViewName)) 
            UpdateStatus();

        if (e.IsProperty((CreateViewViewModel vm) => vm.SelectStatement))
            UpdateStatus();

        if (e.IsProperty((CreateViewViewModel vm) => vm.IsCreating))
            UpdateStatus();
    }

    #endregion

    #region Properties

    [Notify]
    public string ViewName { get; set; } = null!;

    [Notify]
    public string SelectStatement { get; set; } = null!;

    [Notify]
    public bool IsCreating { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool CanCreateView { get; private set; }

    #endregion

    #region Commands

    public ICommand CreateViewCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
