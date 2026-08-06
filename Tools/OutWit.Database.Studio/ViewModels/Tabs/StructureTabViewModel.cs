using System.ComponentModel;
using System.Data;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Ui.Icons;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// ViewModel for displaying object structure in a tab.
/// </summary>
public class StructureTabViewModel : WorkspaceTabViewModel
{
    #region Constructors

    public StructureTabViewModel(ApplicationViewModel applicationVm, IDatabaseSession session,
        string objectName, DatabaseNodeType objectType)
        : base(applicationVm, session)
    {
        ObjectName = objectName;
        ObjectType = objectType;
        Title = $"{objectName} - Structure";

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
    }

    private void InitCommands()
    {
        RefreshCommand = new RelayCommandAsync(LoadStructureAsync);
    }

    #endregion

    #region WorkspaceTabViewModel

    public override WorkspaceTabType TabType => WorkspaceTabType.Structure;

    public override string IconPath => ObjectType switch
    {
        DatabaseNodeType.Table => StudioIcons.PATH_DB_TABLE,
        DatabaseNodeType.View => StudioIcons.PATH_DB_VIEW,
        DatabaseNodeType.Index => StudioIcons.PATH_DB_INDEX,
        _ => StudioIcons.PATH_DB_TABLE
    };

    /// <summary>
    /// Keyed by connection as well as by object, for the same reason the editor is: two databases can
    /// both have a table called Orders.
    /// </summary>
    public override string? UniqueId => $"structure:{Session?.Id}:{ObjectType}:{ObjectName}";

    #endregion

    #region Functions

    /// <summary>
    /// Loads the structure of the object.
    /// </summary>
    public async Task LoadStructureAsync()
    {
        // This object belongs to one connection: the one the tab was opened in (WS-3).
        var session = Session;

        if (session?.IsConnected != true)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            switch (ObjectType)
            {
                case DatabaseNodeType.Table:
                    await LoadTableStructureAsync(session);
                    break;

                case DatabaseNodeType.View:
                    await LoadViewStructureAsync(session);
                    break;

                case DatabaseNodeType.Index:
                    await LoadIndexStructureAsync(session);
                    break;

                default:
                    ErrorMessage = "Unsupported object type";
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load structure: {ex.Message}";
            Logger.LogError(ex, "Failed to load structure for {Type} {Name}", ObjectType, ObjectName);
        }
        finally
        {
            IsLoading = false;
            UpdateStatus();
        }
    }

    private async Task LoadTableStructureAsync(IDatabaseSession session)
    {
        var columns = await session.GetColumnsAsync(ObjectName);
        Columns = columns.ToList();

        ApplicationVm.MainWindowVm.StatusText = $"Loaded {columns.Count} columns from table \"{ObjectName}\"";
        Logger.LogInformation("Loaded structure for table {Name}: {Count} columns", ObjectName, columns.Count);
    }

    private async Task LoadViewStructureAsync(IDatabaseSession session)
    {
        var columns = await session.GetColumnsAsync(ObjectName);
        Columns = columns.ToList();

        // Load view definition
        try
        {
            var result = await session.ExecuteQueryAsync(
                $"SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = '{ObjectName.Replace("'", "''")}'");

            if (string.IsNullOrEmpty(result.ErrorMessage) && result.Data != null && result.Data.Rows.Count > 0)
            {
                ViewDefinition = result.Data.Rows[0][0] as string;
            }
        }
        catch
        {
            // Ignore view definition errors
        }

        ApplicationVm.MainWindowVm.StatusText = $"Loaded {columns.Count} columns from view \"{ObjectName}\"";
        Logger.LogInformation("Loaded structure for view {Name}: {Count} columns", ObjectName, columns.Count);
    }

    private async Task LoadIndexStructureAsync(IDatabaseSession session)
    {
        var indexLiteral = "'" + ObjectName.Replace("'", "''") + "'";

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

        var result = await session.ExecuteQueryAsync(sql);

        if (string.IsNullOrEmpty(result.ErrorMessage) && result.Data != null && result.Data.Rows.Count > 0)
        {
            var list = new List<ColumnInfo>();

            foreach (DataRow row in result.Data.Rows)
            {
                var tableName = row[0] as string;
                var colName = row[1] as string ?? string.Empty;
                var ordinal = row[2] is int o ? o : 0;
                var isUniqueStr = row[3] as string;
                var filter = row[4] as string;
                var dataType = row[5] as string;

                if (string.IsNullOrEmpty(IndexTableName))
                    IndexTableName = tableName;

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
            ApplicationVm.MainWindowVm.StatusText = $"Loaded {Columns.Count} columns from index \"{ObjectName}\"";
            Logger.LogInformation("Loaded structure for index {Name}: {Count} columns", ObjectName, Columns.Count);
            return;
        }

        // Fallback to PRAGMA
        var pragmaResult = await session.ExecuteQueryAsync(
            $"PRAGMA index_info(\"{ObjectName.Replace("\"", "\"\"")}\")");

        if (!string.IsNullOrEmpty(pragmaResult.ErrorMessage) || pragmaResult.Data == null)
        {
            ErrorMessage = pragmaResult.ErrorMessage ?? result.ErrorMessage ?? "Failed to load index info";
            return;
        }

        var fallbackColumns = new List<ColumnInfo>();
        foreach (DataRow row in pragmaResult.Data.Rows)
        {
            var colName = row.ItemArray.Length > 2 ? row[2] as string ?? string.Empty : string.Empty;
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
        ApplicationVm.MainWindowVm.StatusText = $"Loaded {Columns.Count} columns from index \"{ObjectName}\"";
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        CanRefresh = !IsLoading && Session?.IsConnected == true;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((StructureTabViewModel vm) => vm.IsLoading))
            UpdateStatus();
    }

    protected override void OnSessionStatusChanged(bool isConnected)
    {
        UpdateStatus();
    }

    protected override void OnSessionChanged()
    {
        UpdateStatus();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Name of the object.
    /// </summary>
    public string ObjectName { get; }

    /// <summary>
    /// Type of the object.
    /// </summary>
    public DatabaseNodeType ObjectType { get; }

    /// <summary>
    /// Display name for the object type.
    /// </summary>
    public string ObjectTypeDisplay => ObjectType switch
    {
        DatabaseNodeType.Table => "Table",
        DatabaseNodeType.View => "View",
        DatabaseNodeType.Index => "Index",
        _ => "Object"
    };

    /// <summary>
    /// Column definitions.
    /// </summary>
    [Notify]
    public List<ColumnInfo> Columns { get; set; } = null!;

    /// <summary>
    /// Indicates if structure is being loaded.
    /// </summary>
    [Notify]
    public bool IsLoading { get; set; }

    /// <summary>
    /// Error message if loading failed.
    /// </summary>
    [Notify]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Indicates if structure can be refreshed.
    /// </summary>
    [Notify]
    public bool CanRefresh { get; private set; }

    /// <summary>
    /// View definition SQL (for views only).
    /// </summary>
    [Notify]
    public string? ViewDefinition { get; set; }

    /// <summary>
    /// Gets whether view has definition.
    /// </summary>
    public bool HasViewDefinition => !string.IsNullOrWhiteSpace(ViewDefinition);

    /// <summary>
    /// Index table name (for indexes only).
    /// </summary>
    [Notify]
    public string? IndexTableName { get; set; }

    /// <summary>
    /// Index is unique flag (for indexes only).
    /// </summary>
    [Notify]
    public bool? IndexIsUnique { get; set; }

    /// <summary>
    /// Index filter condition (for indexes only).
    /// </summary>
    [Notify]
    public string? IndexFilterCondition { get; set; }

    /// <summary>
    /// Gets whether index has additional details.
    /// </summary>
    public bool HasIndexDetails => !string.IsNullOrWhiteSpace(IndexTableName) ||
                                   IndexIsUnique is not null ||
                                   !string.IsNullOrWhiteSpace(IndexFilterCondition);

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;

    #endregion

    #region Services

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
