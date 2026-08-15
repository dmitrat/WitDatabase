using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.AdoNet.Maintenance;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Ui.Icons;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// One row of the provenance matrix, with its words already in the reader's language.
/// </summary>
public sealed record StorageCapabilityRow(string Operation, string Source, string State, string? Note);

/// <summary>
/// The «База» tab: the storage layer of one connection (WS-54).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every number here was read from somewhere, and the tab says where.</b> The file system knows the
/// size, the header or the sidecar knows what the database was created with, and the open connection
/// knows what its store is doing. Nothing is inferred from anything else, and a fact that is not
/// available is absent rather than zero.
/// </para>
/// <para>
/// <b>The words are written here and the facts come from the service</b> - <see cref="DatabaseOverview"/>
/// carries provider keys, numbers and flags and not one sentence. Stage 10's rule, applied while
/// writing rather than swept up afterwards.
/// </para>
/// <para>
/// <b>A button for something the engine cannot do is absent, not disabled</b> (WS-55). "Compact" exists
/// only while the store is an LSM one; the provider's <c>NotSupported</c> is the second half of the
/// same answer, for the case where the store changed under a stale panel.
/// </para>
/// </remarks>
public sealed class DatabaseTabViewModel : WorkspaceTabViewModel
{
    #region Constructors

    public DatabaseTabViewModel(ApplicationViewModel applicationVm, IDatabaseSession session)
        : base(applicationVm, session)
    {
        Title = Localization.Format("Tab.DatabaseOf", session.DisplayName);
        IsPinned = true;

        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        RefreshCommand = new RelayCommandAsync(RefreshAsync);
        CheckpointCommand = new RelayCommandAsync(() => MaintainAsync(compact: false));
        CompactCommand = new RelayCommandAsync(() => MaintainAsync(compact: true));
        ReadCheckCommand = new RelayCommandAsync(ReadCheckAsync);
        CopyCommand = new RelayCommandAsync(CopyAsync);
        ChangePasswordCommand = new RelayCommandAsync(ChangePasswordAsync);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads everything again. Called when the tab is selected, because the numbers below age: the
    /// memtable fills and the SSTables merge while the tab sits behind a query.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (Session is not { IsConnected: true } session)
        {
            ErrorMessage = Localization["Query.NotConnected"];
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Overview = await DatabaseOverviewReader.ReadAsync(session);

            Describe(Overview);
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Database.ReadFailed", ex.Message);
            Logger.LogError(ex, "Failed to read the storage overview of {Name}", session.DisplayName);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Runs one maintenance operation and says what it DID.
    /// </summary>
    /// <remarks>
    /// The outcome is a code, so the sentence is written here: "nothing to do" is a result and not a
    /// failure, and a silent success is what made the old <c>Compact()</c> look like it worked.
    /// </remarks>
    private async Task MaintainAsync(bool compact)
    {
        if (Session is not { IsConnected: true } session)
            return;

        IsMaintaining = true;
        ErrorMessage = null;

        try
        {
            var result = compact
                ? await session.CompactAsync()
                : await session.CheckpointAsync();

            MaintenanceMessage = Describe(result);
            ApplicationVm.MainWindowVm.StatusText = MaintenanceMessage;

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Database.MaintenanceFailed", ex.Message);
            Logger.LogError(ex, "Storage maintenance failed on {Name}", session.DisplayName);
        }
        finally
        {
            IsMaintaining = false;
        }
    }

    /// <summary>
    /// Opens verification by reading (WS-61) for this connection.
    /// </summary>
    private async Task ReadCheckAsync()
    {
        if (Session is not { IsConnected: true } session)
            return;

        await ApplicationVm.Dialogs.ShowReadCheckAsync(new ReadCheckViewModel(ApplicationVm, session));
    }

    /// <summary>
    /// Opens the byte copy (WS-59) for this connection.
    /// </summary>
    private async Task CopyAsync()
    {
        if (Session is not { } session)
            return;

        await ApplicationVm.Dialogs.ShowDatabaseCopyAsync(new DatabaseCopyViewModel(ApplicationVm, session));

        await RefreshAsync();
    }

    /// <summary>
    /// Opens the password change (WS-58), which is a migration into a new database.
    /// </summary>
    private async Task ChangePasswordAsync()
    {
        if (Session is not { IsConnected: true } session)
            return;

        await ApplicationVm.Dialogs.ShowChangePasswordAsync(
            new ChangePasswordViewModel(ApplicationVm, session));
    }

    /// <summary>
    /// What the operation did, in the reader's language, with the evidence in it.
    /// </summary>
    private string Describe(WitDbMaintenanceResult result)
    {
        var operation = Localization[result.Operation == WitDbMaintenanceOperation.Compact
            ? "Database.Maintenance.Compact"
            : "Database.Maintenance.Checkpoint"];

        return result.Outcome switch
        {
            WitDbMaintenanceOutcome.Completed when result.SstablesBefore is { } before
                                                   && result.SstablesAfter is { } after =>
                Localization.Format("Database.Maintenance.Completed", operation, before, after),

            WitDbMaintenanceOutcome.Completed =>
                Localization.Format("Database.Maintenance.CompletedPlain", operation),

            WitDbMaintenanceOutcome.NothingToDo =>
                Localization.Format("Database.Maintenance.NothingToDo", operation),

            _ => Localization.Format("Database.Maintenance.NotSupported", operation)
        };
    }

    /// <summary>
    /// Turns the facts into the sentences the tab shows.
    /// </summary>
    private void Describe(DatabaseOverview overview)
    {
        StorageKind = Localization[overview.StoreProviderKey == "lsm"
            ? "Database.Store.Lsm"
            : "Database.Store.BTree"];

        StorageDetail = overview.IsDirectory
            ? Localization.Format("Database.Store.Folder",
                Localization.Plural("Count.Sstables", overview.Lsm?.SstableCount ?? 0))
            : Localization.Format("Database.Store.File", Bytes(overview.PageSize ?? 0));

        Size = Bytes(overview.SizeInBytes);
        SizeDetail = overview.PageCount is { } pages
            ? Localization.Plural("Count.Pages", pages)
            : string.Empty;

        HasConfiguration = overview.ConfigurationIsAvailable;

        Encryption = overview.ConfigurationIsAvailable
            ? overview.IsEncrypted
                ? overview.EncryptionProviderKey!
                : Localization["Database.Encryption.None"]
            : Localization["Common.Unknown"];
        // Two different things, and saying the wrong one is how the whole rewrap was missed for a
        // release. Since the format change the data key is drawn at random and the password only
        // WRAPS it - which is exactly what CanChangePassword answers, because a wrapped key is the
        // thing a new password can be wrapped around. A file written before the change, and a
        // database whose caller owns the key, get the older sentence, which is true of them.
        EncryptionDetail = overview.IsEncrypted
            ? Localization[Session?.CanChangePassword == true
                ? "Database.Encryption.WrappedKey"
                : "Database.Encryption.FromPassword"]
            : string.Empty;

        // From the CHAIN this connection assembled, not from the header - which is both the live
        // answer and the only one available for a database created by the open that is reading it.
        Transactions = Localization[overview.ChainHasMvcc
            ? "Database.Transactions.Mvcc"
            : overview.ChainHasTransactions
                ? "Database.Transactions.Locks"
                : "Database.Transactions.None"];
        TransactionsDetail = overview.ChainHasTransactions
            ? Localization.Format("Database.Transactions.Default", Session?.Isolation.ToString() ?? string.Empty)
            : string.Empty;

        // Absent rather than zero, twice over: an LSM database has no database header at all, and a
        // paged one that was created by this very open has one nobody could read.
        HasFormat = overview.FormatVersionText != null;
        Format = overview.FormatVersionText ?? string.Empty;

        Path = overview.Path;
        Journal = string.IsNullOrEmpty(overview.JournalProviderKey)
            ? Localization["Database.Journal.None"]
            : overview.JournalProviderKey;
        // The cache the database was created with, and - since 2026-08-09 - what it is HOLDING. The
        // second half is a reading taken with the rest of this refresh, so it belongs on the line that
        // is re-read rather than in the Configuration block, which cannot change while the database is
        // open. Absent for an LSM database, which has no page cache to ask.
        Cache = overview.CachePagesHeld is { } held
            ? Localization.Format("Database.Cache.Holding", overview.CacheProviderKey,
                Localization.Plural("Count.Pages", overview.CacheSizeInPages),
                Localization.Plural("Count.Pages", held),
                overview.CacheDirtyPages ?? 0)
            : Localization.Format("Database.Cache.Sized", overview.CacheProviderKey,
                Localization.Plural("Count.Pages", overview.CacheSizeInPages));
        Locking = Localization[overview.HasFileLocking switch
        {
            true => "Database.Locking.On",
            false => "Database.Locking.Off",
            null => "Common.Unknown"
        }];

        Schema = Localization.Format("Database.SchemaSummary",
            Localization.Plural("Count.Tables", overview.Schema.Tables),
            Localization.Plural("Count.Views", overview.Schema.Views),
            Localization.Plural("Count.Indexes", overview.Schema.Indexes),
            Localization.Plural("Count.Triggers", overview.Schema.Triggers));

        Chain = string.Join(" → ", overview.StoreChain);

        DescribeNow(overview);
        DescribeLsm(overview);
        DescribeMatrix();
    }

    private void DescribeNow(DatabaseOverview overview)
    {
        var session = Session;

        Transaction = session is { HasOpenTransaction: true }
            ? Localization.Format("Database.Now.TransactionOpen",
                Localization.Plural("Count.Statements", session.TransactionStatementCount))
            : Localization["Database.Now.NoTransaction"];

        Isolation = session?.Isolation.ToString() ?? string.Empty;

        Tabs = Localization.Plural("Count.Tabs",
            ApplicationVm.WorkspaceTabsVm.Tabs.Count(tab => tab.Session == session));

        // The holder cannot be named - the operating system does not say and the lock file carries
        // nothing - so the honest sentence is about who is holding it here, which is the question a
        // developer whose own application will not start is actually asking.
        //
        // Judged by what the lock ANSWERS rather than by the header's flag: this session is holding
        // the database, so a locked path is one Studio is holding, whatever the header says - and the
        // header is not always readable.
        Access = overview switch
        {
            { IsInUse: true } when session is { IsConnected: true } => Localization["Database.Now.HeldHere"],
            { IsInUse: true } => Localization["Database.Now.Busy"],
            { HasFileLocking: false } => Localization["Database.Now.NotLocked"],
            _ => Localization["Database.Now.Free"]
        };
    }

    private void DescribeLsm(DatabaseOverview overview)
    {
        IsLsm = overview.Lsm != null;

        if (overview.Lsm is not { } lsm)
            return;

        Sstables = Localization.Plural("Count.Sstables", lsm.SstableCount);
        CompactionTrigger = Localization.Format("Database.Lsm.Trigger", lsm.CompactionTrigger);

        MemTable = Localization.Format("Database.Lsm.MemTable",
            Bytes(lsm.MemTableUsedBytes), Bytes(lsm.MemTableLimitBytes));
        MemTableFill = lsm.MemTableLimitBytes <= 0
            ? 0
            : Math.Min(1.0, (double)lsm.MemTableUsedBytes / lsm.MemTableLimitBytes);

        IsCompacting = lsm.IsCompacting;

        // "Since this connection opened" is not a caveat, it is what the counters MEASURE: they live on
        // the store object and start at zero when it is built, so a database reopened with a thousand
        // compactions behind it reports none.
        Counters = Localization.Format("Database.Lsm.Counters",
            lsm.CountersSinceOpened.Flushes,
            lsm.CountersSinceOpened.Compactions,
            lsm.CountersSinceOpened.Puts,
            lsm.CountersSinceOpened.Deletes);

        // The design draws L0/L1/L2. This store keeps a flat list and merges all of it into one file,
        // so there is nothing to draw and the panel says the shape it actually has.
        Levels = Localization["Database.Lsm.NoLevels"];
    }

    private void DescribeMatrix()
    {
        if (Capabilities.Count > 0)
            return;

        foreach (var capability in StorageCapabilities.Matrix)
        {
            Capabilities.Add(new StorageCapabilityRow(
                Localization[capability.OperationKey],
                Localization[capability.SourceKey],
                Localization[capability.Availability switch
                {
                    StorageAvailability.Available => "Database.Cap.State.Available",
                    StorageAvailability.NeedsProviderAccess => "Database.Cap.State.NeedsAccess",
                    _ => "Database.Cap.State.NotInEngine"
                }],
                capability.NoteKey == null ? null : Localization[capability.NoteKey]));
        }
    }

    private static string Bytes(long value) => ByteSize.Format(value);

    #endregion

    #region Overrides

    public override WorkspaceTabType TabType => WorkspaceTabType.Database;

    public override string IconPath => StudioIcons.PATH_DB_DATABASE;

    /// <summary>One «База» tab per connection - the tab is about the connection, not about an object.</summary>
    public override string? UniqueId => Session == null ? null : $"database:{Session.Id}";

    public override void OnActivated() => _ = RefreshAsync();

    protected override void OnSessionStatusChanged(bool isConnected)
    {
        if (isConnected)
            _ = RefreshAsync();
    }

    #endregion

    #region Properties

    /// <summary>The facts, exactly as they were read.</summary>
    [Notify]
    public DatabaseOverview? Overview { get; private set; }

    [Notify] public bool IsLoading { get; private set; }

    [Notify] public bool IsMaintaining { get; private set; }

    [Notify] public string? ErrorMessage { get; private set; }

    [Notify] public string? MaintenanceMessage { get; private set; }

    [Notify] public string StorageKind { get; private set; } = string.Empty;

    [Notify] public string StorageDetail { get; private set; } = string.Empty;

    [Notify] public string Size { get; private set; } = string.Empty;

    [Notify] public string SizeDetail { get; private set; } = string.Empty;

    [Notify] public string Encryption { get; private set; } = string.Empty;

    [Notify] public string EncryptionDetail { get; private set; } = string.Empty;

    [Notify] public string Transactions { get; private set; } = string.Empty;

    [Notify] public string TransactionsDetail { get; private set; } = string.Empty;

    /// <summary>Whether the format card is shown at all - an LSM database has no header to read.</summary>
    [Notify] public bool HasFormat { get; private set; }

    /// <summary>
    /// Whether the configuration block has anything in it. False for a database created by the very
    /// open that would have read it: an open database's header cannot be read at all, so the block
    /// says so instead of showing zeros.
    /// </summary>
    [Notify] public bool HasConfiguration { get; private set; }

    [Notify] public string Format { get; private set; } = string.Empty;

    [Notify] public string Path { get; private set; } = string.Empty;

    [Notify] public string Journal { get; private set; } = string.Empty;

    [Notify] public string Cache { get; private set; } = string.Empty;

    [Notify] public string Locking { get; private set; } = string.Empty;

    [Notify] public string Schema { get; private set; } = string.Empty;

    /// <summary>The layers between the database and the disk, outermost first.</summary>
    [Notify] public string Chain { get; private set; } = string.Empty;

    [Notify] public string Transaction { get; private set; } = string.Empty;

    [Notify] public string Isolation { get; private set; } = string.Empty;

    [Notify] public string Tabs { get; private set; } = string.Empty;

    [Notify] public string Access { get; private set; } = string.Empty;

    /// <summary>Whether the LSM panel and the Compact button exist at all (WS-55).</summary>
    [Notify] public bool IsLsm { get; private set; }

    [Notify] public string Sstables { get; private set; } = string.Empty;

    [Notify] public string CompactionTrigger { get; private set; } = string.Empty;

    [Notify] public string MemTable { get; private set; } = string.Empty;

    [Notify] public double MemTableFill { get; private set; }

    [Notify] public bool IsCompacting { get; private set; }

    [Notify] public string Counters { get; private set; } = string.Empty;

    [Notify] public string Levels { get; private set; } = string.Empty;

    public ObservableCollection<StorageCapabilityRow> Capabilities { get; } = [];

    #endregion

    #region Services

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion

    #region Commands

    [Notify] public ICommand RefreshCommand { get; private set; } = null!;

    [Notify] public ICommand CheckpointCommand { get; private set; } = null!;

    [Notify] public ICommand CompactCommand { get; private set; } = null!;

    [Notify] public ICommand ReadCheckCommand { get; private set; } = null!;

    [Notify] public ICommand CopyCommand { get; private set; } = null!;

    [Notify] public ICommand ChangePasswordCommand { get; private set; } = null!;

    #endregion
}
