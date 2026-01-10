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
        Columns = [];
        AvailableDataTypes = DATA_TYPES;

        // Add first column by default
        AddColumn();
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        Columns.CollectionChanged += OnColumnsCollectionChanged;
    }

    private void InitCommands()
    {
        AddColumnCommand = new RelayCommand(AddColumn);
        RemoveColumnCommand = new RelayCommand(RemoveColumn);
        GenerateDdlCommand = new RelayCommand(GenerateDdl);
        CreateTableCommand = new RelayCommandAsync(CreateTableAsync);
        CancelCommand = new RelayCommand(Cancel);
    }

    #endregion

    #region Functions

    private void AddColumn()
    {
        var column = new ColumnDefinition
        {
            Name = $"Column{Columns.Count + 1}",
            DataType = "TEXT",
            IsNullable = true
        };

        // Auto-set first column as PK if none exists
        if (Columns.Count == 0)
        {
            column.Name = "Id";
            column.DataType = "BIGINT";
            column.IsPrimaryKey = true;
            column.IsNullable = false;
            column.IsAutoIncrement = true;
        }

        Columns.Add(column);
    }

    private void RemoveColumn()
    {
        if (SelectedColumn == null || !CanRemoveColumn)
            return;

        Columns.Remove(SelectedColumn);
        SelectedColumn = Columns.LastOrDefault();
    }

    private void GenerateDdl()
    {
        if (!CanGenerateDdl)
            return;

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

            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

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

            if (col.IsPrimaryKey && pkColumns.Count == 0)
            {
                colDef.Append(" PRIMARY KEY");
                
                if (col.IsAutoIncrement)
                    colDef.Append(" AUTOINCREMENT");
                
                pkColumns.Add(col.Name);
            }
            else if (col.IsPrimaryKey)
            {
                pkColumns.Add(col.Name);
            }

            if (!col.IsNullable)
                colDef.Append(" NOT NULL");

            if (col.IsUnique && !col.IsPrimaryKey)
                colDef.Append(" UNIQUE");

            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
                colDef.Append($" DEFAULT {col.DefaultValue}");

            if (!string.IsNullOrWhiteSpace(col.CheckConstraint))
                colDef.Append($" CHECK ({col.CheckConstraint})");

            columnDefs.Add(colDef.ToString());
        }

        sb.AppendLine(string.Join(",\n", columnDefs));

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
        var hasTableName = !string.IsNullOrWhiteSpace(TableName);
        var hasColumns = Columns.Count > 0;

        CanCreateTable = hasTableName && hasColumns && !IsCreating && Database.IsConnected;
        CanGenerateDdl = hasTableName && hasColumns;
        CanRemoveColumn = SelectedColumn != null && Columns.Count > 1;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((CreateTableViewModel vm) => vm.TableName))
            UpdateStatus();

        if (e.IsProperty((CreateTableViewModel vm) => vm.SelectedColumn))
            UpdateStatus();

        if (e.IsProperty((CreateTableViewModel vm) => vm.IsCreating))
            UpdateStatus();
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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
