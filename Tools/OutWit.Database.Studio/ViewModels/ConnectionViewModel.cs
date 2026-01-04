using System.ComponentModel;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Core.Builder;
using Microsoft.Extensions.Logging;
using OutWit.Common.Utils;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for the database connection dialog.
/// </summary>
public class ConnectionViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ISettingsService m_settingsService;
    private readonly ILogger<ConnectionViewModel> m_logger;

    #endregion

    #region Constructors

    public ConnectionViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService,
        ISettingsService settingsService)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_settingsService = settingsService;
        m_logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectionViewModel>.Instance;

        InitDefault();
        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        ConnectionInfo = new ConnectionInfo();
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;

        StorageEngines = ["btree", "lsm"];
        SelectedStorageEngine = "btree";
        
        // Set default page size to 4096
        SelectedPageSize = 4096;
        
        // Initialize page size options
        PageSizeOptions = [512, 1024, 2048, 4096, 8192, 16384, 32768];

        UseAutoDetectedSettings = true;
    }

    private void InitCommands()
    {
        BrowseFileCommand = new DelegateCommand<object>(_ => BrowseFile());
        ConnectCommand = new DelegateCommand<object>(async _ => await ConnectAsync(), _ => CanConnect());
        CancelCommand = new DelegateCommand<object>(_ => Cancel());
    }

    private void InitEvents()
    {
        this.PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Commands

    public DelegateCommand<object> BrowseFileCommand { get; private set; } = null!;
    public DelegateCommand<object> ConnectCommand { get; private set; } = null!;
    public DelegateCommand<object> CancelCommand { get; private set; } = null!;

    private void BrowseFile()
    {
        var mainWindow = System.Linq.Enumerable.FirstOrDefault(
            Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
                ? desktop.Windows 
                : System.Array.Empty<Avalonia.Controls.Window>());

        if (mainWindow != null)
        {
            var storageProvider = mainWindow.StorageProvider;
            _ = BrowseFileAsync(storageProvider);
        }
    }

    private async Task BrowseFileAsync(Avalonia.Platform.Storage.IStorageProvider storageProvider)
    {
        if (IsNewDatabase)
        {
            // For new database - use Save dialog
            var saveOptions = new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Create New Database",
                DefaultExtension = ".witdb",
                SuggestedFileName = "database.witdb",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("WitDatabase Files")
                    {
                        Patterns = new[] { "*.witdb" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            };

            var file = await storageProvider.SaveFilePickerAsync(saveOptions);
            
            if (file != null)
            {
                ConnectionInfo.FilePath = file.Path.LocalPath;
            }
        }
        else
        {
            // For open database - use Open dialog
            var openOptions = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Open Database",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("WitDatabase Files")
                    {
                        Patterns = new[] { "*.witdb", "*.db" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            };

            var files = await storageProvider.OpenFilePickerAsync(openOptions);
            
            if (files.Count > 0)
            {
                var filePath = files[0].Path.LocalPath;
                ConnectionInfo.FilePath = filePath;

                // Auto-detect settings from existing database
                if (UseAutoDetectedSettings && File.Exists(filePath))
                {
                    try
                    {
                        var dbInfo = WitDatabase.GetDatabaseInfo(filePath);
                        if (dbInfo != null)
                        {
                            ConnectionInfo.IsEncrypted = dbInfo.RequiresPassword;
                            
                            // Set storage engine from detected store type
                            if (!string.IsNullOrEmpty(dbInfo.StoreType))
                            {
                                SelectedStorageEngine = dbInfo.StoreType.ToLowerInvariant();
                            }

                            // Configure features
                            // If encrypted, we can't read features reliably, keep user's defaults
                            if (!dbInfo.RequiresPassword)
                            {
                                EnableTransactions = dbInfo.HasTransactions;
                                EnableMvcc = dbInfo.HasMvcc;
                                EnableFileLocking = dbInfo.HasFileLocking;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors during detection
                    }
                }

                ConnectCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task ConnectAsync()
    {
        IsConnecting = true;
        ErrorMessage = null;

        try
        {
            ConnectionInfo.StorageEngine = SelectedStorageEngine;

            // Build connection with advanced settings if creating new database
            if (IsNewDatabase)
            {
                // Validate file path for file-based database
                if (IsFileBased && string.IsNullOrWhiteSpace(ConnectionInfo.FilePath))
                {
                    ErrorMessage = "Please specify a database file path.";
                    return;
                }

                // Create new database with WitDatabaseBuilder
                var builder = new WitDatabaseBuilder();
                
                // Storage
                if (IsFileBased)
                {
                    builder.WithFilePath(ConnectionInfo.FilePath!);
                }
                else
                {
                    builder.WithMemoryStorage();
                    // For in-memory database, set a display name
                    ConnectionInfo.FilePath = ":memory:";
                }
                
                // Storage engine
                if (SelectedStorageEngine == "btree")
                {
                    builder.WithBTree();
                }
                else if (SelectedStorageEngine == "lsm")
                {
                    if (IsFileBased)
                    {
                        var directory = Path.GetDirectoryName(ConnectionInfo.FilePath) ?? ".";
                        builder.WithLsmTree(directory);
                    }
                    else
                    {
                        builder.WithLsmTree(".");
                    }
                }
                
                // Encryption
                if (ConnectionInfo.IsEncrypted && !string.IsNullOrEmpty(ConnectionInfo.Password))
                {
                    builder.WithEncryption(ConnectionInfo.Password);
                }
                
                // Advanced settings
                builder.WithPageSize(SelectedPageSize);
                builder.WithCacheSize(CacheSize);
                
                if (EnableTransactions)
                {
                    if (EnableMvcc)
                    {
                        builder.WithMvcc();
                    }
                    else
                    {
                        builder.WithTransactions();
                    }
                }
                else
                {
                    builder.WithoutTransactions();
                }
                
                if (EnableFileLocking && IsFileBased)
                {
                    builder.WithFileLocking();
                }
                else
                {
                    builder.WithoutFileLocking();
                }
                
                // Build and immediately dispose (just create the file)
                using (var db = builder.Build())
                {
                    // Database file created with settings
                    // Ensure all changes are flushed to disk
                }
                
                // Give the system time to release file locks
                await Task.Delay(100, CancellationToken.None);
                
                m_logger.LogInformation("Database file created: {FilePath}", ConnectionInfo.FilePath);
            }

            // Connect to the database (both for new and existing)
            m_logger.LogInformation("Attempting to connect to database: {FilePath}", ConnectionInfo.FilePath);
            var success = await m_databaseService.ConnectAsync(ConnectionInfo);
            
            if (success)
            {
                if (IsFileBased && !string.IsNullOrWhiteSpace(ConnectionInfo.FilePath) && ConnectionInfo.FilePath != ":memory:")
                {
                    await m_settingsService.AddRecentFileAsync(ConnectionInfo.FilePath);
                }
                
                SelectedConnection = ConnectionInfo;
                DialogResult = true;
                
                CloseDialog();
                
                m_logger.LogInformation("Successfully connected to {FilePath}", ConnectionInfo.FilePath);
            }
            else
            {
                ErrorMessage = "Failed to connect to database. Check file path and credentials.";
                m_logger.LogWarning("Connection failed for {FilePath}", ConnectionInfo.FilePath);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
            m_logger.LogError(ex, "Connection error");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnect()
    {
        // For in-memory database, no file path is needed
        if (IsNewDatabase && !IsFileBased)
            return !IsConnecting && (!ConnectionInfo.IsEncrypted || !string.IsNullOrWhiteSpace(ConnectionInfo.Password));

        // For file-based database, file path is required
        return !string.IsNullOrWhiteSpace(ConnectionInfo?.FilePath) 
            && !IsConnecting
            && (!ConnectionInfo.IsEncrypted || !string.IsNullOrWhiteSpace(ConnectionInfo.Password));
    }

    private void Cancel()
    {
        DialogResult = false;
        CloseDialog();
    }

    private void CloseDialog()
    {
        var dialog = System.Linq.Enumerable.FirstOrDefault(
            Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
                ? desktop.Windows.Where(w => w is Views.CreateDatabaseDialog || w is Views.OpenDatabaseDialog) 
                : System.Array.Empty<Avalonia.Controls.Window>());

        dialog?.Close();
    }



    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((ConnectionViewModel vm) => vm.StorageType))
        {
            OnPropertyChanged(nameof(IsFileBased));
            ConnectCommand?.RaiseCanExecuteChanged();
        }
        else if (e.IsProperty((ConnectionViewModel vm) => vm.ConnectionInfo))
        {
            ConnectCommand?.RaiseCanExecuteChanged();
            
            // Subscribe to ConnectionInfo property changes
            if (ConnectionInfo != null)
            {
                ConnectionInfo.PropertyChanged -= OnConnectionInfoPropertyChanged;
                ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;
            }
        }
        else if (e.IsProperty((ConnectionViewModel vm) => vm.IsConnecting))
        {
            ConnectCommand?.RaiseCanExecuteChanged();
        }
        else if (e.IsProperty((ConnectionViewModel vm) => vm.UseAutoDetectedSettings))
        {
            // When toggling between auto/manual, re-evaluate connect availability.
            ConnectCommand?.RaiseCanExecuteChanged();
        }
    }

    private void OnConnectionInfoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When any ConnectionInfo property changes, re-evaluate CanConnect
        ConnectCommand?.RaiseCanExecuteChanged();
    }

    #endregion

    #endregion

    #region Properties

    [Notify]
    public ConnectionInfo ConnectionInfo { get; set; } = null!;

    [Notify]
    public List<string> StorageEngines { get; set; } = null!;

    [Notify]
    public string SelectedStorageEngine { get; set; } = "btree";

    [Notify]
    public bool IsConnecting { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool DialogResult { get; set; }

    [Notify]
    public bool IsNewDatabase { get; set; }

    public ConnectionInfo? SelectedConnection { get; set; }

    // Storage type: 0 = File-based, 1 = In-Memory
    [Notify]
    public int StorageType { get; set; }

    public bool IsFileBased => StorageType == 0;

    // Advanced settings for new database
    [Notify]
    public int SelectedPageSize { get; set; } = 4096;

    [Notify]
    public int CacheSize { get; set; } = 1000;

    [Notify]
    public bool EnableTransactions { get; set; } = true;

    [Notify]
    public bool EnableMvcc { get; set; } = true;

    [Notify]
    public bool EnableFileLocking { get; set; } = true;

    [Notify]
    public bool UseAutoDetectedSettings { get; set; } = true;

    [Notify]
    public List<int> PageSizeOptions { get; set; } = null!;

    public string DialogTitle => IsNewDatabase ? "Create New Database" : "Open Database";
    
    public string DialogDescription => IsNewDatabase 
        ? "Create a new WitDatabase file with custom settings"
        : "Open an existing WitDatabase file";
    
    public string ConnectButtonText => IsNewDatabase ? "Create" : "Open";

    public bool CanConnectChanged => CanConnect();

    #endregion

    #region Public Methods

    public async Task<bool> ShowCreateDialogAsync()
    {
        IsNewDatabase = true;
        DialogResult = false;
        SelectedConnection = null;

        // Reset to defaults for Create dialog
        ConnectionInfo = new ConnectionInfo();
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;
        StorageType = 0; // File-based by default
        PageSizeOptions = [512, 1024, 2048, 4096, 8192, 16384, 32768];
        SelectedPageSize = 4096;
        CacheSize = 1000;
        EnableTransactions = true;
        EnableMvcc = true;
        EnableFileLocking = true;
        SelectedStorageEngine = "btree";

        UseAutoDetectedSettings = true;

        var dialog = new Views.CreateDatabaseDialog
        {
            DataContext = this
        };

        var mainWindow = System.Linq.Enumerable.FirstOrDefault(
            Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
                ? desktop.Windows 
                : System.Array.Empty<Avalonia.Controls.Window>());

        if (mainWindow != null)
        {
            await dialog.ShowDialog(mainWindow);
        }

        return DialogResult;
    }

    public async Task<bool> ShowOpenDialogAsync()
    {
        IsNewDatabase = false;
        DialogResult = false;
        SelectedConnection = null;

        // Reset ConnectionInfo for Open dialog
        ConnectionInfo ??= new ConnectionInfo();
        ConnectionInfo.PropertyChanged -= OnConnectionInfoPropertyChanged;
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;

        UseAutoDetectedSettings = true;

        var dialog = new Views.OpenDatabaseDialog
        {
            DataContext = this
        };

        var mainWindow = System.Linq.Enumerable.FirstOrDefault(
            Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
                ? desktop.Windows 
                : System.Array.Empty<Avalonia.Controls.Window>());

        if (mainWindow != null)
        {
            await dialog.ShowDialog(mainWindow);
        }

        return DialogResult;
    }

    /// <summary>
    /// Legacy method for backward compatibility - redirects to ShowOpenDialogAsync
    /// </summary>
    public async Task<bool> ShowDialogAsync()
    {
        if (IsNewDatabase)
            return await ShowCreateDialogAsync();
        else
            return await ShowOpenDialogAsync();
    }

    #endregion
}
