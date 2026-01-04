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

    private static ApplicationViewModel? s_instance;
    private static readonly object s_lock = new();

    public static ApplicationViewModel Instance
    {
        get
        {
            if (s_instance == null)
            {
                lock (s_lock)
                {
                    s_instance ??= Program.GetService<ApplicationViewModel>();
                }
            }
            return s_instance;
        }
    }

    #endregion

    #region Constructors

    public ApplicationViewModel(
        IDatabaseService databaseService,
        ISettingsService settingsService,
        ILogger<ApplicationViewModel> logger)
    {
        InitViewModels(databaseService, settingsService, logger);
    }

    #endregion

    #region Initialization

    private void InitViewModels(
        IDatabaseService databaseService,
        ISettingsService settingsService,
        ILogger<ApplicationViewModel> logger)
    {
        MainWindowVm = new MainWindowViewModel(
            this,
            databaseService,
            settingsService,
            logger);

        ConnectionVm = new ConnectionViewModel(
            this,
            databaseService,
            settingsService);

        DatabaseExplorerVm = new DatabaseExplorerViewModel(
            this,
            databaseService);

        QueryEditorVm = new QueryEditorViewModel(
            this,
            databaseService);

        TableStructureVm = new TableStructureViewModel(
            this,
            databaseService);
    }

    #endregion

    #region Properties

    public MainWindowViewModel MainWindowVm { get; private set; } = null!;
    public ConnectionViewModel ConnectionVm { get; private set; } = null!;
    public DatabaseExplorerViewModel DatabaseExplorerVm { get; private set; } = null!;
    public QueryEditorViewModel QueryEditorVm { get; private set; } = null!;
    public TableStructureViewModel TableStructureVm { get; private set; } = null!;

    #endregion
}
