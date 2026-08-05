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
/// Every other fixture in this project drives a <see cref="FakeDatabaseService"/> that is permanently
/// disconnected and answers every question with an empty collection, so no configuration a dialog can
/// express has ever reached the engine. This one drives the REAL <see cref="ConnectionViewModel"/> - the
/// same instance the dialog binds to - over a REAL <see cref="DatabaseService"/>, and asks the question
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
    /// Builds the real ViewModel graph over a real DatabaseService. Nothing here is a test double
    /// except the settings and export services, which the connection path does not touch.
    /// </summary>
    private static (ApplicationViewModel App, ConnectionViewModel Vm, DatabaseService Db) NewStudio()
    {
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);

        var app = new ApplicationViewModel(
            db,
            new FakeSettingsService(),
            new FakeExportService(),
            NullLogger<ApplicationViewModel>.Instance);

        return (app, app.ConnectionVm, db);
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

    private static async Task WriteProbeRowsAsync(DatabaseService db)
    {
        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");

        for (var i = 1; i <= PROBE_ROWS; i++)
            await db.ExecuteNonQueryAsync($"INSERT INTO Probe (Id, Name) VALUES ({i}, 'row-{i}')");
    }

    /// <summary>
    /// Reads the rows back by scanning them. Never asks for a count - on this engine a count is
    /// separate state that can disagree with the rows.
    /// </summary>
    private static async Task<(int Rows, string? Error)> ReadProbeRowsAsync(DatabaseService db)
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
        var created = createDb.IsConnected;
        var createError = createVm.ErrorMessage;

        if (created)
            await WriteProbeRowsAsync(createDb);

        await createDb.DisconnectAsync();
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
        var reopened = openDb.IsConnected;
        var reopenError = openVm.ErrorMessage;

        var rows = -1;
        string? readError = null;

        if (reopened)
            (rows, readError) = await ReadProbeRowsAsync(openDb);

        await openDb.DisconnectAsync();
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

        var (_, vm, db) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = garbage;

        await PressConnectAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(db.IsConnected, Is.False,
                "NEGATIVE CONTROL FAILED - the instrument reports a connection to a text file, so it "
                + "cannot tell success from failure.");
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty,
                "the dialog reported no error for a file it could not open");
        });

        db.Dispose();
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

        var (_, vm, db) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = absent;

        await PressConnectAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(db.IsConnected, Is.False, "a database that does not exist must not open");
            Assert.That(vm.ErrorMessage, Does.Contain("not found"),
                "the dialog must say which file it could not find");
            Assert.That(File.Exists(absent), Is.False,
                "and it must not have created the file it refused to open");
        });

        db.Dispose();
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
    /// PINS A DEFECT, NOT CORRECT BEHAVIOUR - and NOT the defect first looked for.
    ///
    /// The hypothesis was that lsm loses its rows, because the Create dialog builds the LSM store in
    /// Path.GetDirectoryName(FilePath) - the FOLDER the user picked a file in, not the file. It was
    /// measured instead of assumed, and it is wrong: all 8 rows come back. Two things save it, and
    /// neither is the dialog. The rows are written after the reconnect, so they land in the database
    /// the CONNECTION STRING builds ('Store=lsm' on the file path, which the engine makes a
    /// directory); and 12.2.0 restores the store from that directory's provider.meta sidecar, so the
    /// reopen - which names no store at all, because auto-detection guards on File.Exists and a
    /// directory is not a file - still gets an LSM database.
    ///
    /// What is left is real and this is what the test pins: WithLsmTree(parent) builds a SECOND,
    /// EMPTY LSM database in the folder the user chose, and abandons it. Picking
    /// C:\Users\Me\Documents\mydb.witdb drops provider.meta and wal.log into Documents.
    ///
    /// WHEN FIXED: the case directory holds one database - no provider.meta or wal.log beside the
    /// chosen file - and this assertion inverts.
    /// </summary>
    [Test]
    public async Task RoundTripLsmAbandonsASecondDatabaseInTheChosenFolderTest()
    {
        var trip = await RoundTripAsync("lsm", "lsm");

        Assert.Multiple(() =>
        {
            Assert.That(trip.Created, Is.True, $"create: {trip.CreateError}");
            Assert.That(trip.Reopened, Is.True, $"reopen: {trip.ReopenError}");

            // The rows survive - measured, against the hypothesis.
            Assert.That(trip.RowsAfterReopen, Is.EqualTo(PROBE_ROWS), $"read: {trip.ReadError}");

            // The litter is the defect.
            Assert.That(trip.FilesOnDisk, Does.Contain("provider.meta"),
                "PIN: an abandoned LSM database is expected beside the chosen file today. If this is "
                + "gone, the defect is fixed - invert this assertion.");
            Assert.That(trip.FilesOnDisk, Does.Contain("wal.log"),
                "PIN: the abandoned database's write-ahead log, in the user's own folder.");
        });
    }

    /// <summary>
    /// The same defect, at its worst: the in-memory option combined with 'lsm' calls
    /// WithLsmTree(".") - so an LSM database is built in the PROCESS WORKING DIRECTORY, which for an
    /// installed application is wherever it was launched from.
    ///
    /// WHEN FIXED: no database appears in the working directory.
    /// </summary>
    [Test]
    public async Task InMemoryWithLsmBuildsADatabaseInTheWorkingDirectoryTest()
    {
        var working = Directory.GetCurrentDirectory();
        var meta = Path.Combine(working, "provider.meta");
        var wal = Path.Combine(working, "wal.log");

        var metaExisted = File.Exists(meta);
        var walExisted = File.Exists(wal);

        var (_, vm, db) = NewStudio();

        vm.IsNewDatabase = true;
        vm.StorageType = 1;
        vm.SelectedStorageEngine = "lsm";

        await PressConnectAsync(vm);

        await db.DisconnectAsync();
        db.Dispose();

        var appeared = (!metaExisted && File.Exists(meta)) || (!walExisted && File.Exists(wal));

        // Clean up after the subject, since the subject does not.
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

        Assert.That(appeared, Is.True,
            "PIN: choosing in-memory + lsm is expected to write a database into the working directory "
            + $"({working}) today. If nothing appeared, the defect is fixed - invert this test.");
    }

    /// <summary>
    /// PINS A DEFECT, NOT CORRECT BEHAVIOUR.
    ///
    /// The in-memory option builds a database with WitDatabaseBuilder, disposes it, and then
    /// reconnects over 'Data Source=:memory:'. An in-memory database keeps nothing after the last
    /// connection closes, so everything the dialog configured is discarded and the user is connected
    /// to a different, empty database.
    ///
    /// WHEN FIXED: the connection outlives the dialog, or the option states that it is a scratch
    /// database.
    /// </summary>
    [Test]
    public async Task InMemoryConnectsToADifferentDatabaseThanItCreatedTest()
    {
        var (_, vm, db) = NewStudio();

        vm.IsNewDatabase = true;
        vm.StorageType = 1;
        vm.SelectedStorageEngine = "btree";

        await PressConnectAsync(vm);

        Assert.That(db.IsConnected, Is.True, $"create: {vm.ErrorMessage}");
        Assert.That(vm.ConnectionInfo.BuildConnectionString(), Is.EqualTo("Data Source=:memory:"),
            "PIN: the in-memory option reconnects over a bare ':memory:' connection string, so nothing "
            + "the dialog configured reaches the database the user ends up on.");

        await db.DisconnectAsync();
        db.Dispose();
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
    /// other case here builds a fresh DatabaseService, while the application registers ONE as a
    /// singleton and reuses it for every open. An instrument that gives each case a clean service
    /// cannot see a defect that only exists on the second use of a dirty one.
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

        // One service and one ViewModel for the whole session - what Program.cs registers.
        var (_, vm, db) = NewStudio();

        // This is what MainWindowViewModel and DatabaseExplorerViewModel bind to.
        var observed = new List<bool>();
        db.ConnectionStatusChanged += (_, connected) => observed.Add(connected);

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = first;
        await PressConnectAsync(vm);

        Assert.That(db.IsConnected, Is.True,
            $"the first open must succeed for this case to mean anything: {vm.ErrorMessage}");

        await db.ExecuteNonQueryAsync("CREATE TABLE First (Id INTEGER PRIMARY KEY)");

        // The user picks File -> Open Database again.
        vm.ConnectionInfo.FilePath = second;
        await PressConnectAsync(vm);

        TestContext.Out.WriteLine(
            $"status events the interface received: [{string.Join(", ", observed)}]");

        Assert.Multiple(() =>
        {
            Assert.That(db.IsConnected, Is.True,
                "the service is connected to the second database");

            Assert.That(observed[^1], Is.True,
                "the last status the interface hears must agree with the service");

            // connected -> disconnected -> connected. The middle one is real: the first database is
            // genuinely closed before the second opens, and the views should see that.
            Assert.That(observed, Is.EqualTo(new[] { true, false, true }).AsCollection,
                "the interface must be told about the second connection");
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
        var (_, vm, db) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = Path.Combine(m_root, "absent.witdb");

        await PressConnectAsync(vm);

        Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty,
            "the failed attempt must have produced an error for this case to mean anything");

        // What ShowOpenDialogAsync / ShowCreateDialogAsync do before showing the dialog again.
        vm.ResetForNewDialog();

        Assert.That(vm.ErrorMessage, Is.Null,
            "a freshly opened dialog must not show the previous attempt's error");

        db.Dispose();
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

        var (app, vm, db) = NewStudio();

        var confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);
        app.Confirmations = confirmations;

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = path;
        await PressConnectAsync(vm);

        Assert.That(db.IsConnected, Is.True, $"setup: {vm.ErrorMessage}");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        // A second tab has to exist: CloseTab refuses to close the last one.
        var workspace = app.WorkspaceTabsVm;
        var editor = await workspace.OpenTableEditTabAsync("Probe");

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

        var result = await db.ExecuteQueryAsync("SELECT Name FROM Probe");

        Assert.That(result.Data!.Rows[0]["Name"], Is.EqualTo("original"),
            "Cancel writes nothing: the edit is still only in the buffer");

        await db.DisconnectAsync();
        db.Dispose();
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

        var (app, vm, db) = NewStudio();

        var confirmations = new ScriptedConfirmationService(decision);
        app.Confirmations = confirmations;

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = path;
        await PressConnectAsync(vm);

        Assert.That(db.IsConnected, Is.True, $"setup: {vm.ErrorMessage}");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        var workspace = app.WorkspaceTabsVm;
        var editor = await workspace.OpenTableEditTabAsync("Probe");

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

        var result = await db.ExecuteQueryAsync("SELECT Name FROM Probe");

        Assert.That(result.Data!.Rows[0]["Name"], Is.EqualTo(expected));

        await db.DisconnectAsync();
        db.Dispose();
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

        var (app, vm, db) = NewStudio();

        var confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);
        app.Confirmations = confirmations;

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = path;
        await PressConnectAsync(vm);

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        var workspace = app.WorkspaceTabsVm;
        var editor = await workspace.OpenTableEditTabAsync("Probe");

        Assert.That(editor.HasChanges, Is.False, "nothing was edited");

        await PressCloseTabAsync(workspace, editor);

        Assert.Multiple(() =>
        {
            Assert.That(confirmations.TimesAsked, Is.Zero,
                "CONTROL: a tab with nothing to lose must close without a dialog");
            Assert.That(workspace.Tabs, Does.Not.Contain(editor));
        });

        await db.DisconnectAsync();
        db.Dispose();
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

        var (app, vm, db) = NewStudio();

        vm.IsNewDatabase = false;
        vm.ConnectionInfo.FilePath = path;
        await PressConnectAsync(vm);

        Assert.That(db.IsConnected, Is.True, $"setup: {vm.ErrorMessage}");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        var editor = await app.WorkspaceTabsVm.OpenTableEditTabAsync("Probe");

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

        var result = await db.ExecuteQueryAsync("SELECT Name FROM Probe");

        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data!.Rows[0]["Name"], Is.EqualTo("committed"),
            "CONTROL FAILED - the editor cannot save at all, so the case above is not about lost work.");

        await db.DisconnectAsync();
        db.Dispose();
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
        var (_, vm, db) = NewStudio();

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

        db.Dispose();
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

        var (_, vm, db) = NewStudio();

        vm.IsNewDatabase = false;
        vm.UseAutoDetectedSettings = true;
        vm.ConnectionInfo.FilePath = directory;

        // What the Folder... button does after the picker returns.
        vm.ApplyAutoDetectedSettings(directory);

        Assert.That(vm.SelectedStorageEngine, Is.EqualTo("lsm"),
            "auto-detection must report 'lsm' for an LSM directory");

        await PressConnectAsync(vm);

        Assert.That(db.IsConnected, Is.True, $"the LSM database must open: {vm.ErrorMessage}");

        var (rows, error) = await ReadProbeRowsAsync(db);

        Assert.That(error, Is.Null, "reading back from the reopened LSM database failed");
        Assert.That(rows, Is.EqualTo(1), "the row written before the close must come back");

        await db.DisconnectAsync();
        db.Dispose();
    }

    /// <summary>
    /// S5's other half: the dialog has to offer a way to choose a directory at all. A folder picker
    /// needs a window, so what is asserted here is that the command exists and is bound - the button
    /// in OpenDatabaseDialog.axaml binds to exactly this.
    /// </summary>
    [Test]
    public void TheOpenDialogOffersAFolderPickerTest()
    {
        var (_, vm, db) = NewStudio();

        Assert.That(vm.BrowseFolderCommand, Is.Not.Null,
            "the Open dialog must offer a folder route, or an LSM database cannot be selected");

        db.Dispose();
    }

    #endregion
}
