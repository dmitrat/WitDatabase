using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for displaying table structure details.
/// </summary>
public class TableStructureViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ILogger<TableStructureViewModel> m_logger;

    #endregion

    #region Constructors

    public TableStructureViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TableStructureViewModel>.Instance;

        InitDefault();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Columns = [];
    }

    private void InitCommands()
    {
        RefreshCommand = new DelegateCommand<object>(async _ => await RefreshAsync(), _ => CanRefresh());
    }

    #endregion

    #region Commands

    public DelegateCommand<object> RefreshCommand { get; private set; } = null!;

    public async Task RefreshAsync()
    {
        if (!m_databaseService.IsConnected)
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
                var columns = await m_databaseService.GetColumnsAsync(SelectedObjectName);
                Columns = columns.ToList();

                ApplicationVm.MainWindowVm.StatusText = $"Loaded {columns.Count} columns from {SelectedObjectTypeDisplay.ToLowerInvariant()} \"{SelectedObjectName}\"";

                m_logger.LogInformation("Loaded structure for {Type} {Name}: {Count} columns",
                    SelectedObjectTypeDisplay, SelectedObjectName, columns.Count);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load structure: {ex.Message}";
                ApplicationVm.MainWindowVm.StatusText = $"Error loading {SelectedObjectTypeDisplay.ToLowerInvariant()} structure";
                m_logger.LogError(ex, "Failed to load structure for {Type} {Name}", SelectedObjectTypeDisplay, SelectedObjectName);
            }
            finally
            {
                IsLoading = false;
            }

            return;
        }
    }

    private async Task LoadIndexStructureAsync(string indexName)
    {
        if (!m_databaseService.IsConnected)
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

            var result = await m_databaseService.ExecuteQueryAsync(sql);

            if (string.IsNullOrEmpty(result.ErrorMessage) && result.ResultTable != null && result.ResultTable.Rows.Count > 0)
            {
                var list = new List<ColumnInfo>();

                foreach (System.Data.DataRow row in result.ResultTable.Rows)
                {
                    var tableName = row[0]?.ToString();
                    var colName = row[1]?.ToString() ?? string.Empty;
                    var ordinal = 0;
                    _ = int.TryParse(row[2]?.ToString(), out ordinal);
                    var isUniqueStr = row[3]?.ToString();
                    var filter = row[4]?.ToString();
                    var dataType = row[5]?.ToString();

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

            // Fallback to PRAGMA index_info
            var pragmaResult = await m_databaseService.ExecuteQueryAsync(
                $"PRAGMA index_info(\"{indexName.Replace("\"", "\"\"")}\")");

            if (!string.IsNullOrEmpty(pragmaResult.ErrorMessage) || pragmaResult.ResultTable == null)
            {
                ErrorMessage = pragmaResult.ErrorMessage ?? result.ErrorMessage ?? "Failed to load index info";
                return;
            }

            var fallbackColumns = new List<ColumnInfo>();
            foreach (System.Data.DataRow row in pragmaResult.ResultTable.Rows)
            {
                var colName = row.ItemArray.Length > 2 ? row[2]?.ToString() ?? string.Empty : string.Empty;
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
            m_logger.LogError(ex, "Failed to load index structure for {IndexName}", indexName);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRefresh()
    {
        return !string.IsNullOrWhiteSpace(SelectedObjectName) && !IsLoading && m_databaseService.IsConnected;
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

        // Folders/others
        Clear();
    }

    private async Task LoadViewDefinitionAsync(string viewName)
    {
        if (!m_databaseService.IsConnected)
            return;

        try
        {
            var result = await m_databaseService.ExecuteQueryAsync(
                $"SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = '{viewName.Replace("'", "''")}'");

            if (!string.IsNullOrEmpty(result.ErrorMessage) || result.ResultTable == null || result.ResultTable.Rows.Count == 0)
            {
                return;
            }

            ViewDefinition = result.ResultTable.Rows[0][0]?.ToString();
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
        RefreshCommand?.RaiseCanExecuteChanged();
    }

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

    #endregion

    #region Properties

    [Notify(NotifyAlso = nameof(CanRefreshChanged))]
    public TableInfo? SelectedTable { get; set; }

    [Notify]
    public List<ColumnInfo> Columns { get; set; } = null!;

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    public bool CanRefreshChanged => CanRefresh();

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
}
