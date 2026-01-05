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
/// ViewModel for creating a new table.
/// </summary>
public class CreateTableViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    private static readonly string[] DATA_TYPES =
    [
        "TEXT", "VARCHAR(255)", "CHAR(50)",
        "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT",
        "FLOAT", "DOUBLE", "DECIMAL(18,2)", "NUMERIC",
        "BOOLEAN", "BOOL",
        "DATE", "TIME", "DATETIME", "TIMESTAMP",
        "GUID", "UUID",
        "BLOB", "BINARY", "VARBINARY",
        "JSON"
    ];

    #endregion

    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ILogger<CreateTableViewModel> m_logger;

    #endregion

    #region Constructors

    public CreateTableViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateTableViewModel>.Instance;

        InitDefault();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        TableName = string.Empty;
        Columns = new ObservableCollection<ColumnDefinition>();
        AvailableDataTypes = DATA_TYPES;
    }

    private void InitCommands()
    {
        AddColumnCommand = new DelegateCommand<object>(_ => AddColumn());
        RemoveColumnCommand = new DelegateCommand<ColumnDefinition>(RemoveColumn, CanRemoveColumn);
        GenerateDdlCommand = new DelegateCommand<object>(_ => GenerateDdl(), _ => CanGenerateDdl());
        CreateTableCommand = new DelegateCommand<object>(async _ => await CreateTableAsync(), _ => CanCreateTable());
        CancelCommand = new DelegateCommand<object>(_ => Cancel());
        
        // Subscribe to PropertyChanged to update CanExecute
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TableName))
            {
                GenerateDdlCommand.RaiseCanExecuteChanged();
                CreateTableCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanGenerateDdlProperty));
                OnPropertyChanged(nameof(CanCreateTableProperty));
            }
        };
        
        // Add first column by default after commands are initialized
        AddColumn();
    }

    #endregion

    #region Commands

    public DelegateCommand<object> AddColumnCommand { get; private set; } = null!;
    public DelegateCommand<ColumnDefinition> RemoveColumnCommand { get; private set; } = null!;
    public DelegateCommand<object> GenerateDdlCommand { get; private set; } = null!;
    public DelegateCommand<object> CreateTableCommand { get; private set; } = null!;
    public DelegateCommand<object> CancelCommand { get; private set; } = null!;

    private void AddColumn()
    {
        var column = new ColumnDefinition
        {
            Name = $"Column{Columns.Count + 1}",
            DataType = "TEXT",
            IsNullable = true
        };

        Columns.Add(column);
        
        // Auto-set first column as PK if none exists
        if (Columns.Count == 1)
        {
            column.Name = "Id";
            column.DataType = "BIGINT";
            column.IsPrimaryKey = true;
            column.IsNullable = false;
            column.IsAutoIncrement = true;
        }

        RemoveColumnCommand.RaiseCanExecuteChanged();
        GenerateDdlCommand.RaiseCanExecuteChanged();
        CreateTableCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanGenerateDdlProperty));
        OnPropertyChanged(nameof(CanCreateTableProperty));
    }

    private void RemoveColumn(ColumnDefinition? column)
    {
        if (column == null)
            return;

        Columns.Remove(column);
        
        RemoveColumnCommand.RaiseCanExecuteChanged();
        GenerateDdlCommand.RaiseCanExecuteChanged();
        CreateTableCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanGenerateDdlProperty));
        OnPropertyChanged(nameof(CanCreateTableProperty));
    }

    private bool CanRemoveColumn(ColumnDefinition? column)
    {
        return column != null && Columns.Count > 1;
    }

    private void GenerateDdl()
    {
        GeneratedDdl = BuildCreateTableSql();
        m_logger.LogInformation("Generated DDL for table {TableName}", TableName);
    }

    private bool CanGenerateDdl()
    {
        return !string.IsNullOrWhiteSpace(TableName) && Columns.Count > 0;
    }

    private async Task CreateTableAsync()
    {
        if (!CanCreateTable())
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var sql = BuildCreateTableSql();
            await m_databaseService.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = $"Created table: {TableName}";
            m_logger.LogInformation("Created table: {TableName}", TableName);

            // Refresh explorer
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            // Close dialog (set by caller)
            IsCompleted = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create table: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error creating table";
            m_logger.LogError(ex, "Failed to create table {TableName}", TableName);
        }
        finally
        {
            IsCreating = false;
        }
    }

    private bool CanCreateTable()
    {
        return !string.IsNullOrWhiteSpace(TableName) 
            && Columns.Count > 0 
            && !IsCreating
            && m_databaseService.IsConnected;
    }

    private void Cancel()
    {
        IsCancelled = true;
    }

    #endregion

    #region Build DDL

    private string BuildCreateTableSql()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {TableName} (");

        var columnDefs = new List<string>();
        var pkColumns = new List<string>();

        foreach (var col in Columns)
        {
            if (string.IsNullOrWhiteSpace(col.Name))
                continue;

            var colDef = new StringBuilder();
            colDef.Append($"    {col.Name} {col.DataType}");

            // PRIMARY KEY must come first (for single-column PK)
            if (col.IsPrimaryKey && pkColumns.Count == 0)
            {
                colDef.Append(" PRIMARY KEY");
                
                // AUTOINCREMENT must come immediately after PRIMARY KEY
                if (col.IsAutoIncrement)
                    colDef.Append(" AUTOINCREMENT");
                
                pkColumns.Add(col.Name);
            }
            else if (col.IsPrimaryKey)
            {
                // Multi-column PK - will be added as table constraint
                pkColumns.Add(col.Name);
            }

            // NOT NULL
            if (!col.IsNullable)
                colDef.Append(" NOT NULL");

            // UNIQUE
            if (col.IsUnique && !col.IsPrimaryKey)
                colDef.Append(" UNIQUE");

            // DEFAULT
            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
                colDef.Append($" DEFAULT {col.DefaultValue}");

            // CHECK
            if (!string.IsNullOrWhiteSpace(col.CheckConstraint))
                colDef.Append($" CHECK ({col.CheckConstraint})");

            columnDefs.Add(colDef.ToString());
        }

        sb.AppendLine(string.Join(",\n", columnDefs));

        // Add PRIMARY KEY constraint if multiple columns
        if (pkColumns.Count > 1)
        {
            sb.AppendLine($",\n    PRIMARY KEY ({string.Join(", ", pkColumns)})");
        }

        sb.AppendLine(");");

        return sb.ToString();
    }

    #endregion

    #region Public Methods

    public void Reset()
    {
        TableName = string.Empty;
        Columns.Clear();
        AddColumn();
        GeneratedDdl = null;
        ErrorMessage = null;
        IsCompleted = false;
        IsCancelled = false;
    }

    #endregion

    #region Properties

    [Notify]
    public string TableName { get; set; } = null!;

    public ObservableCollection<ColumnDefinition> Columns { get; private set; } = null!;

    public string[] AvailableDataTypes { get; private set; } = null!;

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

    // Computed properties for UI binding
    public bool CanGenerateDdlProperty => CanGenerateDdl();
    public bool CanCreateTableProperty => CanCreateTable();

    #endregion
}
