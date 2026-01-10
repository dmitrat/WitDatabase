using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for displaying table structure details.
/// </summary>
public class TableStructureViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public TableStructureViewModel(ApplicationViewModel applicationVm)
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
        Columns = [];
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        Database.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    private void InitCommands()
    {
        RefreshCommand = new RelayCommandAsync(RefreshAsync);
    }

    #endregion

    #region Functions

    public async Task RefreshAsync()
    {
        if (!Database.IsConnected)
            return;

        if (SelectedObjectType == DatabaseNodeType.Index)
        {
            if (string.IsNullOrWhiteSpace(SelectedObjectName))
                return;

            await LoadIndexStructureAsync(SelectedObjectName);
            return;
        }

        if (SelectedObjectType is DatabaseNodeType.Table or DatabaseNodeType.View)
        {
            if (string.IsNullOrWhiteSpace(SelectedObjectName))
                return;

            IsLoading = true;
            ErrorMessage = null;

            try
            {
                var columns = await Database.GetColumnsAsync(SelectedObjectName);
                Columns = columns.ToList();

                ApplicationVm.MainWindowVm.StatusText = $"Loaded {columns.Count} columns from {SelectedObjectTypeDisplay.ToLowerInvariant()} \"{SelectedObjectName}\"";

                Logger.LogInformation("Loaded structure for {Type} {Name}: {Count} columns",
                    SelectedObjectTypeDisplay, SelectedObjectName, columns.Count);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load structure: {ex.Message}";
                ApplicationVm.MainWindowVm.StatusText = $"Error loading {SelectedObjectTypeDisplay.ToLowerInvariant()} structure";
                Logger.LogError(ex, "Failed to load structure for {Type} {Name}", SelectedObjectTypeDisplay, SelectedObjectName);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    private async Task LoadIndexStructureAsync(string indexName)
    {
        if (!Database.IsConnected)
            return;

        IsLoading = true;
        ErrorMessage = null;
        Columns = [];
        IndexTableName = null;
        IndexIsUnique = null;
        IndexFilterCondition = null;

        try
        {
            var indexLiteral = "'" + indexName.Replace("'", "''") + "'";

            var sql =
                "SELECT " +
                "i.TABLE_NAME, " +
                "i.COLUMN_NAME, " +
                "i.ORDINAL_POSITION, " +
                "i.IS_UNIQUE, " +
                "i.FILTER_CONDITION, " +
                "c.DATA_TYPE " +
                "FROM INFORMATION_SCHEMA.INDEXES i " +
                "LEFT JOIN INFORMATION_SCHEMA.COLUMNS c " +
                "ON c.TABLE_NAME = i.TABLE_NAME AND c.COLUMN_NAME = i.COLUMN_NAME " +
                "WHERE i.INDEX_NAME = " + indexLiteral + " " +
                "ORDER BY i.ORDINAL_POSITION";

            var result = await Database.ExecuteQueryAsync(sql);

            if (string.IsNullOrEmpty(result.ErrorMessage) && result.Data != null && result.Data.Pages.Count > 0)
            {
                var rows = result.Data.Pages.SelectMany(p => p.Rows).ToList();
                if (rows.Count > 0)
                {
                    var list = new List<ColumnInfo>();

                    foreach (var row in rows)
                    {
                        var tableName = row[0]?.Text;
                        var colName = row[1]?.Text ?? string.Empty;
                        var ordinal = 0;
                        _ = int.TryParse(row[2]?.Text, out ordinal);
                        var isUniqueStr = row[3]?.Text;
                        var filter = row[4]?.Text;
                        var dataType = row[5]?.Text;

                        IndexTableName ??= tableName;

                        if (IndexIsUnique is null && !string.IsNullOrWhiteSpace(isUniqueStr))
                            IndexIsUnique = isUniqueStr.Equals("YES", StringComparison.OrdinalIgnoreCase);

                        if (IndexFilterCondition is null && !string.IsNullOrWhiteSpace(filter))
                            IndexFilterCondition = filter;

                        list.Add(new ColumnInfo
                        {
                            Name = colName,
                            OrdinalPosition = ordinal == 0 ? list.Count + 1 : ordinal,
                            DataType = string.IsNullOrWhiteSpace(dataType) ? string.Empty : dataType,
                            IsNullable = true,
                            IsPrimaryKey = false,
                            DefaultValue = null
                        });
                    }

                    Columns = list;
                    ApplicationVm.MainWindowVm.StatusText = $"Loaded {Columns.Count} columns from index \"{indexName}\"";
                    return;
                }
            }

            // Fallback to PRAGMA index_info
            var pragmaResult = await Database.ExecuteQueryAsync(
                $"PRAGMA index_info(\"{indexName.Replace("\"", "\"\"")}\")");

            if (!string.IsNullOrEmpty(pragmaResult.ErrorMessage) || pragmaResult.Data == null)
            {
                ErrorMessage = pragmaResult.ErrorMessage ?? result.ErrorMessage ?? "Failed to load index info";
                return;
            }

            var fallbackColumns = new List<ColumnInfo>();
            var pragmaRows = pragmaResult.Data.Pages.SelectMany(p => p.Rows).ToList();
            foreach (var row in pragmaRows)
            {
                var colName = row.Values.Length > 2 ? row[2]?.Text ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(colName))
                    continue;

                fallbackColumns.Add(new ColumnInfo
                {
                    Name = colName,
                    DataType = string.Empty,
                    IsNullable = true,
                    DefaultValue = null,
                    IsPrimaryKey = false,
                    OrdinalPosition = fallbackColumns.Count + 1
                });
            }

            Columns = fallbackColumns;
            ApplicationVm.MainWindowVm.StatusText = $"Loaded {Columns.Count} columns from index \"{indexName}\"";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load index structure: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error loading index structure";
            Logger.LogError(ex, "Failed to load index structure for {IndexName}", indexName);
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Public Methods

    public async Task LoadTableStructureAsync(TableInfo table)
    {
        SelectedTable = table;
        await RefreshAsync();
    }

    public async Task LoadObjectStructureAsync(DatabaseNode node)
    {
        if (node == null)
            return;

        SelectedObjectName = node.Name;
        SelectedObjectType = node.NodeType;
        ViewDefinition = null;

        if (node.NodeType == DatabaseNodeType.Table)
        {
            SelectedTable = new TableInfo { Name = node.Name };
            await RefreshAsync();
            return;
        }

        if (node.NodeType == DatabaseNodeType.View)
        {
            SelectedTable = new TableInfo { Name = node.Name };
            await LoadViewDefinitionAsync(node.Name);
            await RefreshAsync();
            return;
        }

        if (node.NodeType == DatabaseNodeType.Index)
        {
            SelectedTable = null;
            await LoadIndexStructureAsync(node.Name);
            return;
        }

        Clear();
    }

    private async Task LoadViewDefinitionAsync(string viewName)
    {
        if (!Database.IsConnected)
            return;

        try
        {
            var result = await Database.ExecuteQueryAsync(
                $"SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = '{viewName.Replace("'", "''")}'");

            if (!string.IsNullOrEmpty(result.ErrorMessage) || result.Data == null || result.Data.Pages.Count == 0)
            {
                return;
            }

            var rows = result.Data.Pages.SelectMany(p => p.Rows).ToList();
            if (rows.Count > 0)
            {
                ViewDefinition = rows[0][0]?.Text;
            }
        }
        catch
        {
            // ignore
        }
    }

    public void Clear()
    {
        SelectedTable = null;
        SelectedObjectName = null;
        SelectedObjectType = null;
        Columns = [];
        ErrorMessage = null;
        ViewDefinition = null;
        IndexTableName = null;
        IndexIsUnique = null;
        IndexFilterCondition = null;
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        CanRefresh = !string.IsNullOrWhiteSpace(SelectedObjectName) && !IsLoading && Database.IsConnected;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((TableStructureViewModel vm) => vm.SelectedObjectName))
            UpdateStatus();

        if (e.IsProperty((TableStructureViewModel vm) => vm.IsLoading))
            UpdateStatus();
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        UpdateStatus();
    }

    #endregion

    #region Properties

    public bool IsStructureSelected => SelectedObjectType != null;

    [Notify(NotifyAlso = nameof(IsStructureSelected))]
    public string? SelectedObjectName { get; set; }

    [Notify(NotifyAlso = nameof(IsStructureSelected))]
    public DatabaseNodeType? SelectedObjectType { get; set; }

    public string SelectedObjectTypeDisplay => SelectedObjectType switch
    {
        DatabaseNodeType.Table => "Table",
        DatabaseNodeType.View => "View",
        DatabaseNodeType.Index => "Index",
        _ => "Object"
    };

    [Notify]
    public TableInfo? SelectedTable { get; set; }

    [Notify]
    public List<ColumnInfo> Columns { get; set; } = null!;

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool CanRefresh { get; private set; }

    [Notify(NotifyAlso = nameof(HasViewDefinition))]
    public string? ViewDefinition { get; set; }

    public bool HasViewDefinition => !string.IsNullOrWhiteSpace(ViewDefinition);

    [Notify]
    public string? IndexTableName { get; set; }

    [Notify]
    public bool? IndexIsUnique { get; set; }

    [Notify]
    public string? IndexFilterCondition { get; set; }

    public bool HasIndexDetails => !string.IsNullOrWhiteSpace(IndexTableName) || IndexIsUnique is not null || !string.IsNullOrWhiteSpace(IndexFilterCondition);

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ISettingsService Settings => ApplicationVm.Settings;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
