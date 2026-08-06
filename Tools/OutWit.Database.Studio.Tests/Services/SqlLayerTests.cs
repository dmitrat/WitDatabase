using System.Data;
using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Stage 3, the SQL layer: values are bound rather than written into the statement (B4), a script is
/// executed one statement at a time with errors landing on the right line (WS-22), and a table is
/// read a page at a time (S7, WS-31).
///
/// Everything here goes to a real engine and reads the rows back. A test that asserts on the SQL
/// Studio generated would have passed for every one of the defects below.
/// </summary>
[TestFixture]
public class SqlLayerTests
{
    #region Tools

    private static void EditCell(TableEditTabViewModel editor, int rowIndex, string column, object value)
    {
        var rowView = new DataView(editor.EditableData!)[rowIndex];
        rowView.Row[column] = value;
        editor.CellEditedCommand.Execute(rowView);
    }

    private static async Task<IReadOnlyList<object?>> ColumnAsync(
        IDatabaseSession session, string sql, string column)
    {
        var result = await session.ExecuteQueryAsync(sql);

        Assert.That(result.ErrorMessage, Is.Null.Or.Empty, $"reading back failed: {result.ErrorMessage}");

        return result.Data!.Rows.Cast<DataRow>().Select(row => row[column]).ToList();
    }

    #endregion

    #region B4 - values are bound, not written into the statement

    /// <summary>
    /// A value full of quotes and semicolons is stored whole.
    ///
    /// MEASURED, and the result corrects the plan: the old literal path passes this too. It doubles
    /// the quotes, and the engine reads the result as one string - the table survives and the value
    /// comes back intact. So this is a regression guard, not a demonstration of a defect; what the
    /// binding changes is that there is no longer an escaping step that has to be right.
    ///
    /// The cases the literal path really does fail are the two below: a value it writes with less
    /// precision than it was given, and a value whose type it has no case for.
    /// </summary>
    [Test]
    public async Task AValueThatWouldOtherwiseBecomeSyntaxIsStoredAsAValueTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        const string AWKWARD = "O'Brien'; DROP TABLE Customers; --";

        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Customers");

        EditCell(editor, 0, "Name", AWKWARD);

        await StudioFixture.PressAsync(editor.CommitCommand);

        var names = await ColumnAsync(studio.Database, "SELECT Name FROM Customers ORDER BY Id", "Name");

        Assert.Multiple(() =>
        {
            Assert.That(editor.ErrorMessage, Is.Null.Or.Empty, $"the commit failed: {editor.ErrorMessage}");
            Assert.That(names[0], Is.EqualTo(AWKWARD), "the value arrives whole, quotes and all");
            Assert.That(names, Has.Count.EqualTo(StudioFixture.CUSTOMER_COUNT),
                "CONTROL: the table is still there with all its rows - the value was never syntax");
        });
    }

    /// <summary>
    /// A DATETIME goes to the database as a DateTime. The literal path formats it as
    /// 'yyyy-MM-dd HH:mm:ss', so everything under a second is silently dropped - the value read back
    /// is not the value the user entered.
    ///
    /// Both directions in one case: the parameterised write keeps the milliseconds, and the literal
    /// the formatter would have produced is asserted NOT to contain them. That second assertion is
    /// what makes this a measurement of the fix rather than a description of the present.
    /// </summary>
    [Test]
    public async Task APrecisionThatTheLiteralPathDropsSurvivesAsAParameterTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        var moment = new DateTime(2026, 8, 6, 12, 34, 56, 789, DateTimeKind.Utc);

        await studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Events (Id INTEGER PRIMARY KEY, At DATETIME)");

        var result = await studio.Database.ExecuteBatchAsync(
        [
            new SqlStatement("INSERT INTO Events (Id, At) VALUES (@id, @at)",
                [new OutWit.Database.Studio.Models.SqlParameter("@id", 1), new OutWit.Database.Studio.Models.SqlParameter("@at", moment)])
        ]);

        var stored = (await ColumnAsync(studio.Database, "SELECT At FROM Events", "At")).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Committed, Is.True, $"the write failed: {result.ErrorMessage}");
            Assert.That(stored, Is.EqualTo(moment), "the value read back is the value written");
            Assert.That(((DateTime)stored!).Millisecond, Is.EqualTo(789));

            Assert.That(SqlValueFormatter.FormatForSql(moment), Does.Not.Contain("789"),
                "CONTROL: the literal the old path would have built has no milliseconds in it at all, "
                + "so this case could not have passed before");
        });
    }

    /// <summary>
    /// A BLOB survives a round trip through a binding.
    ///
    /// Also a correction to the plan, which says a BLOB cannot be written at all: measured against
    /// the engine, the hex literal the formatter produces (X'000102FAFBFF') is accepted and comes
    /// back byte for byte. So this is a guard rather than a fix - kept because the binding is the
    /// path the editor now takes, and nothing else in the suite reads a BLOB back.
    /// </summary>
    [Test]
    public async Task ABlobIsWrittenAndComesBackByteForByteTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        var payload = new byte[] { 0, 1, 2, 250, 251, 255 };

        await studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Files (Id INTEGER PRIMARY KEY, Content BLOB)");

        var result = await studio.Database.ExecuteBatchAsync(
        [
            new SqlStatement("INSERT INTO Files (Id, Content) VALUES (@id, @content)",
                [new OutWit.Database.Studio.Models.SqlParameter("@id", 1), new OutWit.Database.Studio.Models.SqlParameter("@content", payload)])
        ]);

        var stored = (await ColumnAsync(studio.Database, "SELECT Content FROM Files", "Content")).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Committed, Is.True, $"the write failed: {result.ErrorMessage}");
            Assert.That(stored, Is.EqualTo(payload).AsCollection);
        });
    }

    /// <summary>
    /// The editor's own statements carry parameters now. Asserted on the statement rather than only
    /// on the row, because "the value is bound" is the property that stops the class of defect, and a
    /// row can be right for other reasons.
    /// </summary>
    [Test]
    public async Task TheEditorsStatementsBindTheirValuesTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Customers");

        EditCell(editor, 0, "Name", "changed");

        var build = typeof(TableEditTabViewModel).GetMethod("BuildChangeScript",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var arguments = new object?[] { null };
        var statements = (List<SqlStatement>)build.Invoke(editor, arguments)!;

        Assert.Multiple(() =>
        {
            Assert.That(statements, Has.Count.EqualTo(1));
            Assert.That(statements[0].Text, Does.Contain("@s0").And.Contain("@w0"),
                "the value and the key are placeholders");
            Assert.That(statements[0].Text, Does.Not.Contain("'changed'"),
                "and the value itself is nowhere in the text");
            Assert.That(statements[0].Parameters.Select(p => p.Value), Does.Contain("changed"));

            Assert.That(statements[0].ToDisplaySql(), Does.Contain("'changed'"),
                "WS-32: shown to a person, the same statement is written out with its values");
        });
    }

    /// <summary>
    /// The one the formatter cannot express. Its last case is <c>value.ToString()</c> with no quoting
    /// at all, so a value of a type it does not know becomes an IDENTIFIER in the statement - and the
    /// engine goes looking for a column by that name.
    ///
    /// Both directions, in one case: the literal is shown to be bare text and to be refused by the
    /// engine, and the same value through a binding is stored and read back.
    /// </summary>
    [Test]
    public async Task AValueTheFormatterHasNoCaseForIsWrittenAsBareTextAndRefusedTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        // A char has no case in SqlValueFormatter, so it falls through to ToString().
        var value = 'x';

        await studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Notes (Id INTEGER PRIMARY KEY, Mark VARCHAR(4))");

        var literal = SqlValueFormatter.FormatForSql(value);

        var asLiteral = await studio.Database.ExecuteQueryAsync(
            $"INSERT INTO Notes (Id, Mark) VALUES (1, {literal})");

        var asParameter = await studio.Database.ExecuteBatchAsync(
        [
            new SqlStatement("INSERT INTO Notes (Id, Mark) VALUES (@id, @mark)",
                [new OutWit.Database.Studio.Models.SqlParameter("@id", 2),
                 new OutWit.Database.Studio.Models.SqlParameter("@mark", value)])
        ]);

        var stored = await ColumnAsync(studio.Database, "SELECT Mark FROM Notes", "Mark");

        Assert.Multiple(() =>
        {
            Assert.That(literal, Is.EqualTo("x"), "the literal is bare text - no quotes anywhere");
            Assert.That(asLiteral.ErrorMessage, Is.Not.Null.And.Not.Empty,
                "and the engine reads it as a column name, not as a value");

            Assert.That(asParameter.Committed, Is.True, $"the binding works: {asParameter.ErrorMessage}");
            Assert.That(stored.Single()!.ToString(), Is.EqualTo("x"));
        });
    }

    #endregion

    #region WS-22 - a script is executed one statement at a time

    /// <summary>
    /// THE READINESS CASE of the stage, in the plan's words: an error in the sixth statement is
    /// reported on the sixth line - through the real tab, over a real database.
    ///
    /// This one is a SYNTAX error, and so it is known before anything is sent: the script is parsed
    /// whole, and a script that does not parse does not run at all. That is the safer half of the
    /// decision - the user is not left with the first five statements applied and a message about the
    /// sixth. The other half, a statement the engine refuses at run time, is the case below.
    /// </summary>
    [Test]
    public async Task AnErrorInTheSixthStatementIsReportedOnTheSixthLineTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        var tab = studio.FirstQueryTab;

        tab.SqlText = string.Join("\n",
        [
            "CREATE TABLE Steps (Id INTEGER PRIMARY KEY, Name VARCHAR(20));",  // 1
            "INSERT INTO Steps (Id, Name) VALUES (1, 'one');",                 // 2
            "INSERT INTO Steps (Id, Name) VALUES (2, 'two');",                 // 3
            "INSERT INTO Steps (Id, Name) VALUES (3, 'three');",               // 4
            "INSERT INTO Steps (Id, Name) VALUES (4, 'four');",                // 5
            "INSERT INTO Steps (Id Name) VALUES (5, 'five');",                 // 6 - a comma is missing
            "INSERT INTO Steps (Id, Name) VALUES (6, 'six');"                  // 7
        ]);

        await StudioFixture.PressAsync(tab.ExecuteQueryCommand);

        var tables = await studio.Database.GetTablesAsync();

        TestContext.Out.WriteLine($"reported: {tab.ErrorMessage}");

        Assert.Multiple(() =>
        {
            Assert.That(tab.ErrorLine, Is.EqualTo(6),
                "the mistake is on line 6 of the tab, and that is where it is reported");
            Assert.That(tab.ErrorMessage, Does.StartWith("Line 6"));
            Assert.That(tab.ErrorMessage, Does.Not.Contain("expecting {"),
                "without the engine's list of every token it would have accepted");
            Assert.That(tab.ErrorDetail, Is.Not.Null.And.Not.Empty,
                "which is kept, in the details");

            Assert.That(tables.Select(t => t.Name), Does.Not.Contain("Steps"),
                "and nothing ran: a script that does not parse is refused whole");
            Assert.That(tab.Statements, Is.Empty);
        });
    }

    /// <summary>
    /// The other half: the script parses, so it runs - and the sixth statement is refused by the
    /// engine when it gets there. The five before it have already been applied, each in its own
    /// transaction, and the seventh is not attempted. Which is which has to be visible, and that is
    /// what the per-statement outcomes are for.
    /// </summary>
    [Test]
    public async Task AStatementRefusedAtRunTimeStopsTheScriptAndSaysWhichOneTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        var tab = studio.FirstQueryTab;

        tab.SqlText = string.Join("\n",
        [
            "CREATE TABLE Steps (Id INTEGER PRIMARY KEY, Name VARCHAR(20));",  // 1
            "INSERT INTO Steps (Id, Name) VALUES (1, 'one');",                 // 2
            "INSERT INTO Steps (Id, Name) VALUES (2, 'two');",                 // 3
            "INSERT INTO Steps (Id, Name) VALUES (3, 'three');",               // 4
            "INSERT INTO Steps (Id, Name) VALUES (4, 'four');",                // 5
            "INSERT INTO Steps (Id, Name) VALUES (1, 'again');",               // 6 - the key is taken
            "INSERT INTO Steps (Id, Name) VALUES (6, 'six');"                  // 7
        ]);

        await StudioFixture.PressAsync(tab.ExecuteQueryCommand);

        var stored = await ColumnAsync(studio.Database, "SELECT Id FROM Steps", "Id");

        TestContext.Out.WriteLine($"reported: {tab.ErrorMessage}");
        TestContext.Out.WriteLine("statements: " + string.Join("; ", tab.Statements.Select(s => s.ToString())));

        Assert.Multiple(() =>
        {
            Assert.That(tab.ErrorMessage, Does.Contain("Statement 6 of 7"),
                "the user is told which statement stopped the run");

            Assert.That(stored, Has.Count.EqualTo(4),
                "the four rows written before it are in the database");
            Assert.That(tab.Statements, Has.Count.EqualTo(6),
                "six outcomes: five that worked and the one that did not");
            Assert.That(tab.Statements[^1].IsSuccess, Is.False);
            Assert.That(tab.Statements.Take(5).All(outcome => outcome.IsSuccess), Is.True);
        });
    }

    /// <summary>
    /// CONTROL: the same script with the mistake taken out runs to the end, reports every statement,
    /// and says that the schema changed. Without it, "the error is on line 6" would pass for a tab
    /// that reports an error on line 6 of everything.
    /// </summary>
    [Test]
    public async Task ControlAScriptWithNoMistakeRunsToTheEndTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        var tab = studio.FirstQueryTab;

        tab.SqlText = string.Join("\n",
        [
            "CREATE TABLE Steps (Id INTEGER PRIMARY KEY, Name VARCHAR(20));",
            "INSERT INTO Steps (Id, Name) VALUES (1, 'one');",
            "INSERT INTO Steps (Id, Name) VALUES (2, 'two');",
            "SELECT * FROM Steps"
        ]);

        await StudioFixture.PressAsync(tab.ExecuteQueryCommand);

        Assert.Multiple(() =>
        {
            Assert.That(tab.ErrorMessage, Is.Null.Or.Empty);
            Assert.That(tab.ErrorLine, Is.EqualTo(0));
            Assert.That(tab.Statements, Has.Count.EqualTo(4), "one outcome per statement");

            Assert.That(tab.Statements[1].RowsAffected, Is.EqualTo(1), "the INSERT says it wrote a row");
            Assert.That(tab.Statements[1].ReturnedRows, Is.False, "and that it returned none");
            Assert.That(tab.Statements[^1].ReturnedRows, Is.True, "while the SELECT returned rows");

            Assert.That(tab.TotalRowCount, Is.EqualTo(2), "the grid shows the last result");
            Assert.That(tab.DdlWasExecuted, Is.True, "and the tree is told to reload");
        });
    }

    /// <summary>
    /// A selection is executed on its own, and the engine then counts lines from the start of the
    /// SELECTION. The tab has to put the answer back where the user is looking.
    /// </summary>
    [Test]
    public async Task AnErrorInASelectionIsReportedInTheTabsCoordinatesTest()
    {
        await using var studio = await StudioFixture.CreateAsync(withSchema: false);

        var tab = studio.FirstQueryTab;

        var padding = string.Join("\n", Enumerable.Repeat("-- filler", 9));
        var fragment = "SELECT FROM WHERE";

        tab.SqlText = padding + "\n" + fragment;
        tab.SelectedText = fragment;

        await StudioFixture.PressAsync(tab.ExecuteSelectionCommand);

        Assert.Multiple(() =>
        {
            Assert.That(tab.ErrorLine, Is.EqualTo(10),
                "the selection starts on line 10 of the tab, and so does the mistake");
            Assert.That(tab.ErrorMessage, Does.Contain("Line 10"));
        });
    }

    #endregion

    #region S7 / WS-31 - pages

    /// <summary>
    /// Pages have to tile the table: every row once, in order, with nothing skipped between one page
    /// and the next. This is the property that OFFSET loses when rows are written underneath it, and
    /// the reason the pages are fetched by key.
    /// </summary>
    [Test]
    public async Task PagesTileTheTableWithoutGapsOrRepeatsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Orders");
        editor.PageSize = 2;

        await editor.LoadDataAsync();

        var seen = new List<long>();
        var pages = 0;

        while (true)
        {
            seen.AddRange(editor.EditableData!.Rows.Cast<DataRow>().Select(row => Convert.ToInt64(row["Id"])));
            pages++;

            if (!editor.HasNextPage)
                break;

            await StudioFixture.PressAsync(editor.NextPageCommand);
        }

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(new long[] { 1, 2, 3 }).AsCollection,
                "every row, once, in key order");
            Assert.That(pages, Is.EqualTo(2), "three rows at two to a page");
            Assert.That(editor.HasNextPage, Is.False, "and the last page says it is the last");
            Assert.That(editor.HasPreviousPage, Is.True);
        });
    }

    [Test]
    public async Task GoingBackShowsThePageBeforeTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Orders");
        editor.PageSize = 2;

        await editor.LoadDataAsync();
        var firstPage = editor.EditableData!.Rows.Cast<DataRow>().Select(row => row["Id"].ToString()).ToList();

        await StudioFixture.PressAsync(editor.NextPageCommand);
        await StudioFixture.PressAsync(editor.PreviousPageCommand);

        var backAgain = editor.EditableData!.Rows.Cast<DataRow>().Select(row => row["Id"].ToString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(editor.PageIndex, Is.EqualTo(0));
            Assert.That(backAgain, Is.EqualTo(firstPage).AsCollection, "the same rows as the first time");
            Assert.That(editor.HasPreviousPage, Is.False);
        });
    }

    /// <summary>
    /// The first page is the one that has to be cheap, and it is cheap because of its SHAPE: no
    /// OFFSET to count past and no key to compare. Asserted on the statement, because a timing
    /// assertion in a test suite measures the machine it runs on.
    ///
    /// What it costs was measured separately, against 100,000 and 400,000 rows, and written into the
    /// phase document: the first page is answered in constant time, and every ordered page after it
    /// is not - the engine sorts the whole table under the LIMIT. That is the engine's, not Studio's.
    /// </summary>
    [Test]
    public async Task TheFirstPageAsksForNothingItDoesNotNeedTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Orders");
        editor.PageSize = 2;
        await editor.LoadDataAsync();

        var build = typeof(TableEditTabViewModel).GetMethod("BuildSelectStatement",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var firstPage = (SqlStatement)build.Invoke(editor, [3])!;

        await StudioFixture.PressAsync(editor.NextPageCommand);

        var secondPage = (SqlStatement)build.Invoke(editor, [3])!;

        Assert.Multiple(() =>
        {
            Assert.That(firstPage.Text, Does.Not.Contain("OFFSET"), "nothing to count past");
            Assert.That(firstPage.Text, Does.Not.Contain("WHERE"), "and nothing to compare");
            Assert.That(firstPage.Text, Does.Contain("LIMIT 3"), "one row more than the page shows");

            Assert.That(secondPage.Text, Does.Contain("WHERE"), "the next page starts from a key");
            Assert.That(secondPage.Text, Does.Not.Contain("OFFSET"));
            Assert.That(secondPage.Parameters.Select(p => p.Name), Does.Contain("@anchor"),
                "and the key is bound, not written in - a key can be a string");
        });
    }

    /// <summary>
    /// A table with no single-column key cannot be paged by key. It is paged by OFFSET, and the tab
    /// says so rather than letting the second page take four seconds for no visible reason (WS-31).
    /// </summary>
    [Test]
    public async Task ATableWithNoKeyPagesByOffsetAndSaysSoTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        // Logs has no primary key at all - the fixture carries it for exactly this kind of case.
        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Logs");
        editor.PageSize = 1;
        await editor.LoadDataAsync();

        Assert.That(editor.CanPageByKey, Is.False, "no key to page by");
        Assert.That(editor.PagingNote, Is.Null, "and nothing to warn about on the first page");

        await StudioFixture.PressAsync(editor.NextPageCommand);

        Assert.Multiple(() =>
        {
            Assert.That(editor.PageIndex, Is.EqualTo(1));
            Assert.That(editor.EditableData!.Rows, Has.Count.EqualTo(1), "the second row is shown");
            Assert.That(editor.IsDeepPage, Is.True);
            Assert.That(editor.PagingNote, Is.Not.Null.And.Contains("no single-column primary key"),
                "and the reason it is slow is said out loud");
        });
    }

    /// <summary>
    /// Paging is off while there are unapplied edits: the buffer belongs to the rows on screen, and
    /// replacing them under it would either lose the edits or apply them to the wrong rows.
    /// </summary>
    [Test]
    public async Task PagingIsRefusedWhileThereAreUnappliedEditsTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Orders");
        editor.PageSize = 2;
        await editor.LoadDataAsync();

        EditCell(editor, 0, "Status", "changed");

        await StudioFixture.PressAsync(editor.NextPageCommand);

        Assert.Multiple(() =>
        {
            Assert.That(editor.HasChanges, Is.True);
            Assert.That(editor.PageIndex, Is.EqualTo(0), "the page did not move");
            Assert.That(editor.CanGoToNextPage, Is.False, "and the command says so");
        });
    }

    #endregion
}
