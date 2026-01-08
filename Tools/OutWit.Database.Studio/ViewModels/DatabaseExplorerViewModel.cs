using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

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
        BrowseDataCommand = new RelayCommand(BrowseData, CanBrowseData);
        ViewDefinitionCommand = new RelayCommand(ViewDefinition, CanViewDefinition);
        DropObjectCommand = new RelayCommandAsync(DropObjectAsync, CanDropObject);
        CreateTableCommand = new RelayCommandAsync(CreateTableAsync, () => Database.IsConnected);
        CreateViewCommand = new RelayCommandAsync(CreateViewAsync, () => Database.IsConnected);
        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync, () => Database.IsConnected);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChangedInternal;
    }

    #endregion

    #region Functions

    private void BrowseData()
    {
        if (SelectedNode == null)
            return;

        var sql = $"SELECT * FROM {SelectedNode.Name} LIMIT 100";
        ApplicationVm.QueryEditorVm.SqlText = sql;
        ApplicationVm.QueryEditorVm.ExecuteCommand.Execute(null);

        Logger.LogInformation("Browse data for {ObjectName}", SelectedNode.Name);
    }

    private bool CanBrowseData()
    {
        return SelectedNode?.NodeType == DatabaseNodeType.Table 
            || SelectedNode?.NodeType == DatabaseNodeType.View;
    }

    private void ViewDefinition()
    {
        if (SelectedNode == null)
            return;

        var sql = SelectedNode.NodeType switch
        {
            DatabaseNodeType.View => $"SELECT sql FROM sqlite_master WHERE type='view' AND name='{SelectedNode.Name}'",
            DatabaseNodeType.Trigger => $"SELECT sql FROM sqlite_master WHERE type='trigger' AND name='{SelectedNode.Name}'",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(sql))
        {
            ApplicationVm.QueryEditorVm.SqlText = sql;
            ApplicationVm.QueryEditorVm.ExecuteCommand.Execute(null);
        }

        Logger.LogInformation("View definition for {ObjectName}", SelectedNode.Name);
    }

    private bool CanViewDefinition()
    {
        return SelectedNode?.NodeType == DatabaseNodeType.View 
            || SelectedNode?.NodeType == DatabaseNodeType.Trigger;
    }

    private async Task DropObjectAsync()
    {
        if (SelectedNode == null)
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

        var sql = $"DROP {objectType} IF EXISTS {SelectedNode.Name}";

        try
        {
            await Database.ExecuteNonQueryAsync(sql);
            await RefreshAsync();

            ApplicationVm.MainWindowVm.StatusText = $"Dropped {objectType.ToLower()}: {SelectedNode.Name}";
            Logger.LogInformation("Dropped {ObjectType}: {ObjectName}", objectType, SelectedNode.Name);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to drop {objectType.ToLower()}: {ex.Message}";
            Logger.LogError(ex, "Failed to drop {ObjectType}: {ObjectName}", objectType, SelectedNode.Name);
        }
    }

    private bool CanDropObject()
    {
        return SelectedNode?.NodeType == DatabaseNodeType.Table
            || SelectedNode?.NodeType == DatabaseNodeType.View
            || SelectedNode?.NodeType == DatabaseNodeType.Index
            || SelectedNode?.NodeType == DatabaseNodeType.Trigger
            || SelectedNode?.NodeType == DatabaseNodeType.Sequence;
    }

    private async Task CreateTableAsync()
    {
        var createTableVm = new CreateTableViewModel(ApplicationVm);

        var dialog = new Views.CreateTableDialog{DataContext = createTableVm};

        createTableVm.ShouldCloseDialog += success => { dialog.Close(success); };

        var result = await dialog.ShowDialog<bool?>(ApplicationVm.MainWindow!);
        
        if (result == true)
            Logger.LogInformation("Table created successfully");
        
    }

    private async Task CreateViewAsync()
    {
        var createViewVm = new CreateViewViewModel(ApplicationVm);

        var dialog = new Views.CreateViewDialog {DataContext = createViewVm};

        createViewVm.ShouldCloseDialog += success => { dialog.Close(success); };

        var result = await dialog.ShowDialog<bool?>(ApplicationVm.MainWindow!);
        
        if (result == true)
            Logger.LogInformation("View created successfully");
        
    }

    private async Task CreateIndexAsync()
    {
        var createIndexVm = new CreateIndexViewModel(ApplicationVm);
        
        // Load tables on dialog open
        createIndexVm.LoadTablesCommand.Execute(null);
        
        var dialog = new Views.CreateIndexDialog{DataContext = createIndexVm };

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
            var newNodes = new List<DatabaseNode>();

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
            rootNode.Children.Add(triggersFolder);

            // Sequences folder
            var sequencesFolder = new DatabaseNode
            {
                Name = "Sequences",
                NodeType = DatabaseNodeType.SequencesFolder
            };
            rootNode.Children.Add(sequencesFolder);

            newNodes.Add(rootNode);
            
            Logger.LogInformation("Setting Nodes collection with {Count} root nodes", newNodes.Count);
            Nodes = newNodes;
            Logger.LogInformation("Nodes.Count after assignment: {Count}", Nodes.Count);

            ApplicationVm.MainWindowVm.StatusText = $"Loaded: {tables.Count} tables, {views.Count} views, {indexes.Count} indexes";

            Logger.LogInformation("Database explorer refreshed: {Tables} tables, {Views} views, {Indexes} indexes",
                tables.Count, views.Count, indexes.Count);
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

    #region Event Handlers

    private void OnPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedNode))
            return;

        if (SelectedNode == null)
        {
            ApplicationVm.TableStructureVm.Clear();
            return;
        }

        if (SelectedNode.NodeType is DatabaseNodeType.Table or DatabaseNodeType.View or DatabaseNodeType.Index)
        {
            _ = ApplicationVm.TableStructureVm.LoadObjectStructureAsync(SelectedNode);
            return;
        }

        // Folders/others
        ApplicationVm.TableStructureVm.Clear();
    }

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand BrowseDataCommand { get; private set; } = null!;

    public ICommand ViewDefinitionCommand { get; private set; } = null!;

    public ICommand DropObjectCommand { get; private set; } = null!;

    public ICommand CreateTableCommand { get; private set; } = null!;

    public ICommand CreateViewCommand { get; private set; } = null!;

    public ICommand CreateIndexCommand { get; private set; } = null!;

    #endregion

    #region Properties

    [Notify]
    public List<DatabaseNode> Nodes { get; set; } = null!;

    [Notify]
    public DatabaseNode? SelectedNode { get; set; }

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
