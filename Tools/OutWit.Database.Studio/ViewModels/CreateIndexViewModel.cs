using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
        InitEvents();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        IndexName = string.Empty;
        TableName = string.Empty;
        IsUnique = false;
        SelectedColumns = [];
        FilterCondition = string.Empty;
        AvailableTables = [];
        AvailableColumns = [];
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        SelectedColumns.CollectionChanged += OnCollectionChanged;
    }

    private void InitCommands()
    {
        LoadTablesCommand = new RelayCommandAsync(LoadTablesAsync);
        LoadColumnsCommand = new RelayCommandAsync(LoadColumnsAsync);
        GenerateDdlCommand = new RelayCommand(GenerateDdl);
        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync);
        CancelCommand = new RelayCommand(Cancel);
    }

    #endregion

    #region Functions

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
        if (!CanCreateIndex)
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var sql = BuildCreateIndexSql();
            await Database.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = $"Created index: {IndexName}";
            Logger.LogInformation("Created index: {IndexName}", IndexName);

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

    private string BuildCreateIndexSql()
    {
        var sb = new StringBuilder();
        
        sb.Append("CREATE ");
        if (IsUnique)
            sb.Append("UNIQUE ");
        
        sb.Append($"INDEX {IndexName}");
        sb.Append($" ON {TableName} (");
        sb.Append(string.Join(", ", SelectedColumns));
        sb.Append(')');

        if (!string.IsNullOrWhiteSpace(FilterCondition))
        {
            sb.Append($" WHERE {FilterCondition}");
        }

        sb.Append(';');

        return sb.ToString();
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        var hasIndexName = !string.IsNullOrWhiteSpace(IndexName);
        var hasTableName = !string.IsNullOrWhiteSpace(TableName);
        var hasColumns = SelectedColumns.Count > 0;

        CanCreateIndex = hasIndexName && hasTableName && hasColumns && !IsCreating && Database.IsConnected;
        CanGenerateDdl = hasIndexName && hasTableName && hasColumns;
        CanLoadColumns = hasTableName;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((CreateIndexViewModel vm) => vm.IndexName))
            UpdateStatus();

        if (e.IsProperty((CreateIndexViewModel vm) => vm.TableName))
            UpdateStatus();

        if (e.IsProperty((CreateIndexViewModel vm) => vm.IsCreating))
            UpdateStatus();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

    [Notify]
    public ObservableCollection<string> SelectedColumns { get; private set; } = null!;

    [Notify]
    public string FilterCondition { get; set; } = null!;

    [Notify]
    public List<string> AvailableTables { get; set; } = null!;

    [Notify]
    public List<string> AvailableColumns { get; set; } = null!;

    [Notify]
    public string? GeneratedDdl { get; set; }

    [Notify]
    public bool IsCreating { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool CanGenerateDdl { get; private set; }

    [Notify]
    public bool CanCreateIndex { get; private set; }

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
