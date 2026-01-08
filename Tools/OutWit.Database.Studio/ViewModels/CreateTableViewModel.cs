using System.Collections.ObjectModel;
using System.Text;
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

    #region Events

    public event Action<bool> ShouldCloseDialog = delegate { };

    #endregion

    #region Constructors

    public CreateTableViewModel(ApplicationViewModel applicationVm)
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
        TableName = string.Empty;
        Columns = new ObservableCollection<ColumnDefinition>();
        AvailableDataTypes = DATA_TYPES;

        // Add first column by default
        AddColumn();
    }

    private void InitCommands()
    {
        AddColumnCommand = new RelayCommand(AddColumn);
        RemoveColumnCommand = new RelayCommand(RemoveColumn);
        GenerateDdlCommand = new RelayCommand(GenerateDdl);
        CreateTableCommand = new RelayCommandAsync(CreateTableAsync);
        CancelCommand = new RelayCommand(Cancel);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Command Functions

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
        
        UpdateStatus();
    }

    private void RemoveColumn()
    {
        if (SelectedColumn == null)
            return;

        Columns.Remove(SelectedColumn);
        
        UpdateStatus();
    }

    private void GenerateDdl()
    {
        GeneratedDdl = BuildCreateTableSql();
        Logger.LogInformation("Generated DDL for table {TableName}", TableName);
    }

    private async Task CreateTableAsync()
    {
        if (!CanCreateTable)
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var sql = BuildCreateTableSql();
            await Database.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = $"Created table: {TableName}";
            Logger.LogInformation("Created table: {TableName}", TableName);

            // Refresh explorer
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            // Close dialog (set by caller)
            ShouldCloseDialog(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create table: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error creating table";
            Logger.LogError(ex, "Failed to create table {TableName}", TableName);
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

    #region Tools

    private void UpdateStatus()
    {
        CanCreateTable = !string.IsNullOrWhiteSpace(TableName) && Columns.Count > 0
                                                               && !IsCreating
                                                               && Database.IsConnected;

        CanGenerateDdl = !string.IsNullOrWhiteSpace(TableName) && Columns.Count > 0;

        CanRemoveColumn = SelectedColumn != null && Columns.Count > 1;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.IsProperty((CreateTableViewModel vm) => vm.TableName))
            UpdateStatus();
    }

    #endregion

    #region Properties

    [Notify]
    public string TableName { get; set; } = null!;

    [Notify]
    public ObservableCollection<ColumnDefinition> Columns { get; private set; } = null!;

    [Notify]
    public ColumnDefinition? SelectedColumn { get; set; }

    [Notify]
    public string[] AvailableDataTypes { get; private set; } = null!;

    [Notify]
    public string? GeneratedDdl { get; set; }

    [Notify]
    public bool IsCreating { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool CanGenerateDdl { get; private set; }

    [Notify]
    public bool CanCreateTable { get; private set; }

    [Notify]
    public bool CanRemoveColumn { get; private set; }

    #endregion

    #region Commands

    public ICommand AddColumnCommand { get; private set; } = null!;

    public ICommand RemoveColumnCommand { get; private set; } = null!;

    public ICommand GenerateDdlCommand { get; private set; } = null!;

    public ICommand CreateTableCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
