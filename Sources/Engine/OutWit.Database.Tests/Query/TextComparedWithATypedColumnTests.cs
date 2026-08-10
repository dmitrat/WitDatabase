using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// Text compared with a typed column is read as that type. <c>Docs/KnownIssues.md</c> 20,
/// fixed 2026-08-10.
/// </summary>
/// <remarks>
/// <para>
/// Every comparison between a text value and a value of another type used to fall through to an
/// ordinal comparison of the two RENDERINGS. That is not a near-miss: it gives wrong answers, and
/// the two worst are wrong in opposite directions on the same row.
/// </para>
/// <code>
/// N > '9'   answered NO  for N = 42, because "42" sorts before "9"
/// N &lt; '9'   answered YES for the same row
/// S = '2026-07-01 13:45:30'   found nothing - a DateTime renders as 2026-07-01T13:45:30.0000000
/// S > '2026-07-01 13:45:30'   answered YES for that very instant - 'T' sorts after the space
/// </code>
/// <para>
/// <b>It was recorded as a temporal-literal problem and it is not one.</b> <c>DATE</c>,
/// <c>TIME</c>, <c>GUID</c> and <c>BOOLEAN</c> happened to work, because their rendering is the way a
/// person writes them - so the defect was visible only where the rendering and the writing disagree,
/// which is <c>DATETIME</c>, <c>DATETIMEOFFSET</c> and <b>every number</b>. An integer column
/// compared with a string parameter is the commonest shape there is.
/// </para>
/// <para>
/// PostgreSQL and SQL Server both read the text as the column's type. Text that is not a value of
/// that type at all - <c>D = 'not a date'</c> - keeps the old behaviour and answers "not equal"
/// rather than being refused: a comparison is not the place to refuse, and that case has a control.
/// </para>
/// </remarks>
[TestFixture]
public class TextComparedWithATypedColumnTests
{
    #region Fields

    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_engine = new WitSqlEngine(WitDatabase.CreateInMemory(), ownsStore: true);

        m_engine.Execute(
            "CREATE TABLE E (Id INT NOT NULL PRIMARY KEY, D DATE, T TIME, S DATETIME, "
            + "O DATETIMEOFFSET, G GUID, B BOOLEAN, N INT, R DOUBLE)");

        m_engine.Execute(
            "INSERT INTO E (Id, D, T, S, O, G, B, N, R) VALUES (1, DATE '2026-07-01', "
            + "TIME '13:45:30', TIMESTAMP '2026-07-01 13:45:30', "
            + "DATETIMEOFFSET '2026-07-01 13:45:30 +03:00', "
            + "'f47ac10b-58cc-4372-a567-0e02b2c3d479', TRUE, 42, 2.5)");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Functions

    private int Count(string sql) => m_engine.Query(sql).Count;

    #endregion

    #region A number against text

    /// <summary>
    /// The pair that says this is not about dates. Both used to be wrong, and in opposite
    /// directions, on the same row - which is the one thing a single case could not have shown.
    /// </summary>
    [Test]
    public void ANumberComparedWithTextComparesNumericallyTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Count("SELECT Id FROM E WHERE N > '9'"), Is.EqualTo(1), "42 > 9");
            Assert.That(Count("SELECT Id FROM E WHERE N < '9'"), Is.Zero, "and 42 is not < 9");
            Assert.That(Count("SELECT Id FROM E WHERE N = '42'"), Is.EqualTo(1));
            Assert.That(Count("SELECT Id FROM E WHERE N = '042'"), Is.EqualTo(1), "a leading zero is a number");
            Assert.That(Count("SELECT Id FROM E WHERE R > '1.5'"), Is.EqualTo(1));
            Assert.That(Count("SELECT Id FROM E WHERE R < '10'"), Is.EqualTo(1), "2.5 < 10, not '2.5' > '10'");
        });
    }

    #endregion

    #region A moment against text

    /// <summary>
    /// The shape the EF provider emitted, and the one issue 2 was reported for.
    /// </summary>
    [TestCase("S = '2026-07-01 13:45:30'", 1)]
    [TestCase("O = '2026-07-01 13:45:30 +03:00'", 1)]
    [TestCase("S = '2026-07-01T13:45:30'", 1)]
    public void AStampIsFoundByTheTextThatWroteItTest(string predicate, int expected)
    {
        Assert.That(Count($"SELECT Id FROM E WHERE {predicate}"), Is.EqualTo(expected));
    }

    /// <summary>
    /// The half that answered WRONGLY rather than emptily: the same instant is not greater than
    /// itself. Ordinally it was, because the rendering carries a <c>T</c> where the text carries a
    /// space, and <c>T</c> sorts after it.
    /// </summary>
    [Test]
    public void AStampComparesAsAMomentAndNotAsItsRenderingTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Count("SELECT Id FROM E WHERE S > '2026-07-01 13:45:30'"), Is.Zero,
                "the same instant is not strictly greater");
            Assert.That(Count("SELECT Id FROM E WHERE S >= '2026-07-01 13:45:30'"), Is.EqualTo(1));
            Assert.That(Count("SELECT Id FROM E WHERE S < '2026-07-01 13:45:31'"), Is.EqualTo(1),
                "one second later IS greater");
            Assert.That(Count("SELECT Id FROM E WHERE S > '2027-01-01 00:00:00'"), Is.Zero);
        });
    }

    /// <summary>
    /// A date written the short way. It is read as a date, which both reference databases do and the
    /// rendering comparison never could.
    /// </summary>
    [Test]
    public void ADateIsReadAsADateRatherThanAsItsSpellingTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Count("SELECT Id FROM E WHERE D = '2026-7-1'"), Is.EqualTo(1));
            Assert.That(Count("SELECT Id FROM E WHERE T = '13:45:30'"), Is.EqualTo(1));
        });
    }

    #endregion

    #region Controls

    /// <summary>
    /// CONTROL: everything that worked before still does. These are the spellings whose rendering
    /// happens to be the way a person writes them, which is exactly why the defect stayed hidden -
    /// so a fix that broke them would have traded one silent wrong answer for another.
    /// </summary>
    [TestCase("D = '2026-07-01'")]
    [TestCase("T > '00:00:00'")]
    [TestCase("G = 'f47ac10b-58cc-4372-a567-0e02b2c3d479'")]
    [TestCase("B = 'true'")]
    [TestCase("B = TRUE")]
    [TestCase("N = 42")]
    [TestCase("D = DATE '2026-07-01'")]
    [TestCase("S = TIMESTAMP '2026-07-01 13:45:30'")]
    [TestCase("O = DATETIMEOFFSET '2026-07-01 13:45:30 +03:00'")]
    public void ControlTheSpellingsThatAlreadyWorkedStillDoTest(string predicate)
    {
        Assert.That(Count($"SELECT Id FROM E WHERE {predicate}"), Is.EqualTo(1));
    }

    /// <summary>
    /// CONTROL: text that is not a value of the other type at all is not refused. A comparison is not
    /// the place to refuse - it answers "not equal", which is what it did before and what a caller
    /// filtering on user input needs. Without this, "text is read as the column's type" would be
    /// equally consistent with a change that throws on every mistyped filter.
    /// </summary>
    [TestCase("D = 'not a date'")]
    [TestCase("D > 'not a date'")]
    [TestCase("N = 'not a number'")]
    [TestCase("G = 'not a guid'")]
    public void ControlTextThatIsNotAValueOfThatTypeIsNotRefusedTest(string predicate)
    {
        Assert.That(() => m_engine.Query($"SELECT Id FROM E WHERE {predicate}"), Throws.Nothing);
        Assert.That(Count($"SELECT Id FROM E WHERE {predicate}"), Is.Zero);
    }

    /// <summary>
    /// CONTROL: text against text is untouched, and still ordinal. The rule is about text meeting
    /// ANOTHER type; applying it to a text column would change how ordinary strings sort.
    /// </summary>
    [Test]
    public void ControlTextAgainstTextIsUnchangedTest()
    {
        m_engine.Execute("CREATE TABLE W (Id INT NOT NULL PRIMARY KEY, V VARCHAR(20))");
        m_engine.Execute("INSERT INTO W (Id, V) VALUES (1, '42')");
        m_engine.Execute("INSERT INTO W (Id, V) VALUES (2, '9')");

        // Ordinal on purpose: on a TEXT column '42' really does sort before '9'.
        Assert.That(Count("SELECT Id FROM W WHERE V < '9'"), Is.EqualTo(1));
        Assert.That(Count("SELECT Id FROM W WHERE V = '42'"), Is.EqualTo(1));
    }

    #endregion
}
