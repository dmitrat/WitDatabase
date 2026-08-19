using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A parse error is underlined across the word it is about.
/// </summary>
/// <remarks>
/// <para>
/// <b>Finding 20, and it took driving Studio to settle it.</b> The report said a parse error is not
/// marked at all while a name error is. It IS marked - with a squiggle ONE CHARACTER wide, under the
/// first letter of the token - and beside <c>Deliveries</c> underlined across its whole ten letters,
/// one character reads as nothing. The mechanism was never broken; the length was.
/// </para>
/// <para>
/// <b>The token comes out of the message.</b> Every shape this parser produces names it in single
/// quotes - <i>mismatched input 'Country'</i>, <i>extraneous input 'x'</i>,
/// <i>no viable alternative at input 'xy'</i>, <i>missing ';' at 'SELECT'</i> - and the alternative
/// was to add a field to the parser's error type, which is an engine change for a drawing detail.
/// When there is no quoted token the mark stays one character, which is what it always was.
/// </para>
/// </remarks>
[TestFixture]
public class AParseErrorUnderlinesItsTokenTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private QueryTabViewModel m_tab = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        m_tab = m_studio.FirstQueryTab;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Tests

    /// <summary>
    /// The case from the report, with the length it was missing.
    /// </summary>
    [Test]
    public async Task TheWordTheParserNamesIsTheWordUnderlinedTest()
    {
        m_tab.SqlText = "SELECT Id, Name" + Environment.NewLine
            + "FROM Customers" + Environment.NewLine
            + "WHER Country = 'Ireland'";

        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        Assert.Multiple(() =>
        {
            Assert.That(m_tab.ErrorMessage, Does.Contain("Country"),
                "the parser names the token it could not use");

            Assert.That(m_tab.UnderlineLine, Is.EqualTo(3), "on the line it was written on");

            Assert.That(m_tab.UnderlineLength, Is.EqualTo("Country".Length),
                "and the mark covers the word, not its first letter");
        });
    }

    /// <summary>
    /// The path a person actually sees: the live syntax check, which wins over the executed one.
    /// </summary>
    /// <remarks>
    /// <b>This is the case the screen taught.</b> With the executed path fixed, driving Studio
    /// still showed a one-letter mark - because whenever the text does not parse, the live check
    /// has an answer and <c>UpdateUnderline</c> prefers it, and that branch hard-coded a length of
    /// one. The ViewModel said seven and the window drew one.
    /// </remarks>
    [Test]
    public void TheLiveCheckMarksTheWordTooTest()
    {
        m_tab.SqlText = "SELECT Id, Name" + Environment.NewLine
            + "FROM Customers" + Environment.NewLine
            + "WHER Country = 'Ireland'";

        m_tab.CheckSyntaxNow();

        Assert.Multiple(() =>
        {
            Assert.That(m_tab.UnderlineLine, Is.EqualTo(3));

            Assert.That(m_tab.UnderlineLength, Is.EqualTo("Country".Length),
                "the branch that wins draws the word as well");
        });
    }
    /// <summary>
    /// The control: a message with no token in it still marks something.
    /// </summary>
    [Test]
    public void AMessageWithNoQuotedTokenKeepsTheOldMarkTest()
    {
        Assert.That(QueryTabViewModel.LengthOfTheOffendingToken("something went wrong"), Is.EqualTo(1),
            "one character, which is what every parse error used to get");
    }

    [TestCase("mismatched input 'Country' expecting {')', ','}", 7)]
    [TestCase("extraneous input 'x' expecting EOF", 1)]
    [TestCase("no viable alternative at input 'SELECTFROM'", 10)]
    [TestCase("missing ';' at 'SELECT'", 1)]
    public void TheTokenIsTakenFromTheMessageTest(string message, int length)
    {
        Assert.That(QueryTabViewModel.LengthOfTheOffendingToken(message), Is.EqualTo(length));
    }

    #endregion
}
