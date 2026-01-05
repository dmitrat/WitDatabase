using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for creating a new view.
/// </summary>
public class CreateViewViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ILogger<CreateViewViewModel> m_logger;

    #endregion

    #region Constructors

    public CreateViewViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateViewViewModel>.Instance;

        InitDefault();
        InitCommands();
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
        CreateViewCommand = new DelegateCommand<object>(async _ => await CreateViewAsync(), _ => CanCreateView());
        CancelCommand = new DelegateCommand<object>(_ => Cancel());
    }

    #endregion

    #region Commands

    public DelegateCommand<object> CreateViewCommand { get; private set; } = null!;
    public DelegateCommand<object> CancelCommand { get; private set; } = null!;

    private async Task CreateViewAsync()
    {
        if (!CanCreateView())
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var sql = $"CREATE VIEW {ViewName} AS\n{SelectStatement}";
            await m_databaseService.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = $"Created view: {ViewName}";
            m_logger.LogInformation("Created view: {ViewName}", ViewName);

            // Refresh explorer
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            IsCompleted = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create view: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error creating view";
            m_logger.LogError(ex, "Failed to create view {ViewName}", ViewName);
        }
        finally
        {
            IsCreating = false;
        }
    }

    private bool CanCreateView()
    {
        return !string.IsNullOrWhiteSpace(ViewName)
            && !string.IsNullOrWhiteSpace(SelectStatement)
            && !IsCreating
            && m_databaseService.IsConnected;
    }

    private void Cancel()
    {
        IsCancelled = true;
    }

    #endregion

    #region Public Methods

    public void Reset()
    {
        ViewName = string.Empty;
        SelectStatement = "SELECT \n    \nFROM \nWHERE ";
        ErrorMessage = null;
        IsCompleted = false;
        IsCancelled = false;
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
    public bool IsCompleted { get; set; }

    [Notify]
    public bool IsCancelled { get; set; }

    public bool CanCreateViewChanged => CanCreateView();

    #endregion
}
