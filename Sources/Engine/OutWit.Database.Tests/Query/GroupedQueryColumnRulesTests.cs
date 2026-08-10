using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// What a grouped query may name, and what a <c>*</c> stands for. <c>Docs/KnownIssues.md</c> 17,
/// fixed 2026-08-10.
/// </summary>
/// <remarks>
/// <para>
/// This fixture replaces <c>SelectStarOverAGroupedQueryTests</c>, which pinned the star half alone.
/// The star turned out to be the least likely way into a larger hole: a select item that is a
/// <c>*</c> carries no expression and one NULL was written for it, while a bare non-grouped COLUMN
/// was answered from an arbitrary row of the group and needed no star at all.
/// </para>
/// <para>
/// <b>Two changes, one of which was a decision.</b> Expanding a star needed no choosing - all three
/// reference databases do it. Refusing every column that is neither grouped nor aggregated is
/// PostgreSQL's and SQL Server's rule and was Dmitry's decision, taken with the cost measured first:
/// adopting it turned <b>one</b> test red across the engine, ADO.NET, EF, Studio and the 8,145-case
/// EF specification suite, and that one was the pin recording the defect.
/// </para>
/// <para>
/// <b>The strict form, deliberately.</b> PostgreSQL also accepts a column functionally dependent on
/// a grouped PRIMARY KEY - <c>SELECT * FROM T GROUP BY Id</c> - and SQL Server does not. The
/// stricter reading is implemented because widening it later cannot break a query that works today,
/// while narrowing it could; the case below pins that choice so it cannot drift unnoticed.
/// </para>
/// <para>
/// <b>Both halves measured separately.</b> With the refusal removed, 8 of these cases go red and
/// every control stays green. With the star expansion removed, 5 - three of them cases the refusal
/// alone would have left answering NULLs, which is why the two had to land together: a rule that
/// lets <c>SELECT * FROM T GROUP BY Id, Kind, Amount</c> through would otherwise have BLESSED a
/// wrong answer.
/// </para>
/// </remarks>
[TestFixture]
public class GroupedQueryColumnRulesTests
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

        // 'c' twice with DIFFERENT amounts, which is what makes "an arbitrary row's value" visible
        // rather than merely possible.
        foreach (var (kind, amount) in new[] { ("c", 30), ("a", 10), ("b", 20), ("c", 31) })
            m_engine.Execute($"INSERT INTO T (Kind, Amount) VALUES ('{kind}', {amount})");
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
    /// CONTROL: everything a grouped query is supposed to be able to name still works. Without this,
    /// "the wrong shapes are refused" would be equally true of a check that refuses everything.
    /// </summary>
    [TestCase("SELECT Kind, COUNT(*) FROM T GROUP BY Kind")]
    [TestCase("SELECT Kind, MAX(Amount) FROM T GROUP BY Kind")]
    [TestCase("SELECT Kind, Amount, COUNT(*) FROM T GROUP BY Kind, Amount")]
    [TestCase("SELECT UPPER(Kind), COUNT(*) FROM T GROUP BY Kind")]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY UPPER(Kind) HAVING UPPER(Kind) > 'A'")]
    [TestCase("SELECT COUNT(*) FROM T")]
    [TestCase("SELECT MAX(Amount) - MIN(Amount) FROM T")]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind")]
    [TestCase("SELECT COUNT(*) AS C FROM T GROUP BY Kind ORDER BY C")]
    [TestCase("SELECT Kind, COUNT(*) AS C FROM T GROUP BY Kind HAVING C > 1")]
    public void ControlAQueryThatNamesOnlyKeysAndAggregatesIsAcceptedTest(string sql)
    {
        Assert.That(() => m_engine.Query(sql), Throws.Nothing);
    }

    /// <summary>
    /// CONTROL, and the one that would have caught the first version of the check: <c>ORDER BY</c>
    /// and <c>HAVING</c> may name an output ALIAS, which is not a source column and is answered from
    /// the projected row. Measured - without the alias arm, four working cases went red.
    /// </summary>
    [Test]
    public void ControlAnOutputAliasIsNotASourceColumnTest()
    {
        Assert.That(
            Answer("SELECT Kind AS K, COUNT(*) AS C FROM T GROUP BY Kind ORDER BY K"),
            Is.EqualTo("Text:a,Integer:1|Text:b,Integer:1|Text:c,Integer:2"));

        Assert.That(
            Answer("SELECT Kind AS K, COUNT(*) AS C FROM T GROUP BY Kind HAVING C > 1"),
            Is.EqualTo("Text:c,Integer:2"));
    }

    /// <summary>
    /// CONTROL: a qualified name and a bare one are the same column. A check that refuses more than
    /// it understands turns a working query into an error, which is the one outcome worse than the
    /// defect it replaces - and a join is where that would happen first.
    /// </summary>
    [TestCase("SELECT T.Kind, COUNT(*) FROM T GROUP BY Kind", TestName = "qualified in SELECT, bare in GROUP BY")]
    [TestCase("SELECT Kind, COUNT(*) FROM T GROUP BY T.Kind", TestName = "bare in SELECT, qualified in GROUP BY")]
    [TestCase("SELECT T.Kind, COUNT(*) FROM T GROUP BY T.Kind", TestName = "qualified in both")]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY T.Kind ORDER BY Kind", TestName = "and in ORDER BY")]
    public void ControlAQualifiedColumnMatchesItsGroupingKeyTest(string sql)
    {
        Assert.That(() => m_engine.Query(sql), Throws.Nothing);
    }

    #endregion

    #region A star stands for the columns it names

    /// <summary>
    /// A star beside another select item is expanded. This had nothing to decide - all three
    /// reference databases do it - and it used to answer one NULL column where three belonged.
    /// </summary>
    [Test]
    public void AStarSharingItsSelectListIsExpandedTest()
    {
        var result = m_engine.Query("SELECT *, Amount * 2 FROM T");

        Assert.That(result.Count, Is.EqualTo(4));
        Assert.That(result.Select(row => row.ColumnCount), Is.All.EqualTo(4), "Id, Kind, Amount, and the double");

        Assert.That(Answer("SELECT *, Amount * 2 FROM T"),
            Is.EqualTo("Integer:1,Text:c,Integer:30,Integer:60"
                       + "|Integer:2,Text:a,Integer:10,Integer:20"
                       + "|Integer:3,Text:b,Integer:20,Integer:40"
                       + "|Integer:4,Text:c,Integer:31,Integer:62"));
    }

    /// <summary>
    /// And a star in a GROUPED query, where every column it stands for is grouped. This is the case
    /// the refusal alone would not have covered: the query is legal under the rule, so it has to
    /// answer with the values rather than be let through to the NULLs it used to give.
    /// </summary>
    [Test]
    public void AStarOverAFullyGroupedQueryAnswersWithValuesTest()
    {
        Assert.That(
            Answer("SELECT * FROM T GROUP BY Id, Kind, Amount ORDER BY Id"),
            Is.EqualTo("Integer:1,Text:c,Integer:30"
                       + "|Integer:2,Text:a,Integer:10"
                       + "|Integer:3,Text:b,Integer:20"
                       + "|Integer:4,Text:c,Integer:31"));
    }

    /// <summary>
    /// CONTROL: the plain <c>SELECT *</c> is untouched. It never reaches the expansion - the
    /// projection answers it directly - and this says the change did not cost the commonest query in
    /// the language a different plan.
    /// </summary>
    [Test]
    public void ControlAPlainSelectStarIsUnchangedTest()
    {
        Assert.That(Answer("SELECT * FROM T ORDER BY Id"),
            Is.EqualTo("Integer:1,Text:c,Integer:30"
                       + "|Integer:2,Text:a,Integer:10"
                       + "|Integer:3,Text:b,Integer:20"
                       + "|Integer:4,Text:c,Integer:31"));
    }

    #endregion

    #region A column no group can answer for is refused

    /// <summary>
    /// The rule, in the four shapes that reach it. Each of these used to answer with the value from
    /// an arbitrary row of the group, silently.
    /// </summary>
    [TestCase("SELECT * FROM T GROUP BY Kind", "Id", TestName = "a star over a partly grouped query")]
    [TestCase("SELECT Kind, Amount FROM T GROUP BY Kind", "Amount", TestName = "a bare column beside its key")]
    [TestCase("SELECT Amount + 1 FROM T GROUP BY Kind", "Amount", TestName = "a column inside an expression")]
    [TestCase("SELECT Kind, COUNT(*) FROM T", "Kind", TestName = "a bare column with no GROUP BY at all")]
    public void AColumnThatIsNeitherGroupedNorAggregatedIsRefusedTest(string sql, string column)
    {
        Assert.That(() => m_engine.Query(sql),
            Throws.InvalidOperationException
                .With.Message.Contains($"Column '{column}'")
                .And.Message.Contains("GROUP BY"));
    }

    /// <summary>
    /// The clauses either side of the select list obey the same rule, which is what makes it one
    /// rule rather than three.
    /// </summary>
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Amount")]
    [TestCase("SELECT COUNT(*) FROM T GROUP BY Kind HAVING Amount > 1")]
    public void TheRuleAppliesToOrderByAndHavingTooTest(string sql)
    {
        Assert.That(() => m_engine.Query(sql),
            Throws.InvalidOperationException.With.Message.Contains("Column 'Amount'"));
    }

    /// <summary>
    /// PINS A DECISION, not a defect. Grouping by the PRIMARY KEY does not make the table's other
    /// columns available: PostgreSQL allows this by functional dependency and SQL Server does not,
    /// and the strict reading is what was chosen - it can be widened later without breaking a query
    /// that works today, and narrowing it later could not.
    /// </summary>
    /// <remarks>
    /// If the functional-dependency exception is ever implemented, this case goes red and should be
    /// replaced by one asserting the full rows.
    /// </remarks>
    [Test]
    public void PinsTheStrictReadingGroupingByThePrimaryKeyIsNotEnoughTest()
    {
        Assert.That(() => m_engine.Query("SELECT * FROM T GROUP BY Id"),
            Throws.InvalidOperationException.With.Message.Contains("Column 'Kind'"),
            "PINS A DECISION: the strict SQL Server reading was chosen over PostgreSQL's "
            + "functional-dependency exception - invert if that is ever adopted");
    }

    /// <summary>
    /// The message names a column and says what to do about it. The old behaviour said nothing at
    /// all, which is the whole complaint; a refusal that does not name the column would be a poor
    /// replacement for a silent wrong answer.
    /// </summary>
    [Test]
    public void TheRefusalNamesTheColumnAndTheRemedyTest()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => m_engine.Query("SELECT Kind, Amount FROM T GROUP BY Kind"));

        Assert.That(refused!.Message, Is.EqualTo(
            "Column 'Amount' must appear in the GROUP BY clause or be used in an aggregate function."));
    }

    #endregion
}
