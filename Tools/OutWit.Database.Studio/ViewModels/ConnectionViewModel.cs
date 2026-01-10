using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Providers;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Views.Dialogs;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for the database connection dialog.
/// </summary>
public class ConnectionViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ConnectionViewModel(ApplicationViewModel applicationVm)
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
        ConnectionInfo = new ConnectionInfo();
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;

        StorageEngines = ["btree", "lsm"];
        SelectedStorageEngine = "btree";
        
        // Set default page size to 4096
        SelectedPageSize = 4096;
        
        // Initialize page size options
        PageSizeOptions = [512, 1024, 2048, 4096, 8192, 16384, 32768];

        StorageType = 0; // File-based by default

        CacheSize = 1000;
        EnableTransactions = true;
        EnableMvcc = true;
        EnableFileLocking = true;

        UseAutoDetectedSettings = true;
    }

    private void InitCommands()
    {
        BrowseFileCommand = new RelayCommandAsync(BrowseFileAsync);
        ConnectCommand = new RelayCommandAsync(ConnectAsync);
        CancelCommand = new RelayCommand(Cancel);
    }

    private void InitEvents()
    {
        this.PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Command Functions

    private async Task BrowseFileAsync()
    {
        if (ApplicationVm.MainWindow == null)
            return;

        var storageProvider = ApplicationVm.MainWindow.StorageProvider;

        if (IsNewDatabase)
            await CreateNewDatabaseAsync(storageProvider);
        
        else
            await OpenExistingDatabaseAsync(storageProvider);
        
    }

    private async Task OpenExistingDatabaseAsync(IStorageProvider storageProvider)
    {
        // For open database - use Open dialog
        var openOptions = new FilePickerOpenOptions
        {
            Title = "Open Database",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("WitDatabase Files")
                {
                    Patterns = ["*.witdb", "*.db"]
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"]
                }
            ]
        };

        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(openOptions);

        if (files.Count <= 0)
            return;

        var filePath = files[0].Path.LocalPath;
        ConnectionInfo.FilePath = filePath;

        // Auto-detect settings from existing database
        if (UseAutoDetectedSettings && File.Exists(filePath))
        {
            try
            {
                StorageDetectionResult dbInfo = WitDatabase.GetDatabaseInfo(filePath);
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
            catch
            {
                // Ignore errors during detection
            }
        }

        UpdateStatus();

    }

    private async Task CreateNewDatabaseAsync(IStorageProvider storageProvider)
    {
        // For new database - use Save dialog
        var saveOptions = new FilePickerSaveOptions
        {
            Title = "Create New Database",
            DefaultExtension = ".witdb",
            SuggestedFileName = "database.witdb",
            FileTypeChoices =
            [
                new FilePickerFileType("WitDatabase Files")
                {
                    Patterns = ["*.witdb"]
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"]
                }
            ]
        };

        var file = await storageProvider.SaveFilePickerAsync(saveOptions);
            
        if (file != null)
        {
            ConnectionInfo.FilePath = file.Path.LocalPath;
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
                    builder.WithFilePath(ConnectionInfo.FilePath);
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
                
                Logger.LogInformation("Database file created: {FilePath}", ConnectionInfo.FilePath);
            }

            // Connect to the database (both for new and existing)
            Logger.LogInformation("Attempting to connect to database: {FilePath}", ConnectionInfo.FilePath);
            var success = await Database.ConnectAsync(ConnectionInfo);
            
            if (success)
            {
                if (IsFileBased && !string.IsNullOrWhiteSpace(ConnectionInfo.FilePath) && ConnectionInfo.FilePath != ":memory:")
                {
                    await Settings.AddRecentFileAsync(ConnectionInfo.FilePath);
                }
                
                SelectedConnection = ConnectionInfo;
                DialogResult = true;
                
                CloseDialog();
                
                Logger.LogInformation("Successfully connected to {FilePath}", ConnectionInfo.FilePath);
            }
            else
            {
                ErrorMessage = "Failed to connect to database. Check file path and credentials.";
                Logger.LogWarning("Connection failed for {FilePath}", ConnectionInfo.FilePath);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
            Logger.LogError(ex, "Connection error");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void UpdateStatus()
    {
        // For in-memory database, no file path is needed
        if (IsNewDatabase && !IsFileBased)
            CanConnect = !IsConnecting && (!ConnectionInfo.IsEncrypted || !string.IsNullOrWhiteSpace(ConnectionInfo.Password));

        // For file-based database, file path is required
        CanConnect = !string.IsNullOrWhiteSpace(ConnectionInfo.FilePath) 
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
        Dialog?.Close();
    }

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((ConnectionViewModel vm) => vm.StorageType))
        {
            OnPropertyChanged(nameof(IsFileBased));
            UpdateStatus();
        }
        else if (e.IsProperty((ConnectionViewModel vm) => vm.ConnectionInfo))
        {
            UpdateStatus();
        }
        else if (e.IsProperty((ConnectionViewModel vm) => vm.IsConnecting))
        {
            UpdateStatus();
        }
        else if (e.IsProperty((ConnectionViewModel vm) => vm.UseAutoDetectedSettings))
        {
            // When toggling between auto/manual, re-evaluate connect availability.
            UpdateStatus();
        }
    }

    private void OnConnectionInfoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When any ConnectionInfo property changes, re-evaluate CanConnect
        UpdateStatus();
    }

    #endregion

    #endregion

    #region Public Methods

    public async Task<bool> ShowCreateDialogAsync()
    {
        IsNewDatabase = true;
        DialogResult = false;
        SelectedConnection = null;

        // Reset to defaults for Create dialog
        InitDefault();

        Dialog = new CreateDatabaseDialog
        {
            DataContext = this
        };
        
        await Dialog.ShowDialog(ApplicationVm.MainWindow!);

        Dialog = null;

        return DialogResult;
    }

    public async Task<bool> ShowOpenDialogAsync()
    {
        IsNewDatabase = false;
        DialogResult = false;
        SelectedConnection = null;

        InitDefault();

        Dialog = new OpenDatabaseDialog
        {
            DataContext = this
        };

        await Dialog.ShowDialog(ApplicationVm.MainWindow!);

        Dialog = null;

        return DialogResult;
    }

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

    [Notify]
    public bool CanConnect { get; set; }

    private Window? Dialog { get; set; }

    #endregion

    #region Commands

    public ICommand BrowseFileCommand { get; private set; } = null!;

    public ICommand ConnectCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ISettingsService Settings => ApplicationVm.Settings;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
