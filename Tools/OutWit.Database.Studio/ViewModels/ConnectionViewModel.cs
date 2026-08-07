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
        if (ConnectionInfo != null)
            ConnectionInfo.PropertyChanged -= OnConnectionInfoPropertyChanged;

        ConnectionInfo = new ConnectionInfo();
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;

        // A dialog reopened after a failed attempt used to come back still showing the old error,
        // because InitDefault replaced everything except this.
        ErrorMessage = null;

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
        BrowseFolderCommand = new RelayCommandAsync(BrowseFolderAsync);
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
        if (IsNewDatabase)
            await CreateNewDatabaseAsync();
        else
            await OpenExistingDatabaseAsync();
    }

    /// <summary>
    /// An LSM database is a DIRECTORY of SSTables, not a file, so a file picker can never select one -
    /// which meant Studio could create an LSM database and never reopen it. This is the other half of
    /// Browse.
    /// </summary>
    private async Task BrowseFolderAsync()
    {
        var folderPath = await Dialogs.OpenFolderAsync("Open LSM Database Folder");

        if (string.IsNullOrEmpty(folderPath))
            return;

        ConnectionInfo.FilePath = folderPath;

        ApplyAutoDetectedSettings(folderPath);

        UpdateStatus();
    }

    private async Task OpenExistingDatabaseAsync()
    {
        var filePath = await Dialogs.OpenFileAsync("Open Database",
        [
            new FileFilter("WitDatabase Files", ["*.witdb", "*.db"]),
            new FileFilter("All Files", ["*.*"])
        ]);

        if (string.IsNullOrEmpty(filePath))
            return;

        ConnectionInfo.FilePath = filePath;

        ApplyAutoDetectedSettings(filePath);

        UpdateStatus();

    }

    /// <summary>
    /// A WitDatabase is a file for the paged stores and a directory for LSM, so both count.
    /// </summary>
    private static bool DatabaseExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return File.Exists(path) || Directory.Exists(path);
    }

    /// <summary>
    /// Looks at the path and shows what is there, before anything is opened (WS-47).
    ///
    /// <para>
    /// The dialog has THREE states rather than the design's one, and the engine is why. An
    /// unencrypted database can be described in full; an encrypted one can be recognised as encrypted
    /// and as nothing else, because its header is inside the encrypted page; and a file with no magic
    /// bytes is indistinguishable from an encrypted database, so Studio says so instead of asking for
    /// the password to a text file. <see cref="StorageProbe"/> carries the measurements.
    /// </para>
    /// <para>
    /// Called by the Browse path and separated so that it can be driven without a file picker.
    /// </para>
    /// </summary>
    public void ApplyAutoDetectedSettings(string filePath)
    {
        Probe = StorageProbe.Look(filePath);

        if (!UseAutoDetectedSettings)
            return;

        ConnectionInfo.IsEncrypted = Probe.RequiresPassword;

        if (!string.IsNullOrEmpty(Probe.StoreType))
            SelectedStorageEngine = Probe.StoreType;

        // Only from a database that could actually be read. An encrypted one publishes none of this,
        // and copying the guesses in would put three wrong facts on screen.
        if (Probe.Kind == StorageKind.Database)
        {
            EnableTransactions = Probe.HasTransactions;
            EnableMvcc = Probe.HasMvcc;
            EnableFileLocking = Probe.HasFileLocking;
        }
    }

    private async Task CreateNewDatabaseAsync()
    {
        // A B-Tree database is a file and an LSM database is a folder, so the two are picked with
        // different pickers (WS-48). Asking for a file and then making a folder out of its parent is
        // what put an abandoned database in the user's Documents.
        if (SelectedStorageEngine == "lsm")
        {
            var folder = await Dialogs.OpenFolderAsync("Create LSM Database In Folder");

            if (!string.IsNullOrEmpty(folder))
                ConnectionInfo.FilePath = folder;

            return;
        }

        var filePath = await Dialogs.SaveFileAsync(
            "Create New Database",
            suggestedFileName: "database.witdb",
            defaultExtension: ".witdb",
            filters:
            [
                new FileFilter("WitDatabase Files", ["*.witdb"]),
                new FileFilter("All Files", ["*.*"])
            ]);

        if (!string.IsNullOrEmpty(filePath))
            ConnectionInfo.FilePath = filePath;
    }

    /// <summary>
    /// Creates the database the Create dialog describes, then closes it so that the connection below
    /// opens it in the ordinary way.
    /// </summary>
    private async Task CreateDatabaseOnDiskAsync()
    {
        var builder = new WitDatabaseBuilder();

        builder.WithFilePath(ConnectionInfo.FilePath);

        if (SelectedStorageEngine == "btree")
        {
            builder.WithBTree();
        }
        else if (SelectedStorageEngine == "lsm")
        {
            // The chosen path IS the LSM database - a folder of SSTables. This used to be handed
            // Path.GetDirectoryName(path), i.e. the folder the user picked a file IN, which built a
            // second, empty LSM database beside the real one and abandoned it: choosing
            // C:\Users\Me\Documents\mydb.witdb dropped provider.meta and wal.log into Documents.
            builder.WithLsmTree(ConnectionInfo.FilePath);
        }

        if (ConnectionInfo.IsEncrypted && !string.IsNullOrEmpty(ConnectionInfo.Password))
        {
            builder.WithEncryption(ConnectionInfo.Password);
        }

        builder.WithPageSize(SelectedPageSize);
        builder.WithCacheSize(CacheSize);

        if (EnableTransactions)
        {
            if (EnableMvcc)
                builder.WithMvcc();
            else
                builder.WithTransactions();
        }
        else
        {
            builder.WithoutTransactions();
        }

        if (EnableFileLocking)
            builder.WithFileLocking();
        else
            builder.WithoutFileLocking();

        // Build and immediately dispose: this call exists to create the database with the settings the
        // dialog describes, and the connection that follows is what the user works through.
        using (var db = builder.Build())
        {
        }

        // Give the system time to release file locks
        await Task.Delay(100, CancellationToken.None);

        Logger.LogInformation("Database created: {FilePath}", ConnectionInfo.FilePath);
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
                // An in-memory database has no storage engine to choose and no place to put one. The
                // combination used to be accepted and answered with WithLsmTree("."), which wrote a
                // database into the process working directory - for an installed application, wherever
                // it happened to be launched from (WS-48).
                if (!IsFileBased && SelectedStorageEngine == "lsm")
                {
                    ErrorMessage = "An in-memory database cannot use the LSM store: LSM is a folder of "
                        + "SSTables on disk. Choose B-Tree, or create the database as a folder.";
                    Logger.LogWarning("Refused in-memory + lsm: the combination has no meaning");
                    return;
                }

                // Validate file path for file-based database
                if (IsFileBased && string.IsNullOrWhiteSpace(ConnectionInfo.FilePath))
                {
                    ErrorMessage = "Please specify a database file path.";
                    return;
                }

                // An in-memory database is built by the connection and lives exactly as long as it.
                // Building one here with WitDatabaseBuilder, disposing it and then connecting over
                // 'Data Source=:memory:' created one database and handed the user another, empty one -
                // every connection to ':memory:' gets its own. So: create nothing, connect.
                if (!IsFileBased)
                {
                    ConnectionInfo.FilePath = ":memory:";
                    Logger.LogInformation("In-memory database: the connection creates it, nothing is built first");
                }
                else
                {
                    await CreateDatabaseOnDiskAsync();
                }
            }
            else if (IsFileBased)
            {
                var probe = StorageProbe.Look(ConnectionInfo.FilePath);

                if (probe.Kind == StorageKind.NotFound)
                {
                    // The engine creates a database it is asked to open and cannot find, which is right
                    // for a provider and wrong for a dialog called Open: a user whose file has moved
                    // would be shown an empty database and read it as their data being gone.
                    ErrorMessage = Localization["Dialog.Open.NotFound"];
                    Logger.LogWarning("Refused to open a database that does not exist: {FilePath}",
                        ConnectionInfo.FilePath);
                    return;
                }

                if (probe.Kind == StorageKind.NotADatabase)
                {
                    // A folder with no SSTable, or a file too short to be one. This is knowable, unlike
                    // the encrypted-or-not-a-database case, so it is refused rather than attempted.
                    ErrorMessage = probe.IsDirectory
                        ? Localization["Dialog.Open.NotADatabaseFolder"]
                        : Localization["Dialog.Open.NotADatabaseFile"];
                    Logger.LogWarning("Refused to open something that is not a database: {FilePath}",
                        ConnectionInfo.FilePath);
                    return;
                }
            }

            // Connect to the database (both for new and existing). The manager adds the session and
            // makes it active; any database already open stays open.
            Logger.LogInformation("Attempting to connect to database: {FilePath}", ConnectionInfo.FilePath);
            OpenedSession = await Connections.OpenAsync(ConnectionInfo);

            if (OpenedSession != null)
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

    /// <summary>
    /// Asks whoever is showing this ViewModel to close it. The ViewModel used to hold the Window and
    /// call Close() on it, which is why it could not be exercised without Avalonia.
    /// </summary>
    private void CloseDialog()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
        else if (e.IsProperty((ConnectionViewModel vm) => vm.Probe))
        {
            // Both are computed from the probe, and a computed property is not re-read unless it is
            // told - which is the defect shape stage 8 found in the section strip.
            OnPropertyChanged(nameof(ProbeMessage));
            OnPropertyChanged(nameof(NeedsPassword));
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

    /// <summary>
    /// Returns the dialog to the state a freshly opened one is in. Public so that the reset both
    /// dialogs perform can be driven without a window.
    /// </summary>
    public void ResetForNewDialog()
    {
        InitDefault();
    }

    public async Task<bool> ShowCreateDialogAsync()
    {
        IsNewDatabase = true;
        DialogResult = false;
        SelectedConnection = null;
        OpenedSession = null;

        // Reset to defaults for Create dialog
        InitDefault();

        return await Dialogs.ShowCreateDatabaseAsync(this);
    }

    public async Task<bool> ShowOpenDialogAsync()
    {
        IsNewDatabase = false;
        DialogResult = false;
        SelectedConnection = null;
        OpenedSession = null;

        InitDefault();

        return await Dialogs.ShowOpenDatabaseAsync(this);
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

    /// <summary>
    /// The session this dialog opened, or null if it opened none. The caller needs the session rather
    /// than the ConnectionInfo now: with several databases open, "the current connection" is not a
    /// question the service can answer.
    /// </summary>
    public IDatabaseSession? OpenedSession { get; private set; }

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

    /// <summary>
    /// What is at the chosen path, as far as it can be known without opening it (WS-47). Never null,
    /// so the banner has something to bind to before a path is picked.
    /// </summary>
    [Notify]
    public StorageProbe Probe { get; set; } = StorageProbe.Look(null);

    /// <summary>
    /// The sentence under the path box. Three states, because the engine allows three
    /// - see <see cref="ApplyAutoDetectedSettings"/>.
    /// </summary>
    public string ProbeMessage => Probe.Kind switch
    {
        StorageKind.Database => Localization.Format("Dialog.Open.Found",
            Probe.StoreType == "lsm" ? "LSM" : "B-Tree",
            Size(Probe.SizeInBytes),
            Probe.HasMvcc ? "MVCC" : Localization["Dialog.Open.NoMvcc"]),

        // ONE sentence covering both, because Studio genuinely cannot tell them apart: the magic bytes
        // are absent, which is what encryption looks like and what a text file looks like. Claiming the
        // more likely one is how a user ends up typing a password at a JPEG and being told the password
        // is wrong.
        StorageKind.Unreadable => Localization.Format("Dialog.Open.EncryptedOrNot", Size(Probe.SizeInBytes)),

        StorageKind.NotADatabase => Probe.IsDirectory
            ? Localization["Dialog.Open.NotADatabaseFolder"]
            : Localization["Dialog.Open.NotADatabaseFile"],

        _ => string.Empty
    };

    /// <summary>Whether the password box is shown at all: only an encrypted database needs one.</summary>
    public bool NeedsPassword => Probe.RequiresPassword;

    /// <summary>
    /// A size a person reads, written invariantly like every other number Studio shows (WS-65).
    /// </summary>
    private static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return size.ToString(unit == 0 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture)
            + " " + units[unit];
    }

    /// <summary>Raised when the dialog showing this ViewModel should close.</summary>
    public event EventHandler? CloseRequested;

    #endregion

    #region Commands

    public ICommand BrowseFileCommand { get; private set; } = null!;

    public ICommand BrowseFolderCommand { get; private set; } = null!;

    public ICommand ConnectCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IConnectionManager Connections => ApplicationVm.Connections;

    public IDialogService Dialogs => ApplicationVm.Dialogs;

    public ISettingsService Settings => ApplicationVm.Settings;

    public Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
