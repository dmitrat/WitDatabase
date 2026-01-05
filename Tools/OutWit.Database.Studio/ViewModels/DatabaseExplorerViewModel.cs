using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for the database explorer tree.
/// </summary>
public class DatabaseExplorerViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ILogger<DatabaseExplorerViewModel> m_logger;
    private DatabaseNode? m_selectedNode;

    #endregion

    #region Constructors

    public DatabaseExplorerViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseExplorerViewModel>.Instance;

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
        RefreshCommand = new DelegateCommand<object>(async _ => await RefreshAsync());
        BrowseDataCommand = new DelegateCommand<object>(_ => BrowseData(), _ => CanBrowseData());
        ViewDefinitionCommand = new DelegateCommand<object>(_ => ViewDefinition(), _ => CanViewDefinition());
        DropObjectCommand = new DelegateCommand<object>(async _ => await DropObjectAsync(), _ => CanDropObject());
        CreateTableCommand = new DelegateCommand<object>(async _ => await CreateTableAsync(), _ => m_databaseService.IsConnected);
        CreateViewCommand = new DelegateCommand<object>(async _ => await CreateViewAsync(), _ => m_databaseService.IsConnected);
        CreateIndexCommand = new DelegateCommand<object>(async _ => await CreateIndexAsync(), _ => m_databaseService.IsConnected);
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChangedInternal;
    }

    #endregion

    #region Commands

    public DelegateCommand<object> RefreshCommand { get; private set; } = null!;
    public DelegateCommand<object> BrowseDataCommand { get; private set; } = null!;
    public DelegateCommand<object> ViewDefinitionCommand { get; private set; } = null!;
    public DelegateCommand<object> DropObjectCommand { get; private set; } = null!;
    public DelegateCommand<object> CreateTableCommand { get; private set; } = null!;
    public DelegateCommand<object> CreateViewCommand { get; private set; } = null!;
    public DelegateCommand<object> CreateIndexCommand { get; private set; } = null!;

    private void BrowseData()
    {
        if (SelectedNode == null)
            return;

        var sql = $"SELECT * FROM {SelectedNode.Name} LIMIT 100";
        ApplicationVm.QueryEditorVm.SqlText = sql;
        ApplicationVm.QueryEditorVm.ExecuteCommand.Execute(null);

        m_logger.LogInformation("Browse data for {ObjectName}", SelectedNode.Name);
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

        m_logger.LogInformation("View definition for {ObjectName}", SelectedNode.Name);
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
            await m_databaseService.ExecuteNonQueryAsync(sql);
            await RefreshAsync();

            ApplicationVm.MainWindowVm.StatusText = $"Dropped {objectType.ToLower()}: {SelectedNode.Name}";
            m_logger.LogInformation("Dropped {ObjectType}: {ObjectName}", objectType, SelectedNode.Name);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to drop {objectType.ToLower()}: {ex.Message}";
            m_logger.LogError(ex, "Failed to drop {ObjectType}: {ObjectName}", objectType, SelectedNode.Name);
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
        var createTableVm = new CreateTableViewModel(ApplicationVm, m_databaseService);
        var dialog = new Views.CreateTableDialog(createTableVm);
        
        var result = await dialog.ShowDialog<bool?>(ApplicationVm.MainWindow);
        
        if (result == true)
        {
            m_logger.LogInformation("Table created successfully");
        }
    }

    private async Task CreateViewAsync()
    {
        var createViewVm = new CreateViewViewModel(ApplicationVm, m_databaseService);
        var dialog = new Views.CreateViewDialog(createViewVm);
        
        var result = await dialog.ShowDialog<bool?>(ApplicationVm.MainWindow);
        
        if (result == true)
        {
            m_logger.LogInformation("View created successfully");
        }
    }

    private async Task CreateIndexAsync()
    {
        var createIndexVm = new CreateIndexViewModel(ApplicationVm, m_databaseService);
        
        // Load tables on dialog open
        createIndexVm.LoadTablesCommand.Execute(null);
        
        var dialog = new Views.CreateIndexDialog(createIndexVm);
        
        var result = await dialog.ShowDialog<bool?>(ApplicationVm.MainWindow);
        
        if (result == true)
        {
            m_logger.LogInformation("Index created successfully");
        }
    }

    public async Task RefreshAsync()
    {
        m_logger.LogInformation("RefreshAsync called. IsConnected: {IsConnected}", m_databaseService.IsConnected);
        
        if (!m_databaseService.IsConnected)
        {
            m_logger.LogWarning("Not connected to database, clearing nodes");
            Nodes.Clear();
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            m_logger.LogInformation("Starting schema load...");
            var newNodes = new List<DatabaseNode>();

            // Create root node
            var dbName = Path.GetFileNameWithoutExtension(m_databaseService.CurrentConnection?.FilePath ?? "Database");
            m_logger.LogInformation("Database name: {DbName}", dbName);
            
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

            m_logger.LogInformation("Loading tables...");
            var tables = await m_databaseService.GetTablesAsync();
            m_logger.LogInformation("Loaded {Count} tables", tables.Count);
            
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

            m_logger.LogInformation("Loading views...");
            var views = await m_databaseService.GetViewsAsync();
            m_logger.LogInformation("Loaded {Count} views", views.Count);
            
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

            m_logger.LogInformation("Loading indexes...");
            var indexes = await m_databaseService.GetIndexesAsync();
            m_logger.LogInformation("Loaded {Count} indexes", indexes.Count);
            
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
            
            m_logger.LogInformation("Setting Nodes collection with {Count} root nodes", newNodes.Count);
            Nodes = newNodes;
            m_logger.LogInformation("Nodes.Count after assignment: {Count}", Nodes.Count);

            ApplicationVm.MainWindowVm.StatusText = $"Loaded: {tables.Count} tables, {views.Count} views, {indexes.Count} indexes";

            m_logger.LogInformation("Database explorer refreshed: {Tables} tables, {Views} views, {Indexes} indexes",
                tables.Count, views.Count, indexes.Count);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load schema: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Error loading schema";
            m_logger.LogError(ex, "Failed to refresh database explorer");
        }
        finally
        {
            IsLoading = false;
            m_logger.LogInformation("RefreshAsync completed. IsLoading: {IsLoading}, Nodes.Count: {NodesCount}", 
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

    #region Properties

    [Notify]
    public List<DatabaseNode> Nodes { get; set; } = null!;

    public DatabaseNode? SelectedNode
    {
        get => m_selectedNode;
        set
        {
            if (m_selectedNode != value)
            {
                m_selectedNode = value;
                OnPropertyChanged(nameof(SelectedNode));
            }
        }
    }

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    #endregion
}
