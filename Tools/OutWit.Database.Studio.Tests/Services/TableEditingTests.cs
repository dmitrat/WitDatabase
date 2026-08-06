using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Common.MVVM.Commands;
using OutWit.Database.AdoNet;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// B2 and B3 - what the table editor writes, and what it refuses to write.
///
/// Both are measured by reading the rows back out of a real database, never by asking the editor what
/// it thinks it did. The editor's own status line said "Changes committed successfully" for a set that
/// was applied halfway.
/// </summary>
[TestFixture]
public class TableEditingTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioEditing", Guid.NewGuid().ToString("N"));
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
            new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(Path.GetTempPath(), "WitStudioTests", Guid.NewGuid().ToString("N"), "settings.json")),
            new ExportService(),
            NullLogger<ApplicationViewModel>.Instance);

        return (app, db);
    }

    private async Task<DatabaseService> ConnectAsync(DatabaseService db, string name)
    {
        var path = Path.Combine(m_root, $"{name}.witdb");

        await using (var connection = new WitDbConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await connection.CloseAsync();
        }

        var connected = await db.ConnectAsync(new ConnectionInfo { FilePath = path });
        Assert.That(connected, Is.True, "setup: the database did not open");

        return db;
    }

    private static async Task PressCommitAsync(TableEditTabViewModel editor)
    {
        var command = (RelayCommandAsync)editor.CommitCommand;

        command.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (command.IsExecuting)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The commit command did not complete within 60 seconds.");

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Reads the rows back by scanning them - never through COUNT(*), which on this engine is separate
    /// state that can disagree with the rows.
    /// </summary>
    private static async Task<List<(object Key, object Value)>> ReadAsync(
        DatabaseService db, string sql, string keyColumn, string valueColumn)
    {
        var result = await db.ExecuteQueryAsync(sql);

        Assert.That(result.ErrorMessage, Is.Null.Or.Empty, "reading back failed");

        return result.Data!.Rows
            .Cast<DataRow>()
            .Select(row => (row[keyColumn], row[valueColumn]))
            .ToList();
    }

    private static void EditCell(TableEditTabViewModel editor, int rowIndex, string column, object value)
    {
        var rowView = new DataView(editor.EditableData!)[rowIndex];
        rowView.Row[column] = value;
        editor.CellEditedCommand.Execute(rowView);
    }

    #endregion

    #region B2 - one transaction

    /// <summary>
    /// A buffer of three edits where the middle one is refused by the engine. Nothing may reach the
    /// database, and the buffer has to survive so that nothing has to be retyped.
    ///
    /// Before the fix this applied the delete and the first update, then reported "Update failed:
    /// Value too long..." - leaving the table in a state the user never asked for and could not see.
    /// </summary>
    [Test]
    public async Task AnEditSetWithOneRefusedStatementAppliesNothingTest()
    {
        var (app, db) = NewStudio();
        await ConnectAsync(db, "atomic");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(8) NOT NULL)");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'one')");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (2, 'two')");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (3, 'three')");

        var editor = await app.WorkspaceTabsVm.OpenTableEditTabAsync("Probe");

        Assert.That(editor.EditableData!.Rows.Count, Is.EqualTo(3), "setup: the editor loaded the wrong rows");

        // Row 1: a change the engine would accept. Row 3: deleted. Row 2: eighteen characters into a
        // VARCHAR(8), which the engine refuses - measured, not assumed.
        EditCell(editor, 0, "Name", "ONE");
        editor.SelectedRowView = new DataView(editor.EditableData)[2];
        editor.DeleteRowCommand.Execute(null);
        EditCell(editor, 1, "Name", "way-too-long-value");

        Assert.That(editor.ChangeCount, Is.EqualTo(3), "setup: three edits are what makes this a set");

        await PressCommitAsync(editor);

        var rows = await ReadAsync(db, "SELECT Id, Name FROM Probe", "Id", "Name");

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(3),
                "the delete must have been rolled back with the rest of the set");

            Assert.That(rows.Single(r => Convert.ToInt32(r.Key) == 1).Value, Is.EqualTo("one"),
                "the accepted change must not survive the refused one - the set is all or nothing");
            Assert.That(rows.Single(r => Convert.ToInt32(r.Key) == 2).Value, Is.EqualTo("two"));
            Assert.That(rows.Any(r => Convert.ToInt32(r.Key) == 3), Is.True);

            Assert.That(editor.HasChanges, Is.True,
                "a refused set keeps its buffer - the user should not have to retype anything");
            Assert.That(editor.ErrorMessage, Is.Not.Null.And.Not.Empty,
                "and has to be told what stopped it");
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    /// <summary>
    /// CONTROL for the test above. "Nothing reached the database" would read identically if the editor
    /// could not write at all; this is the same three-edit shape with nothing refused, and every one of
    /// them has to land.
    /// </summary>
    [Test]
    public async Task ControlAnEditSetWithNothingRefusedIsAppliedWholeTest()
    {
        var (app, db) = NewStudio();
        await ConnectAsync(db, "atomic-control");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(8) NOT NULL)");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'one')");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (2, 'two')");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (3, 'three')");

        var editor = await app.WorkspaceTabsVm.OpenTableEditTabAsync("Probe");

        EditCell(editor, 0, "Name", "ONE");
        editor.SelectedRowView = new DataView(editor.EditableData!)[2];
        editor.DeleteRowCommand.Execute(null);
        EditCell(editor, 1, "Name", "TWO");

        await PressCommitAsync(editor);

        var rows = await ReadAsync(db, "SELECT Id, Name FROM Probe", "Id", "Name");

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2), "CONTROL: the delete has to reach the database");
            Assert.That(rows.Single(r => Convert.ToInt32(r.Key) == 1).Value, Is.EqualTo("ONE"));
            Assert.That(rows.Single(r => Convert.ToInt32(r.Key) == 2).Value, Is.EqualTo("TWO"));

            Assert.That(editor.HasChanges, Is.False, "a committed set empties the buffer");
            Assert.That(editor.ErrorMessage, Is.Null.Or.Empty);
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    /// <summary>
    /// The same claim one layer down, with a failure that cannot be argued with: a statement the parser
    /// refuses. Typed through the ADO.NET base classes on the way in, so this also exercises the
    /// drop-in surface rather than the provider's own transaction type.
    /// </summary>
    [Test]
    public async Task ExecuteBatchAppliesNothingWhenAStatementFailsTest()
    {
        var (_, db) = NewStudio();
        await ConnectAsync(db, "batch");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(20))");

        var result = await db.ExecuteBatchAsync(
        [
            "INSERT INTO Probe (Id, Name) VALUES (1, 'first')",
            "INSERT INTO Probe (Id, Name) VALUES (2, 'second')",
            "UPDATE Probe SET WHERE Id = 1"
        ]);

        var rows = await ReadAsync(db, "SELECT Id, Name FROM Probe", "Id", "Name");

        Assert.Multiple(() =>
        {
            Assert.That(result.Committed, Is.False);
            Assert.That(result.FailedIndex, Is.EqualTo(2), "the third statement is the one that failed");
            Assert.That(result.ErrorMessage, Is.Not.Null.And.Not.Empty);

            Assert.That(rows, Is.Empty,
                "the two accepted inserts must have been rolled back with the refused statement");
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    /// <summary>
    /// CONTROL: the same batch without the bad statement writes both rows. Without it, "the table is
    /// empty" would pass for a transaction that never commits anything.
    /// </summary>
    [Test]
    public async Task ControlExecuteBatchCommitsWhenEveryStatementSucceedsTest()
    {
        var (_, db) = NewStudio();
        await ConnectAsync(db, "batch-control");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(20))");

        var result = await db.ExecuteBatchAsync(
        [
            "INSERT INTO Probe (Id, Name) VALUES (1, 'first')",
            "INSERT INTO Probe (Id, Name) VALUES (2, 'second')"
        ]);

        var rows = await ReadAsync(db, "SELECT Id, Name FROM Probe", "Id", "Name");

        Assert.Multiple(() =>
        {
            Assert.That(result.Committed, Is.True, result.ErrorMessage);
            Assert.That(rows, Has.Count.EqualTo(2), "CONTROL: a clean batch has to reach the database");
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    /// <summary>
    /// Found while building the two tests above, and not in the plan: DELETING A ROW NEVER WORKED.
    ///
    /// The editor's DataTable is filled row by row and never told to AcceptChanges, so every loaded row
    /// sits in state Added rather than Unchanged. DataRow.Delete() on an Added row DETACHES it instead
    /// of marking it deleted - so the row leaves the table, FindOriginalRowIndex reads a detached row
    /// and throws RowNotInTableException, and the whole commit falls into its catch. Nothing is
    /// deleted, and any other edits in the same buffer die with it.
    /// </summary>
    [Test]
    public async Task DeletingARowReachesTheDatabaseTest()
    {
        var (app, db) = NewStudio();
        await ConnectAsync(db, "delete-row");

        await db.ExecuteNonQueryAsync("CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(20))");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (1, 'one')");
        await db.ExecuteNonQueryAsync("INSERT INTO Probe (Id, Name) VALUES (2, 'two')");

        var editor = await app.WorkspaceTabsVm.OpenTableEditTabAsync("Probe");

        editor.SelectedRowView = new DataView(editor.EditableData!)[1];
        editor.DeleteRowCommand.Execute(null);

        Assert.That(editor.HasChanges, Is.True, "setup: the delete has to register as a change");

        await PressCommitAsync(editor);

        var rows = await ReadAsync(db, "SELECT Id, Name FROM Probe", "Id", "Name");

        Assert.Multiple(() =>
        {
            Assert.That(editor.ErrorMessage, Is.Null.Or.Empty, "the delete must not fail");
            Assert.That(rows, Has.Count.EqualTo(1), "the row has to be gone from the database");
            Assert.That(Convert.ToInt32(rows[0].Key), Is.EqualTo(1), "and it has to be the right one");
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    #endregion

    #region B3 - no primary key, no editing

    /// <summary>
    /// Without a primary key there is no way to name one row. The old BuildWhereClause fell back to
    /// every column of the row, which is not a unique condition: two identical rows both match, and
    /// editing one changed both. The engine reports the number of rows affected and the editor did not
    /// look at it, so nothing said a word.
    ///
    /// Editing such a table is now refused up front (WS-35) - the tab opens for viewing and says why.
    /// </summary>
    [Test]
    public async Task ATableWithoutAPrimaryKeyOpensForViewingOnlyTest()
    {
        var (app, db) = NewStudio();
        await ConnectAsync(db, "no-pk");

        await db.ExecuteNonQueryAsync("CREATE TABLE Logs (Message VARCHAR(20), Level VARCHAR(10))");
        await db.ExecuteNonQueryAsync("INSERT INTO Logs (Message, Level) VALUES ('same', 'INFO')");
        await db.ExecuteNonQueryAsync("INSERT INTO Logs (Message, Level) VALUES ('same', 'INFO')");

        var editor = await app.WorkspaceTabsVm.OpenTableEditTabAsync("Logs");

        Assert.That(editor.EditableData!.Rows.Count, Is.EqualTo(2), "setup: two indistinguishable rows");

        Assert.Multiple(() =>
        {
            Assert.That(editor.IsReadOnly, Is.True,
                "a table with no primary key cannot be edited safely, so it is not offered");
            Assert.That(editor.ReadOnlyReason, Is.Not.Null.And.Not.Empty,
                "and the reason is shown rather than leaving the buttons mysteriously grey");
            Assert.That(editor.CanAddRow, Is.False);
        });

        // Even driven directly, the edit must not turn into an UPDATE over every column.
        EditCell(editor, 0, "Message", "changed");
        await PressCommitAsync(editor);

        var rows = await ReadAsync(db, "SELECT Message, Level FROM Logs", "Message", "Level");

        Assert.That(rows.Select(r => r.Key), Is.All.EqualTo("same"),
            "nothing may be written for a table whose rows cannot be addressed - and the old fallback "
            + "would have changed BOTH of these");

        await db.DisconnectAsync();
        db.Dispose();
    }

    /// <summary>
    /// CONTROL. The same editor over a table WITH a primary key edits one row and only one - otherwise
    /// "nothing was written" above would be the behaviour of an editor that never writes.
    /// </summary>
    [Test]
    public async Task ControlWithAPrimaryKeyOnlyTheEditedRowChangesTest()
    {
        var (app, db) = NewStudio();
        await ConnectAsync(db, "with-pk");

        await db.ExecuteNonQueryAsync("CREATE TABLE Logs (Id INTEGER PRIMARY KEY, Message VARCHAR(20))");
        await db.ExecuteNonQueryAsync("INSERT INTO Logs (Id, Message) VALUES (1, 'same')");
        await db.ExecuteNonQueryAsync("INSERT INTO Logs (Id, Message) VALUES (2, 'same')");

        var editor = await app.WorkspaceTabsVm.OpenTableEditTabAsync("Logs");

        Assert.That(editor.IsReadOnly, Is.False, "CONTROL: this table can be addressed row by row");

        EditCell(editor, 0, "Message", "changed");
        await PressCommitAsync(editor);

        var rows = await ReadAsync(db, "SELECT Id, Message FROM Logs", "Id", "Message");

        Assert.Multiple(() =>
        {
            Assert.That(rows.Single(r => Convert.ToInt32(r.Key) == 1).Value, Is.EqualTo("changed"));
            Assert.That(rows.Single(r => Convert.ToInt32(r.Key) == 2).Value, Is.EqualTo("same"),
                "CONTROL: the other row must be untouched - this is exactly what the all-columns "
                + "WHERE clause failed to guarantee");
        });

        await db.DisconnectAsync();
        db.Dispose();
    }

    #endregion
}
