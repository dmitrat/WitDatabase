using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Regression tests for operator precedence, executed rather than asserted on the parse tree.
/// </summary>
/// <remarks>
/// ANTLR compiles an <i>interior</i> recursive reference - one that is neither first nor last in its
/// alternative - as <c>expression(0)</c>, i.e. full precedence, so it swallows everything that
/// follows. <c>LIKE</c> had that shape because of its trailing optional <c>ESCAPE</c> block, and the
/// result was silent: <c>WHERE Name LIKE 'a%' AND Age &gt; 18</c> parsed as
/// <c>Name LIKE ('a%' AND Age &gt; 18)</c> and matched nothing, while
/// <c>DELETE ... WHERE Name NOT LIKE 'p' AND Id = 5</c> deleted every row in the table. Prefix
/// <c>NOT</c> sat above every comparison, so <c>NOT Age &gt; 18</c> meant <c>(NOT Age) &gt; 18</c>.
///
/// None of the 3,700 tests in this solution exercised either shape.
/// </remarks>
[TestFixture]
public sealed class WitSqlEnginePrecedenceTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute(@"
            CREATE TABLE People (
                Id INT PRIMARY KEY,
                Name VARCHAR(50) NOT NULL,
                Age INT NOT NULL,
                Flag INT NOT NULL
            )");

        Insert(1, "alice", 30, 1);
        Insert(2, "bob", 10, 1);
        Insert(3, "anna", 40, 0);
    }

    #endregion

    #region LIKE

    [Test]
    public void LikeDoesNotSwallowTheFollowingConjunctTest()
    {
        var ids = SelectIds("SELECT Id FROM People WHERE Name LIKE 'a%' AND Age > 18 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 3 }));
    }

    [Test]
    public void LikeDoesNotSwallowTheFollowingDisjunctTest()
    {
        var ids = SelectIds("SELECT Id FROM People WHERE Name LIKE 'zz%' OR Age = 10 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 2 }));
    }

    [Test]
    public void NotLikeInADeleteRemovesOnlyTheMatchingRowTest()
    {
        // The data-destroying shape: the pattern used to absorb `AND Id = 5`, so the predicate
        // collapsed to a bare NOT LIKE and the statement emptied the table.
        m_engine.Execute("DELETE FROM People WHERE Name NOT LIKE 'zzz%' AND Id = 2");

        Assert.That(SelectIds("SELECT Id FROM People ORDER BY Id"), Is.EqualTo(new long[] { 1, 3 }));
    }

    [Test]
    public void LikeWithEscapeDoesNotSwallowTheFollowingConjunctTest()
    {
        var ids = SelectIds(
            @"SELECT Id FROM People WHERE Name LIKE 'a%' ESCAPE '\' AND Age > 18 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 3 }));
    }

    [Test]
    public void LikeStillHonoursItsEscapeCharacterTest()
    {
        m_engine.Execute("INSERT INTO People (Id, Name, Age, Flag) VALUES (4, '100%', 20, 0)");

        var ids = SelectIds(@"SELECT Id FROM People WHERE Name LIKE '100\%' ESCAPE '\'");

        Assert.That(ids, Is.EqualTo(new long[] { 4 }),
            "Splitting the LIKE alternatives must not break the ESCAPE semantics");
    }

    #endregion

    #region NOT

    [Test]
    public void NotAppliesToTheWholeComparisonTest()
    {
        var ids = SelectIds("SELECT Id FROM People WHERE NOT Age > 18 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 2 }));
    }

    [Test]
    public void NotBindsTighterThanAndTest()
    {
        // NOT (Age > 18) AND Flag = 1  ->  only bob
        var ids = SelectIds("SELECT Id FROM People WHERE NOT Age > 18 AND Flag = 1 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 2 }));
    }

    [Test]
    public void NotAppliesToAnEqualityTest()
    {
        var ids = SelectIds("SELECT Id FROM People WHERE NOT Name = 'alice' ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 2, 3 }));
    }

    #endregion

    #region Unaffected Shapes

    [Test]
    public void GlobDoesNotSwallowTheFollowingConjunctTest()
    {
        var ids = SelectIds("SELECT Id FROM People WHERE Name GLOB 'a*' AND Age > 18 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 3 }));
    }

    [Test]
    public void InDoesNotSwallowTheFollowingConjunctTest()
    {
        var ids = SelectIds("SELECT Id FROM People WHERE Id IN (1, 2) AND Flag = 1 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public void AndBindsTighterThanOrTest()
    {
        var ids = SelectIds(
            "SELECT Id FROM People WHERE Age > 35 AND Flag = 0 OR Id = 2 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 2, 3 }));
    }

    [Test]
    public void UnaryMinusStillBindsTighterThanComparisonTest()
    {
        var ids = SelectIds("SELECT Id FROM People WHERE Age > -1 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2, 3 }),
            "Removing NOT from unaryExpr must not disturb the arithmetic unary operators");
    }

    #endregion

    #region Known Broken

    [Test]
    [Ignore("BETWEEN's lower bound is an interior recursive reference, so it is parsed at " +
            "precedence 0 and absorbs the following AND conjunct: 'Age BETWEEN 1 AND 10 AND Flag = 1' " +
            "becomes Between(Age, (1 AND 10), Flag = 1). Unlike LIKE this cannot be fixed by " +
            "reordering the alternative - the AND keyword sits structurally in the middle - so it " +
            "needs the boolean layer split out of `expression` into searchCondition/predicate/" +
            "valueExpression rules. Tracked in Docs/AUDIT-2026-07.md section 4.2.")]
    public void BetweenDoesNotSwallowTheFollowingConjunctTest()
    {
        var ids = SelectIds(
            "SELECT Id FROM People WHERE Age BETWEEN 1 AND 35 AND Flag = 1 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    [Ignore("Same interior-recursive-reference defect as BetweenDoesNotSwallowTheFollowingConjunctTest.")]
    public void NotBetweenDoesNotSwallowTheFollowingConjunctTest()
    {
        var ids = SelectIds(
            "SELECT Id FROM People WHERE Age NOT BETWEEN 1 AND 20 AND Flag = 0 ORDER BY Id");

        Assert.That(ids, Is.EqualTo(new long[] { 3 }));
    }

    #endregion

    #region Helper Methods

    private void Insert(int id, string name, int age, int flag)
    {
        m_engine.Execute(
            $"INSERT INTO People (Id, Name, Age, Flag) VALUES ({id}, '{name}', {age}, {flag})");
    }

    private long[] SelectIds(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0].AsInt64()).ToArray();
    }

    #endregion
}
