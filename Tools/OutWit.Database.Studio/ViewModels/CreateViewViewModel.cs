using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Services;

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
        InitEvents();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        ViewName = string.Empty;
        SelectStatement = "SELECT \n    \nFROM \nWHERE ";
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    private void InitCommands()
    {
        CreateViewCommand = new RelayCommandAsync(CreateViewAsync);
        CancelCommand = new RelayCommand(Cancel);
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
            await Database!.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Dialog.CreateView.Created", ViewName);
            Logger.LogInformation("Created view: {ViewName}", ViewName);

            // Refresh explorer
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            ShouldCloseDialog(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Dialog.CreateView.Failed", ex.Message);
            ApplicationVm.MainWindowVm.StatusText = Localization["Dialog.CreateView.FailedShort"];
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
                               && Database?.IsConnected == true;
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

    /// <summary>
    /// The active connection - the one selected in the tree. These dialogs act on what the user is
    /// looking at; an open tab does not (WS-3). Null when nothing is open, which every caller here
    /// already had to handle as "not connected".
    /// </summary>
    public IDatabaseSession? Database => ApplicationVm.ActiveSession;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion

    #region Localization

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
