using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.AdoNet;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// B5 and the exit half of B6. There has to be exactly ONE way out of Studio, and it has to ask before
/// it loses anything.
///
/// File &gt; Exit used to call <c>Environment.Exit(0)</c>. That ends the process where it stands:
/// MainWindow.OnClosing never runs, so the window size is not saved and nothing is asked about
/// unapplied edits, and Program's service provider is never disposed - which since 12.2.0 leaves the
/// database under an exclusive file lock until the operating system reclaims the handle.
///
/// The old behaviour cannot be pinned by a test: a test that called it would take the test host with
/// it. It was measured the other way round instead - by restoring Environment.Exit and watching the
/// runner die, recorded in Docs/PHASE13-STUDIO-AUDIT.md - and what these tests hold is the fix.
/// </summary>
[TestFixture]
public class ShutdownPathTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioShutdown", Guid.NewGuid().ToString("N"));
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

    private static (ApplicationViewModel App, DatabaseService Db) NewStudio()
    {
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);

        var app = new ApplicationViewModel(
            db,
            new FakeSettingsService(),
            new FakeExportService(),
            NullLogger<ApplicationViewModel>.Instance);

        return (app, db);
    }

    private static async Task PressExitAsync(MainWindowViewModel vm)
    {
        var command = (RelayCommandAsync)vm.ExitCommand;

        command.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (command.IsExecuting)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The exit command did not complete within 60 seconds.");

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Connects to a real database with one row, opens the editor on it and edits a cell exactly as
    /// the grid does, leaving the tab dirty.
    /// </summary>
    private async Task<ApplicationViewModel> WithADirtyEditorAsync(DatabaseService db, ApplicationViewModel app, string name)
    {
        var path = Path.Combine(m_root, $"{name}.witdb");

        await using (var connection = new WitDbConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await connection.CloseAsync();
        }

        var connected = await db.ConnectAsync(new ConnectionInfo { FilePath = path });
        Assert.That(connected, Is.True, "setup: the database did not open");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'original')");

        var editor = await app.WorkspaceTabsVm.OpenTableEditTabAsync("Probe");

        var rowView = new System.Data.DataView(editor.EditableData!)[0];
        rowView.Row["Name"] = "edited-but-never-saved";
        editor.CellEditedCommand.Execute(rowView);

        Assert.That(editor.HasChanges, Is.True, "setup: the edit did not register");

        return app;
    }

    #endregion

    #region Tests

    [Test]
    public async Task ExitRequestsShutdownRatherThanEndingTheProcessTest()
    {
        var (app, db) = NewStudio();

        var requested = 0;
        app.ShutdownRequested += (_, _) => requested++;

        await PressExitAsync(app.MainWindowVm);

        Assert.That(requested, Is.EqualTo(1),
            "Exit must ask the host to close the window - the path that saves state and disposes the "
            + "connection - rather than ending the process itself");

        db.Dispose();
    }

    [Test]
    public async Task ExitWithUnappliedChangesCanBeCancelledTest()
    {
        var (app, db) = NewStudio();

        var confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);
        app.Confirmations = confirmations;

        await WithADirtyEditorAsync(db, app, "exit-cancel");

        var requested = 0;
        app.ShutdownRequested += (_, _) => requested++;

        await PressExitAsync(app.MainWindowVm);

        Assert.Multiple(() =>
        {
            Assert.That(confirmations.TimesAsked, Is.EqualTo(1), "leaving must ask about unapplied work");
            Assert.That(requested, Is.Zero, "the answer was Cancel: Studio stays open");
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    [Test]
    public async Task ExitAfterDiscardingUnappliedChangesGoesAheadTest()
    {
        var (app, db) = NewStudio();

        var confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Discard);
        app.Confirmations = confirmations;

        await WithADirtyEditorAsync(db, app, "exit-discard");

        var requested = 0;
        app.ShutdownRequested += (_, _) => requested++;

        await PressExitAsync(app.MainWindowVm);

        Assert.Multiple(() =>
        {
            Assert.That(confirmations.TimesAsked, Is.EqualTo(1));
            Assert.That(requested, Is.EqualTo(1), "the answer allowed the exit");
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    /// <summary>
    /// CONTROL. Without this, "leaving asks" would pass for an implementation that asks on every exit,
    /// including the ordinary one with nothing open - a dialog nobody can turn off.
    /// </summary>
    [Test]
    public async Task ControlLeavingWithNothingUnappliedAsksNothingTest()
    {
        var (app, db) = NewStudio();

        var confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);
        app.Confirmations = confirmations;

        await PressExitAsync(app.MainWindowVm);

        Assert.That(confirmations.TimesAsked, Is.Zero,
            "CONTROL: with no unapplied work there is nothing to ask about");

        db.Dispose();
    }

    /// <summary>
    /// The same question guards Close Database, and it is asked while the connection is still there -
    /// afterwards the only honest offer left would be to discard.
    /// </summary>
    [Test]
    public async Task DisconnectingWithUnappliedChangesCanBeCancelledTest()
    {
        var (app, db) = NewStudio();

        var confirmations = new ScriptedConfirmationService(UnsavedChangesDecision.Cancel);
        app.Confirmations = confirmations;

        await WithADirtyEditorAsync(db, app, "disconnect-cancel");

        var command = (OutWit.Common.MVVM.Commands.RelayCommand)app.MainWindowVm.CloseDatabaseCommand;
        command.Execute(null);

        // CloseDatabaseAsync is 'async void' (S3); give its continuation a chance to run.
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(confirmations.TimesAsked, Is.EqualTo(1));
            Assert.That(db.IsConnected, Is.True,
                "the answer was Cancel: the connection - and with it the chance to apply - stays");
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    #endregion
}
