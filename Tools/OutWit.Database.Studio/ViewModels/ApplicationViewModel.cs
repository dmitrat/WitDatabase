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

    public ApplicationViewModel(IDatabaseService databaseService, ISettingsService settingsService, ILogger<ApplicationViewModel> logger)
    {
        Database = databaseService;
        Settings = settingsService;
        Logger = logger;

        InitViewModels();
    }

    #endregion

    #region Initialization

    private void InitViewModels()
    {
        MainWindowVm = new MainWindowViewModel(this);
        ConnectionVm = new ConnectionViewModel(this);
        DatabaseExplorerVm = new DatabaseExplorerViewModel(this);
        QueryEditorVm = new QueryEditorViewModel(this);
        QueryTabsVm = new QueryTabsViewModel(this,
            Database);
        TableStructureVm = new TableStructureViewModel(this,
            Database);
    }

    #endregion

    #region Functions

    public ApplicationViewModel ResetOwnerWindow(Avalonia.Controls.Window? window)
    {
        MainWindow = window;
        return this;
    }

    #endregion

    #region View Models

    public MainWindowViewModel MainWindowVm { get; private set; } = null!;
    public ConnectionViewModel ConnectionVm { get; private set; } = null!;
    public DatabaseExplorerViewModel DatabaseExplorerVm { get; private set; } = null!;
    public QueryEditorViewModel QueryEditorVm { get; private set; } = null!;
    public QueryTabsViewModel QueryTabsVm { get; private set; } = null!;
    public TableStructureViewModel TableStructureVm { get; private set; } = null!;

    #endregion

    #region Properties

    public IDatabaseService Database { get; }

    public ISettingsService Settings { get; }

    public ILogger<ApplicationViewModel> Logger { get; }
    
    public Avalonia.Controls.Window? MainWindow { get; private set; }

    #endregion
}
