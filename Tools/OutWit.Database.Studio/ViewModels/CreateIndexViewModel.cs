using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for creating a new index.
/// </summary>
public class CreateIndexViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool> ShouldCloseDialog = delegate { };

    #endregion

    #region Constructors

    public CreateIndexViewModel(ApplicationViewModel applicationVm)
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
        IndexName = string.Empty;
        TableName = string.Empty;
        IsUnique = false;
        SelectedColumns = new ObservableCollection<string>();
        FilterCondition = string.Empty;
    }

    private void InitCommands()
    {
        LoadTablesCommand = new RelayCommandAsync(LoadTablesAsync);
        LoadColumnsCommand = new RelayCommandAsync(LoadColumnsAsync);
        GenerateDdlCommand = new RelayCommand(GenerateDdl);
        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync);
        CancelCommand = new RelayCommand(Cancel);
    }

    private void InitEvents()
    {
        this.PropertyChanged += OnPropertyChanged;
        SelectedColumns.CollectionChanged += OnCollectionChanged;
    }

    #endregion

    #region Commands

    private async Task LoadTablesAsync()
    {
        if (!Database.IsConnected)
            return;

        try
        {
            var tables = await Database.GetTablesAsync();
            AvailableTables = tables.Select(t => t.Name).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load tables");
        }
    }

    private async Task LoadColumnsAsync()
    {
        if (!Database.IsConnected || string.IsNullOrWhiteSpace(TableName))
            return;

        try
        {
            var columns = await Database.GetColumnsAsync(TableName);
            AvailableColumns = columns.Select(c => c.Name).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load columns for table {TableName}", TableName);
        }
    }

    private void GenerateDdl()
    {
        GeneratedDdl = BuildCreateIndexSql();
        Logger.LogInformation("Generated DDL for index {IndexName}", IndexName);
    }

    private async Task CreateIndexAsync()
    {
        if (!CanCreateIndexChanged)
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var sql = BuildCreateIndexSql();
            await Database.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = $"Created index: {IndexName}";
            Logger.LogInformation("Created index: {IndexName}", IndexName);

            // Refresh explorer
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            ShouldCloseDialog(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create index: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error creating index";
            Logger.LogError(ex, "Failed to create index {IndexName}", IndexName);
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

    #region Tools

    private void UpdateStatus()
    {
        CanCreateIndexChanged = !string.IsNullOrWhiteSpace(IndexName)
                                && !string.IsNullOrWhiteSpace(TableName)
                                && SelectedColumns.Count > 0
                                && !IsCreating
                                && Database.IsConnected;

        CanGenerateDdlChanged = !string.IsNullOrWhiteSpace(IndexName)
                                && !string.IsNullOrWhiteSpace(TableName)
                                && SelectedColumns.Count > 0;

        CanLoadColumns = !string.IsNullOrWhiteSpace(TableName);
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.IsProperty((CreateIndexViewModel vm)=>vm.IndexName))
            UpdateStatus();

        if (e.IsProperty((CreateIndexViewModel vm) => vm.TableName))
            UpdateStatus();

        if (e.IsProperty((CreateIndexViewModel vm) => vm.IsCreating))
            UpdateStatus();
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateStatus();
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
    public bool CanGenerateDdlChanged { get; private set; }

    [Notify]
    public bool CanCreateIndexChanged { get; private set; }

    [Notify]
    public bool CanLoadColumns { get; private set; }

    #endregion

    #region Commands

    public ICommand LoadTablesCommand { get; private set; } = null!;

    public ICommand LoadColumnsCommand { get; private set; } = null!;

    public ICommand GenerateDdlCommand { get; private set; } = null!;

    public ICommand CreateIndexCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
