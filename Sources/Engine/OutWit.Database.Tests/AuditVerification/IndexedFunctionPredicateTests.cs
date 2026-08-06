using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A predicate that wraps an indexed column in a function returned the WRONG ROWS.
///
/// <c>WHERE ABS(V) = 7</c> over a table holding <c>V = -7</c> answered with the row while there was no
/// index, and with <b>nothing</b> once an ordinary index on <c>V</c> existed - the planner matched the
/// predicate to the index by the column INSIDE the call and then sought the literal in the index, which
/// holds the raw values. Dropping the index made the answer right again.
///
/// Found on 2026-08-06 while building Studio's schema designer, on a table of 200 rows so the
/// ten-row threshold below which no index is considered was not what was being measured. Reproduced on
/// B-Tree and on LSM, across a close and reopen, with LOWER, UPPER and ABS; <c>V + 0 = -7</c> and
/// <c>-V = 7</c> stayed correct, so it is specifically a function CALL.
///
/// Every case here compares the answer WITH the index against the answer WITHOUT it. That is the only
/// assertion that matters: an index is an implementation detail and must never change what a query
/// returns.
/// </summary>
[TestFixture]
public sealed class IndexedFunctionPredicateTests
{
    #region Constants

    /// <summary>
    /// Above <c>MIN_ROWS_FOR_INDEX</c>, or the planner does not consider an index at all and every
    /// case here would pass without proving anything.
    /// </summary>
    private const int ROWS = 200;

    #endregion

    #region Fields

    private string m_databasePath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_fn_predicate_{Guid.NewGuid():N}");

        m_engine = new WitSqlEngine(WitDatabase.Create(m_databasePath), ownsStore: true);

        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V INT NOT NULL, S TEXT NOT NULL)");

        for (var i = 1; i <= ROWS; i++)
            m_engine.Execute($"INSERT INTO T (Id, V, S) VALUES ({i}, {i * 10}, 'row{i}')");

        // The two rows every case below is about: one negative number and one name that is not already
        // in the case the predicate asks for.
        m_engine.Execute($"INSERT INTO T (Id, V, S) VALUES ({ROWS + 1}, -7, 'MIXEDCASE')");
        m_engine.Execute($"INSERT INTO T (Id, V, S) VALUES ({ROWS + 2}, -8, 'MixedTwo')");
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

    #region Tests

    [Test]
    public void AnIndexDoesNotChangeTheAnswerOfAnArithmeticFunctionPredicateTest()
    {
        var withoutIndex = Ids("SELECT Id FROM T WHERE ABS(V) = 7");

        Assert.That(withoutIndex, Is.EqualTo(new[] { (long)ROWS + 1 }),
            "CONTROL: with no index the answer is the negative row, and everything below compares "
            + "against it");

        m_engine.Execute("CREATE INDEX IX_T_V ON T (V)");

        Assert.That(Ids("SELECT Id FROM T WHERE ABS(V) = 7"), Is.EqualTo(withoutIndex),
            "an index must not change what a query returns - the planner used it for a predicate "
            + "about ABS(V) and sought 7 among the raw values");
    }

    [Test]
    public void AnIndexDoesNotChangeTheAnswerOfALowerPredicateTest()
    {
        var withoutIndex = Ids("SELECT Id FROM T WHERE LOWER(S) = 'mixedcase'");

        Assert.That(withoutIndex, Is.EqualTo(new[] { (long)ROWS + 1 }), "CONTROL");

        m_engine.Execute("CREATE INDEX IX_T_S ON T (S)");

        Assert.That(Ids("SELECT Id FROM T WHERE LOWER(S) = 'mixedcase'"), Is.EqualTo(withoutIndex));
    }

    [Test]
    public void AnIndexDoesNotChangeTheAnswerOfAnUpperPredicateTest()
    {
        var withoutIndex = Ids("SELECT Id FROM T WHERE UPPER(S) = 'MIXEDTWO'");

        Assert.That(withoutIndex, Is.EqualTo(new[] { (long)ROWS + 2 }), "CONTROL");

        m_engine.Execute("CREATE INDEX IX_T_S ON T (S)");

        Assert.That(Ids("SELECT Id FROM T WHERE UPPER(S) = 'MIXEDTWO'"), Is.EqualTo(withoutIndex));
    }

    /// <summary>
    /// The defect is quiet on most data: where the raw value already equals the wrapped one - a
    /// lower-case name, a positive number - the wrong seek finds the right row by luck. This case is
    /// the one that passes either way, and it is here so that the three above are known to be about
    /// the values rather than about the shape of the query.
    /// </summary>
    [Test]
    public void ThePredicateThatWouldPassEitherWayStillPassesTest()
    {
        var withoutIndex = Ids("SELECT Id FROM T WHERE ABS(V) = 70");

        m_engine.Execute("CREATE INDEX IX_T_V ON T (V)");

        Assert.That(Ids("SELECT Id FROM T WHERE ABS(V) = 70"), Is.EqualTo(withoutIndex));
        Assert.That(withoutIndex, Is.Not.Empty);
    }

    /// <summary>
    /// Arithmetic around the column was always correct, and stays correct: the fix must not turn a
    /// working predicate into a table scan by being too broad.
    /// </summary>
    [Test]
    public void ArithmeticAroundTheColumnIsUnaffectedTest()
    {
        var withoutIndex = Ids("SELECT Id FROM T WHERE V + 0 = -7");

        m_engine.Execute("CREATE INDEX IX_T_V ON T (V)");

        Assert.That(Ids("SELECT Id FROM T WHERE V + 0 = -7"), Is.EqualTo(withoutIndex));
        Assert.That(withoutIndex, Is.EqualTo(new[] { (long)ROWS + 1 }));
    }

    /// <summary>
    /// The plain predicate must still USE the index, or the fix has bought correctness by turning
    /// every indexed read into a scan.
    /// </summary>
    [Test]
    public void ThePlainPredicateStillUsesTheIndexTest()
    {
        m_engine.Execute("CREATE INDEX IX_T_V ON T (V)");

        var plan = string.Join("\n", m_engine.Query("EXPLAIN SELECT Id FROM T WHERE V = 70")
            .Select(row => row["detail"].AsString()));

        Assert.That(plan, Does.Contain("IX_T_V"),
            "an equality on the bare column is exactly what the index is for");

        Assert.That(Ids("SELECT Id FROM T WHERE V = 70"), Is.EqualTo(new[] { 7L }));
    }

    /// <summary>
    /// And the plan for the function predicate must not name the index at all: the answer being right
    /// is what matters, but a plan that still claims to seek the index would mean the fix landed
    /// somewhere else and this could come back.
    /// </summary>
    [Test]
    public void ThePlanDoesNotClaimToSeekTheIndexForAFunctionPredicateTest()
    {
        m_engine.Execute("CREATE INDEX IX_T_V ON T (V)");

        var plan = string.Join("\n", m_engine.Query("EXPLAIN SELECT Id FROM T WHERE ABS(V) = 7")
            .Select(row => row["detail"].AsString()));

        Assert.That(plan, Does.Not.Contain("IX_T_V"), plan);
    }

    /// <summary>
    /// An index BY the expression is a different question: it holds the wrapped values, so it may be
    /// used - and either way the answer must be the one the table gives.
    /// </summary>
    [Test]
    public void AnExpressionIndexDoesNotChangeTheAnswerEitherTest()
    {
        var withoutIndex = Ids("SELECT Id FROM T WHERE LOWER(S) = 'mixedcase'");

        m_engine.Execute("CREATE INDEX IX_T_LOWER ON T (LOWER(S))");

        Assert.That(Ids("SELECT Id FROM T WHERE LOWER(S) = 'mixedcase'"), Is.EqualTo(withoutIndex));
    }

    #endregion

    #region Helpers

    private long[] Ids(string sql)
    {
        return m_engine.Query(sql).Select(row => row["Id"].AsInt64()).OrderBy(id => id).ToArray();
    }

    #endregion
}
