using NUnit.Framework;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The import (6.4, WS-50): a real CSV into a real table, and the three answers to a key collision.
///
/// <para>
/// The design's rule that shapes all of this is that the import goes in <b>batches, not one
/// transaction</b>: a million rows in one transaction is a million versions in MVCC and a journal
/// that grows until it stops, and a cancel would then throw away work the user watched happen. So a
/// partial result is a normal outcome, and the report has to name it in numbers.
/// </para>
/// </summary>
[TestFixture]
public class ImportWizardTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ImportViewModel m_import = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Target (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");

        m_import = new ImportViewModel(m_studio.App);

        await m_import.InitializeAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        m_import.Dispose();

        await m_studio.DisposeAsync();
    }

    #endregion

    #region The three steps

    [Test]
    public void TheWizardStartsAtTheFileAndWalksForwardAndBackTest()
    {
        Assert.That(m_import.Step, Is.EqualTo(ImportStep.File));

        m_import.NextCommand.Execute(null);
        Assert.That(m_import.Step, Is.EqualTo(ImportStep.Destination));

        m_import.NextCommand.Execute(null);
        Assert.That(m_import.Step, Is.EqualTo(ImportStep.Columns));

        // and it stops at the ends rather than walking off them
        m_import.NextCommand.Execute(null);
        Assert.That(m_import.Step, Is.EqualTo(ImportStep.Columns));

        m_import.BackCommand.Execute(null);
        m_import.BackCommand.Execute(null);
        m_import.BackCommand.Execute(null);
        Assert.That(m_import.Step, Is.EqualTo(ImportStep.File));
    }

    #endregion

    #region What a collision does

    /// <summary>
    /// Skip: the row that is there is left alone, the one from the file is counted as rejected, and
    /// **the rest of the file still goes in**. That last part is the whole difference between Skip and
    /// Abort, and counting alone cannot tell them apart - the values are read back.
    /// </summary>
    [Test]
    public async Task SkipLeavesTheExistingRowAndImportsTheRestAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Target (Id, Name) VALUES (2, 'existing')");

        await ImportAsync("Id,Name\n1,one\n2,two\n3,three\n", ImportConflict.Skip);

        Assert.Multiple(() =>
        {
            Assert.That(m_import.RowsImported, Is.EqualTo(2));
            Assert.That(m_import.RowsFailed, Is.EqualTo(1));
            Assert.That(Read(), Is.EqualTo(new[] { "1|one", "2|existing", "3|three" }),
                "the collision was left alone and the row AFTER it still went in");
        });
    }

    /// <summary>
    /// Update: a MERGE, so the collision is overwritten and the rows that do not collide are still
    /// inserted. An update path that only updated would silently drop every new row in the file, which
    /// is why both are asserted.
    /// </summary>
    [Test]
    public async Task UpdateOverwritesTheCollisionAndStillInsertsTheRestAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Target (Id, Name) VALUES (2, 'existing')");

        m_import.KeyColumn = "Id";

        await ImportAsync("Id,Name\n1,one\n2,two\n3,three\n", ImportConflict.Update);

        Assert.Multiple(() =>
        {
            Assert.That(Read(), Is.EqualTo(new[] { "1|one", "2|two", "3|three" }));
            Assert.That(m_import.RowsFailed, Is.Zero, "nothing was rejected");
        });
    }

    /// <summary>
    /// Abort: the import stops, and **what was already written stays written** - because the file goes
    /// in batches rather than in one transaction. The report has to say so in numbers, which is what
    /// makes a partial result honest rather than a surprise.
    /// </summary>
    [Test]
    public async Task AbortStopsAndWhatWasWrittenStaysWrittenAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Target (Id, Name) VALUES (2, 'existing')");

        await ImportAsync("Id,Name\n1,one\n2,two\n3,three\n", ImportConflict.Abort);

        Assert.Multiple(() =>
        {
            Assert.That(Read(), Is.EqualTo(new[] { "1|one", "2|existing" }),
                "the row before the collision is in the database and the row after it is not");
            Assert.That(m_import.RowsImported, Is.EqualTo(1), "and the report says how many");
        });
    }

    /// <summary>
    /// All-or-nothing is the opt-in, and it is what a person chooses when a partial import is worse
    /// than none. It is NOT the default, because the default must not be the mode that fails on the
    /// largest file.
    /// </summary>
    [Test]
    public async Task AllOrNothingLeavesTheTableUntouchedWhenARowIsRefusedAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Target (Id, Name) VALUES (2, 'existing')");

        m_import.AllOrNothing = true;

        await ImportAsync("Id,Name\n1,one\n2,two\n3,three\n", ImportConflict.Abort);

        Assert.That(Read(), Is.EqualTo(new[] { "2|existing" }),
            "the row that went in before the collision was rolled back with it");
    }

    /// <summary>CONTROL: with no collision at all, every row goes in whatever the answer is.</summary>
    [TestCase(ImportConflict.Skip)]
    [TestCase(ImportConflict.Update)]
    [TestCase(ImportConflict.Abort)]
    public async Task WithNoCollisionEveryRowGoesInAsync(ImportConflict conflict)
    {
        m_import.KeyColumn = "Id";

        await ImportAsync("Id,Name\n1,one\n2,two\n3,three\n", conflict);

        Assert.That(Read(), Is.EqualTo(new[] { "1|one", "2|two", "3|three" }));
    }

    #endregion

    #region The report

    /// <summary>
    /// Every rejected row is kept, not only the ten the window shows: an import that reports "16
    /// skipped" and can name three of them is an import nobody can fix. The record carries the line
    /// number, the engine's own message and the line itself, so the file can be repaired and fed back.
    /// </summary>
    [Test]
    public async Task EveryRejectedRowIsKeptWithItsLineAndItsReasonAsync()
    {
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Target (Id, Name) VALUES (2, 'existing')");
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Target (Id, Name) VALUES (3, 'existing')");

        await ImportAsync("Id,Name\n1,one\n2,two\n3,three\n", ImportConflict.Skip);

        Assert.Multiple(() =>
        {
            Assert.That(m_import.Rejected, Has.Count.EqualTo(2));

            // The line IN THE FILE - the header is line 1, so the first colliding row is line 3. Not
            // the data row number, which would be 2 and would point at the wrong line in an editor.
            Assert.That(m_import.Rejected[0].Line, Is.EqualTo(3));
            Assert.That(m_import.Rejected[0].Reason, Does.Contain("UNIQUE"),
                "the engine's own message, untranslated (WS-64)");
            Assert.That(m_import.Rejected[0].Text, Is.EqualTo("2,two"), "and the line itself");
        });
    }

    #endregion

    #region Empty fields

    /// <summary>
    /// A CSV cannot tell an empty string from a missing value, so the user says which, once. NULL is
    /// the default because a missing value is what an empty field usually means.
    /// </summary>
    [TestCase(true, "")]
    [TestCase(false, "|")]
    public async Task AnEmptyFieldIsNullOrAnEmptyStringAsChosenAsync(bool emptyIsNull, string _)
    {
        m_import.EmptyIsNull = emptyIsNull;

        await ImportAsync("Id,Name\n1,\n", ImportConflict.Skip);

        var rows = await m_studio.Database.ExecuteQueryAsync(
            "SELECT COUNT(*) FROM Target WHERE Name IS NULL");

        var nulls = Convert.ToInt32(rows.Data!.Rows[0][0]);

        Assert.That(nulls, Is.EqualTo(emptyIsNull ? 1 : 0));
    }

    #endregion

    #region Tools

    private async Task ImportAsync(string csv, ImportConflict conflict)
    {
        var path = Path.Combine(m_studio.Root, "import.csv");

        await File.WriteAllTextAsync(path, csv.Replace("\n", Environment.NewLine));

        m_import.InputPath = path;
        m_import.SelectedFormat = ImportFormat.Csv;
        m_import.HasHeaders = true;
        m_import.Delimiter = ",";
        m_import.OnConflict = conflict;
        m_import.SelectedTable = "Target";

        await StudioFixture.PressAsync(m_import.PreviewCommand);
        await StudioFixture.PressAsync(m_import.ImportCommand);
    }

    private string[] Read()
    {
        var result = m_studio.Database.ExecuteQueryAsync("SELECT Id, Name FROM Target ORDER BY Id")
            .GetAwaiter().GetResult();

        return result.Data == null
            ? []
            : result.Data.Rows.Cast<System.Data.DataRow>()
                .Select(row => string.Join("|", row.ItemArray.Select(value => value?.ToString())))
                .ToArray();
    }

    #endregion
}
