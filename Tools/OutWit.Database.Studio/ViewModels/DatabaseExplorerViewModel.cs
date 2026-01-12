using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.ViewModels.Tabs;
using OutWit.Database.Studio.Views.Dialogs;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for the database explorer tree.
/// </summary>
public class DatabaseExplorerViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public DatabaseExplorerViewModel(ApplicationViewModel applicationVm)
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
        Nodes = [];
    }

    private void InitCommands()
    {
        RefreshCommand = new RelayCommandAsync(RefreshAsync);
        SelectTop100Command = new RelayCommand(SelectTop100);
        SelectTop1000Command = new RelayCommand(SelectTop1000);
        EditDataCommand = new RelayCommandAsync(EditDataAsync);
        ViewStructureCommand = new RelayCommandAsync(ViewStructureAsync);
        ViewDefinitionCommand = new RelayCommandAsync(ViewDefinitionAsync);
        DropObjectCommand = new RelayCommandAsync(DropObjectAsync);
        CreateTableCommand = new RelayCommandAsync(CreateTableAsync);
        CreateViewCommand = new RelayCommandAsync(CreateViewAsync);
        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChangedInternal;
    }

    #endregion

    #region Functions

    private void SelectTop100()
    {
        SelectTopRows(100);
    }

    private void SelectTop1000()
    {
        SelectTopRows(1000);
    }

    private void SelectTopRows(int limit)
    {
        if (SelectedNode == null || !CanBrowseData)
            return;

        var tableName = SelectedNode.Name;
        var sql = $"SELECT * FROM [{tableName}] LIMIT {limit}";
        
        // Use new WorkspaceTabsViewModel
        var tab = ApplicationVm.WorkspaceTabsVm.OpenQueryTab(sql, $"{tableName} - Top {limit}");
        
        // Execute the query
        ApplicationVm.WorkspaceTabsVm.ExecuteQueryCommand.Execute(null);

        Logger.LogInformation("Select top {Limit} from {ObjectName}", limit, SelectedNode.Name);
    }

    private async Task EditDataAsync()
    {
        if (SelectedNode == null || !CanEditData)
            return;

        var tableName = SelectedNode.Name;
        
        // Use new WorkspaceTabsViewModel to open table edit tab
        await ApplicationVm.WorkspaceTabsVm.OpenTableEditTabAsync(tableName);
        
        ApplicationVm.MainWindowVm.StatusText = $"Editing table: {tableName}";
        Logger.LogInformation("Edit data for table {TableName}", tableName);
    }

    private async Task ViewStructureAsync()
    {
        if (SelectedNode == null || !CanViewStructure)
            return;

        // Use new WorkspaceTabsViewModel to open structure tab
        await ApplicationVm.WorkspaceTabsVm.OpenStructureTabAsync(SelectedNode.Name, SelectedNode.NodeType);
        
        Logger.LogInformation("View structure for {ObjectType} {ObjectName}", SelectedNode.NodeType, SelectedNode.Name);
    }

    private async Task ViewDefinitionAsync()
    {
        if (SelectedNode == null || !CanViewDefinition)
            return;

        string? definition = null;
        var objectType = SelectedNode.NodeType;

        try
        {
            definition = objectType switch
            {
                DatabaseNodeType.Table => await Database.GetTableDefinitionAsync(SelectedNode.Name),
                DatabaseNodeType.View => await Database.GetViewDefinitionAsync(SelectedNode.Name),
                DatabaseNodeType.Trigger => await Database.GetTriggerDefinitionAsync(SelectedNode.Name),
                DatabaseNodeType.Index => await Database.GetIndexDefinitionAsync(SelectedNode.Name),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get definition for {ObjectName}", SelectedNode.Name);
            ApplicationVm.MainWindowVm.StatusText = $"Failed to get definition: {ex.Message}";
            return;
        }

        if (string.IsNullOrEmpty(definition))
        {
            ApplicationVm.MainWindowVm.StatusText = $"No definition found for {SelectedNode.Name}";
            return;
        }

        // Open definition in a new query tab using new WorkspaceTabsViewModel
        var sql = $"-- Definition for {objectType}: {SelectedNode.Name}\n\n{definition}";
        ApplicationVm.WorkspaceTabsVm.OpenQueryTab(sql, $"{SelectedNode.Name} - Definition");

        Logger.LogInformation("Viewed definition for {ObjectName}", SelectedNode.Name);
    }

    private async Task DropObjectAsync()
    {
        if (SelectedNode == null || !CanDropObject)
            return;

        var objectType = SelectedNode.NodeType switch
        {
            DatabaseNodeType.Table => "TABLE",
            DatabaseNodeType.View => "VIEW",
            DatabaseNodeType.Index => "INDEX",
            DatabaseNodeType.Trigger => "TRIGGER",
            DatabaseNodeType.Sequence => "SEQUENCE",
            _ => null
        };

        if (objectType == null)
            return;

        var objectName = SelectedNode.Name;
        var sql = $"DROP {objectType} IF EXISTS [{objectName}]";

        try
        {
            await Database.ExecuteNonQueryAsync(sql);
            
            // Clear selection before refresh to avoid stale reference
            SelectedNode = null;
            
            await RefreshAsync();

            ApplicationVm.MainWindowVm.StatusText = $"Dropped {objectType.ToLower()}: {objectName}";
            Logger.LogInformation("Dropped {ObjectType}: {ObjectName}", objectType, objectName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to drop {objectType.ToLower()}: {ex.Message}";
            Logger.LogError(ex, "Failed to drop {ObjectType}: {ObjectName}", objectType, objectName);
        }
    }

    private async Task CreateTableAsync()
    {
        if (!Database.IsConnected)
            return;

        var createTableVm = new CreateTableViewModel(ApplicationVm);

        var dialog = new CreateTableDialog { DataContext = createTableVm };

        createTableVm.ShouldCloseDialog += success => { dialog.Close(success); };

        var result = await dialog.ShowDialog<bool?>(ApplicationVm.MainWindow!);
        
        if (result == true)
            Logger.LogInformation("Table created successfully");
    }

    private async Task CreateViewAsync()
    {
        if (!Database.IsConnected)
            return;

        var createViewVm = new CreateViewViewModel(ApplicationVm);

        var dialog = new CreateViewDialog { DataContext = createViewVm };

        createViewVm.ShouldCloseDialog += success => { dialog.Close(success); };

        var result = await dialog.ShowDialog<bool?>(ApplicationVm.MainWindow!);
        
        if (result == true)
            Logger.LogInformation("View created successfully");
    }

    private async Task CreateIndexAsync()
    {
        if (!Database.IsConnected)
            return;

        var createIndexVm = new CreateIndexViewModel(ApplicationVm);
        
        // Load tables on dialog open
        createIndexVm.LoadTablesCommand.Execute(null);
        
        var dialog = new CreateIndexDialog { DataContext = createIndexVm };

        createIndexVm.ShouldCloseDialog += success => { dialog.Close(success); };

        var result = await dialog.ShowDialog<bool?>(ApplicationVm.MainWindow!);
        
        if (result == true)
            Logger.LogInformation("Index created successfully");
    }

    public async Task RefreshAsync()
    {
        Logger.LogInformation("RefreshAsync called. IsConnected: {IsConnected}", Database.IsConnected);
        
        if (!Database.IsConnected)
        {
            Logger.LogWarning("Not connected to database, clearing nodes");
            Nodes.Clear();
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Logger.LogInformation("Starting schema load...");

            // Create root node
            var dbName = Path.GetFileNameWithoutExtension(Database.CurrentConnection?.FilePath ?? "Database");
            Logger.LogInformation("Database name: {DbName}", dbName);
            
            var rootNode = new DatabaseNode
            {
                Name = dbName,
                NodeType = DatabaseNodeType.Database,
                IsExpanded = true
            };

            // Tables folder
            var tablesFolder = new DatabaseNode
            {
                Name = "Tables",
                NodeType = DatabaseNodeType.TablesFolder,
                IsExpanded = true
            };

            Logger.LogInformation("Loading tables...");
            var tables = await Database.GetTablesAsync();
            Logger.LogInformation("Loaded {Count} tables", tables.Count);
            
            foreach (var table in tables)
            {
                tablesFolder.Children.Add(new DatabaseNode
                {
                    Name = table.Name,
                    NodeType = DatabaseNodeType.Table
                });
            }
            rootNode.Children.Add(tablesFolder);

            // Views folder
            var viewsFolder = new DatabaseNode
            {
                Name = "Views",
                NodeType = DatabaseNodeType.ViewsFolder
            };

            Logger.LogInformation("Loading views...");
            var views = await Database.GetViewsAsync();
            Logger.LogInformation("Loaded {Count} views", views.Count);
            
            foreach (var view in views)
            {
                viewsFolder.Children.Add(new DatabaseNode
                {
                    Name = view,
                    NodeType = DatabaseNodeType.View
                });
            }
            rootNode.Children.Add(viewsFolder);

            // Indexes folder
            var indexesFolder = new DatabaseNode
            {
                Name = "Indexes",
                NodeType = DatabaseNodeType.IndexesFolder
            };

            Logger.LogInformation("Loading indexes...");
            var indexes = await Database.GetIndexesAsync();
            Logger.LogInformation("Loaded {Count} indexes", indexes.Count);
            
            foreach (var index in indexes)
            {
                indexesFolder.Children.Add(new DatabaseNode
                {
                    Name = index,
                    NodeType = DatabaseNodeType.Index
                });
            }
            rootNode.Children.Add(indexesFolder);

            // Triggers folder
            var triggersFolder = new DatabaseNode
            {
                Name = "Triggers",
                NodeType = DatabaseNodeType.TriggersFolder
            };

            Logger.LogInformation("Loading triggers...");
            var triggers = await Database.GetTriggersAsync();
            Logger.LogInformation("Loaded {Count} triggers", triggers.Count);

            foreach (var trigger in triggers)
            {
                triggersFolder.Children.Add(new DatabaseNode
                {
                    Name = trigger,
                    NodeType = DatabaseNodeType.Trigger
                });
            }
            rootNode.Children.Add(triggersFolder);

            // Sequences folder
            var sequencesFolder = new DatabaseNode
            {
                Name = "Sequences",
                NodeType = DatabaseNodeType.SequencesFolder
            };

            Logger.LogInformation("Loading sequences...");
            var sequences = await Database.GetSequencesAsync();
            Logger.LogInformation("Loaded {Count} sequences", sequences.Count);

            foreach (var sequence in sequences)
            {
                sequencesFolder.Children.Add(new DatabaseNode
                {
                    Name = sequence,
                    NodeType = DatabaseNodeType.Sequence
                });
            }
            rootNode.Children.Add(sequencesFolder);

            Nodes.Clear();
            Nodes.Add(rootNode);
            
            Logger.LogInformation("Nodes updated. Count: {Count}", Nodes.Count);

            ApplicationVm.MainWindowVm.StatusText = $"Loaded: {tables.Count} tables, {views.Count} views, {indexes.Count} indexes, {triggers.Count} triggers, {sequences.Count} sequences";

            Logger.LogInformation("Database explorer refreshed: {Tables} tables, {Views} views, {Indexes} indexes, {Triggers} triggers, {Sequences} sequences",
                tables.Count, views.Count, indexes.Count, triggers.Count, sequences.Count);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load schema: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error loading schema";
            Logger.LogError(ex, "Failed to refresh database explorer");
        }
        finally
        {
            IsLoading = false;
            Logger.LogInformation("RefreshAsync completed. IsLoading: {IsLoading}, Nodes.Count: {NodesCount}", 
                IsLoading, Nodes.Count);
        }
    }

    #endregion

    #region Tools

    /// <summary>
    /// Quotes an identifier using double quotes (SQL standard).
    /// WitSql also supports square brackets [] and backticks ``.
    /// </summary>
    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private void UpdateCommandStates()
    {
        var nodeType = SelectedNode?.NodeType;

        CanBrowseData = nodeType == DatabaseNodeType.Table || nodeType == DatabaseNodeType.View;
        CanEditData = nodeType == DatabaseNodeType.Table;
        CanViewStructure = nodeType == DatabaseNodeType.Table
                        || nodeType == DatabaseNodeType.View 
                        || nodeType == DatabaseNodeType.Index;
        CanViewDefinition = nodeType == DatabaseNodeType.Table
                         || nodeType == DatabaseNodeType.View 
                         || nodeType == DatabaseNodeType.Trigger 
                         || nodeType == DatabaseNodeType.Index;
        CanDropObject = nodeType == DatabaseNodeType.Table
                     || nodeType == DatabaseNodeType.View
                     || nodeType == DatabaseNodeType.Index
                     || nodeType == DatabaseNodeType.Trigger
                     || nodeType == DatabaseNodeType.Sequence;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedNode))
            return;

        UpdateCommandStates();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Observable collection of database nodes for the tree view.
    /// </summary>
    public ObservableCollection<DatabaseNode> Nodes { get; private set; } = null!;

    [Notify]
    public DatabaseNode? SelectedNode { get; set; }

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool CanBrowseData { get; private set; }

    [Notify]
    public bool CanEditData { get; private set; }

    [Notify]
    public bool CanViewStructure { get; private set; }

    [Notify]
    public bool CanViewDefinition { get; private set; }

    [Notify]
    public bool CanDropObject { get; private set; }

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand SelectTop100Command { get; private set; } = null!;

    public ICommand SelectTop1000Command { get; private set; } = null!;

    public ICommand EditDataCommand { get; private set; } = null!;

    public ICommand ViewStructureCommand { get; private set; } = null!;

    public ICommand ViewDefinitionCommand { get; private set; } = null!;

    public ICommand DropObjectCommand { get; private set; } = null!;

    public ICommand CreateTableCommand { get; private set; } = null!;

    public ICommand CreateViewCommand { get; private set; } = null!;

    public ICommand CreateIndexCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
