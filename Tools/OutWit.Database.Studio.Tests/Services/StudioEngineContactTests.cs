using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.AdoNet;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Instrument S - what Studio's dialogs actually build, and whether the dialog can open again what it
/// just made.
///
/// Written when every other fixture in this project drove a double that was permanently
/// disconnected and answers every question with an empty collection, so no configuration a dialog can
/// express has ever reached the engine. This one drives the REAL <see cref="ConnectionViewModel"/> - the
/// same instance the dialog binds to - over a REAL <see cref="ConnectionManager"/>, and asks the question
/// a user asks: I filled this dialog in, put data in, and came back. Is it there?
///
/// The file picker is the only thing replaced, and it is replaced by what its bindings write:
/// ConnectionInfo.FilePath, and ApplyAutoDetectedSettings - the same method the Browse button calls.
/// Everything else - the builder calls, the connection string, the order - is the shipping code.
///
/// The round trip is deliberate. Asking only "did Connect succeed" reports success for a dialog that
/// creates one database and connects to a different one, which is what two of these cases do.
/// </summary>
[TestFixture]
public class StudioEngineContactTests
{
    #region Constants

    private const int PROBE_ROWS = 8;

    #endregion

    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioContact", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // a leaked handle is a finding, not a reason to fail the teardown
        }
    }

    #endregion

    #region Harness

    /// <summary>
    /// Builds the real ViewModel graph over a real ConnectionManager. Nothing here is a test double
    /// except the settings and export services, which the connection path does not touch.
    /// </summary>
    private static (ApplicationViewModel App, ConnectionViewModel Vm, ConnectionManager Connections) NewStudio()
    {
        var connections = new ConnectionManager(NullLoggerFactory.Instance,
            NullLogger<ConnectionManager>.Instance);

        var app = new ApplicationViewModel(
            connections,
            new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(Path.GetTempPath(), "WitStudioTests", Guid.NewGuid().ToString("N"), "settings.json")),
            new ExportService(),
            // The saved connections, in this run's own folder. Without a store of its own the
            // ViewModel used to fall back to the real one in %AppData% and leave a row there.
            new ConnectionProfileStore(NullLogger<ConnectionProfileStore>.Instance,
                Path.Combine(Path.GetTempPath(), "WitStudioTests", Guid.NewGuid().ToString("N"), "connections.json")),
            NullLogger<ApplicationViewModel>.Instance);

        return (app, app.ConnectionVm, connections);
    }

    /// <summary>
    /// The connection the dialog just opened. There is no "the connection" any more - the manager
    /// holds a collection - so these cases ask about the active one, which is the one the dialog made.
    /// </summary>
    private static IDatabaseSession Session(ConnectionManager connections)
    {
        return connections.Active
            ?? throw new InvalidOperationException("nothing is open: the connection attempt failed");
    }

    private static bool IsConnected(ConnectionManager connections)
    {
        return connections.Active?.IsConnected == true;
    }

    /// <summary>
    /// Presses the dialog's Open/Create button and waits for it to finish. RelayCommandAsync is
    /// 'async void', so IsExecuting is the only handle on completion.
    /// </summary>
    private static async Task PressConnectAsync(ConnectionViewModel vm)
    {
        var command = (RelayCommandAsync)vm.ConnectCommand;

        command.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (command.IsExecuting)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The connect command did not complete within 60 seconds.");

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Presses the X on a tab and waits. Closing became asynchronous when it started asking about
    /// unapplied work, and RelayCommandAsync is 'async void', so IsExecuting is the only handle on
    /// completion - asserting straight after Execute would read the state before the answer.
    /// </summary>
    private static async Task PressCloseTabAsync(WorkspaceTabsViewModel workspace, WorkspaceTabViewModel tab)
    {
        var command = (RelayCommandAsync<WorkspaceTabViewModel>)workspace.CloseTabCommand;

        command.Execute(tab);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (command.IsExecuting)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The close command did not complete within 60 seconds.");

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Puts a real database on disk without going through a dialog, for cases that need one to exist
    /// before the dialog is driven.
    /// </summary>
    private static async Task CreateOnDiskAsync(string path)
    {
        await using var connection = new WitDbConnection($"Data Source={path}");
        await connection.OpenAsync();
        await connection.CloseAsync();
    }

    private static string[] Listing(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        return Directory
            .GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(directory, entry))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task WriteProbeRowsAsync(IDatabaseSession db)
    {
        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");

        for (var i = 1; i <= PROBE_ROWS; i++)
            await db.ExecuteNonQueryAsync($"INSERT INTO Probe (Id, Name) VALUES ({i}, 'row-{i}')");
    }

    /// <summary>
    /// Reads the rows back by scanning them. Never asks for a count - on this engine a count is
    /// separate state that can disagree with the rows.
    /// </summary>
    private static async Task<(int Rows, string? Error)> ReadProbeRowsAsync(IDatabaseSession db)
    {
        var result = await db.ExecuteQueryAsync("SELECT Id, Name FROM Probe");

        if (!string.IsNullOrEmpty(result.ErrorMessage))
            return (-1, result.ErrorMessage);

        return (result.Data?.Rows.Count ?? -1, null);
    }

    #endregion

    #region Round trip

    private sealed record RoundTrip(
        bool Created,
        string? CreateError,
        string CreateConnectionString,
        string[] FilesOnDisk,
        bool Reopened,
        string? ReopenError,
        string ReopenConnectionString,
        int RowsAfterReopen,
        string? ReadError);

    /// <summary>
    /// The whole user story for one configuration: fill the Create dialog in, write rows, close, then
    /// fill the Open dialog in with the same path and read them back.
    /// </summary>
    private async Task<RoundTrip> RoundTripAsync(
        string caseName,
        string storageEngine,
        int storageType = 0,
        string? password = null,
        int pageSize = 4096,
        int cacheSize = 1000,
        bool transactions = true,
        bool mvcc = true,
        bool fileLocking = true)
    {
        var caseDirectory = Path.Combine(m_root, caseName);
        Directory.CreateDirectory(caseDirectory);

        var filePath = Path.Combine(caseDirectory, "created.witdb");

        // ---------- Create ----------
        var (_, createVm, createDb) = NewStudio();

        createVm.IsNewDatabase = true;
        createVm.StorageType = storageType;
        createVm.SelectedStorageEngine = storageEngine;
        createVm.SelectedPageSize = pageSize;
        createVm.CacheSize = cacheSize;
        createVm.EnableTransactions = transactions;
        createVm.EnableMvcc = mvcc;
        createVm.EnableFileLocking = fileLocking;

        if (storageType == 0)
            createVm.ConnectionInfo.FilePath = filePath;

        if (password != null)
        {
            createVm.ConnectionInfo.IsEncrypted = true;
            createVm.ConnectionInfo.Password = password;
        }

        await PressConnectAsync(createVm);

        var createConnectionString = SafeConnectionString(createVm);
        var created = IsConnected(createDb);
        var createError = createVm.ErrorMessage;

        if (created)
            await WriteProbeRowsAsync(Session(createDb));

        await createDb.CloseAllAsync();
        createDb.Dispose();

        var filesOnDisk = Listing(caseDirectory);

        // ---------- Reopen ----------
        var (_, openVm, openDb) = NewStudio();

        openVm.IsNewDatabase = false;
        openVm.ConnectionInfo.FilePath = filePath;

        // Exactly what the Browse button does after the picker returns.
        openVm.ApplyAutoDetectedSettings(filePath);

        if (password != null)
        {
            openVm.ConnectionInfo.IsEncrypted = true;
            openVm.ConnectionInfo.Password = password;
        }

        await PressConnectAsync(openVm);

        var reopenConnectionString = SafeConnectionString(openVm);
        var reopened = IsConnected(openDb);
        var reopenError = openVm.ErrorMessage;

        var rows = -1;
        string? readError = null;

        if (reopened)
            (rows, readError) = await ReadProbeRowsAsync(Session(openDb));

        await openDb.CloseAllAsync();
        openDb.Dispose();

        var trip = new RoundTrip(
            created, createError, createConnectionString, filesOnDisk,
            reopened, reopenError, reopenConnectionString, rows, readError);

        Report(caseName, trip);
        return trip;
    }

    private static string SafeConnectionString(ConnectionViewModel vm)
    {
        try
        {
            return vm.ConnectionInfo.BuildConnectionString();
        }
        catch (Exception ex)
        {
            return $"(could not be built: {ex.Message})";
        }
    }

    private static void Report(string caseName, RoundTrip trip)
    {
        var report = new StringBuilder();

        report.AppendLine($"--- {caseName} ---");
        report.AppendLine($"  create  : {trip.CreateConnectionString}");
        report.AppendLine($"            connected={trip.Created} error={trip.CreateError ?? "(none)"}");
        report.AppendLine($"  on disk : {(trip.FilesOnDisk.Length == 0 ? "(nothing)" : string.Join(", ", trip.FilesOnDisk))}");
        report.AppendLine($"  reopen  : {trip.ReopenConnectionString}");
        report.AppendLine($"            connected={trip.Reopened} error={trip.ReopenError ?? "(none)"}");
        report.AppendLine($"  rows    : {trip.RowsAfterReopen} of {PROBE_ROWS}"
            + (trip.ReadError != null ? $"  ({trip.ReadError})" : ""));

        TestContext.Out.WriteLine(report.ToString());
    }

    #endregion

    #region Controls

    /// <summary>
    /// POSITIVE CONTROL. The default path a first-time user takes: a file, a B+Tree, defaults
    /// everywhere. Create, write, close, reopen, read. If this fails, the instrument is wrong and no
    /// other case it reports is evidence.
    /// </summary>
    [Test]
    public async Task ControlADefaultDatabaseRoundTripsItsRowsTest()
    {
        var trip = await RoundTripAsync("control-default", "btree");

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True,
                $"POSITIVE CONTROL FAILED at create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True,
                $"POSITIVE CONTROL FAILED at reopen: {trip.ReopenError}");
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS),
                $"POSITIVE CONTROL FAILED at read: {trip.ReadError}");
        });
    }

    /// <summary>
    /// NEGATIVE CONTROL. A file that is not a database must be refused. Without a case that fails,
    /// "connected = true" everywhere else is an assertion no run could fail.
    ///
    /// The first negative control tried was a path that does not exist, and it went green - see
    /// <see cref="TheOpenDialogCreatesADatabaseWhenThePathDoesNotExistTest"/>, which is where that
    /// went instead. It is a finding, not a control.
    /// </summary>
    [Test]
    public async Task ControlAFileThatIsNotADatabaseIsRefusedTest()
    {
        var garbage = Path.Combine(m_root, "garbage.witdb");
        await File.WriteAllTextAsync(garbage, "this is not a database, it is a text file");

        var (_, vm, connections) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = garbage;

        await PressConnectAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(IsConnected(connections), Is.False,
                "NEGATIVE CONTROL FAILED - the instrument reports a connection to a text file, so it "
                + "cannot tell success from failure.");
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty,
                "the dialog reported no error for a file it could not open");
        });

        connections.Dispose();
    }

    #endregion

    #region Findings - pinned as measured, with the inversion each fix must produce

    /// <summary>
    /// S2, FIXED. The Open dialog refuses a path that is not there.
    ///
    /// It used to be unable to fail: the engine creates a database it is asked to open and cannot
    /// find, so a user whose file had moved was told the open succeeded and shown an empty database -
    /// which reads as "my data is gone", and the natural next step writes a schema over nothing.
    ///
    /// The engine still creates on open, which is right for a provider and is asserted separately by
    /// AttributionTheEngineItselfCreatesOnOpenTest. The refusal belongs to the dialog, and the file
    /// must not be created as a side effect of being asked for.
    /// </summary>
    [Test]
    public async Task TheOpenDialogRefusesAPathThatDoesNotExistTest()
    {
        var absent = Path.Combine(m_root, "no-such-directory", "absent.witdb");

        var (_, vm, connections) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = absent;

        await PressConnectAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(IsConnected(connections), Is.False, "a database that does not exist must not open");
            // The wording is the design's (6.2) and comes from the string catalogue now. It no longer
            // repeats the path - that is in the box directly above it - and it points at the dialog
            // that WOULD create a database, because "there is nothing here" and "you meant Create" are
            // the same thought.
            Assert.That(vm.ErrorMessage, Does.Contain("Create database"),
                "the refusal must point at the dialog that creates one");
            Assert.That(File.Exists(absent), Is.False,
                "and it must not have created the file it refused to open");
        });

        connections.Dispose();
    }

    /// <summary>
    /// ATTRIBUTION for the test above: the creating is the engine's, not Studio's. Driven straight
    /// through WitDbConnection with no Studio code in the path, so the finding lands on the right
    /// component and the fix goes into the dialog rather than into the provider.
    /// </summary>
    [Test]
    public async Task AttributionTheEngineItselfCreatesOnOpenTest()
    {
        var absent = Path.Combine(m_root, "engine-attribution", "absent.witdb");

        await using var connection = new WitDbConnection($"Data Source={absent}");
        await connection.OpenAsync();

        Assert.That(File.Exists(absent), Is.True,
            "attribution: WitDbConnection creates a database that does not exist, with no Studio code "
            + "in the path - so the Open dialog inherits this rather than causing it.");
    }

    #endregion

    #region Round trips - one per option the Create dialog offers

    [Test]
    public async Task RoundTripBTreeTest()
    {
        var trip = await RoundTripAsync("btree", "btree");

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True, $"create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True, $"reopen: {trip.ReopenError}");
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS), $"read: {trip.ReadError}");
        });
    }

    [Test]
    public async Task RoundTripEncryptedTest()
    {
        var trip = await RoundTripAsync("encrypted", "btree", password: "probe-password");

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True, $"create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True, $"reopen: {trip.ReopenError}");
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS), $"read: {trip.ReadError}");
        });
    }

    [Test]
    public async Task RoundTripWithoutMvccTest()
    {
        var trip = await RoundTripAsync("no-mvcc", "btree", mvcc: false);

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True, $"create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True, $"reopen: {trip.ReopenError}");
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS), $"read: {trip.ReadError}");
        });
    }

    [Test]
    public async Task RoundTripWithoutTransactionsTest()
    {
        var trip = await RoundTripAsync("no-tx", "btree", transactions: false);

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True, $"create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True, $"reopen: {trip.ReopenError}");
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS), $"read: {trip.ReadError}");
        });
    }

    [Test]
    public async Task RoundTripWithoutFileLockingTest()
    {
        var trip = await RoundTripAsync("no-lock", "btree", fileLocking: false);

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True, $"create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True, $"reopen: {trip.ReopenError}");
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS), $"read: {trip.ReadError}");
        });
    }

    [Test]
    public async Task RoundTripNonDefaultPageSizeTest()
    {
        var trip = await RoundTripAsync("pagesize", "btree", pageSize: 16384);

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True, $"create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True, $"reopen: {trip.ReopenError}");
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS), $"read: {trip.ReadError}");
        });
    }

    /// <summary>
    /// INVERTED 2026-08-05, phase 0 / S3. This used to pin the litter: the Create dialog handed
    /// WithLsmTree the FOLDER the user picked a file in, building a second, empty LSM database beside
    /// the real one and abandoning it - choosing C:\Users\Me\Documents\mydb.witdb dropped
    /// provider.meta and wal.log into Documents.
    ///
    /// The chosen path is now the database itself. What is asserted is the same round trip plus the
    /// absence of the litter: the case directory holds ONE database, and it is the one that was asked
    /// for. The rows still come back - measured then, measured now.
    /// </summary>
    [Test]
    public async Task RoundTripLsmLeavesNothingBesideTheDatabaseTest()
    {
        var trip = await RoundTripAsync("lsm", "lsm");

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True, $"create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True, $"reopen: {trip.ReopenError}");
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS), $"read: {trip.ReadError}");

            Assert.That(trip.FilesOnDisk, Does.Not.Contain("provider.meta"),
                "an abandoned LSM database beside the chosen path is what this used to pin");
            Assert.That(trip.FilesOnDisk, Does.Not.Contain("wal.log"),
                "and its write-ahead log, in the user's own folder");

            // CONTROL: the database that WAS asked for is there, one level down. Without this,
            // "no provider.meta in the folder" would pass for a dialog that created nothing at all.
            Assert.That(trip.FilesOnDisk.Any(entry => entry.EndsWith("provider.meta", StringComparison.Ordinal)),
                Is.True, "CONTROL: the LSM database itself must exist under the chosen path");
        });
    }

    /// <summary>
    /// INVERTED 2026-08-05, phase 0 / S3, and CHANGED AGAIN in stage 9. In-memory combined with 'lsm'
    /// used to call WithLsmTree("."), writing a database into the PROCESS WORKING DIRECTORY - for an
    /// installed application, wherever it happened to be launched from.
    ///
    /// <para>
    /// Stage 0 refused the combination. Stage 9 removed the ability to express it: the storage is ONE
    /// choice of three (WS-48), so asking for memory drops the LSM choice rather than producing a pair
    /// that has to be refused. The assertion about the refusal MESSAGE is therefore gone - there is
    /// nothing left to refuse - and what is asserted instead is that nothing is written and that the
    /// two choices cannot both be held. See ChoosingInMemoryDropsTheLsmChoiceTest.
    /// </para>
    /// </summary>
    [Test]
    public async Task InMemoryWithLsmCannotBeExpressedAndWritesNothingTest()
    {
        var working = Directory.GetCurrentDirectory();
        var meta = Path.Combine(working, "provider.meta");
        var wal = Path.Combine(working, "wal.log");

        var metaExisted = File.Exists(meta);
        var walExisted = File.Exists(wal);

        var (_, vm, connections) = NewStudio();

        vm.IsNewDatabase = true;
        vm.StorageType = 1;
        vm.SelectedStorageEngine = "lsm";

        await PressConnectAsync(vm);

        var appeared = (!metaExisted && File.Exists(meta)) || (!walExisted && File.Exists(wal));

        try
        {
            if (!metaExisted && File.Exists(meta))
                File.Delete(meta);

            if (!walExisted && File.Exists(wal))
                File.Delete(wal);
        }
        catch
        {
            // leave it rather than fail the run
        }

        Assert.Multiple(() =>
        {
            Assert.That(appeared, Is.False,
                $"nothing may be written into the working directory ({working})");
            Assert.That(vm.SelectedStorageEngine, Is.Not.EqualTo("lsm"),
                "asking for memory drops the LSM choice - the pair cannot be held at once");
        });

        connections.Dispose();
    }

    /// <summary>
    /// INVERTED 2026-08-05, phase 0 / S6. The in-memory option used to build a database with
    /// WitDatabaseBuilder, dispose it, and then connect over 'Data Source=:memory:' - and every
    /// connection to ':memory:' gets its OWN private database, so everything the dialog configured was
    /// discarded and the user got a different, empty one.
    ///
    /// Nothing is built first now: the connection creates the database and owns it. The round trip is
    /// what proves there is only one - rows written through this connection are read back through it.
    /// </summary>
    [Test]
    public async Task InMemoryConnectsToTheDatabaseItWillUseTest()
    {
        var (_, vm, connections) = NewStudio();

        vm.IsNewDatabase = true;
        vm.StorageType = 1;
        vm.SelectedStorageEngine = "btree";

        await PressConnectAsync(vm);

        Assert.That(IsConnected(connections), Is.True, $"create: {vm.ErrorMessage}");

        await WriteProbeRowsAsync(Session(connections));
        var (rows, readError) = await ReadProbeRowsAsync(Session(connections));

        Assert.Multiple(() =>
        {
            Assert.That(vm.ConnectionInfo.BuildConnectionString(), Is.EqualTo("Data Source=:memory:"));
            Assert.That(rows, Is.EqualTo(PROBE_ROWS), $"read: {readError}");
        });

        await connections.CloseAllAsync();
        connections.Dispose();
    }

    #endregion

    #region Switching databases - found by driving the application, not by this instrument

    /// <summary>
    /// S1, FIXED. Switching databases keeps every view in step with the service.
    ///
    /// Before the fix: open a database, then open another without closing the first, and Studio showed
    /// the second database's node in the explorer, added the path to Recent Files, and reported
    /// "Connected: False" with the welcome screen back and Close Database disabled. Switching
    /// databases required restarting the application.
    ///
    /// Found by driving the shipping executable, NOT by this fixture, and that is the lesson: every
    /// other case here builds a fresh service, while the application registers ONE as a singleton and
    /// reuses it for every open. An instrument that gives each case a clean service cannot see a defect
    /// that only exists on the second use of a dirty one. This case still uses ONE manager for both
    /// opens, for exactly that reason.
    ///
    /// Both databases exist here, so the defect is about the second open and not about the first
    /// database being absent - that was isolated by running the same switch between two databases
    /// that both already existed.
    ///
    /// THE MECHANISM, which was never that the connection failed - the connection always succeeded.
    /// It was the EVENT the whole user interface binds to. ConnectAsync captured
    /// 'wasConnected = IsConnected' (true) and then called DisconnectAsync, which raised
    /// ConnectionStatusChanged(false) from its own comparison. The new connection opened, and
    /// ConnectAsync compared the now-true IsConnected against the stale 'wasConnected' captured
    /// BEFORE the disconnect - equal, so nothing was raised. The last thing the interface heard was
    /// 'false', while the service was connected.
    ///
    /// The fix removes the captured value rather than patching the one call site: the service now
    /// compares against the last status it actually delivered, so no caller can get this wrong again.
    ///
    /// This test asserts the EVENT STREAM rather than IsConnected, because IsConnected was correct
    /// throughout and asserting it is what made the first version of this test pass against the
    /// defect.
    ///
    /// REWRITTEN for stage 2, and the assertion is stronger than it was. Opening a second database no
    /// longer closes the first: each connection has its own session with its own status event, so the
    /// first session must hear [true] and NOTHING else while the second one opens (WS-3, WS-13). The
    /// old expectation - true, false, true - described a defect one level up from S1: switching
    /// databases instead of adding one.
    /// </summary>
    [Test]
    public async Task OpeningASecondDatabaseKeepsEveryViewInStepTest()
    {
        var first = Path.Combine(m_root, "first.witdb");
        var second = Path.Combine(m_root, "second.witdb");

        // Both databases must already exist - the Open dialog refuses a path that is not there since
        // S2 was fixed, and the first version of this test was quietly relying on that defect.
        await CreateOnDiskAsync(first);
        await CreateOnDiskAsync(second);

        // One manager and one ViewModel for the whole session - what Program.cs registers.
        var (app, vm, connections) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = first;
        await PressConnectAsync(vm);

        Assert.That(IsConnected(connections), Is.True,
            $"the first open must succeed for this case to mean anything: {vm.ErrorMessage}");

        var firstSession = Session(connections);

        // This is what the first database's own views bind to now.
        var observedFirst = new List<bool>();
        firstSession.StatusChanged += (_, connected) => observedFirst.Add(connected);

        await firstSession.ExecuteNonQueryAsync("CREATE TABLE First (Id INTEGER PRIMARY KEY)");

        // The user picks File -> Open Database again.
        vm.ConnectionInfo.FilePath = second;
        await PressConnectAsync(vm);

        var secondSession = Session(connections);

        var rowsInFirst = await firstSession.ExecuteQueryAsync("SELECT * FROM First");

        TestContext.Out.WriteLine(
            $"the first connection heard: [{string.Join(", ", observedFirst)}]; "
            + $"{connections.Sessions.Count} connections open");

        Assert.Multiple(() =>
        {
            Assert.That(connections.Sessions, Has.Count.EqualTo(2),
                "opening a second database ADDS a connection");

            Assert.That(secondSession, Is.Not.SameAs(firstSession),
                "and it is a different connection, not the same one pointed elsewhere");

            Assert.That(firstSession.IsConnected, Is.True,
                "the first database is still open");

            Assert.That(observedFirst, Is.Empty,
                "the first connection heard nothing at all: it did not close, and it is not the "
                + "one that opened");

            Assert.That(rowsInFirst.ErrorMessage, Is.Null.Or.Empty,
                "and it can still be read - the table created in it is still its own");

            Assert.That(app.MainWindowVm.IsConnected, Is.True,
                "the interface must agree with the service");
        });
    }

    /// <summary>
    /// S12, FIXED. A dialog reopened after a failed attempt used to come back still showing the old
    /// error - InitDefault replaced ConnectionInfo and everything else, but not ErrorMessage. Found by
    /// driving the application: a refused open left its message sitting on the next fresh dialog.
    /// </summary>
    [Test]
    public async Task ReopeningTheDialogClearsTheErrorFromTheLastAttemptTest()
    {
        var (_, vm, connections) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = Path.Combine(m_root, "absent.witdb");

        await PressConnectAsync(vm);

        Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty,
            "the failed attempt must have produced an error for this case to mean anything");

        // What ShowOpenDialogAsync / ShowCreateDialogAsync do before showing the dialog again.
        vm.ResetForNewDialog();

        Assert.That(vm.ErrorMessage, Is.Null,
            "a freshly opened dialog must not show the previous attempt's error");

        connections.Dispose();
    }

    #endregion

    #region The table editor - unsaved changes

    /// <summary>
    /// INVERTED 2026-08-05, phase 0 / B6. This used to pin the defect: closing a table-edit tab with
    /// unsaved changes discarded them, without asking and without saying anything, because
    /// `TableEditTabViewModel.CanClose()` returned true over a `// TODO: Show confirmation dialog`.
    ///
    /// Now the close asks, and the answer here is Cancel: the tab stays, the buffer stays, the
    /// database is untouched. It still goes through the real close path (the tab strip's
    /// CloseTabCommand) rather than calling a method directly, because what matters is what happens
    /// when a user presses the X.
    /// </summary>
    [Test]
    public async Task ClosingATableEditorWithUnsavedChangesAsksBeforeLosingThemTest()
    {
        var path = Path.Combine(m_root, "editor.witdb");
        await CreateOnDiskAsync(path);

        var (app, vm, connections) = NewStudio();

        var confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);
        app.Confirmations = confirmations;

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = path;
        await PressConnectAsync(vm);

        Assert.That(IsConnected(connections), Is.True, $"setup: {vm.ErrorMessage}");

        await Session(connections).ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await Session(connections).ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        // A second tab has to exist: CloseTab refuses to close the last one.
        var workspace = app.WorkspaceTabsVm;
        var editor = await workspace.OpenTableEditTabAsync(Session(connections), "Probe");

        Assert.That(editor.EditableData, Is.Not.Null, "the editor loaded no data");
        Assert.That(editor.EditableData!.Rows.Count, Is.EqualTo(1));

        // Edit a cell exactly as the grid does: change the value, then tell the ViewModel.
        var rowView = new System.Data.DataView(editor.EditableData)[0];
        rowView.Row["Name"] = "edited-but-never-saved";
        editor.CellEditedCommand.Execute(rowView);

        Assert.That(editor.HasChanges, Is.True,
            "the edit must register as a change for this case to mean anything");

        // The user presses the X on the tab.
        await PressCloseTabAsync(workspace, editor);

        Assert.Multiple(() =>
        {
            Assert.That(confirmations.TimesAsked, Is.EqualTo(1),
                "the close must ask - a close that decides on its own is the defect, whichever way it decides");
            Assert.That(confirmations.LastChangeCount, Is.EqualTo(1),
                "the question names the size of the edit buffer");

            Assert.That(workspace.Tabs, Does.Contain(editor),
                "the answer was Cancel, so the tab stays open");
            Assert.That(editor.EditableData, Is.Not.Null,
                "the edit buffer must survive a refused close - OnClosed disposes it");
            Assert.That(editor.HasChanges, Is.True);
        });

        var result = await Session(connections).ExecuteQueryAsync("SELECT Name FROM Probe");

        Assert.That(result.Data!.Rows[0]["Name"], Is.EqualTo("original"),
            "Cancel writes nothing: the edit is still only in the buffer");

        await connections.CloseAllAsync();
        connections.Dispose();
    }

    /// <summary>
    /// The other two answers, over the same real close path. Discard must lose the buffer and keep the
    /// database; Apply must write it.
    ///
    /// Both directions matter: with only the Cancel case above, an implementation that refused every
    /// close would pass, and a tab that can never be closed is its own defect.
    /// </summary>
    [TestCase(UnsavedChangesDecision.Discard, "original", TestName = "DiscardingUnsavedChangesClosesTheTabAndWritesNothingTest")]
    [TestCase(UnsavedChangesDecision.Apply, "edited-and-applied", TestName = "ApplyingOnCloseWritesTheChangeAndClosesTheTabTest")]
    public async Task ClosingADirtyEditorHonoursTheAnswer(UnsavedChangesDecision decision, string expected)
    {
        var path = Path.Combine(m_root, $"editor-{decision}.witdb");
        await CreateOnDiskAsync(path);

        var (app, vm, connections) = NewStudio();

        var confirmations = new ScriptedConfirmationService(decision);
        app.Confirmations = confirmations;

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = path;
        await PressConnectAsync(vm);

        Assert.That(IsConnected(connections), Is.True, $"setup: {vm.ErrorMessage}");

        await Session(connections).ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await Session(connections).ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        var workspace = app.WorkspaceTabsVm;
        var editor = await workspace.OpenTableEditTabAsync(Session(connections), "Probe");

        var rowView = new System.Data.DataView(editor.EditableData!)[0];
        rowView.Row["Name"] = "edited-and-applied";
        editor.CellEditedCommand.Execute(rowView);

        Assert.That(editor.HasChanges, Is.True);

        await PressCloseTabAsync(workspace, editor);

        Assert.Multiple(() =>
        {
            Assert.That(confirmations.TimesAsked, Is.EqualTo(1));
            Assert.That(workspace.Tabs, Does.Not.Contain(editor), "the answer allowed the close");
        });

        var result = await Session(connections).ExecuteQueryAsync("SELECT Name FROM Probe");

        Assert.That(result.Data!.Rows[0]["Name"], Is.EqualTo(expected));

        await connections.CloseAllAsync();
        connections.Dispose();
    }

    /// <summary>
    /// CONTROL. A clean tab must close with no question at all - otherwise the fix would have turned
    /// every close into a dialog, and the count above would never distinguish the two.
    /// </summary>
    [Test]
    public async Task ControlAClosingTabWithNoChangesIsNotAskedAboutTest()
    {
        var path = Path.Combine(m_root, "editor-clean.witdb");
        await CreateOnDiskAsync(path);

        var (app, vm, connections) = NewStudio();

        var confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);
        app.Confirmations = confirmations;

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = path;
        await PressConnectAsync(vm);

        await Session(connections).ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await Session(connections).ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        var workspace = app.WorkspaceTabsVm;
        var editor = await workspace.OpenTableEditTabAsync(Session(connections), "Probe");

        Assert.That(editor.HasChanges, Is.False, "nothing was edited");

        await PressCloseTabAsync(workspace, editor);

        Assert.Multiple(() =>
        {
            Assert.That(confirmations.TimesAsked, Is.Zero,
                "CONTROL: a tab with nothing to lose must close without a dialog");
            Assert.That(workspace.Tabs, Does.Not.Contain(editor));
        });

        await connections.CloseAllAsync();
        connections.Dispose();
    }

    /// <summary>
    /// POSITIVE CONTROL for the test above, and it is the one that makes it mean anything.
    ///
    /// "The database still says 'original'" would read the same way if the editor could not save at
    /// all. This drives the identical edit and presses Commit instead of the X: the new value must
    /// reach the database. Only with this green does the case above describe lost work rather than a
    /// broken editor.
    /// </summary>
    [Test]
    public async Task ControlCommittingATableEditDoesReachTheDatabaseTest()
    {
        var path = Path.Combine(m_root, "editor-control.witdb");
        await CreateOnDiskAsync(path);

        var (app, vm, connections) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = path;
        await PressConnectAsync(vm);

        Assert.That(IsConnected(connections), Is.True, $"setup: {vm.ErrorMessage}");

        await Session(connections).ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await Session(connections).ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        var editor = await app.WorkspaceTabsVm.OpenTableEditTabAsync(Session(connections), "Probe");

        var rowView = new System.Data.DataView(editor.EditableData!)[0];
        rowView.Row["Name"] = "committed";
        editor.CellEditedCommand.Execute(rowView);

        Assert.That(editor.HasChanges, Is.True);

        var commit = (RelayCommandAsync)editor.CommitCommand;
        commit.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (commit.IsExecuting)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Commit did not complete within 30 seconds.");

            await Task.Delay(10);
        }

        var result = await Session(connections).ExecuteQueryAsync("SELECT Name FROM Probe");

        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data!.Rows[0]["Name"], Is.EqualTo("committed"),
            "CONTROL FAILED - the editor cannot save at all, so the case above is not about lost work.");

        await connections.CloseAllAsync();
        connections.Dispose();
    }

    #endregion

    #region The dialog against the file - what 12.2.0 made the file remember

    /// <summary>
    /// S4, FIXED. The Open dialog used to offer Transactions, MVCC, File locking and a Storage Engine.
    /// BuildConnectionString emits none of the first three, so every one of those controls reached
    /// nothing: clearing "Enable MVCC" got an MVCC database and no message.
    ///
    /// They were removed rather than wired up, because since 12.2.0 the file records all four and
    /// supplies whatever the connection string does not name - so asking the user is asking them to
    /// override a correct answer with a guess.
    ///
    /// What this test guards is the property that made those controls dishonest, and it is still true:
    /// the Open path's connection string carries only what the user genuinely chooses. If a future
    /// change puts one of these keywords back into it, it must come with a control that works.
    /// </summary>
    [Test]
    public void TheOpenPathNamesOnlyWhatTheUserActuallyChoosesTest()
    {
        var (_, vm, connections) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = Path.Combine(m_root, "settings.witdb");
        vm.ConnectionInfo.IsReadOnly = true;

        var connectionString = vm.ConnectionInfo.BuildConnectionString();

        Assert.Multiple(() =>
        {
            // What the user did choose, and what the dialog still offers.
            Assert.That(connectionString, Does.Contain("Data Source="));
            Assert.That(connectionString, Does.Contain("Mode=ReadOnly"));

            // What the file supplies, and the dialog no longer asks for.
            Assert.That(connectionString, Does.Not.Contain("Transactions"));
            Assert.That(connectionString, Does.Not.Contain("MVCC"));
            Assert.That(connectionString, Does.Not.Contain("FileLocking"));
            Assert.That(connectionString, Does.Not.Contain("PageSize"));
            Assert.That(connectionString, Does.Not.Contain("CacheSize"));
        });

        connections.Dispose();
    }

    /// <summary>
    /// S5, FIXED. An LSM database is a DIRECTORY, and the Open dialog used a file picker with no
    /// folder option anywhere in the application - so Studio could create an LSM database and never
    /// reopen one. Typing the path did not help either: auto-detection guarded on File.Exists, which
    /// is false for a directory, so it silently did nothing for one of the two stores.
    ///
    /// The dialog now offers a Folder... button beside File..., and detection accepts both.
    /// </summary>
    [Test]
    public async Task AnLsmDatabaseIsDetectedAndOpenedFromItsDirectoryTest()
    {
        var directory = Path.Combine(m_root, "an-lsm-database");

        // A real LSM database, built by the engine rather than faked with a stub file.
        await using (var seed = new WitDbConnection($"Data Source={directory};Store=lsm"))
        {
            await seed.OpenAsync();

            await using var command = seed.CreateCommand();
            command.CommandText = "CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))";
            await command.ExecuteNonQueryAsync();

            command.CommandText = "INSERT INTO Probe (Id, Name) VALUES (1, 'alpha')";
            await command.ExecuteNonQueryAsync();

            await seed.CloseAsync();
        }

        var (_, vm, connections) = NewStudio();

        vm.IsNewDatabase = false;
        vm.UseAutoDetectedSettings = true;
        vm.ConnectionInfo.FilePath = directory;

        // What the Folder... button does after the picker returns.
        vm.ApplyAutoDetectedSettings(directory);

        Assert.That(vm.SelectedStorageEngine, Is.EqualTo("lsm"),
            "auto-detection must report 'lsm' for an LSM directory");

        await PressConnectAsync(vm);

        Assert.That(IsConnected(connections), Is.True, $"the LSM database must open: {vm.ErrorMessage}");

        var (rows, error) = await ReadProbeRowsAsync(Session(connections));

        Assert.That(error, Is.Null, "reading back from the reopened LSM database failed");
        Assert.That(rows, Is.EqualTo(1), "the row written before the close must come back");

        await connections.CloseAllAsync();
        connections.Dispose();
    }

    /// <summary>
    /// S5's other half: the dialog has to offer a way to choose a directory at all. A folder picker
    /// needs a window, so what is asserted here is that the command exists and is bound - the button
    /// in OpenDatabaseDialog.axaml binds to exactly this.
    /// </summary>
    [Test]
    public void TheOpenDialogOffersAFolderPickerTest()
    {
        var (_, vm, connections) = NewStudio();

        Assert.That(vm.BrowseFolderCommand, Is.Not.Null,
            "the Open dialog must offer a folder route, or an LSM database cannot be selected");

        connections.Dispose();
    }

    #endregion
}
