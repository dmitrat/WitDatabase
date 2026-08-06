using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Common.MVVM.Commands;
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
/// Everything here is the shipping type: <see cref="DatabaseService"/>, <see cref="SettingsService"/>,
/// <see cref="ExportService"/>, the real ViewModel graph. Only two things are stood in for, and both
/// are people rather than services - the answer to a confirmation dialog, and the file picker, which
/// the ConnectionViewModel reaches through properties a test can set directly.
///
/// Written because 249 of Studio's tests used to drive a permanently disconnected double: after any
/// change to the connection, the schema or the write paths, a green suite meant nothing. It also
/// matches the lifetime production has - ONE DatabaseService, reused - which is what let phase 13's
/// worst defect hide from a fixture that gave every case a fresh one.
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

        Database = new DatabaseService(NullLogger<DatabaseService>.Instance);

        Settings = new SettingsService(
            NullLogger<SettingsService>.Instance,
            Path.Combine(root, "settings", "settings.json"));

        Confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);

        App = new ApplicationViewModel(
            Database,
            Settings,
            new ExportService(),
            NullLogger<ApplicationViewModel>.Instance,
            Confirmations);
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
        bool connect = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitStudio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var databasePath = storage switch
        {
            StudioStorage.BTree => Path.Combine(root, "studio.witdb"),
            StudioStorage.Lsm => Path.Combine(root, "studio-lsm"),
            _ => ":memory:"
        };

        var fixture = new StudioFixture(root, databasePath, storage);

        if (connect)
        {
            await fixture.ConnectAsync();

            if (withSchema)
                await fixture.CreateSchemaAsync();
        }

        return fixture;
    }

    /// <summary>
    /// Connects the way the application does - through ConnectionInfo, so the connection string is the
    /// one the dialog would have built.
    /// </summary>
    public async Task ConnectAsync()
    {
        var connection = new ConnectionInfo
        {
            FilePath = DatabasePath,
            StorageEngine = Storage == StudioStorage.Lsm ? "lsm" : "btree"
        };

        var connected = await Database.ConnectAsync(connection);

        if (!connected)
            throw new InvalidOperationException($"The fixture could not open its own database at {DatabasePath}.");
    }

    public async Task CreateSchemaAsync()
    {
        foreach (var statement in SCHEMA.Concat(DATA))
        {
            try
            {
                await Database.ExecuteNonQueryAsync(statement);
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
    /// Reads rows back by scanning them. Never through COUNT(*) - on this engine a count is separate
    /// state that can disagree with the rows.
    /// </summary>
    public async Task<int> CountRowsAsync(string table)
    {
        var result = await Database.ExecuteQueryAsync($"SELECT * FROM {table}");

        if (!string.IsNullOrEmpty(result.ErrorMessage))
            throw new InvalidOperationException($"Reading {table} failed: {result.ErrorMessage}");

        return result.Data?.Rows.Count ?? -1;
    }

    #endregion

    #region Properties

    public string Root { get; }

    public string DatabasePath { get; }

    public StudioStorage Storage { get; }

    public DatabaseService Database { get; }

    public SettingsService Settings { get; }

    public ScriptedConfirmationService Confirmations { get; }

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
            await Database.DisconnectAsync();
        }
        catch
        {
            // the fixture is being torn down; a failure here must not mask the test's own result
        }

        Database.Dispose();

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
