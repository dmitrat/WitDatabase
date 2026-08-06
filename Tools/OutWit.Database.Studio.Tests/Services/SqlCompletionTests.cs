using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Completion, against the schema of a real database (WS-24).
///
/// The catalogue is loaded from the engine in every case - the point of the feature is that Studio
/// offers the names THIS database has, and a fixed list of names would pass a test and help nobody.
/// </summary>
[TestFixture]
public class SqlCompletionTests
{
    #region Fixture

    private StudioFixture m_fixture = null!;
    private ISchemaCatalog m_catalog = null!;

    [SetUp]
    public async Task SetUp()
    {
        m_fixture = await StudioFixture.CreateAsync();

        m_catalog = m_fixture.Database.Catalog;

        await m_catalog.RefreshAsync();
        await m_catalog.LoadColumnsAsync(["Customers", "Orders", "Logs"]);
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region The catalogue

    [Test]
    public void TheCatalogueHoldsWhatTheDatabaseHasTest()
    {
        Assert.That(m_catalog.Tables, Does.Contain("Customers"));
        Assert.That(m_catalog.Tables, Does.Contain("Orders"));
        Assert.That(m_catalog.Views.Select(view => view), Does.Contain("ActiveOrders"));
        Assert.That(m_catalog.Columns("Orders").Select(column => column.Name),
            Is.EquivalentTo(new[] { "Id", "CustomerId", "Total", "Status" }));
    }

    [Test]
    public void ColumnsThatWereNeverAskedForAreEmptyRatherThanWrongTest()
    {
        Assert.That(m_catalog.Columns("OrdersAudit"), Is.Empty,
            "a catalogue that has not read something must say nothing about it, not guess");
        Assert.That(m_catalog.Knows("OrdersAudit"), Is.True, "though it knows the table is there");
    }

    #endregion

    #region Where the caret is

    [Test]
    public void AfterFromTheSuggestionsAreObjectsOfThisDatabaseTest()
    {
        var items = SuggestAt("SELECT * FROM ");

        Assert.That(Texts(items), Does.Contain("Customers"));
        Assert.That(Texts(items), Does.Contain("Orders"));
        Assert.That(Texts(items), Does.Contain("ActiveOrders"), "a view is something to select from");
        Assert.That(Texts(items), Does.Not.Contain("SELECT"), "a keyword is not a table");
    }

    [Test]
    public void AfterAnAliasAndADotTheSuggestionsAreThatTablesColumnsTest()
    {
        var items = SuggestAt("SELECT * FROM Orders o WHERE o.");

        Assert.That(Texts(items), Is.EquivalentTo(new[] { "Id", "CustomerId", "Total", "Status" }));
        Assert.That(items.All(item => item.Kind == SqlCompletionKind.Column), Is.True);
    }

    /// <summary>
    /// The control that the dot means something: the same caret after a DIFFERENT alias has to give a
    /// different list, or the implementation is offering every column it has ever seen.
    /// </summary>
    [Test]
    public void TheColumnsAreOfTheAliasedTableAndNotOfTheOtherOneTest()
    {
        const string sql = "SELECT * FROM Customers c JOIN Orders o ON o.CustomerId = c.Id WHERE ";

        var ofCustomers = Texts(SuggestAt(sql + "c."));
        var ofOrders = Texts(SuggestAt(sql + "o."));

        Assert.That(ofCustomers, Does.Contain("Email"));
        Assert.That(ofCustomers, Does.Not.Contain("Total"));

        Assert.That(ofOrders, Does.Contain("Total"));
        Assert.That(ofOrders, Does.Not.Contain("Email"));
    }

    [Test]
    public void ATableCanBeQualifiedByItsOwnNameTest()
    {
        var items = SuggestAt("SELECT * FROM Orders WHERE Orders.");

        Assert.That(Texts(items), Does.Contain("Total"));
    }

    [Test]
    public void InsideAStatementTheColumnsInScopeComeFirstTest()
    {
        var items = SuggestAt("SELECT * FROM Orders o WHERE To");

        Assert.That(items, Is.Not.Empty);
        Assert.That(items[0].Text, Is.EqualTo("Total"),
            "a column of the table being queried, before a keyword that also starts with To");
    }

    [Test]
    public void AtTheStartOfAStatementKeywordsAreOfferedTest()
    {
        var items = SuggestAt("SEL");

        Assert.That(Texts(items), Does.Contain("SELECT"));
    }

    [Test]
    public void AfterASemicolonTheNextStatementStartsAgainTest()
    {
        var items = SuggestAt("SELECT * FROM Orders; INS");

        Assert.That(Texts(items), Does.Contain("INSERT"));
    }

    [Test]
    public void NothingIsSuggestedInsideAStringTest()
    {
        var context = SqlCompletion.Analyze("SELECT * FROM Logs WHERE Message = 'FROM ", 41);

        Assert.That(context.Target, Is.EqualTo(SqlCompletionTarget.None));
        Assert.That(SqlCompletion.Suggest(context, m_catalog), Is.Empty);
    }

    [Test]
    public void NothingIsSuggestedInsideACommentTest()
    {
        var context = SqlCompletion.Analyze("-- select from ", 15);

        Assert.That(context.Target, Is.EqualTo(SqlCompletionTarget.None));
    }

    [Test]
    public void TheWordBeingTypedIsWhatGetsReplacedTest()
    {
        const string sql = "SELECT * FROM Cust";

        var context = SqlCompletion.Analyze(sql, sql.Length);

        Assert.That(context.Prefix, Is.EqualTo("Cust"));
        Assert.That(context.ReplaceFrom, Is.EqualTo(14),
            "an accepted item replaces the word, not the space in front of it");
        Assert.That(Texts(SqlCompletion.Suggest(context, m_catalog)), Does.Contain("Customers"));
    }

    [Test]
    public void OnlyWhatTheTypedPrefixCanBecomeIsOfferedTest()
    {
        var items = SuggestAt("SELECT * FROM Cu");

        Assert.That(Texts(items), Does.Contain("Customers"));
        Assert.That(Texts(items), Does.Not.Contain("Orders"));
    }

    [Test]
    public void AnAliasIsOfferedInAnExpressionTest()
    {
        var items = SuggestAt("SELECT * FROM Orders o WHERE o");

        Assert.That(Texts(items), Does.Contain("o"));
    }

    #endregion

    #region What the caller has to load

    [Test]
    public void TheObjectsNeededForASuggestionAreNamedTest()
    {
        const string afterDot = "SELECT * FROM Customers c JOIN Orders o ON o.";
        const string inExpression = "SELECT * FROM Customers c JOIN Orders o ON ";

        Assert.That(SqlCompletion.ObjectsToLoad(SqlCompletion.Analyze(afterDot, afterDot.Length)),
            Is.EquivalentTo(new[] { "Orders" }),
            "after a dot only one table's columns can be needed");

        Assert.That(SqlCompletion.ObjectsToLoad(SqlCompletion.Analyze(inExpression, inExpression.Length)),
            Is.EquivalentTo(new[] { "Customers", "Orders" }),
            "and in an expression, every table the statement has joined");
    }

    /// <summary>
    /// The half of the feature that has to work on text nobody could execute. If completion needed the
    /// statement to parse it would be silent exactly while it is being typed.
    /// </summary>
    [Test]
    public void TextThatDoesNotParseStillCompletesTest()
    {
        Assert.That(SqlScript.Split("SELECT * FROM Orders o WHERE o.").IsSuccess, Is.False,
            "the control: the parser refuses this text");

        Assert.That(Texts(SuggestAt("SELECT * FROM Orders o WHERE o.")), Does.Contain("Total"));
    }

    #endregion

    #region The vocabulary

    [Test]
    public void TheLanguageComesFromTheHighlightingFileTest()
    {
        Assert.That(SqlVocabulary.Keywords, Does.Contain("SELECT"));
        Assert.That(SqlVocabulary.Keywords, Does.Contain("PRIMARY"));
        Assert.That(SqlVocabulary.Functions, Does.Contain("COUNT"));
        Assert.That(SqlVocabulary.DataTypes, Does.Contain("VARCHAR"));

        Assert.That(SqlVocabulary.Keywords.Count, Is.GreaterThan(100),
            "the file has over three hundred words in it; a handful means it was not read");

        // The highlighting file itself lists some words under two colours - REPLACE is a keyword and a
        // function there. In a completion list a word is one thing, and this is where that is decided.
        Assert.That(SqlVocabulary.Keywords.Intersect(SqlVocabulary.Functions), Is.Empty,
            "a word is one kind of thing, and the icon beside it says which");
        Assert.That(SqlVocabulary.Keywords.Intersect(SqlVocabulary.DataTypes), Is.Empty);
        Assert.That(SqlVocabulary.Functions, Does.Contain("REPLACE"));
    }

    #endregion

    #region Tools

    private IReadOnlyList<SqlCompletionItem> SuggestAt(string sql)
    {
        var context = SqlCompletion.Analyze(sql, sql.Length);

        return SqlCompletion.Suggest(context, m_catalog);
    }

    private static IReadOnlyList<string> Texts(IEnumerable<SqlCompletionItem> items)
    {
        return items.Select(item => item.Text).ToList();
    }

    #endregion
}
