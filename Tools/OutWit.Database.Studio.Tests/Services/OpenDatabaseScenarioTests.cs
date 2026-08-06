using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.AdoNet;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The scenario the plan names, and the one that could not be run before: a user opens a database
/// from the menu and the schema appears in the explorer.
///
/// It needed Avalonia until now, because the ViewModels constructed windows and reached into
/// <c>MainWindow.StorageProvider</c>. With <see cref="IDialogService"/> the window is the only thing
/// missing: the real ConnectionViewModel runs, the real DatabaseService connects, the real explorer
/// loads the real schema.
/// </summary>
[TestFixture]
public class OpenDatabaseScenarioTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioScenario", Guid.NewGuid().ToString("N"));
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

    #region Tests

    [Test]
    public async Task OpeningADatabaseFromTheMenuLoadsItsSchemaTest()
    {
        var path = Path.Combine(m_root, "scenario.witdb");

        await using (var connection = new WitDbConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name VARCHAR(100))";
            await command.ExecuteNonQueryAsync();

            await connection.CloseAsync();
        }

        using var database = new DatabaseService(NullLogger<DatabaseService>.Instance);

        var app = new ApplicationViewModel(
            database,
            new SettingsService(NullLogger<SettingsService>.Instance,
                Path.Combine(m_root, "settings", "settings.json")),
            new ExportService(),
            NullLogger<ApplicationViewModel>.Instance);

        // The user picks this path in the Open dialog and presses Connect.
        app.Dialogs = ScriptedDialogService.OpeningDatabase(path, PressConnectAsync);

        // File > Open Database...
        app.MainWindowVm.OpenDatabaseCommand.Execute(null);

        await WaitForAsync(() => app.DatabaseExplorerVm.Nodes.Count > 0);

        var tables = app.DatabaseExplorerVm.Nodes
            .SelectMany(node => node.Children)
            .FirstOrDefault(folder => folder.Name.Contains("Table", StringComparison.OrdinalIgnoreCase));

        Assert.Multiple(() =>
        {
            Assert.That(database.IsConnected, Is.True, "the scenario ends connected");
            Assert.That(app.MainWindowVm.IsConnected, Is.True, "and the interface knows it");
            Assert.That(tables, Is.Not.Null, "the explorer shows a folder of tables");
            Assert.That(tables!.Children.Select(c => c.Name), Does.Contain("Customers"),
                "and the table that is actually in the database");
        });

        await database.DisconnectAsync();
    }

    /// <summary>
    /// CONTROL. A user who closes the dialog without connecting must leave Studio exactly as it was -
    /// otherwise the case above would pass for an application that connects to anything it is pointed
    /// at, dialog or no dialog.
    /// </summary>
    [Test]
    public async Task ControlClosingTheDialogWithoutConnectingChangesNothingTest()
    {
        using var database = new DatabaseService(NullLogger<DatabaseService>.Instance);

        var app = new ApplicationViewModel(
            database,
            new SettingsService(NullLogger<SettingsService>.Instance,
                Path.Combine(m_root, "settings", "settings.json")),
            new ExportService(),
            NullLogger<ApplicationViewModel>.Instance);

        var dialogs = new ScriptedDialogService();
        app.Dialogs = dialogs;

        app.MainWindowVm.OpenDatabaseCommand.Execute(null);

        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(dialogs.Shown, Is.Not.Empty, "CONTROL: the dialog was asked for");
            Assert.That(database.IsConnected, Is.False, "and nothing was opened");
            Assert.That(app.DatabaseExplorerVm.Nodes, Is.Empty);
        });
    }

    #endregion

    #region Tools

    private static async Task PressConnectAsync(ConnectionViewModel viewModel)
    {
        var command = (RelayCommandAsync)viewModel.ConnectCommand;

        command.Execute(null);

        await WaitForAsync(() => !command.IsExecuting);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The scenario did not reach its expected state in 30 seconds.");

            await Task.Delay(10);
        }
    }

    #endregion
}
