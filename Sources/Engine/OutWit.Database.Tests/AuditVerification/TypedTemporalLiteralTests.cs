using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using OutWit.Database.Parser.Exceptions;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Typed temporal literals - <c>DATE '…'</c>, <c>TIME '…'</c>, <c>TIMESTAMP '…'</c>,
/// <c>DATETIMEOFFSET '…'</c> - and the rule they were built to: <b>the WORD in front decides the
/// type</b>, spelled the way the type is spelled in DDL.
/// </summary>
/// <remarks>
/// <para>
/// <c>Docs/KnownIssues.md</c> 2. EF Core's stock mappings emit exactly these shapes and the grammar
/// had none of them, so a query comparing against an inlined constant failed to parse before it
/// reached the engine. The provider was then made to emit a plain quoted string instead, which parses
/// - and answers with NOTHING, which is what the cases below pin.
/// </para>
/// <para>
/// The pins are the point: this fixture does not change how text compares with a temporal column. It
/// records that the two are not the same question, so that a future change to comparison has to make
/// these cases go red on purpose rather than by accident.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TypedTemporalLiteralTests
{
    #region Fields

    private string m_databasePath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_typed_temporal_{Guid.NewGuid():N}");

        m_engine = new WitSqlEngine(WitDatabase.Create(m_databasePath), ownsStore: true);

        m_engine.Execute(
            "CREATE TABLE Events (Id INT NOT NULL PRIMARY KEY, D DATE, T TIME, S DATETIME, O DATETIMEOFFSET)");

        // One row, written entirely with typed literals: a date, a time, a stamp carrying a FRACTION
        // of a second, and a moment carrying an OFFSET. Those last two are where a quoted string was
        // measured to answer with nothing.
        m_engine.Execute(
            "INSERT INTO Events (Id, D, T, S, O) VALUES (1, DATE '2026-07-01', TIME '13:45:30', "
            + "TIMESTAMP '2026-07-01 13:45:30.1234567', DATETIMEOFFSET '2026-07-01 13:45:30 +03:00')");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
        m_engine = null!;

        if (!Directory.Exists(m_databasePath))
            return;

        try
        {
            Directory.Delete(m_databasePath, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only - a locked file must not fail the run.
        }
    }

    #endregion

    #region What a typed literal finds

    [TestCase("D = DATE '2026-07-01'")]
    [TestCase("T = TIME '13:45:30'")]
    [TestCase("S = TIMESTAMP '2026-07-01 13:45:30.1234567'")]
    [TestCase("O = DATETIMEOFFSET '2026-07-01 13:45:30 +03:00'")]
    public void ATypedLiteralFindsTheRowItWroteTest(string predicate)
    {
        Assert.That(Count($"SELECT Id FROM Events WHERE {predicate}"), Is.EqualTo(1));
    }

    /// <summary>
    /// CONTROL for the four above: a literal naming another value finds nothing. Without it they would
    /// all pass against a filter that reached nothing at all.
    /// </summary>
    [TestCase("D = DATE '2020-01-01'")]
    [TestCase("T = TIME '01:02:03'")]
    [TestCase("S = TIMESTAMP '2020-01-01 01:02:03'")]
    [TestCase("O = DATETIMEOFFSET '2020-01-01 01:02:03 +00:00'")]
    public void ATypedLiteralNamingAnotherValueFindsNothingTest(string predicate)
    {
        Assert.That(Count($"SELECT Id FROM Events WHERE {predicate}"), Is.Zero);
    }

    /// <summary>
    /// A <c>DATETIMEOFFSET</c> compares INSTANTS: the same moment written under a different offset is
    /// the same moment. It is the reading that makes the type worth having, and the one a quoted
    /// string cannot give - two spellings of one instant are two different strings.
    /// </summary>
    [Test]
    public void TheSameInstantUnderAnotherOffsetIsTheSameMomentTest()
    {
        Assert.That(
            Count("SELECT Id FROM Events WHERE O = DATETIMEOFFSET '2026-07-01 10:45:30 +00:00'"),
            Is.EqualTo(1));
    }

    #endregion

    #region What a bare string does - PINS, not endorsements

    /// <summary>
    /// <b>PINS A DEFECT, NOT CORRECT BEHAVIOUR.</b> A row written by a quoted string cannot be found
    /// by that very same string: text is not converted to the column's type before the comparison, so
    /// the two are compared as different things and nothing matches. This is what the EF provider was
    /// doing until 2026-08-09 - it had traded a loud parse error for an empty result set.
    ///
    /// <para>
    /// If a future change makes text compare as the column's type, these two go GREEN and should be
    /// deleted rather than inverted: the fix would make the question uninteresting.
    /// </para>
    /// </summary>
    [Test]
    public void ABareStringDoesNotFindTheStampItWroteTest()
    {
        m_engine.Execute(
            "INSERT INTO Events (Id, S, O) VALUES (2, '2026-07-01 09:00:00.1234567', "
            + "'2026-07-01 09:00:00.0000000+03:00')");

        Assert.Multiple(() =>
        {
            Assert.That(Count("SELECT Id FROM Events WHERE Id = 2"), Is.EqualTo(1),
                "CONTROL: the row is there");

            Assert.That(Count("SELECT Id FROM Events WHERE S = '2026-07-01 09:00:00.1234567'"), Is.Zero,
                "PINS A DEFECT: written with this text and not found by it");

            Assert.That(Count("SELECT Id FROM Events WHERE O = '2026-07-01 09:00:00.0000000+03:00'"),
                Is.Zero, "PINS A DEFECT: the same, for a moment with an offset");

            Assert.That(Count("SELECT Id FROM Events WHERE S = TIMESTAMP '2026-07-01 09:00:00.1234567'"),
                Is.EqualTo(1), "and the typed literal finds it, which is what says the row is fine");
        });
    }

    /// <summary>
    /// A bare string DOES find a date, and that is why the defect above went unnoticed: an ISO date is
    /// ten characters with nothing after it, so the two forms happen to agree. Recorded so that
    /// "temporal literals are broken" is not read wider than it is.
    /// </summary>
    [Test]
    public void ABareStringStillFindsADateTest()
    {
        Assert.That(Count("SELECT Id FROM Events WHERE D = '2026-07-01'"), Is.EqualTo(1));
    }

    #endregion

    #region Where else a literal is allowed

    /// <summary>
    /// A DEFAULT is a literal too, and it could not be written as a typed one before - so a date
    /// default was stored as TEXT and had to be converted on every insert. The engine writes the
    /// default back out of the catalogue, which is why this is a round trip rather than a parse.
    /// </summary>
    [Test]
    public void ADateDefaultKeepsItsTypeTest()
    {
        m_engine.Execute("CREATE TABLE WithDefault (Id INT NOT NULL PRIMARY KEY, "
                         + "D DATE DEFAULT DATE '2026-07-01')");

        m_engine.Execute("INSERT INTO WithDefault (Id) VALUES (1)");

        Assert.That(Count("SELECT Id FROM WithDefault WHERE D = DATE '2026-07-01'"), Is.EqualTo(1));
    }

    [Test]
    public void ATypedLiteralIsAllowedWhereverAValueIsTest()
    {
        m_engine.Execute("UPDATE Events SET D = DATE '2026-08-09' WHERE Id = 1");

        Assert.Multiple(() =>
        {
            Assert.That(Count("SELECT Id FROM Events WHERE D = DATE '2026-08-09'"), Is.EqualTo(1));
            Assert.That(Count("SELECT Id FROM Events WHERE D BETWEEN DATE '2026-08-01' "
                              + "AND DATE '2026-08-31'"), Is.EqualTo(1));
            Assert.That(Count("SELECT Id FROM Events WHERE D IN (DATE '2026-08-09', DATE '2020-01-01')"),
                Is.EqualTo(1));
        });
    }

    #endregion

    #region What the keyword refuses

    /// <summary>
    /// The rule, stated as a refusal: an offset inside a <c>TIMESTAMP</c> is not dropped, it is
    /// refused, and the message names the keyword that would keep it. PostgreSQL accepts the shape and
    /// discards the offset, which is one row meaning two different instants in two databases.
    /// </summary>
    [Test]
    public void ATimestampWillNotSwallowAnOffsetTest()
    {
        var refused = Assert.Throws<WitSqlParsingException>(() =>
            m_engine.Execute("SELECT Id FROM Events WHERE S = TIMESTAMP '2026-07-01 13:45:30 +03:00'"));

        Assert.That(refused!.Message, Does.Contain("DATETIMEOFFSET"));
    }

    #endregion

    #region Tools

    private int Count(string sql) => m_engine.Query(sql).Count();

    #endregion
}
