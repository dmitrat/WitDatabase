using System.Collections.ObjectModel;
using System.Windows.Input;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// One row of the connections list: the profile, plus what the file system says about it now.
/// </summary>
public sealed class ConnectionRow(ConnectionProfile profile, StorageProbe probe,
    string? openStoreType = null)
{
    public ConnectionProfile Profile { get; } = profile;

    /// <summary>What is at the path at this moment - which is not what the profile remembers.</summary>
    public StorageProbe Probe { get; } = probe;

    /// <summary>
    /// The store as the OPEN session knows it, or null when this application does not hold it open.
    /// </summary>
    /// <remarks>
    /// A database Studio has open is held under an exclusive lock, so the probe cannot read its
    /// header and answers a size and nothing else - the engine name disappeared from the row at the
    /// moment it was most certainly known. It is not read from the profile: the column is about what
    /// the database IS, not about what it was when it was saved. It is read from the session, which
    /// is the same answer the «База» tab gives for the same reason.
    /// </remarks>
    public string? OpenStoreType { get; } = openStoreType;

    /// <summary>
    /// Whether the database is where the profile says it is. A missing one is MARKED and kept: the
    /// disk may not be mounted, and a row that silently disappeared would read as lost settings.
    /// </summary>
    public bool IsMissing => Probe.Kind == StorageKind.NotFound;

    public string Name => Profile.Name;

    public string Path => Profile.Path;

    public int ColorIndex => Profile.ColorIndex;

    public bool IsReadOnly => Profile.IsReadOnly;

    /// <summary>
    /// The store and the size, as they are NOW. Taken from the probe rather than from the profile,
    /// because a list showing what a database used to be is worse than one showing nothing.
    /// </summary>
    public string Storage
    {
        get
        {
            if (IsMissing)
                return string.Empty;

            var store = (OpenStoreType ?? Probe.StoreType) switch
            {
                "lsm" => "LSM",
                "btree" => "B-Tree",
                _ => string.Empty
            };

            var size = Size(Probe.SizeInBytes);

            return string.IsNullOrEmpty(store) ? size : store + " · " + size;
        }
    }

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
}

/// <summary>
/// The saved connections (WS-68) - what "Recent Files" becomes once several databases can be open at
/// once, and the only place a name and a colour survive between sessions.
///
/// <para>
/// Two decisions here are the whole point of the window, and both are about not doing something.
/// <b>"Remove" removes from the LIST</b>: deleting a database from the interface that manages
/// databases is a function that will one day be pressed without looking, and it is not here at all.
/// And <b>a missing file is marked, not dropped</b>: a disk may not be mounted, and a row that
/// vanished on its own is indistinguishable from settings that were lost.
/// </para>
/// </summary>
public sealed class ConnectionsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event EventHandler? CloseRequested;

    #endregion

    #region Constructors

    public ConnectionsViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        Rows = [];

        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        ConnectCommand = new RelayCommandAsync(ConnectAsync);
        RemoveCommand = new RelayCommandAsync(RemoveAsync);
        DuplicateCommand = new RelayCommandAsync(DuplicateAsync);
        AddCommand = new RelayCommandAsync(AddAsync);
        RefreshCommand = new RelayCommandAsync(RefreshAsync);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the list and looks at every path. The look is what makes a missing database visible, and
    /// it is done on opening the window rather than once at startup: a network drive comes and goes.
    /// </summary>
    public async Task RefreshAsync()
    {
        var profiles = await Profiles.LoadAsync();

        Rows.Clear();

        foreach (var profile in profiles)
            Rows.Add(new ConnectionRow(profile, StorageProbe.Look(profile.Path), OpenStoreTypeOf(profile.Path)));

        Selected = Rows.FirstOrDefault();

        OnPropertyChanged(nameof(MissingCount));
    }

    /// <summary>
    /// The store of the session this application has open at <paramref name="path"/>, or null when it
    /// has none there.
    /// </summary>
    /// <remarks>
    /// Null is the honest answer for a database somebody ELSE holds: the file cannot be read and
    /// Studio has nothing to report but its size. Only what this application opened itself is known.
    /// </remarks>
    private string? OpenStoreTypeOf(string path)
    {
        var session = ApplicationVm.Connections.Sessions.FirstOrDefault(open =>
            string.Equals(open.Connection.FilePath, path, StringComparison.OrdinalIgnoreCase));

        if (session == null)
            return null;

        return session.StoredConfiguration is { } stored
            ? stored.IsDirectory ? "lsm" : "btree"
            : session.Connection.StorageEngine;
    }

    /// <summary>
    /// Opens the selected connection. An encrypted one goes through the Open dialog instead of being
    /// opened straight away - the password is not in the list and has to be asked for.
    /// </summary>
    private async Task ConnectAsync()
    {
        if (Selected == null)
            return;

        if (Selected.IsMissing)
        {
            ErrorMessage = ApplicationVm.Localization["Dialog.Open.NotFound"];
            return;
        }

        ErrorMessage = null;

        var connection = Selected.Profile.ToConnectionInfo();

        if (connection.IsEncrypted)
        {
            ApplicationVm.ConnectionVm.ResetForNewDialog();
            ApplicationVm.ConnectionVm.ConnectionInfo.FilePath = connection.FilePath;
            ApplicationVm.ConnectionVm.ConnectionInfo.DisplayName = connection.DisplayName;
            ApplicationVm.ConnectionVm.ConnectionInfo.ColorIndex = connection.ColorIndex;
            ApplicationVm.ConnectionVm.ConnectionInfo.EncryptionProvider = connection.EncryptionProvider;

            await ApplicationVm.ConnectionVm.ShowOpenDialogAsync();

            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var session = await ApplicationVm.OpenDatabaseAsync(connection);

        if (session == null)
        {
            ErrorMessage = ApplicationVm.Localization["Dialog.Connections.CouldNotOpen"];
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the row out of the LIST. The database is not touched.</summary>
    private async Task RemoveAsync()
    {
        if (Selected == null)
            return;

        await Profiles.RemoveAsync(Selected.Path);

        await RefreshAsync();
    }

    /// <summary>
    /// A second entry for the same database - which is what someone wants when they keep one read-only
    /// and one not, under two names and two colours.
    /// </summary>
    private async Task DuplicateAsync()
    {
        if (Selected == null)
            return;

        var copy = Selected.Profile.Clone();

        copy.Name = copy.Name + " (copy)";

        // The store is keyed by PATH, so a copy under the same path would replace the original rather
        // than join it. This is the one thing a duplicate has to change, and it is why the copy is
        // added through the list rather than through SaveAsync.
        Rows.Add(new ConnectionRow(copy, Selected.Probe));

        await Task.CompletedTask;
    }

    /// <summary>Add goes through the Open dialog: there is one place that asks about a database.</summary>
    private async Task AddAsync()
    {
        ApplicationVm.ConnectionVm.ResetForNewDialog();

        await ApplicationVm.ConnectionVm.ShowOpenDialogAsync();

        await RefreshAsync();
    }

    #endregion

    #region Properties

    public ObservableCollection<ConnectionRow> Rows { get; }

    [Notify]
    public ConnectionRow? Selected { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    /// <summary>How many saved databases are not where they were. Shown rather than counted by eye.</summary>
    public int MissingCount => Rows.Count(row => row.IsMissing);

    #endregion

    #region Commands

    public ICommand ConnectCommand { get; private set; } = null!;

    public ICommand RemoveCommand { get; private set; } = null!;

    public ICommand DuplicateCommand { get; private set; } = null!;

    public ICommand AddCommand { get; private set; } = null!;

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand CloseCommand { get; private set; } = null!;

    #endregion

    #region Services

    private IConnectionProfileStore Profiles => ApplicationVm.Profiles;

    #endregion
}
