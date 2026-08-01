namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A pre-existing defect found by phase 3's oracle: an aggregate in a <c>HAVING</c> clause is only
/// recognised inside a plain comparison. Put it inside <c>BETWEEN</c> or <c>IN</c> and the query
/// throws.
/// </summary>
/// <remarks>
/// <para>
/// <c>HAVING COUNT(*) &gt; 1</c> works. <c>HAVING COUNT(*) BETWEEN 1 AND 5</c> and
/// <c>HAVING COUNT(*) IN (1, 2)</c> both raise
/// <c>InvalidOperationException: COUNT(*) should be handled by aggregation iterator</c> — the
/// aggregate reaches the row-level evaluator instead of being pre-computed by the aggregation
/// iterator, and the evaluator refuses it. SQLite answers all three.
/// </para>
/// <para>
/// <b>Not caused by the boolean-layer split, and not a grammar defect at all.</b> Verified by
/// execution against the parent commit <c>39d22e4</c> in a separate worktree: the same two shapes
/// fail identically there. <c>IN</c> failing is the giveaway — it was never part of the
/// <c>BETWEEN</c> precedence problem. The fault is in how the aggregation iterator collects
/// aggregates from a <c>HAVING</c> condition: it looks inside comparison operands and stops there.
/// </para>
/// <para>
/// Recorded rather than fixed, because phase 3 is the grammar. Found only because the oracle sweep
/// compares <b>answers</b> and not just whether a shape parses — both of these parse fine.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class HavingAggregateFindingsTests : WitSqlEngineTestsBase
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

        m_engine.Execute("INSERT INTO People (Id, Name, Age, Flag) VALUES (1, 'alice', 30, 1)");
        m_engine.Execute("INSERT INTO People (Id, Name, Age, Flag) VALUES (2, 'bob', 10, 1)");
        m_engine.Execute("INSERT INTO People (Id, Name, Age, Flag) VALUES (3, 'anna', 40, 0)");
    }

    #endregion

    #region The control - an aggregate in a plain comparison

    [Test]
    public void AggregateInAComparisonWorksTest()
    {
        // The control. If this ever fails, the two findings below are describing something else.
        var flags = Select("SELECT Flag FROM People GROUP BY Flag HAVING COUNT(*) > 1");

        Assert.That(flags, Is.EqualTo(new long[] { 1 }));
    }

    #endregion

    #region The findings

    [Test]
    public void AggregateInsideBetweenWorksTest()
    {
        var flags = Select("SELECT Flag FROM People GROUP BY Flag HAVING COUNT(*) BETWEEN 1 AND 5");

        Assert.That(flags, Is.EqualTo(new long[] { 0, 1 }),
            "both groups have a count within 1..5, which is what SQLite returns");
    }

    [Test]
    public void AggregateInsideInWorksTest()
    {
        var flags = Select("SELECT Flag FROM People GROUP BY Flag HAVING COUNT(*) IN (1, 2)");

        Assert.That(flags, Is.EqualTo(new long[] { 0, 1 }));
    }

    #endregion

    #region Helper Methods

    private long[] Select(string sql) =>
        m_engine.Query(sql).Select(row => row[0].AsInt64()).OrderBy(value => value).ToArray();

    #endregion

    #region Detection - an aggregate query with no GROUP BY to announce it

    /// <summary>
    /// An aggregate inside <c>BETWEEN</c> in the <b>select list</b>, with no <c>GROUP BY</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This covers the other half of the fix, and it exists because a revert test showed the half
    /// was uncovered: with the detector put back to its old top-level-only form, the three tests
    /// above stayed green. They all write <c>GROUP BY</c> explicitly, and that alone routes the
    /// query to the aggregation iterator - so they never asked the detector anything.
    /// </para>
    /// <para>
    /// Here there is no <c>GROUP BY</c>, so whether this is an aggregate query at all is decided by
    /// looking into the expression. A detector that stops at the top level sees a <c>BETWEEN</c>,
    /// not an aggregate, and plans a row-by-row query over an aggregate.
    /// </para>
    /// </remarks>
    [Test]
    public void AggregateInsideBetweenIsDetectedWithoutGroupByTest()
    {
        var rows = m_engine.Query("SELECT MAX(Age) BETWEEN 1 AND 200 AS InRange FROM People");

        Assert.That(rows, Has.Count.EqualTo(1), "an aggregate query without GROUP BY returns one row");
        Assert.That(rows[0][0].AsBool(), Is.True);
    }

    #endregion
}
