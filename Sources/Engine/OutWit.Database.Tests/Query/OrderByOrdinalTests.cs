using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// <c>ORDER BY &lt;position&gt;</c> - an integer naming an output column - sorts by that column.
/// <c>Docs/KnownIssues.md</c> 16, fixed 2026-08-10.
/// </summary>
/// <remarks>
/// <para>
/// These cases PINNED the defect for one commit: the parser makes the integer an ordinary literal,
/// nothing turned it into a position, and <c>IteratorSort</c> evaluated it once per row - every row
/// answered the same number, every comparison was equal, and the sort was a no-op. The answer was
/// <i>exactly</i> what the query without any <c>ORDER BY</c> answers, and <c>ORDER BY 2 DESC</c> was
/// not a descending sort either.
/// </para>
/// <para>
/// <b>The reference behaviour was measured, not assumed.</b> Every expectation below was first run
/// through SQLite (in-memory, <c>Microsoft.Data.Sqlite</c>), because three of the corners are not
/// guessable: <c>ORDER BY 1 + 1</c> and <c>ORDER BY '1'</c> are constants rather than positions and
/// sort nothing, while <c>ORDER BY -1</c> IS a position and is refused as out of range. This engine
/// now answers all three the same way. See <c>use-sqlite-as-the-oracle</c>.
/// </para>
/// <para>
/// <b>There are two resolutions because the clause runs in two places</b>, and the fixture exercises
/// both: over a grouped, windowed or <c>VALUES</c> result the row already IS the output, so a
/// position is a column of it; for an ordinary query the sort runs BEFORE the projection, so a
/// position becomes the N-th select item's own expression.
/// </para>
/// <para>
/// <b>Both directions measured.</b> With the resolution removed altogether, 20 of these cases go red
/// and the three controls stay green. With the projected-row rule used for an ordinary query as
/// well - the plausible simplification - 13 go red, because there the sort still has the SOURCE row
/// in front of it and column one of that is <c>Id</c>, not the first selected column.
/// </para>
/// <para>
/// <b>And one case holds the seam with <c>KnownIssues</c> 15.</b> A grouped query carries its
/// grouping keys as extra trailing columns for <c>ORDER BY</c> and <c>HAVING</c> to reach, and a
/// POSITION must not be able to reach them - they are not columns the query returns. Counting the
/// grouped row instead of the select list reddens exactly
/// <c>SELECT COUNT(*) FROM G GROUP BY Kind ORDER BY 2</c>, and nothing else.
/// </para>
/// </remarks>
[TestFixture]
public class OrderByOrdinalTests
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
            "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Kind VARCHAR(20), Amount INT)");

        // Written in an order that is neither ascending nor descending in Kind or Amount, so an
        // answer that looks sorted cannot be the unsorted one. Id ascends with insertion, which is
        // what makes `SELECT * ... ORDER BY 1` a case that needs its own control.
        foreach (var (kind, amount) in new[] { ("c", 30), ("a", 10), ("b", 20), ("d", 40) })
            m_engine.Execute($"INSERT INTO T (Kind, Amount) VALUES ('{kind}', {amount})");

        // Counts differ per kind, so a grouped ORDER BY 2 is observable through the count alone.
        m_engine.Execute("CREATE TABLE G (Id BIGINT PRIMARY KEY AUTOINCREMENT, Kind VARCHAR(20))");

        foreach (var kind in new[] { "c", "a", "b", "c", "b", "c" })
            m_engine.Execute($"INSERT INTO G (Kind) VALUES ('{kind}')");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Functions

    private string Answer(string sql) =>
        string.Join("|", m_engine.Query(sql).Select(row =>
            string.Join(",", Enumerable.Range(0, row.ColumnCount).Select(i => row[i].ToString()))));

    #endregion

    #region Controls

    /// <summary>
    /// CONTROL: the fixture is not written in the order any of the cases below asks for, and naming
    /// the column sorts it. Without this, "the position sorts" would be equally consistent with rows
    /// that already happen to be in the right order.
    /// </summary>
    [Test]
    public void ControlTheFixtureIsUnsortedAndNamingTheColumnSortsItTest()
    {
        Assert.That(Answer("SELECT Kind FROM T"), Is.EqualTo("Text:c|Text:a|Text:b|Text:d"));
        Assert.That(Answer("SELECT Kind FROM T ORDER BY Kind"), Is.EqualTo("Text:a|Text:b|Text:c|Text:d"));
    }

    /// <summary>
    /// CONTROL, and the one that keeps the fix honest about what a position is NOT. SQLite answers
    /// both of these in insertion order - they are constants, not positions - and so does this
    /// engine. Had the rule been "any expression that evaluates to an integer", both would sort by
    /// column two and column one, and every other case here would pass just the same.
    /// </summary>
    [TestCase("SELECT Kind, Amount FROM T ORDER BY 1 + 1", TestName = "an arithmetic constant is not a position")]
    [TestCase("SELECT Kind, Amount FROM T ORDER BY '1'", TestName = "a string constant is not a position")]
    public void ControlAConstantThatIsNotABareIntegerSortsNothingTest(string sql)
    {
        Assert.That(Answer(sql), Is.EqualTo(Answer("SELECT Kind, Amount FROM T")));
    }

    #endregion

    #region A position over rows that are not projected yet

    [TestCase("SELECT Kind FROM T ORDER BY 1", "Text:a|Text:b|Text:c|Text:d")]
    [TestCase("SELECT Kind, Amount FROM T ORDER BY 2", "Text:a,Integer:10|Text:b,Integer:20|Text:c,Integer:30|Text:d,Integer:40")]
    [TestCase("SELECT Kind, Amount FROM T ORDER BY 2 DESC", "Text:d,Integer:40|Text:c,Integer:30|Text:b,Integer:20|Text:a,Integer:10")]
    [TestCase("SELECT Amount * 2 AS D FROM T ORDER BY 1", "Integer:20|Integer:40|Integer:60|Integer:80")]
    [TestCase("SELECT Kind AS K FROM T ORDER BY 1", "Text:a|Text:b|Text:c|Text:d")]
    [TestCase("SELECT DISTINCT Kind FROM T ORDER BY 1", "Text:a|Text:b|Text:c|Text:d")]
    [TestCase("SELECT Kind FROM T WHERE Amount > 15 ORDER BY 1", "Text:b|Text:c|Text:d")]
    [TestCase("SELECT X.Kind FROM (SELECT Kind FROM T) AS X ORDER BY 1", "Text:a|Text:b|Text:c|Text:d")]
    public void APositionNamesTheNthSelectItemTest(string sql, string expected)
    {
        Assert.That(Answer(sql), Is.EqualTo(expected));
    }

    /// <summary>
    /// The direction and the limit both belong to the position, which a case that only checks
    /// membership would not notice.
    /// </summary>
    [Test]
    public void TheDirectionAndTheLimitApplyToTheOrderedResultTest()
    {
        Assert.That(Answer("SELECT Kind FROM T ORDER BY 1 LIMIT 2"), Is.EqualTo("Text:a|Text:b"));
        Assert.That(Answer("SELECT Kind FROM T ORDER BY 1 DESC LIMIT 2"), Is.EqualTo("Text:d|Text:c"));
    }

    /// <summary>
    /// <c>SELECT *</c> is the one shape whose output columns are not its select list, so a position
    /// there counts the SOURCE's columns - and <c>_rowid</c>, which the engine carries on every
    /// scanned row and hides from a result, must not be one of them. That is what position 4 says.
    /// </summary>
    [Test]
    public void APositionOverSelectStarCountsTheVisibleColumnsTest()
    {
        // Id ascends with insertion, so position 1 leaves the rows where they were - which is why
        // position 3 is asserted beside it: it is the one that could not pass by accident.
        Assert.That(Answer("SELECT * FROM T ORDER BY 1"),
            Is.EqualTo("Integer:1,Text:c,Integer:30|Integer:2,Text:a,Integer:10"
                       + "|Integer:3,Text:b,Integer:20|Integer:4,Text:d,Integer:40"));

        Assert.That(Answer("SELECT * FROM T ORDER BY 3"),
            Is.EqualTo("Integer:2,Text:a,Integer:10|Integer:3,Text:b,Integer:20"
                       + "|Integer:1,Text:c,Integer:30|Integer:4,Text:d,Integer:40"));

        Assert.That(() => m_engine.Query("SELECT * FROM T ORDER BY 4"),
            Throws.InvalidOperationException.With.Message.Contains("3 column"),
            "the internal _rowid is on the row and is not an output column");
    }

    #endregion

    #region A position over rows that already ARE the output

    [TestCase("SELECT Kind, COUNT(*) FROM G GROUP BY Kind ORDER BY 2", "Text:a,Integer:1|Text:b,Integer:2|Text:c,Integer:3")]
    [TestCase("SELECT Kind, COUNT(*) FROM G GROUP BY Kind ORDER BY 2 DESC", "Text:c,Integer:3|Text:b,Integer:2|Text:a,Integer:1")]
    [TestCase("SELECT Kind, COUNT(*) FROM G GROUP BY Kind ORDER BY 1", "Text:a,Integer:1|Text:b,Integer:2|Text:c,Integer:3")]
    [TestCase("SELECT COUNT(*) FROM G GROUP BY Kind ORDER BY 1", "Integer:1|Integer:2|Integer:3")]
    public void APositionOverAGroupedResultNamesAColumnOfItTest(string sql, string expected)
    {
        Assert.That(Answer(sql), Is.EqualTo(expected));
    }

    /// <summary>
    /// A window result is projected by the window iterator, so a position counts its columns - and
    /// position 2 is the computed one, which no source row carries.
    /// </summary>
    [Test]
    public void APositionOverAWindowResultNamesAColumnOfItTest()
    {
        Assert.That(
            Answer("SELECT Kind, ROW_NUMBER() OVER (ORDER BY Amount) AS RN FROM T ORDER BY 1"),
            Is.EqualTo("Text:a,Integer:1|Text:b,Integer:2|Text:c,Integer:3|Text:d,Integer:4"));

        Assert.That(
            Answer("SELECT Kind, ROW_NUMBER() OVER (ORDER BY Amount) AS RN FROM T ORDER BY 2 DESC"),
            Is.EqualTo("Text:d,Integer:4|Text:c,Integer:3|Text:b,Integer:2|Text:a,Integer:1"));
    }

    #endregion

    #region A position that names nothing

    /// <summary>
    /// Out of range is refused rather than ignored, and the message says what the range is. SQLite
    /// refuses all three of these; this engine used to accept every one of them silently.
    /// </summary>
    [TestCase("SELECT Kind FROM T ORDER BY 0", "0")]
    [TestCase("SELECT Kind FROM T ORDER BY 99", "99")]
    [TestCase("SELECT Kind FROM T ORDER BY -1", "-1")]
    [TestCase("SELECT COUNT(*) FROM G GROUP BY Kind ORDER BY 2", "2")]
    public void APositionOutsideTheSelectListIsRefusedTest(string sql, string position)
    {
        Assert.That(() => m_engine.Query(sql),
            Throws.InvalidOperationException
                .With.Message.Contains($"position {position}")
                .And.Message.Contains("1 column"));
    }

    /// <summary>
    /// A star sharing its select list with other items has no column for a position to name, because
    /// this engine does not expand a star there at all - <c>Docs/KnownIssues.md</c> 17, where SQLite
    /// answers <c>SELECT *, Amount * 2 FROM T ORDER BY 4</c> correctly. The refusal names the reason
    /// rather than sorting by the NULL the star becomes.
    /// </summary>
    /// <remarks>
    /// <b>This case goes red when 17 is fixed</b>, and should then assert the sorted answer.
    /// </remarks>
    [Test]
    public void APositionOnAStarSharingItsSelectListIsRefusedTest()
    {
        Assert.That(() => m_engine.Query("SELECT *, Amount * 2 FROM T ORDER BY 1"),
            Throws.InvalidOperationException.With.Message.Contains("does not expand a star"));

        // And the position that IS resolvable in that list still resolves.
        Assert.That(Answer("SELECT *, Amount * 2 FROM T ORDER BY 2"),
            Is.EqualTo("NULL,Integer:20|NULL,Integer:40|NULL,Integer:60|NULL,Integer:80"));
    }

    #endregion
}
