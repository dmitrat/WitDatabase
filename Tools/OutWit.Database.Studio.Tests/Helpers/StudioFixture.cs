using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.Core.Builder;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.Helpers;

/// <summary>
/// Which storage the fixture's database uses. The three are not interchangeable - an LSM database is
/// a directory, an in-memory one has no path at all - and every one of them has had a defect that the
/// other two did not.
/// </summary>
public enum StudioStorage
{
    BTree,
    Lsm,
    InMemory
}

/// <summary>
/// A whole Studio over a real database, for tests that want to know what the application does rather
/// than what a double was told to say.
///
/// Everything here is the shipping type: <see cref="ConnectionManager"/>, <see cref="DatabaseSession"/>,
/// <see cref="SettingsService"/>, <see cref="ExportService"/>, the real ViewModel graph. Only two
/// things are stood in for, and both are people rather than services - the answer to a confirmation
/// dialog, and the file picker, which the ConnectionViewModel reaches through properties a test can
/// set directly.
///
/// Written because 249 of Studio's tests used to drive a permanently disconnected double: after any
/// change to the connection, the schema or the write paths, a green suite meant nothing. It also
/// matches the lifetime production has - ONE ConnectionManager, reused - which is what let phase 13's
/// worst defect hide from a fixture that gave every case a fresh service.
///
/// Since stage 2 it opens MORE THAN ONE database: <see cref="OpenAnotherAsync"/> gives a second, fully
/// independent connection through the same manager, which is the only way to ask whether a tab runs in
/// its own connection (WS-3) and whether disconnecting one leaves the others alone (WS-13).
/// </summary>
public sealed class StudioFixture : IAsyncDisposable
{
    #region Constants

    /// <summary>
    /// The schema every fixture gets: a table with an autoincrement key, a second one with a foreign
    /// key and an index, a trigger writing to a third, a view, and - deliberately - a table with NO
    /// primary key, because "this table cannot be edited" is a case the editor has to get right.
    /// </summary>
    private static readonly string[] SCHEMA =
    [
        "CREATE TABLE Customers (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100) NOT NULL, Email VARCHAR(255))",
        "CREATE TABLE Orders (Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL REFERENCES Customers(Id), Total DECIMAL(18,2) NOT NULL, Status VARCHAR(32) DEFAULT 'new')",
        "CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId)",
        "CREATE TABLE OrdersAudit (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderId INTEGER NOT NULL)",
        "CREATE TRIGGER TR_Orders_Audit AFTER INSERT ON Orders FOR EACH ROW BEGIN INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id); END",
        "CREATE VIEW ActiveOrders AS SELECT Id, CustomerId, Total FROM Orders WHERE Status <> 'archived'",
        "CREATE TABLE Logs (Message VARCHAR(200), Level VARCHAR(10))"
    ];

    private static readonly string[] DATA =
    [
        "INSERT INTO Customers (Name, Email) VALUES ('Northwind Trading', 'ops@nw.example')",
        "INSERT INTO Customers (Name, Email) VALUES ('Acme Industrial', 'buy@acme.example')",
        "INSERT INTO Customers (Name, Email) VALUES ('Vector Supply', NULL)",
        "INSERT INTO Orders (CustomerId, Total, Status) VALUES (1, 4812.50, 'new')",
        "INSERT INTO Orders (CustomerId, Total, Status) VALUES (1, 1204.00, 'shipped')",
        "INSERT INTO Orders (CustomerId, Total, Status) VALUES (2, 2900.75, 'new')",
        "INSERT INTO Logs (Message, Level) VALUES ('connection opened', 'INFO')",
        "INSERT INTO Logs (Message, Level) VALUES ('connection opened', 'INFO')"
    ];

    public const int CUSTOMER_COUNT = 3;
    public const int ORDER_COUNT = 3;

    #endregion

    #region Constructors

    private StudioFixture(string root, string databasePath, StudioStorage storage)
    {
        Root = root;
        DatabasePath = databasePath;
        Storage = storage;

        Connections = new ConnectionManager(NullLoggerFactory.Instance, NullLogger<ConnectionManager>.Instance);

        Settings = new SettingsService(
            NullLogger<SettingsService>.Instance,
            Path.Combine(root, "settings", "settings.json"));

        // The saved connections, in the fixture's own folder for the same reason the settings are: a
        // test that exercises them honestly must not write into the developer's list.
        Profiles = new ConnectionProfileStore(
            NullLogger<ConnectionProfileStore>.Instance,
            Path.Combine(root, "settings", "connections.json"));

        Confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);

        // The real history service over a store inside the fixture's own folder - never the developer's
        // own history, for the same reason SettingsService takes its path as a parameter.
        History = new QueryHistoryService(
            Path.Combine(root, "history", "history.witdb"),
            NullLogger<QueryHistoryService>.Instance);

        App = new ApplicationViewModel(
            Connections,
            Settings,
            new ExportService(),
            NullLogger<ApplicationViewModel>.Instance,
            Confirmations,
            history: History,
            profiles: Profiles);
    }

    #endregion

    #region Creation

    /// <summary>
    /// Builds a Studio over a fresh database. Without <paramref name="withSchema"/> the database is
    /// empty - which is its own case, since an empty database is what a user sees after Create.
    /// </summary>
    public static async Task<StudioFixture> CreateAsync(
        StudioStorage storage = StudioStorage.BTree,
        bool withSchema = true,
        bool connect = true,
        bool withHistory = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitStudio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var databasePath = PathFor(root, storage, "studio");

        var fixture = new StudioFixture(root, databasePath, storage);

        // Off by default: the history is a second database, and most cases have nothing to do with it.
        if (withHistory)
            await fixture.History.InitializeAsync();

        if (connect)
        {
            var session = await fixture.ConnectAsync();

            if (withSchema)
                await CreateSchemaAsync(session);
        }

        return fixture;
    }

    /// <summary>
    /// Connects the way the application does - through ConnectionInfo and the manager, so the
    /// connection string is the one the dialog would have built.
    /// </summary>
    public async Task<IDatabaseSession> ConnectAsync()
    {
        return await OpenAsync(DatabasePath, Storage);
    }

    /// <summary>
    /// Opens a SECOND (third, fourth) database through the same manager, with its own file and its own
    /// schema. This is what makes "the tab ran in the right one" a question that can be asked.
    /// </summary>
    public async Task<IDatabaseSession> OpenAnotherAsync(
        string name,
        StudioStorage storage = StudioStorage.BTree,
        bool withSchema = true)
    {
        var session = await OpenAsync(PathFor(Root, storage, name), storage);

        if (withSchema)
            await CreateSchemaAsync(session);

        return session;
    }

    private async Task<IDatabaseSession> OpenAsync(string path, StudioStorage storage)
    {
        var connection = new ConnectionInfo
        {
            FilePath = path,
            StorageEngine = storage == StudioStorage.Lsm ? "lsm" : "btree"
        };

        var session = await Connections.OpenAsync(connection);

        if (session == null)
            throw new InvalidOperationException($"The fixture could not open its own database at {path}.");

        return session;
    }

    /// <summary>
    /// Builds a database at <paramref name="path"/> and closes it again, for the cases that ask what
    /// Studio can find out about a file <b>nothing has open</b>.
    ///
    /// <para>
    /// Through <c>WitDatabaseBuilder</c> and closed immediately, rather than through the fixture's own
    /// connection: since 12.2.0 an open database is held under an exclusive file lock, and a probe that
    /// has to read the header would be measuring the lock rather than the file.
    /// </para>
    /// </summary>
    public static void CreateDatabaseOnDisk(string path, string? password = null)
    {
        var folder = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        var builder = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree();

        if (!string.IsNullOrEmpty(password))
            builder = builder.WithEncryption(password);

        using var database = builder.Build();

        database.Put("probe"u8.ToArray(), "value"u8.ToArray());
    }

    /// <summary>
    /// The same, for an LSM database - which is a FOLDER, and is therefore the case where a probe can
    /// know more about an encrypted database than it can about an encrypted file.
    /// </summary>
    public static void CreateLsmDatabaseOnDisk(string path, string? password = null)
    {
        Directory.CreateDirectory(path);

        var builder = new WitDatabaseBuilder()
            .WithLsmTree(path)
            .WithMvcc();

        if (!string.IsNullOrEmpty(password))
            builder = builder.WithEncryption(password);

        using var database = builder.Build();

        database.Put("probe"u8.ToArray(), "value"u8.ToArray());
    }

    private static string PathFor(string root, StudioStorage storage, string name) => storage switch
    {
        StudioStorage.BTree => Path.Combine(root, $"{name}.witdb"),
        StudioStorage.Lsm => Path.Combine(root, $"{name}-lsm"),
        _ => ":memory:"
    };

    public static async Task CreateSchemaAsync(IDatabaseSession session)
    {
        foreach (var statement in SCHEMA.Concat(DATA))
        {
            try
            {
                await session.ExecuteNonQueryAsync(statement);
            }
            catch (Exception ex)
            {
                // A fixture that quietly ships half a schema turns every test above it into a
                // question about the fixture.
                throw new InvalidOperationException(
                    $"The fixture's schema was refused by the engine: {statement}", ex);
            }
        }
    }

    #endregion

    #region Driving

    /// <summary>
    /// Runs an async RelayCommand and waits for it. Those commands are 'async void', so IsExecuting is
    /// the only handle on completion - asserting straight after Execute reads the state before the
    /// work is done.
    /// </summary>
    public static async Task PressAsync(System.Windows.Input.ICommand command, object? parameter = null)
    {
        command.Execute(parameter);

        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (IsRunning(command))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("A command did not complete within 60 seconds.");

            await Task.Delay(5);
        }
    }

    private static bool IsRunning(System.Windows.Input.ICommand command) => command switch
    {
        RelayCommandAsync async => async.IsExecuting,
        _ => command.GetType().GetProperty("IsExecuting")?.GetValue(command) as bool? ?? false
    };

    /// <summary>
    /// Reads rows back by scanning them, in the connection given - the primary one by default. Never
    /// through COUNT(*): on this engine a count is separate state that can disagree with the rows.
    /// </summary>
    public async Task<int> CountRowsAsync(string table, IDatabaseSession? session = null)
    {
        var target = session ?? Database;
        var result = await target.ExecuteQueryAsync($"SELECT * FROM {table}");

        if (!string.IsNullOrEmpty(result.ErrorMessage))
            throw new InvalidOperationException($"Reading {table} failed: {result.ErrorMessage}");

        return result.Data?.Rows.Count ?? -1;
    }

    #endregion

    #region Properties

    public string Root { get; }

    public string DatabasePath { get; }

    public StudioStorage Storage { get; }

    /// <summary>
    /// The one manager, reused for every connection - the lifetime the application has.
    /// </summary>
    public ConnectionManager Connections { get; }

    /// <summary>
    /// The first database opened. Named <c>Database</c> because that is what it was when there could
    /// only be one, and most cases still only need one.
    /// </summary>
    public IDatabaseSession Database => Connections.Sessions.FirstOrDefault()
        ?? throw new InvalidOperationException("The fixture has no open connection.");

    public SettingsService Settings { get; }

    /// <summary>The saved connections (WS-68), in the fixture's own folder.</summary>
    public ConnectionProfileStore Profiles { get; }

    public ScriptedConfirmationService Confirmations { get; }

    /// <summary>
    /// The real query history, over a store inside this fixture's folder (WS-29). Only opened when a
    /// case asks for it with <c>withHistory</c>.
    /// </summary>
    public QueryHistoryService History { get; }

    public ApplicationViewModel App { get; }

    public MainWindowViewModel MainWindow => App.MainWindowVm;

    public WorkspaceTabsViewModel Workspace => App.WorkspaceTabsVm;

    public DatabaseExplorerViewModel Explorer => App.DatabaseExplorerVm;

    public ConnectionViewModel Connection => App.ConnectionVm;

    public QueryTabViewModel FirstQueryTab => (QueryTabViewModel)App.WorkspaceTabsVm.Tabs[0];

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        try
        {
            await History.DisposeAsync();
        }
        catch
        {
            // the fixture is being torn down; a failure here must not mask the test's own result
        }

        try
        {
            await Connections.CloseAllAsync();
        }
        catch
        {
            // the fixture is being torn down; a failure here must not mask the test's own result
        }

        Connections.Dispose();

        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // a leaked handle is a finding, not a reason to fail the teardown
        }
    }

    #endregion
}
