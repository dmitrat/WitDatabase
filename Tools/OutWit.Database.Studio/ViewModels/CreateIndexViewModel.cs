using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for creating a new index.
/// </summary>
public class CreateIndexViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ILogger<CreateIndexViewModel> m_logger;

    #endregion

    #region Constructors

    public CreateIndexViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateIndexViewModel>.Instance;

        InitDefault();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        IndexName = string.Empty;
        TableName = string.Empty;
        IsUnique = false;
        SelectedColumns = new ObservableCollection<string>();
        FilterCondition = string.Empty;
    }

    private void InitCommands()
    {
        LoadTablesCommand = new DelegateCommand<object>(async _ => await LoadTablesAsync());
        LoadColumnsCommand = new DelegateCommand<object>(async _ => await LoadColumnsAsync(), _ => !string.IsNullOrWhiteSpace(TableName));
        GenerateDdlCommand = new DelegateCommand<object>(_ => GenerateDdl(), _ => CanGenerateDdl());
        CreateIndexCommand = new DelegateCommand<object>(async _ => await CreateIndexAsync(), _ => CanCreateIndex());
        CancelCommand = new DelegateCommand<object>(_ => Cancel());
    }

    #endregion

    #region Commands

    public DelegateCommand<object> LoadTablesCommand { get; private set; } = null!;
    public DelegateCommand<object> LoadColumnsCommand { get; private set; } = null!;
    public DelegateCommand<object> GenerateDdlCommand { get; private set; } = null!;
    public DelegateCommand<object> CreateIndexCommand { get; private set; } = null!;
    public DelegateCommand<object> CancelCommand { get; private set; } = null!;

    private async Task LoadTablesAsync()
    {
        if (!m_databaseService.IsConnected)
            return;

        try
        {
            var tables = await m_databaseService.GetTablesAsync();
            AvailableTables = tables.Select(t => t.Name).ToList();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to load tables");
        }
    }

    private async Task LoadColumnsAsync()
    {
        if (!m_databaseService.IsConnected || string.IsNullOrWhiteSpace(TableName))
            return;

        try
        {
            var columns = await m_databaseService.GetColumnsAsync(TableName);
            AvailableColumns = columns.Select(c => c.Name).ToList();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to load columns for table {TableName}", TableName);
        }
    }

    private void GenerateDdl()
    {
        GeneratedDdl = BuildCreateIndexSql();
        m_logger.LogInformation("Generated DDL for index {IndexName}", IndexName);
    }

    private bool CanGenerateDdl()
    {
        return !string.IsNullOrWhiteSpace(IndexName)
            && !string.IsNullOrWhiteSpace(TableName)
            && SelectedColumns.Count > 0;
    }

    private async Task CreateIndexAsync()
    {
        if (!CanCreateIndex())
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var sql = BuildCreateIndexSql();
            await m_databaseService.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = $"Created index: {IndexName}";
            m_logger.LogInformation("Created index: {IndexName}", IndexName);

            // Refresh explorer
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            IsCompleted = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create index: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error creating index";
            m_logger.LogError(ex, "Failed to create index {IndexName}", IndexName);
        }
        finally
        {
            IsCreating = false;
        }
    }

    private bool CanCreateIndex()
    {
        return !string.IsNullOrWhiteSpace(IndexName)
            && !string.IsNullOrWhiteSpace(TableName)
            && SelectedColumns.Count > 0
            && !IsCreating
            && m_databaseService.IsConnected;
    }

    private void Cancel()
    {
        IsCancelled = true;
    }

    #endregion

    #region Build DDL

    private string BuildCreateIndexSql()
    {
        var sb = new StringBuilder();
        
        sb.Append("CREATE ");
        if (IsUnique)
            sb.Append("UNIQUE ");
        
        sb.Append($"INDEX {IndexName}");
        sb.Append($" ON {TableName} (");
        sb.Append(string.Join(", ", SelectedColumns));
        sb.Append(")");

        if (!string.IsNullOrWhiteSpace(FilterCondition))
        {
            sb.Append($" WHERE {FilterCondition}");
        }

        sb.Append(";");

        return sb.ToString();
    }

    #endregion

    #region Public Methods

    public void Reset()
    {
        IndexName = string.Empty;
        TableName = string.Empty;
        IsUnique = false;
        SelectedColumns.Clear();
        FilterCondition = string.Empty;
        GeneratedDdl = null;
        ErrorMessage = null;
        IsCompleted = false;
        IsCancelled = false;
    }

    #endregion

    #region Properties

    [Notify]
    public string IndexName { get; set; } = null!;

    [Notify]
    public string TableName { get; set; } = null!;

    [Notify]
    public bool IsUnique { get; set; }

    public ObservableCollection<string> SelectedColumns { get; private set; } = null!;

    [Notify]
    public string FilterCondition { get; set; } = null!;

    [Notify]
    public List<string> AvailableTables { get; set; } = [];

    [Notify]
    public List<string> AvailableColumns { get; set; } = [];

    [Notify]
    public string? GeneratedDdl { get; set; }

    [Notify]
    public bool IsCreating { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool IsCompleted { get; set; }

    [Notify]
    public bool IsCancelled { get; set; }

    public bool CanGenerateDdlChanged => CanGenerateDdl();
    public bool CanCreateIndexChanged => CanCreateIndex();

    #endregion
}
