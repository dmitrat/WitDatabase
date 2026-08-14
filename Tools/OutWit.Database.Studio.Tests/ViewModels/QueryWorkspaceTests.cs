using System.Data;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Stage 6's readiness criterion, which the plan does not give: <b>one session of work with a query
/// goes through from the first keystroke to the history, and every step of it can be checked.</b>
///
/// The cases below are that session, in order - completion, the underline, execution, the plan, the
/// four panels, formatting, the manual transaction, and the history. Everything runs against a real
/// database through the real ViewModel graph.
/// </summary>
[TestFixture]
public class QueryWorkspaceTests
{
    #region Fixture

    private StudioFixture m_fixture = null!;
    private QueryTabViewModel m_tab = null!;

    [SetUp]
    public async Task SetUp()
    {
        m_fixture = await StudioFixture.CreateAsync(withHistory: true);

        await m_fixture.Explorer.RefreshAsync();

        m_tab = m_fixture.FirstQueryTab;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region 1 - completion

    [Test]
    public async Task TheTabSuggestsFromTheSchemaOfItsOwnConnectionTest()
    {
        const string sql = "SELECT * FROM ";

        var items = await m_tab.SuggestAsync(sql, sql.Length);

        Assert.That(items.Select(item => item.Text), Does.Contain("Customers"));
        Assert.That(items.Select(item => item.Text), Does.Contain("Orders"));
    }

    /// <summary>
    /// The half that cannot be done without the connection: the columns of the table the alias names
    /// are read from the database on demand, by the tab, before the list is built.
    /// </summary>
    [Test]
    public async Task ColumnsAfterADotAreLoadedWhenTheyAreNeededTest()
    {
        const string sql = "SELECT * FROM Orders o WHERE o.";

        Assert.That(m_fixture.Database.Catalog.Columns("Orders"), Is.Empty,
            "the control: nothing has asked for the columns of Orders yet");

        var items = await m_tab.SuggestAsync(sql, sql.Length);

        Assert.That(items.Select(item => item.Text), Is.EquivalentTo(new[] { "Id", "CustomerId", "Total", "Status" }));
    }

    [Test]
    public async Task ATabWithNoConnectionSuggestsNothingRatherThanThrowingTest()
    {
        var orphan = new QueryTabViewModel(m_fixture.App, null);

        Assert.That(await orphan.SuggestAsync("SELECT * FROM ", 14), Is.Empty);
    }

    #endregion

    #region 2 - the underline

    [Test]
    public void ASyntaxErrorIsUnderlinedWhereItWasWrittenTest()
    {
        m_tab.SqlText = "SELECT Id, Total\nFROM Orders\nWEHRE Total > 100;";

        m_tab.CheckSyntaxNow();

        Assert.That(m_tab.SyntaxErrorMessage, Is.Not.Null);
        Assert.That(m_tab.SyntaxErrorLine, Is.EqualTo(3), "line 3, where WEHRE is - not line 1");
        Assert.That(m_tab.UnderlineLine, Is.EqualTo(3));
    }

    /// <summary>
    /// A statement that runs takes the previous statement's mark away with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found by driving, and it had been there since the underline was built.</b> A refused
    /// <c>UPDATE</c> on a read-only connection put the mark at line 1; the <c>SELECT</c> typed over
    /// it ran and returned rows, and the mark was still there. A fresh tab had none, which is what
    /// proved the mark was the underline rather than something the editor draws by itself.
    /// </para>
    /// <para>
    /// The cause is that <c>ErrorLine</c> and <c>UnderlineLine</c> are two properties: execution
    /// cleared the first and only <c>UpdateUnderline</c> copies it into the second. So the error was
    /// gone from the message panel and the status bar, and the text still carried it - one of
    /// WS-11's three places quietly disagreeing with the other two.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AStatementThatSucceedsTakesTheLastErrorsMarkWithItTest()
    {
        m_tab.SqlText = "UPDATE Orders SET Total = Total / 0 WHERE Id = 1;";
        await StudioFixture.PressAsync(m_tab.ExecuteQueryCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_tab.ErrorMessage, Is.Not.Null, "the control: this statement has to fail");
            Assert.That(m_tab.UnderlineLine, Is.GreaterThan(0), "and it has to be marked in the text");
        });

        m_tab.SqlText = "SELECT Id FROM Orders;";
        await StudioFixture.PressAsync(m_tab.ExecuteQueryCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_tab.ErrorMessage, Is.Null, "the control: this one has to succeed");
            Assert.That(m_tab.UnderlineLine, Is.Zero,
                "the mark from the failed statement is still under text that has since run");
        });
    }

    [Test]
    public void TextThatParsesLeavesNothingUnderlinedTest()
    {
        m_tab.SqlText = "SELECT Id, Total FROM Orders WHERE Total > 100;";

        m_tab.CheckSyntaxNow();

        Assert.That(m_tab.SyntaxErrorMessage, Is.Null);
        Assert.That(m_tab.UnderlineLine, Is.Zero);
    }

    /// <summary>
    /// The one the engine gives no position for at all: <c>Table 'Ordres' not found</c>, and nothing
    /// about where. Studio finds the name among the statement's own tokens and offers the nearest
    /// name this database does have.
    /// </summary>
    [Test]
    public async Task AMissingTableIsUnderlinedOnItsOwnNameAndACorrectionIsOfferedTest()
    {
        m_tab.SqlText = "SELECT Id FROM Customers;\n\nSELECT Id\nFROM Ordres;";

        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        Assert.That(m_tab.ErrorMessage, Does.Contain("Ordres"));
        Assert.That(m_tab.ErrorLine, Is.EqualTo(4), "line 4, where Ordres is written");
        Assert.That(m_tab.ErrorColumn, Is.EqualTo(5));
        Assert.That(m_tab.ErrorLength, Is.EqualTo("Ordres".Length),
            "the name is underlined, not one character of it");
        Assert.That(m_tab.ErrorSuggestion, Is.EqualTo("Orders"));

        // The message and the underline have to agree about where. Found by running the application:
        // the squiggle was under Ordres on line 4 while the message read "Line 1, column 1", because
        // the message was written from the statement's own start before the name had been found.
        Assert.That(m_tab.ErrorMessage, Does.Contain($"line {m_tab.ErrorLine}").IgnoreCase);
        Assert.That(m_tab.ErrorMessage, Does.Contain($"column {m_tab.ErrorColumn + 1}"));

        Assert.That(m_fixture.MainWindow.StatusText, Is.EqualTo(m_tab.ErrorMessage),
            "and so does the status bar, which was a third answer - it had been handed the message " +
            "before the position was corrected");

        Assert.That(m_tab.ApplySuggestionCommand.CanExecute(null), Is.True,
            "and the Replace button has to be pressable - it was grey, because a RelayCommand's " +
            "CanExecute is not re-asked unless it is told to");

        // And the correction is an edit, not a remark.
        m_tab.ApplySuggestionCommand.Execute(null);

        Assert.That(m_tab.SqlText, Does.Contain("FROM Orders"));
        Assert.That(m_tab.SqlText, Does.Not.Contain("Ordres"));
        Assert.That(m_tab.SyntaxErrorMessage, Is.Null);
    }

    [Test]
    public async Task AMissingColumnIsFoundTooTest()
    {
        await m_fixture.Database.Catalog.LoadColumnsAsync(["Orders"]);

        m_tab.SqlText = "SELECT Totl FROM Orders;";

        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        Assert.That(m_tab.ErrorSuggestion, Is.EqualTo("Total"));
    }

    /// <summary>
    /// The control: a failure that is not about a name in the text gets no suggestion. Offering one
    /// for a constraint violation would be a guess wearing the clothes of an answer.
    /// </summary>
    [Test]
    public async Task AConstraintViolationGetsNoSuggestionTest()
    {
        m_tab.SqlText = "INSERT INTO Customers (Name) VALUES (NULL);";

        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        Assert.That(m_tab.ErrorMessage, Does.Contain("NOT NULL"));
        Assert.That(m_tab.ErrorSuggestion, Is.Null);
        Assert.That(m_tab.ErrorName, Is.Null);
    }

    #endregion

    #region 3 - the plan

    [Test]
    public async Task ThePlanTabShowsTheStatementUnderTheCursorAsATreeTest()
    {
        for (var i = 0; i < 40; i++)
            await m_fixture.Database.ExecuteNonQueryAsync(
                $"INSERT INTO Orders (CustomerId, Total, Status) VALUES ({i % 3 + 1}, {200 + i}, 'new')");

        m_tab.SqlText = "SELECT * FROM Customers;\nSELECT * FROM Orders WHERE Total = 205;";
        m_tab.CaretOffset = m_tab.SqlText.IndexOf("WHERE", StringComparison.Ordinal);

        await StudioFixture.PressAsync(m_tab.ShowPlanCommand);

        Assert.That(m_tab.PlanMessage, Is.Null, m_tab.PlanMessage);
        Assert.That(m_tab.Plan.IsEmpty, Is.False);
        Assert.That(m_tab.PlanStatement, Does.Contain("Total = 205"),
            "the plan is of the statement the cursor is in, not of the first one");

        var scan = m_tab.Plan.All.SingleOrDefault(node => node.Kind == PlanOperatorKind.TableScan);

        Assert.That(scan, Is.Not.Null);
        Assert.That(scan!.Warning, Is.Not.Null, "a scan under a filter is what the panel exists to show");
    }

    [Test]
    public async Task AStatementTheEngineWillNotExplainSaysSoTest()
    {
        m_tab.SqlText = "UPDATE Orders SET Status = 'x' WHERE Id = 1;";
        m_tab.CaretOffset = 0;

        await StudioFixture.PressAsync(m_tab.ShowPlanCommand);

        // Measured 2026-08-06: this engine explains queries only.
        Assert.That(m_tab.Plan.IsEmpty, Is.True);
        Assert.That(m_tab.PlanMessage, Is.Not.Null.And.Not.Empty);
    }

    #endregion

    #region 4 - the four panels

    [Test]
    public async Task EveryPanelHasSomethingInItAfterAScriptTest()
    {
        m_tab.SqlText = """
            INSERT INTO Logs (Message, Level) VALUES ('one', 'INFO');
            INSERT INTO Logs (Message, Level) VALUES ('two', 'INFO');
            SELECT * FROM Logs;
            """;

        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        // Result
        Assert.That(m_tab.HasResults, Is.True);
        Assert.That(m_tab.TotalRowCount, Is.EqualTo(4));

        // Messages - one line per statement (WS-22)
        Assert.That(m_tab.Statements, Has.Count.EqualTo(3));
        Assert.That(m_tab.Statements[0].Summary, Does.Contain("INSERT"));
        Assert.That(m_tab.Statements[2].ReturnedRows, Is.True);

        // Plan
        m_tab.CaretOffset = m_tab.SqlText.LastIndexOf("SELECT", StringComparison.Ordinal);
        await StudioFixture.PressAsync(m_tab.ShowPlanCommand);
        Assert.That(m_tab.Plan.IsEmpty, Is.False);

        // History
        await StudioFixture.PressAsync(m_tab.RefreshHistoryCommand);
        Assert.That(m_tab.History, Is.Not.Empty);
        Assert.That(m_tab.History[0].Text, Does.Contain("SELECT * FROM Logs"));
    }

    #endregion

    #region 5 - formatting

    [Test]
    public void FormattingRewritesWhatItCanAndSaysWhatItDidNotTest()
    {
        m_tab.SqlText = "create table Staging (Id INTEGER PRIMARY KEY);\nselect Id,Total from Orders where Total>1;";

        m_tab.FormatCommand.Execute(null);

        Assert.That(m_tab.SqlText, Does.Contain("create table Staging"), "the CREATE is untouched");
        Assert.That(m_tab.SqlText, Does.Contain("SELECT Id, Total"));
        Assert.That(m_tab.SqlText, Does.Match("(?s).*\nFROM Orders.*"));
        Assert.That(m_tab.FormatSummary, Does.Contain("left as written"));
    }

    [Test]
    public void FormattingTextThatDoesNotParseChangesNothingTest()
    {
        const string original = "select Id from Orders wehre Total > 1;";

        m_tab.SqlText = original;
        m_tab.FormatCommand.Execute(null);

        Assert.That(m_tab.SqlText, Is.EqualTo(original));
    }

    #endregion

    #region 6 - the manual transaction

    /// <summary>
    /// The scenario the design draws: the toggle in the toolbar, an INSERT, and then Rollback - and
    /// the row is not in the database. Read back by scanning the rows, never through a count.
    /// </summary>
    [Test]
    public async Task AnInsertInsideAManualTransactionCanBeRolledBackFromTheTabTest()
    {
        m_tab.Isolation = IsolationLevel.Serializable;

        await StudioFixture.PressAsync(m_tab.BeginTransactionCommand);

        Assert.That(m_tab.HasOpenTransaction, Is.True);
        Assert.That(m_tab.TransactionState, Does.Contain("open"));

        m_tab.SqlText = "INSERT INTO Logs (Message, Level) VALUES ('inside a transaction', 'INFO');";
        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        Assert.That(await m_fixture.CountRowsAsync("Logs"), Is.EqualTo(3));
        Assert.That(m_tab.TransactionStatementCount, Is.GreaterThan(0),
            "the indicator says what is at stake in the transaction");

        await StudioFixture.PressAsync(m_tab.RollbackTransactionCommand);

        Assert.That(m_tab.HasOpenTransaction, Is.False);
        Assert.That(m_tab.TransactionState, Is.EqualTo("Autocommit"));
        Assert.That(await m_fixture.CountRowsAsync("Logs"), Is.EqualTo(2), "the row is not in the database");
    }

    /// <summary>
    /// The control in the other direction, and the one that proves the rollback above did something:
    /// the same scenario with Commit leaves the row.
    /// </summary>
    [Test]
    public async Task AndCommitLeavesItThereTest()
    {
        await StudioFixture.PressAsync(m_tab.BeginTransactionCommand);

        m_tab.SqlText = "INSERT INTO Logs (Message, Level) VALUES ('committed', 'INFO');";
        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        await StudioFixture.PressAsync(m_tab.CommitTransactionCommand);

        Assert.That(await m_fixture.CountRowsAsync("Logs"), Is.EqualTo(3));
    }

    /// <summary>
    /// Two tabs of one connection share one transaction, because a connection can hold exactly one.
    /// The second tab has to be told - a tab showing "Autocommit" over an open transaction is the
    /// worst of both.
    /// </summary>
    [Test]
    public async Task TheOtherTabOfTheSameConnectionSeesTheTransactionTest()
    {
        var second = new QueryTabViewModel(m_fixture.App, m_fixture.Database);

        await StudioFixture.PressAsync(m_tab.BeginTransactionCommand);

        Assert.That(second.HasOpenTransaction, Is.True);
        Assert.That(second.TransactionState, Does.Contain("open"));

        await StudioFixture.PressAsync(m_tab.RollbackTransactionCommand);

        Assert.That(second.HasOpenTransaction, Is.False);
    }

    /// <summary>
    /// And the control that it is the CONNECTION's: a tab of another database is unaffected.
    /// </summary>
    [Test]
    public async Task ATabOfAnotherConnectionIsUnaffectedTest()
    {
        var other = await m_fixture.OpenAnotherAsync("beta");
        var otherTab = new QueryTabViewModel(m_fixture.App, other);

        await StudioFixture.PressAsync(m_tab.BeginTransactionCommand);

        Assert.That(m_tab.HasOpenTransaction, Is.True);
        Assert.That(otherTab.HasOpenTransaction, Is.False);

        await StudioFixture.PressAsync(m_tab.RollbackTransactionCommand);
    }

    #endregion

    #region 7 - the history

    [Test]
    public async Task WhatWasRunIsInTheHistoryAndCanBeBroughtBackTest()
    {
        m_tab.SqlText = "SELECT * FROM Customers;";
        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        m_tab.SqlText = "SELECT * FROM Orders;";
        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        await StudioFixture.PressAsync(m_tab.RefreshHistoryCommand);

        Assert.That(m_tab.History.Select(entry => entry.Text),
            Does.Contain("SELECT * FROM Customers;"));

        var remembered = m_tab.History.First(entry => entry.Text.Contains("Customers"));

        m_tab.UseHistoryEntryCommand.Execute(remembered);

        Assert.That(m_tab.SqlText, Is.EqualTo(remembered.Text));
        Assert.That(m_tab.HasResults, Is.True,
            "bringing a query back puts the TEXT in the editor and runs nothing");
    }

    /// <summary>
    /// Found by running the application: the panel was filled only by the Search button, so opening
    /// the History tab showed a blank panel under a heading that promised a history. Every ViewModel
    /// case here called Refresh itself - which is the one step a user does not take.
    /// </summary>
    [Test]
    public async Task OpeningTheHistoryPanelFillsItTest()
    {
        m_tab.SqlText = "SELECT * FROM Customers;";
        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        Assert.That(m_tab.History, Is.Empty, "the control: nothing has asked for the history yet");

        m_tab.IsHistorySelected = true;

        // The refresh is started by the property change and awaited here, since nothing else can.
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (m_tab.History.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.That(m_tab.History, Is.Not.Empty,
            "opening the panel is the request to see what is in it");
    }

    [Test]
    public async Task AFailedQueryIsRememberedAndSearchableTest()
    {
        m_tab.SqlText = "SELECT * FROM Ordres;";
        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        m_tab.HistorySearch = "Ordres";

        await StudioFixture.PressAsync(m_tab.RefreshHistoryCommand);

        Assert.That(m_tab.History, Has.Count.EqualTo(1));
        Assert.That(m_tab.History[0].Status, Is.EqualTo("error"));
    }

    [Test]
    public async Task WithNoHistoryStoreTheTabSaysSoAndGoesOnWorkingTest()
    {
        await using var noHistory = await StudioFixture.CreateAsync();

        var tab = noHistory.FirstQueryTab;

        tab.SqlText = "SELECT * FROM Customers;";
        await tab.ExecuteSqlAsync(tab.SqlText);

        Assert.That(tab.HasResults, Is.True, "the query runs");

        await StudioFixture.PressAsync(tab.RefreshHistoryCommand);

        Assert.That(tab.History, Is.Empty);
        Assert.That(tab.HistoryMessage, Does.Contain("unavailable"));
    }

    #endregion
}
